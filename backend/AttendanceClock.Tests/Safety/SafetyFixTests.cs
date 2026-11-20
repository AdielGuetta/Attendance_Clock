using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceClock.Api.Admin;
using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Punches;
using AttendanceClock.Api.Requests;
using AttendanceClock.Tests.Punches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceClock.Tests.Safety;

/// <summary>Plan 14 test table: fixes from the edge-case review (cases 1, 3 and 7).</summary>
[Collection(DatabaseCollection.Name)]
public class SafetyFixTests(PunchFixture fixture) : IClassFixture<PunchFixture>
{
    private const string Reason = "Confirmed with the team lead";

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task Deactivate(int employeeId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Employees.Where(e => e.Id == employeeId).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, false));
    }

    private static Task<HttpResponseMessage> ClockIn(HttpClient c) => c.PostAsJsonAsync("/api/punches/clock-in", new { location = "Office" });

    // ── Case 1: an old tab must not close a newer shift ─────────────────────────────────────────────

    [Fact] // 1
    public async Task Clock_out_from_an_old_tab_doesnt_close_the_newer_shift()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        fixture.Clock.SetNow("2026-11-20T08:00:00+01:00");
        var first = (await (await ClockIn(client)).Content.ReadFromJsonAsync<ShiftDto>())!;
        fixture.Clock.SetNow("2026-11-20T12:00:00+01:00");
        await client.PostAsJsonAsync("/api/punches/clock-out", new { shiftId = first.Id });   // e.g. on the phone
        fixture.Clock.SetNow("2026-11-20T13:00:00+01:00");
        var second = (await (await ClockIn(client)).Content.ReadFromJsonAsync<ShiftDto>())!;

        // The laptop tab still shows the FIRST shift and its Clock Out is pressed at 14:00.
        fixture.Clock.SetNow("2026-11-20T14:00:00+01:00");
        var stale = await client.PostAsJsonAsync("/api/punches/clock-out", new { shiftId = first.Id });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("SHIFT_CHANGED", await ErrorCode(stale));
        var status = await client.GetFromJsonAsync<PunchStatusDto>("/api/punches/status");
        Assert.Equal(second.Id, status!.OpenShift!.Id); // the newer shift is still open, untouched
    }

    [Fact] // 2
    public async Task Clock_out_naming_the_open_shift_works()
    {
        var (client, _) = await fixture.NewEmployeeClientAsync();
        fixture.Clock.SetNow("2026-11-20T08:00:00+01:00");
        var shift = (await (await ClockIn(client)).Content.ReadFromJsonAsync<ShiftDto>())!;
        fixture.Clock.SetNow("2026-11-20T16:00:00+01:00");

        var response = await client.PostAsJsonAsync("/api/punches/clock-out", new { shiftId = shift.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(480, (await response.Content.ReadFromJsonAsync<ShiftDto>())!.DurationMinutes);
    }

    // ── Case 3: two changes for the same employee at the same moment ────────────────────────────────

    [Fact] // 3
    public async Task Two_managers_correcting_two_shifts_at_once_never_create_an_overlap()
    {
        // Each correction is fine alone; together they'd overlap (A 08–13 and B 12–16). Without the employee lock, both
        // could pass their overlap check before the other saves. Repeated, because races only show up sometimes.
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00");
        var managerA = (await fixture.NewEmployeeClientAsync(EmployeeRole.Manager)).Client;
        var managerB = (await fixture.NewEmployeeClientAsync(EmployeeRole.Manager)).Client;

        for (var round = 0; round < 15; round++)
        {
            var (_, emp) = await fixture.NewEmployeeClientAsync();
            var a = await fixture.AddShiftAsync(emp, "2026-11-18T08:00:00+01:00", "2026-11-18T10:00:00+01:00");
            var b = await fixture.AddShiftAsync(emp, "2026-11-18T14:00:00+01:00", "2026-11-18T16:00:00+01:00");
            var shifts = (await managerA.GetFromJsonAsync<List<AdminShiftDto>>("/api/admin/shifts?from=2026-11-18&to=2026-11-18"))!;
            string Version(long id) => shifts.Single(s => s.Id == id).RowVersion;

            var results = await Task.WhenAll(
                managerA.PatchAsJsonAsync($"/api/admin/shifts/{a}", new { clockOut = "2026-11-18T13:00", reason = Reason, rowVersion = Version(a) }),
                managerB.PatchAsJsonAsync($"/api/admin/shifts/{b}", new { clockIn = "2026-11-18T12:00", reason = Reason, rowVersion = Version(b) }));

            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            var refused = Assert.Single(results, r => r.StatusCode != HttpStatusCode.OK);
            Assert.Equal("OVERLAPS_ANOTHER_SHIFT", await ErrorCode(refused));
        }
    }

    // ── Case 7: a deactivated account stops working at once, everywhere ─────────────────────────────

    [Fact] // 4
    public async Task A_deactivated_employee_is_refused_on_every_endpoint()
    {
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00");
        var (client, id) = await fixture.NewEmployeeClientAsync();
        var shiftId = await fixture.AddShiftAsync(id, "2026-11-18T08:00:00+01:00", "2026-11-18T16:00:00+01:00");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/shifts/mine")).StatusCode); // works while active

        await Deactivate(id); // their login badge is still valid for up to 60 minutes

        var calls = new[]
        {
            await client.GetAsync("/api/shifts/mine"),
            await client.GetAsync("/api/punches/status"),
            await ClockIn(client),
            await client.PostAsJsonAsync($"/api/shifts/{shiftId}/correction-requests", new { clockOut = "2026-11-18T15:00", reason = Reason }),
            await client.GetAsync("/api/correction-requests/mine"),
        };
        foreach (var response in calls)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("ACCOUNT_INACTIVE", await ErrorCode(response));
        }
    }

    [Fact] // 5
    public async Task A_deactivated_manager_cant_correct_or_approve()
    {
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00");
        var (employee, emp) = await fixture.NewEmployeeClientAsync();
        var (manager, managerId) = await fixture.NewEmployeeClientAsync(EmployeeRole.Manager);
        var shiftId = await fixture.AddShiftAsync(emp, "2026-11-18T08:00:00+01:00", "2026-11-18T16:00:00+01:00");
        var request = (await (await employee.PostAsJsonAsync($"/api/shifts/{shiftId}/correction-requests",
            new { clockOut = "2026-11-18T15:00", reason = Reason })).Content.ReadFromJsonAsync<CorrectionRequestDto>())!;
        var version = (await manager.GetFromJsonAsync<List<AdminShiftDto>>("/api/admin/shifts?from=2026-11-18&to=2026-11-18"))!
            .Single(s => s.Id == shiftId).RowVersion;

        await Deactivate(managerId);

        var calls = new[]
        {
            await manager.PatchAsJsonAsync($"/api/admin/shifts/{shiftId}", new { clockOut = "2026-11-18T15:30", reason = Reason, rowVersion = version }),
            await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/approve", null),
            await manager.GetAsync("/api/admin/correction-requests"),
        };
        foreach (var response in calls)
            Assert.Equal("ACCOUNT_INACTIVE", await ErrorCode(response));

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T16:00:00+01:00"), (await db.Shifts.FindAsync(shiftId))!.ClockOut); // unchanged
    }
}

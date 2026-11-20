using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceClock.Api.Admin;
using AttendanceClock.Api.Domain;
using AttendanceClock.Tests.Punches;

namespace AttendanceClock.Tests.Admin;

/// <summary>Plan 10 test table: manager views and shift corrections.</summary>
[Collection(DatabaseCollection.Name)]
public class AdminTests(PunchFixture fixture) : IClassFixture<PunchFixture>
{
    private const string Reason = "Forgot to clock out, confirmed with team lead";

    private async Task<HttpClient> Manager()
    {
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00"); // "now" for future checks and flags
        return (await fixture.NewEmployeeClientAsync(EmployeeRole.Manager)).Client;
    }

    private static async Task<AdminShiftDto> GetShift(HttpClient manager, long id, string from = "2026-11-01", string to = "2026-11-30")
    {
        var shifts = await manager.GetFromJsonAsync<List<AdminShiftDto>>($"/api/admin/shifts?from={from}&to={to}");
        return shifts!.Single(s => s.Id == id);
    }

    private static Task<HttpResponseMessage> Correct(HttpClient manager, long id, string? clockIn, string? clockOut, string? reason, string rowVersion) =>
        manager.PatchAsJsonAsync($"/api/admin/shifts/{id}", new { clockIn, clockOut, reason, rowVersion });

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact] // 1
    public async Task Employees_cannot_use_manager_endpoints()
    {
        var (employee, _) = await fixture.NewEmployeeClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/on-shift")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/shifts?from=2026-11-16&to=2026-11-22")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PatchAsJsonAsync("/api/admin/shifts/1", new { reason = "x" })).StatusCode);
    }

    [Fact] // 2 (F5, F7)
    public async Task On_shift_list_shows_open_shifts_and_flags_forgotten_ones()
    {
        var manager = await Manager();
        var (_, fresh) = await fixture.NewEmployeeClientAsync();
        var (_, forgot) = await fixture.NewEmployeeClientAsync();
        var freshShift = await fixture.AddShiftAsync(fresh, "2026-11-20T09:00:00+01:00");   // open 9 h
        var forgotShift = await fixture.AddShiftAsync(forgot, "2026-11-19T08:00:00+01:00"); // open 34 h

        var list = await manager.GetFromJsonAsync<OnShiftDto>("/api/admin/on-shift");

        Assert.DoesNotContain("ForgotClockOut", list!.Shifts.Single(s => s.Id == freshShift).Flags);
        Assert.Contains("ForgotClockOut", list.Shifts.Single(s => s.Id == forgotShift).Flags);
        Assert.Equal("Test Employee", list.Shifts.Single(s => s.Id == forgotShift).EmployeeName);
    }

    [Fact] // 3
    public async Task Shift_list_filters_by_employee_and_flagged_only()
    {
        var manager = await Manager();
        var (_, a) = await fixture.NewEmployeeClientAsync();
        var (_, b) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(a, "2026-11-10T08:00:00+01:00", "2026-11-10T16:00:00+01:00");
        var longShift = await fixture.AddShiftAsync(b, "2026-11-10T06:00:00+01:00", "2026-11-10T21:30:00+01:00");
        // Make b's shift flagged the normal way: a correction (also marks it TooLong, 15.5 h).
        var dto = await GetShift(manager, longShift, "2026-11-10", "2026-11-10");
        await Correct(manager, longShift, null, "2026-11-10T21:00", Reason, dto.RowVersion);

        var onlyA = await manager.GetFromJsonAsync<List<AdminShiftDto>>($"/api/admin/shifts?from=2026-11-10&to=2026-11-10&employeeId={a}");
        var flagged = await manager.GetFromJsonAsync<List<AdminShiftDto>>("/api/admin/shifts?from=2026-11-10&to=2026-11-10&flaggedOnly=true");

        Assert.All(onlyA!, s => Assert.Equal(a, s.EmployeeId));
        Assert.Contains(flagged!, s => s.Id == longShift);
        Assert.DoesNotContain(flagged!, s => s.EmployeeId == a);
    }

    [Fact] // 4 (F6)
    public async Task Correcting_a_clock_out_is_labelled_flagged_and_audited()
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-18T08:00:00+01:00", "2026-11-18T19:00:00+01:00");
        var before = await GetShift(manager, id);

        var response = await Correct(manager, id, null, "2026-11-18T17:00", Reason, before.RowVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await response.Content.ReadFromJsonAsync<AdminShiftDto>();
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T17:00:00+01:00"), after!.ClockOut);
        Assert.Equal("ManualCorrection", after.ClockOutSource);
        Assert.Equal("test-data", after.ClockInSource); // untouched field keeps its source
        Assert.Contains("Corrected", after.Flags);
        Assert.Equal(540, after.DurationMinutes);
        Assert.NotEqual(before.RowVersion, after.RowVersion);

        var audit = await manager.GetFromJsonAsync<List<ShiftAuditDto>>($"/api/admin/shifts/{id}/audit");
        var row = Assert.Single(audit!);
        Assert.Equal("ClockOut", row.Field);
        Assert.StartsWith("2026-11-18T19:00:00", row.OldValue);
        Assert.StartsWith("2026-11-18T17:00:00", row.NewValue);
        Assert.Equal(Reason, row.Reason);
        Assert.Equal("Test Manager", row.ChangedBy);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T18:00:00+01:00"), row.ChangedAt); // stamped by the trusted clock
    }

    [Fact] // 5 (EC-4)
    public async Task Closing_a_forgotten_shift_lets_the_employee_clock_in_again()
    {
        var manager = await Manager();
        var (empClient, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-19T08:00:00+01:00"); // never clocked out
        var dto = await GetShift(manager, id);

        var response = await Correct(manager, id, null, "2026-11-19T17:00", Reason, dto.RowVersion);
        var clockIn = await empClient.PostAsJsonAsync("/api/punches/clock-in", new { location = "Office" }); // fake clock: 2026-11-20 18:00

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, clockIn.StatusCode);
        var audit = await manager.GetFromJsonAsync<List<ShiftAuditDto>>($"/api/admin/shifts/{id}/audit");
        Assert.Equal("(open)", Assert.Single(audit!).OldValue);
    }

    [Theory] // 6
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oops")]
    public async Task A_reason_is_required(string? reason)
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-18T08:00:00+01:00", "2026-11-18T19:00:00+01:00");
        var dto = await GetShift(manager, id);

        var response = await Correct(manager, id, null, "2026-11-18T17:00", reason, dto.RowVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("REASON_REQUIRED", await ErrorCode(response));
    }

    [Fact] // 7
    public async Task Clock_out_must_stay_after_clock_in()
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-18T08:00:00+01:00", "2026-11-18T16:00:00+01:00");
        var dto = await GetShift(manager, id);

        var response = await Correct(manager, id, null, "2026-11-18T07:30", Reason, dto.RowVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CLOCK_OUT_NOT_AFTER_CLOCK_IN", await ErrorCode(response));
    }

    [Fact] // 8 (EC-13)
    public async Task Times_in_the_future_are_refused()
    {
        var manager = await Manager(); // now = 2026-11-20 18:00
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-20T08:00:00+01:00");
        var dto = await GetShift(manager, id);

        var response = await Correct(manager, id, null, "2026-11-20T19:00", Reason, dto.RowVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("TIME_IN_FUTURE", await ErrorCode(response));
    }

    [Fact] // 9 (EC-13)
    public async Task A_correction_cannot_overlap_another_shift()
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var first = await fixture.AddShiftAsync(emp, "2026-11-17T08:00:00+01:00", "2026-11-17T12:00:00+01:00");
        await fixture.AddShiftAsync(emp, "2026-11-17T13:00:00+01:00", "2026-11-17T17:00:00+01:00");
        var dto = await GetShift(manager, first);

        var response = await Correct(manager, first, null, "2026-11-17T14:00", Reason, dto.RowVersion); // runs into the 13:00 shift

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("OVERLAPS_ANOTHER_SHIFT", await ErrorCode(response));
    }

    [Fact] // 10 (optimistic concurrency)
    public async Task A_stale_version_stamp_is_refused()
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-18T08:00:00+01:00", "2026-11-18T19:00:00+01:00");
        var opened = await GetShift(manager, id);
        await Correct(manager, id, null, "2026-11-18T18:00", Reason, opened.RowVersion); // someone else saves first

        var response = await Correct(manager, id, null, "2026-11-18T17:00", Reason, opened.RowVersion); // old stamp

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SHIFT_MODIFIED", await ErrorCode(response));
    }

    [Fact] // 11 (DST)
    public async Task Ambiguous_fall_back_time_needs_an_offset()
    {
        // 25 Oct 2026: clocks go 03:00 → 02:00, so 02:30 happens twice.
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-10-24T22:00:00+02:00", "2026-10-25T06:00:00+01:00");
        var dto = await GetShift(manager, id, "2026-10-24", "2026-10-24");

        var ambiguous = await Correct(manager, id, null, "2026-10-25T02:30", Reason, dto.RowVersion);
        var explicitSecond = await Correct(manager, id, null, "2026-10-25T02:30+01:00", Reason, dto.RowVersion);

        Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        Assert.Equal("AMBIGUOUS_TIME", await ErrorCode(ambiguous));
        Assert.Equal(HttpStatusCode.OK, explicitSecond.StatusCode);
        var after = await explicitSecond.Content.ReadFromJsonAsync<AdminShiftDto>();
        Assert.Equal(330, after!.DurationMinutes); // 22:00 CEST → 02:30 CET = 5.5 real hours
    }

    [Fact] // 11b (DST)
    public async Task Non_existent_spring_forward_time_is_refused()
    {
        // 29 Mar 2026: clocks jump 02:00 → 03:00, so 02:30 never happens.
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-03-28T22:00:00+01:00", "2026-03-29T06:00:00+02:00");
        var dto = await GetShift(manager, id, "2026-03-28", "2026-03-28");

        var response = await Correct(manager, id, null, "2026-03-29T02:30", Reason, dto.RowVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_LOCAL_TIME", await ErrorCode(response));
    }

    [Fact] // 12
    public async Task Audit_history_lists_every_change_in_order()
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var id = await fixture.AddShiftAsync(emp, "2026-11-16T08:00:00+01:00", "2026-11-16T16:00:00+01:00");

        var v1 = (await GetShift(manager, id)).RowVersion;
        await Correct(manager, id, "2026-11-16T07:45", "2026-11-16T16:15", "Badge reader was broken", v1); // 2 fields
        var v2 = (await GetShift(manager, id)).RowVersion;
        await Correct(manager, id, null, "2026-11-16T16:30", "Stayed for a delivery", v2);

        var audit = (await manager.GetFromJsonAsync<List<ShiftAuditDto>>($"/api/admin/shifts/{id}/audit"))!;

        Assert.Equal(["ClockIn", "ClockOut", "ClockOut"], audit.Select(a => a.Field));
        Assert.Equal(["Badge reader was broken", "Badge reader was broken", "Stayed for a delivery"], audit.Select(a => a.Reason));
    }

    [Fact] // 13 (Plan 12)
    public async Task Manager_sees_each_shifts_work_location_and_none_for_old_shifts()
    {
        var manager = await Manager();
        var (_, emp) = await fixture.NewEmployeeClientAsync();
        var remote = await fixture.AddShiftAsync(emp, "2026-11-17T08:00:00+01:00", "2026-11-17T16:00:00+01:00", WorkLocation.Remote);
        var old = await fixture.AddShiftAsync(emp, "2026-11-16T08:00:00+01:00", "2026-11-16T16:00:00+01:00"); // before Plan 12

        Assert.Equal("Remote", (await GetShift(manager, remote)).WorkLocation);
        Assert.Null((await GetShift(manager, old)).WorkLocation);

        // A correction changes the times, never the location.
        var dto = await GetShift(manager, remote);
        await Correct(manager, remote, null, "2026-11-17T15:00", Reason, dto.RowVersion);
        Assert.Equal("Remote", (await GetShift(manager, remote)).WorkLocation);
    }
}

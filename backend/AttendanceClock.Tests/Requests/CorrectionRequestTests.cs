using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AttendanceClock.Api.Admin;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Requests;
using AttendanceClock.Tests.Punches;

namespace AttendanceClock.Tests.Requests;

/// <summary>Plan 13 test table: employee correction requests, manager approve / reject.</summary>
[Collection(DatabaseCollection.Name)]
public class CorrectionRequestTests(PunchFixture fixture) : IClassFixture<PunchFixture>
{
    private const string Reason = "Forgot to clock out, left at 17:00";

    // A finished shift on Wed 18 Nov, 08:00 → 19:00 (Zurich). "Now" is Fri 20 Nov, 18:00.
    private async Task<(HttpClient Employee, int EmployeeId, long ShiftId)> EmployeeWithFinishedShift()
    {
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00");
        var (client, id) = await fixture.NewEmployeeClientAsync();
        var shiftId = await fixture.AddShiftAsync(id, "2026-11-18T08:00:00+01:00", "2026-11-18T19:00:00+01:00");
        return (client, id, shiftId);
    }

    private async Task<HttpClient> Manager() => (await fixture.NewEmployeeClientAsync(EmployeeRole.Manager)).Client;

    private static Task<HttpResponseMessage> Ask(HttpClient c, long shiftId, string? clockIn, string? clockOut, string? reason = Reason) =>
        c.PostAsJsonAsync($"/api/shifts/{shiftId}/correction-requests", new { clockIn, clockOut, reason });

    private static async Task<CorrectionRequestDto> Read(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<CorrectionRequestDto>())!;

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<AdminShiftDto> Shift(HttpClient manager, long id) =>
        (await manager.GetFromJsonAsync<List<AdminShiftDto>>("/api/admin/shifts?from=2026-11-16&to=2026-11-22"))!.Single(s => s.Id == id);

    private static async Task<List<ShiftAuditDto>> Audit(HttpClient manager, long id) =>
        (await manager.GetFromJsonAsync<List<ShiftAuditDto>>($"/api/admin/shifts/{id}/audit"))!;

    [Fact] // 1
    public async Task Request_on_my_finished_shift_is_pending_and_changes_nothing_yet()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        var manager = await Manager();

        var response = await Ask(employee, shiftId, null, "2026-11-18T17:00");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var request = await Read(response);
        Assert.Equal("Pending", request.Status);
        Assert.Null(request.RequestedClockIn); // only what changed is stored
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T17:00:00+01:00"), request.RequestedClockOut);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T19:00:00+01:00"), (await Shift(manager, shiftId)).ClockOut); // untouched
        var mine = await employee.GetFromJsonAsync<List<CorrectionRequestDto>>("/api/correction-requests/mine");
        Assert.Equal(request.Id, Assert.Single(mine!).Id);
    }

    [Fact] // 2
    public async Task Someone_elses_shift_is_not_found()
    {
        var (_, _, shiftId) = await EmployeeWithFinishedShift();
        var (stranger, _) = await fixture.NewEmployeeClientAsync();

        var response = await Ask(stranger, shiftId, null, "2026-11-18T17:00");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // 3 (user's decision: only finished shifts)
    public async Task An_open_shift_cant_be_requested()
    {
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00");
        var (employee, id) = await fixture.NewEmployeeClientAsync();
        var openShift = await fixture.AddShiftAsync(id, "2026-11-20T08:00:00+01:00");

        var response = await Ask(employee, openShift, "2026-11-20T07:30", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SHIFT_STILL_OPEN", await ErrorCode(response));
    }

    [Theory] // 4
    [InlineData(null, "2026-11-18T17:00", "", "REASON_REQUIRED")]
    [InlineData(null, "2026-11-21T09:00", Reason, "TIME_IN_FUTURE")]
    [InlineData(null, "2026-11-18T07:00", Reason, "CLOCK_OUT_NOT_AFTER_CLOCK_IN")]
    [InlineData("2026-11-18T08:00", "2026-11-18T19:00", Reason, "NOTHING_TO_CHANGE")] // same as recorded
    [InlineData(null, null, Reason, "NOTHING_TO_CHANGE")]
    [InlineData(null, "yesterday at five", Reason, "INVALID_TIME_FORMAT")]
    public async Task Bad_requests_are_refused_with_a_clear_code(string? clockIn, string? clockOut, string reason, string code)
    {
        var (employee, id, shiftId) = await EmployeeWithFinishedShift();

        var response = await Ask(employee, shiftId, clockIn, clockOut, reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await ErrorCode(response));
    }

    [Fact] // 5
    public async Task A_too_long_reason_is_refused()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();

        var response = await Ask(employee, shiftId, null, "2026-11-18T17:00", new string('x', CorrectionRequestService.MaxReasonLength + 1));

        Assert.Equal("REASON_TOO_LONG", await ErrorCode(response));
    }

    [Fact] // 6
    public async Task A_second_pending_request_for_the_same_shift_is_refused()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        await Ask(employee, shiftId, null, "2026-11-18T17:00");

        var second = await Ask(employee, shiftId, "2026-11-18T07:30", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("REQUEST_ALREADY_PENDING", await ErrorCode(second));
    }

    [Fact] // 7 (DB rule: one pending request per shift)
    public async Task Five_simultaneous_requests_create_exactly_one()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Ask(employee, shiftId, null, "2026-11-18T17:00")));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact] // 8
    public async Task Employees_cant_see_all_requests_or_decide()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        var request = await Read(await Ask(employee, shiftId, null, "2026-11-18T17:00"));

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/correction-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsync($"/api/admin/correction-requests/{request.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsync($"/api/admin/correction-requests/{request.Id}/reject", null)).StatusCode);
    }

    [Fact] // 9 (PRD rule 2: approval = an audited manager correction)
    public async Task Approving_corrects_the_shift_labels_it_and_writes_an_audit_row()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        var manager = await Manager();
        var request = await Read(await Ask(employee, shiftId, null, "2026-11-18T17:00"));
        fixture.Clock.SetNow("2026-11-20T18:05:00+01:00");

        var response = await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = await Read(response);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("Test Manager", approved.DecidedBy);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T18:05:00+01:00"), approved.DecidedAt); // trusted clock

        var shift = await Shift(manager, shiftId);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T17:00:00+01:00"), shift.ClockOut);
        Assert.Equal(AdminService.ManualCorrectionSource, shift.ClockOutSource);
        Assert.Equal("test-data", shift.ClockInSource); // Clock In wasn't requested, so it keeps its source
        Assert.Contains("Corrected", shift.Flags);

        var row = Assert.Single(await Audit(manager, shiftId));
        Assert.Equal("ClockOut", row.Field);
        Assert.Equal("Test Manager", row.ChangedBy);
        Assert.Equal($"Request #{request.Id} from Test Employee: {Reason}", row.Reason);
    }

    [Fact] // 10 (EC-13 re-checked at approval)
    public async Task Approval_that_would_now_overlap_is_refused_and_the_request_stays_pending()
    {
        var (employee, id, shiftId) = await EmployeeWithFinishedShift();
        var manager = await Manager();
        var request = await Read(await Ask(employee, shiftId, null, "2026-11-18T21:00")); // stayed later
        await fixture.AddShiftAsync(id, "2026-11-18T20:00:00+01:00", "2026-11-18T22:00:00+01:00"); // added afterwards

        var response = await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("OVERLAPS_ANOTHER_SHIFT", await ErrorCode(response));
        var pending = await manager.GetFromJsonAsync<List<CorrectionRequestDto>>("/api/admin/correction-requests?status=Pending");
        Assert.Contains(pending!, r => r.Id == request.Id);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T19:00:00+01:00"), (await Shift(manager, shiftId)).ClockOut);
        Assert.Empty(await Audit(manager, shiftId));
    }

    [Fact] // 11 (user's decision: reject needs no explanation)
    public async Task Rejecting_leaves_the_shift_alone_and_allows_a_new_request()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        var manager = await Manager();
        var request = await Read(await Ask(employee, shiftId, null, "2026-11-18T17:00"));

        var response = await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/reject", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = await Read(response);
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Test Manager", rejected.DecidedBy);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T19:00:00+01:00"), (await Shift(manager, shiftId)).ClockOut);
        Assert.Empty(await Audit(manager, shiftId));
        Assert.Equal(HttpStatusCode.Created, (await Ask(employee, shiftId, null, "2026-11-18T17:30")).StatusCode);
    }

    [Fact] // 12
    public async Task A_decided_request_cant_be_decided_again()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        var manager = await Manager();
        var request = await Read(await Ask(employee, shiftId, null, "2026-11-18T17:00"));
        await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/approve", null);

        var again = await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/approve", null);
        var reject = await manager.PostAsync($"/api/admin/correction-requests/{request.Id}/reject", null);

        Assert.Equal("REQUEST_ALREADY_DECIDED", await ErrorCode(again));
        Assert.Equal("REQUEST_ALREADY_DECIDED", await ErrorCode(reject));
        Assert.Single(await Audit(manager, shiftId)); // the shift was corrected once, not twice
    }

    [Fact] // 13
    public async Task Manager_list_filters_by_status_and_rejects_unknown_status()
    {
        var (employee, _, shiftId) = await EmployeeWithFinishedShift();
        var manager = await Manager();
        var request = await Read(await Ask(employee, shiftId, null, "2026-11-18T17:00"));

        var pending = await manager.GetFromJsonAsync<List<CorrectionRequestDto>>("/api/admin/correction-requests?status=pending");
        var approved = await manager.GetFromJsonAsync<List<CorrectionRequestDto>>("/api/admin/correction-requests?status=Approved");
        var bogus = await manager.GetAsync("/api/admin/correction-requests?status=Maybe");

        var mine = Assert.Single(pending!, r => r.Id == request.Id);
        Assert.Equal(DateTimeOffset.Parse("2026-11-18T19:00:00+01:00"), mine.Shift.ClockOut); // recorded, for comparison
        Assert.DoesNotContain(approved!, r => r.Id == request.Id);
        Assert.Equal("INVALID_STATUS", await ErrorCode(bogus));
    }
}

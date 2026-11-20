using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Reports;
using AttendanceClock.Tests.Punches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceClock.Tests.Reports;

/// <summary>Plan 15 test table: manager reports and the CSV export. Each test uses its own month, so totals don't mix.</summary>
[Collection(DatabaseCollection.Name)]
public class ReportTests(PunchFixture fixture) : IClassFixture<PunchFixture>
{
    private async Task<HttpClient> Manager() => (await fixture.NewEmployeeClientAsync(EmployeeRole.Manager)).Client;

    private static async Task<ReportDto> Report(HttpClient manager, string from, string to, int? employeeId = null) =>
        (await manager.GetFromJsonAsync<ReportDto>($"/api/admin/reports?from={from}&to={to}" +
            (employeeId is { } id ? $"&employeeId={id}" : "")))!;

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task MarkCorrected(long shiftId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Shifts.Where(s => s.Id == shiftId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Flags, ShiftFlags.Corrected));
    }

    private async Task<int> NewEmployee(string displayName)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var employee = new Employee { UserName = "emp-" + Guid.NewGuid().ToString("N")[..12], DisplayName = displayName };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    [Fact] // 1 (DECISIONS #21, D-10)
    public async Task Hours_are_the_sum_of_rounded_shifts_and_an_overnight_shift_counts_on_its_start_date()
    {
        var manager = await Manager();
        var (_, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2025-01-06T08:00:00+01:00", "2025-01-06T16:00:29+01:00"); // 480.48 → 480
        await fixture.AddShiftAsync(id, "2025-01-07T09:00:00+01:00", "2025-01-07T12:00:30+01:00"); // 180.5  → 181
        await fixture.AddShiftAsync(id, "2025-01-31T22:00:00+01:00", "2025-02-01T06:00:00+01:00"); // 480, on Jan 31
        await fixture.AddShiftAsync(id, "2025-02-01T08:00:00+01:00", "2025-02-01T09:00:00+01:00"); // February: not counted

        var report = await Report(manager, "2025-01-01", "2025-01-31");

        Assert.Equal(1141, report.Totals.TotalMinutes);
        Assert.Equal(3, report.Totals.Shifts);
        Assert.Equal(1, report.Totals.EmployeesWorked);
        Assert.Equal([480, 181, 480], report.Days.Select(d => d.TotalMinutes));
        Assert.Equal(new DateOnly(2025, 1, 31), report.Days[^1].Date);
        Assert.Equal(1141, Assert.Single(report.Employees).TotalMinutes);
    }

    [Fact] // 2 (DST day boundaries)
    public async Task Range_edges_follow_zurich_midnight_on_the_night_the_clocks_jump()
    {
        // 30 Mar 2025: clocks jump 02:00 → 03:00, so that day is 23 hours long. Midnight after it is 00:00 CEST.
        var manager = await Manager();
        var (_, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2025-03-30T23:30:00+02:00", "2025-03-31T01:00:00+02:00"); // belongs to Mar 30
        await fixture.AddShiftAsync(id, "2025-03-31T00:00:00+02:00", "2025-03-31T02:00:00+02:00"); // belongs to Mar 31

        var day = await Report(manager, "2025-03-30", "2025-03-30");

        Assert.Equal(1, day.Totals.Shifts);
        Assert.Equal(90, day.Totals.TotalMinutes);
    }

    [Fact] // 3
    public async Task Tiles_split_office_remote_unknown_and_count_long_corrected_and_open_shifts()
    {
        var manager = await Manager();
        var (_, id) = await fixture.NewEmployeeClientAsync();
        await fixture.AddShiftAsync(id, "2025-04-01T08:00:00+02:00", "2025-04-01T16:00:00+02:00", WorkLocation.Office); // 480
        await fixture.AddShiftAsync(id, "2025-04-02T08:00:00+02:00", "2025-04-02T12:00:00+02:00", WorkLocation.Remote); // 240
        await fixture.AddShiftAsync(id, "2025-04-03T08:00:00+02:00", "2025-04-03T10:00:00+02:00");                       // 120, no location
        var longOne = await fixture.AddShiftAsync(id, "2025-04-04T06:00:00+02:00", "2025-04-04T21:00:00+02:00", WorkLocation.Office); // 900
        await MarkCorrected(longOne);
        await fixture.AddShiftAsync(id, "2025-04-07T08:00:00+02:00", null, WorkLocation.Remote);                         // still open

        var t = (await Report(manager, "2025-04-01", "2025-04-30")).Totals;

        Assert.Equal(1740, t.TotalMinutes); // the open shift isn't in the hours
        Assert.Equal(4, t.Shifts);
        Assert.Equal(1380, t.OfficeMinutes);
        Assert.Equal(240, t.RemoteMinutes);
        Assert.Equal(120, t.UnknownLocationMinutes);
        Assert.Equal(1, t.OverLimitShifts);
        Assert.Equal(1, t.CorrectedShifts);
        Assert.Equal(1, t.OpenShifts);
    }

    [Fact] // 4
    public async Task One_row_per_employee_and_the_filter_shows_only_that_employee()
    {
        var manager = await Manager();
        var anna = await NewEmployee("Anna Report");
        var ben = await NewEmployee("Ben Report");
        await fixture.AddShiftAsync(anna, "2025-05-05T08:00:00+02:00", "2025-05-05T12:00:00+02:00"); // 240
        await fixture.AddShiftAsync(ben, "2025-05-05T09:00:00+02:00", "2025-05-05T10:00:00+02:00");  // 60
        await fixture.AddShiftAsync(ben, "2025-05-06T09:00:00+02:00", "2025-05-06T10:30:00+02:00");  // 90

        var all = await Report(manager, "2025-05-01", "2025-05-31");
        var onlyBen = await Report(manager, "2025-05-01", "2025-05-31", ben);

        Assert.Equal(["Anna Report", "Ben Report"], all.Employees.Select(e => e.Name)); // by name
        Assert.Equal([240, 150], all.Employees.Select(e => e.TotalMinutes));
        Assert.Equal(2, all.Totals.EmployeesWorked);
        Assert.Equal(150, onlyBen.Totals.TotalMinutes);
        Assert.Equal(ben, Assert.Single(onlyBen.Employees).EmployeeId);
    }

    [Fact] // 5 (Plan 13)
    public async Task Waiting_fix_requests_for_shifts_in_the_range_are_counted()
    {
        fixture.Clock.SetNow("2026-11-20T18:00:00+01:00");
        var manager = await Manager();
        var (employee, id) = await fixture.NewEmployeeClientAsync();
        var shift = await fixture.AddShiftAsync(id, "2025-06-02T08:00:00+02:00", "2025-06-02T16:00:00+02:00");
        await employee.PostAsJsonAsync($"/api/shifts/{shift}/correction-requests",
            new { clockOut = "2025-06-02T15:00", reason = "Left an hour earlier" });

        Assert.Equal(1, (await Report(manager, "2025-06-01", "2025-06-30")).Totals.PendingRequests);
        Assert.Equal(0, (await Report(manager, "2025-07-01", "2025-07-31")).Totals.PendingRequests);
    }

    [Fact] // 6
    public async Task Employees_cant_see_reports_and_bad_ranges_are_refused()
    {
        var manager = await Manager();
        var (employee, _) = await fixture.NewEmployeeClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/reports?from=2025-01-01&to=2025-01-31")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/reports/export?from=2025-01-01&to=2025-01-31")).StatusCode);
        Assert.Equal("INVALID_RANGE", await ErrorCode(await manager.GetAsync("/api/admin/reports?from=2025-02-01&to=2025-01-01")));
        Assert.Equal("INVALID_RANGE", await ErrorCode(await manager.GetAsync("/api/admin/reports?from=2025-01-01&to=2025-06-30")));
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/admin/reports?to=2025-01-31")).StatusCode); // no 'from'
    }

    [Fact] // 7
    public async Task Csv_has_one_row_per_shift_opens_in_excel_and_cant_run_formulas()
    {
        var manager = await Manager();
        var tricky = await NewEmployee("=Smith, Anna \"AJ\"");
        await fixture.AddShiftAsync(tricky, "2025-08-04T08:00:00+02:00", "2025-08-04T16:30:00+02:00", WorkLocation.Remote);
        await fixture.AddShiftAsync(tricky, "2025-08-05T08:00:00+02:00");                               // still open

        var response = await manager.GetAsync($"/api/admin/reports/export?from=2025-08-01&to=2025-08-31&employeeId={tricky}");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attendance-2025-08-01_2025-08-31.csv", response.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);                                // BOM: Excel reads UTF-8
        Assert.Equal(3, lines.Length);                                                             // header + 2 shifts
        Assert.Equal(string.Join(",", ReportCsv.Header), lines[0]);
        Assert.Equal("2025-08-04,\"'=Smith, Anna \"\"AJ\"\"\",2025-08-04 08:00:00,2025-08-04 16:30:00,CEST,8:30,510,Remote,,test-data,test-data",
            lines[1]);                                                                             // quoted + formula guard
        Assert.Contains(",in progress,", lines[2]);
    }
}

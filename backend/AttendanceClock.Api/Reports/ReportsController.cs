using System.Text;
using AttendanceClock.Api.Common;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.History;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AttendanceClock.Api.Reports;

/// <summary>Manager reports and CSV export (Plan 15). Employees get 403 Forbidden.</summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = nameof(EmployeeRole.Manager))]
public class ReportsController(ReportService reports) : ControllerBase
{
    /// <summary>Tiles, per-employee and per-day totals for a range of Zurich dates (at most 93 days, like history).</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery, BindRequired] DateOnly from, [FromQuery, BindRequired] DateOnly to,
        [FromQuery] int? employeeId, CancellationToken ct) =>
        InvalidRange(from, to) ?? Ok(await reports.GetAsync(from, to, employeeId, ct));

    /// <summary>The same range as a CSV file: one row per shift.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery, BindRequired] DateOnly from, [FromQuery, BindRequired] DateOnly to,
        [FromQuery] int? employeeId, CancellationToken ct)
    {
        if (InvalidRange(from, to) is { } problem)
            return problem;
        var csv = await reports.ExportCsvAsync(from, to, employeeId, ct);
        // UTF-8 with a BOM (byte order mark): without it, Excel guesses the old Windows encoding and garbles ä, é, —.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", FileName(from, to));
    }

    /// <summary>"attendance-2026-11-01_2026-11-30.csv" (the screen uses the same name).</summary>
    public static string FileName(DateOnly from, DateOnly to) => $"attendance-{from:yyyy-MM-dd}_{to:yyyy-MM-dd}.csv";

    private static ObjectResult? InvalidRange(DateOnly from, DateOnly to) =>
        from > to || to.DayNumber - from.DayNumber + 1 > HistoryService.MaxRangeDays
            ? ApiProblem.Create(400, "INVALID_RANGE", "Invalid date range.",
                $"'from' must be on or before 'to', and the range at most {HistoryService.MaxRangeDays} days.")
            : null;
}

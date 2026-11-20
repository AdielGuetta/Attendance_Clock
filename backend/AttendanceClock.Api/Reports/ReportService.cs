using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Punches;
using AttendanceClock.Api.Time;
using Microsoft.EntityFrameworkCore;

namespace AttendanceClock.Api.Reports;

/// <summary>
/// Manager reports and the CSV export (Plan 15). Computed live from the existing tables: nothing is stored, so a report
/// is never out of date. Uses the SAME rules as My week and the Team tab, so every number matches:
/// a shift counts on the Zurich date of its Clock In (D-10), and hours are the sum of each closed shift's
/// nearest-minute length (DECISIONS #21). Open shifts are counted separately, never guessed.
/// </summary>
public sealed class ReportService(AppDbContext db)
{
    public async Task<ReportDto> GetAsync(DateOnly from, DateOnly to, int? employeeId, CancellationToken ct)
    {
        var shifts = await ShiftsInRange(from, to, employeeId).ToListAsync(ct);
        var closed = shifts.Where(s => s.ClockOut != null).ToList();

        var (start, end) = Bounds(from, to);
        var pending = await db.CorrectionRequests.CountAsync(r =>
            r.Status == CorrectionRequestStatus.Pending &&
            r.Shift.ClockIn >= start && r.Shift.ClockIn < end &&
            (employeeId == null || r.EmployeeId == employeeId), ct);

        var totals = new ReportTotalsDto(
            TotalMinutes: closed.Sum(Minutes),
            Shifts: closed.Count,
            EmployeesWorked: shifts.Select(s => s.EmployeeId).Distinct().Count(),
            OfficeMinutes: closed.Where(s => s.WorkLocation == WorkLocation.Office).Sum(Minutes),
            RemoteMinutes: closed.Where(s => s.WorkLocation == WorkLocation.Remote).Sum(Minutes),
            UnknownLocationMinutes: closed.Where(s => s.WorkLocation == null).Sum(Minutes),
            OverLimitShifts: closed.Count(IsOverLimit),
            CorrectedShifts: shifts.Count(IsCorrected),
            OpenShifts: shifts.Count(s => s.ClockOut == null),
            PendingRequests: pending);

        var employees = shifts
            .GroupBy(s => s.EmployeeId)
            .Select(g =>
            {
                var done = g.Where(s => s.ClockOut != null).ToList();
                return new EmployeeReportDto(
                    g.Key, g.First().Employee.DisplayName,
                    done.Sum(Minutes), done.Count,
                    done.Where(s => s.WorkLocation == WorkLocation.Office).Sum(Minutes),
                    done.Where(s => s.WorkLocation == WorkLocation.Remote).Sum(Minutes),
                    done.Where(s => s.WorkLocation == null).Sum(Minutes),
                    done.Count(IsOverLimit), g.Count(IsCorrected), g.Count(s => s.ClockOut == null));
            })
            .OrderBy(e => e.Name).ThenBy(e => e.EmployeeId)
            .ToList();

        var days = closed
            .GroupBy(s => ZurichTime.ToZurichDate(s.ClockIn))
            .Select(g => new DayReportDto(g.Key, g.Sum(Minutes), g.Count()))
            .OrderBy(d => d.Date)
            .ToList();

        return new ReportDto(from, to, employeeId, totals, employees, days);
    }

    /// <summary>The CSV export: one row per shift in the range (open ones included, marked "in progress").</summary>
    public async Task<string> ExportCsvAsync(DateOnly from, DateOnly to, int? employeeId, CancellationToken ct) =>
        ReportCsv.Build(await ShiftsInRange(from, to, employeeId).ToListAsync(ct));

    /// <summary>Shifts whose Clock In falls on a Zurich date in the range (D-10), oldest first.</summary>
    private IQueryable<Shift> ShiftsInRange(DateOnly from, DateOnly to, int? employeeId)
    {
        var (start, end) = Bounds(from, to);
        var query = db.Shifts.AsNoTracking().Include(s => s.Employee)
            .Where(s => s.ClockIn >= start && s.ClockIn < end);
        if (employeeId is { } id)
            query = query.Where(s => s.EmployeeId == id);
        return query.OrderBy(s => s.ClockIn).ThenBy(s => s.EmployeeId);
    }

    /// <summary>Zurich midnight to Zurich midnight, using the official rules (DST days are 23 or 25 hours long).</summary>
    private static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly from, DateOnly to) =>
        (ZurichTime.StartOfDay(from), ZurichTime.StartOfDay(to.AddDays(1)));

    /// <summary>The same nearest-minute length the screens show (one place for the rounding: ShiftDto).</summary>
    internal static int Minutes(Shift s) => ShiftDto.From(s).DurationMinutes ?? 0;

    /// <summary>Strictly more than 14 hours, measured on the real instants (same rule as the TooLong flag, F7).</summary>
    internal static bool IsOverLimit(Shift s) => s.ClockOut is { } end && end - s.ClockIn > ShiftService.TooLongThreshold;

    private static bool IsCorrected(Shift s) => s.Flags.HasFlag(ShiftFlags.Corrected);
}

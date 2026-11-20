using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Punches;
using AttendanceClock.Api.Time;
using Microsoft.EntityFrameworkCore;

namespace AttendanceClock.Api.Admin;

/// <summary>Why a correction was refused. Each maps to a stable error code for the UI.</summary>
public enum CorrectionError
{
    None, NotFound, ReasonRequired, NothingToChange, InvalidTimeFormat, AmbiguousTime, TimeDoesNotExist,
    OffsetNotZurich, ClockOutNotAfterClockIn, TimeInFuture, OverlapsAnotherShift, ShiftModified,
    // Plan 13: employee correction requests
    ReasonTooLong, ShiftStillOpen, RequestAlreadyPending, RequestAlreadyDecided,
}

/// <param name="At">When the correction was made (trusted clock); set on success.</param>
public sealed record CorrectionResult(CorrectionError Error, AdminShiftDto? Shift = null, DateTimeOffset? At = null);

/// <summary>Manager views and shift corrections (Plan 10, PRD F5–F7).</summary>
public sealed class AdminService(AppDbContext db, ITrustedClock clock, ILogger<AdminService> logger)
{
    public const int MinReasonLength = 5;
    public const string ManualCorrectionSource = "ManualCorrection";

    /// <summary>Trusted "now" for display flags. If the time service is down the lists still work, just unflagged.</summary>
    private async Task<DateTimeOffset?> TryNowAsync(CancellationToken ct)
    {
        try { return (await clock.GetZurichNowAsync(ct)).Value; }
        catch (TimeSourceUnavailableException) { return null; }
    }

    public Task<List<EmployeeSummaryDto>> GetEmployeesAsync(CancellationToken ct) =>
        db.Employees.AsNoTracking().OrderBy(e => e.DisplayName)
            .Select(e => new EmployeeSummaryDto(e.Id, e.UserName, e.DisplayName, e.Role.ToString(), e.IsActive))
            .ToListAsync(ct);

    public async Task<OnShiftDto> GetOnShiftAsync(CancellationToken ct)
    {
        var now = await TryNowAsync(ct);
        var open = await db.Shifts.AsNoTracking().Include(s => s.Employee)
            .Where(s => s.ClockOut == null)
            .OrderBy(s => s.ClockIn)
            .ToListAsync(ct);
        return new OnShiftDto(now, open.Select(s => AdminShiftDto.From(s, s.Employee.DisplayName, now)).ToList());
    }

    public async Task<List<AdminShiftDto>> GetShiftsAsync(DateOnly from, DateOnly to, int? employeeId, bool flaggedOnly, CancellationToken ct)
    {
        var now = await TryNowAsync(ct);
        var start = ZurichTime.StartOfDay(from);
        var end = ZurichTime.StartOfDay(to.AddDays(1));
        var query = db.Shifts.AsNoTracking().Include(s => s.Employee)
            .Where(s => s.ClockIn >= start && s.ClockIn < end);
        if (employeeId is { } id)
            query = query.Where(s => s.EmployeeId == id);

        var shifts = (await query.OrderBy(s => s.ClockIn).ToListAsync(ct))
            .Select(s => AdminShiftDto.From(s, s.Employee.DisplayName, now));
        return (flaggedOnly ? shifts.Where(s => s.Flags.Count > 0) : shifts).ToList();
    }

    public async Task<List<ShiftAuditDto>?> GetAuditAsync(long shiftId, CancellationToken ct)
    {
        if (!await db.Shifts.AnyAsync(s => s.Id == shiftId, ct))
            return null;
        return await db.ShiftAudits.AsNoTracking()
            .Where(a => a.ShiftId == shiftId)
            .OrderBy(a => a.ChangedAt).ThenBy(a => a.Id)
            .Select(a => new ShiftAuditDto(a.Id, a.ChangedAt, a.ChangedBy.DisplayName, a.Field, a.OldValue, a.NewValue, a.Reason))
            .ToListAsync(ct);
    }

    /// <summary>
    /// A manager corrects a shift's times. Every change is labelled ManualCorrection, flags the shift Corrected,
    /// and adds an audit row per changed field (PRD F6). Validation covers EC-13: order, future, overlap.
    /// </summary>
    public async Task<CorrectionResult> CorrectAsync(long shiftId, int managerId, CorrectShiftRequest request, CancellationToken ct)
    {
        var shift = await db.Shifts.Include(s => s.Employee).FirstOrDefaultAsync(s => s.Id == shiftId, ct);
        if (shift is null)
            return new(CorrectionError.NotFound);

        // 1. Was the shift changed since the manager loaded it? (e.g. the employee clocked out meanwhile)
        byte[] expectedVersion;
        try { expectedVersion = Convert.FromBase64String(request.RowVersion ?? ""); }
        catch (FormatException) { return new(CorrectionError.ShiftModified); }
        if (!expectedVersion.SequenceEqual(shift.RowVersion))
            return new(CorrectionError.ShiftModified);

        // 2. A reason is mandatory: every non-API time must be explained.
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length < MinReasonLength)
            return new(CorrectionError.ReasonRequired);

        // 3. Read the typed times with Zurich's rules (DST: ambiguous / non-existent hours).
        var parseError = ParseTimes(request.ClockIn, request.ClockOut, out var newIn, out var newOut);
        if (parseError != CorrectionError.None)
            return new(parseError);

        var finalIn = newIn ?? shift.ClockIn;
        var finalOut = newOut ?? shift.ClockOut;
        var inChanged = newIn is { } i && i != shift.ClockIn;
        var outChanged = newOut is { } o && o != shift.ClockOut;
        if (!inChanged && !outChanged)
            return new(CorrectionError.NothingToChange);

        // 4. Order, future and overlap checks (EC-13).
        if (finalOut is { } fo && fo <= finalIn)
            return new(CorrectionError.ClockOutNotAfterClockIn);

        var now = await clock.GetZurichNowAsync(ct); // trusted; 503 if unavailable
        if (finalIn > now.Value || finalOut > now.Value)
            return new(CorrectionError.TimeInFuture);

        if (await OverlapsAnotherShiftAsync(shift.EmployeeId, shift.Id, finalIn, finalOut, ct))
            return new(CorrectionError.OverlapsAnotherShift);

        // 5. Apply, label and audit.
        void Audit(string field, string oldValue, DateTimeOffset newValue) => db.ShiftAudits.Add(new ShiftAudit
        {
            ShiftId = shift.Id, ChangedByEmployeeId = managerId, ChangedAt = now.Value,
            Field = field, OldValue = oldValue, NewValue = newValue.ToString("O"), Reason = reason,
        });

        if (inChanged)
        {
            Audit(nameof(Shift.ClockIn), shift.ClockIn.ToString("O"), newIn!.Value);
            shift.ClockIn = newIn.Value;
            shift.ClockInSource = ManualCorrectionSource;
        }
        if (outChanged)
        {
            Audit(nameof(Shift.ClockOut), shift.ClockOut?.ToString("O") ?? "(open)", newOut!.Value);
            shift.ClockOut = newOut.Value;
            shift.ClockOutSource = ManualCorrectionSource;
        }

        shift.Flags |= ShiftFlags.Corrected;
        if (shift.ClockOut is { } end && end - shift.ClockIn > ShiftService.TooLongThreshold)
            shift.Flags |= ShiftFlags.TooLong;
        else
            shift.Flags &= ~ShiftFlags.TooLong;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The employee (or another manager) changed this shift between our read and our save.
            return new(CorrectionError.ShiftModified);
        }

        logger.LogInformation("Manager {ManagerId} corrected shift {ShiftId} of employee {EmployeeId}: {Reason}",
            managerId, shift.Id, shift.EmployeeId, reason);
        return new(CorrectionError.None, AdminShiftDto.From(shift, shift.Employee.DisplayName, now.Value), now.Value);
    }

    /// <summary>
    /// Reads typed Clock In / Clock Out with Zurich's rules (DST: ambiguous / non-existent hours). Empty = not given.
    /// Shared by manager corrections and employee correction requests (Plan 13), so both read times the same way.
    /// </summary>
    public static CorrectionError ParseTimes(string? clockIn, string? clockOut, out DateTimeOffset? newIn, out DateTimeOffset? newOut)
    {
        newIn = null;
        newOut = null;
        foreach (var (text, isIn) in new[] { (clockIn, true), (clockOut, false) })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var (value, problem) = ZurichTime.ParseManagerTime(text);
            if (problem != ZurichTime.ManagerTimeProblem.None)
                return problem switch
                {
                    ZurichTime.ManagerTimeProblem.Ambiguous => CorrectionError.AmbiguousTime,
                    ZurichTime.ManagerTimeProblem.DoesNotExist => CorrectionError.TimeDoesNotExist,
                    ZurichTime.ManagerTimeProblem.OffsetNotZurich => CorrectionError.OffsetNotZurich,
                    _ => CorrectionError.InvalidTimeFormat,
                };
            if (isIn) newIn = value; else newOut = value;
        }
        return CorrectionError.None;
    }

    /// <summary>Would this shift, with these times, overlap another shift of the same employee? (EC-13)</summary>
    public Task<bool> OverlapsAnotherShiftAsync(int employeeId, long shiftId, DateTimeOffset start, DateTimeOffset? end, CancellationToken ct) =>
        db.Shifts.AnyAsync(s =>
            s.EmployeeId == employeeId && s.Id != shiftId &&
            (end == null || s.ClockIn < end) &&
            (s.ClockOut == null || s.ClockOut > start), ct);
}

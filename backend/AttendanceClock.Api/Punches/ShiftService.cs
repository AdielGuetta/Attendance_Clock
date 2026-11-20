using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AttendanceClock.Api.Punches;

/// <summary>What happened when an employee punched.</summary>
public enum PunchOutcome { Ok, AlreadyOnShift, NotOnShift, TimeInconsistent, ShiftChanged }

/// <param name="Shift">The saved shift (Ok), or the shift that's open now (AlreadyOnShift, ShiftChanged).</param>
public sealed record PunchResult(PunchOutcome Outcome, Shift? Shift = null);

/// <summary>
/// The Clock In / Clock Out rules (Plan 07). Every recorded time comes from <see cref="ITrustedClock"/>.
/// The database constraints are the final guard against races (PRD D-9).
/// </summary>
public sealed class ShiftService(AppDbContext db, ITrustedClock clock, ILogger<ShiftService> logger)
{
    /// <summary>Shifts longer than this are flagged for the manager (PRD F7).</summary>
    public static readonly TimeSpan TooLongThreshold = TimeSpan.FromHours(14);

    public Task<Shift?> GetOpenShiftAsync(int employeeId, CancellationToken ct) =>
        db.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.ClockOut == null, ct);

    public async Task<PunchResult> ClockInAsync(int employeeId, WorkLocation location, CancellationToken ct)
    {
        // 1. Already on shift? Answer before calling the time API, to save a call (EC-4).
        var open = await GetOpenShiftAsync(employeeId, ct);
        if (open is not null)
            return new PunchResult(PunchOutcome.AlreadyOnShift, open);

        // 2. Official time. Throws TimeSourceUnavailableException → 503, nothing saved (PRD D-5).
        var now = await clock.GetZurichNowAsync(ct);

        // 2b. One change per employee at a time (Plan 14): a manager correcting this employee's last shift right now
        //     can't slip past the check below. Taken after the time call, so the lock is never held while waiting on it.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockEmployeeAsync(employeeId, ct);

        // 3. Must be after the previous shift ended, so providers that disagree slightly can't create overlapping shifts.
        var lastClockOut = await db.Shifts
            .Where(s => s.EmployeeId == employeeId && s.ClockOut != null)
            .MaxAsync(s => s.ClockOut, ct);
        if (lastClockOut is not null && now.Value <= lastClockOut)
        {
            logger.LogWarning("Clock In for employee {EmployeeId} rejected: {Now:O} is not after the last Clock Out {LastOut:O}",
                employeeId, now.Value, lastClockOut);
            return new PunchResult(PunchOutcome.TimeInconsistent);
        }

        // 4. Save. If a simultaneous request got there first, the unique index refuses this insert (EC-1/2).
        var shift = new Shift { EmployeeId = employeeId, ClockIn = now.Value, ClockInSource = now.Source, WorkLocation = location };
        db.Shifts.Add(shift);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct); // releases the employee lock
        }
        catch (DbUpdateException ex) when (IsUniqueIndexViolation(ex))
        {
            db.Entry(shift).State = EntityState.Detached;
            logger.LogInformation("Concurrent Clock In for employee {EmployeeId} refused by the database", employeeId);
            return new PunchResult(PunchOutcome.AlreadyOnShift, await GetOpenShiftAsync(employeeId, ct));
        }

        logger.LogInformation("Employee {EmployeeId} clocked in at {Time:O} (source {Source}, {Location})",
            employeeId, now.Value, now.Source, location);
        return new PunchResult(PunchOutcome.Ok, shift);
    }

    /// <param name="expectedShiftId">The shift the screen shows (Plan 14); null = whichever is open (as before).</param>
    public async Task<PunchResult> ClockOutAsync(int employeeId, long? expectedShiftId, CancellationToken ct)
    {
        // 1. Is there an open shift to close? (EC-3)
        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.ClockOut == null, ct);
        if (shift is null)
            return new PunchResult(PunchOutcome.NotOnShift);

        // 1b. Is it the one the screen shows? An old tab may still show a shift that was closed elsewhere while a newer
        //     one started: closing the newer one by mistake would cut it short. Refuse, before calling the time API.
        if (expectedShiftId is { } expected && expected != shift.Id)
            return new PunchResult(PunchOutcome.ShiftChanged, shift);

        // 2. Official time (503 if unavailable).
        var now = await clock.GetZurichNowAsync(ct);

        // 3. Must be after the Clock In (EC-9). Never save a zero or negative shift.
        if (now.Value <= shift.ClockIn)
        {
            logger.LogWarning("Clock Out for employee {EmployeeId} rejected: {Now:O} is not after Clock In {In:O}",
                employeeId, now.Value, shift.ClockIn);
            return new PunchResult(PunchOutcome.TimeInconsistent);
        }

        shift.ClockOut = now.Value;
        shift.ClockOutSource = now.Source;
        if (now.Value - shift.ClockIn > TooLongThreshold)
            shift.Flags |= ShiftFlags.TooLong; // recorded honestly, flagged for the manager (F7)

        // 4. Save with the rowversion check: if the shift changed meanwhile (e.g. a double Clock Out), refuse.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("Concurrent Clock Out for employee {EmployeeId}: the shift was already changed", employeeId);
            return new PunchResult(PunchOutcome.NotOnShift);
        }

        logger.LogInformation("Employee {EmployeeId} clocked out at {Time:O} (source {Source})", employeeId, now.Value, now.Source);
        return new PunchResult(PunchOutcome.Ok, shift);
    }

    // SQL Server error 2601 = duplicate key in a unique index; 2627 = unique constraint violation.
    private static bool IsUniqueIndexViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}

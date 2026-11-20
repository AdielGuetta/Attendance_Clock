using Microsoft.EntityFrameworkCore;

namespace AttendanceClock.Api.Data;

/// <summary>
/// "One change per employee at a time" (Plan 14). Some rules span SEVERAL shifts of one employee ("no overlap",
/// "Clock In after the last Clock Out"), so the database can't enforce them with a constraint on one row. If two changes
/// for the same employee run at the same moment, each could check, see no problem, and save: together they'd break
/// the rule (a race condition). Taking this lock right before such a check, and holding it until the save is committed,
/// makes the second change wait and then see the first one's result. Different employees never wait for each other.
/// </summary>
public static class EmployeeLock
{
    /// <summary>
    /// Locks the employee's row until the current transaction ends. UPDLOCK: only one holder at a time; normal reads
    /// (lists, history) aren't blocked. Must be called inside a transaction, or the lock would be released at once.
    /// </summary>
    public static async Task LockEmployeeAsync(this AppDbContext db, int employeeId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("LockEmployeeAsync must run inside a transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM Employees WITH (UPDLOCK, ROWLOCK) WHERE Id = {employeeId}", ct);
    }
}

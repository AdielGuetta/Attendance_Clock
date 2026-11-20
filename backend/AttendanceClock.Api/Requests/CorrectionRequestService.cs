using System.Globalization;
using AttendanceClock.Api.Admin;
using AttendanceClock.Api.Data;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AttendanceClock.Api.Requests;

/// <param name="Request">The request after the action (on success).</param>
public sealed record RequestResult(CorrectionError Error, CorrectionRequestDto? Request = null);

/// <summary>
/// Employee correction requests and the manager's decision (Plan 13, PRD N3).
/// An employee's typed times never touch a shift directly: approving runs the normal manager correction
/// (<see cref="AdminService.CorrectAsync"/>), so every rule, label and audit row of Plan 10 applies (PRD rule 2).
/// </summary>
public sealed class CorrectionRequestService(AppDbContext db, ITrustedClock clock, AdminService admin, ILogger<CorrectionRequestService> logger)
{
    /// <summary>Leaves room in the 500-character audit reason for "Request #N from Name: ".</summary>
    public const int MaxReasonLength = 300;

    public async Task<RequestResult> CreateAsync(int employeeId, long shiftId, CreateCorrectionRequest body, CancellationToken ct)
    {
        // 1. Only your own shift. Someone else's shift answers "not found", so ids don't reveal other people's data.
        var shift = await db.Shifts.Include(s => s.Employee)
            .FirstOrDefaultAsync(s => s.Id == shiftId && s.EmployeeId == employeeId, ct);
        if (shift is null)
            return new(CorrectionError.NotFound);

        // 2. Only a finished shift (user's decision): an open shift is closed by clocking out, not by a request.
        if (shift.ClockOut is not { } recordedOut)
            return new(CorrectionError.ShiftStillOpen);

        // 3. One pending request per shift (the database enforces it too, see step 8).
        if (await db.CorrectionRequests.AnyAsync(r => r.ShiftId == shiftId && r.Status == CorrectionRequestStatus.Pending, ct))
            return new(CorrectionError.RequestAlreadyPending);

        // 4. A reason, so the manager knows what happened.
        var reason = body.Reason?.Trim() ?? "";
        if (reason.Length < AdminService.MinReasonLength)
            return new(CorrectionError.ReasonRequired);
        if (reason.Length > MaxReasonLength)
            return new(CorrectionError.ReasonTooLong);

        // 5. Read the times exactly like a manager's (Zurich rules, DST), and keep only real changes.
        var parseError = AdminService.ParseTimes(body.ClockIn, body.ClockOut, out var newIn, out var newOut);
        if (parseError != CorrectionError.None)
            return new(parseError);
        if (newIn == shift.ClockIn) newIn = null;
        if (newOut == recordedOut) newOut = null;
        if (newIn is null && newOut is null)
            return new(CorrectionError.NothingToChange);

        var finalIn = newIn ?? shift.ClockIn;
        var finalOut = newOut ?? recordedOut;
        if (finalOut <= finalIn)
            return new(CorrectionError.ClockOutNotAfterClockIn);

        // 6. Not in the future, judged by the trusted clock (503 if unavailable, nothing saved).
        var now = await clock.GetZurichNowAsync(ct);
        if (finalIn > now.Value || finalOut > now.Value)
            return new(CorrectionError.TimeInFuture);

        // 7. Caught now rather than at approval, so the employee can fix it straight away. (Approval checks again.)
        if (await admin.OverlapsAnotherShiftAsync(employeeId, shift.Id, finalIn, finalOut, ct))
            return new(CorrectionError.OverlapsAnotherShift);

        // 8. Save. Two requests sent at the same instant: the unique index refuses the second.
        var request = new CorrectionRequest
        {
            Shift = shift, Employee = shift.Employee, ShiftId = shift.Id, EmployeeId = employeeId,
            RequestedClockIn = newIn, RequestedClockOut = newOut, Reason = reason, CreatedAt = now.Value,
        };
        db.CorrectionRequests.Add(request);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return new(CorrectionError.RequestAlreadyPending);
        }

        logger.LogInformation("Employee {EmployeeId} requested a correction of shift {ShiftId} (request {RequestId})",
            employeeId, shift.Id, request.Id);
        return new(CorrectionError.None, CorrectionRequestDto.From(request));
    }

    public Task<List<CorrectionRequestDto>> GetMineAsync(int employeeId, CancellationToken ct) =>
        List(db.CorrectionRequests.Where(r => r.EmployeeId == employeeId), ct);

    /// <param name="status">Null = all requests.</param>
    public Task<List<CorrectionRequestDto>> GetAllAsync(CorrectionRequestStatus? status, CancellationToken ct) =>
        List(status is { } s ? db.CorrectionRequests.Where(r => r.Status == s) : db.CorrectionRequests, ct);

    private static async Task<List<CorrectionRequestDto>> List(IQueryable<CorrectionRequest> query, CancellationToken ct) =>
        (await query.AsNoTracking()
            .Include(r => r.Shift).Include(r => r.Employee).Include(r => r.DecidedBy)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToListAsync(ct))
        .Select(CorrectionRequestDto.From).ToList();

    /// <summary>
    /// Approve = the manager corrects the shift with the requested times. The correction and the "Approved" status are
    /// saved in ONE transaction: never a corrected shift with a pending request, or an approved request with an unchanged shift.
    /// If the correction is refused (e.g. it would now overlap a newer shift), the request stays pending.
    /// </summary>
    public async Task<RequestResult> ApproveAsync(long requestId, int managerId, CancellationToken ct)
    {
        var request = await db.CorrectionRequests.Include(r => r.Shift).Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return new(CorrectionError.NotFound);
        if (request.Status != CorrectionRequestStatus.Pending)
            return new(CorrectionError.RequestAlreadyDecided);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var correction = await admin.CorrectAsync(request.ShiftId, managerId, new CorrectShiftRequest(
            ClockIn: Format(request.RequestedClockIn),
            ClockOut: Format(request.RequestedClockOut),
            Reason: $"Request #{request.Id} from {request.Employee.DisplayName}: {request.Reason}",
            RowVersion: Convert.ToBase64String(request.Shift.RowVersion)), ct);
        if (correction.Error != CorrectionError.None)
            return new(correction.Error); // nothing was saved; the transaction rolls back when disposed

        request.Status = CorrectionRequestStatus.Approved;
        request.DecidedBy = await db.Employees.FindAsync([managerId], ct);
        request.DecidedAt = correction.At;
        if (!await TrySaveAsync(ct))
            return new(CorrectionError.RequestAlreadyDecided); // another manager decided first: roll back our correction too

        await transaction.CommitAsync(ct);
        logger.LogInformation("Manager {ManagerId} approved correction request {RequestId}", managerId, request.Id);
        return new(CorrectionError.None, CorrectionRequestDto.From(request));
    }

    /// <summary>Reject: one tap, no explanation needed (user's decision). The shift isn't touched.</summary>
    public async Task<RequestResult> RejectAsync(long requestId, int managerId, CancellationToken ct)
    {
        var request = await db.CorrectionRequests.Include(r => r.Shift).Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null)
            return new(CorrectionError.NotFound);
        if (request.Status != CorrectionRequestStatus.Pending)
            return new(CorrectionError.RequestAlreadyDecided);

        var now = await clock.GetZurichNowAsync(ct); // decision time from the trusted clock, like audit rows
        request.Status = CorrectionRequestStatus.Rejected;
        request.DecidedBy = await db.Employees.FindAsync([managerId], ct);
        request.DecidedAt = now.Value;
        if (!await TrySaveAsync(ct))
            return new(CorrectionError.RequestAlreadyDecided);

        logger.LogInformation("Manager {ManagerId} rejected correction request {RequestId}", managerId, request.Id);
        return new(CorrectionError.None, CorrectionRequestDto.From(request));
    }

    /// <summary>False if the request changed since we read it (rowversion), i.e. someone else already decided.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    /// <summary>Full ISO with the stored Zurich offset, which the correction parser accepts as-is.</summary>
    private static string? Format(DateTimeOffset? t) =>
        t?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}

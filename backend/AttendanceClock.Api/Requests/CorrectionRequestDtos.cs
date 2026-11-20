using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Punches;

namespace AttendanceClock.Api.Requests;

/// <summary>Body of <c>POST /api/shifts/{id}/correction-requests</c>. Leave a time empty to keep the recorded one.</summary>
/// <param name="ClockIn">Zurich wall time like "2026-11-20T08:00" (same format as manager corrections).</param>
public sealed record CreateCorrectionRequest(string? ClockIn, string? ClockOut, string? Reason);

/// <summary>A correction request as the screens see it, with the shift's CURRENT recorded times for comparison.</summary>
/// <param name="Status">"Pending", "Approved" or "Rejected".</param>
/// <param name="RequestedClockIn">Null = the employee keeps the recorded Clock In.</param>
/// <param name="DecidedBy">Name of the manager who approved or rejected it; null while pending.</param>
public sealed record CorrectionRequestDto(
    long Id,
    long ShiftId,
    int EmployeeId,
    string EmployeeName,
    string Status,
    DateTimeOffset? RequestedClockIn,
    DateTimeOffset? RequestedClockOut,
    string Reason,
    DateTimeOffset CreatedAt,
    string? DecidedBy,
    DateTimeOffset? DecidedAt,
    ShiftDto Shift)
{
    /// <summary>Needs Shift, Employee and DecidedBy loaded.</summary>
    public static CorrectionRequestDto From(CorrectionRequest r) => new(
        r.Id, r.ShiftId, r.EmployeeId, r.Employee.DisplayName, r.Status.ToString(),
        r.RequestedClockIn, r.RequestedClockOut, r.Reason, r.CreatedAt,
        r.DecidedBy?.DisplayName, r.DecidedAt, ShiftDto.From(r.Shift));
}

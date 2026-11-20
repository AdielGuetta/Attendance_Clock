using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Punches;

namespace AttendanceClock.Api.Admin;

/// <summary>A shift as the manager sees it: with the employee's name and the version stamp needed to edit it.</summary>
/// <param name="RowVersion">Base64 version stamp. Send it back when correcting, so a shift that changed meanwhile isn't overwritten.</param>
/// <param name="Flags">Stored flags plus "ForgotClockOut" when the shift is still open after more than 14 hours.</param>
public sealed record AdminShiftDto(
    long Id,
    int EmployeeId,
    string EmployeeName,
    DateOnly ZurichDate,
    DateTimeOffset ClockIn,
    string ClockInSource,
    DateTimeOffset? ClockOut,
    string? ClockOutSource,
    int? DurationMinutes,
    IReadOnlyList<string> Flags,
    string RowVersion,
    string? WorkLocation) // shown to the manager, not editable (Plan 12)
{
    public static AdminShiftDto From(Shift s, string employeeName, DateTimeOffset? now)
    {
        var dto = ShiftDto.From(s);
        var flags = dto.Flags.ToList();
        if (s.ClockOut is null && now is { } n && n - s.ClockIn > ShiftService.TooLongThreshold)
            flags.Add(nameof(ShiftFlags.ForgotClockOut)); // computed against the trusted "now" (PRD F7)
        return new AdminShiftDto(s.Id, s.EmployeeId, employeeName, dto.ZurichDate, s.ClockIn, s.ClockInSource,
            s.ClockOut, s.ClockOutSource, dto.DurationMinutes, flags, Convert.ToBase64String(s.RowVersion), dto.WorkLocation);
    }
}

public sealed record EmployeeSummaryDto(int Id, string UserName, string DisplayName, string Role, bool IsActive);

/// <param name="Now">The trusted Zurich time the list was computed at, or null if the time service was unavailable
/// (the list is still shown, just without the ForgotClockOut flag).</param>
public sealed record OnShiftDto(DateTimeOffset? Now, IReadOnlyList<AdminShiftDto> Shifts);

/// <param name="ClockIn">New Clock In as Zurich wall time ("2026-11-20T08:00") or with an offset; null = unchanged.</param>
/// <param name="ClockOut">New Clock Out, same format; null = unchanged.</param>
public sealed record CorrectShiftRequest(string? ClockIn, string? ClockOut, string? Reason, string? RowVersion);

public sealed record ShiftAuditDto(long Id, DateTimeOffset ChangedAt, string ChangedBy, string Field, string OldValue, string NewValue, string Reason);

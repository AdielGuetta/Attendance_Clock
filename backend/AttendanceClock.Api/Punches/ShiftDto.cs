using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Time;

namespace AttendanceClock.Api.Punches;

/// <summary>
/// A shift as the UI sees it. Times carry the Zurich offset, e.g. "2026-11-20T08:02:11+01:00".
/// <see cref="ZurichDate"/> is the calendar day the shift belongs to: the Zurich date of its Clock In (PRD D-10).
/// </summary>
public sealed record ShiftDto(
    long Id,
    DateOnly ZurichDate,
    DateTimeOffset ClockIn,
    string ClockInSource,
    DateTimeOffset? ClockOut,
    string? ClockOutSource,
    int? DurationMinutes,
    IReadOnlyList<string> Flags,
    string? WorkLocation) // "Office" / "Remote"; null for shifts from before Plan 12
{
    public static ShiftDto From(Shift s) => new(
        s.Id,
        ZurichTime.ToZurichDate(s.ClockIn),
        s.ClockIn,
        s.ClockInSource,
        s.ClockOut,
        s.ClockOutSource,
        // Subtracting DateTimeOffsets compares the real instants, so DST nights come out right (PRD D-6).
        // Rounded to the NEAREST minute (30 s rounds up): fair in both directions, and history totals are
        // the sum of these values, so what the employee sees always adds up (Plan 08, DECISIONS #7).
        s.ClockOut is { } clockOut ? (int)Math.Round((clockOut - s.ClockIn).TotalMinutes, MidpointRounding.AwayFromZero) : null,
        Enum.GetValues<ShiftFlags>().Where(f => f != ShiftFlags.None && s.Flags.HasFlag(f)).Select(f => f.ToString()).ToList(),
        s.WorkLocation?.ToString());
}

public sealed record PunchStatusDto(string State, ShiftDto? OpenShift);

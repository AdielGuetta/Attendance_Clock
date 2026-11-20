using AttendanceClock.Api.Domain;

namespace AttendanceClock.Api.Punches;

/// <summary>
/// Body of <c>POST /api/punches/clock-in</c> (Plan 12): only where the employee is working.
/// There is deliberately NO time field. Any time a client adds to the JSON is ignored, because the
/// server always takes the time from the trusted clock (PRD D-2).
/// </summary>
/// <param name="Location">"Office" or "Remote" (any letter case).</param>
public sealed record ClockInRequest(string? Location)
{
    /// <summary>
    /// Accepts only the names of <see cref="WorkLocation"/>. Plain <c>Enum.TryParse</c> isn't enough: it would also
    /// accept numbers like "1" or "7", which would sneak unknown values past the check.
    /// </summary>
    public static bool TryParseLocation(string? value, out WorkLocation location)
    {
        location = default;
        var name = Enum.GetNames<WorkLocation>()
            .FirstOrDefault(n => string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is not null && Enum.TryParse(name, out location);
    }
}

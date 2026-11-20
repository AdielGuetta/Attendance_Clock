using AttendanceClock.Api.Common;
using AttendanceClock.Api.Requests;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceClock.Api.Admin;

/// <summary>
/// Turns a <see cref="CorrectionError"/> into the HTTP answer the UI understands (ProblemDetails + stable code).
/// Shared by manager corrections (Plan 10) and correction requests (Plan 13), so the same problem always gets the same code.
/// </summary>
public static class CorrectionProblems
{
    public static IActionResult ToResult(CorrectionError error) => error switch
    {
        CorrectionError.NotFound => new NotFoundResult(),
        CorrectionError.ReasonRequired => Bad("REASON_REQUIRED", "A reason is required.",
            $"Explain the correction in at least {AdminService.MinReasonLength} characters."),
        CorrectionError.ReasonTooLong => Bad("REASON_TOO_LONG", "The reason is too long.",
            $"Keep it under {CorrectionRequestService.MaxReasonLength} characters."),
        CorrectionError.NothingToChange => Bad("NOTHING_TO_CHANGE", "Nothing to change.", "The new times are the same as the current ones."),
        CorrectionError.InvalidTimeFormat => Bad("INVALID_TIME_FORMAT", "Invalid time.", "Use Zurich time like 2026-11-20T17:00."),
        CorrectionError.AmbiguousTime => Bad("AMBIGUOUS_TIME", "This time happens twice.",
            "On the night clocks go back, 02:00–02:59 occurs twice. Add +02:00 (first) or +01:00 (second)."),
        CorrectionError.TimeDoesNotExist => Bad("INVALID_LOCAL_TIME", "This time doesn't exist.",
            "On the night clocks jump forward, 02:00–02:59 is skipped in Zurich."),
        CorrectionError.OffsetNotZurich => Bad("INVALID_LOCAL_TIME", "Wrong offset for Zurich.",
            "That offset isn't Zurich's for that date. Leave the offset out and the server will apply it."),
        CorrectionError.ClockOutNotAfterClockIn => Bad("CLOCK_OUT_NOT_AFTER_CLOCK_IN", "Clock Out must be after Clock In."),
        CorrectionError.TimeInFuture => Bad("TIME_IN_FUTURE", "Times can't be in the future."),
        CorrectionError.OverlapsAnotherShift => ApiProblem.Create(409, "OVERLAPS_ANOTHER_SHIFT",
            "This would overlap another shift of the same employee."),
        CorrectionError.ShiftModified => ApiProblem.Create(409, "SHIFT_MODIFIED",
            "This shift was changed since you opened it.", "Reload it and try again."),
        CorrectionError.ShiftStillOpen => ApiProblem.Create(409, "SHIFT_STILL_OPEN",
            "This shift isn't finished yet.", "You can ask for a fix once you've clocked out."),
        CorrectionError.RequestAlreadyPending => ApiProblem.Create(409, "REQUEST_ALREADY_PENDING",
            "There's already a request waiting for this shift.", "Wait for your manager to approve or reject it."),
        CorrectionError.RequestAlreadyDecided => ApiProblem.Create(409, "REQUEST_ALREADY_DECIDED",
            "This request was already approved or rejected.", "Reload the list to see its current status."),
        _ => new StatusCodeResult(500),
    };

    private static ObjectResult Bad(string code, string title, string? detail = null) => ApiProblem.Create(400, code, title, detail);
}

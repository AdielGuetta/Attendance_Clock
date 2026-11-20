using AttendanceClock.Api.Auth;
using AttendanceClock.Api.Common;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.History;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceClock.Api.Admin;

/// <summary>Manager-only views and corrections (Plan 10). Employees get 403 Forbidden.</summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = nameof(EmployeeRole.Manager))]
public class AdminController(AdminService admin) : ControllerBase
{
    [HttpGet("employees")]
    public async Task<IActionResult> Employees(CancellationToken ct) => Ok(await admin.GetEmployeesAsync(ct));

    [HttpGet("on-shift")]
    public async Task<IActionResult> OnShift(CancellationToken ct) => Ok(await admin.GetOnShiftAsync(ct));

    [HttpGet("shifts")]
    public async Task<IActionResult> Shifts([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] int? employeeId, [FromQuery] bool flaggedOnly, CancellationToken ct)
    {
        if (from > to || to.DayNumber - from.DayNumber + 1 > HistoryService.MaxRangeDays)
            return ApiProblem.Create(400, "INVALID_RANGE", "Invalid date range.",
                $"'from' must be on or before 'to', and the range at most {HistoryService.MaxRangeDays} days.");
        return Ok(await admin.GetShiftsAsync(from, to, employeeId, flaggedOnly, ct));
    }

    [HttpGet("shifts/{id:long}/audit")]
    public async Task<IActionResult> Audit(long id, CancellationToken ct) =>
        await admin.GetAuditAsync(id, ct) is { } audit ? Ok(audit) : NotFound();

    [HttpPatch("shifts/{id:long}")]
    public async Task<IActionResult> Correct(long id, CorrectShiftRequest request, CancellationToken ct)
    {
        var result = await admin.CorrectAsync(id, User.GetEmployeeId(), request, ct);
        return result.Error == CorrectionError.None ? Ok(result.Shift) : CorrectionProblems.ToResult(result.Error);
    }
}

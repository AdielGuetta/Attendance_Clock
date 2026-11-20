using AttendanceClock.Api.Admin;
using AttendanceClock.Api.Auth;
using AttendanceClock.Api.Common;
using AttendanceClock.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceClock.Api.Requests;

/// <summary>Employee side of correction requests (Plan 13): ask for a fix, see my requests.</summary>
[ApiController]
[Authorize] // any logged-in employee (managers too, for their own shifts)
public class CorrectionRequestsController(CorrectionRequestService requests) : ControllerBase
{
    [HttpPost("api/shifts/{shiftId:long}/correction-requests")]
    public async Task<IActionResult> Create(long shiftId, CreateCorrectionRequest body, CancellationToken ct)
    {
        var result = await requests.CreateAsync(User.GetEmployeeId(), shiftId, body, ct);
        return result.Error == CorrectionError.None
            ? StatusCode(StatusCodes.Status201Created, result.Request)
            : CorrectionProblems.ToResult(result.Error);
    }

    [HttpGet("api/correction-requests/mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) => Ok(await requests.GetMineAsync(User.GetEmployeeId(), ct));
}

/// <summary>Manager side (Plan 13): list requests, approve (= an audited correction) or reject. Employees get 403.</summary>
[ApiController]
[Route("api/admin/correction-requests")]
[Authorize(Roles = nameof(EmployeeRole.Manager))]
public class AdminCorrectionRequestsController(CorrectionRequestService requests) : ControllerBase
{
    /// <param name="status">"Pending", "Approved" or "Rejected"; empty = all.</param>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        CorrectionRequestStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.GetNames<CorrectionRequestStatus>().Any(n => n.Equals(status, StringComparison.OrdinalIgnoreCase)))
                return ApiProblem.Create(400, "INVALID_STATUS", "Unknown status.", "Use Pending, Approved or Rejected.");
            filter = Enum.Parse<CorrectionRequestStatus>(status, ignoreCase: true);
        }
        return Ok(await requests.GetAllAsync(filter, ct));
    }

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, CancellationToken ct) =>
        Answer(await requests.ApproveAsync(id, User.GetEmployeeId(), ct));

    [HttpPost("{id:long}/reject")]
    public async Task<IActionResult> Reject(long id, CancellationToken ct) =>
        Answer(await requests.RejectAsync(id, User.GetEmployeeId(), ct));

    private IActionResult Answer(RequestResult result) =>
        result.Error == CorrectionError.None ? Ok(result.Request) : CorrectionProblems.ToResult(result.Error);
}

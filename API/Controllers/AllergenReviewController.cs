using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/admin/allergen-requests")]
public class AllergenReviewController(IUserAllergenService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<AllergenSubmissionResponse>> List([FromQuery] string? status, CancellationToken ct) => service.GetReviewQueueAsync(status, ct);

    [HttpPost("{id:int}/approve")]
    public Task<AllergenSubmissionResponse> Approve(int id, CancellationToken ct) => service.ReviewAsync(id, User.GetRequiredUserId(), true, null, ct);

    [HttpPost("{id:int}/reject")]
    public Task<AllergenSubmissionResponse> Reject(int id, RejectAllergenSubmissionRequest request, CancellationToken ct) => service.ReviewAsync(id, User.GetRequiredUserId(), false, request.Reason, ct);
}

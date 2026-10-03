using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController(IModerationService moderationService) : ControllerBase
{
    [HttpPost("posts/{postId:int}")] public async Task<ActionResult<ModerationFlagResponse>> ReportPost(int postId, ReportRequest request, CancellationToken ct) => Created($"/api/reports/posts/{postId}", await moderationService.ReportPostAsync(postId, User.GetRequiredUserId(), request, ct));
    [HttpPost("comments/{commentId:int}")] public async Task<ActionResult<ModerationFlagResponse>> ReportComment(int commentId, ReportRequest request, CancellationToken ct) => Created($"/api/reports/comments/{commentId}", await moderationService.ReportCommentAsync(commentId, User.GetRequiredUserId(), request, ct));
}

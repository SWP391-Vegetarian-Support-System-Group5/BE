using API.Infrastructure;
using BLL.DTOs;
using BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/posts")]
public class PostsController(IPostService postService) : ControllerBase
{
    [Authorize]
    [HttpPost] public async Task<ActionResult<PostResponse>> Create(CreatePostRequest request, CancellationToken ct) { var post = await postService.CreateAsync(User.GetRequiredUserId(), request, ct); return Created($"/api/posts/{post.PostId}", post); }
    [HttpGet] public Task<IReadOnlyCollection<PostResponse>> Get([FromQuery] string? keyword, [FromQuery] int? category, [FromQuery] string? postType, [FromQuery] int? tag, [FromQuery] string? status, CancellationToken ct) => postService.GetAsync(keyword, category, postType, tag, status, User.IsInRole("ADMIN"), ct);
    [HttpGet("{id:int}")] public Task<PostResponse> GetById(int id, CancellationToken ct) => postService.GetByIdAsync(id, User.Identity?.IsAuthenticated == true ? User.GetRequiredUserId() : null, User.Identity?.IsAuthenticated == true ? User.GetRoleName() : null, ct);
    [Authorize]
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, UpdatePostRequest request, CancellationToken ct) { await postService.UpdateAsync(id, User.GetRequiredUserId(), User.GetRoleName(), request, ct); return NoContent(); }
    [Authorize]
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await postService.DeleteAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct); return NoContent(); }
    [Authorize]
    [HttpPost("{postId:int}/comments")] public async Task<ActionResult<CommentResponse>> Comment(int postId, CommentRequest request, CancellationToken ct) => Created($"/api/posts/{postId}/comments", await postService.AddCommentAsync(postId, User.GetRequiredUserId(), null, request, ct));
    [HttpGet("{postId:int}/comments")] public Task<IReadOnlyCollection<CommentResponse>> GetComments(int postId, CancellationToken ct) => postService.GetCommentsAsync(postId, ct);
    [Authorize]
    [HttpPost("{postId:int}/rating")] public async Task<IActionResult> CreateRating(int postId, RatingRequest request, CancellationToken ct) { await postService.SetRatingAsync(postId, User.GetRequiredUserId(), request.Rating, false, ct); return Created($"/api/posts/{postId}/rating", null); }
    [Authorize]
    [HttpPut("{postId:int}/rating")] public async Task<IActionResult> UpdateRating(int postId, RatingRequest request, CancellationToken ct) { await postService.SetRatingAsync(postId, User.GetRequiredUserId(), request.Rating, true, ct); return NoContent(); }
    [Authorize]
    [HttpDelete("{postId:int}/rating")] public async Task<IActionResult> DeleteRating(int postId, CancellationToken ct) { await postService.DeleteRatingAsync(postId, User.GetRequiredUserId(), ct); return NoContent(); }
    [HttpGet("{postId:int}/rating")] public async Task<IActionResult> GetRating(int postId, CancellationToken ct) => Ok(new { averageRating = await postService.GetRatingAsync(postId, ct) });
    [Authorize]
    [HttpPost("{postId:int}/bookmark")] public async Task<IActionResult> Bookmark(int postId, CancellationToken ct) { await postService.AddBookmarkAsync(postId, User.GetRequiredUserId(), ct); return Created($"/api/posts/{postId}/bookmark", null); }
    [Authorize]
    [HttpDelete("{postId:int}/bookmark")] public async Task<IActionResult> DeleteBookmark(int postId, CancellationToken ct) { await postService.DeleteBookmarkAsync(postId, User.GetRequiredUserId(), ct); return NoContent(); }
}

[ApiController]
[Route("api/comments")]
public class CommentsController(IPostService postService) : ControllerBase
{
    [Authorize]
    [HttpPost("{id:int}/replies")] public async Task<ActionResult<CommentResponse>> Reply(int id, CommentRequest request, CancellationToken ct) => Created($"/api/comments/{id}/replies", await postService.AddReplyAsync(id, User.GetRequiredUserId(), request, ct));
    [Authorize]
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, CommentRequest request, CancellationToken ct) { await postService.UpdateCommentAsync(id, User.GetRequiredUserId(), User.GetRoleName(), request, ct); return NoContent(); }
    [Authorize]
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await postService.DeleteCommentAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct); return NoContent(); }
}

[ApiController]
[Route("api/recipes")]
public class RecipesController(IRecipeService recipeService) : ControllerBase
{
    [Authorize]
    [HttpPost] public async Task<ActionResult<RecipeResponse>> Create(CreateRecipeRequest request, CancellationToken ct) { var recipe = await recipeService.CreateAsync(User.GetRequiredUserId(), request, ct); return Created($"/api/recipes/{recipe.PostId}", recipe); }
    [HttpGet("search")] public Task<IReadOnlyCollection<RecipeResponse>> Search([FromQuery] string? keyword, [FromQuery] int? category, [FromQuery] int? tag, CancellationToken ct) => recipeService.SearchAsync(keyword, category, tag, ct);
    [HttpGet("{id:int}")] public Task<RecipeResponse> Get(int id, CancellationToken ct) => recipeService.GetAsync(id, ct);
    [Authorize]
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, CreateRecipeRequest request, CancellationToken ct) { await recipeService.UpdateAsync(id, User.GetRequiredUserId(), User.GetRoleName(), request, ct); return NoContent(); }
    [Authorize]
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await recipeService.DeleteAsync(id, User.GetRequiredUserId(), User.GetRoleName(), ct); return NoContent(); }
}

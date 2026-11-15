// Contract-only doubles; tests compile the real controller source.
using System.ComponentModel.DataAnnotations;
namespace BLL.DTOs
{
public record ChatMessageRequest([Required, MaxLength(1000)] string Content);
public record ChatMessageResponse(int ChatMessageId, string Sender, string Content, DateTime CreatedAt);
public record RelatedRecipeResponse(int RecipeId, string Title);
public record ChatReplyResponse(int? ChatMessageId, string? Answer, IReadOnlyCollection<RelatedRecipeResponse> RelatedRecipes, IReadOnlyCollection<string> Sources, int? RemainingGuestMessages, bool SignupRequired);
public record ChatSessionResponse(int ChatSessionId, int? UserId, DateTime CreatedAt, IReadOnlyCollection<ChatMessageResponse> Messages, string? GuestAccessToken = null);
}
namespace BLL.Services
{
using BLL.DTOs;
public interface IChatService
{
    Task<ChatSessionResponse> CreateSessionAsync(int? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ChatSessionResponse>> GetSessionsAsync(int? userId, CancellationToken cancellationToken = default);
    Task<ChatSessionResponse> GetSessionAsync(int id, int? userId, string? role, string? guestAccessToken, CancellationToken cancellationToken = default);
    Task<ChatReplyResponse> SendAsync(int id, int? userId, string? role, string? guestAccessToken, ChatMessageRequest request, CancellationToken cancellationToken = default);
}
}

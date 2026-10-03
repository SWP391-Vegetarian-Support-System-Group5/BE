using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ChatService(IRepository<ChatSession> sessions, IRepository<ChatMessage> messages, IAiChatService aiChatService) : IChatService
{
    private const int GuestMessageLimit = 5;

    public async Task<ChatSessionResponse> CreateSessionAsync(int? userId, CancellationToken cancellationToken = default)
    {
        var session = new ChatSession { UserId = userId }; await sessions.AddAsync(session, cancellationToken); await sessions.SaveChangesAsync(cancellationToken); return new(session.ChatSessionId, session.UserId, []);
    }
    public async Task<IReadOnlyCollection<ChatSessionResponse>> GetSessionsAsync(int? userId, CancellationToken cancellationToken = default)
    {
        if (!userId.HasValue) return [];
        var items = await sessions.Query().Include(x => x.Messages).Where(x => x.UserId == userId).OrderByDescending(x => x.ChatSessionId).ToListAsync(cancellationToken); return items.Select(ToResponse).ToList();
    }
    public async Task<ChatSessionResponse> GetSessionAsync(int id, int? userId, string? role, CancellationToken cancellationToken = default)
    {
        var session = await sessions.Query().Include(x => x.Messages).SingleOrDefaultAsync(x => x.ChatSessionId == id, cancellationToken) ?? throw new ServiceException("Chat session was not found.", 404);
        EnsureAccess(session, userId, role); return ToResponse(session);
    }
    public async Task<ChatMessageResponse> SendAsync(int id, int? userId, string? role, ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        var session = await sessions.Query().Include(x => x.Messages).SingleOrDefaultAsync(x => x.ChatSessionId == id, cancellationToken) ?? throw new ServiceException("Chat session was not found.", 404);
        EnsureAccess(session, userId, role);
        if (!session.UserId.HasValue && session.Messages.Count(x => x.Sender == "USER") >= GuestMessageLimit) throw new ServiceException("Guest chatbot trial limit has been reached.", 403);
        await messages.AddAsync(new ChatMessage { ChatSessionId = id, Sender = "USER", Content = request.Content.Trim() }, cancellationToken);
        var answer = await aiChatService.ReplyAsync(request.Content, cancellationToken);
        var response = new ChatMessage { ChatSessionId = id, Sender = "AI", Content = answer };
        await messages.AddAsync(response, cancellationToken); await messages.SaveChangesAsync(cancellationToken);
        return new(response.ChatMessageId, response.Sender, response.Content);
    }
    private static ChatSessionResponse ToResponse(ChatSession x) => new(x.ChatSessionId, x.UserId, x.Messages.OrderBy(m => m.ChatMessageId).Select(m => new ChatMessageResponse(m.ChatMessageId, m.Sender, m.Content)).ToList());
    private static void EnsureAccess(ChatSession session, int? userId, string? role) { if (role == "ADMIN") return; if (session.UserId != userId) throw new ServiceException("You do not have permission to access this chat session.", 403); }
}

public class MockAiChatService : IAiChatService
{
    public Task<string> ReplyAsync(string message, CancellationToken cancellationToken = default)
    {
        var prompt = message.ToLowerInvariant();
        var reply = prompt.Contains("bmi") || prompt.Contains("calorie") ? "A balanced vegetarian plan starts with your BMI, BMR, activity level, and health goal. Generate a meal plan for personalized calorie targets." : prompt.Contains("substitut") ? "For vegetarian cooking, try tofu or tempeh for protein, nutritional yeast for a cheesy flavor, and flaxseed plus water as an egg substitute." : "I can help with vegetarian nutrition, ingredient substitutions, calorie questions, and recipe ideas. Please share your goal or ingredients.";
        return Task.FromResult(reply);
    }
}

public class MockIngredientRecognitionService : IIngredientRecognitionService
{
    public Task<IReadOnlyCollection<IngredientRecognitionResponse>> RecognizeAsync(Stream image, string? fileName, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<IngredientRecognitionResponse>>([new("Tomato", 0.92m, "Looks fresh", ["Tomato tofu stir-fry", "Vegetable salad"]), new("Tofu", 0.88m, "Looks fresh", ["Tofu vegetable bowl"]) ]);
}

public class MockVideoRecipeSummaryService : IVideoRecipeSummaryService
{
    public Task<VideoSummaryResponse> SummarizeAsync(string videoUrl, CancellationToken cancellationToken = default) => Task.FromResult(new VideoSummaryResponse("Mock vegetarian recipe summary", ["Tofu", "Vegetables", "Soy sauce"], ["Prepare ingredients", "Cook vegetables", "Add tofu and season", "Serve warm"], 25));
}

public class MockModerationAiService : IModerationAiService
{
    private static readonly string[] FlaggedTerms = ["hate", "violence", "spam", "scam"];
    public Task<string?> FlagReasonAsync(string content, CancellationToken cancellationToken = default) => Task.FromResult(FlaggedTerms.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)) ? "Mock AI flagged potentially unsafe content for admin review." : null);
}

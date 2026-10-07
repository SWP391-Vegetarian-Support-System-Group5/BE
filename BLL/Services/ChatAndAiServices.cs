using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BLL.Services;

public class NutritionChatbotService(
    IRepository<ChatSession> sessions,
    IRepository<ChatMessage> messages,
    IRepository<User> users,
    IRepository<Recipe> recipes,
    IAiChatService aiChatService,
    IKnowledgeBaseService knowledgeBaseService) : IChatService
{
    private const int GuestMessageLimit = 5;

    public async Task<ChatSessionResponse> CreateSessionAsync(int? userId, CancellationToken cancellationToken = default)
    {
        var guestToken = userId.HasValue ? null : CreateGuestToken();
        var session = new ChatSession
        {
            UserId = userId,
            GuestAccessTokenHash = guestToken is null ? null : HashToken(guestToken),
            CreatedAt = VietnamTime.Now
        };
        await sessions.AddAsync(session, cancellationToken);
        await sessions.SaveChangesAsync(cancellationToken);
        return new(session.ChatSessionId, session.UserId, session.CreatedAt, [], guestToken);
    }
    public async Task<IReadOnlyCollection<ChatSessionResponse>> GetSessionsAsync(int? userId, CancellationToken cancellationToken = default)
    {
        if (!userId.HasValue) return [];
        var items = await sessions.Query().Include(x => x.Messages).Where(x => x.UserId == userId).OrderByDescending(x => x.ChatSessionId).ToListAsync(cancellationToken); return items.Select(ToResponse).ToList();
    }
    public async Task<ChatSessionResponse> GetSessionAsync(int id, int? userId, string? role, string? guestAccessToken, CancellationToken cancellationToken = default)
    {
        var session = await sessions.Query().Include(x => x.Messages).SingleOrDefaultAsync(x => x.ChatSessionId == id, cancellationToken) ?? throw new ServiceException("Chat session was not found.", 404);
        EnsureAccess(session, userId, role, guestAccessToken); return ToResponse(session);
    }
    public async Task<ChatReplyResponse> SendAsync(int id, int? userId, string? role, string? guestAccessToken, ChatMessageRequest request, CancellationToken cancellationToken = default)
    {
        var session = await sessions.Query().Include(x => x.Messages).SingleOrDefaultAsync(x => x.ChatSessionId == id, cancellationToken) ?? throw new ServiceException("Chat session was not found.", 404);
        EnsureAccess(session, userId, role, guestAccessToken);
        var guestMessageCount = session.Messages.Count(x => x.Sender == "USER");
        if (!session.UserId.HasValue && guestMessageCount >= GuestMessageLimit)
            return new(null, null, [], [], 0, true);

        var userMessage = new ChatMessage { ChatSessionId = id, Sender = "USER", Content = request.Content.Trim(), CreatedAt = VietnamTime.Now };
        await messages.AddAsync(userMessage, cancellationToken);
        await messages.SaveChangesAsync(cancellationToken);

        var relatedRecipes = await GetRelatedRecipesAsync(userId, request.Content, cancellationToken);
        var knowledge = knowledgeBaseService.FindRelevant(request.Content);
        var context = await BuildApplicationContextAsync(userId, session.Messages, relatedRecipes, cancellationToken);
        context += $"\n\nRETRIEVED KNOWLEDGE:\n{knowledge.Content}";
        var chatbotReply = await aiChatService.ReplyAsync(new(request.Content.Trim(), context), cancellationToken);
        var response = new ChatMessage { ChatSessionId = id, Sender = "AI", Content = chatbotReply.Answer, CreatedAt = VietnamTime.Now };
        await messages.AddAsync(response, cancellationToken);
        await messages.SaveChangesAsync(cancellationToken);

        int? remainingMessages = session.UserId.HasValue ? null : GuestMessageLimit - guestMessageCount - 1;
        return new(response.ChatMessageId, chatbotReply.Answer, relatedRecipes, knowledge.Sources, remainingMessages, false);
    }
    private async Task<IReadOnlyCollection<RelatedRecipeResponse>> GetRelatedRecipesAsync(int? userId, string message, CancellationToken cancellationToken)
    {
        var user = userId.HasValue
            ? await users.Query().Include(x => x.Profile).Include(x => x.UserAllergens).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            : null;
        var items = await recipes.Query()
            .Include(x => x.Post)
            .Include(x => x.Ingredients)
            .Include(x => x.DietCompatibilities)
            .Where(x => x.Post.Status == "PUBLISHED")
            .ToListAsync(cancellationToken);
        var allergens = user?.UserAllergens.Select(x => x.AllergenId).ToHashSet() ?? [];
        var terms = message.Split([' ', ',', '.', '?', '!', ':', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var dietTypeId = user?.Profile?.DietTypeId;
        return items
            .Where(x => VegetarianDietRules.IsVerifiedVegetarianRecipe(x.Ingredients.Select(i => i.DietaryGroup)))
            .Where(x => !x.Ingredients.Any(i => i.AllergenId.HasValue && allergens.Contains(i.AllergenId.Value)))
            .Where(x => !dietTypeId.HasValue || x.DietCompatibilities.Any(d => d.DietTypeId == dietTypeId.Value && d.IsCompatible))
            .OrderByDescending(x => terms.Count(term => x.Post.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Ingredients.Any(i => i.IngredientName.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .ThenByDescending(x => x.Post.PostId)
            .Take(5)
            .Select(x => new RelatedRecipeResponse(x.PostId, x.Post.Title))
            .ToList();
    }
    private async Task<string> BuildApplicationContextAsync(int? userId, IEnumerable<ChatMessage> previousMessages, IReadOnlyCollection<RelatedRecipeResponse> relatedRecipes, CancellationToken cancellationToken)
    {
        var user = userId.HasValue
            ? await users.Query().Include(x => x.Profile).ThenInclude(x => x.DietType).Include(x => x.UserAllergens).ThenInclude(x => x.Allergen).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            : null;
        var profile = user is null ? "Guest user. No diet or allergen profile is available." : $"Diet type: {user.Profile?.DietType?.Name ?? "Not specified"}. Allergens: {string.Join(", ", user.UserAllergens.Select(x => x.Allergen.Name))}.";
        var recipeContext = relatedRecipes.Count == 0 ? "No matching recipes were found." : string.Join("\n", relatedRecipes.Select(x => $"- RecipeId {x.RecipeId}: {x.Title}"));
        var history = string.Join("\n", previousMessages.OrderByDescending(x => x.ChatMessageId).Take(10).Reverse().Select(x => $"{x.Sender}: {x.Content}"));
        return $"USER PROFILE:\n{profile}\n\nSAFE RECIPE SUGGESTIONS:\n{recipeContext}\n\nRECENT CONVERSATION:\n{history}";
    }
    private static ChatSessionResponse ToResponse(ChatSession x) => new(x.ChatSessionId, x.UserId, x.CreatedAt, x.Messages.OrderBy(m => m.ChatMessageId).Select(m => new ChatMessageResponse(m.ChatMessageId, m.Sender, m.Content, m.CreatedAt)).ToList());
    private static void EnsureAccess(ChatSession session, int? userId, string? role, string? guestAccessToken)
    {
        if (role == "ADMIN") return;
        if (session.UserId.HasValue && session.UserId == userId) return;
        if (!session.UserId.HasValue && !string.IsNullOrWhiteSpace(guestAccessToken) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(session.GuestAccessTokenHash ?? string.Empty), Convert.FromHexString(HashToken(guestAccessToken)))) return;
        throw new ServiceException("You do not have permission to access this chat session.", 403);
    }
    private static string CreateGuestToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class GeminiChatbotSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gemini-3.5-flash-lite";
}

public class GeminiChatbotService(HttpClient httpClient, GeminiChatbotSettings settings) : IAiChatService
{
    private const string Instructions = "You are a warm, practical Vietnamese vegetarian cooking assistant. Answer in Vietnamese unless the user writes in another language. Answer conversationally and directly: begin with the answer or recipe, not a disclaimer, summary of the user profile, or description of your limitations. You can use general cooking knowledge for vegetarian recipes, ingredient substitutions, vegetarian diets, and food allergies. The supplied application context and retrieved knowledge are helpful facts, not the only material you may use. Mention the user's diet type or allergies only when the question, a requested ingredient, or your recommended ingredients make them relevant. Allergies are strict safety restrictions: never recommend an ingredient or recipe containing a listed allergen, even when the user asks for it. In that case, briefly explain the conflict and offer safe alternatives. For recipe requests, give concrete ingredients and numbered steps when useful. Do not invent recipe IDs, claim an ingredient exists in a system recipe when it does not, invent medical facts, diagnose illness, or provide treatment. Do not calculate BMI, TDEE, calories, or diet targets. For severe allergy questions, advise checking labels and consulting a qualified professional.";

    public async Task<ChatbotReply> ReplyAsync(ChatbotRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new ServiceException("The nutrition chatbot is not configured. Set Gemini:ApiKey before using it.", 503);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{settings.Model}:generateContent");
        message.Headers.Add("x-goog-api-key", settings.ApiKey);
        message.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = Instructions } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = $"APPLICATION CONTEXT:\n{request.ApplicationContext}\n\nUSER QUESTION:\n{request.Message}" } } } }
        });
        using var response = await httpClient.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new ServiceException("The nutrition chatbot could not answer right now.", 503, [body]);

        using var document = JsonDocument.Parse(body);
        var answer = document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(answer)) throw new ServiceException("The nutrition chatbot returned an empty answer.", 503);
        return new(answer, []);
    }
}

public class LocalKnowledgeBaseService(string knowledgeBaseDirectory) : IKnowledgeBaseService
{
    public KnowledgeBaseMatch FindRelevant(string question)
    {
        if (!Directory.Exists(knowledgeBaseDirectory)) return new("No local knowledge base is available.", []);
        var terms = Regex.Matches(question.ToLowerInvariant(), "[\\p{L}\\p{N}]{3,}").Select(x => x.Value).Distinct().ToHashSet();
        var matches = Directory.EnumerateFiles(knowledgeBaseDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .SelectMany(path => Regex.Split(File.ReadAllText(path), "\\r?\\n\\r?\\n").Where(chunk => !string.IsNullOrWhiteSpace(chunk)).Select(chunk => new { File = Path.GetFileName(path), Chunk = chunk, Score = terms.Count(term => chunk.Contains(term, StringComparison.OrdinalIgnoreCase)) }))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(3).ToList();
        return matches.Count == 0
            ? new("No directly relevant knowledge-base text was found.", [])
            : new(string.Join("\n\n", matches.Select(x => x.Chunk)), matches.Select(x => x.File).Distinct().ToArray());
    }
}

public class MockIngredientRecognitionService : IIngredientRecognitionService
{
    public Task<IReadOnlyCollection<IngredientRecognitionResponse>> RecognizeAsync(Stream image, string? fileName, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<IngredientRecognitionResponse>>([new("Tomato", 0.92m, "Looks fresh", ["Tomato tofu stir-fry", "Vegetable salad"]), new("Tofu", 0.88m, "Looks fresh", ["Tofu vegetable bowl"]) ]);
}

public class MockModerationAiService : IModerationAiService
{
    private static readonly string[] FlaggedTerms = ["hate", "violence", "spam", "scam"];
    public Task<string?> FlagReasonAsync(string content, CancellationToken cancellationToken = default) => Task.FromResult(FlaggedTerms.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)) ? "Mock AI flagged potentially unsafe content for admin review." : null);
}

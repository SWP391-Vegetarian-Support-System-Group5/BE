using System.Net.Http.Json;
using System.Text.Json;
using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public sealed class VideoRecipeSettings
{
    public string StorageDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads", "videos");
    public long MaximumFileSizeBytes { get; init; } = 100 * 1024 * 1024;
}

public interface IVideoRecipeAnalyzer
{
    Task<GeneratedVideoRecipe> AnalyzeAsync(string videoPath, string contentType, CancellationToken cancellationToken = default);
}

public sealed record GeneratedVideoRecipe(string Title, string Description, string Transcript, int? EstimatedPrepMinutes, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, string GeminiFileName);

public sealed class LocalVideoStorage(VideoRecipeSettings settings)
{
    public async Task<string> SaveAsync(Stream video, string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.StorageDirectory);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        await using var destination = File.Create(Path.Combine(settings.StorageDirectory, storedName));
        await video.CopyToAsync(destination, cancellationToken);
        return $"/uploads/videos/{storedName}";
    }

    public string GetPath(string videoUrl) => Path.Combine(settings.StorageDirectory, Path.GetFileName(videoUrl));

    public void Delete(string videoUrl)
    {
        var path = GetPath(videoUrl);
        if (File.Exists(path)) File.Delete(path);
    }
}

public sealed class GeminiVideoRecipeAnalyzer(HttpClient httpClient, GeminiChatbotSettings settings) : IVideoRecipeAnalyzer
{
    private const string RecipePrompt = "Analyze this cooking video. Return only valid JSON. Write Vietnamese text. Create a concise recipe from what is explicitly shown or said. Do not invent ingredients or quantities. Classify every ingredient as exactly one of PLANT, DAIRY, EGG, HONEY. If the video contains meat, poultry, fish, shellfish, animal broth, gelatin, or an unclear non-vegetarian ingredient, return it in warnings and do not include it as a recipe ingredient. transcript must capture the spoken cooking instructions. title, description, transcript, ingredients, and steps are required.";

    public async Task<GeneratedVideoRecipe> AnalyzeAsync(string videoPath, string contentType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) throw new ServiceException("Video AI is not configured. Set Gemini:ApiKey before uploading a video.", 503);
        var uploaded = await UploadAsync(videoPath, contentType, cancellationToken);
        try
        {
            var file = await WaitUntilActiveAsync(uploaded.Name, cancellationToken);
            var generated = await GenerateAsync(file.Uri, file.MimeType, cancellationToken);
            return generated with { GeminiFileName = uploaded.Name };
        }
        finally
        {
            await DeleteGeminiFileAsync(uploaded.Name, cancellationToken);
        }
    }

    private async Task<GeminiFile> UploadAsync(string videoPath, string contentType, CancellationToken ct)
    {
        var length = new FileInfo(videoPath).Length;
        using var start = new HttpRequestMessage(HttpMethod.Post, "upload/v1beta/files");
        start.Headers.Add("x-goog-api-key", settings.ApiKey);
        start.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        start.Headers.Add("X-Goog-Upload-Command", "start");
        start.Headers.Add("X-Goog-Upload-Header-Content-Length", length.ToString());
        start.Headers.Add("X-Goog-Upload-Header-Content-Type", contentType);
        start.Content = JsonContent.Create(new { file = new { display_name = Path.GetFileName(videoPath) } });
        using var startResponse = await httpClient.SendAsync(start, ct);
        var startBody = await startResponse.Content.ReadAsStringAsync(ct);
        if (!startResponse.IsSuccessStatusCode) throw new ServiceException("Gemini could not start the video upload.", 503, [startBody]);
        var uploadUrl = startResponse.Headers.TryGetValues("X-Goog-Upload-URL", out var urls) ? urls.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(uploadUrl)) throw new ServiceException("Gemini did not return an upload URL.", 503);

        await using var stream = File.OpenRead(videoPath);
        using var upload = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        upload.Headers.Add("X-Goog-Upload-Offset", "0");
        upload.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        upload.Content = new StreamContent(stream);
        upload.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var uploadResponse = await httpClient.SendAsync(upload, ct);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(ct);
        if (!uploadResponse.IsSuccessStatusCode) throw new ServiceException("Gemini could not upload the video.", 503, [uploadBody]);
        return ReadFile(uploadBody);
    }

    private async Task<GeminiFile> WaitUntilActiveAsync(string name, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"v1beta/{name}");
            request.Headers.Add("x-goog-api-key", settings.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new ServiceException("Gemini could not process the uploaded video.", 503, [body]);
            var file = ReadFile(body);
            if (string.Equals(file.State, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return file;
            if (string.Equals(file.State, "FAILED", StringComparison.OrdinalIgnoreCase)) throw new ServiceException("Gemini could not process this video.", 503, [body]);
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        throw new ServiceException("Gemini took too long to process the video. Please try a shorter video.", 503);
    }

    private async Task<GeneratedVideoRecipe> GenerateAsync(string fileUri, string mimeType, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1beta/models/{settings.Model}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            contents = new[] { new { parts = new object[] { new { file_data = new { mime_type = mimeType, file_uri = fileUri } }, new { text = RecipePrompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        title = new { type = "STRING" }, description = new { type = "STRING" }, transcript = new { type = "STRING" }, estimatedPrepMinutes = new { type = "INTEGER", nullable = true },
                        ingredients = new { type = "ARRAY", items = new { type = "OBJECT", properties = new { ingredientName = new { type = "STRING" }, amount = new { type = "STRING", nullable = true }, dietaryGroup = new { type = "STRING" } }, required = new[] { "ingredientName", "dietaryGroup" } } },
                        steps = new { type = "ARRAY", items = new { type = "OBJECT", properties = new { stepNumber = new { type = "INTEGER" }, instruction = new { type = "STRING" } }, required = new[] { "stepNumber", "instruction" } } }
                    },
                    required = new[] { "title", "description", "transcript", "ingredients", "steps" }
                }
            }
        });
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new ServiceException("Gemini could not summarize this video.", 503, [body]);
        using var outer = JsonDocument.Parse(body);
        var text = outer.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text)) throw new ServiceException("Gemini returned an empty video summary.", 503);
        using var recipe = JsonDocument.Parse(text);
        var root = recipe.RootElement;
        var ingredients = root.GetProperty("ingredients").EnumerateArray().Select(x => new IngredientRequest(x.GetProperty("ingredientName").GetString() ?? string.Empty, x.TryGetProperty("amount", out var amount) && amount.ValueKind != JsonValueKind.Null ? amount.GetString() : null, VegetarianDietRules.NormalizeIngredientGroup(x.GetProperty("dietaryGroup").GetString() ?? string.Empty), null)).ToList();
        var steps = root.GetProperty("steps").EnumerateArray().Select((x, index) => new RecipeStepRequest(x.TryGetProperty("stepNumber", out var number) ? number.GetInt32() : index + 1, x.GetProperty("instruction").GetString() ?? string.Empty)).ToList();
        if (ingredients.Count == 0 || steps.Count == 0) throw new ServiceException("Gemini did not identify enough recipe details. Please use a clearer cooking video.", 422);
        return new(root.GetProperty("title").GetString() ?? "Video recipe", root.GetProperty("description").GetString() ?? string.Empty, root.GetProperty("transcript").GetString() ?? string.Empty, root.TryGetProperty("estimatedPrepMinutes", out var minutes) && minutes.ValueKind == JsonValueKind.Number ? minutes.GetInt32() : null, ingredients, steps, string.Empty);
    }

    private async Task DeleteGeminiFileAsync(string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v1beta/{name}");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        using var _ = await httpClient.SendAsync(request, ct);
    }

    private static GeminiFile ReadFile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var file = document.RootElement.TryGetProperty("file", out var nested) ? nested : document.RootElement;
        var name = GetRequiredString(file, "name");
        var uri = GetRequiredString(file, "uri");
        var mimeType = GetOptionalString(file, "mimeType", "mime_type") ?? "video/mp4";
        var state = GetOptionalString(file, "state") ?? "PROCESSING";
        return new GeminiFile(name, uri, mimeType, state);
    }

    private static string GetRequiredString(JsonElement element, params string[] propertyNames) =>
        GetOptionalString(element, propertyNames) ?? throw new ServiceException($"Gemini file response is missing '{propertyNames[0]}'.", 503, [element.GetRawText()]);

    private static string? GetOptionalString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
                return value.GetString();
        return null;
    }

    private sealed record GeminiFile(string Name, string Uri, string MimeType, string State);
}

public sealed class VideoRecipeDraftService(
    IRepository<VideoRecipeDraft> drafts,
    IRepository<VideoRecipeDraftIngredient> ingredients,
    IRepository<VideoRecipeDraftStep> steps,
    LocalVideoStorage storage,
    IVideoRecipeAnalyzer analyzer,
    IRecipeService recipes,
    Microsoft.Extensions.Logging.ILogger<VideoRecipeDraftService> logger) : IVideoRecipeDraftService
{
    public async Task<VideoRecipeDraftResponse> CreateAsync(int userId, Stream video, string fileName, string contentType, CancellationToken ct = default)
    {
        var videoUrl = await storage.SaveAsync(video, fileName, ct);
        var draft = new VideoRecipeDraft { UserId = userId, VideoUrl = videoUrl, Status = "PENDING", CreatedAt = VietnamTime.Now, UpdatedAt = VietnamTime.Now };
        await drafts.AddAsync(draft, ct); await drafts.SaveChangesAsync(ct);
        return ToResponse(draft);
    }

    public async Task<VideoRecipeDraftResponse> GetAsync(int id, int userId, string role, CancellationToken ct = default) => ToResponse(await GetOwnedDraftAsync(id, userId, role, ct));

    public async Task<VideoRecipeDraftResponse> UpdateAsync(int id, int userId, string role, UpdateVideoRecipeDraftRequest request, CancellationToken ct = default)
    {
        var draft = await GetOwnedDraftAsync(id, userId, role, ct);
        if (draft.Status != "READY") throw new ServiceException("Only a ready video draft can be edited.", 409);
        if (request.Ingredients.Count == 0 || request.Steps.Count == 0 || request.Steps.GroupBy(x => x.StepNumber).Any(x => x.Count() > 1)) throw new ServiceException("Provide at least one ingredient, one step, and unique step numbers.");
        foreach (var item in draft.Ingredients) ingredients.Remove(item);
        foreach (var item in draft.Steps) steps.Remove(item);
        draft.Title = request.Title.Trim(); draft.Description = request.Description.Trim(); draft.Transcript = request.Transcript?.Trim(); draft.EstimatedPrepMinutes = request.EstimatedPrepMinutes; draft.UpdatedAt = VietnamTime.Now;
        foreach (var item in request.Ingredients) await ingredients.AddAsync(new VideoRecipeDraftIngredient { VideoRecipeDraftId = id, IngredientName = item.IngredientName.Trim(), Amount = item.Amount?.Trim(), DietaryGroup = VegetarianDietRules.NormalizeIngredientGroup(item.DietaryGroup), AllergenId = item.AllergenId }, ct);
        foreach (var item in request.Steps) await steps.AddAsync(new VideoRecipeDraftStep { VideoRecipeDraftId = id, StepNumber = item.StepNumber, Instruction = item.Instruction.Trim() }, ct);
        await drafts.SaveChangesAsync(ct);
        return await GetAsync(id, userId, role, ct);
    }

    public async Task<VideoRecipeDraftResponse> RetryAsync(int id, int userId, string role, CancellationToken ct = default)
    {
        var draft = await GetOwnedDraftAsync(id, userId, role, ct);
        if (draft.Status != "FAILED") throw new ServiceException("Only a failed video draft can be retried.", 409);
        if (!File.Exists(storage.GetPath(draft.VideoUrl))) throw new ServiceException("The original video file is no longer available.", 409);
        draft.Status = "PENDING"; draft.ErrorMessage = null; draft.UpdatedAt = VietnamTime.Now;
        await drafts.SaveChangesAsync(ct);
        return ToResponse(draft);
    }

    public async Task<RecipeResponse> PublishAsync(int id, int userId, string role, PublishVideoRecipeDraftRequest request, CancellationToken ct = default)
    {
        var draft = await GetOwnedDraftAsync(id, userId, role, ct);
        if (draft.Status != "READY") throw new ServiceException("Only a ready video draft can be published.", 409);
        var recipe = await recipes.CreateAsync(draft.UserId, new CreateRecipeRequest(draft.Title ?? "Video recipe", draft.Description ?? string.Empty, request.CategoryId, draft.VideoUrl, draft.EstimatedPrepMinutes, null, null, draft.Ingredients.Select(x => new IngredientRequest(x.IngredientName, x.Amount, x.DietaryGroup, x.AllergenId)).ToList(), draft.Steps.OrderBy(x => x.StepNumber).Select(x => new RecipeStepRequest(x.StepNumber, x.Instruction)).ToList(), request.TagIds), ct);
        draft.Status = "PUBLISHED"; draft.UpdatedAt = VietnamTime.Now; await drafts.SaveChangesAsync(ct);
        return recipe;
    }

    public async Task DeleteAsync(int id, int userId, string role, CancellationToken ct = default)
    {
        var draft = await GetOwnedDraftAsync(id, userId, role, ct);
        if (draft.Status == "PUBLISHED") throw new ServiceException("A published draft keeps its video as part of the recipe.", 409);
        storage.Delete(draft.VideoUrl); drafts.Remove(draft); await drafts.SaveChangesAsync(ct);
    }

    public async Task ProcessPendingAsync(CancellationToken ct = default)
    {
        var pending = await drafts.Query().Where(x => x.Status == "PENDING").OrderBy(x => x.VideoRecipeDraftId).Take(3).ToListAsync(ct);
        foreach (var draft in pending)
        {
            draft.Status = "PROCESSING"; draft.UpdatedAt = VietnamTime.Now; await drafts.SaveChangesAsync(ct);
            try
            {
                var generated = await analyzer.AnalyzeAsync(storage.GetPath(draft.VideoUrl), GetContentType(draft.VideoUrl), ct);
                draft.GeminiFileName = generated.GeminiFileName; draft.Title = generated.Title; draft.Description = generated.Description; draft.Transcript = generated.Transcript; draft.EstimatedPrepMinutes = generated.EstimatedPrepMinutes; draft.Status = "READY"; draft.UpdatedAt = VietnamTime.Now;
                foreach (var ingredient in generated.Ingredients) await ingredients.AddAsync(new VideoRecipeDraftIngredient { VideoRecipeDraftId = draft.VideoRecipeDraftId, IngredientName = ingredient.IngredientName, Amount = ingredient.Amount, DietaryGroup = ingredient.DietaryGroup, AllergenId = null }, ct);
                foreach (var step in generated.Steps) await steps.AddAsync(new VideoRecipeDraftStep { VideoRecipeDraftId = draft.VideoRecipeDraftId, StepNumber = step.StepNumber, Instruction = step.Instruction }, ct);
                await drafts.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Video recipe draft {DraftId} failed during Gemini processing.", draft.VideoRecipeDraftId);
                draft.Status = "FAILED"; draft.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message; draft.UpdatedAt = VietnamTime.Now; await drafts.SaveChangesAsync(ct);
            }
        }
    }

    private async Task<VideoRecipeDraft> GetOwnedDraftAsync(int id, int userId, string role, CancellationToken ct) =>
        await drafts.Query().Include(x => x.Ingredients).Include(x => x.Steps).SingleOrDefaultAsync(x => x.VideoRecipeDraftId == id, ct) is { } draft
            ? role == "ADMIN" || draft.UserId == userId ? draft : throw new ServiceException("You do not have permission to access this video draft.", 403)
            : throw new ServiceException("Video recipe draft was not found.", 404);

    private static string GetContentType(string videoUrl) => Path.GetExtension(videoUrl).ToLowerInvariant() switch { ".mp4" => "video/mp4", ".mov" => "video/quicktime", ".webm" => "video/webm", ".avi" => "video/avi", _ => "application/octet-stream" };
    private static VideoRecipeDraftResponse ToResponse(VideoRecipeDraft x) => new(x.VideoRecipeDraftId, x.Status, x.VideoUrl, x.Title, x.Description, x.Transcript, x.EstimatedPrepMinutes, x.Ingredients.OrderBy(i => i.VideoRecipeDraftIngredientId).Select(i => new IngredientRequest(i.IngredientName, i.Amount, i.DietaryGroup, i.AllergenId)).ToList(), x.Steps.OrderBy(s => s.StepNumber).Select(s => new RecipeStepRequest(s.StepNumber, s.Instruction)).ToList(), x.ErrorMessage, x.CreatedAt, x.UpdatedAt);
}

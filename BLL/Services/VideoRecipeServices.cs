using System.Net.Http.Json;
using System.Text.Json;
using BLL.Common;
using BLL.DTOs;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

public sealed class VideoRecipeSettings
{
    public string StorageDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, ".local", "video-processing");
    public long MaximumFileSizeBytes { get; init; } = 100 * 1024 * 1024;
}

public sealed class CloudinarySettings
{
    public string CloudName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string VideoFolder { get; init; } = "veggiemate/videos";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiSecret);
}

public interface IVideoRecipeAnalyzer
{
    Task<GeneratedVideoRecipe> AnalyzeAsync(string videoPath, string contentType, CancellationToken cancellationToken = default);
}

public sealed record GeneratedVideoRecipe(string Title, string Description, string Transcript, int? EstimatedPrepMinutes, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, string GeminiFileName);

public sealed class LocalVideoStorage(VideoRecipeSettings settings, CloudinarySettings cloudinarySettings)
{
    private static readonly HttpClient DownloadClient = new();
    private readonly Cloudinary? cloudinary = cloudinarySettings.IsConfigured
        ? new Cloudinary(new Account(cloudinarySettings.CloudName, cloudinarySettings.ApiKey, cloudinarySettings.ApiSecret))
        : null;

    public async Task<string> SaveAsync(Stream video, string fileName, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.StorageDirectory);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var publicId = Guid.NewGuid().ToString("N");
        var storedName = $"{publicId}{extension}";
        var localPath = Path.Combine(settings.StorageDirectory, storedName);
        await using (var destination = File.Create(localPath))
        {
            await video.CopyToAsync(destination, cancellationToken);
        }

        if (cloudinary is null)
            throw new ServiceException("Cloudinary is not configured. Set Cloudinary:CloudName, Cloudinary:ApiKey, and Cloudinary:ApiSecret before uploading videos.", 503);

        var upload = await cloudinary.UploadAsync(new VideoUploadParams
        {
            File = new FileDescription(localPath),
            Folder = cloudinarySettings.VideoFolder,
            PublicId = publicId,
            Overwrite = false,
            UseFilename = false,
            UniqueFilename = false
        });

        if (upload.Error is not null)
            throw new ServiceException("Cloudinary could not upload the video.", 503, [upload.Error.Message]);

        if (upload.SecureUrl is null)
            throw new ServiceException("Cloudinary did not return a secure video URL.", 503);

        return upload.SecureUrl.ToString();
    }

    public string GetPath(string videoUrl) => Path.Combine(settings.StorageDirectory, GetStoredFileName(videoUrl));

    public async Task<string> PrepareForProcessingAsync(string videoUrl, CancellationToken cancellationToken)
    {
        var path = GetPath(videoUrl);
        if (File.Exists(path)) return path;

        if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri))
            throw new ServiceException("The original video file is no longer available.", 409);

        Directory.CreateDirectory(settings.StorageDirectory);
        await using var source = await DownloadClient.GetStreamAsync(uri, cancellationToken);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination, cancellationToken);
        return path;
    }

    public void Delete(string videoUrl)
    {
        var path = GetPath(videoUrl);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string GetStoredFileName(string videoUrl) =>
        Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : Path.GetFileName(videoUrl);
}

public sealed class GeminiVideoRecipeAnalyzer(HttpClient httpClient, GeminiChatbotSettings settings) : IVideoRecipeAnalyzer
{
    private const string RecipePrompt = "Analyze this cooking video. Return only valid JSON. Write Vietnamese text. Create a concise recipe from what is explicitly shown or said. Do not invent ingredients or quantities. Classify every ingredient as exactly one of PLANT, DAIRY, EGG, HONEY. If the video contains meat, poultry, fish, shellfish, animal broth, gelatin, or an unclear non-vegetarian ingredient, return it in warnings and do not include it as a recipe ingredient. Keep transcript as a short spoken-instruction summary only, maximum 3 sentences. title, description, ingredients, and steps are required.";

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
        using var start = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "upload/v1beta/files");
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
        using var upload = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, uploadUrl);
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
        for (var attempt = 0; attempt < 180; attempt++)
        {
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"v1beta/{name}");
            request.Headers.Add("x-goog-api-key", settings.ApiKey);
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw new ServiceException("Gemini could not process the uploaded video.", 503, [body]);
            var file = ReadFile(body);
            if (string.Equals(file.State, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return file;
            if (string.Equals(file.State, "FAILED", StringComparison.OrdinalIgnoreCase)) throw new ServiceException("Gemini could not process this video.", 503, [body]);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        throw new ServiceException("Gemini took too long to process the video. Please try a shorter video.", 503);
    }

    private async Task<GeneratedVideoRecipe> GenerateAsync(string fileUri, string mimeType, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"v1beta/models/{settings.Model}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.ApiKey);
        request.Content = JsonContent.Create(new
        {
            contents = new[] { new { parts = new object[] { new { file_data = new { mime_type = mimeType, file_uri = fileUri } }, new { text = RecipePrompt } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                maxOutputTokens = 1600,
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        title = new { type = "STRING" }, description = new { type = "STRING" }, transcript = new { type = "STRING" }, estimatedPrepMinutes = new { type = "INTEGER", nullable = true },
                        ingredients = new { type = "ARRAY", items = new { type = "OBJECT", properties = new { ingredientName = new { type = "STRING" }, amount = new { type = "STRING", nullable = true }, dietaryGroup = new { type = "STRING" } }, required = new[] { "ingredientName", "dietaryGroup" } } },
                        steps = new { type = "ARRAY", items = new { type = "OBJECT", properties = new { stepNumber = new { type = "INTEGER" }, instruction = new { type = "STRING" } }, required = new[] { "stepNumber", "instruction" } } }
                    },
                    required = new[] { "title", "description", "ingredients", "steps" }
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
        var transcript = root.TryGetProperty("transcript", out var transcriptElement) && transcriptElement.ValueKind == JsonValueKind.String
            ? transcriptElement.GetString() ?? string.Empty
            : string.Empty;
        return new(root.GetProperty("title").GetString() ?? "Video recipe", root.GetProperty("description").GetString() ?? string.Empty, transcript, root.TryGetProperty("estimatedPrepMinutes", out var minutes) && minutes.ValueKind == JsonValueKind.Number ? minutes.GetInt32() : null, ingredients, steps, string.Empty);
    }

    private async Task DeleteGeminiFileAsync(string name, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Delete, $"v1beta/{name}");
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
    IRepository<Category> categories,
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
        ValidateDraftDetails(request.Title, request.Description, request.EstimatedPrepMinutes, request.Servings, request.CaloriesPerServing, request.Ingredients, request.Steps);
        await ValidateDraftCategoryAsync(request.CategoryId, ct);
        foreach (var item in draft.Ingredients) ingredients.Remove(item);
        foreach (var item in draft.Steps) steps.Remove(item);
        draft.Title = request.Title.Trim(); draft.Description = request.Description.Trim(); draft.Transcript = request.Transcript?.Trim(); draft.EstimatedPrepMinutes = request.EstimatedPrepMinutes; draft.Servings = request.Servings; draft.CategoryId = request.CategoryId; draft.CaloriesPerServing = request.CaloriesPerServing; draft.UpdatedAt = VietnamTime.Now;
        foreach (var item in request.Ingredients) await ingredients.AddAsync(new VideoRecipeDraftIngredient { VideoRecipeDraftId = id, IngredientName = item.IngredientName.Trim(), Amount = item.Amount?.Trim(), DietaryGroup = VegetarianDietRules.NormalizeIngredientGroup(item.DietaryGroup), AllergenId = item.AllergenId }, ct);
        foreach (var item in request.Steps) await steps.AddAsync(new VideoRecipeDraftStep { VideoRecipeDraftId = id, StepNumber = item.StepNumber, Instruction = item.Instruction.Trim() }, ct);
        await drafts.SaveChangesAsync(ct);
        return await GetAsync(id, userId, role, ct);
    }

    public async Task<VideoRecipeDraftResponse> RetryAsync(int id, int userId, string role, CancellationToken ct = default)
    {
        var draft = await GetOwnedDraftAsync(id, userId, role, ct);
        if (draft.Status != "FAILED") throw new ServiceException("Only a failed video draft can be retried.", 409);
        await storage.PrepareForProcessingAsync(draft.VideoUrl, ct);
        draft.Status = "PENDING"; draft.ErrorMessage = null; draft.UpdatedAt = VietnamTime.Now;
        await drafts.SaveChangesAsync(ct);
        return ToResponse(draft);
    }

    public async Task<RecipeResponse> PublishAsync(int id, int userId, string role, PublishVideoRecipeDraftRequest request, CancellationToken ct = default)
    {
        var draft = await GetOwnedDraftAsync(id, userId, role, ct);
        if (draft.Status != "READY") throw new ServiceException("Only a ready video draft can be published.", 409);
        var categoryId = request.CategoryId ?? draft.CategoryId;
        var recipe = await recipes.CreateAsync(draft.UserId, new CreateRecipeRequest(draft.Title ?? "Video recipe", draft.Description ?? string.Empty, categoryId, draft.VideoUrl, draft.EstimatedPrepMinutes, null, draft.Servings, draft.CaloriesPerServing, draft.Ingredients.Select(x => new IngredientRequest(x.IngredientName, x.Amount, x.DietaryGroup, x.AllergenId)).ToList(), draft.Steps.OrderBy(x => x.StepNumber).Select(x => new RecipeStepRequest(x.StepNumber, x.Instruction)).ToList(), request.TagIds), ct);
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
                var videoPath = await storage.PrepareForProcessingAsync(draft.VideoUrl, ct);
                var generated = await analyzer.AnalyzeAsync(videoPath, GetContentType(draft.VideoUrl), ct);
                draft.GeminiFileName = generated.GeminiFileName; draft.Title = generated.Title; draft.Description = generated.Description; draft.Transcript = generated.Transcript; draft.EstimatedPrepMinutes = generated.EstimatedPrepMinutes; draft.Status = "READY"; draft.UpdatedAt = VietnamTime.Now;
                foreach (var ingredient in generated.Ingredients) await ingredients.AddAsync(new VideoRecipeDraftIngredient { VideoRecipeDraftId = draft.VideoRecipeDraftId, IngredientName = ingredient.IngredientName, Amount = ingredient.Amount, DietaryGroup = ingredient.DietaryGroup, AllergenId = null }, ct);
                foreach (var step in generated.Steps) await steps.AddAsync(new VideoRecipeDraftStep { VideoRecipeDraftId = draft.VideoRecipeDraftId, StepNumber = step.StepNumber, Instruction = step.Instruction }, ct);
                await drafts.SaveChangesAsync(ct);
                storage.Delete(draft.VideoUrl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Video recipe draft {DraftId} failed during Gemini processing.", draft.VideoRecipeDraftId);
                draft.Status = "FAILED"; draft.ErrorMessage = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message; draft.UpdatedAt = VietnamTime.Now; await drafts.SaveChangesAsync(ct);
            }
        }
    }

    public async Task<VideoRecipeDraftResponse> CreateManualAsync(
    int userId,
    Stream videoStream,
    string fileName,
    string contentType,
    CreateManualVideoRecipeDraftRequest request,
    CancellationToken cancellationToken = default)
    {
        ValidateManualDraftRequest(request);
        await ValidateDraftCategoryAsync(request.CategoryId, cancellationToken);

        var videoUrl = await storage.SaveAsync(videoStream, fileName, cancellationToken);
        var draft = new VideoRecipeDraft
        {
            UserId = userId,
            VideoUrl = videoUrl,
            Status = "READY",
            //Source = "MANUAL",
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Transcript = request.Transcript?.Trim(),
            EstimatedPrepMinutes = request.EstimatedPrepMinutes,
            Servings = request.Servings,
            CategoryId = request.CategoryId,
            CaloriesPerServing = request.CaloriesPerServing,
            CreatedAt = VietnamTime.Now,
            UpdatedAt = VietnamTime.Now
        };

        await drafts.AddAsync(draft, cancellationToken);
        await drafts.SaveChangesAsync(cancellationToken);

        foreach (var item in request.Ingredients)
        {
            await ingredients.AddAsync(new VideoRecipeDraftIngredient
            {
                VideoRecipeDraftId = draft.VideoRecipeDraftId,
                IngredientName = item.IngredientName.Trim(),
                Amount = item.Amount?.Trim(),
                DietaryGroup = VegetarianDietRules.NormalizeIngredientGroup(item.DietaryGroup),
                AllergenId = item.AllergenId
            }, cancellationToken);
        }

        foreach (var item in request.Steps)
        {
            await steps.AddAsync(new VideoRecipeDraftStep
            {
                VideoRecipeDraftId = draft.VideoRecipeDraftId,
                StepNumber = item.StepNumber,
                Instruction = item.Instruction.Trim()
            }, cancellationToken);
        }

        await drafts.SaveChangesAsync(cancellationToken);
        storage.Delete(draft.VideoUrl);

        return await GetAsync(draft.VideoRecipeDraftId, userId, "USER", cancellationToken);
    }

    private static void ValidateManualDraftRequest(CreateManualVideoRecipeDraftRequest request)
    {
        ValidateDraftDetails(request.Title, request.Description, request.EstimatedPrepMinutes, request.Servings, request.CaloriesPerServing, request.Ingredients, request.Steps);
    }

    private static void ValidateDraftDetails(string? title, string? description, int? estimatedPrepMinutes, decimal? servings, decimal? caloriesPerServing, List<IngredientRequest>? ingredients, List<RecipeStepRequest>? steps)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
            errors.Add("Title is required.");
        if (string.IsNullOrWhiteSpace(description))
            errors.Add("Description is required.");
        if (estimatedPrepMinutes is <= 0)
            errors.Add("EstimatedPrepMinutes must be greater than zero.");
        if (servings is <= 0 or > 1000)
            errors.Add("Servings must be greater than zero and no more than 1000.");
        if (caloriesPerServing is <= 0)
            errors.Add("CaloriesPerServing must be greater than zero.");

        if (ingredients is not { Count: > 0 })
        {
            errors.Add("Vui lòng cung cấp ít nhất 1 nguyên liệu.");
        }
        else
        {
            for (var index = 0; index < ingredients.Count; index++)
            {
                var ingredient = ingredients[index];
                if (string.IsNullOrWhiteSpace(ingredient.IngredientName))
                    errors.Add($"Ingredients[{index}].IngredientName is required.");
                if (string.IsNullOrWhiteSpace(ingredient.DietaryGroup))
                    errors.Add($"Ingredients[{index}].DietaryGroup is required.");
            }
        }

        if (steps is not { Count: > 0 })
        {
            errors.Add("Vui lòng cung cấp ít nhất 1 bước thực hiện.");
        }
        else
        {
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                if (step.StepNumber <= 0)
                    errors.Add($"Steps[{index}].StepNumber must be greater than zero.");
                if (string.IsNullOrWhiteSpace(step.Instruction))
                    errors.Add($"Steps[{index}].Instruction is required.");
            }

            if (steps.GroupBy(x => x.StepNumber).Any(x => x.Count() > 1))
                errors.Add("Step numbers must be unique.");
        }

        if (errors.Count > 0)
            throw new ServiceException("Video recipe draft request is invalid.", 400, errors);
    }

    private async Task ValidateDraftCategoryAsync(int? categoryId, CancellationToken ct)
    {
        if (categoryId.HasValue && !await categories.Query().AnyAsync(x => x.CategoryId == categoryId.Value, ct))
            throw new ServiceException("Category was not found.", 404);
    }

    private async Task<VideoRecipeDraft> GetOwnedDraftAsync(int id, int userId, string role, CancellationToken ct) =>
        await drafts.Query().Include(x => x.Ingredients).Include(x => x.Steps).SingleOrDefaultAsync(x => x.VideoRecipeDraftId == id, ct) is { } draft
            ? role == "ADMIN" || draft.UserId == userId ? draft : throw new ServiceException("You do not have permission to access this video draft.", 403)
            : throw new ServiceException("Video recipe draft was not found.", 404);

    private static string GetContentType(string videoUrl) => Path.GetExtension(videoUrl).ToLowerInvariant() switch { ".mp4" => "video/mp4", ".mov" => "video/quicktime", ".webm" => "video/webm", ".avi" => "video/avi", _ => "application/octet-stream" };
    private static VideoRecipeDraftResponse ToResponse(VideoRecipeDraft x) => new(x.VideoRecipeDraftId, x.Status, x.VideoUrl, x.Title, x.Description, x.Transcript, x.EstimatedPrepMinutes, x.Servings, x.CategoryId, x.CaloriesPerServing, x.Ingredients.OrderBy(i => i.VideoRecipeDraftIngredientId).Select(i => new IngredientRequest(i.IngredientName, i.Amount, i.DietaryGroup, i.AllergenId)).ToList(), x.Steps.OrderBy(s => s.StepNumber).Select(s => new RecipeStepRequest(s.StepNumber, s.Instruction)).ToList(), x.ErrorMessage, x.CreatedAt, x.UpdatedAt);
}

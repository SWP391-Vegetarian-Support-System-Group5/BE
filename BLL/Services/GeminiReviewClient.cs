using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BLL.Services;

// Shared, bounded retry policy for short AI decisions. Never log the API key or user content.
internal static class GeminiReviewClient
{
    internal sealed class Failure(string message, int statusCode = 503) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

    public static async Task<JsonDocument> CompleteAsync(HttpClient client, GeminiChatbotSettings settings,
        string instructions, string input, int maxOutputTokens, ILogger? logger, CancellationToken cancellationToken)
    {
        var primary = string.IsNullOrWhiteSpace(settings.ChatModel) ? settings.Model : settings.ChatModel;
        var fallback = settings.Model;
        var models = string.Equals(primary, fallback, StringComparison.OrdinalIgnoreCase)
            ? new[] { primary, primary, primary }
            : new[] { primary, fallback, primary };

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        Failure lastFailure = new("AI đang bận hoặc không phản hồi. Dữ liệu chưa được lưu; vui lòng thử lại sau.");
        for (var attempt = 0; attempt < models.Length; attempt++)
        {
            if (attempt > 0)
            {
                var delay = TimeSpan.FromMilliseconds((1 << (attempt - 1)) * 350 + Random.Shared.Next(0, 250));
                try { await Task.Delay(delay, deadline.Token); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { break; }
            }
            using var oneAttempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            oneAttempt.CancelAfter(TimeSpan.FromSeconds(10));
            var model = models[attempt];
            using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent");
            request.Headers.Add("x-goog-api-key", settings.ApiKey);
            request.Content = JsonContent.Create(new
            {
                systemInstruction = new { parts = new[] { new { text = instructions } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = input } } } },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    maxOutputTokens,
                    thinkingConfig = new { thinkingLevel = "minimal" }
                }
            });
            try
            {
                using var response = await client.SendAsync(request, oneAttempt.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Gemini review failed: model={Model}, attempt={Attempt}, status={Status}", model, attempt + 1, (int)response.StatusCode);
                    if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                        throw new Failure("Cấu hình hoặc quyền truy cập AI không hợp lệ. Vui lòng liên hệ quản trị viên.");
                    lastFailure = response.StatusCode == HttpStatusCode.TooManyRequests
                        ? new Failure("AI đã đạt giới hạn yêu cầu. Vui lòng thử lại sau.", 429)
                        : new Failure("AI đang quá tải hoặc tạm không phản hồi. Vui lòng thử lại sau.");
                    continue;
                }
                var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(oneAttempt.Token));
                if (result.RootElement.TryGetProperty("promptFeedback", out var feedback) &&
                    feedback.TryGetProperty("blockReason", out _))
                {
                    result.Dispose();
                    throw new Failure("Nội dung bị bộ lọc an toàn của AI từ chối. Vui lòng chỉnh sửa nội dung.", 422);
                }
                if (result.RootElement.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0 &&
                    candidates[0].TryGetProperty("finishReason", out var finish) &&
                    finish.GetString() is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST")
                {
                    result.Dispose();
                    throw new Failure("Nội dung bị bộ lọc an toàn của AI từ chối. Vui lòng chỉnh sửa nội dung.", 422);
                }
                if (!result.RootElement.TryGetProperty("candidates", out candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0 ||
                    !candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) ||
                    parts.ValueKind != JsonValueKind.Array || !parts.EnumerateArray().Any(part =>
                        part.TryGetProperty("text", out var value) && !string.IsNullOrWhiteSpace(value.GetString())))
                {
                    result.Dispose();
                    logger?.LogWarning("Gemini review returned no text: model={Model}, attempt={Attempt}", model, attempt + 1);
                    lastFailure = new Failure("AI không trả lời đầy đủ. Vui lòng thử lại sau.");
                    continue;
                }
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Failure) { throw; }
            catch (OperationCanceledException)
            {
                logger?.LogWarning("Gemini review timed out: model={Model}, attempt={Attempt}", model, attempt + 1);
                lastFailure = new Failure("AI không phản hồi kịp thời. Vui lòng thử lại sau.");
            }
            catch (HttpRequestException error)
            {
                logger?.LogWarning("Gemini review transport error: model={Model}, attempt={Attempt}, type={Type}", model, attempt + 1, error.GetType().Name);
                lastFailure = new Failure("Không kết nối được dịch vụ AI. Vui lòng thử lại sau.");
            }
            catch (JsonException)
            {
                logger?.LogWarning("Gemini review returned invalid JSON: model={Model}, attempt={Attempt}", model, attempt + 1);
                lastFailure = new Failure("AI trả về dữ liệu không hợp lệ. Vui lòng thử lại sau.");
            }
        }
        throw lastFailure;
    }
}

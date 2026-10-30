using System.Net;
using System.Text;
using System.Text.Json;
using BLL.Common;
using BLL.Services;
using DAL.Entities;

static class GeminiReviewTests
{
    public static async Task RunAsync()
    {
        var models = new List<string>();
        var responses = new Queue<HttpStatusCode>([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        using var client = new HttpClient(new StubHandler(request =>
        {
            models.Add(request.RequestUri!.AbsolutePath);
            var status = responses.Dequeue();
            return Reply(status, new { allowed = true, reason = "" });
        })) { BaseAddress = new Uri("https://example.invalid/v1beta/") };
        var settings = new GeminiChatbotSettings { ApiKey = "test", ChatModel = "gemini-3.1-flash-lite", Model = "gemini-3.5-flash-lite" };
        var moderation = new GeminiModerationAiService(client, settings);
        Check(await moderation.FlagReasonAsync("Tên món: Bún nấm") is null, "503 retries through the fallback model");
        Check(models.Count == 2 && models[0].Contains("3.1") && models[1].Contains("3.5"), "fallback uses a distinct model");
        Check((await moderation.FlagReasonAsync("Công thức này đúng là đồ ngu, đéo ăn được"))?.Contains("xúc phạm") == true && models.Count == 2,
            "obvious abuse remains blocked even while the provider is unavailable");

        var allergyResponses = new Queue<HttpStatusCode>([HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        using var allergyClient = new HttpClient(new StubHandler(_ => Reply(allergyResponses.Dequeue(),
            new { allowed = true, name = "Dừa", duplicateOf = "", reason = "" }))) { BaseAddress = new Uri("https://example.invalid/v1beta/") };
        var validator = new GeminiAllergenNameValidator(allergyClient, settings);
        Check((await validator.ValidateAsync("Dừa", [new Allergen { Name = "Trứng" }])).Name == "Dừa",
            "custom allergy is accepted after a transient 503");

        var safetyJson = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "SAFETY" } } });
        using var safetyClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(safetyJson, Encoding.UTF8, "application/json") })) { BaseAddress = new Uri("https://example.invalid/v1beta/") };
        var safetyValidator = new GeminiAllergenNameValidator(safetyClient, settings);
        try { await safetyValidator.ValidateAsync("bầu trời", []); throw new Exception("Expected AI safety refusal"); }
        catch (ServiceException error) when (error.StatusCode == 422)
        { Check(error.Message.Contains("bộ lọc an toàn"), "AI safety block is explained instead of reported as outage"); }

        var unauthorizedCalls = 0;
        using var unauthorizedClient = new HttpClient(new StubHandler(_ =>
        { unauthorizedCalls++; return Reply(HttpStatusCode.Forbidden, new { allowed = true }); })) { BaseAddress = new Uri("https://example.invalid/v1beta/") };
        var unauthorized = new GeminiAllergenNameValidator(unauthorizedClient, settings);
        try { await unauthorized.ValidateAsync("Dừa", []); throw new Exception("Expected configuration failure"); }
        catch (ServiceException error) when (error.StatusCode == 503)
        { Check(unauthorizedCalls == 1 && error.Message.Contains("quyền truy cập"), "403 stops without pointless retries"); }
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, object decision)
    {
        var part = new { text = JsonSerializer.Serialize(decision) };
        var candidate = new { content = new { parts = new[] { part } } };
        var body = JsonSerializer.Serialize(new { candidates = new[] { candidate } });
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS Gemini review " + label);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(reply(request));
    }
}

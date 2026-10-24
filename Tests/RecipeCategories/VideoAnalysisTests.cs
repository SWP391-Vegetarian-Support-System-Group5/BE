using System.Net;
using System.Text;
using System.Text.Json;
using BLL.Common;
using BLL.Services;

internal static class VideoAnalysisTests
{
    public static async Task RunAsync()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), $"veggiemate-video-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDirectory);
        try
        {
            var storage = new LocalVideoStorage(new VideoRecipeSettings { StorageDirectory = cacheDirectory }, new CloudinarySettings());
            await using (var source = new MemoryStream([1, 2, 3]))
            {
                var stagedName = await storage.StageAsync(source, "short.mp4", CancellationToken.None);
                if (LocalVideoStorage.IsCloudVideo(stagedName) ||
                    await storage.PrepareForProcessingAsync(stagedName, CancellationToken.None) != storage.GetPath(stagedName))
                    throw new Exception("Small video was not staged locally before cloud upload.");
                storage.Delete(stagedName);
            }
            Console.WriteLine("PASS small video is staged without waiting for Cloudinary");
            const string cloudUrl = "https://res.cloudinary.com/test/video/upload/veggiemate/videos/cached.mp4";
            var cachedPath = storage.GetPath(cloudUrl);
            await File.WriteAllBytesAsync(cachedPath, [1, 2, 3]);
            if (await storage.PrepareForProcessingAsync(cloudUrl, CancellationToken.None) != cachedPath)
                throw new Exception("Video processing did not use the local upload cache.");
            storage.Delete(cloudUrl);
            if (File.Exists(cachedPath)) throw new Exception("The local upload cache was not cleaned up.");
            Console.WriteLine("PASS video processing uses and cleans local upload cache");
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }

        var videoPath = Path.Combine(Path.GetTempPath(), $"veggiemate-video-{Guid.NewGuid():N}.mp4");
        var video = new byte[] { 0, 1, 2, 3 };
        await File.WriteAllBytesAsync(videoPath, video);
        try
        {
            var requests = 0;
            using var client = new HttpClient(new FakeHandler(async request =>
            {
                requests++;
                if (!request.RequestUri!.AbsolutePath.EndsWith(":generateContent", StringComparison.Ordinal))
                    throw new Exception("Small video unexpectedly used the Files API.");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var media = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0];
                var inline = media.GetProperty("inline_data");
                if (inline.GetProperty("mime_type").GetString() != "video/mp4" ||
                    inline.GetProperty("data").GetString() != Convert.ToBase64String(video))
                    throw new Exception("The inline video payload is incorrect.");
                const string recipe = "{\"title\":\"Test\",\"description\":\"Test\",\"transcript\":\"hello\",\"ingredients\":[{\"ingredientName\":\"tofu\",\"dietaryGroup\":\"PLANT\"}],\"nonVegetarianIngredients\":[],\"steps\":[{\"stepNumber\":1,\"instruction\":\"cook\"}]}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = recipe } } } } } }), Encoding.UTF8, "application/json")
                };
            })) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
            var analyzer = new GeminiVideoRecipeAnalyzer(client, new GeminiChatbotSettings { ApiKey = "test-key" });
            var result = await analyzer.AnalyzeAsync(videoPath, "video/mp4");
            if (requests != 1 || result.Transcript != "hello" || result.Ingredients.Count != 1 || result.GeminiFileName != string.Empty)
                throw new Exception("Small video analysis returned an unexpected result.");
            Console.WriteLine("PASS small video uses one inline Gemini request");

            const string dairyAndEggRecipe = "{\"title\":\"Test\",\"description\":\"Test\",\"transcript\":\"milk and egg\",\"ingredients\":[{\"ingredientName\":\"milk\",\"dietaryGroup\":\"DAIRY\"},{\"ingredientName\":\"egg\",\"dietaryGroup\":\"EGG\"}],\"nonVegetarianIngredients\":[],\"steps\":[{\"stepNumber\":1,\"instruction\":\"cook\"}]}";
            using var dairyClient = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = dairyAndEggRecipe } } } } } }), Encoding.UTF8, "application/json")
            }))) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
            var dairyResult = await new GeminiVideoRecipeAnalyzer(dairyClient, new GeminiChatbotSettings { ApiKey = "test-key" }).AnalyzeAsync(videoPath, "video/mp4");
            var dairyDiets = VegetarianDietRules.GetCompatibleDietTypes(dairyResult.Ingredients.Select(x => x.DietaryGroup));
            if (dairyDiets.Contains("VEGAN") || !dairyDiets.Contains("LACTO_OVO_VEGETARIAN"))
                throw new Exception("Dairy and egg must be allowed in a vegetarian draft without receiving a vegan label.");
            Console.WriteLine("PASS dairy and egg remain vegetarian but are not labeled vegan");

            foreach (var (name, recipeJson) in new[]
            {
                ("explicit non-vegetarian list", "{\"title\":\"Test\",\"description\":\"Test\",\"transcript\":\"thit heo\",\"ingredients\":[{\"ingredientName\":\"tofu\",\"dietaryGroup\":\"PLANT\"}],\"nonVegetarianIngredients\":[\"thit heo\"],\"steps\":[{\"stepNumber\":1,\"instruction\":\"cook\"}]}"),
                ("non-vegetarian ingredient group", "{\"title\":\"Test\",\"description\":\"Test\",\"transcript\":\"thit heo\",\"ingredients\":[{\"ingredientName\":\"thit heo\",\"dietaryGroup\":\"MEAT\"}],\"nonVegetarianIngredients\":[],\"steps\":[{\"stepNumber\":1,\"instruction\":\"cook\"}]}"),
                ("missing safety verdict", "{\"title\":\"Test\",\"description\":\"Test\",\"transcript\":\"hello\",\"ingredients\":[{\"ingredientName\":\"tofu\",\"dietaryGroup\":\"PLANT\"}],\"steps\":[{\"stepNumber\":1,\"instruction\":\"cook\"}]}"),
            })
            {
                using var rejectedClient = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = recipeJson } } } } } }), Encoding.UTF8, "application/json")
                }))) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/") };
                var rejectedAnalyzer = new GeminiVideoRecipeAnalyzer(rejectedClient, new GeminiChatbotSettings { ApiKey = "test-key" });
                try { await rejectedAnalyzer.AnalyzeAsync(videoPath, "video/mp4"); throw new Exception($"Video with {name} was accepted."); }
                catch (ServiceException ex) when (ex.StatusCode == 422) { Console.WriteLine($"PASS {name} blocks video draft"); }
            }
        }
        finally
        {
            File.Delete(videoPath);
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}

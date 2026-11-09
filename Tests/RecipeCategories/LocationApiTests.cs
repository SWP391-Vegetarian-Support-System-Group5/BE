using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

static class LocationApiTests
{
    public static async Task RunAsync(HttpClient client, string token)
    {
        var checks = 0;
        async Task<JsonElement> Send(string method, string path, int expected, object? body = null, bool authenticated = true)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != expected)
                throw new Exception($"{method} {path}: expected {expected}, got {(int)response.StatusCode}: {text}");
            checks++;
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        const string address = "Thành phố Hồ Chí Minh";
        var result = await Send("PUT", "/api/users/me/location", 200, new { latitude = 10.7769m, longitude = 106.7009m, livingAddress = address });
        Check(result.GetProperty("latitude").GetDecimal() == 10.7769m && result.GetProperty("longitude").GetDecimal() == 106.7009m && result.GetProperty("livingAddress").GetString() == address, "Saved location returned correctly");
        var reloaded = await Send("GET", "/api/users/me", 200);
        Check(reloaded.GetProperty("livingAddress").GetString() == address, "Address survives a fresh HTTP request");
        await Send("PUT", "/api/users/me/location", 200, new { latitude = 0m, longitude = 0m });
        reloaded = await Send("GET", "/api/users/me", 200);
        Check(reloaded.GetProperty("latitude").GetDecimal() == 0m && reloaded.GetProperty("longitude").GetDecimal() == 0m && reloaded.GetProperty("livingAddress").GetString() == address, "Zero coordinates accepted and omitted address preserved");
        await Send("PUT", "/api/users/me/location", 400, new { latitude = 91m, longitude = 0m });
        await Send("PUT", "/api/users/me/location", 400, new { latitude = 0m, longitude = -181m });
        await Send("PUT", "/api/users/me/location", 400, new { longitude = 0m });
        await Send("PUT", "/api/users/me/location", 400, new { latitude = 0m });
        await Send("PUT", "/api/users/me/location", 400, new { latitude = 0m, longitude = 0m, livingAddress = new string('x', 301) });
        reloaded = await Send("GET", "/api/users/me", 200);
        Check(reloaded.GetProperty("latitude").GetDecimal() == 0m && reloaded.GetProperty("livingAddress").GetString() == address, "Invalid requests leave saved location unchanged");
        await Send("PUT", "/api/users/me/location", 401, new { latitude = 0m, longitude = 0m }, authenticated: false);
        Console.WriteLine($"LOCATION API PASS: {checks} HTTP/state checks; isolated database, zero live writes.");
    }
}

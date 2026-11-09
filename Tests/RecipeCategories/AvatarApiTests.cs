using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

static class AvatarApiTests
{
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a7xkAAAAASUVORK5CYII=");
    public static async Task RunAsync(HttpClient client, string user, string otherUser)
    {
        var checks = 0;
        async Task<JsonElement> Send(string method, string path, int expected, string? token, object? body = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != expected) throw new Exception($"{method} {path}: expected {expected}, got {(int)response.StatusCode}: {text}");
            checks++;
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
        async Task<JsonElement> Upload(byte[] bytes, string mime, int expected, string? token, string field = "file")
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me/avatar");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(mime);
            form.Add(file, field, "avatar.png");
            form.Add(new StringContent("1"), "userId"); // Cannot override the JWT's owner.
            request.Content = form;
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != expected) throw new Exception($"Avatar upload: expected {expected}, got {(int)response.StatusCode}: {text}");
            checks++;
            return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        await Upload(Png, "image/png", 401, null);
        await Send("DELETE", "/api/users/me/avatar", 401, null);
        await Upload(Png, "image/png", 400, user, "image");
        await Upload([1, 2, 3], "image/png", 415, user);
        await Upload(Png, "image/jpeg", 415, user);
        await Upload([], "image/png", 415, user);
        await Upload(new byte[5 * 1024 * 1024 + 1], "image/png", 413, user);
        var uploaded = await Upload(Png, "image/png", 200, user);
        var url = uploaded.GetProperty("imageUrl").GetString();
        Check(url?.Contains("/avatars/2/") == true && !uploaded.GetProperty("isGenerated").GetBoolean() && uploaded.GetProperty("previousImageDeleted").GetBoolean(), "FE mutation response matches contract and uses the signed-in owner");
        var profile = await Send("GET", "/api/users/me", 200, user);
        Check(profile.GetProperty("avatarUrl").GetString() == url, "Avatar survives reloading the profile");
        var other = await Send("GET", "/api/users/me", 200, otherUser);
        Check(other.GetProperty("avatarUrl").ValueKind == JsonValueKind.Null, "Another account's avatar is unchanged");
        var login = await Send("POST", "/api/auth/login", 200, null, new { email = "user2@example.invalid", password = "Test@123" });
        Check(login.GetProperty("user").GetProperty("avatarUrl").GetString() == url, "Login returns the saved avatar");
        profile = await Send("PUT", "/api/users/me/profile", 200, user, new { fullName = "Avatar test user" });
        Check(profile.GetProperty("avatarUrl").GetString() == url, "Saving health profile preserves the avatar");
        profile = await Send("PUT", "/api/users/me/location", 200, user, new { latitude = 10.77m, longitude = 106.7m });
        Check(profile.GetProperty("avatarUrl").GetString() == url, "Saving location preserves the avatar");
        uploaded = await Upload(Png, "image/png", 200, user);
        Check(uploaded.GetProperty("imageUrl").GetString() != url && uploaded.GetProperty("previousImageDeleted").GetBoolean(), "Replacement uses a new asset and cleans the previous one");
        var deleted = await Send("DELETE", "/api/users/me/avatar", 200, user);
        Check(deleted.GetProperty("imageUrl").ValueKind == JsonValueKind.Null && deleted.GetProperty("previousImageDeleted").GetBoolean(), "Delete clears the avatar and reports cleanup");
        profile = await Send("GET", "/api/users/me", 200, user);
        Check(profile.GetProperty("avatarUrl").ValueKind == JsonValueKind.Null, "Deleted avatar remains absent after reload");
        await Send("DELETE", "/api/users/me/avatar", 200, user);
        Console.WriteLine($"AVATAR API PASS: {checks} HTTP/state checks; isolated database and storage.");
    }
}

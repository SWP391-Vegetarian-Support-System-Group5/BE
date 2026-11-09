using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

static class AllergenReviewApiTests
{
    public static async Task RunAsync(HttpClient client, string admin, string user)
    {
        var checks = 0;
        async Task<JsonElement> Send(string method, string path, int expected, object? body = null, string? token = null)
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
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        foreach (var (method, path, body) in new (string, string, object?)[] {
            ("GET", "/api/admin/allergen-requests", null),
            ("POST", "/api/admin/allergen-requests/1/approve", null),
            ("POST", "/api/admin/allergen-requests/1/reject", new { reason = "Invalid" }) })
        {
            await Send(method, path, 401, body);
            await Send(method, path, 403, body, user);
        }
        await Send("GET", "/api/users/me/allergen-requests", 401);
        var egg = await Send("POST", "/api/admin/allergens", 201, new { name = "Trứng" }, admin);
        var eggId = egg.GetProperty("id").GetInt32();
        await Send("POST", "/api/admin/allergens", 409, new { name = "  EGG  " }, admin);
        await Send("POST", "/api/users/me/allergens/custom", 409, new { name = "  EGG  " }, user);
        var catalog = await Send("GET", "/api/allergens", 200, token: user);
        Check(catalog.GetArrayLength() == 1, "Admin catalog reaches user");
        await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = new[] { eggId, eggId } }, user);
        var selected = await Send("GET", "/api/users/me/allergens", 200, token: user);
        Check(selected.GetProperty("allergens").GetArrayLength() == 1, "Selection persists across a fresh HTTP request and deduplicates IDs");
        await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = Array.Empty<int>() }, user);
        var submitted = await Send("POST", "/api/users/me/allergens/custom", 200, new { name = "Hạt chia" }, user);
        var id = submitted.GetProperty("id").GetInt32();
        Check(submitted.GetProperty("status").GetString() == "PENDING", "New request waits for admin");
        await Send("POST", "/api/users/me/allergens/custom", 409, new { name = " HẠT   CHIA " }, user);
        await Send("PUT", "/api/users/me/allergens", 404, new { allergenIds = new[] { 99999 } }, user);
        Check((await Send("GET", "/api/allergens", 200, token: user)).GetArrayLength() == 1, "Pending request is not selectable");
        Check((await Send("GET", "/api/users/me/allergen-requests", 200, token: user)).GetArrayLength() == 1, "User can reload own request");
        Check((await Send("GET", "/api/users/me/allergen-requests", 200, token: admin)).GetArrayLength() == 0, "Other user cannot see request history");
        Check((await Send("GET", "/api/admin/allergen-requests", 200, token: admin)).GetArrayLength() == 1, "Admin default queue contains pending only");
        await Send("GET", "/api/admin/allergen-requests?status=INVALID", 400, token: admin);
        await Send("POST", $"/api/admin/allergen-requests/{id}/reject", 400, new { reason = "   " }, admin);
        await Send("POST", $"/api/admin/allergen-requests/{id}/reject", 400, new { }, admin);
        await Send("POST", $"/api/admin/allergen-requests/{id}/reject", 400, new { reason = new string('x', 1001) }, admin);
        const string reason = "Tên chưa đủ thông tin để xác minh.";
        var rejected = await Send("POST", $"/api/admin/allergen-requests/{id}/reject", 200, new { reason }, admin);
        Check(rejected.GetProperty("status").GetString() == "REJECTED" && rejected.GetProperty("rejectionReason").GetString() == reason, "Reject records the reason");
        Check((await Send("GET", "/api/admin/allergen-requests", 200, token: admin)).GetArrayLength() == 0, "Rejected request disappears from pending queue");
        Check((await Send("GET", "/api/admin/allergen-requests?status=REJECTED", 200, token: admin)).GetArrayLength() == 1, "Decision retained for audit");
        var history = await Send("GET", "/api/users/me/allergen-requests", 200, token: user);
        Check(history[0].GetProperty("rejectionReason").GetString() == reason, "User sees reason after reload");
        await Send("POST", $"/api/admin/allergen-requests/{id}/approve", 409, token: admin);
        submitted = await Send("POST", "/api/users/me/allergens/custom", 200, new { name = "Hạt chia" }, user);
        id = submitted.GetProperty("id").GetInt32();
        var approved = await Send("POST", $"/api/admin/allergen-requests/{id}/approve", 200, token: admin);
        var allergenId = approved.GetProperty("allergenId").GetInt32();
        Check(approved.GetProperty("status").GetString() == "APPROVED", "Admin can approve a resubmission");
        Check((await Send("GET", "/api/admin/allergen-requests", 200, token: admin)).GetArrayLength() == 0, "Approved request leaves pending queue");
        Check((await Send("GET", "/api/users/me/allergens", 200, token: user)).GetProperty("allergens").GetArrayLength() == 0, "Approval does not silently change user selection");
        await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = new[] { allergenId } }, user);
        Check((await Send("GET", "/api/users/me/allergens", 200, token: user)).GetProperty("allergens")[0].GetProperty("id").GetInt32() == allergenId, "Approved item can be selected and reloaded");
        await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = new[] { allergenId, eggId } }, user);
        await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = new[] { allergenId, eggId } }, user);
        selected = await Send("GET", "/api/users/me/allergens", 200, token: user);
        Check(selected.GetProperty("allergens").GetArrayLength() == 2, "Repeated PUT preserves the saved set");
        await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = new[] { eggId } }, user);
        await Send("PUT", "/api/users/me/allergens", 404, new { allergenIds = new[] { eggId, 99999 } }, user);
        selected = await Send("GET", "/api/users/me/allergens", 200, token: user);
        Check(selected.GetProperty("allergens").GetArrayLength() == 1 && selected.GetProperty("allergens")[0].GetProperty("id").GetInt32() == eggId, "Changing an overlapping set persists and invalid IDs leave it unchanged");
        await Send("POST", $"/api/admin/allergen-requests/{id}/approve", 409, token: admin);
        await Send("POST", "/api/users/me/allergens/custom", 409, new { name = "Hạt chia" }, user);
        await Send("POST", "/api/admin/allergen-requests/99999/approve", 404, token: admin);
        submitted = await Send("POST", "/api/users/me/allergens/custom", 200, new { name = "Hạt lanh" }, user);
        var addedMeanwhile = await Send("POST", "/api/admin/allergens", 201, new { name = "Hạt lanh" }, admin);
        approved = await Send("POST", $"/api/admin/allergen-requests/{submitted.GetProperty("id").GetInt32()}/approve", 200, token: admin);
        Check(approved.GetProperty("allergenId").GetInt32() == addedMeanwhile.GetProperty("id").GetInt32(), "Approval reuses a catalog item added while waiting");
        Console.WriteLine($"ALLERGEN REVIEW PASS: {checks} HTTP and state checks; isolated storage, no live writes.");
    }
}

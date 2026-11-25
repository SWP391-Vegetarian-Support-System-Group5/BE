using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using API.Controllers;
using API.Infrastructure;
using API.Services;
using BLL.Common;
using BLL.DTOs;
using BLL.Services;
using DAL.Data;
using DAL.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

static class AdminApiTests
{
    // Real HTTP/controllers/services/JWT, isolated storage; never reads Aiven credentials.
    public static async Task RunAsync(bool locationOnly = false, bool avatarOnly = false, bool sourceAllergensOnly = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development, Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        const string key = "admin-api-tests-only-key-not-a-real-production-secret";
        builder.Configuration["Jwt:Key"] = key;
        builder.Configuration["Jwt:Issuer"] = "AdminApiTests";
        builder.Configuration["Jwt:Audience"] = "AdminApiTests";
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<VegetarianDbContext>(options => options.UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddVegetarianBusinessLogic();
        builder.Services.RemoveAll<IChatService>();
        builder.Services.RemoveAll<IVideoRecipeDraftService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ICommunityEventPublisher, TestEvents>();
        builder.Services.AddSingleton<IModerationAiService, MockModerationAiService>();
        builder.Services.AddSingleton<IAllergenIngredientMatcher, TestAllergenMatcher>();
        builder.Services.AddSingleton<INotificationEventPublisher, TestNotificationPublisher>();
        builder.Services.AddSingleton<IEmailSender, NoEmail>();
        builder.Services.AddSingleton<IOpenStreetMapService, TestMap>();
        builder.Services.AddSingleton<IRestaurantRouteService, TestRoute>();
        builder.Services.AddSingleton<IImageStorage, TestImageStorage>();
        builder.Services.AddSingleton<IAvatarImageStorage, TestAvatarStorage>();
        builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(AdministrationController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            options.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = "AdminApiTests", ValidAudience = "AdminApiTests",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew = TimeSpan.Zero
            });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VegetarianDbContext>();
            foreach (var (id, role) in new[] { (1, "ADMIN"), (2, "USER"), (3, "USER") })
                db.Users.Add(new User { UserId = id, Email = $"user{id}@example.invalid", PasswordHash = PasswordHasher.Hash("Test@123"), Role = role,
                    IsActive = true, IsEmailVerified = true, Profile = new UserProfile { FullName = $"Test {id}" } });
            foreach (var name in new[] { "VEGAN", "VEGETARIAN", "LACTO_VEGETARIAN", "OVO_VEGETARIAN", "LACTO_OVO_VEGETARIAN" }) db.DietTypes.Add(new DietType { Name = name });
            db.IngredientCategories.AddRange(new IngredientCategory { Code = "PLANT", Name = "Plant", DietaryGroup = "PLANT" }, new IngredientCategory { Code = "DAIRY", Name = "Dairy", DietaryGroup = "DAIRY" });
            db.RecipeCategories.AddRange(new[] { "Món từ ngũ cốc", "Bánh", "Món ăn sáng", "Bánh mì & bánh kẹp", "Tráng miệng", "Mì", "Salad & món ăn kèm", "Nước sốt", "Ăn nhẹ", "Súp & món hầm" }.Select(name => new RecipeCategory { Name = name, CreatedAt = DateTime.UtcNow }));
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        try
        {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(20) };
            int checks = 0;
            async Task<JsonElement> Send(string method, string path, int expected, object? body = null, string? token = null)
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), path);
                if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body is not null) request.Content = JsonContent.Create(body);
                using var response = await client.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if ((int)response.StatusCode != expected) throw new Exception($"FAIL {method} {path}: expected {expected}, got {(int)response.StatusCode}: {text}");
                checks++;
                Console.WriteLine($"PASS {expected} {method} {path}");
                return text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default;
            }
            async Task<JsonElement> SendImage(string purpose, int expected, string? token, byte[]? content = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/media/images?purpose={purpose}");
                if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(content ?? [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01]), "image", "dish.png");
                request.Content = form;
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                if ((int)response.StatusCode != expected) throw new Exception($"FAIL image {purpose}: expected {expected}, got {(int)response.StatusCode}: {body}");
                checks++;
                return body.Length > 0 ? JsonDocument.Parse(body).RootElement.Clone() : default;
            }
            var adminLogin = await Send("POST", "/api/auth/login", 200, new { email = "user1@example.invalid", password = "Test@123" });
            var userLogin = await Send("POST", "/api/auth/login", 200, new { email = "user2@example.invalid", password = "Test@123" });
            var admin = adminLogin.GetProperty("token").GetString()!;
            var user = userLogin.GetProperty("token").GetString()!;
            if (sourceAllergensOnly)
            {
                await Send("PUT", "/api/users/me/allergens", 401, new { allergenIds = Array.Empty<int>() });
                await Send("POST", "/api/admin/allergens", 403, new { name = "Forbidden new allergen" }, user);
                var added = await Send("POST", "/api/admin/allergens", 201, new { name = "Đậu nành" }, admin);
                var selectedCatalogId = added.GetProperty("id").GetInt32();
                await Send("GET", "/api/allergens", 200, token: user);
                var saved = await Send("PUT", "/api/users/me/allergens", 200, new { userId = 3, allergenIds = new[] { selectedCatalogId, selectedCatalogId } }, user);
                if (saved.GetProperty("userId").GetInt32() != 2 || saved.GetProperty("allergens").GetArrayLength() != 1)
                    throw new Exception("Selection must use JWT account and deduplicate IDs.");
                var thirdLogin = await Send("POST", "/api/auth/login", 200, new { email = "user3@example.invalid", password = "Test@123" });
                var thirdToken = thirdLogin.GetProperty("token").GetString()!;
                var other = await Send("GET", "/api/users/me/allergens", 200, token: thirdToken);
                if (other.GetProperty("allergens").GetArrayLength() != 0) throw new Exception("Other account was modified.");
                await Send("PUT", "/api/users/me/allergens", 404, new { allergenIds = new[] { int.MaxValue } }, user);
                await Send("PUT", "/api/users/me/allergens", 400, new { allergenIds = new[] { -1 } }, user);
                var unchanged = await Send("GET", "/api/users/me/allergens", 200, token: user);
                if (unchanged.GetProperty("allergens").GetArrayLength() != 1) throw new Exception("Invalid IDs changed saved selection.");
                var recipeWithVideo = await Send("POST", "/api/recipes", 201, new
                {
                    title = "Tàu hũ kho", content = "Món dùng tàu hũ", recipeName = "Tàu hũ kho",
                    videoUrl = "https://res.cloudinary.com/test/video/upload/tofu.mp4",
                    ingredients = new[] { new { ingredientName = "TAU HU", amount = "100g", dietaryGroup = "PLANT" } },
                    steps = new[] { new { stepNumber = 1, instruction = "Kho" } }
                }, admin);
                var tofuRecipeId = recipeWithVideo.GetProperty("postId").GetInt32();
                foreach (var endpoint in new[] { "/api/posts", "/api/recipes/search" })
                {
                    var filtered = await Send("GET", endpoint, 200, token: user);
                    var unfiltered = await Send("GET", endpoint, 200, token: thirdToken);
                    if (filtered.EnumerateArray().Any(item => item.GetProperty("postId").GetInt32() == tofuRecipeId)
                        || !unfiltered.EnumerateArray().Any(item => item.GetProperty("postId").GetInt32() == tofuRecipeId))
                        throw new Exception("A recipe with video and unmapped TAU HU must be excluded only for the soy-allergic account.");
                }

                var titleCases = new[]
                {
                    ("Đậu nành", "Cách làm TÀU HŨ tại nhà", "VIDEO"),
                    ("Milk", "Cách làm SUA CHUA tại nhà", "BLOG"),
                    ("Egg", "Cách làm mayonnaise", "BLOG"),
                    ("Peanut", "Cách làm bơ lạc", "VIDEO"),
                    ("Gluten", "Cách làm mì căn", "BLOG"),
                    ("Tree Nut", "Cách rang hạt dẻ cười", "VIDEO"),
                    ("Sesame", "Cách làm tahini", "BLOG")
                };
                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<VegetarianDbContext>();
                    db.Allergens.AddRange(titleCases.Skip(1).Select(item => new Allergen { Name = item.Item1 }));
                    var plant = await db.IngredientCategories.SingleAsync(x => x.Code == "PLANT");
                    var safeDish = new Post { UserId = 1, Title = "Cách làm sữa chua dừa đã xác minh", Content = "Test", PostType = "RECIPE", Status = "PUBLISHED" };
                    db.Recipes.Add(new Recipe
                    {
                        Post = safeDish, RecipeName = safeDish.Title,
                        Ingredients = [new RecipeIngredient { Ingredient = new Ingredient
                        {
                            Name = "Sữa chua dừa", Category = plant, IsAllergenProfileVerified = true, AllergenEvidence = "Test coconut-only label"
                        } }]
                    });
                    db.Posts.AddRange(titleCases.Select(item => new Post
                    {
                        UserId = 1, Title = item.Item2, Content = "Test", PostType = item.Item3, Status = "PUBLISHED"
                    }));
                    await db.SaveChangesAsync();
                }
                var catalog = await Send("GET", "/api/allergens", 200, token: user);
                foreach (var (name, title, _) in titleCases)
                {
                    var id = catalog.EnumerateArray().Single(item => item.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();
                    await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = new[] { id } }, user);
                    var filtered = await Send("GET", "/api/posts", 200, token: user);
                    bool HasTitle(string expected) => filtered.EnumerateArray().Any(item => item.GetProperty("title").GetString() == expected);
                    if (HasTitle(title) || !HasTitle("Cách làm sữa chua dừa đã xác minh"))
                        throw new Exception($"Title fallback did not respect the {name} profile and confirmed recipe ingredients.");
                }
                var guest = await Send("GET", "/api/posts", 200);
                if (titleCases.Any(test => !guest.EnumerateArray().Any(item => item.GetProperty("title").GetString() == test.Item2)))
                    throw new Exception("Guest feed must keep posts excluded only by personal allergy selections.");
                var cleared = await Send("PUT", "/api/users/me/allergens", 200, new { allergenIds = Array.Empty<int>() }, user);
                if (cleared.GetProperty("allergens").GetArrayLength() != 0) throw new Exception("Empty selection was not saved.");
                await Send("PUT", "/api/admin/ingredients/1/allergens", 403, new { allergenIds = new[] { selectedCatalogId }, isVerified = true, evidence = "Test" }, user);
                Console.WriteLine("PASS source allergy JWT/catalog HTTP contract; isolated test storage only.");
                return;
            }

            if (avatarOnly)
            {
                await AvatarApiTests.RunAsync(client, user, admin);
                await AvatarServiceTests.RunAsync();
                return;
            }
            if (locationOnly)
            {
                await LocationApiTests.RunAsync(client, user);
                return;
            }
            await SendImage("recipes", 401, null);
            await SendImage("restaurants", 403, user);
            await SendImage("categories", 403, user);
            var userImage = await SendImage("recipes", 200, user);
            if (!userImage.GetProperty("imageUrl").GetString()!.Contains("/recipes/")) throw new Exception("Recipe upload did not use Cloudinary recipe folder");
            var restaurantImage = await SendImage("restaurants", 200, admin);
            if (!restaurantImage.GetProperty("imageUrl").GetString()!.Contains("/restaurants/")) throw new Exception("Restaurant upload did not use Cloudinary restaurant folder");
            await SendImage("categories", 200, admin);
            await SendImage("recipes", 400, user, [0x01, 0x02, 0x03]);
            await SendImage("unknown", 400, admin);
            var adminRoutes = new (string Method, string Path, object? Body)[] {
                ("GET","/api/admin/users",null), ("GET","/api/admin/users/3",null), ("PUT","/api/admin/users/3/status",new { isActive = false }), ("DELETE","/api/admin/users/3",null),
                ("GET","/api/admin/posts",null), ("DELETE","/api/admin/posts/1",null), ("GET","/api/admin/comments",null), ("DELETE","/api/admin/comments/1",null),
                ("PUT","/api/admin/comments/1/visibility",new { hidden=true }),
                ("PUT","/api/admin/ingredients/1",new { name="Tofu",ingredientCategoryId=1 }),
                ("POST","/api/admin/ingredient-categories",new {code="VEGETABLES",name="Vegetables",dietaryGroup="PLANT"}),
                ("PUT","/api/admin/ingredient-categories/1",new {code="PLANT",name="Plants",dietaryGroup="PLANT"}),
                ("POST","/api/admin/allergens",new { name="Test allergen" }), ("PUT","/api/admin/allergens/1",new { name="Test allergen" }), ("DELETE","/api/admin/allergens/1",null),
                ("POST","/api/admin/restaurants",new { name="Test", address="Test address" }), ("PUT","/api/admin/restaurants/1",new { name="Test",address="Test address" }), ("DELETE","/api/admin/restaurants/1",null),
                ("GET","/api/admin/moderation",null), ("GET","/api/admin/moderation/1",null), ("POST","/api/admin/moderation/1/approve",null), ("POST","/api/admin/moderation/1/remove",null),
                ("POST","/api/recipe-categories",new {name="Test"}), ("PUT","/api/recipe-categories/1",new {name="Test"}),
                ("PUT","/api/admin/recipes/1/categories",new {categoryIds=Array.Empty<int>()})
            };
            foreach (var route in adminRoutes)
            {
                await Send(route.Method, route.Path, 401, route.Body);
                await Send(route.Method, route.Path, 403, route.Body, user);
            }
            await Send("GET", "/api/admin/users", 200, token: admin);
            await Send("GET", "/api/admin/users/3", 200, token: admin);
            await Send("PUT", "/api/admin/users/3/status", 204, new { isActive = false }, admin);
            var blocked = await Send("GET", "/api/admin/users/3", 200, token: admin);
            if (blocked.GetProperty("isActive").GetBoolean()) throw new Exception("User status was not persisted");
            await Send("PUT", "/api/admin/users/3/status", 204, new { isActive = true }, admin);
            await Send("DELETE", "/api/admin/users/3", 204, token: admin);
            await Send("POST", "/api/auth/login", 403, new { email="user3@example.invalid", password="Test@123" });
            await Send("GET", "/api/admin/users/9999", 404, token: admin);
            await Send("PUT", "/api/admin/users/9999/status", 404, new { isActive=true }, admin);
            await Send("DELETE", "/api/admin/users/9999", 404, token: admin);

            var allergen = await Send("POST", "/api/admin/allergens", 201, new { name="Test allergen" }, admin);
            var allergenId = allergen.GetProperty("id").GetInt32();
            await Send("POST", "/api/admin/allergens", 409, new { name="Test allergen" }, admin);
            await Send("POST", "/api/admin/allergens", 400, new { name="" }, admin);
            await Send("PUT", $"/api/admin/allergens/{allergenId}", 204, new { name="Changed allergen" }, admin);
            await Send("GET", "/api/allergens", 200, token: admin);
            await Send("DELETE", $"/api/admin/allergens/{allergenId}", 204, token: admin);
            await Send("DELETE", $"/api/admin/allergens/{allergenId}", 404, token: admin);

            var diets = await Send("GET", "/api/diet-types", 200, token: admin);
            var dietId = diets.EnumerateArray().First(x => x.GetProperty("name").GetString() == "VEGAN").GetProperty("id").GetInt32();
            var secondDietId = diets.EnumerateArray().First(x => x.GetProperty("name").GetString() == "LACTO_VEGETARIAN").GetProperty("id").GetInt32();
            var restaurantBody = new { name="Test kitchen",address="Test address",imageUrl="https://res.cloudinary.com/test/image/upload/v1/restaurant.jpg",latitude=1.0,longitude=2.0,dietTypeIds=new[]{dietId,secondDietId,secondDietId},foods=new[]{"Tofu","Tofu","Rice"} };
            var restaurant = await Send("POST", "/api/admin/restaurants", 201, restaurantBody, admin);
            var restaurantId = restaurant.GetProperty("restaurantId").GetInt32();
            if (restaurant.GetProperty("imageUrl").GetString() != restaurantBody.imageUrl) throw new Exception("Restaurant image URL was not saved");
            if (restaurant.GetProperty("latitude").GetDecimal() != 10m || restaurant.GetProperty("longitude").GetDecimal() != 106m) throw new Exception("Restaurant coordinates must come from its address, not the client");
            if (restaurant.GetProperty("foods").GetArrayLength()!=2) throw new Exception("Restaurant foods were not deduplicated");
            if (restaurant.GetProperty("dietTypeIds").GetArrayLength()!=2) throw new Exception("Restaurant diet types were not deduplicated");
            var nearby = await Send("GET", $"/api/restaurants/nearby?latitude=10&longitude=106&radius=5&dietTypeId={dietId}", 200, token: user);
            if (!nearby.EnumerateArray().Any(x => x.GetProperty("restaurantId").GetInt32() == restaurantId)) throw new Exception("Restaurant was not returned for its first diet type");
            var secondDietNearby = await Send("GET", $"/api/restaurants/nearby?latitude=10&longitude=106&radius=5&dietTypeId={secondDietId}", 200, token: user);
            if (!secondDietNearby.EnumerateArray().Any(x => x.GetProperty("restaurantId").GetInt32() == restaurantId)) throw new Exception("Restaurant was not returned for its second diet type");
            await Send("GET", "/api/restaurants/nearby?latitude=10&longitude=106&radius=5&dietTypeId=99999", 404, token: user);
            await Send("GET", $"/api/restaurants/{restaurantId}", 200, token: admin);
            int mushroomId;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VegetarianDbContext>();
                var restaurantPlantCategoryId = await db.IngredientCategories.Where(x => x.Code == "PLANT").Select(x => x.IngredientCategoryId).SingleAsync();
                var mushroom = new Ingredient { Name = "Mushroom", IngredientCategoryId = restaurantPlantCategoryId };
                db.Ingredients.Add(mushroom);
                await db.SaveChangesAsync();
                mushroomId = mushroom.IngredientId;
            }
            await Send("PUT", $"/api/admin/restaurants/{restaurantId}", 204, new {name="Changed kitchen",address="Changed address",imageUrl="https://res.cloudinary.com/test/image/upload/v1/changed.jpg",dietTypeIds=new[]{dietId},foods=new[]{"Soup"},ingredientIds=new[]{mushroomId}}, admin);
            var updatedRestaurant = await Send("GET", $"/api/restaurants/{restaurantId}", 200, token: admin);
            if (updatedRestaurant.GetProperty("imageUrl").GetString() != "https://res.cloudinary.com/test/image/upload/v1/changed.jpg") throw new Exception("Restaurant image URL was not updated");
            if (updatedRestaurant.GetProperty("foods")[0].GetString() != "Soup") throw new Exception("Restaurant update did not replace foods");
            if (updatedRestaurant.GetProperty("latitude").GetDecimal() != 11m) throw new Exception("Changing restaurant address must refresh coordinates");
            if (updatedRestaurant.GetProperty("ingredientIds")[0].GetInt32() != mushroomId) throw new Exception("Restaurant ingredients were not saved");
            if (updatedRestaurant.GetProperty("dietTypeIds").GetArrayLength()!=1) throw new Exception("Restaurant diet type replacement did not remove the previous type");
            var removedDietNearby = await Send("GET", $"/api/restaurants/nearby?latitude=11&longitude=107&radius=5&dietTypeId={secondDietId}", 200, token: user);
            if (removedDietNearby.GetArrayLength()!=0) throw new Exception("Removed diet type still matched nearby restaurant");
            var withMushroom = await Send("GET", $"/api/restaurants/nearby?latitude=11&longitude=107&radius=5&dietTypeId={dietId}&ingredient=Mushroom", 200, token: user);
            if (!withMushroom.EnumerateArray().Any(x => x.GetProperty("restaurantId").GetInt32() == restaurantId)) throw new Exception("Restaurant ingredient filter omitted a matching restaurant");
            var withoutMushroom = await Send("GET", $"/api/restaurants/nearby?latitude=11&longitude=107&radius=5&dietTypeId={dietId}&ingredient=Tofu", 200, token: user);
            if (withoutMushroom.GetArrayLength() != 0) throw new Exception("Restaurant ingredient filter returned an untagged restaurant");
            await Send("POST", "/api/admin/restaurants", 422, new {name="Unknown",address="Unknown address"}, admin);
            await Send("POST", "/api/admin/restaurants", 404, new {name="Bad",address="Test address",dietTypeIds=new[]{9999}}, admin);
            await Send("POST", "/api/admin/restaurants", 400, new {name="No diet",address="Test address",dietTypeIds=Array.Empty<int>()}, admin);
            await Send("POST", "/api/admin/restaurants", 400, new {name="Bad image",address="Test address",dietTypeIds=new[]{dietId},imageUrl="javascript:alert(1)"}, admin);
            await Send("POST", "/api/admin/restaurants", 400, new {name="External image",address="Test address",dietTypeIds=new[]{dietId},imageUrl="https://example.com/photo.jpg"}, admin);
            await Send("PUT", $"/api/admin/restaurants/{restaurantId}", 404, new {name="Changed kitchen",address="Changed address",dietTypeIds=new[]{9999}}, admin);
            await Send("GET", "/api/restaurants?food=Soup", 200, token: admin);
            await Send("DELETE", $"/api/admin/restaurants/{restaurantId}", 204, token: admin);
            await Send("GET", $"/api/restaurants/{restaurantId}", 404, token: admin);

            var category = await Send("POST", "/api/recipe-categories", 201, new {name="Test category",isActive=true}, admin);
            var categoryId = category.GetProperty("id").GetInt32();
            await Send("POST", "/api/recipe-categories", 409, new {name="Test category"}, admin);
            await Send("POST", "/api/recipe-categories", 400, new {name="   "}, admin);
            await Send("POST", "/api/recipe-categories", 400, new {name=new string('x', 151)}, admin);
            await Send("PUT", $"/api/recipe-categories/{categoryId}", 200, new {name="Changed category",isActive=false}, admin);
            var visible = await Send("GET", "/api/recipe-categories", 200);
            if (visible.EnumerateArray().Any(x=>x.GetProperty("id").GetInt32()==categoryId)) throw new Exception("Inactive category leaked to public");
            var all = await Send("GET", "/api/recipe-categories?includeInactive=true", 200, token: admin);
            if (!all.EnumerateArray().Any(x=>x.GetProperty("id").GetInt32()==categoryId)) throw new Exception("Admin cannot see inactive category");

            var post = await Send("POST", "/api/posts", 201, new {title="Test post",content="Test content",postType="BLOG"}, user);
            var postId = post.GetProperty("postId").GetInt32();
            if (post.GetProperty("compatibleDietTypes").GetArrayLength() != 0) throw new Exception("A plain post must not claim a vegetarian type");
            await Send("POST", $"/api/admin/posts/{postId}/approve", 204, token: admin);
            var comment = await Send("POST", $"/api/posts/{postId}/comments", 201, new {content="Test comment"}, user);
            var commentId = comment.GetProperty("commentId").GetInt32();
            await Send("PUT", $"/api/admin/comments/{commentId}/visibility", 204, new { hidden=true }, admin);
            var hiddenComments = await Send("GET", "/api/admin/comments", 200, token: admin);
            if (hiddenComments[0].GetProperty("status").GetString() != "HIDDEN") throw new Exception("Comment hide did not persist");
            await Send("PUT", $"/api/admin/comments/{commentId}/visibility", 204, new { hidden=false }, admin);
            await Send("GET", "/api/admin/posts?postType=BLOG&status=PUBLISHED&keyword=Test", 200, token: admin);
            await Send("GET", "/api/admin/comments", 200, token: admin);
            var report = await Send("POST", $"/api/reports/posts/{postId}", 201, new {reason="Test report"}, user);
            var reportId = report.GetProperty("flagId").GetInt32();
            await Send("GET", "/api/admin/moderation", 200, token: admin);
            await Send("GET", $"/api/admin/moderation/{reportId}", 200, token: admin);
            await Send("POST", $"/api/admin/moderation/{reportId}/approve", 204, token: admin);
            var approved = await Send("GET", $"/api/admin/moderation/{reportId}", 200, token: admin);
            if (approved.GetProperty("status").GetString() != "APPROVED") throw new Exception("Approve did not persist");
            await Send("POST", $"/api/reports/comments/{commentId}", 403, new {reason="Self-report"}, user);
            var commentReport = await Send("POST", $"/api/reports/comments/{commentId}", 201, new {reason="Test comment report"}, admin);
            var reportedComments = await Send("GET", $"/api/posts/{postId}/comments", 200);
            if (commentReport.GetProperty("source").GetString() != "USER_REPORT"
                || reportedComments.EnumerateArray().Single().GetProperty("status").GetString() != "PUBLISHED")
                throw new Exception("A user report must leave the comment public until review");
            await Send("POST", $"/api/admin/moderation/{commentReport.GetProperty("flagId").GetInt32()}/remove", 204, token: admin);
            var comments = await Send("GET", "/api/admin/comments", 200, token: admin);
            if (comments[0].GetProperty("status").GetString() != "REMOVED") throw new Exception("Moderation did not remove comment");
            var flaggedComment = await Send("POST", $"/api/posts/{postId}/comments", 201, new { content = "spam" }, user);
            var flaggedCommentId = flaggedComment.GetProperty("commentId").GetInt32();
            if (flaggedComment.GetProperty("status").GetString() != "PENDING_REVIEW") throw new Exception("AI-flagged comment must wait for review");
            var pendingComments = await Send("GET", $"/api/posts/{postId}/comments", 200);
            if (pendingComments.GetArrayLength() != 0) throw new Exception("AI-flagged comment became public before approval");
            var flags = await Send("GET", "/api/admin/moderation", 200, token: admin);
            var aiFlagId = flags.EnumerateArray().Single(x => x.GetProperty("commentId").ValueKind == JsonValueKind.Number
                && x.GetProperty("commentId").GetInt32() == flaggedCommentId).GetProperty("flagId").GetInt32();
            await Send("POST", $"/api/admin/moderation/{aiFlagId}/approve", 204, token: admin);
            var publicComments = await Send("GET", $"/api/posts/{postId}/comments", 200);
            if (publicComments[0].GetProperty("commentId").GetInt32() != flaggedCommentId
                || publicComments[0].GetProperty("status").GetString() != "PUBLISHED") throw new Exception("Approved comment did not stay public");
            var reply = await Send("POST", $"/api/comments/{flaggedCommentId}/replies", 201, new { content = "Test reply" }, user);
            var replyId = reply.GetProperty("commentId").GetInt32();
            var flaggedReply = await Send("POST", $"/api/comments/{flaggedCommentId}/replies", 201, new { content = "spam" }, user);
            if (flaggedReply.GetProperty("status").GetString() != "PENDING_REVIEW"
                || (await Send("GET", $"/api/posts/{postId}/comments", 200))[0].GetProperty("replies").GetArrayLength() != 1)
                throw new Exception("AI-flagged replies must stay out of the public thread");
            await Send("POST", $"/api/admin/moderation/{aiFlagId}/remove", 204, token: admin);
            await Send("POST", $"/api/admin/moderation/{aiFlagId}/remove", 204, token: admin);
            var remainingThread = await Send("GET", $"/api/posts/{postId}/comments", 200);
            var placeholder = remainingThread.EnumerateArray().Single();
            var publicReply = placeholder.GetProperty("replies").EnumerateArray().Single();
            if (placeholder.GetProperty("commentId").GetInt32() != flaggedCommentId
                || placeholder.GetProperty("status").GetString() != "REMOVED"
                || placeholder.GetProperty("content").GetString() != ""
                || placeholder.GetProperty("authorName").GetString() != ""
                || publicReply.GetProperty("commentId").GetInt32() != replyId
                || publicReply.GetProperty("status").GetString() != "PUBLISHED")
                throw new Exception("A removed comment must preserve public replies without exposing its own content");
            await Send("POST", $"/api/comments/{flaggedCommentId}/replies", 404, new { content = "Test reply" }, user);
            await Send("DELETE", $"/api/admin/comments/{replyId}", 204, token: admin);
            if ((await Send("GET", $"/api/posts/{postId}/comments", 200)).GetArrayLength() != 0)
                throw new Exception("A removed comment without public descendants must disappear from the public feed");

            var secondComment = await Send("POST", $"/api/posts/{postId}/comments", 201, new { content = "spam" }, user);
            var secondCommentId = secondComment.GetProperty("commentId").GetInt32();
            var secondFlags = await Send("GET", "/api/admin/moderation", 200, token: admin);
            var secondAiFlagId = secondFlags.EnumerateArray().Single(x => x.GetProperty("commentId").ValueKind == JsonValueKind.Number
                && x.GetProperty("commentId").GetInt32() == secondCommentId).GetProperty("flagId").GetInt32();
            await Send("POST", $"/api/reports/comments/{secondCommentId}", 409, new { reason = "Pending comment report" }, admin);
            int secondReportId;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VegetarianDbContext>();
                var legacyReport = new ModerationFlag { CommentId = secondCommentId, Source = "USER_REPORT", Status = "PENDING", Reason = "Legacy report" };
                db.ModerationFlags.Add(legacyReport);
                await db.SaveChangesAsync();
                secondReportId = legacyReport.FlagId;
            }
            await Send("POST", $"/api/admin/moderation/{secondReportId}/remove", 204, token: admin);
            await Send("POST", $"/api/admin/moderation/{secondAiFlagId}/approve", 409, token: admin);
            var removedAiFlag = await Send("GET", $"/api/admin/moderation/{secondAiFlagId}", 200, token: admin);
            if (removedAiFlag.GetProperty("status").GetString() != "REMOVED"
                || (await Send("GET", $"/api/posts/{postId}/comments", 200)).GetArrayLength() != 0)
                throw new Exception("Removing a reported comment must close its AI flag and keep it private");

            await Send("DELETE", $"/api/admin/comments/{commentId}", 204, token: admin);
            await Send("POST", $"/api/admin/moderation/{reportId}/remove", 204, token: admin);
            await Send("DELETE", $"/api/admin/posts/{postId}", 204, token: admin);
            await Send("GET", $"/api/posts/{postId}", 404);
            await Send("DELETE", "/api/admin/posts/9999", 404, token: admin);
            await Send("DELETE", "/api/admin/comments/9999", 404, token: admin);
            await Send("POST", "/api/admin/moderation/9999/approve", 404, token: admin);
            await Send("POST", "/api/admin/moderation/9999/remove", 404, token: admin);

            var flaggedRecipeRequest = new
            {
                title = "AI moderation test", content = "spam", recipeName = "Tofu dish",
                ingredients = new[] { new { ingredientName = "Test tofu", amount = "100g", dietaryGroup = "PLANT" } },
                steps = new[] { new { stepNumber = 1, instruction = "Cook" } }
            };
            var flaggedRecipe = await Send("POST", "/api/recipes", 201, flaggedRecipeRequest, user);
            var flaggedRecipeId = flaggedRecipe.GetProperty("postId").GetInt32();
            var recipeFlags = await Send("GET", "/api/admin/moderation", 200, token: admin);
            var recipeFlagId = recipeFlags.EnumerateArray().Single(x => x.GetProperty("postId").ValueKind == JsonValueKind.Number
                && x.GetProperty("postId").GetInt32() == flaggedRecipeId).GetProperty("flagId").GetInt32();
            await Send("PUT", $"/api/recipes/{flaggedRecipeId}", 204, flaggedRecipeRequest, user);
            int duplicateFlagId;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VegetarianDbContext>();
                var duplicate = new ModerationFlag { PostId = flaggedRecipeId, Source = "AI", Status = "PENDING", Reason = "Older duplicate flag" };
                db.ModerationFlags.Add(duplicate);
                await db.SaveChangesAsync();
                duplicateFlagId = duplicate.FlagId;
            }
            await Send("POST", $"/api/admin/moderation/{recipeFlagId}/approve", 204, token: admin);
            await Send("PUT", $"/api/recipes/{flaggedRecipeId}", 204, flaggedRecipeRequest, user);
            var reviewedRecipeFlags = await Send("GET", "/api/admin/moderation", 200, token: admin);
            var matchingFlags = reviewedRecipeFlags.EnumerateArray().Where(x => x.GetProperty("postId").ValueKind == JsonValueKind.Number
                && x.GetProperty("postId").GetInt32() == flaggedRecipeId).ToArray();
            if (matchingFlags.Length != 2 || matchingFlags.Any(x => x.GetProperty("status").GetString() != "APPROVED"))
                throw new Exception("Recipe approval must resolve both existing flags without creating another");
            var publicRecipe = await Send("GET", $"/api/recipes/{flaggedRecipeId}", 200);
            if (publicRecipe.GetProperty("status").GetString() != "PUBLISHED") throw new Exception("Approved recipe did not stay public");
            await Send("POST", $"/api/admin/moderation/{duplicateFlagId}/remove", 204, token: admin);
            await Send("POST", $"/api/admin/moderation/{recipeFlagId}/approve", 409, token: admin);
            await Send("PUT", $"/api/recipes/{flaggedRecipeId}", 409, flaggedRecipeRequest, user);
            await Send("GET", $"/api/recipes/{flaggedRecipeId}", 404);

            var ingredientCategories = await Send("GET", "/api/ingredient-categories", 200, token: admin);
            var plantId = ingredientCategories.EnumerateArray().First(x=>x.GetProperty("code").GetString()=="PLANT").GetProperty("id").GetInt32();
            var dairyId = ingredientCategories.EnumerateArray().First(x=>x.GetProperty("code").GetString()=="DAIRY").GetProperty("id").GetInt32();
            var vegetables = await Send("POST", "/api/admin/ingredient-categories", 201, new {code="VEGETABLES",name="Vegetables",dietaryGroup="PLANT"}, admin);
            var vegetableId = vegetables.GetProperty("id").GetInt32();
            await Send("POST", "/api/admin/ingredient-categories", 409, new {code="VEGETABLES",name="Other",dietaryGroup="PLANT"}, admin);
            await Send("POST", "/api/admin/ingredient-categories", 400, new {code="BAD",name="Bad",dietaryGroup="MEAT"}, admin);
            await Send("PUT", $"/api/admin/ingredient-categories/{vegetableId}", 200, new {code="VEGETABLES",name="Fresh vegetables",dietaryGroup="PLANT"}, admin);
            await Send("PUT", $"/api/admin/ingredient-categories/{plantId}", 400, new {code="PLANT",name="Plants",dietaryGroup="DAIRY"}, admin);
            var recipe = await Send("POST", "/api/recipes", 201, new { title="Recipe post",content="Test",recipeName="Tofu dish",imageUrl="https://res.cloudinary.com/test/image/upload/v1/tofu.jpg",ingredients=new[]{new{ingredientName="Test tofu",amount="100g",dietaryGroup="PLANT"}},steps=new[]{new{stepNumber=1,instruction="Cook"}} }, admin);
            var recipeId = recipe.GetProperty("postId").GetInt32();
            if (recipe.GetProperty("imageUrl").GetString() != "https://res.cloudinary.com/test/image/upload/v1/tofu.jpg") throw new Exception("Recipe image URL was not saved");
            await Send("POST", "/api/recipes", 400, new { title="Bad image",content="Test",recipeName="Tofu dish",imageUrl="http://example.com/tofu.jpg",ingredients=new[]{new{ingredientName="Test tofu",amount="100g",dietaryGroup="PLANT"}} }, admin);
            var recipePost = await Send("GET", $"/api/posts/{recipeId}", 200);
            if (recipePost.GetProperty("thumbnailUrl").GetString() != "https://res.cloudinary.com/test/image/upload/v1/tofu.jpg") throw new Exception("Recipe image was not shown on the post");
            if (recipePost.GetProperty("compatibleDietTypes").GetArrayLength() == 0 || !recipePost.GetProperty("compatibleDietTypes").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "VEGAN")) throw new Exception("Plant recipe post did not expose its compatible diet types");
            var grainsId = all.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Món từ ngũ cốc").GetProperty("id").GetInt32();
            await Send("PUT", $"/api/admin/recipes/{recipeId}/categories", 204, new { categoryIds=new[]{grainsId} }, admin);
            var categoryPosts = await Send("GET", $"/api/posts?category={grainsId}", 200);
            if (!categoryPosts.EnumerateArray().Any(x => x.GetProperty("postId").GetInt32() == recipeId)) throw new Exception("Public recipe category page cannot list the assigned post");
            await Send("PUT", $"/api/admin/recipes/{recipeId}/categories", 204, new { categoryIds=Array.Empty<int>() }, admin);
            await Send("PUT", $"/api/admin/recipes/{recipeId}/categories", 400, new { categoryIds=new[]{9999} }, admin);
            var ingredients = await Send("GET", "/api/ingredients?keyword=Test tofu", 200, token: admin);
            var ingredientId = ingredients[0].GetProperty("id").GetInt32();
            var assigned = await Send("PUT", $"/api/admin/ingredients/{ingredientId}", 200, new {name="Test tofu",ingredientCategoryId=vegetableId}, admin);
            if (assigned.GetProperty("dietaryGroup").GetString() != "PLANT") throw new Exception("Custom ingredient category lost its dietary classification");
            await Send("PUT", $"/api/admin/ingredients/{ingredientId}", 200, new {name="Test cheese",ingredientCategoryId=dairyId}, admin);
            var changedRecipe = await Send("GET", $"/api/recipes/{recipeId}", 200);
            if (changedRecipe.GetProperty("ingredients")[0].GetProperty("dietaryGroup").GetString()!="DAIRY") throw new Exception("Ingredient edit did not propagate");
            var updatedPost = await Send("GET", $"/api/posts/{recipeId}", 200);
            if (updatedPost.GetProperty("compatibleDietTypes").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "VEGAN")) throw new Exception("Recipe post kept the vegan label after its ingredient became dairy");
            await Send("PUT", $"/api/admin/ingredients/{ingredientId}", 404, new {name="Bad",ingredientCategoryId=9999}, admin);
            await Send("PUT", "/api/admin/ingredients/9999", 404, new {name="Bad",ingredientCategoryId=plantId}, admin);
            Console.WriteLine($"ADMIN API PASS: {checks} HTTP checks; {adminRoutes.Length} admin route definitions checked for guest 401 and user 403. Storage: EF InMemory; zero Aiven writes.");
        }
        finally { await app.StopAsync(); }
    }

    sealed class NoEmail : IEmailSender
    {
        public Task SendOtpAsync(string recipientEmail, string purpose, string otpCode, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Email is not part of admin API tests.");
    }


    sealed class TestAllergenMatcher : IAllergenIngredientMatcher
    {
        public Task<IReadOnlySet<int>> FindUnsafeAsync(IReadOnlyCollection<Allergen> allergens, IReadOnlyCollection<Ingredient> ingredients, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
    }

    sealed class TestNotificationPublisher : INotificationEventPublisher
    {
        public Task SendAsync(int userId, UserNotificationResponse notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    sealed class TestImageStorage : IImageStorage
    {
        public Task<string> UploadAsync(Stream image, string extension, string purpose, CancellationToken ct) =>
            Task.FromResult($"https://res.cloudinary.com/test/image/upload/v1/{purpose}/mock{extension}");
    }

    sealed class TestRoute : IRestaurantRouteService
    {
        public Task<RestaurantRouteResponse> GetRouteAsync(decimal originLatitude, decimal originLongitude, decimal destinationLatitude, decimal destinationLongitude, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Route providers are not used by admin API tests.");
    }

    sealed class TestMap : IOpenStreetMapService
    {
        public Task<GeocodeResponse?> GeocodeAsync(string address, CancellationToken ct) => Task.FromResult<GeocodeResponse?>(address switch
        {
            "Test address" => new(10, 106, address),
            "Changed address" => new(11, 107, address),
            _ => null
        });
        public Task<IReadOnlyCollection<AddressSuggestionResponse>> GetAddressSuggestionsAsync(string query, decimal? latitude, decimal? longitude, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ReverseGeocodeAsync(decimal latitude, decimal longitude, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<NearbyPlaceResponse>> GetNearbyVegetarianPlacesAsync(decimal latitude, decimal longitude, decimal radiusKm, string provinceCode, string areaCode, CancellationToken ct) => throw new NotSupportedException();
    }
}

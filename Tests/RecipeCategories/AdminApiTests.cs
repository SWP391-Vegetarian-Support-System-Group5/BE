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
    public static async Task RunAsync()
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
        builder.Services.AddSingleton<IAllergenNameValidator, TestAllergenValidator>();
        builder.Services.AddSingleton<IAllergenIngredientMatcher, TestAllergenMatcher>();
        builder.Services.AddSingleton<INotificationEventPublisher, TestNotificationPublisher>();
        builder.Services.AddSingleton<IEmailSender, NoEmail>();
        builder.Services.AddSingleton<IOpenStreetMapService, TestMap>();
        builder.Services.AddSingleton<IImageStorage, TestImageStorage>();
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
            await db.SaveChangesAsync();
            await BMISchemaUpdate.ApplyAsync(db);
            await scope.ServiceProvider.GetRequiredService<RecipeCategoryService>().SeedAsync(default);
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
                ("POST","/api/recipe-categories",new {name="Test",slug="test",type="CUISINE"}), ("PUT","/api/recipe-categories/1",new {name="Test",slug="test",type="CUISINE"}),
                ("GET","/api/admin/recipes/bmi-types",null), ("PUT","/api/admin/recipes/1/bmi-types",new {bmiTypeIds=Array.Empty<int>()}),
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

            var category = await Send("POST", "/api/recipe-categories", 201, new {name="Test category",slug="test-category",type="CUISINE",isActive=true}, admin);
            var categoryId = category.GetProperty("id").GetInt32();
            await Send("POST", "/api/recipe-categories", 409, new {name="Duplicate",slug="test-category",type="CUISINE"}, admin);
            await Send("POST", "/api/recipe-categories", 400, new {name="Invalid",slug="bad",type="NOT_A_TYPE"}, admin);
            await Send("POST", "/api/recipe-categories", 400, new {name="External image",slug="external-image",type="CUISINE",imageUrl="https://example.com/photo.jpg"}, admin);
            await Send("PUT", $"/api/recipe-categories/{categoryId}", 200, new {name="Changed category",slug="test-category",type="CUISINE",isActive=false}, admin);
            var visible = await Send("GET", "/api/recipe-categories?type=CUISINE", 200);
            if (visible.EnumerateArray().Any(x=>x.GetProperty("id").GetInt32()==categoryId)) throw new Exception("Inactive category leaked to public");
            var all = await Send("GET", "/api/recipe-categories?includeInactive=true", 200, token: admin);
            if (!all.EnumerateArray().Any(x=>x.GetProperty("id").GetInt32()==categoryId)) throw new Exception("Admin cannot see inactive category");

            var post = await Send("POST", "/api/posts", 201, new {title="Test post",content="Test content",postType="BLOG"}, user);
            var postId = post.GetProperty("postId").GetInt32();
            if (post.GetProperty("compatibleDietTypes").GetArrayLength() != 0) throw new Exception("A plain post must not claim a vegetarian type");
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
            var commentReport = await Send("POST", $"/api/reports/comments/{commentId}", 201, new {reason="Test comment report"}, user);
            await Send("POST", $"/api/admin/moderation/{commentReport.GetProperty("flagId").GetInt32()}/remove", 204, token: admin);
            var comments = await Send("GET", "/api/admin/comments", 200, token: admin);
            if (comments[0].GetProperty("status").GetString() != "REMOVED") throw new Exception("Moderation did not remove comment");
            await Send("POST", $"/api/admin/moderation/{reportId}/remove", 409, token: admin);
            await Send("DELETE", $"/api/admin/comments/{commentId}", 204, token: admin);
            await Send("DELETE", $"/api/admin/posts/{postId}", 204, token: admin);
            await Send("GET", $"/api/posts/{postId}", 404);
            await Send("DELETE", "/api/admin/posts/9999", 404, token: admin);
            await Send("DELETE", "/api/admin/comments/9999", 404, token: admin);
            await Send("POST", "/api/admin/moderation/9999/approve", 404, token: admin);
            await Send("POST", "/api/admin/moderation/9999/remove", 404, token: admin);

            var ingredientCategories = await Send("GET", "/api/ingredient-categories", 200, token: admin);
            var plantId = ingredientCategories.EnumerateArray().First(x=>x.GetProperty("code").GetString()=="PLANT").GetProperty("id").GetInt32();
            var dairyId = ingredientCategories.EnumerateArray().First(x=>x.GetProperty("code").GetString()=="DAIRY").GetProperty("id").GetInt32();
            var vegetables = await Send("POST", "/api/admin/ingredient-categories", 201, new {code="VEGETABLES",name="Vegetables",dietaryGroup="PLANT"}, admin);
            var vegetableId = vegetables.GetProperty("id").GetInt32();
            await Send("POST", "/api/admin/ingredient-categories", 409, new {code="VEGETABLES",name="Other",dietaryGroup="PLANT"}, admin);
            await Send("POST", "/api/admin/ingredient-categories", 400, new {code="BAD",name="Bad",dietaryGroup="MEAT"}, admin);
            await Send("PUT", $"/api/admin/ingredient-categories/{vegetableId}", 200, new {code="VEGETABLES",name="Fresh vegetables",dietaryGroup="PLANT"}, admin);
            await Send("PUT", $"/api/admin/ingredient-categories/{plantId}", 400, new {code="PLANT",name="Plants",dietaryGroup="DAIRY"}, admin);
            var bmiTypes = await Send("GET", "/api/bmi-types", 200);
            var bmiId = bmiTypes[0].GetProperty("id").GetInt32();
            var recipe = await Send("POST", "/api/recipes", 201, new { title="Recipe post",content="Test",recipeName="Tofu dish",imageUrl="https://res.cloudinary.com/test/image/upload/v1/tofu.jpg",ingredients=new[]{new{ingredientName="Test tofu",amount="100g",dietaryGroup="PLANT"}},steps=new[]{new{stepNumber=1,instruction="Cook"}},bmiTypeIds=new[]{bmiId} }, admin);
            var recipeId = recipe.GetProperty("postId").GetInt32();
            if (recipe.GetProperty("imageUrl").GetString() != "https://res.cloudinary.com/test/image/upload/v1/tofu.jpg") throw new Exception("Recipe image URL was not saved");
            await Send("POST", "/api/recipes", 400, new { title="Bad image",content="Test",recipeName="Tofu dish",imageUrl="http://example.com/tofu.jpg",ingredients=new[]{new{ingredientName="Test tofu",amount="100g",dietaryGroup="PLANT"}} }, admin);
            var recipePost = await Send("GET", $"/api/posts/{recipeId}", 200);
            if (recipePost.GetProperty("thumbnailUrl").GetString() != "https://res.cloudinary.com/test/image/upload/v1/tofu.jpg") throw new Exception("Recipe image was not shown on the post");
            if (recipePost.GetProperty("compatibleDietTypes").GetArrayLength() == 0 || !recipePost.GetProperty("compatibleDietTypes").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "VEGAN")) throw new Exception("Plant recipe post did not expose its compatible diet types");
            var grainsId = all.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == "mon-tu-ngu-coc").GetProperty("id").GetInt32();
            await Send("PUT", $"/api/admin/recipes/{recipeId}/categories", 204, new { categoryIds=new[]{grainsId} }, admin);
            var mappedRecipes = await Send("GET", "/api/admin/recipes/bmi-types", 200, token: admin);
            if (!mappedRecipes.EnumerateArray().Single(x => x.GetProperty("postId").GetInt32() == recipeId).GetProperty("categoryIds").EnumerateArray().Any(x => x.GetInt32() == grainsId)) throw new Exception("Recipe category assignment did not persist");
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
            await Send("GET", "/api/admin/recipes/bmi-types", 200, token: admin);
            await Send("PUT", $"/api/admin/recipes/{recipeId}/bmi-types", 204, new {bmiTypeIds=new[]{bmiId,bmiId}}, admin);
            await Send("PUT", $"/api/admin/recipes/{recipeId}/bmi-types", 400, new {bmiTypeIds=new[]{9999}}, admin);
            await Send("PUT", $"/api/admin/recipes/{recipeId}/bmi-types", 204, new {bmiTypeIds=Array.Empty<int>()}, admin);
            await Send("PUT", "/api/admin/recipes/9999/bmi-types", 404, new {bmiTypeIds=Array.Empty<int>()}, admin);
            Console.WriteLine($"ADMIN API PASS: {checks} HTTP checks; {adminRoutes.Length} admin route definitions checked for guest 401 and user 403. Storage: EF InMemory; zero Aiven writes.");
        }
        finally { await app.StopAsync(); }
    }

    sealed class NoEmail : IEmailSender
    {
        public Task SendOtpAsync(string recipientEmail, string purpose, string otpCode, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Email is not part of admin API tests.");
    }

    sealed class TestAllergenValidator : IAllergenNameValidator
    {
        public Task<ValidatedAllergenName> ValidateAsync(string input, IReadOnlyCollection<Allergen> existing, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidatedAllergenName(input));
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

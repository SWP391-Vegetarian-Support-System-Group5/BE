using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ModerationService(IRepository<ModerationFlag> flags, IRepository<Post> posts, IRepository<Comment> comments, IUserNotificationService notifications, ICommunityEventPublisher events) : IModerationService
{
    public Task<ModerationFlagResponse> ReportPostAsync(int postId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(postId, null, userId, request, cancellationToken);
    public Task<ModerationFlagResponse> ReportCommentAsync(int commentId, int userId, ReportRequest request, CancellationToken cancellationToken = default) => ReportAsync(null, commentId, userId, request, cancellationToken);
    public async Task<IReadOnlyCollection<ModerationFlagResponse>> GetAsync(CancellationToken cancellationToken = default) => (await flags.Query().OrderByDescending(x => x.FlagId).ToListAsync(cancellationToken)).Select(ToResponse).ToList();
    public async Task<ModerationFlagResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default) => ToResponse(await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404));
    public async Task ReviewAsync(int id, int adminId, bool removeContent, CancellationToken cancellationToken = default)
    {
        var flag = await flags.FindAsync(id) ?? throw new ServiceException("Moderation flag was not found.", 404);
        if (flag.Status != "PENDING") throw new ServiceException("Moderation flag was already reviewed.", 409);
        flag.ReviewedBy = adminId; flag.Status = removeContent ? "REMOVED" : "APPROVED";
        var post = removeContent && flag.PostId.HasValue ? await posts.FindAsync(flag.PostId.Value) : null;
        var comment = removeContent && flag.CommentId.HasValue ? await comments.FindAsync(flag.CommentId.Value) : null;
        if (post?.Status == "REMOVED") post = null;
        if (comment?.Status == "REMOVED") comment = null;
        if (post is not null) { post.Status = "REMOVED"; post.UpdatedAt = VietnamTime.Now; }
        if (comment is not null) { comment.Status = "REMOVED"; comment.UpdatedAt = VietnamTime.Now; }
        await flags.SaveChangesAsync(cancellationToken);
        if (post is not null)
        {
            await events.PublishAsync(new CommunityEvent("postRemoved", post.PostId), cancellationToken);
            if (post.UserId != adminId) await notifications.CreateAsync(post.UserId, "POST_REMOVED", "Bài viết của bạn đã bị quản trị viên gỡ.", post.PostId, cancellationToken);
        }
        if (comment is not null)
        {
            await events.PublishAsync(new CommunityEvent("commentRemoved", comment.PostId), cancellationToken);
            if (comment.UserId != adminId) await notifications.CreateAsync(comment.UserId, "COMMENT_REMOVED", "Bình luận của bạn đã bị quản trị viên gỡ.", comment.PostId, cancellationToken);
        }
    }
    private async Task<ModerationFlagResponse> ReportAsync(int? postId, int? commentId, int userId, ReportRequest request, CancellationToken ct)
    {
        if (postId.HasValue && !await posts.Query().AnyAsync(x => x.PostId == postId, ct)) throw new ServiceException("Post was not found.", 404);
        if (commentId.HasValue && !await comments.Query().AnyAsync(x => x.CommentId == commentId, ct)) throw new ServiceException("Comment was not found.", 404);
        var flag = new ModerationFlag { PostId = postId, CommentId = commentId, Source = "USER_REPORT", ReportedBy = userId, Reason = request.Reason.Trim(), Status = "PENDING" };
        await flags.AddAsync(flag, ct); await flags.SaveChangesAsync(ct); return ToResponse(flag);
    }
    private static ModerationFlagResponse ToResponse(ModerationFlag x) => new(x.FlagId, x.PostId, x.CommentId, x.Source, x.Reason, x.Status, x.ReportedBy, x.ReviewedBy);
}

public class SeedService(
    IRepository<DietType> dietTypes,
    IRepository<Allergen> allergens,
    IRepository<User> users,
    IRepository<Post> posts,
    IRepository<Recipe> recipes,
    IRepository<RecipeIngredient> recipeIngredients,
    IRepository<RecipeStep> recipeSteps,
    IRepository<RecipeDietCompatibility> recipeDietCompatibilities,
    RecipeCategoryService recipeCategories,
    IngredientCatalogService ingredientCatalog) : ISeedService
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await AddMissingAsync(dietTypes, ["VEGAN", "VEGETARIAN", "LACTO_VEGETARIAN", "OVO_VEGETARIAN", "LACTO_OVO_VEGETARIAN"], name => new DietType { Name = name }, x => x.Name, cancellationToken);
        await AddMissingAsync(allergens, ["Peanut", "Milk", "Egg", "Soy", "Gluten", "Tree Nut"], name => new Allergen { Name = name }, x => x.Name, cancellationToken);
        if (!await users.Query().AnyAsync(x => x.Email == "admin@vegetarian.local", cancellationToken))
        {
            var now = DateTime.UtcNow;
            await users.AddAsync(new User { Email = "admin@vegetarian.local", PasswordHash = PasswordHasher.Hash("Admin@123"), Role = "ADMIN", Profile = new UserProfile { FullName = "System Administrator" }, CreatedAt = now, UpdatedAt = now, IsActive = true, IsEmailVerified = true }, cancellationToken);
            await users.SaveChangesAsync(cancellationToken);
        }
        await SeedRecipeCatalogAsync(cancellationToken);
        await ingredientCatalog.RefreshCompatibilityAsync(cancellationToken);
        await recipeCategories.SeedAsync(cancellationToken);
    }
    private static async Task AddMissingAsync<T>(IRepository<T> repository, IEnumerable<string> names, Func<string, T> create, Func<T, string> getName, CancellationToken ct) where T : class
    {
        var existing = (await repository.Query().ToListAsync(ct)).Select(getName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names.Where(name => !existing.Contains(name))) await repository.AddAsync(create(name), ct);
        await repository.SaveChangesAsync(ct);
    }

    private async Task SeedRecipeCatalogAsync(CancellationToken ct)
    {
        var authors = await users.Query().Where(x => x.Role == "USER")
            .Select(x => new { x.UserId, PostCount = x.Posts.Count }).ToListAsync(ct);
        if (authors.Count == 0) return;
        var authorPostCounts = authors.ToDictionary(x => x.UserId, x => x.PostCount);
        var dietIds = await dietTypes.Query().ToDictionaryAsync(x => x.Name, x => x.DietTypeId, StringComparer.OrdinalIgnoreCase, ct);
        var allergenIds = await allergens.Query().ToDictionaryAsync(x => x.Name, x => x.AllergenId, StringComparer.OrdinalIgnoreCase, ct);
        var existingTitles = await posts.Query().Select(x => x.Title).ToListAsync(ct);
        var existing = existingTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = VietnamTime.Now;

        foreach (var seed in SeedRecipes.Where(seed => !existing.Contains(seed.Title)))
        {
            var authorId = authorPostCounts.OrderBy(x => x.Value).ThenBy(x => x.Key).First().Key;
            var post = new Post
            {
                UserId = authorId,
                Title = seed.Title,
                Content = seed.Description,
                PostType = "RECIPE",
                Status = "PUBLISHED",
                CreatedAt = now,
                UpdatedAt = now
            };
            await posts.AddAsync(post, ct);
            await posts.SaveChangesAsync(ct);

            var recipe = new Recipe
            {
                PostId = post.PostId,
                RecipeName = seed.Title,
                PrepMinutes = seed.PrepMinutes,
                CookMinutes = seed.CookMinutes,
                Servings = seed.Servings,
                CaloriesPerServing = seed.CaloriesPerServing
            };
            await recipes.AddAsync(recipe, ct);

            var groups = seed.Ingredients.Select(x => x.Group).ToArray();
            var resolved = await ingredientCatalog.ResolveAsync(seed.Ingredients.Select(x => new IngredientRequest(x.Name, x.Amount, x.Group, x.AllergenName is not null && allergenIds.TryGetValue(x.AllergenName, out var allergenId) ? allergenId : null)), ct);
            foreach (var ingredient in resolved)
            {
                await recipeIngredients.AddAsync(new RecipeIngredient
                {
                    RecipeId = post.PostId,
                    IngredientId = ingredient.Ingredient.IngredientId,
                    Amount = ingredient.Amount,
                }, ct);
            }

            var stepNo = 1;
            foreach (var step in seed.Steps)
                await recipeSteps.AddAsync(new RecipeStep { RecipeId = post.PostId, StepNumber = stepNo++, Instruction = step }, ct);

            foreach (var dietName in VegetarianDietRules.GetCompatibleDietTypes(groups))
                if (dietIds.TryGetValue(dietName, out var dietId))
                    await recipeDietCompatibilities.AddAsync(new RecipeDietCompatibility { RecipeId = post.PostId, DietTypeId = dietId, IsCompatible = true }, ct);

            await recipes.SaveChangesAsync(ct);
            authorPostCounts[authorId]++;
        }
    }

    private sealed record SeedIngredient(string Name, string Amount, string Group = VegetarianDietRules.Plant, string? AllergenName = null);
    private sealed record SeedRecipe(string Title, string Description, int PrepMinutes, int CookMinutes, decimal Servings, decimal CaloriesPerServing, SeedIngredient[] Ingredients, string[] Steps);

    private static readonly SeedRecipe[] SeedRecipes =
    [
        R("Mushroom Ginger Rice Bowl", "Brown rice with mixed mushrooms, ginger and herbs.", 10, 20, 2, 420, [I("brown rice","2 cups"), I("mushroom","250g"), I("ginger","1 tbsp"), I("bok choy","150g")]),
        R("Lemongrass Mushroom Vermicelli", "Rice vermicelli topped with lemongrass mushrooms and greens.", 15, 20, 2, 390, [I("rice vermicelli","180g"), I("mushroom","220g"), I("lemongrass","2 stalks"), I("lettuce","100g")]),
        R("Tomato Mushroom Claypot", "A savory claypot stew with tomato, mushroom and eggplant.", 12, 28, 3, 360, [I("tomato","3"), I("mushroom","250g"), I("eggplant","1"), I("spring onion","2 stalks")]),
        R("Pumpkin Coconut Soup", "Creamy pumpkin soup with coconut milk and basil.", 10, 25, 3, 310, [I("pumpkin","400g"), I("coconut milk","150ml"), I("basil","1 handful"), I("carrot","1")]),
        R("Lotus Root Mushroom Stir Fry", "Crisp lotus root tossed with mushrooms and sesame.", 15, 15, 2, 330, [I("lotus root","250g"), I("mushroom","200g"), I("sesame seed","1 tbsp"), I("snow pea","120g")]),
        R("Chickpea Spinach Curry", "Plant protein curry with chickpeas and spinach.", 10, 25, 3, 440, [I("chickpea","250g"), I("spinach","180g"), I("tomato","2"), I("coconut milk","120ml")]),
        R("Red Lentil Sweet Potato Dal", "Comforting dal with sweet potato and turmeric.", 10, 30, 4, 450, [I("red lentil","250g"), I("sweet potato","300g"), I("turmeric","1 tsp"), I("cilantro","1 handful")]),
        R("Garden Vegetable Fried Rice", "Fast fried rice with corn, carrot and peas.", 10, 15, 2, 410, [I("rice","2 cups"), I("corn","80g"), I("carrot","1"), I("green pea","80g")]),
        R("Bitter Melon Mushroom Soup", "Light Vietnamese-style soup with bitter melon and mushroom.", 12, 22, 3, 210, [I("bitter melon","2"), I("mushroom","180g"), I("glass noodle","50g"), I("cilantro","1 handful")]),
        R("Braised Banana Blossom", "Banana blossom braised with tomato and herbs.", 20, 25, 3, 300, [I("banana blossom","350g"), I("tomato","2"), I("perilla","1 handful"), I("mushroom","150g")]),
        R("Cauliflower Turmeric Roast", "Golden cauliflower roasted with turmeric and lime.", 10, 25, 3, 280, [I("cauliflower","1 head"), I("turmeric","1 tsp"), I("lime","1"), I("cucumber","1")]),
        R("Kabocha Mushroom Porridge", "Soft kabocha rice porridge with mushrooms.", 10, 30, 3, 340, [I("kabocha","300g"), I("rice","1 cup"), I("mushroom","160g"), I("ginger","1 tsp")]),
        R("Herb Rice Paper Rolls", "Fresh rolls with herbs, avocado and vegetables.", 25, 0, 2, 290, [I("rice paper","10 sheets"), I("avocado","1"), I("lettuce","150g"), I("mint","1 handful")]),
        R("Mung Bean Coconut Pudding", "Soft mung bean dessert with coconut and pandan.", 10, 30, 4, 260, [I("mung bean","200g"), I("coconut milk","150ml"), I("pandan","2 leaves"), I("cane sugar","40g")]),
        R("Stir Fried Morning Glory", "Crisp morning glory with garlic and chili.", 10, 8, 2, 180, [I("morning glory","300g"), I("garlic","3 cloves"), I("chili","1"), I("lime","1")]),
        R("Mushroom Pho Broth", "Aromatic pho broth with mushrooms and rice noodles.", 20, 45, 4, 380, [I("rice noodle","300g"), I("mushroom","300g"), I("star anise","3"), I("cinnamon","1 stick")]),
        R("Vegetable Hotpot", "Clear vegetable hotpot with mushrooms and greens.", 20, 25, 4, 330, [I("mushroom","300g"), I("napa cabbage","300g"), I("daikon","1"), I("corn","1")]),
        R("Eggplant Basil Rice", "Pan-seared eggplant with basil over rice.", 10, 18, 2, 400, [I("eggplant","2"), I("basil","1 handful"), I("rice","2 cups"), I("tomato","1")]),
        R("Okra Tomato Stew", "Okra simmered with tomato and herbs.", 10, 20, 3, 240, [I("okra","250g"), I("tomato","3"), I("onion","1"), I("cilantro","1 handful")]),
        R("Jackfruit Lemongrass Bowl", "Young jackfruit stir-fried with lemongrass.", 15, 22, 3, 370, [I("young jackfruit","350g"), I("lemongrass","2 stalks"), I("rice","2 cups"), I("cucumber","1")]),
        R("Seaweed Mushroom Soup", "Mineral-rich soup with seaweed and mushrooms.", 8, 15, 2, 160, [I("seaweed","20g"), I("mushroom","180g"), I("daikon","150g"), I("ginger","1 tsp")]),
        R("Tofu Tomato Skillet", "Classic tofu simmered in tomato sauce.", 10, 20, 2, 360, [I("tofu","300g", VegetarianDietRules.Plant, "Soy"), I("tomato","3"), I("spring onion","2 stalks"), I("rice","2 cups")]),
        R("Tofu Mushroom Lettuce Cups", "Crisp lettuce cups with tofu and mushrooms.", 15, 15, 2, 330, [I("tofu","250g", VegetarianDietRules.Plant, "Soy"), I("mushroom","180g"), I("lettuce","1 head"), I("carrot","1")]),
        R("Soy Milk Oat Porridge", "Warm breakfast porridge with soy milk and banana.", 5, 12, 2, 320, [I("soy milk","300ml", VegetarianDietRules.Plant, "Soy"), I("oat","100g", VegetarianDietRules.Plant, "Gluten"), I("banana","1"), I("chia seed","1 tbsp")]),
        R("Peanut Mushroom Noodles", "Rice noodles with mushrooms and peanut sauce.", 15, 15, 2, 430, [I("rice noodle","200g"), I("mushroom","180g"), I("peanut","40g", VegetarianDietRules.Plant, "Peanut"), I("cucumber","1")]),
        R("Cashew Vegetable Curry", "Vegetable curry finished with cashew cream.", 15, 25, 3, 470, [I("cashew","60g", VegetarianDietRules.Plant, "Tree Nut"), I("potato","2"), I("carrot","1"), I("coconut milk","150ml")]),
        R("Sesame Tofu Soba", "Buckwheat soba with sesame tofu and herbs.", 15, 12, 2, 430, [I("soba noodle","200g", VegetarianDietRules.Plant, "Gluten"), I("tofu","220g", VegetarianDietRules.Plant, "Soy"), I("sesame seed","1 tbsp"), I("cucumber","1")]),
        R("Cheesy Mushroom Omelet Rice", "Lacto-ovo vegetarian rice with mushrooms.", 10, 15, 1, 520, [I("egg","2", VegetarianDietRules.Egg, "Egg"), I("cheese","30g", VegetarianDietRules.Dairy, "Milk"), I("mushroom","120g"), I("rice","1 cup")]),
        R("Yogurt Herb Cucumber Bowl", "Cooling cucumber rice bowl with yogurt herbs.", 10, 0, 1, 310, [I("yogurt","120g", VegetarianDietRules.Dairy, "Milk"), I("cucumber","1"), I("rice","1 cup"), I("mint","1 handful")]),
        R("Honey Roasted Carrots", "Carrots roasted with honey and thyme.", 10, 25, 2, 220, [I("carrot","400g"), I("honey","1 tbsp", VegetarianDietRules.Honey), I("thyme","1 tsp"), I("lime","1")]),
        R("Miso Eggplant Rice", "Miso-glazed eggplant with steamed rice.", 10, 22, 2, 420, [I("eggplant","2"), I("miso","1 tbsp", VegetarianDietRules.Plant, "Soy"), I("rice","2 cups"), I("spring onion","2 stalks")]),
        R("Almond Pumpkin Salad", "Roasted pumpkin salad with almonds.", 12, 25, 2, 360, [I("pumpkin","350g"), I("almond","35g", VegetarianDietRules.Plant, "Tree Nut"), I("lettuce","120g"), I("orange","1")]),
        R("Wheat Noodle Mushroom Soup", "Wheat noodles in a mushroom broth.", 10, 20, 2, 390, [I("wheat noodle","200g", VegetarianDietRules.Plant, "Gluten"), I("mushroom","220g"), I("bok choy","150g"), I("ginger","1 tsp")]),
        R("Crispy Tofu Turmeric Rice", "Turmeric rice topped with crispy tofu.", 10, 20, 2, 450, [I("tofu","280g", VegetarianDietRules.Plant, "Soy"), I("rice","2 cups"), I("turmeric","1 tsp"), I("cucumber","1")]),
        R("Edamame Corn Rice", "Quick rice bowl with edamame and corn.", 8, 15, 2, 400, [I("edamame","150g", VegetarianDietRules.Plant, "Soy"), I("corn","100g"), I("rice","2 cups"), I("carrot","1")]),
        R("Walnut Herb Pasta", "Herb pasta with toasted walnuts.", 10, 15, 2, 480, [I("pasta","200g", VegetarianDietRules.Plant, "Gluten"), I("walnut","40g", VegetarianDietRules.Plant, "Tree Nut"), I("basil","1 handful"), I("tomato","2")]),
        R("Rice Noodle Mango Salad", "Bright rice noodle salad with mango and herbs.", 15, 5, 2, 330, [I("rice noodle","180g"), I("mango","1"), I("mint","1 handful"), I("cucumber","1")]),
        R("Daikon Carrot Clear Soup", "Simple clear soup with daikon and carrot.", 10, 25, 3, 170, [I("daikon","1"), I("carrot","2"), I("mushroom","120g"), I("cilantro","1 handful")]),
        R("Sweet Corn Mushroom Chowder", "Dairy-free chowder with corn and mushrooms.", 10, 22, 3, 300, [I("corn","2 ears"), I("mushroom","180g"), I("potato","1"), I("coconut milk","100ml")]),
        R("Water Spinach Rice Bowl", "Garlicky water spinach over warm rice.", 8, 10, 2, 310, [I("water spinach","300g"), I("garlic","3 cloves"), I("rice","2 cups"), I("chili","1")]),
        R("Tamarind Vegetable Soup", "Sour tamarind soup with pineapple and okra.", 12, 20, 3, 240, [I("tamarind","2 tbsp"), I("pineapple","150g"), I("okra","150g"), I("tomato","2")]),
        R("Mushroom Stuffed Bell Peppers", "Bell peppers filled with mushroom rice.", 15, 30, 3, 390, [I("bell pepper","3"), I("mushroom","220g"), I("rice","2 cups"), I("parsley","1 handful")]),
        R("Cabbage Mushroom Rolls", "Cabbage leaves wrapped around mushroom rice.", 20, 25, 3, 350, [I("cabbage","1 head"), I("mushroom","220g"), I("rice","2 cups"), I("carrot","1")]),
        R("Coconut Green Bean Stew", "Green beans simmered with coconut and tomato.", 10, 24, 3, 310, [I("green bean","250g"), I("coconut milk","120ml"), I("tomato","2"), I("ginger","1 tsp")]),
        R("Roasted Beetroot Rice Plate", "Earthy roasted beets with rice and herbs.", 10, 35, 2, 360, [I("beetroot","3"), I("rice","2 cups"), I("lettuce","100g"), I("lime","1")]),
        R("Mushroom Pineapple Fried Rice", "Sweet-sour fried rice with pineapple and mushrooms.", 10, 15, 2, 420, [I("pineapple","150g"), I("mushroom","180g"), I("rice","2 cups"), I("green pea","80g")]),
        R("Black Bean Vegetable Chili", "Hearty chili with black beans and vegetables.", 10, 30, 4, 460, [I("black bean","260g"), I("tomato","3"), I("corn","100g"), I("bell pepper","1")]),
        R("Lentil Mushroom Lettuce Bowl", "Protein-rich lentil bowl with mushrooms.", 10, 25, 3, 430, [I("lentil","240g"), I("mushroom","200g"), I("lettuce","120g"), I("cucumber","1")]),
        R("Ginger Carrot Rice Soup", "Light rice soup with carrot and ginger.", 8, 20, 2, 260, [I("rice","1 cup"), I("carrot","2"), I("ginger","1 tbsp"), I("cilantro","1 handful")]),
        R("Herbed Potato Mushroom Bake", "Baked potatoes and mushrooms with herbs.", 12, 35, 3, 390, [I("potato","4"), I("mushroom","250g"), I("rosemary","1 tsp"), I("tomato","1")])
    ];

    private static SeedRecipe R(string title, string description, int prep, int cook, decimal servings, decimal calories, SeedIngredient[] ingredients, string[]? steps = null) =>
        new(title, description, prep, cook, servings, calories, ingredients, steps ?? ["Prepare and wash all vegetables.", "Cook the main ingredients until tender and fragrant.", "Season to taste and serve warm."]);

    private static SeedIngredient I(string name, string amount, string group = VegetarianDietRules.Plant, string? allergenName = null) => new(name, amount, group, allergenName);
}

public class HealthService(IRepository<DietType> dietTypes) : IHealthService
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await dietTypes.Query().Select(x => x.DietTypeId).Take(1).ToListAsync(cancellationToken);
        return true;
    }
}

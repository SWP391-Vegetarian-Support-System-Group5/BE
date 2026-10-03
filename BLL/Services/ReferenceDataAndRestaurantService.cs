using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ReferenceDataAndRestaurantService(
    IRepository<DietType> dietTypes,
    IRepository<Category> categories,
    IRepository<Allergen> allergens,
    IRepository<Tag> tags,
    IRepository<Restaurant> restaurants,
    IRepository<RestaurantFood> restaurantFoods,
    IRepository<Post> posts,
    IRepository<RestaurantReview> restaurantReviews,
    IRepository<User> users,
    IRepository<RecipeDietCompatibility> recipeDietCompatibilities,
    IRepository<UserAllergen> userAllergens,
    IRepository<RecipeIngredient> recipeIngredients,
    IRepository<PostTag> postTags) : IReferenceDataAndRestaurantService
{
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetDietTypesAsync(CancellationToken cancellationToken = default) =>
        await dietTypes.Query().OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.DietTypeId, x.Name, null)).ToListAsync(cancellationToken);
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await categories.Query().OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.CategoryId, x.Name, x.Type)).ToListAsync(cancellationToken);
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergensAsync(CancellationToken cancellationToken = default) =>
        await allergens.Query().OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.AllergenId, x.Name, null)).ToListAsync(cancellationToken);
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetTagsAsync(CancellationToken cancellationToken = default) =>
        await tags.Query().OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.TagId, x.Name, null)).ToListAsync(cancellationToken);

    public async Task<CatalogItemResponse> CreateDietTypeAsync(DietTypeRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new DietType { Name = request.Name.Trim().ToUpperInvariant() };
        await EnsureUniqueAsync(dietTypes.Query(), entity.Name, x => x.Name, cancellationToken);
        await dietTypes.AddAsync(entity, cancellationToken); await dietTypes.SaveChangesAsync(cancellationToken);
        return new(entity.DietTypeId, entity.Name);
    }
    public async Task<CatalogItemResponse> CreateCategoryAsync(CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Category { Name = request.Name.Trim(), Type = FixedValues.CategoryType(request.Type) };
        await categories.AddAsync(entity, cancellationToken); await categories.SaveChangesAsync(cancellationToken);
        return new(entity.CategoryId, entity.Name, entity.Type);
    }
    public async Task<CatalogItemResponse> CreateAllergenAsync(AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Allergen { Name = request.Name.Trim() };
        await EnsureUniqueAsync(allergens.Query(), entity.Name, x => x.Name, cancellationToken);
        await allergens.AddAsync(entity, cancellationToken); await allergens.SaveChangesAsync(cancellationToken);
        return new(entity.AllergenId, entity.Name);
    }
    public async Task<CatalogItemResponse> CreateTagAsync(TagRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Tag { Name = request.Name.Trim() };
        await EnsureUniqueAsync(tags.Query(), entity.Name, x => x.Name, cancellationToken);
        await tags.AddAsync(entity, cancellationToken); await tags.SaveChangesAsync(cancellationToken);
        return new(entity.TagId, entity.Name);
    }

    public async Task UpdateDietTypeAsync(int id, DietTypeRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(dietTypes, id, cancellationToken); var name = request.Name.Trim().ToUpperInvariant();
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) await EnsureUniqueAsync(dietTypes.Query().Where(x => x.DietTypeId != id), name, x => x.Name, cancellationToken);
        entity.Name = name; await dietTypes.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateCategoryAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(categories, id, cancellationToken); entity.Name = request.Name.Trim(); entity.Type = FixedValues.CategoryType(request.Type); await categories.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateAllergenAsync(int id, AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(allergens, id, cancellationToken); var name = request.Name.Trim();
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) await EnsureUniqueAsync(allergens.Query().Where(x => x.AllergenId != id), name, x => x.Name, cancellationToken);
        entity.Name = name; await allergens.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateTagAsync(int id, TagRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(tags, id, cancellationToken); var name = request.Name.Trim();
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) await EnsureUniqueAsync(tags.Query().Where(x => x.TagId != id), name, x => x.Name, cancellationToken);
        entity.Name = name; await tags.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDietTypeAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireAsync(dietTypes, id, cancellationToken);
        if (await users.Query().AnyAsync(x => x.DietTypeId == id, cancellationToken) || await restaurants.Query().AnyAsync(x => x.DietTypeId == id, cancellationToken) || await recipeDietCompatibilities.Query().AnyAsync(x => x.DietTypeId == id, cancellationToken)) throw new ServiceException("Diet type is in use and cannot be deleted.", 409);
        await DeleteAsync(dietTypes, id, cancellationToken);
    }
    public async Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireAsync(categories, id, cancellationToken);
        if (await posts.Query().AnyAsync(x => x.CategoryId == id, cancellationToken)) throw new ServiceException("Category is in use and cannot be deleted.", 409);
        await DeleteAsync(categories, id, cancellationToken);
    }
    public async Task DeleteAllergenAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireAsync(allergens, id, cancellationToken);
        if (await userAllergens.Query().AnyAsync(x => x.AllergenId == id, cancellationToken) || await recipeIngredients.Query().AnyAsync(x => x.AllergenId == id, cancellationToken)) throw new ServiceException("Allergen is in use and cannot be deleted.", 409);
        await DeleteAsync(allergens, id, cancellationToken);
    }
    public async Task DeleteTagAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireAsync(tags, id, cancellationToken);
        if (await postTags.Query().AnyAsync(x => x.TagId == id, cancellationToken)) throw new ServiceException("Tag is in use and cannot be deleted.", 409);
        await DeleteAsync(tags, id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurantsAsync(string? food, CancellationToken cancellationToken = default)
    {
        var query = restaurants.Query().Include(x => x.Foods).AsQueryable();
        if (!string.IsNullOrWhiteSpace(food)) { var term = food.Trim().ToLower(); query = query.Where(x => x.Name.ToLower().Contains(term) || x.Foods.Any(f => f.FoodName.ToLower().Contains(term))); }
        return (await query.OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(ToRestaurant).ToList();
    }
    public async Task<RestaurantResponse?> GetRestaurantAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.Query().Include(x => x.Foods).SingleOrDefaultAsync(x => x.RestaurantId == id, cancellationToken);
        return entity is null ? null : ToRestaurant(entity);
    }
    public async Task<IReadOnlyCollection<RestaurantResponse>> GetNearbyRestaurantsAsync(decimal latitude, decimal longitude, decimal radiusKm, CancellationToken cancellationToken = default)
    {
        if (radiusKm <= 0) throw new ServiceException("Radius must be greater than zero.");
        var candidates = await restaurants.Query().Include(x => x.Foods).Where(x => x.Latitude.HasValue && x.Longitude.HasValue).ToListAsync(cancellationToken);
        return candidates.Where(x => DistanceKm(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value) <= radiusKm).OrderBy(x => DistanceKm(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value)).Select(ToRestaurant).ToList();
    }
    public async Task<RestaurantResponse> CreateRestaurantAsync(RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        var entity = new Restaurant { Name = request.Name.Trim(), Address = request.Address.Trim(), Latitude = request.Latitude, Longitude = request.Longitude, DietTypeId = request.DietTypeId };
        await restaurants.AddAsync(entity, cancellationToken); await restaurants.SaveChangesAsync(cancellationToken);
        await ReplaceFoodsAsync(entity.RestaurantId, request.Foods, cancellationToken); return (await GetRestaurantAsync(entity.RestaurantId, cancellationToken))!;
    }
    public async Task UpdateRestaurantAsync(int id, RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        entity.Name = request.Name.Trim(); entity.Address = request.Address.Trim(); entity.Latitude = request.Latitude; entity.Longitude = request.Longitude; entity.DietTypeId = request.DietTypeId;
        await restaurants.SaveChangesAsync(cancellationToken); await ReplaceFoodsAsync(id, request.Foods, cancellationToken);
    }
    public async Task DeleteRestaurantAsync(int id, CancellationToken cancellationToken = default)
    {
        var restaurant = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        foreach (var food in await restaurantFoods.Query().Where(x => x.RestaurantId == id).ToListAsync(cancellationToken))
            restaurantFoods.Remove(food);
        foreach (var review in await restaurantReviews.Query().Where(x => x.RestaurantId == id).ToListAsync(cancellationToken))
            restaurantReviews.Remove(review);
        await restaurantFoods.SaveChangesAsync(cancellationToken);
        restaurants.Remove(restaurant);
        await restaurants.SaveChangesAsync(cancellationToken);
    }

    public async Task<RestaurantReviewResponse> CreateRestaurantReviewAsync(int restaurantId, int userId, RestaurantReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, cancellationToken)) throw new ServiceException("Restaurant was not found.", 404);
        var post = new Post { UserId = userId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = "BLOG", Status = "PUBLISHED" };
        await posts.AddAsync(post, cancellationToken); await posts.SaveChangesAsync(cancellationToken);
        await restaurantReviews.AddAsync(new RestaurantReview { PostId = post.PostId, RestaurantId = restaurantId, Rating = request.Rating }, cancellationToken); await restaurantReviews.SaveChangesAsync(cancellationToken);
        return new(post.PostId, restaurantId, userId, post.Title, post.Content, request.Rating);
    }

    public async Task<IReadOnlyCollection<RestaurantReviewResponse>> GetRestaurantReviewsAsync(int restaurantId, CancellationToken cancellationToken = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, cancellationToken)) throw new ServiceException("Restaurant was not found.", 404);
        return await restaurantReviews.Query().Include(x => x.Post).Where(x => x.RestaurantId == restaurantId && x.Post.Status == "PUBLISHED").Select(x => new RestaurantReviewResponse(x.PostId, x.RestaurantId, x.Post.UserId, x.Post.Title, x.Post.Content, x.Rating)).ToListAsync(cancellationToken);
    }

    private async Task ReplaceFoodsAsync(int restaurantId, List<string>? foods, CancellationToken cancellationToken)
    {
        var existing = await restaurantFoods.Query().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);
        foreach (var food in existing) restaurantFoods.Remove(food);
        foreach (var name in (foods ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            await restaurantFoods.AddAsync(new RestaurantFood { RestaurantId = restaurantId, FoodName = name }, cancellationToken);
        await restaurantFoods.SaveChangesAsync(cancellationToken);
    }
    private async Task ValidateDietTypeAsync(int? dietTypeId, CancellationToken cancellationToken)
    {
        if (dietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == dietTypeId.Value, cancellationToken)) throw new ServiceException("Diet type was not found.", 404);
    }
    private static RestaurantResponse ToRestaurant(Restaurant x) => new(x.RestaurantId, x.Name, x.Address, x.Latitude, x.Longitude, x.DietTypeId, x.Foods.Select(f => f.FoodName).ToList());
    private static decimal DistanceKm(decimal lat1, decimal lon1, decimal lat2, decimal lon2)
    {
        var dLat = DegreesToRadians((double)(lat2 - lat1)); var dLon = DegreesToRadians((double)(lon2 - lon1));
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(DegreesToRadians((double)lat1)) * Math.Cos(DegreesToRadians((double)lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return (decimal)(6371d * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a)));
    }
    private static double DegreesToRadians(double value) => value * Math.PI / 180d;
    private static async Task EnsureUniqueAsync<T>(IQueryable<T> query, string value, Func<T, string> selector, CancellationToken ct) where T : class
    {
        if ((await query.ToListAsync(ct)).Any(x => string.Equals(selector(x), value, StringComparison.OrdinalIgnoreCase))) throw new ServiceException("A record with this name already exists.", 409);
    }
    private static async Task<T> RequireAsync<T>(IRepository<T> repository, int id, CancellationToken ct) where T : class => await repository.FindAsync(id) ?? throw new ServiceException("Record was not found.", 404);
    private static async Task DeleteAsync<T>(IRepository<T> repository, int id, CancellationToken ct) where T : class { var entity = await RequireAsync(repository, id, ct); repository.Remove(entity); await repository.SaveChangesAsync(ct); }
}

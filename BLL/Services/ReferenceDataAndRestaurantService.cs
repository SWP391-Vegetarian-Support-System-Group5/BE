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
    IRepository<Restaurant> restaurants,
    IRepository<RestaurantFood> restaurantFoods,
    IRepository<Post> posts,
    IRepository<RestaurantReview> restaurantReviews,
    IRepository<UserAllergen> userAllergens,
    IRepository<RecipeIngredient> recipeIngredients) : IReferenceDataAndRestaurantService
{
    public async Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypesAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        (await dietTypes.Query().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken))
            .Select(x => new DietTypeResponse(x.DietTypeId, x.Name, VegetarianDietRules.GetAllowedGroups(x.Name), VegetarianDietRules.ProhibitedNonVegetarianGroups.Concat(VegetarianDietRules.SupportedIngredientGroups.Except(VegetarianDietRules.GetAllowedGroups(x.Name))).ToArray(), x.IsActive))
            .ToList();
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        await categories.Query().OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.CategoryId, x.Name, x.Type, true)).ToListAsync(cancellationToken);
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergensAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        await allergens.Query().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.AllergenId, x.Name, null, x.IsActive)).ToListAsync(cancellationToken);
    public async Task<CatalogItemResponse> CreateCategoryAsync(CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Category { Name = request.Name.Trim(), Type = FixedValues.CategoryType(request.Type) };
        await categories.AddAsync(entity, cancellationToken); await categories.SaveChangesAsync(cancellationToken);
        return new(entity.CategoryId, entity.Name, entity.Type);
    }
    public async Task<CatalogItemResponse> CreateAllergenAsync(AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Allergen { Name = request.Name.Trim(), IsActive = request.IsActive };
        await EnsureUniqueAsync(allergens.Query(), entity.Name, x => x.Name, cancellationToken);
        await allergens.AddAsync(entity, cancellationToken); await allergens.SaveChangesAsync(cancellationToken);
        return new(entity.AllergenId, entity.Name, null, entity.IsActive);
    }

    public async Task UpdateCategoryAsync(int id, CategoryRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(categories, id, cancellationToken); entity.Name = request.Name.Trim(); entity.Type = FixedValues.CategoryType(request.Type); await categories.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateAllergenAsync(int id, AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(allergens, id, cancellationToken); var name = request.Name.Trim();
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) await EnsureUniqueAsync(allergens.Query().Where(x => x.AllergenId != id), name, x => x.Name, cancellationToken);
        entity.Name = name; entity.IsActive = request.IsActive; await allergens.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateDietTypeStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(dietTypes, id, cancellationToken); entity.IsActive = isActive; await dietTypes.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateAllergenStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(allergens, id, cancellationToken); entity.IsActive = isActive; await allergens.SaveChangesAsync(cancellationToken);
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

    public async Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurantsAsync(string? food, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = restaurants.Query().Include(x => x.Foods).Where(x => includeInactive || x.IsActive).AsQueryable();
        if (!string.IsNullOrWhiteSpace(food)) { var term = food.Trim().ToLower(); query = query.Where(x => x.Name.ToLower().Contains(term) || x.Foods.Any(f => f.FoodName.ToLower().Contains(term))); }
        return (await query.OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(ToRestaurant).ToList();
    }
    public async Task<RestaurantResponse?> GetRestaurantAsync(int id, bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.Query().Include(x => x.Foods).SingleOrDefaultAsync(x => x.RestaurantId == id && (includeInactive || x.IsActive), cancellationToken);
        return entity is null ? null : ToRestaurant(entity);
    }
    public async Task<IReadOnlyCollection<RestaurantResponse>> GetNearbyRestaurantsAsync(decimal latitude, decimal longitude, decimal radiusKm, CancellationToken cancellationToken = default)
    {
        if (radiusKm <= 0) throw new ServiceException("Radius must be greater than zero.");
        var candidates = await restaurants.Query().Include(x => x.Foods).Where(x => x.IsActive && x.Latitude.HasValue && x.Longitude.HasValue).ToListAsync(cancellationToken);
        return candidates.Where(x => DistanceKm(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value) <= radiusKm).OrderBy(x => DistanceKm(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value)).Select(ToRestaurant).ToList();
    }
    public async Task<RestaurantResponse> CreateRestaurantAsync(RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        await ValidateRestaurantAsync(null, request, cancellationToken);
        var entity = new Restaurant { Name = request.Name.Trim(), Address = request.Address.Trim(), Description = Clean(request.Description), PhoneNumber = Clean(request.PhoneNumber), OpeningHours = Clean(request.OpeningHours), ImageUrl = ValidateImageUrl(request.ImageUrl), IsActive = request.IsActive, Latitude = request.Latitude, Longitude = request.Longitude, DietTypeId = request.DietTypeId };
        await restaurants.AddAsync(entity, cancellationToken); await restaurants.SaveChangesAsync(cancellationToken);
        await ReplaceFoodsAsync(entity.RestaurantId, request.Foods, cancellationToken); return (await GetRestaurantAsync(entity.RestaurantId, true, cancellationToken))!;
    }
    public async Task UpdateRestaurantAsync(int id, RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        await ValidateRestaurantAsync(id, request, cancellationToken);
        entity.Name = request.Name.Trim(); entity.Address = request.Address.Trim(); entity.Description = Clean(request.Description); entity.PhoneNumber = Clean(request.PhoneNumber); entity.OpeningHours = Clean(request.OpeningHours); entity.ImageUrl = ValidateImageUrl(request.ImageUrl); entity.IsActive = request.IsActive; entity.Latitude = request.Latitude; entity.Longitude = request.Longitude; entity.DietTypeId = request.DietTypeId;
        await restaurants.SaveChangesAsync(cancellationToken); await ReplaceFoodsAsync(id, request.Foods, cancellationToken);
    }
    public async Task UpdateRestaurantStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        entity.IsActive = isActive; await restaurants.SaveChangesAsync(cancellationToken);
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

    public async Task<IReadOnlyCollection<RestaurantFoodResponse>> GetRestaurantFoodsAsync(int restaurantId, bool includeUnavailable = false, CancellationToken cancellationToken = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, cancellationToken)) throw new ServiceException("Restaurant was not found.", 404);
        return await restaurantFoods.Query()
            .Where(x => x.RestaurantId == restaurantId && (includeUnavailable || x.IsAvailable))
            .OrderBy(x => x.FoodName)
            .Select(x => new RestaurantFoodResponse(x.RestaurantFoodId, x.FoodName, x.Description, x.ImageUrl, x.Price, x.DietTypeId, x.IsAvailable))
            .ToListAsync(cancellationToken);
    }

    public async Task<RestaurantFoodResponse> CreateRestaurantFoodAsync(int restaurantId, RestaurantFoodRequest request, CancellationToken cancellationToken = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, cancellationToken)) throw new ServiceException("Restaurant was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        var name = request.FoodName.Trim();
        if (await restaurantFoods.Query().AnyAsync(x => x.RestaurantId == restaurantId && x.FoodName.ToLower() == name.ToLower(), cancellationToken)) throw new ServiceException("This food already exists in the restaurant.", 409);
        var entity = new RestaurantFood { RestaurantId = restaurantId, FoodName = name, Description = Clean(request.Description), ImageUrl = ValidateImageUrl(request.ImageUrl), Price = request.Price, DietTypeId = request.DietTypeId, IsAvailable = request.IsAvailable };
        await restaurantFoods.AddAsync(entity, cancellationToken); await restaurantFoods.SaveChangesAsync(cancellationToken);
        return ToRestaurantFood(entity);
    }

    public async Task<RestaurantFoodResponse> UpdateRestaurantFoodAsync(int restaurantId, int foodId, RestaurantFoodRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await restaurantFoods.Query().SingleOrDefaultAsync(x => x.RestaurantId == restaurantId && x.RestaurantFoodId == foodId, cancellationToken) ?? throw new ServiceException("Restaurant food was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        var name = request.FoodName.Trim();
        if (await restaurantFoods.Query().AnyAsync(x => x.RestaurantId == restaurantId && x.RestaurantFoodId != foodId && x.FoodName.ToLower() == name.ToLower(), cancellationToken)) throw new ServiceException("This food already exists in the restaurant.", 409);
        entity.FoodName = name; entity.Description = Clean(request.Description); entity.ImageUrl = ValidateImageUrl(request.ImageUrl); entity.Price = request.Price; entity.DietTypeId = request.DietTypeId; entity.IsAvailable = request.IsAvailable;
        await restaurantFoods.SaveChangesAsync(cancellationToken);
        return ToRestaurantFood(entity);
    }

    public async Task DeleteRestaurantFoodAsync(int restaurantId, int foodId, CancellationToken cancellationToken = default)
    {
        var entity = await restaurantFoods.Query().SingleOrDefaultAsync(x => x.RestaurantId == restaurantId && x.RestaurantFoodId == foodId, cancellationToken) ?? throw new ServiceException("Restaurant food was not found.", 404);
        restaurantFoods.Remove(entity); await restaurantFoods.SaveChangesAsync(cancellationToken);
    }

    public async Task<RestaurantReviewResponse> CreateRestaurantReviewAsync(int restaurantId, int userId, RestaurantReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, cancellationToken)) throw new ServiceException("Restaurant was not found.", 404);
        var now = VietnamTime.Now;
        var post = new Post { UserId = userId, Title = request.Title.Trim(), Content = request.Content.Trim(), PostType = "BLOG", Status = "PUBLISHED", CreatedAt = now, UpdatedAt = now };
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
        // A null list means "leave foods unchanged"; send an empty list to remove all foods.
        if (foods is null) return;
        var existing = await restaurantFoods.Query().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);
        foreach (var food in existing) restaurantFoods.Remove(food);
        foreach (var name in (foods ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            await restaurantFoods.AddAsync(new RestaurantFood { RestaurantId = restaurantId, FoodName = name, IsAvailable = true }, cancellationToken);
        await restaurantFoods.SaveChangesAsync(cancellationToken);
    }
    private async Task ValidateDietTypeAsync(int? dietTypeId, CancellationToken cancellationToken)
    {
        if (dietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == dietTypeId.Value && x.IsActive, cancellationToken)) throw new ServiceException("Diet type was not found or is inactive.", 404);
    }
    private async Task ValidateRestaurantAsync(int? id, RestaurantRequest request, CancellationToken ct)
    {
        var name = request.Name.Trim().ToLower(); var address = request.Address.Trim().ToLower();
        if (await restaurants.Query().AnyAsync(x => x.RestaurantId != (id ?? 0) && x.Name.ToLower() == name && x.Address.ToLower() == address, ct)) throw new ServiceException("A restaurant with the same name and address already exists.", 409);
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? ValidateImageUrl(string? value)
    {
        value = Clean(value);
        if (value is null || value.StartsWith('/')) return value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) throw new ServiceException("Image URL must be HTTP, HTTPS, or an application-relative path.");
        return value;
    }
    private static RestaurantFoodResponse ToRestaurantFood(RestaurantFood x) => new(x.RestaurantFoodId, x.FoodName, x.Description, x.ImageUrl, x.Price, x.DietTypeId, x.IsAvailable);
    private static RestaurantResponse ToRestaurant(Restaurant x) => new(x.RestaurantId, x.Name, x.Address, x.Latitude, x.Longitude, x.DietTypeId, x.Foods.Select(f => f.FoodName).ToList(), x.Description, x.PhoneNumber, x.OpeningHours, x.ImageUrl, x.IsActive, x.Foods.Select(ToRestaurantFood).ToList());
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

using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Data;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ReferenceDataAndRestaurantService(
    IRepository<DietType> dietTypes,
    IRepository<Allergen> allergens,
    IRepository<Restaurant> restaurants,
    IRepository<RestaurantDietType> restaurantDietTypes,
    IRepository<RestaurantFood> restaurantFoods,
    IRepository<RestaurantIngredient> restaurantIngredients,
    IRepository<UserAllergen> userAllergens,
    IRepository<Ingredient> ingredients,
    VegetarianDbContext db) : IReferenceDataAndRestaurantService
{
    public async Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypesAsync(CancellationToken cancellationToken = default) =>
        (await dietTypes.Query().OrderBy(x => x.Name).ToListAsync(cancellationToken))
            .Select(x => new DietTypeResponse(x.DietTypeId, x.Name, VegetarianDietRules.GetAllowedGroups(x.Name), VegetarianDietRules.ProhibitedNonVegetarianGroups.Concat(VegetarianDietRules.SupportedIngredientGroups.Except(VegetarianDietRules.GetAllowedGroups(x.Name))).ToArray()))
            .ToList();
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergensAsync(CancellationToken cancellationToken = default) =>
        await allergens.Query().OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.AllergenId, x.Name, null)).ToListAsync(cancellationToken);
    public async Task<CatalogItemResponse> CreateAllergenAsync(AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Allergen { Name = request.Name.Trim() };
        await EnsureUniqueAsync(allergens.Query(), entity.Name, x => x.Name, cancellationToken);
        await allergens.AddAsync(entity, cancellationToken); await allergens.SaveChangesAsync(cancellationToken);
        return new(entity.AllergenId, entity.Name);
    }

    public async Task UpdateAllergenAsync(int id, AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(allergens, id, cancellationToken); var name = request.Name.Trim();
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) await EnsureUniqueAsync(allergens.Query().Where(x => x.AllergenId != id), name, x => x.Name, cancellationToken);
        entity.Name = name; await allergens.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAllergenAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireAsync(allergens, id, cancellationToken);
        if (await userAllergens.Query().AnyAsync(x => x.AllergenId == id, cancellationToken) || await ingredients.Query().AnyAsync(x => x.AllergenId == id, cancellationToken)) throw new ServiceException("Allergen is in use and cannot be deleted.", 409);
        await DeleteAsync(allergens, id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurantsAsync(string? food, CancellationToken cancellationToken = default)
    {
        var query = restaurants.Query().Include(x => x.Foods).Include(x => x.DietTypes).Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).AsQueryable();
        if (!string.IsNullOrWhiteSpace(food)) { var term = food.Trim().ToLower(); query = query.Where(x => x.Name.ToLower().Contains(term) || x.Foods.Any(f => f.FoodName.ToLower().Contains(term))); }
        return (await query.OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(ToRestaurant).ToList();
    }
    public async Task<RestaurantResponse?> GetRestaurantAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.Query().Include(x => x.Foods).Include(x => x.DietTypes).Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).SingleOrDefaultAsync(x => x.RestaurantId == id, cancellationToken);
        return entity is null ? null : ToRestaurant(entity);
    }
    public async Task<IReadOnlyCollection<RestaurantResponse>> GetNearbyRestaurantsAsync(decimal latitude, decimal longitude, decimal radiusKm, int? dietTypeId = null, string? ingredient = null, CancellationToken cancellationToken = default)
    {
        if (radiusKm <= 0) throw new ServiceException("Radius must be greater than zero.");
        if (dietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == dietTypeId.Value, cancellationToken))
            throw new ServiceException("Diet type was not found.", 404);
        var query = restaurants.Query().Include(x => x.Foods).Include(x => x.DietTypes)
            .Include(x => x.Ingredients).ThenInclude(x => x.Ingredient).ThenInclude(x => x.Category)
            .Where(x => x.Latitude.HasValue && x.Longitude.HasValue);
        if (dietTypeId.HasValue)
            query = query.Where(x => x.DietTypes.Any(item => item.DietTypeId == dietTypeId.Value));
        if (!string.IsNullOrWhiteSpace(ingredient))
        {
            var term = ingredient.Trim();
            query = query.Where(x => x.Ingredients.Any(item => item.Ingredient.Name.Contains(term)));
        }
        var candidates = await query.ToListAsync(cancellationToken);
        return candidates.Where(x => DistanceKm(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value) <= radiusKm).OrderBy(x => DistanceKm(latitude, longitude, x.Latitude!.Value, x.Longitude!.Value)).Select(ToRestaurant).ToList();
    }
    public async Task<RestaurantResponse> CreateRestaurantAsync(RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        var dietIds = await ValidateDietTypeIdsAsync(request.DietTypeIds, cancellationToken);
        var ingredientIds = await ValidateIngredientIdsAsync(request.IngredientIds, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = new Restaurant { Name = request.Name.Trim(), Address = request.Address.Trim(), Latitude = request.Latitude, Longitude = request.Longitude };
        await restaurants.AddAsync(entity, cancellationToken); await restaurants.SaveChangesAsync(cancellationToken);
        await ReplaceDietTypesAsync(entity.RestaurantId, dietIds, cancellationToken);
        await ReplaceFoodsAsync(entity.RestaurantId, request.Foods, cancellationToken);
        await ReplaceIngredientsAsync(entity.RestaurantId, ingredientIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetRestaurantAsync(entity.RestaurantId, cancellationToken))!;
    }
    public async Task UpdateRestaurantAsync(int id, RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        var dietIds = await ValidateDietTypeIdsAsync(request.DietTypeIds, cancellationToken);
        var ingredientIds = await ValidateIngredientIdsAsync(request.IngredientIds, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        entity.Name = request.Name.Trim(); entity.Address = request.Address.Trim(); entity.Latitude = request.Latitude; entity.Longitude = request.Longitude;
        await restaurants.SaveChangesAsync(cancellationToken); await ReplaceDietTypesAsync(id, dietIds, cancellationToken); await ReplaceFoodsAsync(id, request.Foods, cancellationToken);
        await ReplaceIngredientsAsync(id, ingredientIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task DeleteRestaurantAsync(int id, CancellationToken cancellationToken = default)
    {
        var restaurant = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        foreach (var diet in await restaurantDietTypes.Query().Where(x => x.RestaurantId == id).ToListAsync(cancellationToken))
            restaurantDietTypes.Remove(diet);
        await restaurantDietTypes.SaveChangesAsync(cancellationToken);
        foreach (var ingredient in await restaurantIngredients.Query().Where(x => x.RestaurantId == id).ToListAsync(cancellationToken))
            restaurantIngredients.Remove(ingredient);
        await restaurantIngredients.SaveChangesAsync(cancellationToken);
        foreach (var food in await restaurantFoods.Query().Where(x => x.RestaurantId == id).ToListAsync(cancellationToken))
            restaurantFoods.Remove(food);
        await restaurantFoods.SaveChangesAsync(cancellationToken);
        restaurants.Remove(restaurant);
        await restaurants.SaveChangesAsync(cancellationToken);
    }

    private async Task ReplaceFoodsAsync(int restaurantId, List<string>? foods, CancellationToken cancellationToken)
    {
        var existing = await restaurantFoods.Query().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);
        foreach (var food in existing) restaurantFoods.Remove(food);
        foreach (var name in (foods ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            await restaurantFoods.AddAsync(new RestaurantFood { RestaurantId = restaurantId, FoodName = name }, cancellationToken);
        await restaurantFoods.SaveChangesAsync(cancellationToken);
    }
    private async Task<int[]> ValidateIngredientIdsAsync(List<int>? ids, CancellationToken ct)
    {
        var unique = (ids ?? []).Distinct().ToArray();
        if (await ingredients.Query().CountAsync(x => unique.Contains(x.IngredientId), ct) != unique.Length)
            throw new ServiceException("One or more restaurant ingredients were not found.", 400);
        return unique;
    }
    private async Task ReplaceIngredientsAsync(int restaurantId, int[] ids, CancellationToken ct)
    {
        var existing = await restaurantIngredients.Query().Where(x => x.RestaurantId == restaurantId).ToListAsync(ct);
        foreach (var item in existing.Where(x => !ids.Contains(x.IngredientId))) restaurantIngredients.Remove(item);
        foreach (var id in ids.Except(existing.Select(x => x.IngredientId)))
            await restaurantIngredients.AddAsync(new RestaurantIngredient { RestaurantId = restaurantId, IngredientId = id }, ct);
        await restaurantIngredients.SaveChangesAsync(ct);
    }
    private async Task<int[]> ValidateDietTypeIdsAsync(List<int>? ids, CancellationToken ct)
    {
        var unique = (ids ?? []).Distinct().ToArray();
        if (unique.Length == 0) throw new ServiceException("Select at least one restaurant diet type.", 400);
        if (await dietTypes.Query().CountAsync(x => unique.Contains(x.DietTypeId), ct) != unique.Length)
            throw new ServiceException("Diet type was not found.", 404);
        return unique;
    }
    private async Task ReplaceDietTypesAsync(int restaurantId, int[] ids, CancellationToken ct)
    {
        var existing = await restaurantDietTypes.Query().Where(x => x.RestaurantId == restaurantId).ToListAsync(ct);
        foreach (var item in existing.Where(x => !ids.Contains(x.DietTypeId))) restaurantDietTypes.Remove(item);
        foreach (var id in ids.Except(existing.Select(x => x.DietTypeId)))
            await restaurantDietTypes.AddAsync(new RestaurantDietType { RestaurantId = restaurantId, DietTypeId = id }, ct);
        await restaurantDietTypes.SaveChangesAsync(ct);
    }
    private static RestaurantResponse ToRestaurant(Restaurant x) => new(x.RestaurantId, x.Name, x.Address, x.Latitude, x.Longitude, x.DietTypes.Select(d => d.DietTypeId).OrderBy(id => id).ToList(), x.Foods.Select(f => f.FoodName).ToList(), x.Ingredients.Select(i => i.IngredientId).ToList(), x.Ingredients.Select(i => i.Ingredient.Name).ToList());
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

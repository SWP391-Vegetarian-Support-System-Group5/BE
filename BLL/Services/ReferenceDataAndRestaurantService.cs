using BLL.Common;
using BLL.DTOs;
using DAL.Entities;
using DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BLL.Services;

public class ReferenceDataAndRestaurantService(
    IRepository<DietType> dietTypes,
    IRepository<Allergen> allergens,
    IRepository<Restaurant> restaurants,
    IRepository<RestaurantFood> restaurantFoods,
    IRepository<UserAllergen> userAllergens,
    IRepository<Ingredient> ingredients) : IReferenceDataAndRestaurantService
{
    public async Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypesAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        (await dietTypes.Query().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name).ToListAsync(cancellationToken))
            .Select(x => new DietTypeResponse(x.DietTypeId, x.Name, VegetarianDietRules.GetAllowedGroups(x.Name), VegetarianDietRules.ProhibitedNonVegetarianGroups.Concat(VegetarianDietRules.SupportedIngredientGroups.Except(VegetarianDietRules.GetAllowedGroups(x.Name))).ToArray(), x.IsActive))
            .ToList();
    public async Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergensAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        await allergens.Query().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name).Select(x => new CatalogItemResponse(x.AllergenId, x.Name, null, x.IsActive)).ToListAsync(cancellationToken);
    public async Task<CatalogItemResponse> CreateAllergenAsync(AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Allergen { Name = request.Name.Trim(), IsActive = request.IsActive };
        await EnsureUniqueAsync(allergens.Query(), entity.Name, x => x.Name, cancellationToken);
        await allergens.AddAsync(entity, cancellationToken); await allergens.SaveChangesAsync(cancellationToken);
        return new(entity.AllergenId, entity.Name);
    }

    public async Task UpdateAllergenAsync(int id, AllergenRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await RequireAsync(allergens, id, cancellationToken); var name = request.Name.Trim();
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase)) await EnsureUniqueAsync(allergens.Query().Where(x => x.AllergenId != id), name, x => x.Name, cancellationToken);
        entity.Name = name; entity.IsActive = request.IsActive; await allergens.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAllergenAsync(int id, CancellationToken cancellationToken = default)
    {
        await RequireAsync(allergens, id, cancellationToken);
        if (await userAllergens.Query().AnyAsync(x => x.AllergenId == id, cancellationToken) || await ingredients.Query().AnyAsync(x => x.AllergenId == id, cancellationToken)) throw new ServiceException("Allergen is in use and cannot be deleted.", 409);
        await DeleteAsync(allergens, id, cancellationToken);
    }

    public async Task UpdateAllergenStatusAsync(int id, bool isActive, CancellationToken ct = default) { var entity = await RequireAsync(allergens, id, ct); entity.IsActive = isActive; await allergens.SaveChangesAsync(ct); }
    public async Task UpdateDietTypeStatusAsync(int id, bool isActive, CancellationToken ct = default) { var entity = await RequireAsync(dietTypes, id, ct); entity.IsActive = isActive; await dietTypes.SaveChangesAsync(ct); }

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
        var entity = new Restaurant { Name = request.Name.Trim(), Address = request.Address.Trim(), Latitude = request.Latitude, Longitude = request.Longitude, DietTypeId = request.DietTypeId, Description = request.Description?.Trim(), PhoneNumber = request.PhoneNumber?.Trim(), OpeningHours = request.OpeningHours?.Trim(), ImageUrl = request.ImageUrl?.Trim(), IsActive = request.IsActive };
        await restaurants.AddAsync(entity, cancellationToken); await restaurants.SaveChangesAsync(cancellationToken);
        await ReplaceFoodsAsync(entity.RestaurantId, request.Foods, cancellationToken); return (await GetRestaurantAsync(entity.RestaurantId, true, cancellationToken))!;
    }
    public async Task UpdateRestaurantAsync(int id, RestaurantRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, cancellationToken);
        entity.Name = request.Name.Trim(); entity.Address = request.Address.Trim(); entity.Latitude = request.Latitude; entity.Longitude = request.Longitude; entity.DietTypeId = request.DietTypeId; entity.Description = request.Description?.Trim(); entity.PhoneNumber = request.PhoneNumber?.Trim(); entity.OpeningHours = request.OpeningHours?.Trim(); entity.ImageUrl = request.ImageUrl?.Trim(); entity.IsActive = request.IsActive;
        await restaurants.SaveChangesAsync(cancellationToken); await ReplaceFoodsAsync(id, request.Foods, cancellationToken);
    }

    public async Task UpdateRestaurantStatusAsync(int id, bool isActive, CancellationToken ct = default) { var entity = await RequireAsync(restaurants, id, ct); entity.IsActive = isActive; await restaurants.SaveChangesAsync(ct); }

    public async Task<IReadOnlyCollection<RestaurantFoodResponse>> GetRestaurantFoodsAsync(int restaurantId, bool includeUnavailable = false, CancellationToken ct = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, ct)) throw new ServiceException("Restaurant was not found.", 404);
        return await restaurantFoods.Query().Where(x => x.RestaurantId == restaurantId && (includeUnavailable || x.IsAvailable)).OrderBy(x => x.FoodName).Select(x => new RestaurantFoodResponse(x.FoodName, x.Description, x.ImageUrl, x.Price, x.DietTypeId, x.IsAvailable)).ToListAsync(ct);
    }

    public async Task<RestaurantFoodResponse> CreateRestaurantFoodAsync(int restaurantId, RestaurantFoodRequest request, CancellationToken ct = default)
    {
        if (!await restaurants.Query().AnyAsync(x => x.RestaurantId == restaurantId, ct)) throw new ServiceException("Restaurant was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, ct);
        var name = request.FoodName.Trim();
        if (await restaurantFoods.Query().AnyAsync(x => x.RestaurantId == restaurantId && x.FoodName == name, ct)) throw new ServiceException("This food already exists in the restaurant.", 409);
        var entity = BuildFood(restaurantId, request); await restaurantFoods.AddAsync(entity, ct); await restaurantFoods.SaveChangesAsync(ct); return ToFood(entity);
    }

    public async Task<RestaurantFoodResponse> UpdateRestaurantFoodAsync(int restaurantId, string foodName, RestaurantFoodRequest request, CancellationToken ct = default)
    {
        var entity = await restaurantFoods.Query().SingleOrDefaultAsync(x => x.RestaurantId == restaurantId && x.FoodName == foodName, ct) ?? throw new ServiceException("Restaurant food was not found.", 404);
        await ValidateDietTypeAsync(request.DietTypeId, ct);
        var replacement = BuildFood(restaurantId, request);
        if (!string.Equals(entity.FoodName, replacement.FoodName, StringComparison.OrdinalIgnoreCase)) { if (await restaurantFoods.Query().AnyAsync(x => x.RestaurantId == restaurantId && x.FoodName == replacement.FoodName, ct)) throw new ServiceException("This food already exists in the restaurant.", 409); restaurantFoods.Remove(entity); await restaurantFoods.AddAsync(replacement, ct); }
        else { entity.Description = replacement.Description; entity.ImageUrl = replacement.ImageUrl; entity.Price = replacement.Price; entity.DietTypeId = replacement.DietTypeId; entity.IsAvailable = replacement.IsAvailable; replacement = entity; }
        await restaurantFoods.SaveChangesAsync(ct); return ToFood(replacement);
    }

    public async Task DeleteRestaurantFoodAsync(int restaurantId, string foodName, CancellationToken ct = default) { var entity = await restaurantFoods.Query().SingleOrDefaultAsync(x => x.RestaurantId == restaurantId && x.FoodName == foodName, ct) ?? throw new ServiceException("Restaurant food was not found.", 404); restaurantFoods.Remove(entity); await restaurantFoods.SaveChangesAsync(ct); }
    public async Task DeleteRestaurantAsync(int id, CancellationToken cancellationToken = default)
    {
        var restaurant = await restaurants.FindAsync(id) ?? throw new ServiceException("Restaurant was not found.", 404);
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
    private async Task ValidateDietTypeAsync(int? dietTypeId, CancellationToken cancellationToken)
    {
        if (dietTypeId.HasValue && !await dietTypes.Query().AnyAsync(x => x.DietTypeId == dietTypeId.Value && x.IsActive, cancellationToken)) throw new ServiceException("Diet type was not found or is inactive.", 404);
    }
    private static RestaurantFood BuildFood(int restaurantId, RestaurantFoodRequest request) => new() { RestaurantId = restaurantId, FoodName = request.FoodName.Trim(), Description = request.Description?.Trim(), ImageUrl = request.ImageUrl?.Trim(), Price = request.Price, DietTypeId = request.DietTypeId, IsAvailable = request.IsAvailable };
    private static RestaurantFoodResponse ToFood(RestaurantFood x) => new(x.FoodName, x.Description, x.ImageUrl, x.Price, x.DietTypeId, x.IsAvailable);
    private static RestaurantResponse ToRestaurant(Restaurant x) => new(x.RestaurantId, x.Name, x.Address, x.Latitude, x.Longitude, x.DietTypeId, x.Foods.Where(f => f.IsAvailable).Select(f => f.FoodName).ToList(), x.Description, x.PhoneNumber, x.OpeningHours, x.ImageUrl, x.IsActive, x.Foods.Select(ToFood).ToList());
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

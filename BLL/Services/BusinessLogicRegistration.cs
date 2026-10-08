using DAL.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace BLL.Services;

public static class BusinessLogicRegistration
{
    public static IServiceCollection AddVegetarianBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IUserAllergenService, UserAllergenService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IReferenceDataAndRestaurantService, ReferenceDataAndRestaurantService>();
        services.AddSingleton<ILocationService, VietnamLocationService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IRecipeService, RecipeService>();
        services.AddScoped<IMealPlanService, MealPlanService>();
        services.AddScoped<IChatService, NutritionChatbotService>();
        services.AddScoped<IVideoRecipeDraftService, VideoRecipeDraftService>();
        services.AddScoped<IModerationService, ModerationService>();
        services.AddScoped<ISeedService, SeedService>();
        services.AddScoped<IHealthService, HealthService>();
        services.AddSingleton<IIngredientRecognitionService, MockIngredientRecognitionService>();
        services.AddSingleton<IModerationAiService, MockModerationAiService>();
        return services;
    }
}

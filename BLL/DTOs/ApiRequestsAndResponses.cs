using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    [Required, MaxLength(150)] string FullName,
    [Range(1, 150)] int Age,
    [Required] string Sex,
    [Range(1, 300)] decimal HeightCm,
    [Range(1, 1000)] decimal WeightKg,
    [Required] string ActivityLevel,
    [Required] string HealthGoal,
    int? DietTypeId);

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record UserResponse(int UserId, string Email, string Role, string FullName, int? Age, string? Sex, decimal? HeightCm, decimal? WeightKg, string? ActivityLevel, string? HealthGoal, int? DietTypeId, bool IsActive);
public record AuthResponse(string Token, UserResponse User);
public record UpdateUserStatusRequest(bool IsActive);
public record UpdateUserAllergensRequest([Required] List<int> AllergenIds);
public record UserAllergensResponse(int UserId, IReadOnlyCollection<CatalogItemResponse> Allergens);
public record CategoryRequest([Required, MaxLength(150)] string Name, [Required] string Type);
public record DietTypeRequest([Required, MaxLength(100)] string Name);
public record AllergenRequest([Required, MaxLength(150)] string Name);
public record TagRequest([Required, MaxLength(100)] string Name);
public record CatalogItemResponse(int Id, string Name, string? Type = null);
public record RestaurantRequest([Required, MaxLength(200)] string Name, [Required, MaxLength(300)] string Address, decimal? Latitude, decimal? Longitude, int? DietTypeId, List<string>? Foods);
public record RestaurantResponse(int RestaurantId, string Name, string Address, decimal? Latitude, decimal? Longitude, int? DietTypeId, IReadOnlyCollection<string> Foods);
public record RestaurantReviewRequest([Required, MaxLength(250)] string Title, [Required] string Content, [Range(1, 5)] int Rating);
public record RestaurantReviewResponse(int PostId, int RestaurantId, int UserId, string Title, string Content, int Rating);
public record CreatePostRequest([Required, MaxLength(250)] string Title, [Required] string Content, [Required] string PostType, int? CategoryId, string? VideoUrl, List<int>? TagIds);
public record UpdatePostRequest([Required, MaxLength(250)] string Title, [Required] string Content, [Required] string PostType, int? CategoryId, string? VideoUrl, string? Status, List<int>? TagIds);
public record PostResponse(int PostId, int UserId, string AuthorName, int? CategoryId, string? CategoryName, string Title, string Content, string PostType, string? VideoUrl, string Status, IReadOnlyCollection<string> Tags, double AverageRating);
public record CommentRequest([Required] string Content);
public record CommentResponse(int CommentId, int PostId, int UserId, string AuthorName, int? ParentCommentId, string Content, string Status, IReadOnlyCollection<CommentResponse> Replies);
public record RatingRequest([Range(1, 5)] int Rating);
public record IngredientRequest([Required, MaxLength(200)] string IngredientName, string? Amount, int? AllergenId);
public record RecipeStepRequest([Range(1, int.MaxValue)] int StepNumber, [Required] string Instruction);
public record CreateRecipeRequest([Required, MaxLength(250)] string Title, [Required] string Content, int? CategoryId, string? VideoUrl, [Range(1, int.MaxValue)] int? PrepMinutes, [Range(1, int.MaxValue)] int? CookMinutes, [Range(0.01, 1000)] decimal? Servings, [Range(0, 100000)] decimal? CaloriesPerServing, decimal? ProteinPerServing, decimal? CarbsPerServing, decimal? FatPerServing, List<IngredientRequest>? Ingredients, List<RecipeStepRequest>? Steps, List<int>? CompatibleDietTypeIds, List<int>? TagIds);
public record RecipeResponse(int PostId, string Title, string Content, string Status, decimal? CaloriesPerServing, decimal? Servings, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, IReadOnlyCollection<int> CompatibleDietTypeIds);
public record GenerateMealPlanRequest(DateTime? StartDate, string? AvailableIngredients);
public record MealPlanMealResponse(int DayNumber, string MealType, int RecipeId, string RecipeTitle, decimal? PlannedCalories);
public record MealPlanResponse(int MealPlanId, DateTime StartDate, string? HealthGoal, decimal? BMI, decimal? BMR, decimal? TDEE, decimal? TargetCaloriesPerDay, IReadOnlyCollection<MealPlanMealResponse> Meals);
public record CreateChatSessionRequest();
public record ChatMessageRequest([Required] string Content);
public record ChatMessageResponse(int ChatMessageId, string Sender, string Content);
public record ChatSessionResponse(int ChatSessionId, int? UserId, IReadOnlyCollection<ChatMessageResponse> Messages);
public record IngredientRecognitionResponse(string IngredientName, decimal Confidence, string FreshnessAssessment, IReadOnlyCollection<string> SuggestedRecipes);
public record VideoSummaryRequest([Required, Url] string VideoUrl);
public record VideoSummaryResponse(string Title, IReadOnlyCollection<string> Ingredients, IReadOnlyCollection<string> Steps, int EstimatedPreparationTime);
public record ReportRequest([Required, MaxLength(500)] string Reason);
public record ModerationFlagResponse(int FlagId, int? PostId, int? CommentId, string Source, string? Reason, string Status, int? ReportedBy, int? ReviewedBy);

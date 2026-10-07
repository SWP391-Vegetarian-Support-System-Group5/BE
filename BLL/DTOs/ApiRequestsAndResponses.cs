using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    [Required, MaxLength(150)] string FullName,
    [Required] string Sex,
    [Range(1, 300)] decimal HeightCm,
    [Range(1, 1000)] decimal WeightKg,
    int? DietTypeId);

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record UserResponse(int UserId, string Email, string Role, string FullName, string? Sex, decimal? HeightCm, decimal? WeightKg, int? DietTypeId, bool IsActive);
public record AuthResponse(string Token, UserResponse User);
public record UpdateUserStatusRequest(bool IsActive);
public record UpdateUserAllergensRequest([Required] List<int> AllergenIds);
public record UserAllergensResponse(int UserId, IReadOnlyCollection<CatalogItemResponse> Allergens);
public record CategoryRequest([Required, MaxLength(150)] string Name, [Required] string Type);
public record DietTypeResponse(int Id, string Name, IReadOnlyCollection<string> AllowedIngredientGroups, IReadOnlyCollection<string> ProhibitedIngredientGroups);
public record AllergenRequest([Required, MaxLength(150)] string Name);
public record TagRequest([Required, MaxLength(100)] string Name);
public record CatalogItemResponse(int Id, string Name, string? Type = null);
public record ProvinceResponse(string Code, string Name, string Type, decimal Latitude, decimal Longitude);
public record AreaResponse(string Code, string Name, string Type, decimal Latitude, decimal Longitude);
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
public record IngredientRequest([Required, MaxLength(200)] string IngredientName, string? Amount, [Required, MaxLength(30)] string DietaryGroup, int? AllergenId);
public record RecipeStepRequest([Range(1, int.MaxValue)] int StepNumber, [Required] string Instruction);
public record CreateRecipeRequest([Required, MaxLength(250)] string Title, [Required] string Content, int? CategoryId, string? VideoUrl, [Range(1, int.MaxValue)] int? PrepMinutes, [Range(1, int.MaxValue)] int? CookMinutes, [Range(0.01, 1000)] decimal? Servings, List<IngredientRequest>? Ingredients, List<RecipeStepRequest>? Steps, List<int>? TagIds);
public record RecipeResponse(int PostId, string Title, string Content, string Status, decimal? Servings, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, IReadOnlyCollection<int> CompatibleDietTypeIds);
public record GenerateMealPlanRequest(DateTime? StartDate, string? AvailableIngredients);
public record MealPlanMealResponse(int DayNumber, string MealType, int RecipeId, string RecipeTitle);
public record MealPlanResponse(int MealPlanId, DateTime StartDate, IReadOnlyCollection<MealPlanMealResponse> Meals);
public record CreateChatSessionRequest();
public record ChatMessageRequest([Required, MaxLength(1000)] string Content);
public record ChatMessageResponse(int ChatMessageId, string Sender, string Content, DateTime CreatedAt);
public record RelatedRecipeResponse(int RecipeId, string Title);
public record ChatReplyResponse(int? ChatMessageId, string? Answer, IReadOnlyCollection<RelatedRecipeResponse> RelatedRecipes, IReadOnlyCollection<string> Sources, int? RemainingGuestMessages, bool SignupRequired);
public record ChatSessionResponse(int ChatSessionId, int? UserId, DateTime CreatedAt, IReadOnlyCollection<ChatMessageResponse> Messages, string? GuestAccessToken = null);
public record IngredientRecognitionResponse(string IngredientName, decimal Confidence, string FreshnessAssessment, IReadOnlyCollection<string> SuggestedRecipes);
public record VideoRecipeDraftResponse(int VideoRecipeDraftId, string Status, string VideoUrl, string? Title, string? Description, string? Transcript, int? EstimatedPrepMinutes, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, string? ErrorMessage, DateTime CreatedAt, DateTime UpdatedAt);
public record UpdateVideoRecipeDraftRequest([Required, MaxLength(250)] string Title, [Required] string Description, string? Transcript, [Range(1, int.MaxValue)] int? EstimatedPrepMinutes, [Required] List<IngredientRequest> Ingredients, [Required] List<RecipeStepRequest> Steps);
public record PublishVideoRecipeDraftRequest(int? CategoryId, List<int>? TagIds);
public record ReportRequest([Required, MaxLength(500)] string Reason);
public record ModerationFlagResponse(int FlagId, int? PostId, int? CommentId, string Source, string? Reason, string Status, int? ReportedBy, int? ReviewedBy);

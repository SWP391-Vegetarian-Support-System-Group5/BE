using System.ComponentModel.DataAnnotations;

namespace BLL.DTOs;

public record RegisterRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    [Required, MaxLength(150)] string Name);

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record RequestEmailOtpRequest([Required, EmailAddress] string Email);
public record VerifyEmailOtpRequest([Required, EmailAddress] string Email, [Required, RegularExpression("^[0-9]{6}$")] string OtpCode);
public record ResetPasswordWithOtpRequest([Required, EmailAddress] string Email, [Required, RegularExpression("^[0-9]{6}$")] string OtpCode, [Required, MinLength(6)] string NewPassword);
public record EmailOtpSentResponse(string Email, string Message, int ExpiresInSeconds);
public record ApiMessageResponse(string Message);
public record UserResponse(int UserId, string Email, string Role, string FullName, string? Sex, DateTime? BirthDate, decimal? HeightCm, decimal? WeightKg, string? ActivityLevel, int? DietTypeId, decimal? Latitude, decimal? Longitude, bool IsActive, DateTime? CreatedAt = null, bool? IsEmailVerified = null);
public record AdminUserActivityResponse(int UserId, int Posts, int Videos, int Comments, int Saved, int Recipes, int Ratings, DateTime? LastActivityAt, IReadOnlyList<int> DailyContributions);
public record UpdateUserProfileRequest(
    [Required, MaxLength(150)] string FullName,
    string? Sex,
    [DataType(DataType.Date)] DateTime? BirthDate,
    [Range(1, 300)] decimal? HeightCm,
    [Range(1, 1000)] decimal? WeightKg,
    string? ActivityLevel,
    int? DietTypeId,
    [Range(-90, 90)] decimal? Latitude,
    [Range(-180, 180)] decimal? Longitude);
public record UpdateUserLocationRequest(
    [property: System.Text.Json.Serialization.JsonRequired]
    [Range(-90, 90)] decimal Latitude,
    [property: System.Text.Json.Serialization.JsonRequired]
    [Range(-180, 180)] decimal Longitude);
public record AuthResponse(string Token, UserResponse User);
public record UpdateUserStatusRequest(bool IsActive);
public record UpdateUserAllergensRequest([Required] List<int> AllergenIds);
public record AddCustomAllergenRequest([Required, MinLength(2), MaxLength(80)] string Name);
public record UserAllergensResponse(int UserId, IReadOnlyCollection<CatalogItemResponse> Allergens);
public record DietTypeResponse(int Id, string Name, IReadOnlyCollection<string> AllowedIngredientGroups, IReadOnlyCollection<string> ProhibitedIngredientGroups);
public record AllergenRequest([Required, MaxLength(150)] string Name);
public record CatalogItemResponse(int Id, string Name, string? Type = null);
public record ProvinceResponse(string Code, string Name, string Type, decimal? Latitude, decimal? Longitude);
public record AreaResponse(string Code, string Name, string Type, decimal? Latitude, decimal? Longitude);
public record RestaurantRequest([Required, MaxLength(200)] string Name, [Required, MaxLength(300)] string Address, decimal? Latitude, decimal? Longitude, List<int>? DietTypeIds, List<string>? Foods, List<int>? IngredientIds = null, [MaxLength(1000)] string? ImageUrl = null);
public record RestaurantResponse(int RestaurantId, string Name, string Address, decimal? Latitude, decimal? Longitude, IReadOnlyCollection<int> DietTypeIds, IReadOnlyCollection<string> Foods, IReadOnlyCollection<int> IngredientIds, IReadOnlyCollection<string> IngredientNames, string? ImageUrl = null);
public record CreatePostRequest([Required, MaxLength(250)] string Title, [Required] string Content, [Required] string PostType, string? VideoUrl);
public record UpdatePostRequest([Required, MaxLength(250)] string Title, [Required] string Content, [Required] string PostType, string? VideoUrl, string? Status);
public record PostDietTypeResponse(int Id, string Name);
public record PostResponse(int PostId, int UserId, string AuthorName, string Title, string Content, string PostType, string? VideoUrl, string? ThumbnailUrl, string Status, double AverageRating, int RatingCount, int CommentCount, int? CurrentUserRating, DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyCollection<PostDietTypeResponse> CompatibleDietTypes, string? ModerationReason = null);
public record CommentRequest([Required] string Content);
public record UpdateCommentVisibilityRequest([property: System.Text.Json.Serialization.JsonRequired] bool Hidden);
public record CommentResponse(int CommentId, int PostId, int UserId, string AuthorName, int? ParentCommentId, string Content, string Status, IReadOnlyCollection<CommentResponse> Replies, DateTime CreatedAt, DateTime UpdatedAt);
public record RatingRequest([Range(1, 5)] int Rating);
public record IngredientRequest([Required, MaxLength(200)] string IngredientName, string? Amount, [Required, MaxLength(30)] string DietaryGroup, int? AllergenId, int? IngredientCategoryId = null);
public record IngredientCategoryResponse(int Id, string Code, string Name, string DietaryGroup);
public record IngredientCategoryRequest([Required, MaxLength(30)] string Code, [Required, MaxLength(100)] string Name, [Required, MaxLength(30)] string DietaryGroup);
public record IngredientResponse(int Id, string Name, int IngredientCategoryId, string DietaryGroup, int? AllergenId);
public record UpdateIngredientRequest([Required, MaxLength(200)] string Name, [Range(1, int.MaxValue)] int IngredientCategoryId, int? AllergenId);
public record RecipeStepRequest([Range(1, int.MaxValue)] int StepNumber, [Required] string Instruction);
public record CreateRecipeRequest([Required, MaxLength(250)] string Title, [Required] string Content, string? VideoUrl, [Range(1, int.MaxValue)] int? PrepMinutes, [Range(1, int.MaxValue)] int? CookMinutes, [Range(0.01, 1000)] decimal? Servings, List<IngredientRequest>? Ingredients, List<RecipeStepRequest>? Steps, [Required, MaxLength(250)] string RecipeName, List<int>? CategoryIds = null, List<int>? BmiTypeIds = null, [MaxLength(1000)] string? ImageUrl = null);
public record RecipeResponse(int PostId, string Title, string Content, string Status, string? VideoUrl, string? ThumbnailUrl, int? PrepMinutes, int? CookMinutes, decimal? Servings, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, IReadOnlyCollection<int> CompatibleDietTypeIds, string RecipeName, IReadOnlyCollection<RecipeCategoryResponse> Categories, IReadOnlyCollection<BmiTypeResponse>? BmiTypes = null, string? ModerationReason = null, string? ImageUrl = null);
public record BmiTypeResponse(int Id, string Code, string Name, string? Description);
public record UpdateRecipeBmiTypesRequest([Required, MaxLength(3)] List<int> BmiTypeIds);
public record AdminRecipeBmiResponse(int PostId, string RecipeName, string Title, string Status, IReadOnlyCollection<BmiTypeResponse> BmiTypes, IReadOnlyCollection<int> CategoryIds);
public record UpdateRecipeCategoryMappingsRequest([Required] List<int> CategoryIds);
public record RecipeCategoryResponse(int Id, string Name, string Slug, string Type, string? Description, string? ImageUrl, bool IsActive, DateTime CreatedAt);
public record RecipeCategoryRequest([Required, MaxLength(150)] string Name, [Required, MaxLength(150)] string Slug, [Required] string Type, [MaxLength(1000)] string? Description, [MaxLength(1000)] string? ImageUrl, bool IsActive = true);
public record GenerateMealPlanRequest(DateTime? StartDate, string? AvailableIngredients);
public record MealPlanMealResponse(int MealPlanMealId, int DayNumber, string MealType, int RecipeId, string RecipeName);
public record MealPlanResponse(int MealPlanId, DateTime StartDate, decimal? BMI, string? BmiCategory, IReadOnlyCollection<MealPlanMealResponse> Meals);
public record AiMealResponse(int Day, string MealType, int RecipeId);
public record CreateChatSessionRequest();
public record ChatMessageRequest([Required, MaxLength(1000)] string Content);
public record ChatMessageResponse(int ChatMessageId, string Sender, string Content, DateTime CreatedAt);
public record RelatedRecipeResponse(int RecipeId, string Title);
public record ChatReplyResponse(int? ChatMessageId, string? Answer, IReadOnlyCollection<RelatedRecipeResponse> RelatedRecipes, IReadOnlyCollection<string> Sources, int? RemainingGuestMessages, bool SignupRequired);
public record ChatSessionResponse(int ChatSessionId, int? UserId, DateTime CreatedAt, IReadOnlyCollection<ChatMessageResponse> Messages, string? GuestAccessToken = null);
public record IngredientRecognitionResponse(string IngredientName, decimal Confidence, string FreshnessAssessment, IReadOnlyCollection<string> SuggestedRecipes);
public record VideoRecipeDraftResponse(int VideoRecipeDraftId, string Status, string VideoUrl, string? Title, string? Description, string? Transcript, int? EstimatedPrepMinutes, IReadOnlyCollection<IngredientRequest> Ingredients, IReadOnlyCollection<RecipeStepRequest> Steps, string? ErrorMessage, DateTime CreatedAt, DateTime UpdatedAt, string? RecipeName = null, int? RecipePostId = null);
public record UpdateVideoRecipeDraftRequest([Required, MaxLength(250)] string Title, [Required] string Description, string? Transcript, [Range(1, int.MaxValue)] int? EstimatedPrepMinutes, [Required] List<IngredientRequest> Ingredients, [Required] List<RecipeStepRequest> Steps, [MaxLength(250)] string? RecipeName = null);
public record PublishVideoRecipeDraftRequest([Required, MaxLength(250)] string RecipeName, List<int>? CategoryIds = null, List<int>? BmiTypeIds = null);
public record ReportRequest([Required, MaxLength(500)] string Reason);
public record ModerationFlagResponse(int FlagId, int? PostId, int? CommentId, string Source, string? Reason, string Status, int? ReportedBy, int? ReviewedBy);
public record UserNotificationResponse(long Id, string Type, string Message, int? PostId, bool IsRead, DateTime CreatedAt);
public record CreateManualVideoRecipeDraftRequest([Required, MaxLength(250)] string Title,[Required] string Description,string? Transcript,
    [Range(1, int.MaxValue)] int? EstimatedPrepMinutes, [Required] List<IngredientRequest> Ingredients, [Required] List<RecipeStepRequest> Steps, [MaxLength(250)] string? RecipeName = null);

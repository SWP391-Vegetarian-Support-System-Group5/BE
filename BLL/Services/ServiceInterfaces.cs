using BLL.DTOs;

namespace BLL.Services;

public interface IAuthService
{
    Task<EmailOtpSentResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task VerifyRegistrationOtpAsync(VerifyEmailOtpRequest request, CancellationToken cancellationToken = default);
    Task<EmailOtpSentResponse> ResendRegistrationOtpAsync(RequestEmailOtpRequest request, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(RequestEmailOtpRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordWithOtpRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse> ValidateCredentialsAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserResponse> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
    Task SendOtpAsync(string recipientEmail, string purpose, string otpCode, CancellationToken cancellationToken = default);
}

public sealed class SendGridSettings
{
    public string? ApiKey { get; init; }
    public string? FromEmail { get; init; }
    public string FromName { get; init; } = "Vegetarian Support System";
}

public interface IUserAdministrationService
{
    Task<IReadOnlyCollection<AdminUserResponse>> GetUsersAsync(string? keyword = null, string? role = null, bool? isActive = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<AdminUserDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<AdminUserResponse> GetUserByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AdminUserResponse> CreateAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default);
    Task<AdminUserResponse> UpdateAsync(int id, int actorId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(int id, int actorId, bool isActive, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, int actorId, CancellationToken cancellationToken = default);
}

public interface IAdminAuditService
{
    Task RecordAsync(int adminUserId, string method, string path, int statusCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AdminAuditLogResponse>> GetAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
}

public interface IUserAllergenService
{
    Task<UserAllergensResponse> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task<UserAllergensResponse> UpdateAsync(int userId, UpdateUserAllergensRequest request, CancellationToken cancellationToken = default);
}

public interface IUserProfileService
{
    Task<UserResponse> UpdateAsync(int userId, UpdateUserProfileRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse> UpdateLocationAsync(int userId, UpdateUserLocationRequest request, CancellationToken cancellationToken = default);
}

public interface ILocationService
{
    IReadOnlyCollection<ProvinceResponse> GetProvinces();
    IReadOnlyCollection<AreaResponse>? GetAreas(string provinceCode);
}

public interface IReferenceDataAndRestaurantService
{
    Task<IReadOnlyCollection<DietTypeResponse>> GetDietTypesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CatalogItemResponse>> GetAllergensAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<CatalogItemResponse> CreateAllergenAsync(AllergenRequest request, CancellationToken cancellationToken = default);
    Task UpdateAllergenAsync(int id, AllergenRequest request, CancellationToken cancellationToken = default);
    Task DeleteAllergenAsync(int id, CancellationToken cancellationToken = default);
    Task UpdateAllergenStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default);
    Task UpdateDietTypeStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RestaurantResponse>> GetRestaurantsAsync(string? food, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<RestaurantResponse?> GetRestaurantAsync(int id, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RestaurantResponse>> GetNearbyRestaurantsAsync(decimal latitude, decimal longitude, decimal radiusKm, CancellationToken cancellationToken = default);
    Task<RestaurantResponse> CreateRestaurantAsync(RestaurantRequest request, CancellationToken cancellationToken = default);
    Task UpdateRestaurantAsync(int id, RestaurantRequest request, CancellationToken cancellationToken = default);
    Task DeleteRestaurantAsync(int id, CancellationToken cancellationToken = default);
    Task UpdateRestaurantStatusAsync(int id, bool isActive, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RestaurantFoodResponse>> GetRestaurantFoodsAsync(int restaurantId, bool includeUnavailable = false, CancellationToken cancellationToken = default);
    Task<RestaurantFoodResponse> CreateRestaurantFoodAsync(int restaurantId, RestaurantFoodRequest request, CancellationToken cancellationToken = default);
    Task<RestaurantFoodResponse> UpdateRestaurantFoodAsync(int restaurantId, string foodName, RestaurantFoodRequest request, CancellationToken cancellationToken = default);
    Task DeleteRestaurantFoodAsync(int restaurantId, string foodName, CancellationToken cancellationToken = default);
}

public interface IPostService
{
    Task<PostResponse> CreateAsync(int userId, CreatePostRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PostResponse>> GetAsync(string? keyword, int? categoryId, string? postType, string? categoryType, string? status, bool isAdmin, int? actorId = null, int? limit = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PostResponse>> GetMineAsync(int userId, CancellationToken cancellationToken = default);
    Task<PostResponse> GetByIdAsync(int id, int? actorId, string? role, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, int actorId, string role, UpdatePostRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default);
    Task SetStatusAsync(int id, string status, CancellationToken cancellationToken = default);
    Task<CommentResponse> AddCommentAsync(int postId, int userId, int? parentCommentId, CommentRequest request, CancellationToken cancellationToken = default);
    Task<CommentResponse> AddReplyAsync(int parentCommentId, int userId, CommentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CommentResponse>> GetCommentsAsync(int postId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CommentResponse>> GetAllCommentsAsync(CancellationToken cancellationToken = default);
    Task UpdateCommentAsync(int id, int actorId, string role, CommentRequest request, CancellationToken cancellationToken = default);
    Task DeleteCommentAsync(int id, int actorId, string role, CancellationToken cancellationToken = default);
    Task SetCommentVisibilityAsync(int id, bool hidden, CancellationToken cancellationToken = default);
    Task SetRatingAsync(int postId, int userId, int rating, bool updateOnly, CancellationToken cancellationToken = default);
    Task DeleteRatingAsync(int postId, int userId, CancellationToken cancellationToken = default);
    Task<double> GetRatingAsync(int postId, CancellationToken cancellationToken = default);
    Task AddBookmarkAsync(int postId, int userId, CancellationToken cancellationToken = default);
    Task DeleteBookmarkAsync(int postId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PostResponse>> GetBookmarksAsync(int userId, CancellationToken cancellationToken = default);
}

public interface IRecipeService
{
    Task<RecipeResponse> CreateAsync(int userId, CreateRecipeRequest request, CancellationToken cancellationToken = default);
    Task<RecipeResponse> GetAsync(int id, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, int actorId, string role, CreateRecipeRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, int actorId, string role, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RecipeResponse>> SearchAsync(string? keyword, int? categoryId, string? categoryType, CancellationToken cancellationToken = default);
}

public interface IMealPlanService
{
    Task<MealPlanResponse> GenerateAsync(int userId, GenerateMealPlanRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<MealPlanResponse>> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task<MealPlanResponse> GetByIdAsync(int id, int userId, string role, CancellationToken cancellationToken = default);
    Task<MealPlanResponse> RegenerateAsync(int id, int userId, GenerateMealPlanRequest request, CancellationToken cancellationToken = default);
    Task<MealPlanResponse> ReplaceMealAsync(int mealPlanId, int mealPlanMealId, int userId, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, int userId, string role, CancellationToken cancellationToken = default);
}

public interface IChatService
{
    Task<ChatSessionResponse> CreateSessionAsync(int? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ChatSessionResponse>> GetSessionsAsync(int? userId, CancellationToken cancellationToken = default);
    Task<ChatSessionResponse> GetSessionAsync(int id, int? userId, string? role, string? guestAccessToken, CancellationToken cancellationToken = default);
    Task<ChatReplyResponse> SendAsync(int id, int? userId, string? role, string? guestAccessToken, ChatMessageRequest request, CancellationToken cancellationToken = default);
}

public record ChatbotReply(string Answer, IReadOnlyCollection<string> Sources);
public record ChatbotRequest(string Message, string ApplicationContext);
public interface IAiChatService { Task<ChatbotReply> ReplyAsync(ChatbotRequest request, CancellationToken cancellationToken = default); }
public record KnowledgeBaseMatch(string Content, IReadOnlyCollection<string> Sources);
public interface IKnowledgeBaseService { KnowledgeBaseMatch FindRelevant(string question); }
public interface IIngredientRecognitionService { Task<IReadOnlyCollection<IngredientRecognitionResponse>> RecognizeAsync(Stream image, string? fileName, CancellationToken cancellationToken = default); }
public interface IVideoRecipeDraftService
{
    Task<VideoRecipeDraftResponse> CreateAsync(int userId, Stream video, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<VideoRecipeDraftResponse> GetAsync(int id, int userId, string role, CancellationToken cancellationToken = default);
    Task<VideoRecipeDraftResponse> UpdateAsync(int id, int userId, string role, UpdateVideoRecipeDraftRequest request, CancellationToken cancellationToken = default);
    Task<VideoRecipeDraftResponse> RetryAsync(int id, int userId, string role, CancellationToken cancellationToken = default);
    Task<RecipeResponse> PublishAsync(int id, int userId, string role, PublishVideoRecipeDraftRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, int userId, string role, CancellationToken cancellationToken = default);
    Task ProcessPendingAsync(CancellationToken cancellationToken = default);
    Task<VideoRecipeDraftResponse> CreateManualAsync(int userId, Stream videoStream, string fileName, string contentType, CreateManualVideoRecipeDraftRequest request, CancellationToken ct = default);
}
public interface IModerationAiService { Task<string?> FlagReasonAsync(string content, CancellationToken cancellationToken = default); }

public record CommunityEvent(string Type, int? PostId, PostResponse? Post = null, CommentResponse? Comment = null, double? AverageRating = null, int? RatingCount = null, int? CommentCount = null);
public interface ICommunityEventPublisher
{
    Task PublishAsync(CommunityEvent communityEvent, CancellationToken cancellationToken = default);
}

public interface IModerationService
{
    Task<ModerationFlagResponse> ReportPostAsync(int postId, int userId, ReportRequest request, CancellationToken cancellationToken = default);
    Task<ModerationFlagResponse> ReportCommentAsync(int commentId, int userId, ReportRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ModerationFlagResponse>> GetAsync(CancellationToken cancellationToken = default);
    Task<ModerationFlagResponse> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task ReviewAsync(int id, int adminId, bool removeContent, CancellationToken cancellationToken = default);
}

public interface ISeedService { Task SeedAsync(CancellationToken cancellationToken = default); }
public interface IHealthService { Task<bool> CanConnectAsync(CancellationToken cancellationToken = default); }

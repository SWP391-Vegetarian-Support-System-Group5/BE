namespace DAL.Entities;

public class User
{
    public int UserId { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Role { get; set; } = "USER";
    public string FullName { get; set; } = null!;
    public string? Sex { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public int? DietTypeId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsEmailVerified { get; set; } = true;
    public DietType? DietType { get; set; }
    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<UserAllergen> UserAllergens { get; set; } = new List<UserAllergen>();
}

public class EmailOtpCode
{
    public int EmailOtpCodeId { get; set; }
    public string Email { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DietType
{
    public int DietTypeId { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Restaurant> Restaurants { get; set; } = new List<Restaurant>();
    public ICollection<RecipeDietCompatibility> RecipeCompatibilities { get; set; } = new List<RecipeDietCompatibility>();
}

public class Category
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class Post
{
    public int PostId { get; set; }
    public int UserId { get; set; }
    public int? CategoryId { get; set; }
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string PostType { get; set; } = null!;
    public string? VideoUrl { get; set; }
    public string Status { get; set; } = "DRAFT";
    public User User { get; set; } = null!;
    public Category? Category { get; set; }
    public Recipe? Recipe { get; set; }
    public RestaurantReview? RestaurantReview { get; set; }
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<PostRating> Ratings { get; set; } = new List<PostRating>();
    public ICollection<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();
    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
}

public class Comment
{
    public int CommentId { get; set; }
    public int PostId { get; set; }
    public int UserId { get; set; }
    public int? ParentCommentId { get; set; }
    public string Content { get; set; } = null!;
    public string Status { get; set; } = "PUBLISHED";
    public Post Post { get; set; } = null!;
    public User User { get; set; } = null!;
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = new List<Comment>();
}

public class PostRating
{
    public int PostId { get; set; }
    public int UserId { get; set; }
    public int Rating { get; set; }
    public Post Post { get; set; } = null!;
    public User User { get; set; } = null!;
}

public class Bookmark
{
    public int UserId { get; set; }
    public int PostId { get; set; }
    public User User { get; set; } = null!;
    public Post Post { get; set; } = null!;
}

public class Recipe
{
    public int PostId { get; set; }
    public int? PrepMinutes { get; set; }
    public int? CookMinutes { get; set; }
    public decimal? Servings { get; set; }
    public string? VideoUrl { get; set; }
    public Post Post { get; set; } = null!;
    public ICollection<RecipeIngredient> Ingredients { get; set; } = new List<RecipeIngredient>();
    public ICollection<RecipeStep> Steps { get; set; } = new List<RecipeStep>();
    public ICollection<RecipeDietCompatibility> DietCompatibilities { get; set; } = new List<RecipeDietCompatibility>();
}

public class RecipeIngredient
{
    public int RecipeIngredientId { get; set; }
    public int RecipeId { get; set; }
    public string IngredientName { get; set; } = null!;
    public string? Amount { get; set; }
    public string DietaryGroup { get; set; } = "UNVERIFIED";
    public int? AllergenId { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public Allergen? Allergen { get; set; }
}

public class RecipeStep
{
    public int RecipeStepId { get; set; }
    public int RecipeId { get; set; }
    public int StepNumber { get; set; }
    public string Instruction { get; set; } = null!;
    public Recipe Recipe { get; set; } = null!;
}

public class RecipeDietCompatibility
{
    public int RecipeId { get; set; }
    public int DietTypeId { get; set; }
    public bool IsCompatible { get; set; }
    public Recipe Recipe { get; set; } = null!;
    public DietType DietType { get; set; } = null!;
}

public class Allergen
{
    public int AllergenId { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<UserAllergen> UserAllergens { get; set; } = new List<UserAllergen>();
    public ICollection<RecipeIngredient> RecipeIngredients { get; set; } = new List<RecipeIngredient>();
}

public class UserAllergen
{
    public int UserId { get; set; }
    public int AllergenId { get; set; }
    public User User { get; set; } = null!;
    public Allergen Allergen { get; set; } = null!;
}

public class Restaurant
{
    public int RestaurantId { get; set; }
    public string Name { get; set; } = null!;
    public string Address { get; set; } = null!;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? DietTypeId { get; set; }
    public DietType? DietType { get; set; }
    public ICollection<RestaurantFood> Foods { get; set; } = new List<RestaurantFood>();
    public ICollection<RestaurantReview> Reviews { get; set; } = new List<RestaurantReview>();
}

public class RestaurantReview
{
    public int PostId { get; set; }
    public int RestaurantId { get; set; }
    public int Rating { get; set; }
    public Post Post { get; set; } = null!;
    public Restaurant Restaurant { get; set; } = null!;
}

public class RestaurantFood
{
    public int RestaurantId { get; set; }
    public string FoodName { get; set; } = null!;
    public Restaurant Restaurant { get; set; } = null!;
}

public class MealPlan
{
    public int MealPlanId { get; set; }
    public int UserId { get; set; }
    public DateTime StartDate { get; set; }
    public string? AvailableIngredients { get; set; }
    public User User { get; set; } = null!;
    public ICollection<MealPlanMeal> Meals { get; set; } = new List<MealPlanMeal>();
}

public class MealPlanMeal
{
    public int MealPlanMealId { get; set; }
    public int MealPlanId { get; set; }
    public int DayNumber { get; set; }
    public string MealType { get; set; } = null!;
    public int RecipeId { get; set; }
    public MealPlan MealPlan { get; set; } = null!;
    public Recipe Recipe { get; set; } = null!;
}

public class ChatSession
{
    public int ChatSessionId { get; set; }
    public int? UserId { get; set; }
    public string? GuestAccessTokenHash { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatMessage
{
    public int ChatMessageId { get; set; }
    public int ChatSessionId { get; set; }
    public string Sender { get; set; } = null!;
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public ChatSession ChatSession { get; set; } = null!;
}

public class VideoRecipeDraft
{
    public int VideoRecipeDraftId { get; set; }
    public int UserId { get; set; }
    public string VideoUrl { get; set; } = null!;
    public string? GeminiFileName { get; set; }
    public string Status { get; set; } = "PROCESSING";
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Transcript { get; set; }
    public int? EstimatedPrepMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public User User { get; set; } = null!;
    public ICollection<VideoRecipeDraftIngredient> Ingredients { get; set; } = new List<VideoRecipeDraftIngredient>();
    public ICollection<VideoRecipeDraftStep> Steps { get; set; } = new List<VideoRecipeDraftStep>();
}

public class VideoRecipeDraftIngredient
{
    public int VideoRecipeDraftIngredientId { get; set; }
    public int VideoRecipeDraftId { get; set; }
    public string IngredientName { get; set; } = null!;
    public string? Amount { get; set; }
    public string DietaryGroup { get; set; } = "PLANT";
    public int? AllergenId { get; set; }
    public VideoRecipeDraft Draft { get; set; } = null!;
}

public class VideoRecipeDraftStep
{
    public int VideoRecipeDraftStepId { get; set; }
    public int VideoRecipeDraftId { get; set; }
    public int StepNumber { get; set; }
    public string Instruction { get; set; } = null!;
    public VideoRecipeDraft Draft { get; set; } = null!;
}

public class ModerationFlag
{
    public int FlagId { get; set; }
    public int? PostId { get; set; }
    public int? CommentId { get; set; }
    public string Source { get; set; } = null!;
    public int? ReportedBy { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = "PENDING";
    public int? ReviewedBy { get; set; }
    public Post? Post { get; set; }
    public Comment? Comment { get; set; }
    public User? Reporter { get; set; }
    public User? Reviewer { get; set; }
}

public class Tag
{
    public int TagId { get; set; }
    public string Name { get; set; } = null!;
    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
}

public class PostTag
{
    public int PostId { get; set; }
    public int TagId { get; set; }
    public Post Post { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}

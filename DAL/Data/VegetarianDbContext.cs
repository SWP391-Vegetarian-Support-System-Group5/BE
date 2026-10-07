using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Data;

public class VegetarianDbContext(DbContextOptions<VegetarianDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<EmailOtpCode> EmailOtpCodes => Set<EmailOtpCode>();
    public DbSet<DietType> DietTypes => Set<DietType>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<PostRating> PostRatings => Set<PostRating>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeDietCompatibility> RecipeDietCompatibilities => Set<RecipeDietCompatibility>();
    public DbSet<Allergen> Allergens => Set<Allergen>();
    public DbSet<UserAllergen> UserAllergens => Set<UserAllergen>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<RestaurantReview> RestaurantReviews => Set<RestaurantReview>();
    public DbSet<RestaurantFood> RestaurantFoods => Set<RestaurantFood>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<MealPlanMeal> MealPlanMeals => Set<MealPlanMeal>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<VideoRecipeDraft> VideoRecipeDrafts => Set<VideoRecipeDraft>();
    public DbSet<VideoRecipeDraftIngredient> VideoRecipeDraftIngredients => Set<VideoRecipeDraftIngredient>();
    public DbSet<VideoRecipeDraftStep> VideoRecipeDraftSteps => Set<VideoRecipeDraftStep>();
    public DbSet<ModerationFlag> ModerationFlags => Set<ModerationFlag>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PostTag> PostTags => Set<PostTag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUsers(modelBuilder);
        ConfigureContent(modelBuilder);
        ConfigureRecipes(modelBuilder);
        ConfigureRestaurants(modelBuilder);
        ConfigureMealPlans(modelBuilder);
        ConfigureChatAndModeration(modelBuilder);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.Email).HasMaxLength(254).IsUnicode(false).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsUnicode(false).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Sex).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.HeightCm).HasPrecision(5, 2);
            entity.Property(x => x.WeightKg).HasPrecision(6, 2);
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.Property(x => x.IsEmailVerified).IsRequired().HasDefaultValue(true);
            entity.HasOne(x => x.DietType).WithMany(x => x.Users).HasForeignKey(x => x.DietTypeId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<EmailOtpCode>(entity =>
        {
            entity.ToTable("EmailOtpCodes");
            entity.HasKey(x => x.EmailOtpCodeId);
            entity.Property(x => x.Email).HasMaxLength(254).IsUnicode(false).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.Property(x => x.CodeHash).HasMaxLength(500).IsUnicode(false).IsRequired();
            entity.Property(x => x.ExpiresAt).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.HasIndex(x => new { x.Email, x.Purpose, x.UsedAt });
        });

        modelBuilder.Entity<DietType>(entity =>
        {
            entity.ToTable("DietTypes");
            entity.HasKey(x => x.DietTypeId);
            entity.Property(x => x.Name).HasMaxLength(100).IsUnicode(false).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Allergen>(entity =>
        {
            entity.ToTable("Allergens");
            entity.HasKey(x => x.AllergenId);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<UserAllergen>(entity =>
        {
            entity.ToTable("UserAllergens");
            entity.HasKey(x => new { x.UserId, x.AllergenId });
            entity.HasOne(x => x.User).WithMany(x => x.UserAllergens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Allergen).WithMany(x => x.UserAllergens).HasForeignKey(x => x.AllergenId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureContent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.CategoryId);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(30).IsUnicode(false).IsRequired();
        });

        modelBuilder.Entity<Post>(entity =>
        {
            entity.ToTable("Posts");
            entity.HasKey(x => x.PostId);
            entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Content).IsRequired();
            entity.Property(x => x.PostType).HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.Property(x => x.VideoUrl).HasMaxLength(1000).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.HasOne(x => x.User).WithMany(x => x.Posts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Category).WithMany(x => x.Posts).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.ToTable("Comments");
            entity.HasKey(x => x.CommentId);
            entity.Property(x => x.Content).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.HasOne(x => x.Post).WithMany(x => x.Comments).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.User).WithMany(x => x.Comments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ParentComment).WithMany(x => x.Replies).HasForeignKey(x => x.ParentCommentId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PostRating>(entity =>
        {
            entity.ToTable("PostRatings");
            entity.HasKey(x => new { x.PostId, x.UserId });
            entity.Property(x => x.Rating).IsRequired();
            entity.HasOne(x => x.Post).WithMany(x => x.Ratings).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Bookmark>(entity =>
        {
            entity.ToTable("Bookmarks");
            entity.HasKey(x => new { x.UserId, x.PostId });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Post).WithMany(x => x.Bookmarks).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tags");
            entity.HasKey(x => x.TagId);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<PostTag>(entity =>
        {
            entity.ToTable("PostTags");
            entity.HasKey(x => new { x.PostId, x.TagId });
            entity.HasOne(x => x.Post).WithMany(x => x.PostTags).HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Tag).WithMany(x => x.PostTags).HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureRecipes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Recipe>(entity =>
        {
            entity.ToTable("Recipes");
            entity.HasKey(x => x.PostId);
            entity.Property(x => x.Servings).HasPrecision(5, 2);
            entity.Property(x => x.VideoUrl).HasMaxLength(1000).IsUnicode(false);
            entity.HasOne(x => x.Post).WithOne(x => x.Recipe).HasForeignKey<Recipe>(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecipeIngredient>(entity =>
        {
            entity.ToTable("RecipeIngredients");
            entity.HasKey(x => x.RecipeIngredientId);
            entity.Property(x => x.IngredientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasMaxLength(100);
            entity.Property(x => x.DietaryGroup).HasMaxLength(30).IsUnicode(false).IsRequired().HasDefaultValue("UNVERIFIED");
            entity.HasOne(x => x.Recipe).WithMany(x => x.Ingredients).HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Allergen).WithMany(x => x.RecipeIngredients).HasForeignKey(x => x.AllergenId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecipeStep>(entity =>
        {
            entity.ToTable("RecipeSteps");
            entity.HasKey(x => x.RecipeStepId);
            entity.Property(x => x.Instruction).IsRequired();
            entity.HasIndex(x => new { x.RecipeId, x.StepNumber }).IsUnique();
            entity.HasOne(x => x.Recipe).WithMany(x => x.Steps).HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecipeDietCompatibility>(entity =>
        {
            entity.ToTable("RecipeDietCompatibility");
            entity.HasKey(x => new { x.RecipeId, x.DietTypeId });
            entity.HasOne(x => x.Recipe).WithMany(x => x.DietCompatibilities).HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.DietType).WithMany(x => x.RecipeCompatibilities).HasForeignKey(x => x.DietTypeId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureRestaurants(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Restaurant>(entity =>
        {
            entity.ToTable("Restaurants");
            entity.HasKey(x => x.RestaurantId);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.HasOne(x => x.DietType).WithMany(x => x.Restaurants).HasForeignKey(x => x.DietTypeId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RestaurantFood>(entity =>
        {
            entity.ToTable("RestaurantFoods");
            entity.HasKey(x => new { x.RestaurantId, x.FoodName });
            entity.Property(x => x.FoodName).HasMaxLength(200).IsRequired();
            entity.HasOne(x => x.Restaurant).WithMany(x => x.Foods).HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RestaurantReview>(entity =>
        {
            entity.ToTable("RestaurantReviews");
            entity.HasKey(x => x.PostId);
            entity.Property(x => x.Rating).IsRequired();
            entity.HasOne(x => x.Post).WithOne(x => x.RestaurantReview).HasForeignKey<RestaurantReview>(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Restaurant).WithMany(x => x.Reviews).HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureMealPlans(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MealPlan>(entity =>
        {
            entity.ToTable("MealPlans");
            entity.HasKey(x => x.MealPlanId);
            entity.Property(x => x.StartDate).HasColumnType("date");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<MealPlanMeal>(entity =>
        {
            entity.ToTable("MealPlanMeals");
            entity.HasKey(x => x.MealPlanMealId);
            entity.Property(x => x.MealType).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.HasOne(x => x.MealPlan).WithMany(x => x.Meals).HasForeignKey(x => x.MealPlanId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureChatAndModeration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.ToTable("ChatSessions");
            entity.HasKey(x => x.ChatSessionId);
            entity.Property(x => x.GuestAccessTokenHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("DATEADD(HOUR, 7, SYSUTCDATETIME())");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("ChatMessages");
            entity.HasKey(x => x.ChatMessageId);
            entity.Property(x => x.Sender).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(x => x.Content).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("DATEADD(HOUR, 7, SYSUTCDATETIME())");
            entity.HasOne(x => x.ChatSession).WithMany(x => x.Messages).HasForeignKey(x => x.ChatSessionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VideoRecipeDraft>(entity =>
        {
            entity.ToTable("VideoRecipeDrafts");
            entity.HasKey(x => x.VideoRecipeDraftId);
            entity.Property(x => x.VideoUrl).HasMaxLength(1000).IsUnicode(false).IsRequired();
            entity.Property(x => x.GeminiFileName).HasMaxLength(200).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(250);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
            entity.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("DATEADD(HOUR, 7, SYSUTCDATETIME())");
            entity.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("DATEADD(HOUR, 7, SYSUTCDATETIME())");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VideoRecipeDraftIngredient>(entity =>
        {
            entity.ToTable("VideoRecipeDraftIngredients");
            entity.HasKey(x => x.VideoRecipeDraftIngredientId);
            entity.Property(x => x.IngredientName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasMaxLength(100);
            entity.Property(x => x.DietaryGroup).HasMaxLength(30).IsUnicode(false).IsRequired();
            entity.HasOne(x => x.Draft).WithMany(x => x.Ingredients).HasForeignKey(x => x.VideoRecipeDraftId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VideoRecipeDraftStep>(entity =>
        {
            entity.ToTable("VideoRecipeDraftSteps");
            entity.HasKey(x => x.VideoRecipeDraftStepId);
            entity.Property(x => x.Instruction).IsRequired();
            entity.HasIndex(x => new { x.VideoRecipeDraftId, x.StepNumber }).IsUnique();
            entity.HasOne(x => x.Draft).WithMany(x => x.Steps).HasForeignKey(x => x.VideoRecipeDraftId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ModerationFlag>(entity =>
        {
            entity.ToTable("ModerationFlags", table => table.HasCheckConstraint(
                "CK_ModerationFlags_OneTarget",
                "([PostId] IS NULL AND [CommentId] IS NOT NULL) OR ([PostId] IS NOT NULL AND [CommentId] IS NULL)"));
            entity.HasKey(x => x.FlagId);
            entity.Property(x => x.Source).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
            entity.HasOne(x => x.Post).WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Comment).WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReportedBy).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.NoAction);
        });
    }
}

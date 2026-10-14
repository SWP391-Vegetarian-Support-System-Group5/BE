# Aiven schema (verified 2026-10-14)

Database: `VegetarianSupportSystemDB`. This list was read from `information_schema.COLUMNS` on Aiven. It includes 28 application tables and two metadata/legacy utility tables. Column order follows the database. Navigation properties in C# are not database columns.

| Table | Columns | Purpose |
| --- | --- | --- |
| `Users` | `UserId`, `Email`, `PasswordHash`, `Role`, `CreatedAt`, `UpdatedAt`, `IsActive`, `IsEmailVerified` | Login accounts and permissions. |
| `UserProfiles` | `UserId`, `FullName`, `Sex`, `HeightCm`, `WeightKg`, `DietTypeId`, `Latitude`, `Longitude`, `ActivityLevel`, `BirthDate`, `HealthGoal` | Personal profile, dietary preference and location; `UserId` links to `Users`. |
| `EmailOtpCodes` | `EmailOtpCodeId`, `Email`, `Purpose`, `CodeHash`, `ExpiresAt`, `FailedAttempts`, `UsedAt`, `CreatedAt` | Email verification and password reset codes. |
| `DietTypes` | `DietTypeId`, `Name` | Vegetarian diets used by profiles, restaurants and recipe compatibility. |
| `Allergens` | `AllergenId`, `Name` | Allergen names. |
| `UserAllergens` | `UserId`, `AllergenId` | User-to-allergen many-to-many mapping. |
| `Categories` | `CategoryId`, `Name`, `Type` | Legacy post-level categories, referenced by `Posts.CategoryId` and video drafts. |
| `Posts` | `PostId`, `UserId`, `CategoryId`, `Title`, `Content`, `PostType`, `VideoUrl`, `Status`, `ThumbnailUrl`, `CreatedAt`, `UpdatedAt` | User-authored blog/recipe post and publication state. `Title` is the post title. |
| `Comments` | `CommentId`, `PostId`, `UserId`, `ParentCommentId`, `Content`, `Status`, `CreatedAt`, `UpdatedAt` | Comments and threaded replies. |
| `PostRatings` | `PostId`, `UserId`, `Rating` | One user's rating of one post. |
| `Bookmarks` | `UserId`, `PostId` | Saved posts per user. |
| `Recipes` | `PostId`, `PrepMinutes`, `CookMinutes`, `Servings`, `VideoUrl`, `CaloriesPerServing`, `RecipeName` | Recipe detail for a post. `PostId` is both primary key and reference to `Posts`; `RecipeName` is the dish name. |
| `RecipeIngredients` | `RecipeIngredientId`, `RecipeId`, `IngredientName`, `Amount`, `AllergenId`, `DietaryGroup` | Ingredients for a recipe; dietary group is stored on each ingredient row. |
| `RecipeSteps` | `RecipeStepId`, `RecipeId`, `StepNumber`, `Instruction` | Ordered cooking steps. |
| `RecipeDietCompatibility` | `RecipeId`, `DietTypeId`, `IsCompatible` | Existing recipe-to-diet compatibility flags used by the meal planner. |
| `RecipeCategories` | `Id`, `Name`, `Slug`, `Type`, `Description`, `ImageUrl`, `IsActive`, `CreatedAt` | New recipe taxonomy: `MEAL_TYPE`, `CUISINE`, `DISH_TYPE`, `LIFESTYLE`, `DIETARY_TYPE`. |
| `RecipeCategoryMappings` | `RecipeId`, `CategoryId` | Many-to-many recipe-to-category links; `RecipeId` points to `Recipes.PostId`. |
| `Restaurants` | `RestaurantId`, `Name`, `Address`, `Latitude`, `Longitude`, `DietTypeId` | Restaurant directory and coordinates. |
| `RestaurantFoods` | `RestaurantId`, `FoodName` | Food names associated with a restaurant. |
| `RestaurantReviews` | `PostId`, `RestaurantId`, `Rating` | Restaurant review attached to a post. |
| `MealPlans` | `MealPlanId`, `UserId`, `StartDate`, `AvailableIngredients`, `BMI` | User's generated plan and available-ingredient input. |
| `MealPlanMeals` | `MealPlanMealId`, `MealPlanId`, `DayNumber`, `MealType`, `RecipeId` | A selected recipe for a day and meal slot. |
| `ChatSessions` | `ChatSessionId`, `UserId`, `GuestAccessTokenHash`, `CreatedAt` | Chat history owner; supports registered users and guests. |
| `ChatMessages` | `ChatMessageId`, `ChatSessionId`, `Sender`, `Content`, `CreatedAt` | Messages within a chat session. |
| `VideoRecipeDrafts` | `VideoRecipeDraftId`, `UserId`, `VideoUrl`, `GeminiFileName`, `Status`, `Title`, `Description`, `Transcript`, `EstimatedPrepMinutes`, `ErrorMessage`, `CreatedAt`, `UpdatedAt`, `CategoryId`, `RecipeName` | Video analysis draft, status, transcript and proposed post/recipe names. |
| `VideoRecipeDraftIngredients` | `VideoRecipeDraftIngredientId`, `VideoRecipeDraftId`, `IngredientName`, `Amount`, `DietaryGroup`, `AllergenId` | Extracted or edited draft ingredients. |
| `VideoRecipeDraftSteps` | `VideoRecipeDraftStepId`, `VideoRecipeDraftId`, `StepNumber`, `Instruction` | Extracted or edited draft instructions. |
| `ModerationFlags` | `FlagId`, `PostId`, `CommentId`, `Source`, `ReportedBy`, `Reason`, `Status`, `ReviewedBy` | Reports and moderation decisions for posts/comments. |

| Other table | Columns | Purpose |
| --- | --- | --- |
| `__EFMigrationsHistory` | `MigrationId`, `ProductVersion` | EF Core migration bookkeeping. |
| `sysdiagrams` | `name`, `principal_id`, `diagram_id`, `version`, `definition` | Legacy database diagram storage; not an app entity. |

`Tags`, `PostTags`, the five temporary `Backup_*` tables, and the seven requested legacy columns are absent. `Recipes.Servings`, `Recipes.CaloriesPerServing`, and `UserProfiles.HealthGoal` remain because the removal request targeted the corresponding columns in `VideoRecipeDrafts` and `MealPlans`.

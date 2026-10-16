# Shared Ingredient Schema

- `IngredientCategories`: `IngredientCategoryId`, `Code`, `Name`.
- `Ingredients`: `IngredientId`, `Name`, `IngredientCategoryId`, optional `AllergenId`.
- `RecipeIngredients`: `RecipeIngredientId`, `RecipeId`, `IngredientId`, optional `Amount`.
- `RecipeCategories` and `RecipeCategoryMappings` continue classifying recipes.

The API resolves ingredient names to shared catalog entries when a recipe is created or updated. Existing entries supply their category and allergen; conflicting classifications are rejected. Ingredient amount belongs to the recipe link. Names are unique with case-insensitive, accent-sensitive comparison.

Video draft ingredients remain editable AI proposals. Publishing resolves them into the shared ingredient catalog.

Catalog lookup uses `GET /api/ingredients` and `GET /api/ingredient-categories`. Admins edit shared ingredients using `PUT /api/admin/ingredients/{id}` or the frontend page `/admin/ingredients`. Updating a category recomputes dietary compatibility/labels for recipes and publishes realtime refresh events. An unverified ingredient must be classified before it is used in a published recipe.

The schema update copies legacy ingredient names, groups and allergens before removing those columns from `RecipeIngredients`. A verified group is preferred over legacy `UNVERIFIED` entries. Conflicting verified groups or multiple allergen IDs stop conversion before legacy columns are removed.

It also removes `UserProfiles.HealthGoal`, `Posts.CategoryId`, `VideoRecipeDrafts.CategoryId`, `Categories`, and `RestaurantReviews`. Existing posts are retained. Post creation no longer accepts a legacy category ID. Restaurant search/location features remain available.

## Apply and Verify

`IngredientSchemaUpdate.ApplyAsync` runs during API startup when `Database:RunStartupTasks` is enabled. This project excludes the old SQL Server migrations from compilation; the MySQL update uses the existing startup schema-update mechanism.

For the configured local Aiven connection, run from the `BE` directory:

```powershell
dotnet run --project Tests/RecipeCategories -- --inspect-aiven
dotnet run --project Tests/RecipeCategories -- --apply-aiven
dotnet run --project Tests/RecipeCategories
```

The apply command saves the affected legacy data to an ignored local JSON backup under `.local/schema-backups`, applies the update twice to verify repeatability, rebuilds dietary compatibility and labels, and checks that recipe/link counts were preserved. It reads the connection string from `API/.aiven.env` without printing it.

Deploy the updated backend and frontend together. Old backend binaries still reference the removed columns/tables and cannot run against the new schema.

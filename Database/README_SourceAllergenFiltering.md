# BE handoff: verified ingredient-source allergy filtering

## Cause and behavior
The old `Ingredient.AllergenId` represented only one allergy. Feed/search checked that nullable field, so missing source relations (soy-derived tofu, animal-milk yogurt) passed. Planner/chat then used different name/AI checks; chat could limit candidates before those checks. A matching dish name could therefore receive a different result on different screens.

`PersonalRecipeFilter` now uses stored diet + current user allergy IDs, legacy relation + all `IngredientAllergens` relations. ANY matching selected allergen excludes a recipe. As clarified by the product owner, incomplete profile verification alone does not exclude a recipe, including in Meal Planner. Adding allergy selections progressively removes matching dishes; removing selections restores otherwise eligible dishes. No inference from generic tofu/yogurt names, title, transcript or an AI safety verdict is accepted. Without saved allergies the allergy restriction is removed; publication, classified vegetarian ingredients and saved diet compatibility still apply.

Applies before keyword/category/rating order/limits in public posts (including VIDEO with linked recipe), recipe search, bookmarks and chatbot-related recipes. Planner generation/options/explicit replacement and existing-plan validation use the same rule. Admin list and author's management list are unaffected. Details remain accessible to owners/admin for correction.

No profile or recommendation-result cache is used. Existing planner cache contains usage counters only, not eligible recipes. Every query reads the account's latest persisted selections. Video-derived and manually entered recipes both resolve through IngredientCatalogService; optional ingredientId selects an exact canonical record. Name-only resolution is exact, not an alias/source guess. New records remain unverified. Source-specific product names are required when composition differs.

## Existing selection API (unchanged)
GET /api/allergens returns the administrator's selectable catalog, e.g.
```json
[{"id":1,"name":"Đậu nành","type":null},{"id":2,"name":"Sữa","type":null}]
```
IDs here are illustrative, not hardcoded. All existing catalog entries are selectable; there is no active/selectable flag in the present model.

PUT /api/users/me/allergens, Authorization: Bearer <JWT>:
```json
{"allergenIds":[1,2,1]}
```
Response 200 (userId is derived from JWT):
```json
{"userId":42,"allergens":[{"id":1,"name":"Đậu nành","type":null},{"id":2,"name":"Sữa","type":null}]}
```
GET /api/users/me/allergens returns this same shape. PUT with `{"allergenIds":[]}` clears selections and returns `{"userId":42,"allergens":[]}`. Duplicate IDs are removed. Nonpositive IDs: 400. Missing catalog IDs: 404, previous selection remains. Null/missing list: validation error. No custom allergen creation is supported on this API. One SaveChanges must succeed before returning stored values. Overlapping saved IDs are retained rather than deleted/reinserted.

## Mapping verification and migration (NOT executed automatically)
Migration: `202611230001_AddVerifiedIngredientAllergens`, explicitly included in DAL.csproj.
- Adds IsAllergenProfileVerified=false, AllergenEvidence (nullable).
- Creates IngredientAllergens with composite key, FK restrictions and allergen lookup index.
- Copies every non-null legacy Ingredient.AllergenId into the link table; retains the original column.
- Does NOT mark a partial legacy relation as a complete source audit.
- No bulk name-based source assignment or production data change is performed.
- Automatic Down is refused because dropping the link table would lose multiple relations. Supply a reviewed preservation/backup procedure.
Review/apply this migration separately in a test database before rollout; no startup auto-migration was added. Migration defaults remain unverified; this flag is review metadata, not a filter gate. Empty results occur when all candidates fail recorded allergy matches or the existing diet/publication rules; no conditions are widened.

READ-ONLY audit: IngredientAllergenAudit.sql. We have NOT audited or mapped the production ingredient inventory.

Admin-only PUT /api/admin/ingredients/{id}/allergens:
```json
{"allergenIds":[1,3],"isVerified":true,"evidence":"Product-specific ingredient label: soy and sesame; reviewed by administrator."}
```
Both IDs must exist. Evidence is required to verify a complete profile. This replaces the full mapping, supports verified absence with `allergenIds:[]`, and synchronizes the legacy field to the first ID for old clients. Admin must use actual product composition; do not mark generic ambiguous names verified based on examples. Examples to review:
- Soy-derived tofu / soy milk -> Soy.
- Animal-milk yogurt -> Milk.
- Chickpea tofu / coconut yogurt -> actual composition, NOT assumed Soy/Milk.
Any admin rename/category/legacy allergy edit invalidates verification until a new review.
Filtering reflects recorded relationships only; an unmapped allergen can still pass. This is not a guarantee of allergy safety. FE must not label an unmatched dish as verified allergen-free. Admins still need to complete origin mappings. The existing recipe response retains unverifiedIngredientNames; this BE-only change does not add warning UI or change list/planner response shapes.
GET /api/ingredients adds allergenIds, isAllergenProfileVerified, allergenEvidence. RecipeResponse adds unverifiedIngredientNames so authors can see what needs catalog review; an empty list means each included ingredient has a reviewed profile, not a medical guarantee.

## Lists and ordering
GET /api/posts?sort=rating&limit=20 uses the current JWT profile; sort=rating orders averageRating DESC, ratingCount DESC, then PostId DESC on the entire FILTERED set before limit. Omit sort (or latest) to keep existing latest order. No new paging wrapper.
GET /api/recipes/search?keyword=...&category=... returns the full eligible array; FE can sort by the additive averageRating/ratingCount fields.

Illustrative returned recipe item (other existing fields still present):
```json
[{"postId":6,"title":"Cơm","content":"Recipe","status":"PUBLISHED","videoUrl":null,"thumbnailUrl":null,"prepMinutes":10,"cookMinutes":10,"servings":1,"ingredients":[{"ingredientName":"Gạo","amount":"100g","dietaryGroup":"PLANT","allergenId":null,"ingredientCategoryId":1,"ingredientId":6}],"steps":[{"stepNumber":1,"instruction":"Cook"}],"compatibleDietTypeIds":[1,2],"recipeName":"Cơm","categories":[],"moderationReason":null,"imageUrl":null,"caloriesPerServing":700,"averageRating":4,"ratingCount":2,"unverifiedIngredientNames":[]}]
```
No matching lists: HTTP 200 `[]`. This remains true for an exact excluded title/category.
Planner generation preserves MealPlanResponse shape: no eligible recipes -> HTTP 200 with mealPlanId=0 and meals=[]; this is NOT stored. FE should check meals.length before showing plan actions. Example:
```json
{"mealPlanId":0,"startDate":"2026-11-23T00:00:00","bmi":20.8,"bmiCategory":"Normal","meals":[],"tdee":2300,"targetCaloriesPerDay":2300,"nutritionGoal":"MAINTAIN","calorieAdjustment":null}
```
Planner option list can be []; explicitly choosing an excluded recipe returns 422. An existing plan that no longer matches changed profile returns 409 when modified/viewed rather than silently reusing its foods.

## Validation
Only isolated EF InMemory stores and fake media/provider responses are used by the new tests. SQL translation is checked with Pomelo ToQueryString, without opening MySQL. No production database, accounts or .env secrets are used by CI.
```sh
dotnet build Tests/RecipeCategories/RecipeCategories.csproj
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj --no-build -- --source-allergens
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj --no-build -- --source-allergens-api
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj --no-build
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj --no-build -- --video-analysis
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj --no-build -- --birth-year
```
New source suite: soy, soy tofu/milk, animal vs plant yogurt, multi-allergen ANY semantics, exact-name/category/home/video consistency, safe foods retained, removal/clear, isolated accounts, author/admin bypass, canonical manual/video resolution, unverified reporting/admin review, ratings before limit, MySQL translation, planner/options/explicit selection, empty lists/empty plan, and 100 stored allergy selections.
New HTTP suite: JWT required, userId body cannot target another account, ID validation, duplicate/empty selections, catalog read, user cannot create allergens or verify ingredient sources.
Live MySQL execution and production mapping completeness are separate rollout checks; do not claim they passed from these tests.

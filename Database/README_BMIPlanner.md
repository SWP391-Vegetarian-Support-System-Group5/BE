# Recipe BMI groups

## Schema and deployment

The backend uses EF Core 8 / Pomelo MySQL. `Recipes.PostId` is the existing
recipe primary key shared with Posts; this change does not create a second recipe table.

- `BMITypes`: `BMITypeId` (identity PK), `Code` (unique, fixed to UNDERWEIGHT,
  NORMAL, OBESE), `Name` (Vietnamese), `Description` (nullable).
- `RecipeBMITypes`: `RecipeId` -> `Recipes.PostId`, `BMITypeId` ->
  `BMITypes.BMITypeId`. Composite PK prevents duplicates; reverse composite
  index supports filtering by BMI type. Recipe deletion cascades mappings;
  BMI type deletion is restricted.

`DAL/Data/BMISchemaUpdate.cs` follows the project's existing additive schema-update
mechanism. Note that `DAL.csproj` excludes `Data/Migrations/**/*.cs` from compilation;
running `dotnet ef database update` alone will NOT apply this schema update.

Normal API startup calls this update when `Database:RunStartupTasks=true` (the
existing default). It creates missing tables and seeds only the three fixed BMI
types. It does not reset the DB, alter existing columns, or assign recipes to groups.

To apply only this update using the existing ignored `API/.aiven.env`, from `BE`:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --apply-bmi-aiven
```

The command runs the update twice to verify idempotence and checks that post and
recipe counts are preserved. Use normal API deployment for other environments.
No credentials are stored in source control by this change.

## Admin workflow

Sign in as ADMIN and open `/admin/content`. The new Recipe BMI groups section
loads real recipes (including unpublished ones, excluding removed recipes).
Find the recipe, check one or more groups, and Save. Uncheck all to remove all
assignments. The Create recipe link opens the existing `/create-content` form,
which also includes BMI checkboxes for administrators in manual and AI-video modes.

Endpoints:

- `GET /api/bmi-types`: the three fixed options; no create/delete endpoint.
- `GET /api/admin/recipes/bmi-types`: real recipes and current assignments; ADMIN.
- `PUT /api/admin/recipes/{recipeId}/bmi-types`: `{ "bmiTypeIds": [1, 2] }`; ADMIN.
- Recipe create/update and video-draft publish accept optional `bmiTypeIds`.
  If omitted, existing assignments stay unchanged on edit. `[]` clears them.
  Supplying the field as a normal user is rejected, including an empty array.

Both the admin route and service check permissions; IDs are validated server-side.
Recipe create/update and mappings commit in the same transaction. BMI-only changes
also use a transaction and publish the existing `postUpdated` event.

## Planner behavior

Existing height/weight calculation and rounding are preserved. Per the project
owner's explicit instruction, the old Overweight group is now called OBESE:

- BMI < 18.5: UNDERWEIGHT.
- 18.5 <= BMI < 25: NORMAL.
- BMI >= 25: OBESE.

These are this project's existing business thresholds with the requested rename,
not a newly introduced medical classification standard. No calories/macros are added.

Generate, regenerate and replace all require profile measurements and a vegetarian
type, then filter published recipes by BMI mappings, vegetarian compatibility and
allergies. Unassigned recipes are excluded. Only then does the existing bilingual
ingredient matcher rank recipes by matching ingredient count/coverage. Ingredients
are a preference, not a requirement that all ingredients must be available.

AI receives only eligible candidates, and returned IDs are validated against that
set. Local fallback uses the same eligible set. An empty set returns a localized
validation message instead of inventing food or ignoring BMI. Failed regeneration
leaves the previous plan intact. Existing saved plans remain readable.

No recipe-group assignments are auto-seeded. An administrator must review and
assign existing recipes before the planner can return new plans.

## Verification

```powershell
# From BE: original category tests plus BMI/service/planner/authorization tests
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj
dotnet build API/API.csproj

# From FE
npx tsc --noEmit
npx eslint src/lib/bmi-api.ts src/components/recipe/BmiTypePicker.tsx src/components/recipe/RecipeBmiManager.tsx src/components/recipe/CreateContentView.tsx src/lib/recipe-api.ts src/lib/my-content.ts src/lib/localized-content.ts
node --test tests/bmi-api.test.mjs tests/merge-contract.test.mjs
npm run build
```

Service tests run against EF InMemory, not the shared production database. They
cover fixed seeds, mapping keys/cascade metadata, multi-selection, deduplication,
removal, invalid IDs, admin/user behavior, create/update, BMI + vegetarian filtering,
ingredient ranking, empty results, regeneration safety, replacement and OBESE.
They do not claim relational transaction rollback testing or live AI-provider testing.

## Changed files

Backend: `API/Program.cs`, `API/Controllers/RecipeBmiController.cs`,
`DAL/Entities/BMIType.cs`, `DAL/Entities/VegetarianSupportEntities.cs`,
`DAL/Data/VegetarianDbContext.cs`, `DAL/Data/BMISchemaUpdate.cs`,
`BLL/Common/MealPlannerBmi.cs`, `BLL/DTOs/ApiRequestsAndResponses.cs`,
`BLL/Services/RecipeBmiService.cs`, `BLL/Services/RecipeService.cs`,
`BLL/Services/MealPlanService.cs`, `BLL/Services/VideoRecipeServices.cs`,
`BLL/Services/BusinessLogicRegistration.cs`, `Tests/RecipeCategories/BmiTests.cs`,
`Tests/RecipeCategories/Program.cs`, `Tests/RecipeCategories/RecipeCategories.csproj`.

Frontend: `src/lib/bmi-api.ts`, `src/lib/recipe-api.ts`, `src/lib/my-content.ts`,
`src/lib/localized-content.ts`, `src/app/admin/content/page.tsx`,
`src/components/recipe/BmiTypePicker.tsx`, `src/components/recipe/RecipeBmiManager.tsx`,
`src/components/recipe/CreateContentView.tsx`, `tests/bmi-api.test.mjs`.

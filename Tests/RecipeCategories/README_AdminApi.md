# Admin API regression checks

Run from BE:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --admin-api
```

Location HTTP regression checks:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --location-api
```

These cover saving/reloading coordinates and the living address, zero coordinates,
omitted addresses, missing/out-of-range coordinates, oversized addresses and guest
access. The valid PUT test catches MVC record validation metadata errors that
service-only tests cannot detect. `LivingAddress` validation belongs on the record
constructor parameter, while `JsonRequired` remains on coordinate properties for
JSON deserialization. Tests use isolated storage and never update live profiles.

Allergen selection persistence can also be checked against the configured MySQL:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --verify-allergen-selection-mysql
```

This runs the real selection service inside an always-rolled-back transaction,
checks repeat/overlapping selections, removals and invalid IDs, then verifies the
original selection is unchanged. It never calls AI or changes the schema.

The runner starts a temporary HTTP server on an OS-assigned loopback port and
stops it in a finally block. It uses actual API controllers, business services,
validation, exception middleware and signed JWTs obtained through the actual
login endpoint. Its EF InMemory database is unique per run. It does not read
the application's env files, contact Aiven, send email, or call AI services.

Coverage: the 25 admin-only method/route definitions listed in the runner,
including AdministrationController and RecipeCategoriesController.
Each rejects guests with 401 and authenticated USER accounts with 403.
Successful ADMIN operations and relevant validation/not-found paths are checked:

- Users: list/detail, disable, enable, soft delete, disabled login rejection.
- Posts/comments: list/filter and soft delete, including realtime response creation.
- Allergens: create/update/delete, duplicate and empty-name rejection.
- Restaurants: create/read/update/delete, food replacement/deduplication, invalid diet.
- Moderation: list/detail, approve, remove post/comment, missing report rejection.
- Ingredients: update shared ingredient and verify the recipe reflects its group.
- Recipe categories: create/update/deactivate, duplicate-name and blank/oversized-name validation,
  public active-only vs admin include-inactive visibility.

Restaurant reference endpoints require `IRestaurantRouteService` when their
controller is constructed. The runner registers a stub that fails if routing is
actually called, so these checks cannot contact an external route provider.
Recipe category requests follow the current name/active contract; they do not
expect the removed slug/type fields. A failing `--admin-api` run prints
`ADMIN API FAIL` and exits with code 1 instead of leaving an unhandled exception
for the Windows Just-In-Time debugger.

At completion the suite reports 147 HTTP checks. Failures are printed to stderr
and return exit code 1. The database disappears with the process.

Regression fixed during this audit: DeleteCommentAsync used FindAsync without
loading User/Profile. Building the commentRemoved payload dereferenced User and
returned 500 after saving REMOVED. It now loads the author before the mutation;
the HTTP delete test covers this fresh-request scenario.

Separate live verification on Aiven used the existing administrator login and
14 GET requests covering users/detail, posts/filters, comments, moderation,
recipe BMI assignments, recipe categories and reference catalogs. All returned
200. No live users/posts/comments were deleted or blocked by this audit.

Limitations: EF InMemory does not enforce MySQL foreign keys or transaction
rollback semantics. The isolated suite verifies HTTP/business behavior, not all
possible inputs, concurrency, live CRUD constraints, or browser interactions.

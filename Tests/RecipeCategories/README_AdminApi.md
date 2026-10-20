# Admin API regression checks

Run from BE:

```powershell
dotnet run --project Tests/RecipeCategories/RecipeCategories.csproj -- --admin-api
```

The runner starts a temporary HTTP server on an OS-assigned loopback port and
stops it in a finally block. It uses actual API controllers, business services,
validation, exception middleware and signed JWTs obtained through the actual
login endpoint. Its EF InMemory database is unique per run. It does not read
the application's env files, contact Aiven, send email, or call AI services.

Coverage: all 23 current admin-only method/route definitions in
AdministrationController, RecipeCategoriesController and RecipeBmiController.
Each rejects guests with 401 and authenticated USER accounts with 403.
Successful ADMIN operations and relevant validation/not-found paths are checked:

- Users: list/detail, disable, enable, soft delete, disabled login rejection.
- Posts/comments: list/filter and soft delete, including realtime response creation.
- Allergens: create/update/delete, duplicate and empty-name rejection.
- Restaurants: create/read/update/delete, food replacement/deduplication, invalid diet.
- Moderation: list/detail, approve, remove post/comment, missing report rejection.
- Ingredients: update shared ingredient and verify the recipe reflects its group.
- Recipe categories: create/update/deactivate, duplicate/type validation,
  public active-only vs admin include-inactive visibility.
- BMI: admin recipe create, list assignments, update/deduplicate/clear,
  invalid group and missing recipe rejection.

At completion the suite reports 113 HTTP checks. Failures throw and return a
nonzero exit code. The database disappears with the process.

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

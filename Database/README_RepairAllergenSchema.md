# Repair missing ingredient allergy schema (MySQL 8)

## Confirmed failure
The supplied log starts successfully, then reports:
- Unknown column 'i.AllergenEvidence' in 'field list'.
- Table 'VegetarianSupportSystemDB.IngredientAllergens' doesn't exist.

The ingredient allergy entity model is running against an older schema. Recipe details, posts, bookmarks, planner and nearby restaurants load that entity, so they repeat the same underlying SQL failure. EF's multiple-collection warning is separate; it does not cause these missing-column/table exceptions.

GitHub Tri includes the new code after merge ce649bc. Git merge, build and push do not apply database migrations. API startup intentionally does not migrate the database.

## Local/test repair
1. Stop BE. Back up the intended local/test database. Check the actual MySQL host and database in your running configuration; localhost:5000 is the API address, not evidence that MySQL is local. API/.aiven.env can select a remote database even in Development.
2. In MySQL Workbench, open a connection to your LOCAL/TEST copy. Run:
   ```sql
   SELECT DATABASE(), @@hostname;
   SHOW COLUMNS FROM Ingredients;
   SHOW TABLES LIKE 'IngredientAllergens';
   ```
3. Select that local/test schema as default. Open Database/ApplyVerifiedIngredientAllergens.sql and execute the whole file with stop-on-error enabled. With the mysql CLI, use its source command from that same selected connection. Never use --force.
4. The file adds the two missing columns and relation table; copies non-null legacy AllergenId links; keeps legacy columns, existing multi-links, evidence, reviews and other data; records ONLY this migration in EF history. It can be rerun after a partial interruption that left the expected columns/table. It does not repair arbitrarily malformed existing tables. MySQL DDL is not transactional, so keep the backup.
5. Restart BE. With a logged-in account, retry recipe details, /api/posts, /api/users/me/bookmarks, /api/meal-plans and /api/restaurants/nearby, supplying each endpoint's normal parameters. Confirm missing-schema 500s no longer occur.
6. Profiles remain unverified unless already reviewed. Review actual product compositions through PUT /api/admin/ingredients/{id}/allergens. Empty personalized recipe results may be expected until that review is complete. Do not bulk mark unknown ingredients safe.

Alternatively, use the existing EF migration after reviewing the generated SQL and target connection. Do not run a generic database update against API's configured remote host without reviewing all pending migrations. The targeted SQL repair records its own migration so EF will not duplicate it.

No server database or deployment is changed by committing these files. The CI schema job uses only a disposable MySQL container; it cannot validate your live connection or prove that your local API has recovered.

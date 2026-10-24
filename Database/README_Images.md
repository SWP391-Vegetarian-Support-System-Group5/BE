# Image APIs

All writes require `Authorization: Bearer <JWT>`. Image bytes are sent to BE, never a client-supplied Cloudinary public ID or deletion URL. Only assets uploaded by this feature are deleted; existing video thumbnails are not deleted.

## Endpoints

| Method | Route | Request |
| --- | --- | --- |
| PUT | `/api/users/me/avatar` | multipart/form-data, field `file` |
| DELETE | `/api/users/me/avatar` | no body |
| PUT | `/api/posts/{postId}/image` | multipart/form-data, field `file` |
| DELETE | `/api/posts/{postId}/image` | no body |
| PUT | `/api/meal-plans/{planId}/meals/{mealId}/image` | multipart/form-data, field `file` |
| POST | `/api/meal-plans/{planId}/meals/{mealId}/image/generate` | JSON `{ "style": "Top-down food photography" }`, or `{}` |
| DELETE | `/api/meal-plans/{planId}/meals/{mealId}/image` | no body |

Each successful write returns HTTP 200:

```json
{ "imageUrl": "https://res.cloudinary.com/...", "isGenerated": false, "previousImageDeleted": true }
```

Delete returns `imageUrl: null`. This removes the stored image selection, not the post, account or meal. Post owner/admin may edit post images; avatar is always the signed-in user's avatar; meal image writes are restricted to the plan owner. Do not set a FormData request's Content-Type manually in FE: the browser must supply its boundary.

File uploads accept JPEG/PNG/WebP up to 5 MiB. The server checks byte signatures and declared MIME type. Generate accepts at most 300 style characters and has an in-process limit of 3 requests/minute per user; this is not an account billing cap and is not shared across replicas. Provider errors do not produce placeholder images. `413` means too large, `415` means unsupported/mismatched image content, `401/403/404` cover authentication/ownership/missing target, `409` means a simultaneous image change, `429` means local/provider limits, and `502/503` indicate provider/configuration/timeout failures.

## Read/persistence flow

- `GET /api/users/me`, login and profile responses include `avatarUrl`.
- Post lists/detail/bookmarks/my-posts include `imageUrl`.
- Recipe detail/search includes `imageUrl` from its post.
- Meal-plan GET responses include `imageUrl` and `imageIsGenerated` on each meal. A personal image wins, then the shared recipe post image, then its existing video thumbnail.
- FE should label generated images as illustrations using the flag. Existing image URL fields remain compatible; use the new field for uploaded covers.
- Create the post/recipe first, obtain its `postId`, then upload the cover. A cover-upload failure does not undo post creation: offer retry for that post ID.
- Generating/regenerating an image is a separate user action after a meal plan has been created. It does not change the selected recipe, ingredients or anyone else's recipe image. There is no automatic image generation during plan creation.
- Changing a meal's recipe resets its personal image. Plan replacement/deletion cleans images after saving changes. If an outer transaction owns plan regeneration, image cleanup is deferred to avoid deleting images before that transaction commits.

## Replacement safety

BE uploads under a new unique Cloudinary ID, saves URL/public ID to DB, then deletes the previous managed asset. Upload/generation failure preserves the old image. Failed DB saves trigger cleanup of the new asset. An optimistic concurrency check prevents simultaneous requests from silently overwriting one another. A Cloudinary delete failure does not roll back a successfully saved image; `previousImageDeleted: false` reports this and logs the asset ID for manual cleanup. Cleanup currently has no persistent retry worker; inspect warning logs and retry those deletions operationally. Do not remove images still in use elsewhere.

## MySQL and Render configuration

Seven nullable/defaulted columns are added; no existing table/data is removed. New code must have these columns before serving DB-backed requests. With `Database__RunStartupTasks=true`, startup runs `ImageSchemaUpdate` before seed. If startup tasks are disabled or the runtime DB user lacks ALTER privileges, apply `Database/20261024_AddImages.sql` to the intended MySQL database before deploying the API. SQL Server migrations remain excluded by this project's current MySQL setup. The script can be rerun for missing columns.

Configure these environment variables on Render (local `.env` is not included in the image):

```text
Cloudinary__CloudName=<cloud name>
Cloudinary__ApiKey=<api key>
Cloudinary__ApiSecret=<api secret>
GeminiImages__Model=gemini-2.5-flash-image
GeminiImages__ApiKey=<key with access and quota for an image-generation model>
```

If `GeminiImages__ApiKey` is absent, the existing `Gemini__ApiKey` is used. Text/chat models do not generate images. The image model is configurable; its access, billing and quotas must be enabled separately by the provider. Client-visible timeouts may also depend on the deployment proxy. No secret is needed in FE. Cloudinary images persist independently of Render's temporary filesystem.

## Verification

`dotnet run --project Tests/ImageMedia/ImageMedia.csproj` runs isolated in-memory/provider-stub tests without touching a real DB. `-- --cloudinary` explicitly performs a small real upload and deletes that new test asset; `-- --live-generation` explicitly invokes Gemini and may consume provider quota. These options read local ignored `.env` files or process environment variables. Do not commit those files or their values.

The implementation was checked with a real Cloudinary upload/delete successfully. The configured Gemini key returned HTTP 429 on the real image-generation probe; provider quota/access must be resolved before claiming the live Generate flow works. No shared/live DB schema was changed during those checks.

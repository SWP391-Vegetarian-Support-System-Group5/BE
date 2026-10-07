# Vegetarian Support System API

Backend MUST-HAVE cho hệ thống hỗ trợ người ăn chay. API cung cấp xác thực JWT, cộng đồng bài viết/công thức, nhà hàng, meal planner 7 ngày, chatbot/AI mock và moderation.

## Architecture

```text
API (controllers, JWT, Swagger, middleware)
    -> BLL (business services, validation, meal selection, AI abstractions)
        -> DAL (EF Core entities, DbContext, repositories)
            -> SQL Server
```

`API` không truy cập `DbContext` trực tiếp. `BLL` chỉ làm việc qua repository ở `DAL`.

## Technology

- ASP.NET Core Web API / .NET 8
- C# and Entity Framework Core 8
- SQL Server Express
- JWT Bearer authentication
- Swagger / OpenAPI

## Project structure

```text
API/    Presentation layer, controllers, middleware, JWT configuration
BLL/    DTOs, services, business rules, nutrition chatbot and RAG services
DAL/    23 entity mappings, VegetarianDbContext, repositories
```

## Database

The application maps the existing `VegetarianSupportSystemDB` schema. On startup, EF Core automatically applies pending migrations before seed data is checked. The migrations remove discontinued nutrition-calculation fields, secure guest chat sessions, and add the RAG Vector Store configuration table.

The Gemini and SendGrid API keys are stored in `API/.env`, which is ignored by Git. Copy `API/.env.example` to `API/.env` and replace its placeholders:

```text
Gemini__ApiKey="<Gemini API key>"
SendGrid__ApiKey="<SendGrid API key>"
```

Do not put API keys or future Cloudinary credentials in `appsettings.json`. For deployment, use environment variables with the same names instead of uploading `.env`.

On startup, idempotent seed data is added only when absent: five diet types, four categories, six allergens, and the admin account.

## Run

```powershell
dotnet restore
dotnet build SWP391_VegetarianSupportSystem.sln
dotnet run --project API/API.csproj
```

Swagger in Development: `http://localhost:5000/swagger` or the launch-profile URL shown by `dotnet run`.

## Authentication

Use `POST /api/auth/login` then paste the returned token into Swagger's **Authorize** dialog.

Seeded administrator:

```text
Email: admin@vegetarian.local
Password: Admin@123
```

Passwords are PBKDF2-SHA256 hashes. API responses never expose `PasswordHash`.

## Main APIs

- Health: `GET /api/health`, `GET /api/health/db`
- Auth: `POST /api/auth/register`, `POST /api/auth/login`
- Reference data: `GET /api/diet-types`, `GET /api/categories`, `GET /api/allergens`, `GET /api/tags`
- Posts: `/api/posts`, comments, ratings and bookmarks
- Recipes: `POST /api/recipes`, `GET /api/recipes/{id}`, `GET /api/recipes/search`
- Restaurants: `/api/restaurants`, `/nearby`, `/search`, reviews
- Meal planner: `POST /api/meal-plans/generate`, `GET /api/meal-plans`
- Nutrition chatbot: `/api/chat`; Gemini Free Tier with local RAG from `KnowledgeBase/`; guest sessions use the `X-Guest-Chat-Token` header
- Ingredient recognition remains a mock implementation: `/api/ai/ingredient-recognition`
- Video recipe AI: `/api/video-recipe-drafts` uploads a cooking video, creates a Gemini-generated editable recipe draft, then publishes it as a recipe
- Moderation: `/api/reports/*`, `/api/admin/moderation/*`
- Admin: users, content moderation, categories, diet types, allergens, restaurants

## Verification

The API was verified against the existing SQL Server database with health checks, JWT login, Swagger, posts, comments, ratings, bookmarks, recipe search, restaurant search, moderation, guest chat, and a 28-meal plan generated for seven days.

## Enable Gemini

Add `Gemini__ApiKey` to `API/.env`, then run the API. The same key is used by the chatbot and video-recipe analysis.

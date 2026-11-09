using API.Infrastructure;
using API.Services;
using BLL.Common;
using BLL.Services;
using DAL.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using CloudinaryDotNet;


var builder = WebApplication.CreateBuilder(args);
DotEnvConfiguration.AddFile(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, ".env"));
DotEnvConfiguration.AddFile(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, ".aiven.env"));
const long maxUploadSize = 500L * 1024 * 1024; // 500 MB

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadSize;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadSize;
});
var connectionString = builder.Configuration.GetConnectionString("VegetarianSupportDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string is missing or empty. Set ConnectionStrings__VegetarianSupportDatabase " +
        "in API/.env, API/.aiven.env, or environment variables to a valid MySQL connection string.");
}
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32) throw new InvalidOperationException("JWT signing key is not configured or is too short.");

var mysqlVersion = Version.Parse(builder.Configuration["Database:MySqlVersion"] ?? "8.0.0");
builder.Services.AddDbContext<VegetarianDbContext>(options => options.UseMySql(
    connectionString, new MySqlServerVersion(mysqlVersion)));
builder.Services.AddVegetarianBusinessLogic();
builder.Services.AddSingleton<CommunityEventStream>();
builder.Services.AddSingleton<ICommunityEventPublisher>(provider => provider.GetRequiredService<CommunityEventStream>());
builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificationEventPublisher, SignalRNotificationPublisher>();
builder.Services.AddMemoryCache();
var cloudinarySettings = builder.Configuration.GetSection("Cloudinary").Get<CloudinarySettings>() ?? new CloudinarySettings();
builder.Services.AddSingleton(cloudinarySettings);
var cloudinaryAccount = new CloudinaryDotNet.Account(cloudinarySettings.CloudName, cloudinarySettings.ApiKey, cloudinarySettings.ApiSecret);
builder.Services.AddSingleton(new CloudinaryDotNet.Cloudinary(cloudinaryAccount));
builder.Services.AddScoped<IImageStorage, CloudinaryImageStorage>();
var geminiSettings = builder.Configuration.GetSection("Gemini").Get<GeminiChatbotSettings>() ?? new GeminiChatbotSettings();
builder.Services.AddSingleton(geminiSettings);
var sendGridSettings = builder.Configuration.GetSection("SendGrid").Get<SendGridSettings>() ?? new SendGridSettings();
builder.Services.AddSingleton(sendGridSettings);
builder.Services.AddHttpClient("Gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(geminiSettings.ChatTimeoutSeconds, 10, 120));
});
builder.Services.AddHttpClient<IModerationAiService, GeminiModerationAiService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddHttpClient<IAllergenNameValidator, GeminiAllergenNameValidator>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(40);
});
builder.Services.AddHttpClient<IAllergenIngredientMatcher, GeminiAllergenIngredientMatcher>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient("GeminiFiles", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(geminiSettings.MediaRequestTimeoutSeconds, 30, 300));
});
builder.Services.AddHttpClient("SendGrid", client => client.BaseAddress = new Uri("https://api.sendgrid.com/v3/"));
builder.Services.AddHttpClient("Photon", client =>
{
    client.BaseAddress = new Uri("https://photon.komoot.io/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHttpClient("Overpass", client =>
{
    client.BaseAddress = new Uri("https://overpass-api.de/");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VeggieMate-SWP391/1.0");
});
builder.Services.AddHttpClient("Nominatim", client =>
{
    client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VeggieMate-SWP391/1.0");
});
builder.Services.AddHttpClient("OSRM", client =>
{
    client.BaseAddress = new Uri("https://router.project-osrm.org/");
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("VeggieMate-SWP391/1.0");
});
builder.Services.AddScoped<IRestaurantRouteService, RestaurantRouteService>();
builder.Services.AddScoped<IOpenStreetMapService, OpenStreetMapService>();
builder.Services.AddScoped<IAiChatService>(provider => new GeminiChatbotService(provider.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini"), provider.GetRequiredService<GeminiChatbotSettings>(), provider.GetRequiredService<ILogger<GeminiChatbotService>>()));
builder.Services.AddSingleton(new VideoRecipeSettings
{
    StorageDirectory = Path.Combine(builder.Environment.ContentRootPath, ".local", "video-processing"),
    PollIntervalSeconds = Math.Clamp(builder.Configuration.GetValue("VideoRecipe:PollIntervalSeconds", 30), 10, 300)
});
builder.Services.AddSingleton<LocalVideoStorage>();
builder.Services.AddSingleton<IVideoRecipeAnalyzer>(provider => new GeminiVideoRecipeAnalyzer(provider.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiFiles"), provider.GetRequiredService<GeminiChatbotSettings>(), provider.GetRequiredService<ILogger<GeminiVideoRecipeAnalyzer>>()));
// Video analysis is an application feature, independent from database schema maintenance.
builder.Services.AddHostedService<VideoRecipeProcessingWorker>();
builder.Services.AddSingleton<IKnowledgeBaseService>(_ => new LocalKnowledgeBaseService(Path.Combine(AppContext.BaseDirectory, "KnowledgeBase")));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailSender>(provider => new SendGridEmailSender(
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("SendGrid"),
    provider.GetRequiredService<SendGridSettings>(),
    provider.GetRequiredService<ILogger<SendGridEmailSender>>()));
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
{
    var errors = context.ModelState
        .Where(item => item.Value?.Errors.Count > 0)
        .SelectMany(item => item.Value!.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? $"{item.Key} is invalid." : error.ErrorMessage))
        .ToArray();
    return new BadRequestObjectResult(new { success = false, message = "Validation failed.", errors });
});
builder.Services.AddCors(options => options.AddPolicy("DevelopmentFrontend", policy => policy
    .WithOrigins(
        "http://localhost:3000",
        "http://localhost:4200",
        "http://localhost:5173",
        "https://veggie-mate-system.vercel.app")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Negotiation uses the Authorization header; WebSocket upgrades use the query token.
            if (context.Request.Path.StartsWithSegments("/hubs/notifications") &&
                !string.IsNullOrEmpty(context.Request.Query["access_token"]))
                context.Token = context.Request.Query["access_token"];
            return Task.CompletedTask;
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
// Render terminates TLS at its load balancer and forwards requests to Kestrel
// over HTTP. Trust those forwarded headers so generated OpenAPI URLs retain
// the browser-facing HTTPS scheme and host.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    // The container is reachable through Render's proxy, whose IP addresses
    // are not static. Render does not expose Kestrel directly to the public.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "VeggieMate API",
        Version = "v1",
        Description = "VeggieMate backend API"
    });
    options.TagActionsBy(api =>
    {
        var controller = (api.ActionDescriptor as ControllerActionDescriptor)?.ControllerName;
        var path = api.RelativePath ?? string.Empty;
        var tag = controller switch
        {
            "Authentication" => "Authentication",
            "Health" => "System Health",
            "CurrentUser" => "My Profile",
            "Posts" or "Comments" => "Community",
            "Recipes" => "Recipes",
            "MealPlans" => "Meal Planner",
            "Chat" or "AiFeatures" or "VideoRecipeDrafts" => "AI Assistant",
            "Reports" => "Moderation",
            "Administration" => "Administration",
            "ReferenceData" when path.StartsWith("api/restaurants", StringComparison.OrdinalIgnoreCase) => "Restaurants",
            "Locations" => "Locations",
            "ReferenceData" => "Reference Data",
            _ => "Other"
        };
        return [tag];
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Enter a JWT bearer token." });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
});

var app = builder.Build();
// Must run before Swagger/redirect/authentication so ASP.NET Core sees the
// original public HTTPS URL instead of Render's internal HTTP hop.
app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
var swaggerEnabled = app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", true);
if (swaggerEnabled)
{
    app.UseSwagger(options =>
    {
        options.PreSerializeFilters.Add((document, request) =>
        {
            var scheme = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
            var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
            document.Servers = [new OpenApiServer { Url = $"{scheme}://{host}{request.PathBase}" }];
        });
    });
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        // A relative URL works with Render's HTTPS proxy and with local hosts.
        options.SwaggerEndpoint("./v1/swagger.json", "VeggieMate API v1");
        options.DocumentTitle = "VeggieMate API Documentation";
    });
}
// Render terminates HTTPS at its proxy. Redirect only for local/non-production hosting.
if (!app.Environment.IsProduction()) app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("DevelopmentFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

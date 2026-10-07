using System.Text;
using API.Infrastructure;
using API.Services;
using BLL.Services;
using DAL.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
DotEnvConfiguration.AddFile(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, ".env"));
const long maxUploadSize = 500L * 1024 * 1024; // 500 MB

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadSize;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadSize;
});
var connectionString = builder.Configuration.GetConnectionString("VegetarianSupportDatabase") ?? throw new InvalidOperationException("Database connection string is not configured.");
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32) throw new InvalidOperationException("JWT signing key is not configured or is too short.");

builder.Services.AddDbContext<VegetarianDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddVegetarianBusinessLogic();
var geminiSettings = builder.Configuration.GetSection("Gemini").Get<GeminiChatbotSettings>() ?? new GeminiChatbotSettings();
builder.Services.AddSingleton(geminiSettings);
var sendGridSettings = builder.Configuration.GetSection("SendGrid").Get<SendGridSettings>() ?? new SendGridSettings();
builder.Services.AddSingleton(sendGridSettings);
builder.Services.AddHttpClient("Gemini", client => client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/"));
builder.Services.AddHttpClient("GeminiFiles", client => client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"));
builder.Services.AddHttpClient("SendGrid", client => client.BaseAddress = new Uri("https://api.sendgrid.com/v3/"));
builder.Services.AddScoped<IAiChatService>(provider => new GeminiChatbotService(provider.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini"), provider.GetRequiredService<GeminiChatbotSettings>()));
builder.Services.AddSingleton(new VideoRecipeSettings { StorageDirectory = Path.Combine(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "uploads", "videos") });
builder.Services.AddSingleton<LocalVideoStorage>();
builder.Services.AddSingleton<IVideoRecipeAnalyzer>(provider => new GeminiVideoRecipeAnalyzer(provider.GetRequiredService<IHttpClientFactory>().CreateClient("GeminiFiles"), provider.GetRequiredService<GeminiChatbotSettings>()));
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
builder.Services.AddCors(options => options.AddPolicy("DevelopmentFrontend", policy => policy.WithOrigins("http://localhost:3000", "http://localhost:4200", "http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
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
builder.Services.AddSwaggerGen(options =>
{
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
            "ReferenceData" => "Reference Data",
            _ => "Other"
        };
        return [tag];
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Enter a JWT bearer token." });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("DevelopmentFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    await services.GetRequiredService<VegetarianDbContext>().Database.MigrateAsync();
    await services.GetRequiredService<ISeedService>().SeedAsync();
}

app.Run();

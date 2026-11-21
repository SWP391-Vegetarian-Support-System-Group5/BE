using DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace API.Infrastructure;

/// <summary>
/// Used only by EF CLI. It prevents a migration launched from the BE solution
/// directory from accidentally using an old localhost environment variable.
/// API/.aiven.env is intentionally loaded last for migration commands.
/// </summary>
public sealed class AivenDesignTimeDbContextFactory : IDesignTimeDbContextFactory<VegetarianDbContext>
{
    public VegetarianDbContext CreateDbContext(string[] args)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        var apiDirectory = File.Exists(Path.Combine(workingDirectory, "appsettings.json"))
            ? workingDirectory
            : Path.Combine(workingDirectory, "API");

        if (!File.Exists(Path.Combine(apiDirectory, "appsettings.json")))
            throw new InvalidOperationException("Could not locate API/appsettings.json for the EF migration command.");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables();
        DotEnvConfiguration.AddFile(configuration, Path.Combine(apiDirectory, ".env"));
        DotEnvConfiguration.AddFile(configuration, Path.Combine(apiDirectory, ".aiven.env"));

        var builtConfiguration = configuration.Build();
        var connectionString = builtConfiguration.GetConnectionString("VegetarianSupportDatabase");
        if (string.IsNullOrWhiteSpace(connectionString) || !connectionString.Contains("aivencloud.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EF migrations require the Aiven connection in API/.aiven.env; localhost is intentionally rejected.");

        var serverVersion = Version.Parse(builtConfiguration["Database:MySqlVersion"] ?? "8.0.0");
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(serverVersion))
            .Options;
        return new VegetarianDbContext(options);
    }
}

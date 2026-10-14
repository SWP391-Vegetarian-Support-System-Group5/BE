using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DAL.Data;

// Schema generation is offline; runtime credentials are read by the API.
public sealed class VegetarianDbContextFactory : IDesignTimeDbContextFactory<VegetarianDbContext>
{
    public VegetarianDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__VegetarianSupportDatabase")
            ?? "Server=localhost;Database=VegetarianSupportSystem;User ID=migration;Password=offline;";
        var version = Version.Parse(Environment.GetEnvironmentVariable("Database__MySqlVersion") ?? "8.0.0");
        var options = new DbContextOptionsBuilder<VegetarianDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(version))
            .Options;
        return new VegetarianDbContext(options);
    }
}

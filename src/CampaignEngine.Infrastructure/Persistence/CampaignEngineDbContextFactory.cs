using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CampaignEngine.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core CLI tools (dotnet ef migrations add, etc.)
/// when the startup project is the Infrastructure project itself.
/// This avoids requiring the Web project (ASP.NET Core) to be available at design time.
/// </summary>
public class CampaignEngineDbContextFactory : IDesignTimeDbContextFactory<CampaignEngineDbContext>
{
    public CampaignEngineDbContext CreateDbContext(string[] args)
    {
        // Read configuration from the Web project's appsettings or use a default
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(local);Database=CampaignEngine;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

        var optionsBuilder = new DbContextOptionsBuilder<CampaignEngineDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(CampaignEngineDbContext).Assembly.FullName);
        });

        return new CampaignEngineDbContext(optionsBuilder.Options);
    }
}

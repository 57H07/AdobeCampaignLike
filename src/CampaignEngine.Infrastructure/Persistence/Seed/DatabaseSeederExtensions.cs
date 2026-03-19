using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Extension methods to invoke the DatabaseSeeder from the application startup pipeline.
/// </summary>
public static class DatabaseSeederExtensions
{
    /// <summary>
    /// Applies pending migrations and seeds development data.
    /// Call this in Program.cs after app.Build() in Development environment only.
    /// </summary>
    /// <example>
    /// if (app.Environment.IsDevelopment())
    ///     await app.SeedDatabaseAsync();
    /// </example>
    public static async Task SeedDatabaseAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var seeder = services.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<DatabaseSeeder>>();
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}

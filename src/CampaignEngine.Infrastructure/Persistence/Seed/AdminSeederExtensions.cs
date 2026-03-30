using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Extension methods to invoke <see cref="AdminSeeder"/> from the application startup pipeline.
/// </summary>
public static class AdminSeederExtensions
{
    /// <summary>
    /// Seeds application roles and the default admin account if none exists.
    /// Idempotent — safe to call on every startup in all environments.
    /// </summary>
    /// <example>
    /// await app.SeedAdminAsync();
    /// </example>
    public static async Task SeedAdminAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        try
        {
            var seeder = services.GetRequiredService<AdminSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<AdminSeeder>>();
            logger.LogError(ex, "An error occurred while seeding the default admin user. The application will continue but may require manual admin setup.");
        }
    }
}

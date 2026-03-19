using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using CampaignEngine.Infrastructure.Logging;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Security;
using CampaignEngine.Infrastructure.Persistence.Seed;
using CampaignEngine.Infrastructure.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Infrastructure layer services into the DI container.
/// Called from the Web layer's Program.cs: services.AddInfrastructure(configuration)
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind strongly-typed configuration options
        services.Configure<CampaignEngineOptions>(
            configuration.GetSection(CampaignEngineOptions.SectionName));
        services.Configure<SmtpOptions>(
            configuration.GetSection(SmtpOptions.SectionName));

        // Register cross-cutting logging abstraction
        services.AddSingleton(typeof(IAppLogger<>), typeof(AppLogger<>));

        // ----------------------------------------------------------------
        // EF Core: SQL Server DbContext
        // ----------------------------------------------------------------
        services.AddDbContext<CampaignEngineDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                    sqlOptions.CommandTimeout(30);
                    sqlOptions.MigrationsAssembly(typeof(CampaignEngineDbContext).Assembly.FullName);
                });
        });

        // ----------------------------------------------------------------
        // Connection string encryption via ASP.NET Core Data Protection
        // Keys stored on local file system on Windows/IIS hosting.
        // ----------------------------------------------------------------
        services.AddDataProtection();
        services.AddScoped<IConnectionStringEncryptor, DataProtectionConnectionStringEncryptor>();

        // ----------------------------------------------------------------
        // Seed data service (Development environment only)
        // Call host.SeedDatabaseAsync() in Program.cs for Development
        // ----------------------------------------------------------------
        services.AddScoped<DatabaseSeeder>();

        // ----------------------------------------------------------------
        // Template renderer — Scriban implementation (stateless, thread-safe, sandboxed)
        // Registered as Singleton: stateless by design, expensive parse is per-call, not per-instance.
        // ----------------------------------------------------------------
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();

        // Channel dispatcher registry — resolves dispatchers by ChannelType via DI strategy pattern.
        // Register as scoped so it can aggregate scoped IChannelDispatcher instances.
        services.AddScoped<IChannelDispatcherRegistry, ChannelDispatcherRegistry>();

        // Channel dispatchers registered as IChannelDispatcher implementations.
        // Each dispatcher registers itself — the registry resolves by ChannelType at runtime.
        // To add a new channel: services.AddScoped<IChannelDispatcher, YourNewDispatcher>()
        // Example (to be enabled when dispatchers are implemented):
        // services.AddScoped<IChannelDispatcher, SmtpEmailDispatcher>();
        // services.AddScoped<IChannelDispatcher, SmsApiDispatcher>();
        // services.AddScoped<IChannelDispatcher, PdfLetterDispatcher>();

        // Data source connectors (strategy pattern)
        // Example:
        // services.AddScoped<IDataSourceConnector, SqlServerDataSourceConnector>();

        return services;
    }
}

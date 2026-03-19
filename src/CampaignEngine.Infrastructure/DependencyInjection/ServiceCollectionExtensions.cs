using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Logging;
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

        // Channel dispatchers registered as IChannelDispatcher implementations.
        // Each dispatcher registers itself — DI resolves by ChannelType at runtime.
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

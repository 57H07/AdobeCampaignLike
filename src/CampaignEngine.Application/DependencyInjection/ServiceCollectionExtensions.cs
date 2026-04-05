using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Mappings;
using CampaignEngine.Application.Services;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace CampaignEngine.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering Application layer services into the DI container.
/// Called from the Web layer's Program.cs: services.AddApplication()
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // ----------------------------------------------------------------
        // Mapster — centralized mapping configuration
        // Configures TypeAdapterConfig.GlobalSettings so that entity.Adapt<TDto>()
        // works throughout the application without injecting IMapper.
        // ----------------------------------------------------------------
        MappingConfig.ConfigureGlobalSettings();
        services.AddSingleton(TypeAdapterConfig.GlobalSettings);

        // ----------------------------------------------------------------
        // Single send — request validation (stateless, no infrastructure deps)
        // ----------------------------------------------------------------
        services.AddScoped<ISendRequestValidator, SendRequestValidator>();

        return services;
    }
}

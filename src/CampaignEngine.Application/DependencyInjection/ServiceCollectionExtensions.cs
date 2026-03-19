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
        // Register application services here as they are implemented.
        // Example: services.AddScoped<ITemplateService, TemplateService>();

        return services;
    }
}

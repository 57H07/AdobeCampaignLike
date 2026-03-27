using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Mappings;
using CampaignEngine.Application.Services;
using CampaignEngine.Domain.Enums;
using Mapster;
using Microsoft.AspNetCore.Authorization;
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

        // ----------------------------------------------------------------
        // Authorization policies — role-based access control
        // Business rules:
        //   Designer: template CRUD + preview, no campaign access
        //   Operator: campaign CRUD + monitoring, read-only template access
        //   Admin: full access + user management + configuration
        // ----------------------------------------------------------------
        // AddAuthorizationCore is available in non-web class libraries.
        // The Web layer calls AddAuthorization (which includes AddAuthorizationCore)
        // and these policies are registered via the Application layer service registration.
        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(AuthorizationPolicies.RequireAdmin, policy =>
                policy.RequireRole(UserRole.Admin));

            options.AddPolicy(AuthorizationPolicies.RequireDesigner, policy =>
                policy.RequireRole(UserRole.Designer));

            options.AddPolicy(AuthorizationPolicies.RequireOperator, policy =>
                policy.RequireRole(UserRole.Operator));

            options.AddPolicy(AuthorizationPolicies.RequireDesignerOrAdmin, policy =>
                policy.RequireRole(UserRole.Designer, UserRole.Admin));

            options.AddPolicy(AuthorizationPolicies.RequireOperatorOrAdmin, policy =>
                policy.RequireRole(UserRole.Operator, UserRole.Admin));

            options.AddPolicy(AuthorizationPolicies.RequireAuthenticated, policy =>
                policy.RequireAuthenticatedUser());

            // Default fallback: all pages/endpoints require authentication
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}

using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;
using System.Reflection;

namespace CampaignEngine.Web.OpenApi;

/// <summary>
/// Extension methods for registering and configuring OpenAPI / Swagger services.
/// </summary>
public static class SwaggerServiceExtensions
{
    private const string ApiKeySecuritySchemeName = "ApiKey";

    /// <summary>
    /// Registers Swashbuckle services with full OpenAPI 3.0 configuration:
    /// XML documentation, API key security scheme, and request/response examples.
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            // ----------------------------------------------------------------
            // OpenAPI 3.0 document metadata
            // ----------------------------------------------------------------
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "CampaignEngine API",
                Version = "v1",
                Description = """
                    Multi-channel marketing campaign engine — Email, SMS, Letter.

                    ## Authentication

                    All endpoints (except health checks) require authentication via the
                    `X-Api-Key` header. Obtain an API key from an Administrator using the
                    `/api/apikeys` endpoint (Admin role required).

                    ### API Key format
                    ```
                    X-Api-Key: ce_<your-key-value>
                    ```

                    ## Rate Limiting

                    Each API key is subject to a per-minute rate limit (default: 1000 req/min).
                    Exceeding the limit returns HTTP 429 with a `Retry-After` header.

                    ## Error responses

                    | Status | Meaning                                  |
                    |--------|------------------------------------------|
                    | 400    | Validation error — see `errors` field     |
                    | 401    | Missing or invalid API key                |
                    | 403    | Authenticated but insufficient role       |
                    | 404    | Requested resource not found              |
                    | 422    | Domain rule violation                     |
                    | 429    | Rate limit exceeded                       |
                    | 500    | Internal server error                     |
                    """,
                Contact = new OpenApiContact
                {
                    Name = "CampaignEngine Team",
                    Email = "admin@campaignengine.local"
                }
            });

            // ----------------------------------------------------------------
            // Security scheme: X-Api-Key header
            // ----------------------------------------------------------------
            options.AddSecurityDefinition(ApiKeySecuritySchemeName, new OpenApiSecurityScheme
            {
                Name = "X-Api-Key",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API key issued by an Administrator. Pass the full key value (including the `ce_` prefix). Example: `ce_abcdef1234567890`."
            });

            // Apply the API key requirement to all operations by default.
            // Individual [AllowAnonymous] endpoints will still show the lock icon
            // but authentication is not enforced by Swagger UI.
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = ApiKeySecuritySchemeName
                        }
                    },
                    []
                }
            });

            // ----------------------------------------------------------------
            // XML documentation — include comments from Web and Application assemblies
            // ----------------------------------------------------------------
            var webXmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var webXmlPath = Path.Combine(AppContext.BaseDirectory, webXmlFile);
            if (File.Exists(webXmlPath))
                options.IncludeXmlComments(webXmlPath, includeControllerXmlComments: true);

            var appAssemblyName = typeof(CampaignEngine.Application.DTOs.Send.SendRequest).Assembly.GetName().Name;
            var appXmlPath = Path.Combine(AppContext.BaseDirectory, $"{appAssemblyName}.xml");
            if (File.Exists(appXmlPath))
                options.IncludeXmlComments(appXmlPath);

            // ----------------------------------------------------------------
            // Swashbuckle.AspNetCore.Filters — example request/response bodies
            // ----------------------------------------------------------------
            options.ExampleFilters();

            // ----------------------------------------------------------------
            // Order endpoints by controller then HTTP method for readability
            // ----------------------------------------------------------------
            options.OrderActionsBy(api => $"{api.ActionDescriptor.RouteValues["controller"]}_{api.HttpMethod}");

            // Custom schema filter to apply [SwaggerSchema(Example = ...)] on enums
            options.UseInlineDefinitionsForEnums();

            // Flatten polymorphic schema refs to avoid $ref indirection in simple cases
            options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
        });

        // Register example classes discovered in the Web assembly
        services.AddSwaggerExamplesFromAssemblyOf<Program>();

        return services;
    }

    /// <summary>
    /// Configures the Swagger middleware pipeline.
    /// Swagger UI is available in Development and Staging environments only (business rule BR-1).
    /// The raw OpenAPI JSON spec is always served (needed for tooling integration).
    /// </summary>
    public static WebApplication UseSwaggerDocumentation(this WebApplication app)
    {
        // Serve the raw OpenAPI JSON spec at /swagger/v1/swagger.json in all environments
        // so CI/CD tooling can validate it. Access to the UI is environment-gated.
        app.UseSwagger(options =>
        {
            options.RouteTemplate = "swagger/{documentName}/swagger.json";
        });

        if (!app.Environment.IsProduction())
        {
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "CampaignEngine API v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "CampaignEngine API Documentation";

                // ----------------------------------------------------------------
                // UI customisation
                // ----------------------------------------------------------------
                options.DefaultModelsExpandDepth(2);
                options.DefaultModelExpandDepth(3);
                options.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Model);
                options.DisplayRequestDuration();
                options.EnableFilter();
                options.EnableDeepLinking();
                options.ShowExtensions();

                // Persist authorisation between page refreshes (convenient for dev)
                options.ConfigObject.AdditionalItems["persistAuthorization"] = true;

                // Custom CSS for a cleaner dark-accented header
                options.InjectStylesheet("/swagger-ui/custom.css");
            });
        }

        return app;
    }
}

using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.ApiKeys;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.DataSources;
using Microsoft.Extensions.Options;
using CampaignEngine.Infrastructure.Dispatch;
using CampaignEngine.Infrastructure.Identity;
using CampaignEngine.Infrastructure.Logging;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Security;
using CampaignEngine.Infrastructure.Persistence.Seed;
using CampaignEngine.Infrastructure.Rendering;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;
using CampaignEngine.Infrastructure.Templates;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.Configure<SmsOptions>(
            configuration.GetSection(SmsOptions.SectionName));

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

        // ----------------------------------------------------------------
        // Channel post-processors (US-013)
        // Strategy pattern: each channel registers its IChannelPostProcessor implementation.
        // The registry resolves the correct processor at runtime based on ChannelType.
        //
        // DinkToPdf (Letter channel):
        //   Requires libwkhtmltox.dll (x64) in the application root on Windows.
        //   Download: https://wkhtmltopdf.org/downloads.html
        //   The SynchronizedConverter is Singleton (thread-safe via internal lock).
        // ----------------------------------------------------------------
        services.Configure<LetterPostProcessorOptions>(
            configuration.GetSection(LetterPostProcessorOptions.SectionName));

        // DinkToPdf converter: Singleton — SynchronizedConverter serializes native calls.
        services.AddSingleton<IConverter>(new SynchronizedConverter(new PdfTools()));

        // Post-processor implementations (Scoped — safe to use scoped logger)
        services.AddScoped<IChannelPostProcessor, EmailPostProcessor>();
        services.AddScoped<IChannelPostProcessor, LetterPostProcessor>();
        services.AddScoped<IChannelPostProcessor, SmsPostProcessor>();

        // Registry resolves IChannelPostProcessor by ChannelType at runtime.
        services.AddScoped<IChannelPostProcessorRegistry, ChannelPostProcessorRegistry>();

        // PDF consolidation (PdfSharp — pure .NET, no native deps)
        services.AddScoped<IPdfConsolidationService, PdfConsolidationService>();

        // Channel dispatcher registry — resolves dispatchers by ChannelType via DI strategy pattern.
        // Register as scoped so it can aggregate scoped IChannelDispatcher instances.
        services.AddScoped<IChannelDispatcherRegistry, ChannelDispatcherRegistry>();

        // Send log service — SEND_LOG is the source of truth for all dispatch activity.
        services.AddScoped<ISendLogService, SendLogService>();

        // Template service — CRUD operations with soft delete and unique-name enforcement.
        services.AddScoped<ITemplateService, TemplateService>();

        // Placeholder manifest service — manages typed placeholder declarations per template.
        services.AddScoped<IPlaceholderManifestService, PlaceholderManifestService>();

        // Placeholder parser service — extracts placeholder keys from template HTML and validates completeness.
        services.AddSingleton<IPlaceholderParserService, PlaceholderParserService>();

        // Sub-template resolver service — resolves {{> name}} references recursively with circular detection.
        services.AddScoped<ISubTemplateResolverService, SubTemplateResolverService>();

        // Template preview service — renders a template with sample data for designer workflow (US-010).
        services.AddScoped<ITemplatePreviewService, TemplatePreviewService>();

        // Logging dispatch orchestrator — wraps dispatchers with before/after SEND_LOG recording.
        services.AddScoped<ILoggingDispatchOrchestrator, LoggingDispatchOrchestrator>();

        // Channel dispatchers registered as IChannelDispatcher implementations.
        // Each dispatcher registers itself — the registry resolves by ChannelType at runtime.
        // To add a new channel: services.AddScoped<IChannelDispatcher, YourNewDispatcher>()
        //
        // US-019: EmailDispatcher using MailKit — handles ChannelType.Email sends via SMTP.
        services.AddScoped<IChannelDispatcher, EmailDispatcher>();

        // US-020: SmsDispatcher — handles ChannelType.Sms sends via configurable HTTP provider.
        // SmsProviderClient is registered separately (wraps IHttpClientFactory) so it can be
        // overridden in tests by subclassing.
        services.AddHttpClient("SmsProvider");
        services.AddScoped<SmsProviderClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SmsOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<SmsProviderClient>>();
            return new SmsProviderClient(opts, factory, logger);
        });
        services.AddScoped<IChannelDispatcher, SmsDispatcher>();

        // US-021: LetterDispatcher — handles ChannelType.Letter sends via PDF generation + file drop.
        // PrintProviderFileDropHandler is registered separately so it can be overridden in tests.
        services.Configure<LetterOptions>(
            configuration.GetSection(LetterOptions.SectionName));
        services.AddScoped<PrintProviderFileDropHandler>();
        services.AddScoped<IChannelDispatcher, LetterDispatcher>();

        // ----------------------------------------------------------------
        // Data source management — declaration, schema, and connection testing
        // ----------------------------------------------------------------
        // IHttpClientFactory is used by ConnectionTestService for REST API tests.
        // AddHttpClient() is idempotent and already called in Program.cs via AddControllers/Razor,
        // but registering it here makes the Infrastructure layer self-contained.
        services.AddHttpClient("ConnectionTest");
        services.AddScoped<IConnectionTestService, ConnectionTestService>();
        services.AddScoped<IDataSourceService, DataSourceService>();

        // ----------------------------------------------------------------
        // Data source connectors (strategy pattern keyed by DataSourceType)
        // SqlServerConnector: Dapper-based, parameterized SQL, read-only.
        // Registered as Scoped — opens/closes SqlConnection per call (ADO.NET pools handles reuse).
        // ----------------------------------------------------------------
        services.Configure<SqlServerConnectorOptions>(
            configuration.GetSection(SqlServerConnectorOptions.SectionName));

        services.AddScoped<SqlServerConnector>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SqlServerConnectorOptions>>().Value;
            var logger = sp.GetRequiredService<IAppLogger<SqlServerConnector>>();
            return new SqlServerConnector(opts, logger);
        });

        // Register as IDataSourceConnector for consumers that inject by interface.
        // When multiple connector implementations exist, resolve by DataSourceType via a registry.
        services.AddScoped<IDataSourceConnector, SqlServerConnector>(sp =>
            sp.GetRequiredService<SqlServerConnector>());

        // ----------------------------------------------------------------
        // ASP.NET Core Identity: user and role management
        // Uses ApplicationUser / ApplicationRole backed by SQL Server via EF Core.
        // Cookie lifetime and password policy configurable per environment.
        // ----------------------------------------------------------------
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                // Password policy
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // Lockout policy: 5 failed attempts → locked 15 minutes
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // User settings
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<CampaignEngineDbContext>()
            .AddDefaultTokenProviders();

        // ----------------------------------------------------------------
        // Identity services
        // ----------------------------------------------------------------
        // Resolves the current authenticated user from the HTTP context.
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Audit trail for authentication events.
        services.AddScoped<IAuthAuditService, AuthAuditService>();

        // ----------------------------------------------------------------
        // API key management and authentication (US-031)
        // ApiKeyService handles key generation (BCrypt), validation, revocation, rotation.
        // Registered as Scoped — requires DbContext (scoped).
        // ----------------------------------------------------------------
        services.Configure<ApiKeyOptions>(
            configuration.GetSection(ApiKeyOptions.SectionName));
        services.AddScoped<IApiKeyService, ApiKeyService>();

        return services;
    }
}

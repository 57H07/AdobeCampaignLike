using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Infrastructure.ApiKeys;
using CampaignEngine.Infrastructure.Attachments;
using CampaignEngine.Infrastructure.Batch;
using CampaignEngine.Infrastructure.Campaigns;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Storage;
using CampaignEngine.Infrastructure.DataSources;
using Microsoft.Extensions.Options;
using CampaignEngine.Infrastructure.Dispatch;
using CampaignEngine.Infrastructure.Identity;
using CampaignEngine.Infrastructure.Logging;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Persistence.Security;
using CampaignEngine.Infrastructure.Persistence.Seed;
using CampaignEngine.Infrastructure.Rendering;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;
using CampaignEngine.Infrastructure.Send;
using CampaignEngine.Infrastructure.Startup;
using CampaignEngine.Infrastructure.Templates;
using Hangfire;
using Hangfire.SqlServer;
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
        // Unit of Work + Repository Pattern
        // All scoped to match DbContext lifetime.
        // ----------------------------------------------------------------
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IDataSourceRepository, DataSourceRepository>();
        services.AddScoped<ISendLogRepository, SendLogRepository>();
        services.AddScoped<ICampaignStatusHistoryRepository, CampaignStatusHistoryRepository>();
        services.AddScoped<IPlaceholderManifestRepository, PlaceholderManifestRepository>();
        services.AddScoped<IAuthAuditLogRepository, AuthAuditLogRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<ICampaignChunkRepository, CampaignChunkRepository>();

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
        // Default admin seeder (all environments, US-026)
        // Registers roles and creates the default admin account on first startup.
        // Call host.SeedAdminAsync() in Program.cs after migrations.
        // Credentials configured via DefaultAdmin section in appsettings.json.
        // ----------------------------------------------------------------
        services.Configure<DefaultAdminOptions>(
            configuration.GetSection(DefaultAdminOptions.SectionName));
        services.AddScoped<AdminSeeder>();

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
        // Note: LetterPostProcessor (DinkToPdf / wkhtmltopdf) removed in US-023 —
        // Letter channel now uses pre-rendered DOCX bytes (BinaryContent) without
        // any HTML-to-PDF conversion step.
        // ----------------------------------------------------------------

        // Post-processor implementations (Scoped — safe to use scoped logger)
        services.AddScoped<IChannelPostProcessor, EmailPostProcessor>();
        services.AddScoped<IChannelPostProcessor, SmsPostProcessor>();

        // Registry resolves IChannelPostProcessor by ChannelType at runtime.
        services.AddScoped<IChannelPostProcessorRegistry, ChannelPostProcessorRegistry>();

        // Channel dispatcher registry — resolves dispatchers by ChannelType via DI strategy pattern.
        // Register as scoped so it can aggregate scoped IChannelDispatcher instances.
        services.AddScoped<IChannelDispatcherRegistry, ChannelDispatcherRegistry>();

        // Send log service — SEND_LOG is the source of truth for all dispatch activity.
        services.AddScoped<ISendLogService, SendLogService>();

        // Single send service — orchestrates template resolution, rendering, and dispatch (US-007 TASK-007-03).
        services.AddScoped<ISingleSendService, SingleSendService>();

        // ----------------------------------------------------------------
        // US-029: CC/BCC resolution services
        // EmailValidationService: validates individual email addresses using MimeKit.
        // CcResolutionService: combines static + dynamic CC, validates, deduplicates, caps at 10.
        // Both are Scoped (use scoped logger).
        // ----------------------------------------------------------------
        services.AddScoped<IEmailValidationService, EmailValidationService>();
        services.AddScoped<ICcResolutionService, CcResolutionService>();

        // Template service — CRUD operations with soft delete and unique-name enforcement.
        services.AddScoped<ITemplateService, TemplateService>();

        // Placeholder manifest service — manages typed placeholder declarations per template.
        services.AddScoped<IPlaceholderManifestService, PlaceholderManifestService>();

        // Placeholder parser service — extracts placeholder keys from template HTML and validates completeness.
        services.AddSingleton<IPlaceholderParserService, PlaceholderParserService>();

        // US-018: DOCX placeholder parser service — extracts keys from DOCX streams and computes
        // undeclared-key warnings at upload time. DocxPlaceholderParser is stateless (compiled regexes).
        services.AddSingleton<DocxPlaceholderParser>();
        services.AddSingleton<IDocxPlaceholderParserService, DocxPlaceholderParserService>();

        // US-020: DOCX rendering pipeline components — all stateless, registered as Singleton.
        // DocxRunMerger, DocxPlaceholderReplacer, DocxTableCollectionRenderer,
        // DocxConditionalBlockRenderer are concrete helpers with no external state.
        // DocxTemplateRenderer orchestrates all four steps (F-301 through F-304).
        services.AddSingleton<DocxRunMerger>();
        services.AddSingleton<DocxPlaceholderReplacer>();
        services.AddSingleton<DocxTableCollectionRenderer>();
        services.AddSingleton<DocxConditionalBlockRenderer>();
        services.AddSingleton<IDocxTemplateRenderer, DocxTemplateRenderer>();

        // Sub-template resolver service — resolves {{> name}} references recursively with circular detection.
        services.AddScoped<ISubTemplateResolverService, SubTemplateResolverService>();

        // Template preview service — renders a template with sample data for designer workflow (US-010).
        services.AddScoped<ITemplatePreviewService, TemplatePreviewService>();

        // Retry policy — exponential backoff (30s/2min/10min, max 3 attempts) for transient dispatch failures.
        // Configured via CampaignEngine:BatchProcessing:MaxRetryAttempts and RetryDelaysSeconds.
        // Registered as Singleton: stateless, configuration-driven.
        services.AddSingleton<IRetryPolicy, RetryPolicy>();

        // Transient failure detector — classifies exceptions as transient or permanent.
        // Centralizes SMTP 4xx/network and SMS 429/5xx detection logic.
        services.AddSingleton<ITransientFailureDetector, TransientFailureDetector>();

        // Logging dispatch orchestrator — wraps dispatchers with before/after SEND_LOG recording
        // and automatic retry via IRetryPolicy.
        services.AddScoped<ILoggingDispatchOrchestrator, LoggingDispatchOrchestrator>();

        // ----------------------------------------------------------------
        // US-022: Channel rate limiting (token bucket)
        // RateLimitOptions configures per-channel message-per-second limits.
        // ChannelRateLimiterRegistry is Singleton: the token bucket state must
        // persist across requests to correctly enforce sustained rate limits.
        // RateLimitMetricsService is Singleton: thread-safe Interlocked counters.
        // ----------------------------------------------------------------
        services.Configure<RateLimitOptions>(
            configuration.GetSection(RateLimitOptions.SectionName));
        services.AddSingleton<IChannelRateLimiterRegistry, ChannelRateLimiterRegistry>();
        services.AddSingleton<IRateLimitMetricsService, RateLimitMetricsService>();

        // Channel dispatchers registered as IChannelDispatcher implementations.
        // Each concrete dispatcher is registered under its own type AND wrapped with
        // ThrottledChannelDispatcher for rate limiting before being exposed as IChannelDispatcher.
        // The registry collects all IChannelDispatcher entries via IEnumerable<IChannelDispatcher>.
        //
        // US-019: EmailDispatcher using MailKit — handles ChannelType.Email sends via SMTP.
        services.AddScoped<EmailDispatcher>();
        services.AddScoped<IChannelDispatcher>(sp => new ThrottledChannelDispatcher(
            sp.GetRequiredService<EmailDispatcher>(),
            sp.GetRequiredService<IChannelRateLimiterRegistry>(),
            sp.GetRequiredService<IRateLimitMetricsService>(),
            sp.GetRequiredService<IAppLogger<ThrottledChannelDispatcher>>()));

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
        services.AddScoped<SmsDispatcher>();
        services.AddScoped<IChannelDispatcher>(sp => new ThrottledChannelDispatcher(
            sp.GetRequiredService<SmsDispatcher>(),
            sp.GetRequiredService<IChannelRateLimiterRegistry>(),
            sp.GetRequiredService<IRateLimitMetricsService>(),
            sp.GetRequiredService<IAppLogger<ThrottledChannelDispatcher>>()));

        // US-023: LetterDispatcher (rewritten) — writes one DOCX per recipient via PrintProviderFileDropHandler.
        // Accepts DispatchRequest.BinaryContent (pre-rendered DOCX bytes) — no PDF conversion.
        // PrintProviderFileDropHandler is registered separately so it can be overridden in tests.
        // Letter channel is unlimited (TokensPerSecond = 0) per BR-1, so ThrottledChannelDispatcher
        // delegates to NoOpRateLimiter and adds zero overhead.
        services.Configure<LetterOptions>(
            configuration.GetSection(LetterOptions.SectionName));
        services.AddScoped<PrintProviderFileDropHandler>();
        services.AddScoped<LetterDispatcher>();
        services.AddScoped<IChannelDispatcher>(sp => new ThrottledChannelDispatcher(
            sp.GetRequiredService<LetterDispatcher>(),
            sp.GetRequiredService<IChannelRateLimiterRegistry>(),
            sp.GetRequiredService<IRateLimitMetricsService>(),
            sp.GetRequiredService<IAppLogger<ThrottledChannelDispatcher>>()));

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
        // RestApiConnector: HttpClient-based, JSON parsing, auth + pagination.
        // Resolved at runtime by DataSourceConnectorRegistry keyed on DataSourceType.
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

        // RestApiConnector: HTTP-based connector for REST API data sources (US-017).
        // OAuth2TokenCache is Singleton: token lifetime outlasts scoped connector instances.
        services.Configure<RestApiConnectorOptions>(
            configuration.GetSection(RestApiConnectorOptions.SectionName));

        services.AddHttpClient("RestApiConnector");

        services.AddSingleton<RestApiOAuth2TokenCache>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new RestApiOAuth2TokenCache(factory);
        });

        services.AddScoped<RestApiConnector>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RestApiConnectorOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var oauth2Cache = sp.GetRequiredService<RestApiOAuth2TokenCache>();
            var logger = sp.GetRequiredService<IAppLogger<RestApiConnector>>();
            return new RestApiConnector(opts, factory, oauth2Cache, logger);
        });

        // Connector registry: resolves IDataSourceConnector by DataSourceType at runtime.
        services.AddScoped<IDataSourceConnectorRegistry, DataSourceConnectorRegistry>();

        // ----------------------------------------------------------------
        // Filter expression services (US-016)
        // FilterAstTranslator: translates AST to parameterized SQL WHERE clause.
        // FilterExpressionValidator: validates AST before translation (early 400 errors).
        // DataSourcePreviewService: executes filtered preview queries (max 100 rows).
        // ----------------------------------------------------------------
        services.AddScoped<IFilterAstTranslator, FilterAstTranslator>();
        services.AddScoped<IFilterExpressionValidator, FilterExpressionValidator>();
        services.AddScoped<IDataSourcePreviewService, DataSourcePreviewService>();

        // ----------------------------------------------------------------
        // US-002: Template body file-system store
        // Singleton: stateless, wraps pure file-system calls.
        // ITemplateBodyStore is consumed by Application services via the interface.
        // ----------------------------------------------------------------
        services.Configure<TemplateBodyStoreOptions>(
            configuration.GetSection(TemplateBodyStoreOptions.SectionName));
        services.AddSingleton<ITemplateBodyStore, FileSystemTemplateBodyStore>();

        // ----------------------------------------------------------------
        // US-004: Template storage root startup validation
        // TemplateStorageOptions: bound from "TemplateStorage" section in appsettings.json.
        // TemplateStorageStartupValidator: IHostedService that validates RootPath before
        // the HTTP server accepts any requests (BR-1 + BR-2).
        // ----------------------------------------------------------------
        services.Configure<TemplateStorageOptions>(
            configuration.GetSection(TemplateStorageOptions.SectionName));
        services.AddHostedService<TemplateStorageStartupValidator>();

        // ----------------------------------------------------------------
        // Attachment management (US-028)
        // AttachmentStorageOptions: configures UNC/local file share base path and limits.
        // AttachmentValidationService: extension whitelist + size limit enforcement.
        // FileUploadService: writes files to the configured file share path.
        // DynamicAttachmentResolver: resolves per-recipient file paths at send time.
        // AttachmentService: orchestrates upload, validation, and DB persistence.
        // AttachmentRepository: EF Core queries for CampaignAttachment entities.
        // ----------------------------------------------------------------
        services.Configure<AttachmentStorageOptions>(
            configuration.GetSection(AttachmentStorageOptions.SectionName));
        services.AddScoped<IAttachmentValidationService, AttachmentValidationService>();
        services.AddScoped<IFileUploadService, FileUploadService>();
        services.AddScoped<IDynamicAttachmentResolver, DynamicAttachmentResolver>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();

        // ----------------------------------------------------------------
        // Campaign management (US-023, US-024, US-027)
        // CampaignService: CRUD, validation, draft creation.
        // RecipientCountService: pre-execution recipient count estimation.
        // CampaignStepValidationService: multi-step business rule enforcement (max 10, order uniqueness).
        // CampaignStepSchedulingService: calculates per-step execution dates from delays.
        // TemplateSnapshotService: freezes template content at scheduling time (US-025).
        // CampaignStatusService: validates status transitions, persists history (US-027).
        // ----------------------------------------------------------------
        services.AddScoped<ICampaignService, CampaignService>();
        services.AddScoped<ICampaignStatusService, CampaignStatusService>();
        services.AddScoped<ICampaignDashboardService, CampaignDashboardService>();
        services.AddScoped<IRecipientCountService, RecipientCountService>();
        services.AddScoped<ICampaignStepValidationService, CampaignStepValidationService>();
        services.AddSingleton<ICampaignStepSchedulingService, CampaignStepSchedulingService>();
        services.AddScoped<ITemplateSnapshotService, TemplateSnapshotService>();

        // ----------------------------------------------------------------
        // Batch processing (US-026): Chunk Coordinator pattern
        // RecipientChunkingService: splits recipients into fixed-size chunks.
        // ChunkCoordinatorService: enqueues Hangfire jobs and tracks completion atomically.
        // CampaignCompletionService: transitions campaign to final status after all chunks done.
        // ProcessChunkJob: Hangfire background job for each chunk.
        // ----------------------------------------------------------------
        services.AddScoped<IRecipientChunkingService, RecipientChunkingService>();
        services.AddScoped<ICampaignCompletionService, CampaignCompletionService>();
        services.AddScoped<IChunkCoordinatorService, ChunkCoordinatorService>();
        services.AddScoped<IProcessChunkJob, ProcessChunkJob>();

        // ----------------------------------------------------------------
        // Hangfire: SQL Server storage + worker configuration (US-026)
        // WorkerCount configurable via "Hangfire:WorkerCount" (default: 8).
        // Dashboard restricted to Admin role — configured in Program.cs.
        // ----------------------------------------------------------------
        services.Configure<HangfireOptions>(
            configuration.GetSection(HangfireOptions.SectionName));

        var hangfireConnectionString = configuration.GetConnectionString("DefaultConnection");
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(hangfireConnectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true
            }));

        var workerCount = configuration.GetValue("Hangfire:WorkerCount", 8);
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = workerCount;
            options.ServerName = $"CampaignEngine-{Environment.MachineName}";
            options.Queues = ["default"];
        });

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

        // Identity service — abstracts UserManager/SignInManager for the Web layer.
        services.AddScoped<IIdentityService, IdentityService>();

        // ----------------------------------------------------------------
        // API key management and authentication (US-031)
        // ApiKeyService handles key generation (BCrypt), validation, revocation, rotation.
        // Registered as Scoped — requires DbContext (scoped).
        // ----------------------------------------------------------------
        services.Configure<ApiKeyOptions>(
            configuration.GetSection(ApiKeyOptions.SectionName));
        services.AddScoped<IApiKeyService, ApiKeyService>();

        // ----------------------------------------------------------------
        // API key rate limiting (US-033)
        // ApiKeyRateLimiter: in-memory sliding-window per-key rate limiter.
        // Registered as Singleton: rate-limit state (timestamps queue per key) must
        // persist across requests. The implementation is thread-safe (per-key locking).
        // ----------------------------------------------------------------
        services.AddSingleton<IApiKeyRateLimiter, ApiKeyRateLimiter>();

        return services;
    }
}

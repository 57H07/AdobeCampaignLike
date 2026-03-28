using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Infrastructure.DependencyInjection;
using CampaignEngine.Web.Middleware;
using CampaignEngine.Web.OpenApi;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Serilog;
using Serilog.Events;

// ----------------------------------------------------------------
// Bootstrap Serilog early so startup errors are captured.
// Full configuration is read from appsettings.json via UseSerilog().
// ----------------------------------------------------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting CampaignEngine web host");

    var builder = WebApplication.CreateBuilder(args);

    // ----------------------------------------------------------------
    // Serilog: replace default logging with structured Serilog pipeline
    // Full sink configuration driven from appsettings.json "Serilog" section
    // ----------------------------------------------------------------
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName());

    // ----------------------------------------------------------------
    // DI: Application and Infrastructure layers
    // ----------------------------------------------------------------
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // ----------------------------------------------------------------
    // ASP.NET Core: Razor Pages + Web API
    // Configure cookie-based authentication paths for Identity
    // ----------------------------------------------------------------
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

    builder.Services.AddRazorPages();
    builder.Services.AddControllers();

    // ----------------------------------------------------------------
    // OpenAPI / Swagger — full configuration via SwaggerServiceExtensions
    // ----------------------------------------------------------------
    builder.Services.AddSwaggerDocumentation();

    // ----------------------------------------------------------------
    // HTTP context accessor (needed by correlation ID middleware)
    // ----------------------------------------------------------------
    builder.Services.AddHttpContextAccessor();

    var app = builder.Build();

    // ----------------------------------------------------------------
    // Middleware pipeline
    // ----------------------------------------------------------------
    // ----------------------------------------------------------------
    // OpenAPI / Swagger UI — accessible in non-production only (business rule BR-1)
    // ----------------------------------------------------------------
    app.UseSwaggerDocumentation();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();

    // Serilog request logging — enriches each request with structured properties
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
            diagnosticContext.Set("CorrelationId",
                httpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
                ?? httpContext.TraceIdentifier);
        };
    });

    app.UseRouting();

    // Correlation ID middleware — must run before authorization to enrich all subsequent logs
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<GlobalExceptionMiddleware>();

    app.UseAuthentication();

    // API key authentication middleware — must run after UseAuthentication so it can
    // complement (not replace) cookie-based Identity authentication for UI routes.
    // If X-Api-Key header is present it validates the key and synthesises a ClaimsPrincipal.
    app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

    // API key rate limiting — must run after ApiKeyAuthenticationMiddleware so
    // ApiKeyId and ApiKeyRateLimitPerMinute context items are already populated.
    // Only requests authenticated via API key are subject to per-key rate limits.
    app.UseMiddleware<ApiKeyRateLimitingMiddleware>();

    app.UseAuthorization();

    app.MapRazorPages();
    app.MapControllers();

    // ----------------------------------------------------------------
    // Hangfire dashboard (US-026): accessible to Admin role only.
    // Dashboard path configured via "Hangfire:DashboardPath" (default: /hangfire).
    // In Development, allows all authenticated users for convenience.
    // ----------------------------------------------------------------
    var hangfirePath = app.Configuration.GetValue("Hangfire:DashboardPath", "/hangfire")!;
    app.UseHangfireDashboard(hangfirePath, new DashboardOptions
    {
        Authorization =
        [
            new HangfireAdminAuthorizationFilter()
        ],
        DashboardTitle = "CampaignEngine — Batch Jobs"
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

// Make Program accessible for integration tests
public partial class Program { }

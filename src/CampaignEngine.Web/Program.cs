using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Infrastructure.DependencyInjection;
using CampaignEngine.Web.Middleware;
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
    // ----------------------------------------------------------------
    builder.Services.AddRazorPages();
    builder.Services.AddControllers();

    // ----------------------------------------------------------------
    // OpenAPI / Swagger (development only)
    // ----------------------------------------------------------------
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "CampaignEngine API",
            Version = "v1",
            Description = "Multi-channel campaign engine — Email, SMS, Letter"
        });
    });

    // ----------------------------------------------------------------
    // HTTP context accessor (needed by correlation ID middleware)
    // ----------------------------------------------------------------
    builder.Services.AddHttpContextAccessor();

    var app = builder.Build();

    // ----------------------------------------------------------------
    // Middleware pipeline
    // ----------------------------------------------------------------
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
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
    app.UseAuthorization();

    app.MapRazorPages();
    app.MapControllers();

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

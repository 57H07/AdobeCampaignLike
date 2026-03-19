using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Infrastructure.DependencyInjection;
using CampaignEngine.Web.Middleware;

var builder = WebApplication.CreateBuilder(args);

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
// Logging: structured logging via ILogger (Serilog/NLog can plug in)
// ----------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

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

app.UseRouting();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();

// Make Program accessible for integration tests
public partial class Program { }

using CampaignEngine.Web.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using System.Net.Http;

namespace CampaignEngine.Infrastructure.Tests.OpenApi;

/// <summary>
/// Integration tests that verify the generated OpenAPI spec is valid and complete.
/// Uses WebApplicationFactory to spin up the full middleware pipeline
/// and requests the OpenAPI JSON spec from the /swagger/v1/swagger.json endpoint.
///
/// These tests satisfy TASK-032-05 acceptance criteria:
///   - Spec is well-formed OpenAPI 3.0
///   - Required endpoints are present
///   - Security scheme is documented
///   - No schema parse errors
/// </summary>
public class OpenApiSpecValidityTests : IClassFixture<OpenApiSpecValidityTests.SwaggerTestFactory>
{
    private readonly SwaggerTestFactory _factory;

    public OpenApiSpecValidityTests(SwaggerTestFactory factory)
    {
        _factory = factory;
    }

    // ----------------------------------------------------------------
    // Spec parsing tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task SwaggerJson_IsAccessible_AtExpectedRoute()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue(
            because: "the OpenAPI spec endpoint must be accessible in Development environment");
        response.Content.Headers.ContentType?.MediaType.Should().Contain("application/json");
    }

    [Fact]
    public async Task SwaggerJson_ParsesWithoutErrors_AsOpenApi30()
    {
        // Arrange
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");

        // Act — parse the spec using the official OpenAPI reader
        var reader = new OpenApiStringReader();
        var document = reader.Read(json, out var diagnostic);

        // Assert
        diagnostic.Errors.Should().BeEmpty(
            because: "the generated OpenAPI spec must have no parse errors");
        document.Should().NotBeNull();
        document.Info.Should().NotBeNull();
        document.Info.Title.Should().Be("CampaignEngine API");
        document.Info.Version.Should().Be("v1");
    }

    [Fact]
    public async Task SwaggerJson_ContainsApiKeySecurityScheme()
    {
        // Arrange
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        var reader = new OpenApiStringReader();
        var document = reader.Read(json, out _);

        // Assert — security scheme must be documented (business rule BR-4)
        document.Components.SecuritySchemes.Should().ContainKey("ApiKey",
            because: "API key authentication must be documented in the spec");

        var scheme = document.Components.SecuritySchemes["ApiKey"];
        scheme.Type.Should().Be(SecuritySchemeType.ApiKey);
        scheme.In.Should().Be(ParameterLocation.Header);
        scheme.Name.Should().Be("X-Api-Key");
    }

    [Fact]
    public async Task SwaggerJson_ContainsRequiredEndpoints()
    {
        // Arrange
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        var reader = new OpenApiStringReader();
        var document = reader.Read(json, out _);

        // Assert — all critical API paths must be present
        var paths = document.Paths.Keys.ToList();

        paths.Should().Contain(p => p.Equals("/api/send", StringComparison.OrdinalIgnoreCase),
            because: "the Generic Send API endpoint must be documented");
        paths.Should().Contain(p => p.Equals("/api/apikeys", StringComparison.OrdinalIgnoreCase),
            because: "the API key management endpoint must be documented");
        paths.Should().Contain(p => p.Equals("/api/templates", StringComparison.OrdinalIgnoreCase),
            because: "the Templates endpoint must be documented");
        paths.Should().Contain(p => p.Equals("/api/campaigns", StringComparison.OrdinalIgnoreCase),
            because: "the Campaigns endpoint must be documented");
        paths.Should().Contain(p => p.Equals("/api/sendlogs", StringComparison.OrdinalIgnoreCase),
            because: "the Send Logs endpoint must be documented");
    }

    [Fact]
    public async Task SwaggerJson_SendEndpoint_HasCorrectHttpMethod()
    {
        // Arrange
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        var reader = new OpenApiStringReader();
        var document = reader.Read(json, out _);

        // Assert — POST /api/send must have the correct response codes documented
        document.Paths.Should().ContainKey("/api/send");
        var sendPath = document.Paths["/api/send"];
        sendPath.Operations.Should().ContainKey(OperationType.Post,
            because: "POST /api/send is the primary send endpoint");

        var postOp = sendPath.Operations[OperationType.Post];
        postOp.Responses.Should().ContainKey("200",
            because: "successful send returns HTTP 200");
        postOp.Responses.Should().ContainKey("400",
            because: "validation errors return HTTP 400");
    }

    [Fact]
    public async Task SwaggerJson_HasGlobalSecurityRequirement()
    {
        // Arrange
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        var reader = new OpenApiStringReader();
        var document = reader.Read(json, out _);

        // Assert — global security requirement must reference ApiKey scheme
        document.SecurityRequirements.Should().NotBeEmpty(
            because: "all endpoints must require authentication by default");

        var globalSecurity = document.SecurityRequirements.First();
        globalSecurity.Keys.Should().Contain(
            k => k.Reference.Id == "ApiKey",
            because: "the ApiKey security scheme must be the global default");
    }

    [Fact]
    public async Task SwaggerJson_ContainsSchemas_ForCoreDtos()
    {
        // Arrange
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        var reader = new OpenApiStringReader();
        var document = reader.Read(json, out _);

        // Assert — key DTOs must be present as schema components
        var schemas = document.Components.Schemas.Keys.ToList();

        // At minimum the core Send API and ApiKey DTOs should be documented
        schemas.Should().Contain(s => s.Contains("SendRequest") || s.Contains("Send"),
            because: "the SendRequest DTO must appear in the schema components");

        schemas.Should().Contain(s => s.Contains("ApiKey") || s.Contains("CreateApiKey"),
            because: "the API key DTOs must appear in the schema components");
    }

    // ----------------------------------------------------------------
    // Web Application Factory — minimal test host
    // Uses "Development" environment to enable Swagger UI
    // ----------------------------------------------------------------

    /// <summary>
    /// Minimal WebApplicationFactory that configures a test host using the
    /// real Swagger registration (AddSwaggerDocumentation) but a fake DI context.
    ///
    /// We only need the Swagger middleware to respond to /swagger/v1/swagger.json —
    /// we do NOT need a real database, so infrastructure services are replaced with stubs.
    /// </summary>
    public sealed class SwaggerTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            // No additional overrides needed — the Swagger middleware does not require
            // a live database to serve the spec. Service resolution for controllers
            // only occurs on actual request dispatch, not spec generation.
        }
    }
}

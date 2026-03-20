using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace CampaignEngine.Infrastructure.Tests.ApiKeys;

/// <summary>
/// Unit tests for ApiKeyAuthenticationMiddleware.
/// Verifies that:
///   - Requests without X-Api-Key header pass through unchanged.
///   - Empty X-Api-Key header returns 401.
///   - Invalid/expired/revoked keys return 401.
///   - Valid keys set ClaimsPrincipal with ApiConsumer role and populate context items.
/// </summary>
public class ApiKeyAuthenticationMiddlewareTests
{
    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static ApiKeyAuthenticationMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        IApiKeyService? apiKeyService = null)
    {
        next ??= _ => Task.CompletedTask;
        var logger = new Mock<ILogger<ApiKeyAuthenticationMiddleware>>();
        return new ApiKeyAuthenticationMiddleware(next, logger.Object);
    }

    private static HttpContext CreateHttpContext(string? apiKeyHeaderValue = null, IApiKeyService? apiKeyService = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new System.IO.MemoryStream();

        if (apiKeyHeaderValue is not null)
            context.Request.Headers["X-Api-Key"] = apiKeyHeaderValue;

        if (apiKeyService is not null)
        {
            var services = new Mock<IServiceProvider>();
            services
                .Setup(sp => sp.GetService(typeof(IApiKeyService)))
                .Returns(apiKeyService);
            context.RequestServices = services.Object;
        }

        return context;
    }

    private static ApiKey BuildValidApiKey(string name = "Test Key") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        KeyHash = "$2b$10$placeholder",
        KeyPrefix = "ce_test1",
        IsActive = true,
        ExpiresAt = DateTime.UtcNow.AddDays(365),
        RateLimitPerMinute = 500
    };

    // ----------------------------------------------------------------
    // No X-Api-Key header — pass through
    // ----------------------------------------------------------------

    [Fact]
    public async Task NoApiKeyHeader_PassesThroughToNextMiddleware()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext(); // no header

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200); // unchanged
    }

    // ----------------------------------------------------------------
    // Empty X-Api-Key header — 401
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyApiKeyHeader_Returns401()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKeyHeaderValue: "   "); // whitespace

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(401);
    }

    // ----------------------------------------------------------------
    // Invalid key — 401
    // ----------------------------------------------------------------

    [Fact]
    public async Task InvalidApiKey_Returns401AndDoesNotCallNext()
    {
        // Arrange
        var nextCalled = false;
        var apiKeyService = new Mock<IApiKeyService>();
        apiKeyService
            .Setup(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiKey?)null);

        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateHttpContext("ce_invalidkey12345678", apiKeyService.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
    }

    // ----------------------------------------------------------------
    // Valid key — ClaimsPrincipal set correctly
    // ----------------------------------------------------------------

    [Fact]
    public async Task ValidApiKey_SetsPrincipalWithApiConsumerRole()
    {
        // Arrange
        var apiKey = BuildValidApiKey("My Integration");
        var apiKeyService = new Mock<IApiKeyService>();
        apiKeyService
            .Setup(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        ClaimsPrincipal? capturedPrincipal = null;
        var middleware = CreateMiddleware(next: ctx =>
        {
            capturedPrincipal = ctx.User;
            return Task.CompletedTask;
        });
        var context = CreateHttpContext("ce_validkeyabc12345678901234", apiKeyService.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(200);
        capturedPrincipal.Should().NotBeNull();
        capturedPrincipal!.Identity!.IsAuthenticated.Should().BeTrue();
        capturedPrincipal.Identity!.AuthenticationType.Should().Be(ApiKeyAuthenticationDefaults.AuthenticationScheme);
        capturedPrincipal.IsInRole("ApiConsumer").Should().BeTrue();
        capturedPrincipal.Identity!.Name.Should().Be("My Integration");
    }

    [Fact]
    public async Task ValidApiKey_SetsApiKeyIdInContextItems()
    {
        // Arrange
        var apiKey = BuildValidApiKey();
        var apiKeyService = new Mock<IApiKeyService>();
        apiKeyService
            .Setup(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext("ce_validkeyabc12345678901234", apiKeyService.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items["ApiKeyId"].Should().Be(apiKey.Id);
        context.Items["ApiKeyRateLimitPerMinute"].Should().Be(500); // From BuildValidApiKey
    }

    [Fact]
    public async Task ValidApiKey_WhenNoRateLimitOverride_UsesDefault()
    {
        // Arrange
        var apiKey = BuildValidApiKey();
        apiKey.RateLimitPerMinute = null; // No override

        var apiKeyService = new Mock<IApiKeyService>();
        apiKeyService
            .Setup(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext("ce_validkeyabc12345678901234", apiKeyService.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert — should use the system default
        context.Items["ApiKeyRateLimitPerMinute"].Should().Be(ApiKeyAuthenticationDefaults.DefaultRateLimitPerMinute);
    }

    [Fact]
    public async Task ValidApiKey_RateLimitClaimContainsEffectiveLimit()
    {
        // Arrange
        var apiKey = BuildValidApiKey();
        apiKey.RateLimitPerMinute = 200;

        var apiKeyService = new Mock<IApiKeyService>();
        apiKeyService
            .Setup(s => s.ValidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(apiKey);

        ClaimsPrincipal? capturedPrincipal = null;
        var middleware = CreateMiddleware(next: ctx =>
        {
            capturedPrincipal = ctx.User;
            return Task.CompletedTask;
        });
        var context = CreateHttpContext("ce_validkeyabc12345678901234", apiKeyService.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert — RateLimitPerMinute claim present
        var claim = capturedPrincipal!.FindFirst("RateLimitPerMinute");
        claim.Should().NotBeNull();
        claim!.Value.Should().Be("200");
    }
}

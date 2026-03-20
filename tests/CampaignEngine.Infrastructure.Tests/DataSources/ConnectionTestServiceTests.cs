using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.DataSources;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.DataSources;

/// <summary>
/// Unit tests for ConnectionTestService — validates the connectivity test logic
/// without requiring a real SQL Server or REST endpoint.
/// </summary>
public class ConnectionTestServiceTests
{
    private readonly Mock<IAppLogger<ConnectionTestService>> _loggerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    public ConnectionTestServiceTests()
    {
        _loggerMock = new Mock<IAppLogger<ConnectionTestService>>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
    }

    // ----------------------------------------------------------------
    // SQL Server tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task TestSqlServer_WithInvalidConnectionString_ReturnsFail()
    {
        // Arrange
        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act — deliberately invalid connection string triggers immediate exception
        var result = await service.TestAsync(
            DataSourceType.SqlServer,
            "Server=invalid_host_that_does_not_exist_12345;Database=test;User Id=sa;Password=test;Connect Timeout=1;",
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Connection failed");
        result.ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
        result.TestedAt.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TestSqlServer_WithEmptyConnectionString_ReturnsFail()
    {
        // Arrange
        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act
        var result = await service.TestAsync(
            DataSourceType.SqlServer,
            string.Empty,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
    }

    // ----------------------------------------------------------------
    // REST API tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task TestRestApi_WithInvalidUrl_ReturnsFail()
    {
        // Arrange
        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);
        // Use a fake HTTP client factory that returns a MockHttpMessageHandler client
        var fakeClient = new HttpClient(new FailingHttpMessageHandler());
        _httpClientFactoryMock.Setup(f => f.CreateClient("ConnectionTest")).Returns(fakeClient);

        // Act
        var result = await service.TestAsync(
            DataSourceType.RestApi,
            "https://this-host-does-not-exist-xyz.invalid/api",
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Connection failed");
    }

    [Fact]
    public async Task TestRestApi_WithNonAbsoluteUrl_ReturnsFail()
    {
        // Arrange
        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act — relative URL cannot create a valid HttpRequestMessage
        var result = await service.TestAsync(
            DataSourceType.RestApi,
            "not-a-valid-url",
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not a valid absolute URL");
    }

    [Fact]
    public async Task TestRestApi_WithSuccessfulResponse_ReturnsOk()
    {
        // Arrange
        var successHandler = new StubHttpMessageHandler(System.Net.HttpStatusCode.OK);
        var fakeClient = new HttpClient(successHandler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("ConnectionTest")).Returns(fakeClient);

        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act
        var result = await service.TestAsync(
            DataSourceType.RestApi,
            "https://example.com/api",
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("HTTP 200");
        result.ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task TestRestApi_WithAuthError401_ReturnsOk_BecauseEndpointIsReachable()
    {
        // Arrange — a 401 means the endpoint exists; it's reachable
        var handler = new StubHttpMessageHandler(System.Net.HttpStatusCode.Unauthorized);
        var fakeClient = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("ConnectionTest")).Returns(fakeClient);

        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act
        var result = await service.TestAsync(
            DataSourceType.RestApi,
            "https://example.com/api",
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue("A 401 response proves the endpoint is reachable");
    }

    [Fact]
    public async Task TestRestApi_WithServerError500_ReturnsFail()
    {
        // Arrange — a 500 indicates server-side failure
        var handler = new StubHttpMessageHandler(System.Net.HttpStatusCode.InternalServerError);
        var fakeClient = new HttpClient(handler);
        _httpClientFactoryMock.Setup(f => f.CreateClient("ConnectionTest")).Returns(fakeClient);

        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act
        var result = await service.TestAsync(
            DataSourceType.RestApi,
            "https://example.com/api",
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("HTTP 500");
    }

    // ----------------------------------------------------------------
    // Unknown type tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task Test_WithUnsupportedType_ReturnsFail()
    {
        // Arrange
        var service = new ConnectionTestService(_loggerMock.Object, _httpClientFactoryMock.Object);

        // Act — cast an out-of-range int to the enum to simulate an unknown type
        var result = await service.TestAsync(
            (DataSourceType)99,
            "anything",
            CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unsupported");
    }

    // ----------------------------------------------------------------
    // ConnectionTestResult factory method tests
    // ----------------------------------------------------------------

    [Fact]
    public void ConnectionTestResult_Ok_SetsSuccessAndMessage()
    {
        var result = ConnectionTestResult.Ok("Connected OK", 42);

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connected OK");
        result.ElapsedMs.Should().Be(42);
        result.TestedAt.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ConnectionTestResult_Fail_SetsFailureAndMessage()
    {
        var result = ConnectionTestResult.Fail("Could not connect", 100);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Could not connect");
        result.ElapsedMs.Should().Be(100);
    }
}

// ----------------------------------------------------------------
// Test doubles for HTTP message handler
// ----------------------------------------------------------------

/// <summary>Always throws HttpRequestException to simulate a network failure.</summary>
internal sealed class FailingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new HttpRequestException("Simulated network error");
}

/// <summary>Returns a fixed HTTP status code for all requests.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly System.Net.HttpStatusCode _statusCode;

    public StubHttpMessageHandler(System.Net.HttpStatusCode statusCode)
        => _statusCode = statusCode;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_statusCode));
}

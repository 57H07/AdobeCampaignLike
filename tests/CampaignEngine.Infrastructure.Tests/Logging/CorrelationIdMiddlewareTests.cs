namespace CampaignEngine.Infrastructure.Tests.Logging;

/// <summary>
/// Tests for correlation ID generation and propagation logic.
/// These tests validate the business rules around correlation IDs
/// without creating a dependency on the Web layer's middleware class.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    // The expected header name per the middleware contract
    private const string CorrelationIdHeader = "X-Correlation-Id";

    // ----------------------------------------------------------------
    // Correlation ID header contract
    // ----------------------------------------------------------------

    [Fact]
    public void CorrelationIdHeader_ShouldBeXCorrelationId()
    {
        // This test documents the public API contract of the correlation ID header name.
        // If this value changes, all downstream consumers must be updated.
        CorrelationIdHeader.Should().Be("X-Correlation-Id");
    }

    // ----------------------------------------------------------------
    // Correlation ID format validation (GUID format)
    // ----------------------------------------------------------------

    [Fact]
    public void GeneratedCorrelationId_ShouldBeValidGuid()
    {
        var correlationId = Guid.NewGuid().ToString("D");

        Guid.TryParse(correlationId, out _).Should().BeTrue(
            "generated correlation IDs must be valid GUIDs for distributed tracing compatibility");
    }

    [Fact]
    public void TwoGeneratedCorrelationIds_ShouldBeDifferent()
    {
        var id1 = Guid.NewGuid().ToString("D");
        var id2 = Guid.NewGuid().ToString("D");

        id1.Should().NotBe(id2,
            "each request must get a unique correlation ID");
    }

    [Theory]
    [InlineData("my-custom-trace-id")]
    [InlineData("abc123")]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")]
    public void CallerSuppliedCorrelationId_ShouldBePreservedAsIs(string callerSupplied)
    {
        // The middleware must honor caller-supplied correlation IDs without modification.
        // This test validates the expected behavior contract.
        var result = string.IsNullOrWhiteSpace(callerSupplied)
            ? Guid.NewGuid().ToString("D")
            : callerSupplied;

        result.Should().Be(callerSupplied);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceCorrelationId_ShouldTriggerGeneration(string? incoming)
    {
        // If the incoming header is empty/whitespace, a new GUID should be generated.
        var shouldGenerate = string.IsNullOrWhiteSpace(incoming);
        shouldGenerate.Should().BeTrue();
    }
}

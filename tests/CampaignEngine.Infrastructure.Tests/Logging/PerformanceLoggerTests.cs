using CampaignEngine.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Tests.Logging;

/// <summary>
/// Unit tests for PerformanceLogger.
/// Verifies that performance metrics are logged when the scope is disposed.
/// </summary>
public class PerformanceLoggerTests
{
    [Fact]
    public void Dispose_ShouldLogPerformanceMetric()
    {
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        using (new PerformanceLogger(mockLogger.Object, "TestOperation"))
        {
            // Simulate some work
            Thread.Sleep(10);
        }

        // Verify a log entry was made at Information or Warning level
        mockLogger.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Information || l == LogLevel.Warning),
                new EventId(1000, "PerformanceMetric"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldLogOnlyOnce()
    {
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var perf = new PerformanceLogger(mockLogger.Object, "DoubleDispose");
        perf.Dispose();
        perf.Dispose(); // Second dispose should be a no-op

        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                new EventId(1000, "PerformanceMetric"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Dispose_WithAdditionalProperties_ShouldStillLogOnce()
    {
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        using (new PerformanceLogger(mockLogger.Object, "OperationWithProps",
            ("CampaignId", (object)Guid.NewGuid()),
            ("RecipientCount", (object)500)))
        {
            // simulate work
        }

        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                new EventId(1000, "PerformanceMetric"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

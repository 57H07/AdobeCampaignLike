using CampaignEngine.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Tests.Logging;

public class AppLoggerTests
{
    // ----------------------------------------------------------------
    // Construction
    // ----------------------------------------------------------------

    [Fact]
    public void AppLogger_ShouldInstantiateWithILogger()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        appLogger.Should().NotBeNull();
    }

    // ----------------------------------------------------------------
    // Core log levels
    // ----------------------------------------------------------------

    [Fact]
    public void LogInformation_ShouldDelegateToILogger()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        appLogger.LogInformation("Test message");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogDebug_ShouldDelegateToILogger()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        appLogger.LogDebug("Debug message");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogWarning_ShouldDelegateToILogger()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        appLogger.LogWarning("Warning message");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogError_ShouldDelegateToILoggerWithException()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);
        var exception = new Exception("Test error");

        appLogger.LogError(exception, "Error occurred");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogCritical_ShouldDelegateToILoggerWithException()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);
        var exception = new InvalidOperationException("Critical failure");

        appLogger.LogCritical(exception, "Critical: {Message}", exception.Message);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Performance logging
    // ----------------------------------------------------------------

    [Fact]
    public void LogPerformance_FastOperation_ShouldLogAtInformationLevel()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        // 500ms is under the 1000ms threshold → Information
        appLogger.LogPerformance("RenderTemplate", 500);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                new EventId(1000, "PerformanceMetric"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogPerformance_SlowOperation_ShouldLogAtWarningLevel()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        mockLogger.Setup(x => x.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        // 1500ms exceeds 1000ms threshold → Warning
        appLogger.LogPerformance("SendBatch", 1500);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                new EventId(1000, "PerformanceMetric"),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Dispatch logging
    // ----------------------------------------------------------------

    [Fact]
    public void LogDispatch_Success_ShouldLogAtInformationLevel()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);
        var campaignId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        appLogger.LogDispatch(campaignId, templateId, "Email", "Sent");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogDispatch_WithError_ShouldLogAtWarningLevel()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);
        var campaignId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        appLogger.LogDispatch(campaignId, templateId, "Email", "Failed", "SMTP connection refused");

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // API request logging
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(200, LogLevel.Information)]
    [InlineData(201, LogLevel.Information)]
    [InlineData(400, LogLevel.Warning)]
    [InlineData(404, LogLevel.Warning)]
    [InlineData(500, LogLevel.Error)]
    [InlineData(503, LogLevel.Error)]
    public void LogApiRequest_ShouldUseCorrectLevelForStatusCode(
        int statusCode, LogLevel expectedLevel)
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        appLogger.LogApiRequest("POST", "/api/send", statusCode, 120, "correlation-xyz");

        mockLogger.Verify(
            x => x.Log(
                expectedLevel,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

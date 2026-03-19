using CampaignEngine.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Tests.Logging;

public class AppLoggerTests
{
    [Fact]
    public void AppLogger_ShouldInstantiateWithILogger()
    {
        var mockLogger = new Mock<ILogger<AppLoggerTests>>();
        var appLogger = new AppLogger<AppLoggerTests>(mockLogger.Object);

        appLogger.Should().NotBeNull();
    }

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
                It.IsAny<Exception>(),
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
}

using CampaignEngine.Application.DTOs.Dispatch;

namespace CampaignEngine.Application.Tests.DTOs;

public class DispatchResultTests
{
    [Fact]
    public void Ok_ShouldReturnSuccessResult()
    {
        var result = DispatchResult.Ok("msg-123");

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("msg-123");
        result.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public void Fail_ShouldReturnFailedResult()
    {
        var result = DispatchResult.Fail("SMTP connection refused", isTransient: true);

        result.Success.Should().BeFalse();
        result.ErrorDetail.Should().Be("SMTP connection refused");
        result.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public void Fail_PermanentError_ShouldNotBeTransient()
    {
        var result = DispatchResult.Fail("Invalid email address");

        result.IsTransientFailure.Should().BeFalse();
    }
}

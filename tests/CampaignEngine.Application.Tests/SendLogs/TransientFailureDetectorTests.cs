using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Dispatch;
using System.Net.Sockets;

namespace CampaignEngine.Application.Tests.SendLogs;

/// <summary>
/// Unit tests for TransientFailureDetector — SMTP and SMS error classification.
///
/// TASK-035-02: Transient failure detection (SMTP, SMS errors).
/// TASK-035-05: Retry logic tests (transient vs permanent detection).
///
/// Business rules validated:
///   Transient (retriable): SMTP 4xx, socket errors, SMS 429/5xx, network timeouts.
///   Permanent (not retriable): SMTP 5xx reject, auth failure, invalid phone, template errors.
/// </summary>
public class TransientFailureDetectorTests
{
    private readonly TransientFailureDetector _detector = new();

    // ----------------------------------------------------------------
    // IsTransient: SmtpDispatchException
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransient_SmtpExceptionTransient_ReturnsTrue()
    {
        var ex = new SmtpDispatchException("Connection timeout", isTransient: true, smtpStatusCode: 421);

        _detector.IsTransient(ex).Should().BeTrue(
            because: "SMTP 4xx/connection errors are transient per BR-3");
    }

    [Fact]
    public void IsTransient_SmtpExceptionPermanent_ReturnsFalse()
    {
        var ex = new SmtpDispatchException("Mailbox not found", isTransient: false, smtpStatusCode: 550);

        _detector.IsTransient(ex).Should().BeFalse(
            because: "SMTP 5xx reject is a permanent failure per BR-4");
    }

    // ----------------------------------------------------------------
    // IsTransient: SmsDispatchException
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransient_SmsExceptionTransient_ReturnsTrue()
    {
        var ex = new SmsDispatchException("Rate limited", isTransient: true, httpStatusCode: 429);

        _detector.IsTransient(ex).Should().BeTrue(
            because: "SMS rate limit (429) is a transient failure per BR-3");
    }

    [Fact]
    public void IsTransient_SmsExceptionPermanent_ReturnsFalse()
    {
        var ex = new SmsDispatchException("Invalid phone number", isTransient: false, httpStatusCode: 400);

        _detector.IsTransient(ex).Should().BeFalse(
            because: "Invalid phone number is a permanent failure per BR-4");
    }

    // ----------------------------------------------------------------
    // IsTransient: domain exceptions (permanent)
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransient_AttachmentValidationException_ReturnsFalse()
    {
        var ex = new AttachmentValidationException("File too large", "attachment.pdf");

        _detector.IsTransient(ex).Should().BeFalse(
            because: "attachment validation failures are permanent per BR-4");
    }

    [Fact]
    public void IsTransient_InvalidPhoneNumberException_ReturnsFalse()
    {
        var ex = new InvalidPhoneNumberException("+invalid");

        _detector.IsTransient(ex).Should().BeFalse(
            because: "invalid phone number is a permanent failure per BR-4");
    }

    [Fact]
    public void IsTransient_TemplateRenderException_ReturnsFalse()
    {
        var ex = new TemplateRenderException("Scriban parsing failed");

        _detector.IsTransient(ex).Should().BeFalse(
            because: "template rendering errors are permanent per BR-4");
    }

    [Fact]
    public void IsTransient_DomainException_ReturnsFalse()
    {
        var ex = new DomainException("Domain invariant violated");

        _detector.IsTransient(ex).Should().BeFalse(
            because: "domain invariant violations are permanent failures");
    }

    // ----------------------------------------------------------------
    // IsTransient: network/infrastructure exceptions (transient)
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransient_SocketException_ReturnsTrue()
    {
        var ex = new SocketException((int)SocketError.ConnectionRefused);

        _detector.IsTransient(ex).Should().BeTrue(
            because: "socket/network connectivity errors are transient per BR-3");
    }

    [Fact]
    public void IsTransient_IOException_ReturnsTrue()
    {
        var ex = new IOException("Connection reset by peer");

        _detector.IsTransient(ex).Should().BeTrue(
            because: "IO errors are generally transient per BR-3");
    }

    [Fact]
    public void IsTransient_OperationCanceledException_ReturnsFalse()
    {
        var ex = new OperationCanceledException("Operation was cancelled");

        _detector.IsTransient(ex).Should().BeFalse(
            because: "cancellations are intentional and should not be retried");
    }

    // ----------------------------------------------------------------
    // IsTransient: unknown exceptions treated as transient (fail-safe)
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransient_UnknownException_ReturnsTrueForRecovery()
    {
        var ex = new InvalidOperationException("Unexpected error from provider");

        _detector.IsTransient(ex).Should().BeTrue(
            because: "unknown exceptions are treated as transient to attempt recovery");
    }

    // ----------------------------------------------------------------
    // IsTransientMessage: transient error message fragments
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("Connection timeout waiting for SMTP")]
    [InlineData("Server timed out after 30s")]
    [InlineData("Connection refused by remote host")]
    [InlineData("Connection reset by peer")]
    [InlineData("Service temporarily unavailable")]
    [InlineData("Rate limit exceeded — too many requests")]
    [InlineData("Service unavailable, please try again")]
    [InlineData("Transient error, retry later")]
    [InlineData("Socket write error")]
    [InlineData("Network failure while sending")]
    [InlineData("IO error during transmission")]
    public void IsTransientMessage_TransientFragment_ReturnsTrue(string errorMessage)
    {
        _detector.IsTransientMessage(errorMessage).Should().BeTrue(
            because: $"'{errorMessage}' contains a transient failure indicator");
    }

    // ----------------------------------------------------------------
    // IsTransientMessage: permanent error message fragments
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("Invalid email address: user@")]
    [InlineData("Invalid address format for recipient")]
    [InlineData("Authentication failed — bad credentials")]
    [InlineData("Authentication failure for SMTP")]
    [InlineData("Invalid phone number format")]
    [InlineData("Phone number not in E.164 format")]
    [InlineData("Template error: placeholder missing")]
    [InlineData("Template rendering failed")]
    [InlineData("Attachment size exceeds limit")]
    public void IsTransientMessage_PermanentFragment_ReturnsFalse(string errorMessage)
    {
        _detector.IsTransientMessage(errorMessage).Should().BeFalse(
            because: $"'{errorMessage}' contains a permanent failure indicator");
    }

    // ----------------------------------------------------------------
    // IsTransientMessage: null/empty inputs
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsTransientMessage_NullOrWhitespace_ReturnsFalse(string? errorMessage)
    {
        _detector.IsTransientMessage(errorMessage!).Should().BeFalse(
            because: "null or whitespace messages cannot be classified as transient");
    }

    // ----------------------------------------------------------------
    // IsTransientMessage: ambiguous message (no known fragment) → false
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransientMessage_UnknownMessage_ReturnsFalse()
    {
        // A message with no transient or permanent fragments
        _detector.IsTransientMessage("An unexpected error occurred during send.").Should().BeFalse(
            because: "messages without known transient fragments default to non-transient");
    }

    // ----------------------------------------------------------------
    // IsTransientMessage: case-insensitive matching
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransientMessage_UpperCaseFragment_IsCaseInsensitive()
    {
        _detector.IsTransientMessage("CONNECTION TIMEOUT").Should().BeTrue(
            because: "transient fragment matching is case-insensitive");
    }

    [Fact]
    public void IsTransientMessage_MixedCasePermanentFragment_IsCaseInsensitive()
    {
        _detector.IsTransientMessage("INVALID EMAIL address detected").Should().BeFalse(
            because: "permanent fragment detection is case-insensitive");
    }
}

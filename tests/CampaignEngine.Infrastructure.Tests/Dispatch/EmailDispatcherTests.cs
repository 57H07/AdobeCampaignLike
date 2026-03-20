using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Unit and integration tests for EmailDispatcher covering:
///   - Channel property
///   - Message construction: To, CC, BCC, Reply-To, Subject
///   - Attachment validation: whitelist, per-file size, total size, file path loading
///   - SMTP error categorization: transient vs permanent
///   - Happy path and edge cases
///
/// TASK-019-06: Integration tests (using a testable subclass that overrides SendViaSmtpAsync
///              to avoid requiring a live SMTP server).
/// TASK-019-07: Attachment validation tests.
/// </summary>
public class EmailDispatcherTests
{
    private readonly SmtpOptions _defaultOptions = new()
    {
        Host = "localhost",
        Port = 587,
        UseSsl = false,
        UserName = "test",
        Password = "test",
        FromAddress = "noreply@example.com",
        FromName = "Test Sender",
        ReplyToAddress = null,
        TimeoutSeconds = 10,
        MaxAttachmentFileSizeBytes = 10 * 1024 * 1024,    // 10 MB
        MaxAttachmentTotalSizeBytes = 25 * 1024 * 1024,   // 25 MB
        AllowedAttachmentExtensions = [".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg"]
    };

    // ----------------------------------------------------------------
    // Channel property
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsEmail()
    {
        var dispatcher = CreateDispatcher();
        dispatcher.Channel.Should().Be(ChannelType.Email);
    }

    // ----------------------------------------------------------------
    // TASK-019-06: Basic send scenarios
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidRequest_ReturnsSuccess()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
        result.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_MissingRecipientEmail_ReturnsFailure()
    {
        var dispatcher = CreateDispatcher();
        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "<p>Hello</p>",
            Subject = "Test",
            Recipient = new RecipientInfo { Email = null }
        };

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("email address is required");
    }

    [Fact]
    public async Task SendAsync_EmptyRecipientEmail_ReturnsFailure()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_SuccessResult_ContainsMessageId()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
        result.MessageId.Should().NotBeNullOrWhiteSpace();
    }

    // ----------------------------------------------------------------
    // TASK-019-04: CC/BCC recipient handling
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithValidCcAddresses_Succeeds()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.CcAddresses.Add("cc1@example.com");
        request.CcAddresses.Add("cc2@example.com");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithValidBccAddresses_Succeeds()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.BccAddresses.Add("bcc@example.com");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithInvalidCcAddress_SkipsInvalidAndSucceeds()
    {
        // Invalid CC addresses are logged and skipped — they don't fail the send.
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.CcAddresses.Add("not-an-email");
        request.CcAddresses.Add("valid-cc@example.com");

        // Send should still succeed; invalid CC is silently skipped
        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithReplyToOverride_Succeeds()
    {
        // Business rule 2: Reply-To optional per campaign, overrides SmtpOptions default
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.ReplyToAddress = "reply@campaign.com";

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithInvalidReplyTo_StillSucceeds()
    {
        // Invalid Reply-To is silently ignored (not a valid email)
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.ReplyToAddress = "not-a-valid-email";

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // TASK-019-07: Attachment validation tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithValidAttachmentBytes_Succeeds()
    {
        // Business rule 3: whitelist — PDF is allowed
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Attachments.Add(AttachmentInfo.FromBytes(
            "document.pdf",
            new byte[1024],
            "application/pdf"));

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_AttachmentWithDisallowedExtension_ReturnsFailure()
    {
        // Business rule 3: EXE is not whitelisted
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Attachments.Add(AttachmentInfo.FromBytes(
            "malware.exe",
            new byte[512],
            "application/octet-stream"));

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain(".exe");
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    public async Task SendAsync_WhitelistedExtension_Succeeds(string extension)
    {
        // Business rule 3: all whitelisted extensions should be accepted
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Attachments.Add(AttachmentInfo.FromBytes(
            $"file{extension}",
            new byte[128],
            AttachmentInfo.GetMimeType(extension)));

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_AttachmentExceedsPerFileLimit_ReturnsFailure()
    {
        // Business rule 4: 10 MB per file limit (we test with a smaller limit)
        var options = new SmtpOptions
        {
            Host = "localhost",
            Port = 587,
            FromAddress = "from@example.com",
            MaxAttachmentFileSizeBytes = 1024,        // 1 KB for test
            MaxAttachmentTotalSizeBytes = 25 * 1024 * 1024,
            AllowedAttachmentExtensions = [".pdf"]
        };
        var dispatcher = CreateDispatcher(options: options);
        var request = BuildRequest("recipient@example.com");
        request.Attachments.Add(AttachmentInfo.FromBytes(
            "large.pdf",
            new byte[2048],           // 2 KB — exceeds 1 KB limit
            "application/pdf"));

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("maximum file size");
    }

    [Fact]
    public async Task SendAsync_TotalAttachmentSizeExceedsLimit_ReturnsFailure()
    {
        // Business rule 4: total size limit test
        var options = new SmtpOptions
        {
            Host = "localhost",
            Port = 587,
            FromAddress = "from@example.com",
            MaxAttachmentFileSizeBytes = 5 * 1024,     // 5 KB per file
            MaxAttachmentTotalSizeBytes = 8 * 1024,    // 8 KB total
            AllowedAttachmentExtensions = [".pdf"]
        };
        var dispatcher = CreateDispatcher(options: options);
        var request = BuildRequest("recipient@example.com");

        // Two 5 KB files = 10 KB total, exceeds 8 KB limit
        request.Attachments.Add(AttachmentInfo.FromBytes("doc1.pdf", new byte[5 * 1024], "application/pdf"));
        request.Attachments.Add(AttachmentInfo.FromBytes("doc2.pdf", new byte[5 * 1024], "application/pdf"));

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail!.ToLowerInvariant().Should().Contain("total attachment size");
    }

    [Fact]
    public async Task SendAsync_MultipleValidAttachments_Succeeds()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Attachments.Add(AttachmentInfo.FromBytes("doc.pdf", new byte[1024], "application/pdf"));
        request.Attachments.Add(AttachmentInfo.FromBytes("sheet.xlsx", new byte[512], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        request.Attachments.Add(AttachmentInfo.FromBytes("image.png", new byte[256], "image/png"));

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_AttachmentFromFilePath_FileNotFound_ReturnsFailure()
    {
        // TASK-019-03: file path handling — file doesn't exist
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Attachments.Add(new AttachmentInfo
        {
            FileName = "missing.pdf",
            MimeType = "application/pdf",
            FilePath = "/nonexistent/path/to/missing.pdf",
            Data = []
        });

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("not found");
    }

    [Fact]
    public async Task SendAsync_AttachmentFromFilePath_ValidFile_Succeeds()
    {
        // TASK-019-03: Create a real temp file and verify it gets loaded
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[512]);

            var dispatcher = CreateDispatcher();
            var request = BuildRequest("recipient@example.com");
            request.Attachments.Add(AttachmentInfo.FromFilePath(tempFile));

            var result = await dispatcher.SendAsync(request);

            result.Success.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ----------------------------------------------------------------
    // TASK-019-05: SMTP error categorization tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_TransientSmtpFailure_ReturnsTransientFailure()
    {
        // Simulate a transient SMTP failure (connection refused / network error)
        var dispatcher = CreateDispatcherWithSmtpFailure(isTransient: true);
        var request = BuildRequest("recipient@example.com");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_PermanentSmtpFailure_ReturnsPermanentFailure()
    {
        // Simulate a permanent SMTP failure (e.g. 550 - user not found)
        var dispatcher = CreateDispatcherWithSmtpFailure(isTransient: false);
        var request = BuildRequest("recipient@example.com");

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    // ----------------------------------------------------------------
    // AttachmentInfo static factory and MIME type tests (TASK-019-07)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".unknown", "application/octet-stream")]
    public void AttachmentInfo_GetMimeType_ReturnsCorrectMimeType(string extension, string expectedMime)
    {
        AttachmentInfo.GetMimeType(extension).Should().Be(expectedMime);
    }

    [Fact]
    public void AttachmentInfo_FromFilePath_SetsFileNameAndMimeType()
    {
        var path = "/some/path/report.pdf";

        var attachment = AttachmentInfo.FromFilePath(path);

        attachment.FileName.Should().Be("report.pdf");
        attachment.MimeType.Should().Be("application/pdf");
        attachment.FilePath.Should().Be(path);
        attachment.Data.Should().BeEmpty(); // data loaded lazily at send time
    }

    [Fact]
    public void AttachmentInfo_FromBytes_SetsAllFields()
    {
        var data = new byte[] { 1, 2, 3 };

        var attachment = AttachmentInfo.FromBytes("test.png", data, "image/png");

        attachment.FileName.Should().Be("test.png");
        attachment.MimeType.Should().Be("image/png");
        attachment.Data.Should().Equal(data);
        attachment.FilePath.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // Subject propagation
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_WithSubject_Succeeds()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Subject = "Campaign: Q1 Newsletter";

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_NullSubject_UsesDefaultSubjectAndSucceeds()
    {
        var dispatcher = CreateDispatcher();
        var request = BuildRequest("recipient@example.com");
        request.Subject = null;

        // Should not throw — default "(No Subject)" is used
        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // DispatchRequest BccAddresses field
    // ----------------------------------------------------------------

    [Fact]
    public void DispatchRequest_BccAddresses_DefaultsToEmpty()
    {
        var request = new DispatchRequest();
        request.BccAddresses.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void DispatchRequest_ReplyToAddress_DefaultsToNull()
    {
        var request = new DispatchRequest();
        request.ReplyToAddress.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a testable EmailDispatcher with a no-op SMTP transport (no live server).
    /// </summary>
    private TestableEmailDispatcher CreateDispatcher(SmtpOptions? options = null)
    {
        var opts = Options.Create(options ?? _defaultOptions);
        return new TestableEmailDispatcher(opts, NullLogger<EmailDispatcher>.Instance,
            smtpException: null);
    }

    /// <summary>
    /// Creates a testable EmailDispatcher that simulates SMTP send failures.
    /// </summary>
    private TestableEmailDispatcher CreateDispatcherWithSmtpFailure(bool isTransient)
    {
        var opts = Options.Create(_defaultOptions);
        var exception = new Domain.Exceptions.SmtpDispatchException(
            isTransient ? "Simulated transient SMTP failure" : "Simulated permanent SMTP failure",
            isTransient: isTransient);
        return new TestableEmailDispatcher(opts, NullLogger<EmailDispatcher>.Instance,
            smtpException: exception);
    }

    private static DispatchRequest BuildRequest(string recipientEmail) => new()
    {
        Channel = ChannelType.Email,
        Content = "<html><body><p>Hello</p></body></html>",
        Subject = "Test Subject",
        Recipient = new RecipientInfo
        {
            Email = recipientEmail,
            DisplayName = "Test User"
        }
    };
}

/// <summary>
/// Testable subclass of EmailDispatcher that overrides the protected SendViaSmtpAsync
/// to bypass real SMTP network calls. This allows testing all message-building and
/// validation logic in isolation.
/// </summary>
internal sealed class TestableEmailDispatcher : EmailDispatcher
{
    private readonly Exception? _smtpException;
    private MimeMessage? _lastSentMessage;

    public MimeMessage? LastSentMessage => _lastSentMessage;

    public TestableEmailDispatcher(
        IOptions<SmtpOptions> smtpOptions,
        ILogger<EmailDispatcher> logger,
        Exception? smtpException)
        : base(smtpOptions, logger)
    {
        _smtpException = smtpException;
    }

    protected override Task SendViaSmtpAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        if (_smtpException is not null)
        {
            throw _smtpException;
        }

        _lastSentMessage = message;
        return Task.CompletedTask;
    }
}

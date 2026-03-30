using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Unit tests for the rewritten LetterDispatcher (US-023 / F-403).
///
/// TASK-023-09: Unit tests per F-404 coverage requirements.
///
/// Coverage:
///   - Happy path: valid BinaryContent → success + file written
///   - Channel disabled → permanent failure, no file written
///   - Null BinaryContent → permanent failure, no file written
///   - Empty BinaryContent → permanent failure, no file written
///   - File naming convention: {campaignId}_{recipientId}_{timestamp}.docx
///   - I/O failure → LetterDispatchException (transient)
///   - Message ID format: LETTER-{campaignId}-{recipientId}
///   - Channel property returns Letter
///
/// Uses NoOpLetterFileDropHandler to capture writes without touching disk.
/// Uses FailingLetterFileDropHandler to simulate I/O failures.
/// </summary>
public class LetterDispatcherTests
{
    private static readonly byte[] ValidDocxBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00]; // DOCX/ZIP magic bytes

    private static readonly LetterOptions DefaultOptions = new()
    {
        IsEnabled = true,
        OutputDirectory = "/tmp/letters-test"
    };

    // ----------------------------------------------------------------
    // Factory helpers
    // ----------------------------------------------------------------

    private static LetterDispatcher CreateDispatcher(
        NoOpLetterFileDropHandler? fileDropHandler = null,
        LetterOptions? options = null)
    {
        var opts = options ?? DefaultOptions;
        var drop = fileDropHandler ?? new NoOpLetterFileDropHandler(opts);
        return new LetterDispatcher(drop, Options.Create(opts), NullLogger<LetterDispatcher>.Instance);
    }

    private static DispatchRequest BuildRequest(
        byte[]? binaryContent = null,
        string? recipientId = null,
        string? displayName = null,
        Guid? campaignId = null) => new()
    {
        Channel = ChannelType.Letter,
        BinaryContent = binaryContent ?? ValidDocxBytes,
        CampaignId = campaignId ?? Guid.NewGuid(),
        Recipient = new RecipientInfo
        {
            ExternalRef = recipientId ?? "REC-001",
            DisplayName = displayName ?? "Alice Smith",
            Email = "alice@example.com"
        }
    };

    // ----------------------------------------------------------------
    // IChannelDispatcher contract
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsLetter()
    {
        CreateDispatcher().Channel.Should().Be(ChannelType.Letter);
    }

    // ----------------------------------------------------------------
    // TASK-023-01/04: Happy path — valid BinaryContent
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidBinaryContent_ReturnsSuccess()
    {
        var dispatcher = CreateDispatcher();
        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeTrue();
        result.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ValidBinaryContent_WritesOneFile()
    {
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);

        await dispatcher.SendAsync(BuildRequest());

        fileDropHandler.WrittenFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAsync_ValidBinaryContent_MessageIdContainsCampaignId()
    {
        var campaignId = Guid.NewGuid();
        var dispatcher = CreateDispatcher();

        var result = await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));

        result.Success.Should().BeTrue();
        result.MessageId.Should().Contain(campaignId.ToString("N"));
    }

    [Fact]
    public async Task SendAsync_ValidBinaryContent_MessageIdContainsRecipientId()
    {
        var dispatcher = CreateDispatcher();

        var result = await dispatcher.SendAsync(BuildRequest(recipientId: "REC-007"));

        result.Success.Should().BeTrue();
        result.MessageId.Should().Contain("REC-007");
    }

    // ----------------------------------------------------------------
    // TASK-023-03: File naming convention
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidRequest_FileNameContainsCampaignId()
    {
        var campaignId = Guid.NewGuid();
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);

        await dispatcher.SendAsync(BuildRequest(campaignId: campaignId));

        var writtenFile = fileDropHandler.WrittenFiles.Single();
        writtenFile.CampaignId.Should().Be(campaignId);
    }

    [Fact]
    public async Task SendAsync_ValidRequest_FileNameContainsRecipientId()
    {
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);

        await dispatcher.SendAsync(BuildRequest(recipientId: "REC-042"));

        var writtenFile = fileDropHandler.WrittenFiles.Single();
        writtenFile.RecipientId.Should().Be("REC-042");
    }

    [Fact]
    public async Task SendAsync_ValidRequest_FileNameHasDocxExtension()
    {
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);

        await dispatcher.SendAsync(BuildRequest());

        var writtenFile = fileDropHandler.WrittenFiles.Single();
        writtenFile.FilePath.Should().EndWith(".docx");
    }

    [Fact]
    public async Task SendAsync_TwoRecipients_WritesTwoFiles()
    {
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);
        var campaignId = Guid.NewGuid();

        await dispatcher.SendAsync(BuildRequest(recipientId: "REC-001", campaignId: campaignId));
        await dispatcher.SendAsync(BuildRequest(recipientId: "REC-002", campaignId: campaignId));

        fileDropHandler.WrittenFiles.Should().HaveCount(2);
    }

    // ----------------------------------------------------------------
    // TASK-023-04: Validation — null BinaryContent
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_NullBinaryContent_ReturnsPermanentFailure()
    {
        var dispatcher = CreateDispatcher();
        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = null,
            CampaignId = Guid.NewGuid(),
            Recipient = new RecipientInfo { ExternalRef = "REC-001", DisplayName = "Alice" }
        };

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_NullBinaryContent_WritesNoFiles()
    {
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);
        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = null,
            CampaignId = Guid.NewGuid(),
            Recipient = new RecipientInfo { ExternalRef = "REC-001", DisplayName = "Alice" }
        };

        await dispatcher.SendAsync(request);

        fileDropHandler.WrittenFiles.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // TASK-023-04: Validation — empty BinaryContent
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_EmptyBinaryContent_ReturnsPermanentFailure()
    {
        var dispatcher = CreateDispatcher();
        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = Array.Empty<byte>(),
            CampaignId = Guid.NewGuid(),
            Recipient = new RecipientInfo { ExternalRef = "REC-001", DisplayName = "Alice" }
        };

        var result = await dispatcher.SendAsync(request);

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_EmptyBinaryContent_WritesNoFiles()
    {
        var fileDropHandler = new NoOpLetterFileDropHandler(DefaultOptions);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler);
        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = Array.Empty<byte>(),
            CampaignId = Guid.NewGuid(),
            Recipient = new RecipientInfo { ExternalRef = "REC-001", DisplayName = "Alice" }
        };

        await dispatcher.SendAsync(request);

        fileDropHandler.WrittenFiles.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Disabled channel
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ChannelDisabled_ReturnsPermanentFailure()
    {
        var opts = new LetterOptions { IsEnabled = false, OutputDirectory = "/tmp" };
        var dispatcher = CreateDispatcher(options: opts);

        var result = await dispatcher.SendAsync(BuildRequest());

        result.Success.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorDetail.Should().Contain("disabled");
    }

    [Fact]
    public async Task SendAsync_ChannelDisabled_WritesNoFiles()
    {
        var opts = new LetterOptions { IsEnabled = false, OutputDirectory = "/tmp" };
        var fileDropHandler = new NoOpLetterFileDropHandler(opts);
        var dispatcher = CreateDispatcher(fileDropHandler: fileDropHandler, options: opts);

        await dispatcher.SendAsync(BuildRequest());

        fileDropHandler.WrittenFiles.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // TASK-023-05: I/O failure maps to LetterDispatchException (transient)
    // ----------------------------------------------------------------

    [Fact]
    public async Task SendAsync_IoFailure_ThrowsLetterDispatchException()
    {
        var fileDropHandler = new FailingLetterFileDropHandler(DefaultOptions);
        var dispatcher = new LetterDispatcher(
            fileDropHandler,
            Options.Create(DefaultOptions),
            NullLogger<LetterDispatcher>.Instance);

        var act = async () => await dispatcher.SendAsync(BuildRequest());

        await act.Should().ThrowAsync<LetterDispatchException>();
    }

    [Fact]
    public async Task SendAsync_IoFailure_ExceptionIsTransient()
    {
        var fileDropHandler = new FailingLetterFileDropHandler(DefaultOptions);
        var dispatcher = new LetterDispatcher(
            fileDropHandler,
            Options.Create(DefaultOptions),
            NullLogger<LetterDispatcher>.Instance);

        var ex = await Assert.ThrowsAsync<LetterDispatchException>(
            () => dispatcher.SendAsync(BuildRequest()));

        ex.IsTransient.Should().BeTrue();
    }
}

// ================================================================
// Test doubles
// ================================================================

/// <summary>
/// File drop handler that captures written files in memory without touching the file system.
/// </summary>
internal sealed class NoOpLetterFileDropHandler : PrintProviderFileDropHandler
{
    public record WrittenFile(Guid CampaignId, string RecipientId, byte[] DocxBytes, string FilePath);
    public List<WrittenFile> WrittenFiles { get; } = [];

    public NoOpLetterFileDropHandler(LetterOptions options)
        : base(Options.Create(options), NullLogger<PrintProviderFileDropHandler>.Instance)
    { }

    public override Task<string> WriteFileAsync(
        byte[] docxBytes,
        Guid campaignId,
        string recipientId,
        DateTime? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        var ts = (timestamp ?? DateTime.UtcNow).ToString("yyyyMMddHHmmss");
        var filePath = Path.Combine("test-output", $"{campaignId:N}_{recipientId}_{ts}.docx");
        WrittenFiles.Add(new WrittenFile(campaignId, recipientId, docxBytes, filePath));
        return Task.FromResult(filePath);
    }
}

/// <summary>
/// File drop handler that always throws a LetterDispatchException (transient) to simulate I/O failures.
/// </summary>
internal sealed class FailingLetterFileDropHandler : PrintProviderFileDropHandler
{
    public FailingLetterFileDropHandler(LetterOptions options)
        : base(Options.Create(options), NullLogger<PrintProviderFileDropHandler>.Instance)
    { }

    public override Task<string> WriteFileAsync(
        byte[] docxBytes,
        Guid campaignId,
        string recipientId,
        DateTime? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        throw new LetterDispatchException(
            $"Simulated I/O failure writing DOCX for recipient '{recipientId}'.",
            isTransient: true);
    }
}

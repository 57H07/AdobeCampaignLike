using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Attachments;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.Attachments;

/// <summary>
/// Unit tests for DynamicAttachmentResolver.
///
/// US-028 TASK-028-09: Missing dynamic attachment handling tests.
///
/// Covers:
///   - Static-only scenario (no dynamic field configured)
///   - Dynamic field missing from recipient data — warning logged, send not blocked
///   - Dynamic field value empty — warning logged, send not blocked
///   - Dynamic file not found on disk — warning logged, send not blocked
///   - Successful dynamic attachment resolution
///   - Static + dynamic combination
/// </summary>
public class DynamicAttachmentResolverTests : IDisposable
{
    private readonly Mock<IAppLogger<DynamicAttachmentResolver>> _loggerMock;
    private readonly DynamicAttachmentResolver _sut;

    // Temp files created during tests
    private readonly List<string> _tempFiles = [];

    public DynamicAttachmentResolverTests()
    {
        _loggerMock = new Mock<IAppLogger<DynamicAttachmentResolver>>();
        _sut = new DynamicAttachmentResolver(_loggerMock.Object);
    }

    public void Dispose()
    {
        // Clean up any temp files created during tests
        foreach (var f in _tempFiles)
        {
            if (File.Exists(f))
                File.Delete(f);
        }
    }

    // ----------------------------------------------------------------
    // No dynamic field configured
    // ----------------------------------------------------------------

    [Fact]
    public void Resolve_NullDynamicField_ReturnsStaticOnly()
    {
        var staticAttachment = BuildStaticAttachment("report.pdf");
        var recipient = EmptyRecipient();

        var result = _sut.Resolve([staticAttachment], null, recipient);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("report.pdf");
    }

    [Fact]
    public void Resolve_EmptyDynamicField_ReturnsStaticOnly()
    {
        var staticAttachment = BuildStaticAttachment("report.pdf");
        var recipient = EmptyRecipient();

        var result = _sut.Resolve([staticAttachment], "   ", recipient);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_NoStaticAndNoDynamic_ReturnsEmpty()
    {
        var result = _sut.Resolve([], null, EmptyRecipient());

        result.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // BR-5: Missing dynamic field in recipient data — non-fatal
    // ----------------------------------------------------------------

    [Fact]
    public void Resolve_DynamicFieldMissingFromRecipientData_LogsWarningAndReturnsStatic()
    {
        var staticAttachment = BuildStaticAttachment("cover.pdf");
        var recipient = new Dictionary<string, object?>
        {
            ["email"] = "test@example.com"
            // "attachment_path" field is absent
        };

        var result = _sut.Resolve([staticAttachment], "attachment_path", recipient);

        // Static attachment still returned
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("cover.pdf");

        // Warning must be logged (BR-5)
        _loggerMock.Verify(
            l => l.LogWarning(
                It.IsAny<string>(),
                It.IsAny<object[]>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // BR-5: Dynamic field value empty — non-fatal
    // ----------------------------------------------------------------

    [Fact]
    public void Resolve_DynamicFieldValueNull_LogsWarningAndReturnsStatic()
    {
        var staticAttachment = BuildStaticAttachment("cover.pdf");
        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = null
        };

        var result = _sut.Resolve([staticAttachment], "attachment_path", recipient);

        result.Should().HaveCount(1);
        _loggerMock.Verify(
            l => l.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.Once);
    }

    [Fact]
    public void Resolve_DynamicFieldValueEmpty_LogsWarningAndReturnsStatic()
    {
        var staticAttachment = BuildStaticAttachment("cover.pdf");
        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = ""
        };

        var result = _sut.Resolve([staticAttachment], "attachment_path", recipient);

        result.Should().HaveCount(1);
        _loggerMock.Verify(
            l => l.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.Once);
    }

    [Fact]
    public void Resolve_DynamicFieldValueWhitespace_LogsWarningAndReturnsStatic()
    {
        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = "   "
        };

        var result = _sut.Resolve([], "attachment_path", recipient);

        result.Should().BeEmpty();
        _loggerMock.Verify(
            l => l.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // BR-5: File not found on disk — non-fatal
    // ----------------------------------------------------------------

    [Fact]
    public void Resolve_DynamicFileNotFoundOnDisk_LogsWarningAndReturnsStatic()
    {
        var staticAttachment = BuildStaticAttachment("cover.pdf");
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.pdf");

        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = nonExistentPath
        };

        var result = _sut.Resolve([staticAttachment], "attachment_path", recipient);

        // Static returned, dynamic skipped
        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("cover.pdf");

        // Warning logged for missing file (BR-5)
        _loggerMock.Verify(
            l => l.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.Once);
    }

    [Fact]
    public void Resolve_DynamicFileNotFound_DoesNotThrow()
    {
        var nonExistentPath = @"\\nonexistent\share\file.pdf";

        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = nonExistentPath
        };

        // Must not throw — send continues without the attachment
        var act = () => _sut.Resolve([], "attachment_path", recipient);
        act.Should().NotThrow();
    }

    // ----------------------------------------------------------------
    // Successful dynamic attachment resolution
    // ----------------------------------------------------------------

    [Fact]
    public void Resolve_DynamicFileExists_AddsToResult()
    {
        // Create a real temp file for the resolver to find
        var tempFile = CreateTempFile("test.pdf");

        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = tempFile
        };

        var result = _sut.Resolve([], "attachment_path", recipient);

        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be(tempFile);
        result[0].FileName.Should().Be(Path.GetFileName(tempFile));
    }

    [Fact]
    public void Resolve_StaticAndDynamicBothPresent_ReturnsBoth()
    {
        var staticAttachment = BuildStaticAttachment("static.pdf");
        var tempFile = CreateTempFile("dynamic.pdf");

        var recipient = new Dictionary<string, object?>
        {
            ["attachment_path"] = tempFile
        };

        var result = _sut.Resolve([staticAttachment], "attachment_path", recipient);

        result.Should().HaveCount(2);
        result.Should().Contain(a => a.FileName == "static.pdf");
        result.Should().Contain(a => a.FileName == Path.GetFileName(tempFile));
    }

    [Fact]
    public void Resolve_DynamicFileExists_LogsInformation()
    {
        var tempFile = CreateTempFile("receipt.pdf");
        var recipient = new Dictionary<string, object?> { ["doc_path"] = tempFile };

        _sut.Resolve([], "doc_path", recipient);

        _loggerMock.Verify(
            l => l.LogInformation(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Null / argument guards
    // ----------------------------------------------------------------

    [Fact]
    public void Resolve_NullRecipientData_ThrowsArgumentNullException()
    {
        var act = () => _sut.Resolve([], "field", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_NullStaticAttachments_ThrowsArgumentNullException()
    {
        var act = () => _sut.Resolve(null!, "field", EmptyRecipient());

        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static AttachmentInfo BuildStaticAttachment(string fileName)
    {
        return new AttachmentInfo
        {
            FileName = fileName,
            MimeType = "application/pdf",
            Data = new byte[] { 0x25, 0x50, 0x44, 0x46 } // %PDF magic bytes
        };
    }

    private static Dictionary<string, object?> EmptyRecipient()
        => new Dictionary<string, object?>();

    /// <summary>Creates a real temp file and registers it for cleanup.</summary>
    private string CreateTempFile(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{suffix}");
        File.WriteAllBytes(path, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x0A }); // minimal PDF
        _tempFiles.Add(path);
        return path;
    }
}

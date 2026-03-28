using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Attachments;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Attachments;

/// <summary>
/// Unit tests for AttachmentValidationService.
///
/// US-028 TASK-028-08: Attachment validation tests.
///
/// Covers:
///   - Extension whitelist enforcement (BR-1)
///   - Per-file size limit (BR-2: 10 MB)
///   - Total size limit (BR-3: 25 MB)
///   - Edge cases: empty file, empty name, boundary sizes
/// </summary>
public class AttachmentValidationServiceTests
{
    private readonly AttachmentValidationService _sut;

    private const long MaxFileSizeBytes  = 10 * 1024 * 1024; // 10 MB
    private const long MaxTotalSizeBytes = 25 * 1024 * 1024; // 25 MB

    public AttachmentValidationServiceTests()
    {
        var options = Options.Create(new AttachmentStorageOptions
        {
            AllowedExtensions  = [".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg"],
            MaxFileSizeBytes   = MaxFileSizeBytes,
            MaxTotalSizeBytes  = MaxTotalSizeBytes
        });

        _sut = new AttachmentValidationService(options);
    }

    // ----------------------------------------------------------------
    // ValidateFile — extension whitelist (BR-1)
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("document.pdf")]
    [InlineData("report.docx")]
    [InlineData("spreadsheet.xlsx")]
    [InlineData("image.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    public void ValidateFile_WithAllowedExtension_ReturnsValid(string fileName)
    {
        var result = _sut.ValidateFile(fileName, 1024);

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("script.exe")]
    [InlineData("archive.zip")]
    [InlineData("document.html")]
    [InlineData("data.csv")]
    [InlineData("image.gif")]
    [InlineData("noextension")]
    public void ValidateFile_WithDisallowedExtension_ReturnsInvalid(string fileName)
    {
        var result = _sut.ValidateFile(fileName, 1024);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateFile_WithUppercaseExtension_ReturnsValid()
    {
        // Extension check must be case-insensitive (BR-1)
        var result = _sut.ValidateFile("document.PDF", 1024);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateFile_WithMixedCaseExtension_ReturnsValid()
    {
        var result = _sut.ValidateFile("PHOTO.Jpg", 1024);

        result.IsValid.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // ValidateFile — per-file size limit (BR-2)
    // ----------------------------------------------------------------

    [Fact]
    public void ValidateFile_ExactlyAtSizeLimit_ReturnsValid()
    {
        var result = _sut.ValidateFile("document.pdf", MaxFileSizeBytes);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateFile_OneByteBelowSizeLimit_ReturnsValid()
    {
        var result = _sut.ValidateFile("document.pdf", MaxFileSizeBytes - 1);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateFile_OneByteOverSizeLimit_ReturnsInvalid()
    {
        var result = _sut.ValidateFile("document.pdf", MaxFileSizeBytes + 1);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("too large");
    }

    [Fact]
    public void ValidateFile_EmptyFile_ReturnsInvalid()
    {
        // 0-byte files should be rejected
        var result = _sut.ValidateFile("document.pdf", 0);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateFile_EmptyFileName_ReturnsInvalid()
    {
        var result = _sut.ValidateFile("", 1024);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateFile_WhitespaceFileName_ReturnsInvalid()
    {
        var result = _sut.ValidateFile("   ", 1024);

        result.IsValid.Should().BeFalse();
    }

    // ----------------------------------------------------------------
    // ValidateTotalSize — total size limit (BR-3)
    // ----------------------------------------------------------------

    [Fact]
    public void ValidateTotalSize_BelowLimit_ReturnsValid()
    {
        var result = _sut.ValidateTotalSize(MaxTotalSizeBytes - 1);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateTotalSize_ExactlyAtLimit_ReturnsValid()
    {
        var result = _sut.ValidateTotalSize(MaxTotalSizeBytes);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateTotalSize_OneByteOverLimit_ReturnsInvalid()
    {
        var result = _sut.ValidateTotalSize(MaxTotalSizeBytes + 1);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds the maximum");
    }

    [Fact]
    public void ValidateTotalSize_Zero_ReturnsValid()
    {
        // Zero total (no attachments) is always valid
        var result = _sut.ValidateTotalSize(0);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateTotalSize_LargeValue_ReturnsInvalid()
    {
        var result = _sut.ValidateTotalSize(100 * 1024 * 1024); // 100 MB

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("MB");
    }

    // ----------------------------------------------------------------
    // Error message content checks
    // ----------------------------------------------------------------

    [Fact]
    public void ValidateFile_DisallowedExtension_ErrorMessageListsAllowedTypes()
    {
        var result = _sut.ValidateFile("virus.exe", 1024);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain(".pdf");
    }

    [Fact]
    public void ValidateFile_TooLarge_ErrorMessageContainsFileSizeInfo()
    {
        var result = _sut.ValidateFile("large.pdf", MaxFileSizeBytes + 1);

        result.IsValid.Should().BeFalse();
        // Error message should mention MB sizes
        result.ErrorMessage.Should().Contain("MB");
    }

    // ----------------------------------------------------------------
    // AttachmentValidationResult static factories
    // ----------------------------------------------------------------

    [Fact]
    public void AttachmentValidationResult_Ok_IsValid()
    {
        var result = AttachmentValidationResult.Ok();

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void AttachmentValidationResult_Fail_IsNotValid()
    {
        var result = AttachmentValidationResult.Fail("some error");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("some error");
    }
}

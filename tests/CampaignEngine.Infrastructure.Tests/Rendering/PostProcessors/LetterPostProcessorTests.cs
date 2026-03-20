using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Rendering.PostProcessors;
using DinkToPdf.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using DinkToPdfContracts = DinkToPdf.Contracts;

namespace CampaignEngine.Infrastructure.Tests.Rendering.PostProcessors;

/// <summary>
/// Unit tests for LetterPostProcessor.
/// Uses a mock IConverter to avoid requiring the libwkhtmltox.dll native library.
/// Covers: successful conversion, error handling, empty input, size limit enforcement.
/// </summary>
public class LetterPostProcessorTests
{
    private readonly Mock<IConverter> _mockConverter;
    private readonly IOptions<LetterPostProcessorOptions> _defaultOptions;

    public LetterPostProcessorTests()
    {
        _mockConverter = new Mock<IConverter>();
        _defaultOptions = Options.Create(new LetterPostProcessorOptions());
    }

    private LetterPostProcessor CreateProcessor()
        => new(_mockConverter.Object, _defaultOptions, NullLogger<LetterPostProcessor>.Instance);

    // ----------------------------------------------------------------
    // Channel identifier
    // ----------------------------------------------------------------

    [Fact]
    public void Channel_ReturnsLetter()
    {
        CreateProcessor().Channel.Should().Be(ChannelType.Letter);
    }

    // ----------------------------------------------------------------
    // TASK-013-03: Successful PDF conversion
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ValidHtml_ReturnsPdfBytes()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF header
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns(pdfBytes);

        var result = await CreateProcessor().ProcessAsync("<html><body><p>Letter</p></body></html>");

        result.IsBinary.Should().BeTrue();
        result.BinaryContent.Should().Equal(pdfBytes);
        result.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task ProcessAsync_ValidHtml_CallsConverterOnce()
    {
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns(new byte[] { 1, 2, 3, 4 });

        await CreateProcessor().ProcessAsync("<p>Test</p>");

        _mockConverter.Verify(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithContext_PassesCampaignId()
    {
        var campaignId = Guid.NewGuid();
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns(new byte[] { 1, 2, 3, 4 });
        var context = new PostProcessingContext { CampaignId = campaignId };

        // Should not throw — context is used for logging only
        var result = await CreateProcessor().ProcessAsync("<p>Letter</p>", context);

        result.IsBinary.Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // Error handling: empty input
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_EmptyHtml_ThrowsPostProcessingException()
    {
        var act = async () => await CreateProcessor().ProcessAsync(string.Empty);

        await act.Should().ThrowAsync<PostProcessingException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task ProcessAsync_WhitespaceHtml_ThrowsPostProcessingException()
    {
        var act = async () => await CreateProcessor().ProcessAsync("   \n  ");

        await act.Should().ThrowAsync<PostProcessingException>();
    }

    // ----------------------------------------------------------------
    // Error handling: converter returns null/empty
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ConverterReturnsEmpty_ThrowsTransientException()
    {
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns(Array.Empty<byte>());

        var act = async () => await CreateProcessor().ProcessAsync("<p>Test</p>");

        var ex = await act.Should().ThrowAsync<PostProcessingException>();
        ex.Which.IsTransient.Should().BeTrue();
        ex.Which.Channel.Should().Be("Letter");
    }

    [Fact]
    public async Task ProcessAsync_ConverterReturnsNull_ThrowsTransientException()
    {
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns((byte[])null!);

        var act = async () => await CreateProcessor().ProcessAsync("<p>Test</p>");

        await act.Should().ThrowAsync<PostProcessingException>();
    }

    // ----------------------------------------------------------------
    // Error handling: converter throws exception
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_ConverterThrows_WrapsInPostProcessingException()
    {
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Throws(new InvalidOperationException("libwkhtmltox.dll not found"));

        var act = async () => await CreateProcessor().ProcessAsync("<p>Test</p>");

        var ex = await act.Should().ThrowAsync<PostProcessingException>();
        ex.Which.IsTransient.Should().BeTrue();
        ex.Which.Channel.Should().Be("Letter");
        ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    // ----------------------------------------------------------------
    // File size limit enforcement (BR-2: max 10 MB)
    // ----------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_PdfExceedsMaxSize_ThrowsPermanentException()
    {
        // Generate a byte array just over 10 MB
        var oversizedPdf = new byte[LetterPostProcessor.MaxFileSizeBytes + 1];
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns(oversizedPdf);

        var act = async () => await CreateProcessor().ProcessAsync("<p>Long document</p>");

        var ex = await act.Should().ThrowAsync<PostProcessingException>();
        ex.Which.IsTransient.Should().BeFalse(); // Permanent — content too large
        ex.Which.Channel.Should().Be("Letter");
    }

    [Fact]
    public async Task ProcessAsync_PdfAtExactMaxSize_Succeeds()
    {
        var exactSizePdf = new byte[LetterPostProcessor.MaxFileSizeBytes];
        _mockConverter
            .Setup(c => c.Convert(It.IsAny<DinkToPdf.Contracts.IDocument>()))
            .Returns(exactSizePdf);

        var result = await CreateProcessor().ProcessAsync("<p>Exactly max</p>");

        result.IsBinary.Should().BeTrue();
        result.BinaryContent!.Length.Should().Be(LetterPostProcessor.MaxFileSizeBytes);
    }

    // ----------------------------------------------------------------
    // Constants
    // ----------------------------------------------------------------

    [Fact]
    public void MaxFileSizeBytes_Is10MB()
    {
        LetterPostProcessor.MaxFileSizeBytes.Should().Be(10 * 1024 * 1024);
    }
}

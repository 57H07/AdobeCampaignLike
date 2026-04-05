using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// API integration tests for the DOCX download endpoint (US-008, F-110).
///
/// Tests target the TemplatesController directly with mocked service dependencies,
/// covering:
///   - TASK-008-05: GET /api/templates/{id}/docx — success path
///   - TASK-008-06: GET /api/templates/{id}/docx — 404 (not found) and 422 (not Letter) responses
/// </summary>
public class DocxDownloadApiTests
{
    private readonly Mock<ITemplateService> _templateServiceMock;
    private readonly TemplatesController _controller;

    public DocxDownloadApiTests()
    {
        _templateServiceMock = new Mock<ITemplateService>();

        var docxParserMock = new Mock<IDocxPlaceholderParserService>();
        docxParserMock
            .Setup(p => p.GetUndeclaredPlaceholders(
                It.IsAny<Stream>(),
                It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(Array.Empty<string>());

        _controller = new TemplatesController(
            _templateServiceMock.Object,
            new Mock<IPlaceholderManifestService>().Object,
            new Mock<IPlaceholderParserService>().Object,
            docxParserMock.Object,
            new Mock<ISubTemplateResolverService>().Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<ITemplatePreviewService>().Object,
            new Mock<ITemplateBodyStore>().Object);

        // Provide a default HTTP context so Response.Headers is accessible
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // ================================================================
    // TASK-008-05: Success path
    // ================================================================

    [Fact]
    public async Task DownloadDocx_LetterTemplate_Returns200WithDocxStream()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 };
        var docxStream = new MemoryStream(docxBytes);

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((docxStream, "Invoice Letter"));

        // Act
        var result = await _controller.DownloadDocx(templateId);

        // Assert — must be a FileStreamResult
        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should()
            .Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        fileResult.FileStream.Should().NotBeNull();
        fileResult.FileStream.Should().BeSameAs(docxStream);
    }

    [Fact]
    public async Task DownloadDocx_SetsContentDispositionHeaderWithTemplateName()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var docxStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((docxStream, "My Invoice Template"));

        // Act
        await _controller.DownloadDocx(templateId);

        // Assert — Content-Disposition header set with template name and .docx extension
        var contentDisposition = _controller.Response.Headers["Content-Disposition"].ToString();
        contentDisposition.Should().Contain("attachment");
        contentDisposition.Should().Contain("My Invoice Template.docx");
    }

    [Fact]
    public async Task DownloadDocx_CallsGetDocxBodyAsyncWithCorrectId()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var docxStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((docxStream, "Test Template"));

        // Act
        await _controller.DownloadDocx(templateId);

        // Assert — service was called with the exact ID
        _templateServiceMock.Verify(
            s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DownloadDocx_TemplateNameWithQuotes_SanitisesFilename()
    {
        // Arrange — template name contains a double-quote (potentially unsafe in header)
        var templateId = Guid.NewGuid();
        var docxStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((docxStream, "Invoice \"Special\" Letter"));

        // Act
        await _controller.DownloadDocx(templateId);

        // Assert — quotes are replaced (not passed through to header value)
        var contentDisposition = _controller.Response.Headers["Content-Disposition"].ToString();
        contentDisposition.Should().NotContain("\"Special\"");
    }

    // ================================================================
    // TASK-008-06: 404 / 422 error responses
    // ================================================================

    [Fact]
    public async Task DownloadDocx_TemplateNotFound_ThrowsNotFoundException()
    {
        // Arrange — service throws NotFoundException (caught by GlobalExceptionMiddleware → 404)
        var missingId = Guid.NewGuid();

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(missingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Template", missingId));

        // Act
        Func<Task> act = async () => await _controller.DownloadDocx(missingId);

        // Assert — NotFoundException propagates (middleware maps it to HTTP 404)
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage($"*{missingId}*");
    }

    [Fact]
    public async Task DownloadDocx_NonLetterTemplate_ThrowsDomainException()
    {
        // Arrange — service throws DomainException (caught by GlobalExceptionMiddleware → 422)
        var templateId = Guid.NewGuid();

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(
                $"Template '{templateId}' is a Email template. DOCX download is only available for Letter templates."));

        // Act
        Func<Task> act = async () => await _controller.DownloadDocx(templateId);

        // Assert — DomainException propagates (middleware maps it to HTTP 422)
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*Email template*Letter*");
    }

    [Fact]
    public async Task DownloadDocx_TemplateHasNoDocxAttached_ThrowsDomainException()
    {
        // Arrange — service throws DomainException when BodyPath is empty
        var templateId = Guid.NewGuid();

        _templateServiceMock
            .Setup(s => s.GetDocxBodyAsync(templateId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(
                $"Template '{templateId}' does not have a DOCX file attached."));

        // Act
        Func<Task> act = async () => await _controller.DownloadDocx(templateId);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*does not have a DOCX file*");
    }
}

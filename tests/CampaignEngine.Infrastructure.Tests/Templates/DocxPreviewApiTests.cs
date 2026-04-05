using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using ValidationException = CampaignEngine.Domain.Exceptions.ValidationException;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// API integration tests for POST /api/templates/{id}/preview/docx (US-020, F-401).
///
/// Tests target TemplatesController directly with mocked service dependencies, covering:
///   - TASK-020-01: success path — 200 OK + FileContentResult with DOCX bytes
///   - TASK-020-04: response headers — MIME type and Content-Disposition
///   - Error cases: 404 (template not found), 422 (non-Letter template)
/// </summary>
public class DocxPreviewApiTests
{
    private const string DocxMimeType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly Mock<ITemplatePreviewService> _previewServiceMock;
    private readonly TemplatesController _controller;

    public DocxPreviewApiTests()
    {
        _previewServiceMock = new Mock<ITemplatePreviewService>();

        _controller = new TemplatesController(
            new Mock<ITemplateService>().Object,
            new Mock<IPlaceholderManifestService>().Object,
            new Mock<IPlaceholderParserService>().Object,
            new Mock<IDocxPlaceholderParserService>().Object,
            new Mock<ISubTemplateResolverService>().Object,
            new Mock<ICurrentUserService>().Object,
            _previewServiceMock.Object,
            new Mock<ITemplateBodyStore>().Object);

        // Provide a default HttpContext so Response.Headers is always available.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };
    }

    // ----------------------------------------------------------------
    // Success path
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewDocxTemplate_ValidRequest_Returns200WithDocxBytes()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01, 0x02 }; // minimal PK header
        var request = new DocxPreviewRequest
        {
            Scalars = new Dictionary<string, string> { { "firstName", "Marc" } }
        };

        _previewServiceMock
            .Setup(s => s.PreviewDocxAsync(templateId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocxPreviewResult
            {
                TemplateId = templateId,
                TemplateName = "Invoice Letter",
                DocxBytes = docxBytes
            });

        // Act
        var actionResult = await _controller.PreviewDocxTemplate(templateId, request);

        // Assert — returns FileContentResult with DOCX bytes
        var fileResult = actionResult.Should().BeOfType<FileContentResult>().Subject;
        fileResult.FileContents.Should().Equal(docxBytes);
        fileResult.ContentType.Should().Be(DocxMimeType);
    }

    [Fact]
    public async Task PreviewDocxTemplate_ValidRequest_SetsContentDispositionHeader()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var request = new DocxPreviewRequest();

        _previewServiceMock
            .Setup(s => s.PreviewDocxAsync(templateId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocxPreviewResult
            {
                TemplateId = templateId,
                TemplateName = "Invoice Letter",
                DocxBytes = new byte[] { 0x50, 0x4B }
            });

        // Provide a minimal HttpContext so Response.Headers is available.
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        // Act
        await _controller.PreviewDocxTemplate(templateId, request);

        // Assert — Content-Disposition header contains the sanitised template name
        var disposition = _controller.Response.Headers["Content-Disposition"].ToString();
        disposition.Should().Contain("attachment");
        disposition.Should().Contain("preview-Invoice_Letter.docx");
    }

    [Fact]
    public async Task PreviewDocxTemplate_TemplateNameWithSpaces_SanitisesFilename()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var request = new DocxPreviewRequest();

        _previewServiceMock
            .Setup(s => s.PreviewDocxAsync(templateId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocxPreviewResult
            {
                TemplateId = templateId,
                TemplateName = "My Letter Template 2024",
                DocxBytes = new byte[] { 0x50, 0x4B }
            });

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        // Act
        await _controller.PreviewDocxTemplate(templateId, request);

        // Assert — spaces in the template name are replaced with underscores in the filename
        var disposition = _controller.Response.Headers["Content-Disposition"].ToString();
        disposition.Should().Contain("preview-My_Letter_Template_2024.docx");
        // The filename portion must not contain spaces (the separator "; " is acceptable)
        var filenamePart = disposition.Split('"').FirstOrDefault(p => p.StartsWith("preview-"));
        filenamePart.Should().NotBeNull().And.NotContain(" ");
    }

    // ----------------------------------------------------------------
    // Error cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task PreviewDocxTemplate_TemplateNotFound_Returns404()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        var request = new DocxPreviewRequest();

        _previewServiceMock
            .Setup(s => s.PreviewDocxAsync(missingId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Template", missingId));

        // Act
        var actionResult = await _controller.PreviewDocxTemplate(missingId, request);

        // Assert
        actionResult.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task PreviewDocxTemplate_NonLetterTemplate_Returns422()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var request = new DocxPreviewRequest();

        _previewServiceMock
            .Setup(s => s.PreviewDocxAsync(templateId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(
                $"Template '{templateId}' is a Email template. " +
                "DOCX preview is only available for Letter channel templates."));

        // Act
        var actionResult = await _controller.PreviewDocxTemplate(templateId, request);

        // Assert — 422 Unprocessable Entity
        var unprocessableResult = actionResult.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        unprocessableResult.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task PreviewDocxTemplate_ServicePassesCorrectSampleData()
    {
        // Arrange — verify the controller passes the request verbatim to the service
        var templateId = Guid.NewGuid();
        DocxPreviewRequest? capturedRequest = null;

        var request = new DocxPreviewRequest
        {
            Scalars = new Dictionary<string, string>
            {
                { "firstName", "Marc" },
                { "lastName", "Dupont" }
            },
            Collections = new Dictionary<string, List<Dictionary<string, string>>>
            {
                {
                    "orders", new List<Dictionary<string, string>>
                    {
                        new() { { "item", "Widget" }, { "qty", "3" } }
                    }
                }
            },
            Conditions = new Dictionary<string, bool>
            {
                { "hasDiscount", true }
            }
        };

        _previewServiceMock
            .Setup(s => s.PreviewDocxAsync(
                templateId,
                It.IsAny<DocxPreviewRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, DocxPreviewRequest, CancellationToken>(
                (_, req, _) => capturedRequest = req)
            .ReturnsAsync(new DocxPreviewResult
            {
                TemplateId = templateId,
                TemplateName = "Test",
                DocxBytes = new byte[] { 0x50, 0x4B }
            });

        // Act
        await _controller.PreviewDocxTemplate(templateId, request);

        // Assert — service received the full request object
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Scalars.Should().ContainKey("firstName").WhoseValue.Should().Be("Marc");
        capturedRequest.Collections.Should().ContainKey("orders");
        capturedRequest.Conditions.Should().ContainKey("hasDiscount").WhoseValue.Should().BeTrue();
    }
}

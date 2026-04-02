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
using ValidationException = CampaignEngine.Domain.Exceptions.ValidationException;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// API integration tests for the Letter template upload endpoints (US-011, F-205).
///
/// Tests target the TemplatesController directly with mocked service dependencies,
/// covering:
///   - TASK-011-07: POST /api/templates/letter — create success path
///   - TASK-011-08: PUT /api/templates/{id}/letter — update success path
///   - TASK-011-09: Error cases (409 name conflict, 404 not found, 422 channel mismatch,
///                   400 validation, 413 file too large)
/// </summary>
public class LetterUploadApiTests
{
    private readonly Mock<ITemplateService> _templateServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly TemplatesController _controller;

    // A minimal valid DOCX magic-bytes stream (PK header)
    private static MemoryStream MinimalDocxStream() =>
        new(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

    public LetterUploadApiTests()
    {
        _templateServiceMock = new Mock<ITemplateService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _currentUserServiceMock.Setup(s => s.UserName).Returns("designer@test.com");

        // US-018: IDocxPlaceholderParserService returns no warnings by default in legacy tests.
        var docxParserMock = new Mock<IDocxPlaceholderParserService>();
        docxParserMock
            .Setup(p => p.GetUndeclaredPlaceholders(It.IsAny<Stream>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(Array.Empty<string>());

        _controller = new TemplatesController(
            _templateServiceMock.Object,
            new Mock<IPlaceholderManifestService>().Object,
            new Mock<IPlaceholderParserService>().Object,
            docxParserMock.Object,
            new Mock<ISubTemplateResolverService>().Object,
            _currentUserServiceMock.Object,
            new Mock<ITemplatePreviewService>().Object,
            new Mock<ITemplateBodyStore>().Object);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static IFormFile MakeFormFile(string fileName, long sizeBytes = 512)
    {
        var formFile = new Mock<IFormFile>();
        formFile.Setup(f => f.FileName).Returns(fileName);
        formFile.Setup(f => f.Length).Returns(sizeBytes);
        formFile.Setup(f => f.OpenReadStream()).Returns(MinimalDocxStream);
        // US-018: controller now uses CopyToAsync instead of OpenReadStream for DOCX buffering.
        formFile
            .Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>(async (target, ct) =>
            {
                var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
                await target.WriteAsync(bytes, ct);
            });
        return formFile.Object;
    }

    private static Template MakeLetterTemplate(
        Guid? id = null,
        string name = "My Letter",
        int version = 1,
        string bodyPath = "templates/abc/v1.docx")
    {
        return new Template
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Channel = ChannelType.Letter,
            BodyPath = bodyPath,
            BodyChecksum = null,
            Status = TemplateStatus.Draft,
            Version = version,
            IsSubTemplate = false,
            Description = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // ================================================================
    // TASK-011-07: POST /api/templates/letter — create success path
    // ================================================================

    [Fact]
    public async Task CreateLetterTemplate_ValidRequest_Returns201WithDto()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var template = MakeLetterTemplate(templateId, "Invoice Letter", 1, $"templates/{templateId}/v1.docx");

        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var formFile = MakeFormFile("invoice.docx");

        // Act
        var result = await _controller.CreateLetterTemplate(
            name: "Invoice Letter",
            file: formFile,
            description: "Annual invoice template");

        // Assert — HTTP 201
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);

        var dto = createdResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.Id.Should().Be(templateId);
        dto.Name.Should().Be("Invoice Letter");
        dto.Channel.Should().Be("Letter");
        dto.Status.Should().Be("Draft");
        dto.Version.Should().Be(1);
    }

    [Fact]
    public async Task CreateLetterTemplate_ValidRequest_DtoBodyPathIsRelative()
    {
        // Arrange — service returns a template with a conventional relative path
        var templateId = Guid.NewGuid();
        var relativePath = $"templates/{templateId}/v1.docx";
        var template = MakeLetterTemplate(templateId, bodyPath: relativePath);

        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var formFile = MakeFormFile("letter.docx");

        // Act
        var result = await _controller.CreateLetterTemplate("My Letter", formFile);

        // Assert — BodyPath must not start with a drive letter or UNC prefix
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = createdResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.BodyPath.Should().StartWith("templates/");
        dto.BodyPath.Should().NotContain(":\\");   // no Windows absolute path
        dto.BodyPath.Should().NotStartWith("/");   // no Unix absolute path
        dto.BodyPath.Should().NotStartWith("\\\\"); // no UNC path
    }

    [Fact]
    public async Task CreateLetterTemplate_ServiceReceivesDocxContentStream()
    {
        // Arrange — verify the controller passes a non-null DocxContent stream
        CreateTemplateRequest? capturedRequest = null;

        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTemplateRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(MakeLetterTemplate());

        var formFile = MakeFormFile("template.docx");

        // Act
        await _controller.CreateLetterTemplate("Test Letter", formFile);

        // Assert — DocxContent was set and BodyPath was left empty (service will derive it)
        capturedRequest.Should().NotBeNull();
        capturedRequest!.DocxContent.Should().NotBeNull();
        capturedRequest.Channel.Should().Be(ChannelType.Letter);
    }

    [Fact]
    public async Task CreateLetterTemplate_ServiceReceivesFileSizeBytes()
    {
        // Arrange
        const long expectedSize = 1_024;
        CreateTemplateRequest? capturedRequest = null;

        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateTemplateRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(MakeLetterTemplate());

        var formFile = MakeFormFile("template.docx", sizeBytes: expectedSize);

        // Act
        await _controller.CreateLetterTemplate("Test Letter", formFile);

        // Assert
        capturedRequest!.FileSizeBytes.Should().Be(expectedSize);
    }

    [Fact]
    public async Task CreateLetterTemplate_MissingFile_Returns400()
    {
        // Act — pass null for file
        var result = await _controller.CreateLetterTemplate(
            name: "Test Letter",
            file: null!);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateLetterTemplate_EmptyName_Returns400()
    {
        // Act
        var result = await _controller.CreateLetterTemplate(
            name: "   ",
            file: MakeFormFile("test.docx"));

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateLetterTemplate_NameExceeds200Chars_Returns400()
    {
        // Arrange — name with 201 characters
        var longName = new string('A', 201);

        // Act
        var result = await _controller.CreateLetterTemplate(
            name: longName,
            file: MakeFormFile("test.docx"));

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateLetterTemplate_DescriptionExceeds500Chars_Returns400()
    {
        // Arrange
        var longDesc = new string('X', 501);

        // Act
        var result = await _controller.CreateLetterTemplate(
            name: "Valid Name",
            file: MakeFormFile("test.docx"),
            description: longDesc);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CreateLetterTemplate_FileTooLarge_Returns413()
    {
        // Arrange — 1 byte over 10 MB limit
        const long overLimit = 10_485_761;
        var formFile = MakeFormFile("big.docx", overLimit);

        // Act
        var result = await _controller.CreateLetterTemplate(
            name: "Big File",
            file: formFile);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status413RequestEntityTooLarge);
    }

    // ================================================================
    // TASK-011-08: PUT /api/templates/{id}/letter — update success path
    // ================================================================

    [Fact]
    public async Task UpdateLetterTemplate_ValidRequestWithFile_Returns200WithDto()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var existing = MakeLetterTemplate(templateId, "Old Name", 1, $"templates/{templateId}/v1.docx");
        var updated = MakeLetterTemplate(templateId, "New Name", 2, $"templates/{templateId}/v2.docx");

        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var formFile = MakeFormFile("updated.docx");

        // Act
        var result = await _controller.UpdateLetterTemplate(
            id: templateId,
            name: "New Name",
            file: formFile,
            description: "Updated description");

        // Assert — HTTP 200
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var dto = okResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.Id.Should().Be(templateId);
        dto.Name.Should().Be("New Name");
        dto.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateLetterTemplate_ValidRequestWithoutFile_Returns200_RetainsExistingPath()
    {
        // Arrange — no file supplied; existing DOCX should be retained (business rule 2)
        var templateId = Guid.NewGuid();
        var existingPath = $"templates/{templateId}/v1.docx";
        var existing = MakeLetterTemplate(templateId, "My Letter", 1, existingPath);
        var updated = MakeLetterTemplate(templateId, "Renamed Letter", 2, existingPath);

        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        UpdateTemplateRequest? capturedRequest = null;
        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateTemplateRequest, CancellationToken>((_, req, _) => capturedRequest = req)
            .ReturnsAsync(updated);

        // Act — no file argument
        var result = await _controller.UpdateLetterTemplate(
            id: templateId,
            name: "Renamed Letter",
            file: null,
            description: null);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>()
            .Which.StatusCode.Should().Be(200);

        // The request passed to UpdateAsync must retain the existing BodyPath
        capturedRequest!.BodyPath.Should().Be(existingPath);
        capturedRequest.DocxContent.Should().BeNull();
    }

    [Fact]
    public async Task UpdateLetterTemplate_WithFile_ServiceReceivesDocxContentStream()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var existing = MakeLetterTemplate(templateId);
        var updated = MakeLetterTemplate(templateId, version: 2);

        _templateServiceMock.Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        UpdateTemplateRequest? capturedRequest = null;
        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateTemplateRequest, CancellationToken>((_, req, _) => capturedRequest = req)
            .ReturnsAsync(updated);

        var formFile = MakeFormFile("new-version.docx");

        // Act
        await _controller.UpdateLetterTemplate(templateId, "My Letter", formFile);

        // Assert — DocxContent stream was passed to the service
        capturedRequest!.DocxContent.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateLetterTemplate_ChangedBySetFromCurrentUser()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        _templateServiceMock.Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeLetterTemplate(templateId));

        UpdateTemplateRequest? capturedRequest = null;
        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateTemplateRequest, CancellationToken>((_, req, _) => capturedRequest = req)
            .ReturnsAsync(MakeLetterTemplate(templateId));

        // Act
        await _controller.UpdateLetterTemplate(templateId, "My Letter");

        // Assert — ChangedBy populated from ICurrentUserService
        capturedRequest!.ChangedBy.Should().Be("designer@test.com");
    }

    // ================================================================
    // TASK-011-09: Error cases (409, 404, 422, 400, 413)
    // ================================================================

    // ----------------------------------------------------------------
    // HTTP 409 — name collision on create
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateLetterTemplate_NameCollision_Returns409()
    {
        // Arrange — service throws ValidationException with "already exists" message
        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(
                "A template named 'Invoice' already exists for channel 'Letter'."));

        var formFile = MakeFormFile("invoice.docx");

        // Act
        var result = await _controller.CreateLetterTemplate("Invoice", formFile);

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>()
            .Which.StatusCode.Should().Be(409);
    }

    // ----------------------------------------------------------------
    // HTTP 409 — name collision on update
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateLetterTemplate_NameCollision_Returns409()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeLetterTemplate(templateId));

        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException(
                "A template named 'Invoice' already exists for channel 'Letter'."));

        // Act
        var result = await _controller.UpdateLetterTemplate(templateId, "Invoice");

        // Assert
        result.Result.Should().BeOfType<ConflictObjectResult>()
            .Which.StatusCode.Should().Be(409);
    }

    // ----------------------------------------------------------------
    // HTTP 404 — template not found on update
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateLetterTemplate_TemplateNotFound_Returns404()
    {
        // Arrange — GetByIdAsync returns null (not found)
        var missingId = Guid.NewGuid();
        _templateServiceMock
            .Setup(s => s.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Template?)null);

        // Act
        var result = await _controller.UpdateLetterTemplate(missingId, "Some Name");

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>()
            .Which.StatusCode.Should().Be(404);
    }

    // ----------------------------------------------------------------
    // HTTP 422 — channel mismatch on update (non-Letter template)
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateLetterTemplate_EmailChannelTemplate_Returns422()
    {
        // Arrange — existing template is an Email template, not Letter
        var templateId = Guid.NewGuid();
        var emailTemplate = new Template
        {
            Id = templateId,
            Name = "Email Newsletter",
            Channel = ChannelType.Email,
            BodyPath = $"templates/{templateId}/v1.html",
            Status = TemplateStatus.Draft,
            Version = 1
        };

        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(emailTemplate);

        // Act
        var result = await _controller.UpdateLetterTemplate(templateId, "Email Newsletter");

        // Assert
        var objectResult = result.Result.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task UpdateLetterTemplate_SmsChannelTemplate_Returns422()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var smsTemplate = new Template
        {
            Id = templateId,
            Name = "SMS Alert",
            Channel = ChannelType.Sms,
            BodyPath = $"templates/{templateId}/v1.txt",
            Status = TemplateStatus.Draft,
            Version = 1
        };

        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(smsTemplate);

        // Act
        var result = await _controller.UpdateLetterTemplate(templateId, "SMS Alert");

        // Assert
        result.Result.Should().BeOfType<UnprocessableEntityObjectResult>()
            .Which.StatusCode.Should().Be(422);
    }

    // ----------------------------------------------------------------
    // HTTP 400 — input validation on update
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateLetterTemplate_EmptyName_Returns400()
    {
        // Act
        var result = await _controller.UpdateLetterTemplate(Guid.NewGuid(), "  ");

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UpdateLetterTemplate_NameExceeds200Chars_Returns400()
    {
        // Arrange
        var longName = new string('B', 201);

        // Act
        var result = await _controller.UpdateLetterTemplate(Guid.NewGuid(), longName);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.StatusCode.Should().Be(400);
    }

    // ----------------------------------------------------------------
    // HTTP 413 — file too large on update
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateLetterTemplate_FileTooLarge_Returns413()
    {
        // Arrange — file 1 byte over 10 MB limit
        var templateId = Guid.NewGuid();
        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeLetterTemplate(templateId));

        var oversizedFile = MakeFormFile("oversized.docx", sizeBytes: 10_485_761);

        // Act
        var result = await _controller.UpdateLetterTemplate(
            id: templateId,
            name: "My Letter",
            file: oversizedFile);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status413RequestEntityTooLarge);
    }

    // ----------------------------------------------------------------
    // Authorization — CampaignManager excluded (TASK-011-03)
    // This test verifies the policy name on the action attributes
    // rather than exercising ASP.NET Core's authorization pipeline.
    // ----------------------------------------------------------------

    [Fact]
    public void CreateLetterTemplate_Action_HasRequireDesignerOrAdminPolicy()
    {
        // Arrange
        var method = typeof(TemplatesController).GetMethod(nameof(TemplatesController.CreateLetterTemplate));

        // Act
        var authorizeAttributes = method!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .ToList();

        // Assert — policy must be RequireDesignerOrAdmin (CampaignManager excluded)
        authorizeAttributes.Should().ContainSingle(a => a.Policy == AuthorizationPolicies.RequireDesignerOrAdmin);
    }

    [Fact]
    public void UpdateLetterTemplate_Action_HasRequireDesignerOrAdminPolicy()
    {
        // Arrange
        var method = typeof(TemplatesController).GetMethod(nameof(TemplatesController.UpdateLetterTemplate));

        // Act
        var authorizeAttributes = method!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .ToList();

        // Assert
        authorizeAttributes.Should().ContainSingle(a => a.Policy == AuthorizationPolicies.RequireDesignerOrAdmin);
    }

    // ================================================================
    // US-018: Manifest validation warnings (TASK-018-05)
    // ================================================================

    /// <summary>
    /// Builds a controller that uses the supplied IDocxPlaceholderParserService mock.
    /// IPlaceholderManifestService returns an empty manifest by default (new template).
    /// </summary>
    private TemplatesController MakeControllerWithDocxParser(
        Mock<IDocxPlaceholderParserService> docxParserMock,
        Mock<IPlaceholderManifestService>? manifestMock = null)
    {
        manifestMock ??= new Mock<IPlaceholderManifestService>();
        manifestMock
            .Setup(m => m.GetByTemplateIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaceholderManifestEntryDto>());

        return new TemplatesController(
            _templateServiceMock.Object,
            manifestMock.Object,
            new Mock<IPlaceholderParserService>().Object,
            docxParserMock.Object,
            new Mock<ISubTemplateResolverService>().Object,
            _currentUserServiceMock.Object,
            new Mock<ITemplatePreviewService>().Object,
            new Mock<ITemplateBodyStore>().Object);
    }

    [Fact]
    public async Task CreateLetterTemplate_UndeclaredPlaceholders_WarningsIncludedInDto()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var template = MakeLetterTemplate(templateId);
        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var expectedWarnings = new[] { "invoiceDate", "customerAddress" };
        var docxParserMock = new Mock<IDocxPlaceholderParserService>();
        docxParserMock
            .Setup(p => p.GetUndeclaredPlaceholders(It.IsAny<Stream>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(expectedWarnings);

        var controller = MakeControllerWithDocxParser(docxParserMock);

        // Act
        var result = await controller.CreateLetterTemplate(
            name: "Invoice Letter",
            file: MakeFormFile("invoice.docx"));

        // Assert — HTTP 201 with warnings in DTO
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = createdResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.Warnings.Should().BeEquivalentTo(expectedWarnings);
    }

    [Fact]
    public async Task CreateLetterTemplate_AllPlaceholdersDeclared_EmptyWarnings()
    {
        // Arrange — parser finds no undeclared keys
        var template = MakeLetterTemplate();
        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var docxParserMock = new Mock<IDocxPlaceholderParserService>();
        docxParserMock
            .Setup(p => p.GetUndeclaredPlaceholders(It.IsAny<Stream>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(Array.Empty<string>());

        var controller = MakeControllerWithDocxParser(docxParserMock);

        // Act
        var result = await controller.CreateLetterTemplate("My Letter", MakeFormFile("letter.docx"));

        // Assert — upload succeeds even though no warnings (non-blocking confirmed by HTTP 201)
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var dto = createdResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateLetterTemplate_WithWarnings_StillReturns201()
    {
        // Arrange — F-307: upload must succeed even when warnings are present
        var template = MakeLetterTemplate();
        _templateServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var docxParserMock = new Mock<IDocxPlaceholderParserService>();
        docxParserMock
            .Setup(p => p.GetUndeclaredPlaceholders(It.IsAny<Stream>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(new[] { "undeclaredKey" });

        var controller = MakeControllerWithDocxParser(docxParserMock);

        // Act
        var result = await controller.CreateLetterTemplate("My Letter", MakeFormFile("letter.docx"));

        // Assert — non-blocking: HTTP 201, not 400/422
        result.Result.Should().BeOfType<CreatedAtActionResult>()
            .Which.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task UpdateLetterTemplate_NewDocxUploaded_WarningsPopulated()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var existingTemplate = MakeLetterTemplate(templateId);
        var updatedTemplate = MakeLetterTemplate(templateId, version: 2);

        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);
        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedTemplate);

        var expectedWarnings = new[] { "newField" };
        var docxParserMock = new Mock<IDocxPlaceholderParserService>();
        docxParserMock
            .Setup(p => p.GetUndeclaredPlaceholders(It.IsAny<Stream>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(expectedWarnings);

        var controller = MakeControllerWithDocxParser(docxParserMock);

        // Act
        var result = await controller.UpdateLetterTemplate(
            id: templateId,
            name: "Updated Letter",
            file: MakeFormFile("updated.docx"));

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.Warnings.Should().BeEquivalentTo(expectedWarnings);
    }

    [Fact]
    public async Task UpdateLetterTemplate_NoNewDocx_WarningsEmpty()
    {
        // Arrange — update with metadata only (no DOCX file) → no validation runs
        var templateId = Guid.NewGuid();
        var existingTemplate = MakeLetterTemplate(templateId);
        var updatedTemplate = MakeLetterTemplate(templateId, version: 2);

        _templateServiceMock
            .Setup(s => s.GetByIdAsync(templateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);
        _templateServiceMock
            .Setup(s => s.UpdateAsync(templateId, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedTemplate);

        var docxParserMock = new Mock<IDocxPlaceholderParserService>();

        var controller = MakeControllerWithDocxParser(docxParserMock);

        // Act — no file supplied
        var result = await controller.UpdateLetterTemplate(
            id: templateId,
            name: "Renamed Letter",
            file: null);

        // Assert — no validation ran, warnings must be empty
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<TemplateDto>().Subject;
        dto.Warnings.Should().BeEmpty();

        // Verify parser was never called
        docxParserMock.Verify(
            p => p.GetUndeclaredPlaceholders(It.IsAny<Stream>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()),
            Times.Never);
    }
}

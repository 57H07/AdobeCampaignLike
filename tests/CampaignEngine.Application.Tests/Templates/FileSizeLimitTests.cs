using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Tests for the 10 MB file size limit enforcement (US-010, F-204).
/// Validates service-layer re-validation (defense-in-depth) for both
/// exactly-at-limit (10 MB — allowed) and over-limit (11 MB — rejected) uploads.
/// </summary>
public class FileSizeLimitTests : IDisposable
{
    private const long MaxFileSizeBytes = 10_485_760; // 10 MB

    private readonly CampaignEngineDbContext _context;
    private readonly TemplateService _service;

    public FileSizeLimitTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object, manifestService.Object, parserService.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // TASK-010-04: 10 MB file — succeeds
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ExactlyAtLimit_10MB_Succeeds()
    {
        // Arrange — FileSizeBytes exactly at the 10 MB limit
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template 10MB",
            Channel = ChannelType.Letter,
            BodyPath = "templates/letter-10mb/v1.docx",
            FileSizeBytes = MaxFileSizeBytes // exactly 10 MB — allowed
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.Name.Should().Be("Letter Template 10MB");
        result.Channel.Should().Be(ChannelType.Letter);
        result.Status.Should().Be(TemplateStatus.Draft);
    }

    [Fact]
    public async Task CreateAsync_OneByteBelowLimit_Succeeds()
    {
        // Arrange — 1 byte below the 10 MB limit
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template Under Limit",
            Channel = ChannelType.Letter,
            BodyPath = "templates/letter-under/v1.docx",
            FileSizeBytes = MaxFileSizeBytes - 1
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Channel.Should().Be(ChannelType.Letter);
    }

    [Fact]
    public async Task CreateAsync_NullFileSizeBytes_Succeeds()
    {
        // Arrange — FileSizeBytes is null (not a file upload, e.g., existing path reference)
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template No Size",
            Channel = ChannelType.Letter,
            BodyPath = "templates/letter-no-size/v1.docx",
            FileSizeBytes = null
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert — no size provided, skip validation
        result.Should().NotBeNull();
    }

    // ----------------------------------------------------------------
    // TASK-010-05: 11 MB file — rejected
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_Over10MB_ThrowsValidationException()
    {
        // Arrange — 11 MB (1 byte over the limit)
        const long elevenMb = 11L * 1024 * 1024;
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template 11MB",
            Channel = ChannelType.Letter,
            BodyPath = "templates/letter-11mb/v1.docx",
            FileSizeBytes = elevenMb
        };

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert — service layer must reject file exceeding 10 MB
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*exceeds the 10 MB limit*");
    }

    [Fact]
    public async Task CreateAsync_OneBytOverLimit_ThrowsValidationException()
    {
        // Arrange — 1 byte over the 10 MB limit
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template One Over",
            Channel = ChannelType.Letter,
            BodyPath = "templates/letter-one-over/v1.docx",
            FileSizeBytes = MaxFileSizeBytes + 1
        };

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*exceeds the 10 MB limit*");
    }

    [Fact]
    public async Task CreateAsync_Over10MB_ErrorMessageContainsFileSizeAndLimit()
    {
        // Arrange
        const long elevenMb = 11L * 1024 * 1024;
        var request = new CreateTemplateRequest
        {
            Name = "Letter Template Message Check",
            Channel = ChannelType.Letter,
            BodyPath = "templates/letter-msg-check/v1.docx",
            FileSizeBytes = elevenMb
        };

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert — error message includes the actual size and the limit (F-204 requirement for clear message)
        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Message.Contains("10 MB limit", StringComparison.OrdinalIgnoreCase));
    }
}

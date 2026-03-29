using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Tests for the Template lifecycle: Draft → Published → Archived.
/// Validates status transition rules, manifest completeness enforcement,
/// and audit logging behavior.
/// </summary>
public class TemplateLifecycleTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly TemplateService _service;
    private readonly Mock<IPlaceholderManifestService> _manifestServiceMock;
    private readonly Mock<IPlaceholderParserService> _parserServiceMock;

    public TemplateLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var logger = new Mock<IAppLogger<TemplateService>>();
        _manifestServiceMock = new Mock<IPlaceholderManifestService>();
        _parserServiceMock = new Mock<IPlaceholderParserService>();
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        _service = new TemplateService(
            templateRepository,
            unitOfWork,
            logger.Object,
            _manifestServiceMock.Object,
            _parserServiceMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // New template defaults
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_NewTemplate_StartsAsDraft()
    {
        // Arrange
        var request = new CreateTemplateRequest
        {
            Name = "New Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Status.Should().Be(TemplateStatus.Draft);
    }

    // ----------------------------------------------------------------
    // Draft → Published
    // ----------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_DraftTemplateWithCompleteManifest_TransitionsToPublished()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Ready to Publish",
            Channel = ChannelType.Email,
            BodyPath = "templates/ready-to-publish/v1.html"
        });

        // Mock: manifest service returns one entry for 'name'
        _manifestServiceMock
            .Setup(m => m.GetByTemplateIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaceholderManifestEntryDto>
            {
                new() { Id = Guid.NewGuid(), TemplateId = template.Id, Key = "name",
                        Type = "Scalar", IsFromDataSource = true }
            }.AsReadOnly());

        // Mock: parser says manifest is complete
        _parserServiceMock
            .Setup(p => p.ValidateManifestCompleteness(It.IsAny<string>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(new ManifestValidationResult
            {
                IsComplete = true,
                UndeclaredKeys = Array.Empty<string>(),
                OrphanKeys = Array.Empty<string>(),
                Summary = "Manifest is complete."
            });

        // Act
        var result = await _service.PublishAsync(template.Id);

        // Assert
        result.Status.Should().Be(TemplateStatus.Published);
    }

    [Fact]
    public async Task PublishAsync_DraftTemplateWithIncompleteManifest_ThrowsValidationException()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Incomplete Manifest Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/incomplete-manifest/v1.html"
        });

        // Mock: manifest service returns only 'name', missing 'ref'
        _manifestServiceMock
            .Setup(m => m.GetByTemplateIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlaceholderManifestEntryDto>
            {
                new() { Id = Guid.NewGuid(), TemplateId = template.Id, Key = "name",
                        Type = "Scalar", IsFromDataSource = true }
            }.AsReadOnly());

        // Mock: parser says manifest is NOT complete (ref is undeclared)
        _parserServiceMock
            .Setup(p => p.ValidateManifestCompleteness(It.IsAny<string>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(new ManifestValidationResult
            {
                IsComplete = false,
                UndeclaredKeys = new[] { "ref" },
                OrphanKeys = Array.Empty<string>(),
                Summary = "1 placeholder undeclared: 'ref'."
            });

        // Act
        var act = () => _service.PublishAsync(template.Id);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*manifest is incomplete*");
    }

    [Fact]
    public async Task PublishAsync_AlreadyPublishedTemplate_ThrowsValidationException()
    {
        // Arrange — set up a template and publish it
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Already Published",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        // Set to Published directly in context to simulate already-published state
        var entity = await _context.Templates.FirstAsync(t => t.Id == template.Id);
        entity.Status = TemplateStatus.Published;
        await _context.SaveChangesAsync();

        // Act — attempt to publish again
        var act = () => _service.PublishAsync(template.Id);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*cannot be published*current status is 'Published'*");
    }

    [Fact]
    public async Task PublishAsync_ArchivedTemplate_ThrowsValidationException()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Archived Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        var entity = await _context.Templates.FirstAsync(t => t.Id == template.Id);
        entity.Status = TemplateStatus.Archived;
        await _context.SaveChangesAsync();

        // Act — attempt to publish an archived template
        var act = () => _service.PublishAsync(template.Id);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*cannot be published*current status is 'Archived'*");
    }

    [Fact]
    public async Task PublishAsync_NonExistentTemplate_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.PublishAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // Draft/Published → Archived
    // ----------------------------------------------------------------

    [Fact]
    public async Task ArchiveAsync_DraftTemplate_TransitionsToArchived()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Draft to Archive",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        // Act
        var result = await _service.ArchiveAsync(template.Id);

        // Assert
        result.Status.Should().Be(TemplateStatus.Archived);
    }

    [Fact]
    public async Task ArchiveAsync_PublishedTemplate_TransitionsToArchived()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Published to Archive",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        // Set to Published directly in context
        var entity = await _context.Templates.FirstAsync(t => t.Id == template.Id);
        entity.Status = TemplateStatus.Published;
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ArchiveAsync(template.Id);

        // Assert
        result.Status.Should().Be(TemplateStatus.Archived);
    }

    [Fact]
    public async Task ArchiveAsync_AlreadyArchivedTemplate_ThrowsValidationException()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Already Archived",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        // Set to Archived
        var entity = await _context.Templates.FirstAsync(t => t.Id == template.Id);
        entity.Status = TemplateStatus.Archived;
        await _context.SaveChangesAsync();

        // Act — attempt to archive again
        var act = () => _service.ArchiveAsync(template.Id);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*already Archived*");
    }

    [Fact]
    public async Task ArchiveAsync_NonExistentTemplate_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.ArchiveAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // Business rule: Archived templates cannot go back to Published
    // ----------------------------------------------------------------

    [Fact]
    public async Task ArchivedTemplate_CannotBePublished_EvenWithCompleteManifest()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Archived Cannot Publish",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        // Archive the template
        var entity = await _context.Templates.FirstAsync(t => t.Id == template.Id);
        entity.Status = TemplateStatus.Archived;
        await _context.SaveChangesAsync();

        // Mock complete manifest
        _manifestServiceMock
            .Setup(m => m.GetByTemplateIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaceholderManifestEntryDto>().ToList().AsReadOnly());

        _parserServiceMock
            .Setup(p => p.ValidateManifestCompleteness(It.IsAny<string>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(new ManifestValidationResult
            {
                IsComplete = true,
                UndeclaredKeys = Array.Empty<string>(),
                OrphanKeys = Array.Empty<string>(),
                Summary = "Complete."
            });

        // Act — attempt to publish (should fail because template is Archived)
        var act = () => _service.PublishAsync(template.Id);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*cannot be published*");
    }

    // ----------------------------------------------------------------
    // Persistence: status changes are saved to the database
    // ----------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_PersistsStatusChange_InDatabase()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Persist Publish Test",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        _manifestServiceMock
            .Setup(m => m.GetByTemplateIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaceholderManifestEntryDto>().ToList().AsReadOnly());

        _parserServiceMock
            .Setup(p => p.ValidateManifestCompleteness(It.IsAny<string>(), It.IsAny<IEnumerable<PlaceholderManifestEntryDto>>()))
            .Returns(new ManifestValidationResult { IsComplete = true, UndeclaredKeys = Array.Empty<string>(), OrphanKeys = Array.Empty<string>(), Summary = "Complete." });

        // Act
        await _service.PublishAsync(template.Id);

        // Assert — query directly via context (bypass service cache)
        var persisted = await _context.Templates
            .AsNoTracking()
            .FirstAsync(t => t.Id == template.Id);

        persisted.Status.Should().Be(TemplateStatus.Published);
    }

    [Fact]
    public async Task ArchiveAsync_PersistsStatusChange_InDatabase()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Persist Archive Test",
            Channel = ChannelType.Email,
            BodyPath = "templates/new-template/v1.html"
        });

        // Act
        await _service.ArchiveAsync(template.Id);

        // Assert
        var persisted = await _context.Templates
            .AsNoTracking()
            .FirstAsync(t => t.Id == template.Id);

        persisted.Status.Should().Be(TemplateStatus.Archived);
    }
}

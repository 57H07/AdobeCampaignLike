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
/// Unit tests for PlaceholderManifestService using an EF Core in-memory database.
/// Validates business rules: key uniqueness, FreeField source enforcement, bulk replace.
/// TASK-006-08: Validation tests for manifest completeness.
/// </summary>
public class PlaceholderManifestServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly PlaceholderManifestService _service;
    private readonly Guid _templateId;

    public PlaceholderManifestServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var manifestRepository = new PlaceholderManifestRepository(_context);
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);
        var logger = new Mock<IAppLogger<PlaceholderManifestService>>();
        _service = new PlaceholderManifestService(manifestRepository, templateRepository, unitOfWork, logger.Object);

        // Seed a template for tests to use
        _templateId = Guid.NewGuid();
        var template = new Template
        {
            Id = _templateId,
            Name = "Test Template",
            Channel = ChannelType.Email,
            HtmlBody = "<p>{{ name }}</p>",
            Status = TemplateStatus.Draft,
            Version = 1
        };
        _context.Templates.Add(template);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // GetByTemplateIdAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetByTemplateIdAsync_NoEntries_ReturnsEmpty()
    {
        // Act
        var result = await _service.GetByTemplateIdAsync(_templateId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByTemplateIdAsync_WithEntries_ReturnsAll()
    {
        // Arrange
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "firstName",
            Type = PlaceholderType.Scalar,
            IsFromDataSource = true
        });
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "orders",
            Type = PlaceholderType.Table,
            IsFromDataSource = true
        });

        // Act
        var result = await _service.GetByTemplateIdAsync(_templateId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(e => e.Key == "firstName");
        result.Should().Contain(e => e.Key == "orders");
    }

    // ----------------------------------------------------------------
    // AddEntryAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task AddEntryAsync_ValidRequest_CreatesEntry()
    {
        // Arrange
        var request = new UpsertPlaceholderManifestRequest
        {
            Key = "customerName",
            Type = PlaceholderType.Scalar,
            IsFromDataSource = true,
            Description = "Customer full name"
        };

        // Act
        var result = await _service.AddEntryAsync(_templateId, request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.TemplateId.Should().Be(_templateId);
        result.Key.Should().Be("customerName");
        result.Type.Should().Be("Scalar");
        result.IsFromDataSource.Should().BeTrue();
        result.Description.Should().Be("Customer full name");
    }

    [Fact]
    public async Task AddEntryAsync_DuplicateKey_ThrowsValidationException()
    {
        // Arrange
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "name",
            Type = PlaceholderType.Scalar
        });

        // Act
        var act = () => _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "name",
            Type = PlaceholderType.Scalar
        });

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*name*already declared*");
    }

    [Fact]
    public async Task AddEntryAsync_NonExistentTemplate_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.AddEntryAsync(Guid.NewGuid(), new UpsertPlaceholderManifestRequest
        {
            Key = "key",
            Type = PlaceholderType.Scalar
        });

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddEntryAsync_FreeFieldType_ForcesIsFromDataSourceFalse()
    {
        // Arrange — FreeField with IsFromDataSource = true (should be overridden)
        var request = new UpsertPlaceholderManifestRequest
        {
            Key = "campaignTitle",
            Type = PlaceholderType.FreeField,
            IsFromDataSource = true // should be forced to false
        };

        // Act
        var result = await _service.AddEntryAsync(_templateId, request);

        // Assert
        result.IsFromDataSource.Should().BeFalse(
            because: "FreeField placeholders always require operator input");
    }

    // ----------------------------------------------------------------
    // UpdateEntryAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateEntryAsync_ValidRequest_UpdatesEntry()
    {
        // Arrange
        var entry = await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "original",
            Type = PlaceholderType.Scalar
        });

        // Act
        var updated = await _service.UpdateEntryAsync(_templateId, entry.Id, new UpsertPlaceholderManifestRequest
        {
            Key = "renamed",
            Type = PlaceholderType.List,
            IsFromDataSource = false,
            Description = "Updated"
        });

        // Assert
        updated.Key.Should().Be("renamed");
        updated.Type.Should().Be("List");
        updated.IsFromDataSource.Should().BeFalse();
        updated.Description.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateEntryAsync_NonExistentEntry_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.UpdateEntryAsync(_templateId, Guid.NewGuid(), new UpsertPlaceholderManifestRequest
        {
            Key = "key",
            Type = PlaceholderType.Scalar
        });

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateEntryAsync_KeyConflictsWithOtherEntry_ThrowsValidationException()
    {
        // Arrange
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "alpha",
            Type = PlaceholderType.Scalar
        });
        var beta = await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "beta",
            Type = PlaceholderType.Scalar
        });

        // Act — rename beta to alpha (conflict)
        var act = () => _service.UpdateEntryAsync(_templateId, beta.Id, new UpsertPlaceholderManifestRequest
        {
            Key = "alpha",
            Type = PlaceholderType.Scalar
        });

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----------------------------------------------------------------
    // DeleteEntryAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task DeleteEntryAsync_ExistingEntry_RemovesEntry()
    {
        // Arrange
        var entry = await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "toDelete",
            Type = PlaceholderType.Scalar
        });

        // Act
        await _service.DeleteEntryAsync(_templateId, entry.Id);

        // Assert
        var remaining = await _service.GetByTemplateIdAsync(_templateId);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEntryAsync_NonExistentEntry_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.DeleteEntryAsync(_templateId, Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // ReplaceManifestAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task ReplaceManifestAsync_ReplacesAllEntries()
    {
        // Arrange — add initial entries
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "old1",
            Type = PlaceholderType.Scalar
        });
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "old2",
            Type = PlaceholderType.Scalar
        });

        // Act — replace with new entries
        var newEntries = new[]
        {
            new UpsertPlaceholderManifestRequest { Key = "new1", Type = PlaceholderType.Scalar, IsFromDataSource = true },
            new UpsertPlaceholderManifestRequest { Key = "new2", Type = PlaceholderType.Table, IsFromDataSource = true }
        };
        var result = await _service.ReplaceManifestAsync(_templateId, newEntries);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(e => e.Key == "new1");
        result.Should().Contain(e => e.Key == "new2");
        result.Should().NotContain(e => e.Key == "old1");
        result.Should().NotContain(e => e.Key == "old2");
    }

    [Fact]
    public async Task ReplaceManifestAsync_DuplicateKeysInInput_ThrowsValidationException()
    {
        // Arrange
        var entries = new[]
        {
            new UpsertPlaceholderManifestRequest { Key = "name", Type = PlaceholderType.Scalar },
            new UpsertPlaceholderManifestRequest { Key = "name", Type = PlaceholderType.Scalar } // duplicate
        };

        // Act
        var act = () => _service.ReplaceManifestAsync(_templateId, entries);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Duplicate*");
    }

    [Fact]
    public async Task ReplaceManifestAsync_EmptyList_RemovesAllEntries()
    {
        // Arrange — add some entries first
        await _service.AddEntryAsync(_templateId, new UpsertPlaceholderManifestRequest
        {
            Key = "existing",
            Type = PlaceholderType.Scalar
        });

        // Act — replace with empty
        var result = await _service.ReplaceManifestAsync(_templateId, Array.Empty<UpsertPlaceholderManifestRequest>());

        // Assert
        result.Should().BeEmpty();
    }
}

using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for template versioning logic (US-008).
/// Validates: auto-snapshot on update, version increment, history retrieval,
/// diff generation, and revert behaviour.
/// </summary>
public class TemplateVersioningTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly TemplateService _service;

    public TemplateVersioningTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        var bodyStore = new Mock<ITemplateBodyStore>();
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);
        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object, manifestService.Object, parserService.Object,
            bodyStore.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // TASK-008-01: Version starts at 1
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateTemplate_Version_StartsAtOne()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Hello Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/hello/v1.html"
        });

        template.Version.Should().Be(1);
    }

    // ----------------------------------------------------------------
    // TASK-008-03: Auto-snapshot on update + version increment
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateTemplate_IncrementsVersionByOne()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Original",
            Channel = ChannelType.Email,
            BodyPath = "templates/original/v1.html"
        });

        template.Version.Should().Be(1);

        // Act
        var updated = await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Updated",
            BodyPath = "templates/original/v2.html"
        });

        // Assert
        updated.Version.Should().Be(2);
    }

    [Fact]
    public async Task UpdateTemplate_MultipleUpdates_VersionIncrementsEachTime()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Multi",
            Channel = ChannelType.Sms,
            BodyPath = "templates/multi/v1.txt"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Multi", BodyPath = "templates/multi/v2.txt" });
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Multi", BodyPath = "templates/multi/v3.txt" });
        var latest = await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Multi", BodyPath = "templates/multi/v4.txt" });

        latest.Version.Should().Be(4);
    }

    [Fact]
    public async Task UpdateTemplate_CreatesHistorySnapshot_WithCorrectVersion()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Snap",
            Channel = ChannelType.Email,
            BodyPath = "templates/snap/v1.html"
        });

        // Act: update the template
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Snap Updated",
            BodyPath = "templates/snap/v2.html",
            ChangedBy = "designer@example.com"
        });

        // Assert: history entry exists for v1 (snapshot of pre-update state)
        var history = await _context.TemplateHistories.ToListAsync();
        history.Should().HaveCount(1);

        var entry = history.Single();
        entry.Version.Should().Be(1);
        entry.Name.Should().Be("Snap");
        entry.BodyPath.Should().Be("templates/snap/v1.html");
        entry.TemplateId.Should().Be(template.Id);
        entry.ChangedBy.Should().Be("designer@example.com");
    }

    [Fact]
    public async Task UpdateTemplate_MultipleUpdates_CreatesHistoryEntryPerUpdate()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "T",
            Channel = ChannelType.Email,
            BodyPath = "templates/t/v1.html"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "T", BodyPath = "templates/t/v2.html" });
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "T", BodyPath = "templates/t/v3.html" });

        var history = await _context.TemplateHistories
            .Where(h => h.TemplateId == template.Id)
            .OrderBy(h => h.Version)
            .ToListAsync();

        history.Should().HaveCount(2);
        history[0].Version.Should().Be(1);
        history[0].BodyPath.Should().Be("templates/t/v1.html");
        history[1].Version.Should().Be(2);
        history[1].BodyPath.Should().Be("templates/t/v2.html");
    }

    // ----------------------------------------------------------------
    // TASK-008-04: Get history
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_NoHistory_ReturnsEmptyList()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Fresh",
            Channel = ChannelType.Email,
            BodyPath = "templates/fresh/v1.html"
        });

        var history = await _service.GetHistoryAsync(template.Id);

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_AfterUpdates_ReturnsVersionsDescending()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Hist",
            Channel = ChannelType.Email,
            BodyPath = "templates/hist/v1.html"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Hist", BodyPath = "templates/hist/v2.html" });
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Hist", BodyPath = "templates/hist/v3.html" });

        var history = await _service.GetHistoryAsync(template.Id);

        // History should be ordered descending (most recent first)
        history.Should().HaveCount(2);
        history[0].Version.Should().Be(2);
        history[1].Version.Should().Be(1);
    }

    [Fact]
    public async Task GetHistoryAsync_NonExistentTemplate_ThrowsNotFoundException()
    {
        var act = () => _service.GetHistoryAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // TASK-008-04 (diff): Get diff
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDiffAsync_ComparesHistoricVersionToCurrentVersion()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "DiffMe",
            Channel = ChannelType.Email,
            BodyPath = "templates/diffme/v1.html"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "DiffMe Updated",
            BodyPath = "templates/diffme/v2.html"
        });

        var diff = await _service.GetDiffAsync(template.Id, fromVersion: 1, toVersion: null);

        diff.FromVersion.Should().Be(1);
        diff.ToVersion.Should().Be(2);
        diff.FromBodyPath.Should().Be("templates/diffme/v1.html");
        diff.ToBodyPath.Should().Be("templates/diffme/v2.html");
        diff.FromName.Should().Be("DiffMe");
        diff.ToName.Should().Be("DiffMe Updated");
        diff.BodyPathChanged.Should().BeTrue();
        diff.NameChanged.Should().BeTrue();
    }

    [Fact]
    public async Task GetDiffAsync_SameContent_ReportsNoChanges()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Same",
            Channel = ChannelType.Email,
            BodyPath = "templates/same/v1.html"
        });

        // Update name only (same body path)
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Same",
            BodyPath = "templates/same/v1.html"
        });

        var diff = await _service.GetDiffAsync(template.Id, fromVersion: 1, toVersion: null);

        diff.BodyPathChanged.Should().BeFalse();
        diff.NameChanged.Should().BeFalse();
    }

    [Fact]
    public async Task GetDiffAsync_NonExistentVersion_ThrowsNotFoundException()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "T",
            Channel = ChannelType.Email,
            BodyPath = "templates/t-diff/v1.html"
        });

        // Version 99 does not exist
        var act = () => _service.GetDiffAsync(template.Id, fromVersion: 99, toVersion: null);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // TASK-008-05: Revert
    // ----------------------------------------------------------------

    [Fact]
    public async Task RevertToVersionAsync_RestoresHistoricContent()
    {
        // Arrange: create then update to v2
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Revert Test",
            Channel = ChannelType.Email,
            BodyPath = "templates/revert-test/v1.html"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Revert Test",
            BodyPath = "templates/revert-test/v2.html"
        });

        // Current version is now 2. Revert to v1.
        var reverted = await _service.RevertToVersionAsync(template.Id, version: 1, changedBy: "admin");

        // Assert: new version (v3) has v1 content
        reverted.Version.Should().Be(3);
        reverted.Name.Should().Be("Revert Test");
        reverted.BodyPath.Should().Be("templates/revert-test/v1.html");
    }

    [Fact]
    public async Task RevertToVersionAsync_DoesNotDeleteExistingHistory()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "History Preserved",
            Channel = ChannelType.Email,
            BodyPath = "templates/history-preserved/v1.html"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "History Preserved", BodyPath = "templates/history-preserved/v2.html" });

        // Act: revert to v1 — this should create a new history entry (snapshot of v2) + new version v3
        await _service.RevertToVersionAsync(template.Id, version: 1, changedBy: "user");

        // Assert: history should have 2 entries (v1 snapshot from first update, v2 snapshot from revert)
        var history = await _context.TemplateHistories
            .Where(h => h.TemplateId == template.Id)
            .OrderBy(h => h.Version)
            .ToListAsync();

        history.Should().HaveCount(2);
        history[0].Version.Should().Be(1);
        history[0].BodyPath.Should().Be("templates/history-preserved/v1.html");
        history[1].Version.Should().Be(2);
        history[1].BodyPath.Should().Be("templates/history-preserved/v2.html");
    }

    [Fact]
    public async Task RevertToVersionAsync_NonExistentVersion_ThrowsNotFoundException()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "NoVer",
            Channel = ChannelType.Email,
            BodyPath = "templates/nover/v1.html"
        });

        var act = () => _service.RevertToVersionAsync(template.Id, version: 99, changedBy: null);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RevertToVersionAsync_NonExistentTemplate_ThrowsNotFoundException()
    {
        var act = () => _service.RevertToVersionAsync(Guid.NewGuid(), version: 1, changedBy: null);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

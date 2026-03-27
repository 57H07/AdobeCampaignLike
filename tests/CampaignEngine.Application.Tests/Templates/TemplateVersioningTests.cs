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
    // TASK-008-01: Version starts at 1
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateTemplate_Version_StartsAtOne()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Hello Template",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Hello</p>"
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
            HtmlBody = "<p>v1 content</p>"
        });

        template.Version.Should().Be(1);

        // Act
        var updated = await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Updated",
            HtmlBody = "<p>v2 content</p>"
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
            HtmlBody = "v1"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Multi", HtmlBody = "v2" });
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Multi", HtmlBody = "v3" });
        var latest = await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Multi", HtmlBody = "v4" });

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
            HtmlBody = "<p>original</p>"
        });

        // Act: update the template
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Snap Updated",
            HtmlBody = "<p>updated</p>",
            ChangedBy = "designer@example.com"
        });

        // Assert: history entry exists for v1 (snapshot of pre-update state)
        var history = await _context.TemplateHistories.ToListAsync();
        history.Should().HaveCount(1);

        var entry = history.Single();
        entry.Version.Should().Be(1);
        entry.Name.Should().Be("Snap");
        entry.HtmlBody.Should().Be("<p>original</p>");
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
            HtmlBody = "v1"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "T", HtmlBody = "v2" });
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "T", HtmlBody = "v3" });

        var history = await _context.TemplateHistories
            .Where(h => h.TemplateId == template.Id)
            .OrderBy(h => h.Version)
            .ToListAsync();

        history.Should().HaveCount(2);
        history[0].Version.Should().Be(1);
        history[0].HtmlBody.Should().Be("v1");
        history[1].Version.Should().Be(2);
        history[1].HtmlBody.Should().Be("v2");
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
            HtmlBody = "<p>new</p>"
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
            HtmlBody = "v1"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Hist", HtmlBody = "v2" });
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "Hist", HtmlBody = "v3" });

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
            HtmlBody = "<p>original body</p>"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "DiffMe Updated",
            HtmlBody = "<p>updated body</p>"
        });

        var diff = await _service.GetDiffAsync(template.Id, fromVersion: 1, toVersion: null);

        diff.FromVersion.Should().Be(1);
        diff.ToVersion.Should().Be(2);
        diff.FromHtmlBody.Should().Be("<p>original body</p>");
        diff.ToHtmlBody.Should().Be("<p>updated body</p>");
        diff.FromName.Should().Be("DiffMe");
        diff.ToName.Should().Be("DiffMe Updated");
        diff.HtmlBodyChanged.Should().BeTrue();
        diff.NameChanged.Should().BeTrue();
    }

    [Fact]
    public async Task GetDiffAsync_SameContent_ReportsNoChanges()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Same",
            Channel = ChannelType.Email,
            HtmlBody = "<p>content</p>"
        });

        // Update name only (same body)
        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Same",
            HtmlBody = "<p>content</p>"
        });

        var diff = await _service.GetDiffAsync(template.Id, fromVersion: 1, toVersion: null);

        diff.HtmlBodyChanged.Should().BeFalse();
        diff.NameChanged.Should().BeFalse();
    }

    [Fact]
    public async Task GetDiffAsync_NonExistentVersion_ThrowsNotFoundException()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "T",
            Channel = ChannelType.Email,
            HtmlBody = "body"
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
            HtmlBody = "<p>v1 content</p>"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest
        {
            Name = "Revert Test",
            HtmlBody = "<p>v2 content</p>"
        });

        // Current version is now 2. Revert to v1.
        var reverted = await _service.RevertToVersionAsync(template.Id, version: 1, changedBy: "admin");

        // Assert: new version (v3) has v1 content
        reverted.Version.Should().Be(3);
        reverted.Name.Should().Be("Revert Test");
        reverted.HtmlBody.Should().Be("<p>v1 content</p>");
    }

    [Fact]
    public async Task RevertToVersionAsync_DoesNotDeleteExistingHistory()
    {
        // Arrange
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "History Preserved",
            Channel = ChannelType.Email,
            HtmlBody = "v1"
        });

        await _service.UpdateAsync(template.Id, new UpdateTemplateRequest { Name = "History Preserved", HtmlBody = "v2" });

        // Act: revert to v1 — this should create a new history entry (snapshot of v2) + new version v3
        await _service.RevertToVersionAsync(template.Id, version: 1, changedBy: "user");

        // Assert: history should have 2 entries (v1 snapshot from first update, v2 snapshot from revert)
        var history = await _context.TemplateHistories
            .Where(h => h.TemplateId == template.Id)
            .OrderBy(h => h.Version)
            .ToListAsync();

        history.Should().HaveCount(2);
        history[0].Version.Should().Be(1);
        history[0].HtmlBody.Should().Be("v1");
        history[1].Version.Should().Be(2);
        history[1].HtmlBody.Should().Be("v2");
    }

    [Fact]
    public async Task RevertToVersionAsync_NonExistentVersion_ThrowsNotFoundException()
    {
        var template = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "NoVer",
            Channel = ChannelType.Email,
            HtmlBody = "body"
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

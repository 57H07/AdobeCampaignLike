using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using CampaignEngine.Infrastructure.Tests.Persistence;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// Integration tests for the GET /api/templates/{id}/history endpoint logic (US-024).
/// Tests TemplateService.GetHistoryAsync via the EF Core in-memory provider.
/// Validates: version ordering, field presence (ChangedBy, BodyPath, timestamp), and error cases.
/// </summary>
public class TemplateHistoryIntegrationTests : DbContextTestBase
{
    private readonly TemplateService _service;

    public TemplateHistoryIntegrationTests()
    {
        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        var bodyStore = new Mock<ITemplateBodyStore>();
        var templateRepository = new TemplateRepository(Context);
        var unitOfWork = new UnitOfWork(Context);
        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object, manifestService.Object, parserService.Object,
            bodyStore.Object);
    }

    // ----------------------------------------------------------------
    // Happy path: history entries exist
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_AfterTwoUpdates_ReturnsTwoEntriesSortedDescending()
    {
        // Arrange — create template then update it twice to generate two history entries
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "History Test Template",
            Channel = ChannelType.Letter,
            BodyPath = "templates/history-test/v1.docx"
        });

        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "History Test Template",
            BodyPath = "templates/history-test/v2.docx",
            ChangedBy = "claire@example.com"
        });

        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "History Test Template",
            BodyPath = "templates/history-test/v3.docx",
            ChangedBy = "admin@example.com"
        });

        // Act
        var history = await _service.GetHistoryAsync(created.Id);

        // Assert — two snapshots created (one per update, v1 and v2)
        history.Should().HaveCount(2);

        // Sorted descending by version
        history[0].Version.Should().BeGreaterThan(history[1].Version);
    }

    [Fact]
    public async Task GetHistoryAsync_EachEntry_ContainsRequiredFields()
    {
        // Arrange
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Fields Test Template",
            Channel = ChannelType.Letter,
            BodyPath = "templates/fields-test/v1.docx"
        });

        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Fields Test Template",
            BodyPath = "templates/fields-test/v2.docx",
            ChangedBy = "claire@example.com"
        });

        // Act
        var history = await _service.GetHistoryAsync(created.Id);

        // Assert — entry has all required audit fields (US-024 AC)
        history.Should().HaveCount(1);
        var entry = history[0];
        entry.Version.Should().BeGreaterThan(0);
        entry.ChangedBy.Should().Be("claire@example.com");
        entry.BodyPath.Should().Be("templates/fields-test/v1.docx"); // snapshot of the PREVIOUS body
        entry.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetHistoryAsync_BodyPath_IsSnapshotOfPreviousVersion()
    {
        // Arrange — v1 created with path-v1, then updated to path-v2
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Snapshot Path Test",
            Channel = ChannelType.Letter,
            BodyPath = "templates/snapshot-path/v1.docx"
        });

        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Snapshot Path Test",
            BodyPath = "templates/snapshot-path/v2.docx"
        });

        // Act
        var history = await _service.GetHistoryAsync(created.Id);

        // Assert — history entry captures the PREVIOUS body path (v1)
        history.Should().HaveCount(1);
        history[0].BodyPath.Should().Be("templates/snapshot-path/v1.docx");
    }

    // ----------------------------------------------------------------
    // Happy path: no history yet
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_NoUpdatesYet_ReturnsEmptyList()
    {
        // Arrange — freshly created template has no history entries
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "No History Template",
            Channel = ChannelType.Letter,
            BodyPath = "templates/no-history/v1.docx"
        });

        // Act
        var history = await _service.GetHistoryAsync(created.Id);

        // Assert
        history.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Error case: template does not exist
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_NonExistentTemplate_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.GetHistoryAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // History is accessible after soft delete
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_AfterSoftDelete_StillReturnsHistory()
    {
        // Arrange — create, update (generates history), then soft-delete
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Soft-Deleted History",
            Channel = ChannelType.Letter,
            BodyPath = "templates/soft-deleted/v1.docx"
        });

        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Soft-Deleted History",
            BodyPath = "templates/soft-deleted/v2.docx"
        });

        await _service.DeleteAsync(created.Id);

        // Act — history must still be accessible (audit requirement BR-1)
        var history = await _service.GetHistoryAsync(created.Id);

        // Assert
        history.Should().HaveCount(1);
    }
}

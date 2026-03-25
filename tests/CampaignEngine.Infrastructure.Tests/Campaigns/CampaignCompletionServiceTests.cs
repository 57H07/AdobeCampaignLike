using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Batch;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Integration tests for CampaignCompletionService (TASK-026-09).
/// Validates failure-rate thresholds and campaign status transitions.
///
/// Business rules under test:
///   - failure rate &lt; 2%  → Completed
///   - failure rate >= 2% and &lt; 10% → PartialFailure
///   - failure rate >= 10% → ManualReview
/// </summary>
public class CampaignCompletionServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly CampaignCompletionService _sut;

    public CampaignCompletionServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new CampaignEngineDbContext(options);

        var loggerMock = new Mock<IAppLogger<CampaignCompletionService>>();
        _sut = new CampaignCompletionService(_context, loggerMock.Object);
    }

    public void Dispose() => _context.Dispose();

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<(Campaign Campaign, CampaignStep Step)> SeedRunningCampaignAsync(
        int totalRecipients, int successCount, int failureCount)
    {
        var campaign = new Campaign
        {
            Name = $"Test Campaign {Guid.NewGuid():N}",
            Status = CampaignStatus.Running,
            TotalRecipients = totalRecipients,
            SuccessCount = successCount,
            FailureCount = failureCount,
            ProcessedCount = successCount + failureCount
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var step = new CampaignStep
        {
            CampaignId = campaign.Id,
            StepOrder = 1,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid()
        };
        _context.CampaignSteps.Add(step);
        await _context.SaveChangesAsync();

        return (campaign, step);
    }

    // ------------------------------------------------------------------
    // Status transitions
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Zero failures → status becomes Completed")]
    public async Task FinalizeStepAsync_NoFailures_SetsCompleted()
    {
        var (campaign, step) = await SeedRunningCampaignAsync(1000, 1000, 0);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Completed);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact(DisplayName = "Failure rate exactly 0% → Completed")]
    public async Task FinalizeStepAsync_ZeroFailureRate_SetsCompleted()
    {
        var (campaign, step) = await SeedRunningCampaignAsync(500, 500, 0);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Completed);
    }

    [Fact(DisplayName = "Failure rate 1% (below 2%) → Completed")]
    public async Task FinalizeStepAsync_OnePercentFailureRate_SetsCompleted()
    {
        // 10 failures out of 1000 = 1%
        var (campaign, step) = await SeedRunningCampaignAsync(1000, 990, 10);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Completed);
    }

    [Fact(DisplayName = "Failure rate exactly 2% → PartialFailure")]
    public async Task FinalizeStepAsync_TwoPercentFailureRate_SetsPartialFailure()
    {
        // 20 failures out of 1000 = 2%
        var (campaign, step) = await SeedRunningCampaignAsync(1000, 980, 20);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.PartialFailure);
    }

    [Fact(DisplayName = "Failure rate 5% (between 2% and 10%) → PartialFailure")]
    public async Task FinalizeStepAsync_FivePercentFailureRate_SetsPartialFailure()
    {
        // 50 failures out of 1000 = 5%
        var (campaign, step) = await SeedRunningCampaignAsync(1000, 950, 50);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.PartialFailure);
    }

    [Fact(DisplayName = "Failure rate exactly 10% → ManualReview")]
    public async Task FinalizeStepAsync_TenPercentFailureRate_SetsManualReview()
    {
        // 100 failures out of 1000 = 10%
        var (campaign, step) = await SeedRunningCampaignAsync(1000, 900, 100);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.ManualReview);
    }

    [Fact(DisplayName = "Failure rate 50% → ManualReview")]
    public async Task FinalizeStepAsync_HighFailureRate_SetsManualReview()
    {
        var (campaign, step) = await SeedRunningCampaignAsync(1000, 500, 500);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.ManualReview);
    }

    [Fact(DisplayName = "100% failure rate → ManualReview")]
    public async Task FinalizeStepAsync_AllFailed_SetsManualReview()
    {
        var (campaign, step) = await SeedRunningCampaignAsync(500, 0, 500);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.ManualReview);
    }

    // ------------------------------------------------------------------
    // Zero recipients edge case
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Zero total recipients (no data source) → Completed with 0% failure")]
    public async Task FinalizeStepAsync_ZeroRecipients_SetsCompleted()
    {
        var (campaign, step) = await SeedRunningCampaignAsync(0, 0, 0);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Completed);
    }

    // ------------------------------------------------------------------
    // Step ExecutedAt is set
    // ------------------------------------------------------------------

    [Fact(DisplayName = "FinalizeStepAsync sets step.ExecutedAt")]
    public async Task FinalizeStepAsync_Always_SetsStepExecutedAt()
    {
        var (campaign, step) = await SeedRunningCampaignAsync(100, 100, 0);

        await _sut.FinalizeStepAsync(campaign.Id, step.Id);

        var updatedStep = await _context.CampaignSteps.FindAsync(step.Id);
        updatedStep!.ExecutedAt.Should().NotBeNull();
    }

    // ------------------------------------------------------------------
    // Not found exceptions
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Unknown campaign throws NotFoundException")]
    public async Task FinalizeStepAsync_UnknownCampaign_ThrowsNotFoundException()
    {
        var act = async () => await _sut.FinalizeStepAsync(Guid.NewGuid(), Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact(DisplayName = "Unknown step throws NotFoundException")]
    public async Task FinalizeStepAsync_UnknownStep_ThrowsNotFoundException()
    {
        var (campaign, _) = await SeedRunningCampaignAsync(100, 100, 0);

        var act = async () => await _sut.FinalizeStepAsync(campaign.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }
}

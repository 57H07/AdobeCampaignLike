using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Unit tests for CampaignDashboardService.
/// Validates aggregate metrics calculation, filter logic,
/// ETA computation, and step timeline status derivation.
/// </summary>
public class CampaignDashboardServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly CampaignDashboardService _service;

    public CampaignDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var logger = new Mock<IAppLogger<CampaignDashboardService>>();
        var campaignRepository = new CampaignRepository(_context);

        _service = new CampaignDashboardService(campaignRepository, logger.Object);
    }

    public void Dispose() => _context.Dispose();

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private Campaign SeedCampaign(
        CampaignStatus status = CampaignStatus.Running,
        int totalRecipients = 1000,
        int processedCount = 500,
        int successCount = 480,
        int failureCount = 20,
        string? createdBy = null,
        DateTime? startedAt = null,
        IEnumerable<CampaignStep>? steps = null)
    {
        var campaign = new Campaign
        {
            Name = $"Campaign {Guid.NewGuid():N}",
            Status = status,
            TotalRecipients = totalRecipients,
            ProcessedCount = processedCount,
            SuccessCount = successCount,
            FailureCount = failureCount,
            CreatedBy = createdBy,
            StartedAt = startedAt ?? DateTime.UtcNow.AddMinutes(-30)
        };

        if (steps != null)
        {
            foreach (var step in steps)
            {
                step.Campaign = campaign;
                campaign.Steps.Add(step);
            }
        }

        _context.Campaigns.Add(campaign);
        _context.SaveChanges();
        return campaign;
    }

    // ----------------------------------------------------------------
    // Default filter: Running and StepInProgress only
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_NoFilter_ReturnsOnlyRunningAndStepInProgress()
    {
        // Arrange
        SeedCampaign(status: CampaignStatus.Running);
        SeedCampaign(status: CampaignStatus.StepInProgress);
        SeedCampaign(status: CampaignStatus.Draft);
        SeedCampaign(status: CampaignStatus.Completed);
        SeedCampaign(status: CampaignStatus.WaitingNext);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Should().NotBeNull();
        dashboard.ActiveCampaignCount.Should().Be(2);
        dashboard.Campaigns.Should().HaveCount(2);
        dashboard.Campaigns.All(c => c.Status == "Running" || c.Status == "StepInProgress").Should().BeTrue();
    }

    [Fact]
    public async Task GetDashboardAsync_NoActiveCampaigns_ReturnsEmptyList()
    {
        // Arrange — only draft campaigns
        SeedCampaign(status: CampaignStatus.Draft);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(0);
        dashboard.Campaigns.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Per-campaign metrics
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_CampaignMetrics_AreCorrect()
    {
        // Arrange
        SeedCampaign(
            status: CampaignStatus.Running,
            totalRecipients: 1000,
            processedCount: 600,
            successCount: 580,
            failureCount: 20);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns.Should().HaveCount(1);
        var c = dashboard.Campaigns[0];
        c.TotalRecipients.Should().Be(1000);
        c.ProcessedCount.Should().Be(600);
        c.SuccessCount.Should().Be(580);
        c.FailureCount.Should().Be(20);
    }

    [Fact]
    public async Task GetDashboardAsync_ProgressPercent_CalculatedCorrectly()
    {
        // Arrange: 300/1000 = 30%
        SeedCampaign(totalRecipients: 1000, processedCount: 300);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns[0].ProgressPercent.Should().Be(30);
    }

    [Fact]
    public async Task GetDashboardAsync_ProgressPercent_ZeroWhenNoRecipients()
    {
        // Arrange
        SeedCampaign(totalRecipients: 0, processedCount: 0);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns[0].ProgressPercent.Should().Be(0);
    }

    [Fact]
    public async Task GetDashboardAsync_FailureRatePercent_CalculatedCorrectly()
    {
        // Arrange: 100 failures out of 1000 = 10%
        SeedCampaign(totalRecipients: 1000, processedCount: 1000, successCount: 900, failureCount: 100);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns[0].FailureRatePercent.Should().Be(10.0);
    }

    // ----------------------------------------------------------------
    // Estimated completion time
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_EstimatedCompletion_ComputedFromSendRate()
    {
        // Arrange: 500 processed in 10 minutes → rate = 500/600 = ~0.833/s
        // Remaining = 500 → eta ~ 10 minutes ahead
        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        SeedCampaign(
            totalRecipients: 1000,
            processedCount: 500,
            startedAt: startedAt);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var c = dashboard.Campaigns[0];

        // Assert
        c.EstimatedCompletionUtc.Should().NotBeNull();
        // ETA should be roughly 10 minutes in the future (within a 60s tolerance)
        c.EstimatedCompletionUtc!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task GetDashboardAsync_EstimatedCompletion_NullWhenNotStarted()
    {
        // Arrange: no StartedAt
        var campaign = new Campaign
        {
            Name = "Test Campaign",
            Status = CampaignStatus.Running,
            TotalRecipients = 1000,
            ProcessedCount = 100,
            StartedAt = null  // Not started yet
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns[0].EstimatedCompletionUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardAsync_EstimatedCompletion_NullWhenNothingProcessed()
    {
        // Arrange
        SeedCampaign(totalRecipients: 1000, processedCount: 0);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns[0].EstimatedCompletionUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardAsync_EstimatedCompletion_NullWhenFullyProcessed()
    {
        // Arrange: all processed = no remaining work
        SeedCampaign(totalRecipients: 1000, processedCount: 1000);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.Campaigns[0].EstimatedCompletionUtc.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // Filter: status
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_StatusFilter_ReturnsOnlyMatchingStatuses()
    {
        // Arrange
        SeedCampaign(status: CampaignStatus.Running);
        SeedCampaign(status: CampaignStatus.StepInProgress);
        SeedCampaign(status: CampaignStatus.WaitingNext);

        // Act — only Running
        var dashboard = await _service.GetDashboardAsync(new DashboardFilter { Status = "Running" });

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(1);
        dashboard.Campaigns[0].Status.Should().Be("Running");
    }

    [Fact]
    public async Task GetDashboardAsync_StatusFilter_MultipleStatuses()
    {
        // Arrange
        SeedCampaign(status: CampaignStatus.Running);
        SeedCampaign(status: CampaignStatus.StepInProgress);
        SeedCampaign(status: CampaignStatus.WaitingNext);

        // Act — Running + WaitingNext
        var dashboard = await _service.GetDashboardAsync(
            new DashboardFilter { Status = "Running,WaitingNext" });

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(2);
    }

    // ----------------------------------------------------------------
    // Filter: createdBy
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_CreatedByFilter_ReturnsOnlyMatchingOperator()
    {
        // Arrange
        SeedCampaign(createdBy: "thomas");
        SeedCampaign(createdBy: "marie");

        // Act
        var dashboard = await _service.GetDashboardAsync(new DashboardFilter { CreatedBy = "thomas" });

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(1);
        dashboard.Campaigns[0].CreatedBy.Should().Be("thomas");
    }

    // ----------------------------------------------------------------
    // Filter: date range
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_StartedFromFilter_ExcludesEarlierCampaigns()
    {
        // Arrange
        SeedCampaign(startedAt: DateTime.UtcNow.AddDays(-5));  // too old
        SeedCampaign(startedAt: DateTime.UtcNow.AddDays(-1));  // recent

        // Act — only show campaigns started in last 2 days
        var filter = new DashboardFilter { StartedFrom = DateTime.UtcNow.AddDays(-2) };
        var dashboard = await _service.GetDashboardAsync(filter);

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDashboardAsync_StartedToFilter_ExcludesNewerCampaigns()
    {
        // Arrange
        SeedCampaign(startedAt: DateTime.UtcNow.AddDays(-1));  // recent — should be excluded
        SeedCampaign(startedAt: DateTime.UtcNow.AddDays(-5));  // older — should be included

        // Act — only show campaigns started more than 2 days ago
        var filter = new DashboardFilter { StartedTo = DateTime.UtcNow.AddDays(-2) };
        var dashboard = await _service.GetDashboardAsync(filter);

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(1);
    }

    // ----------------------------------------------------------------
    // Step timeline status derivation
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_StepStatus_CompletedWhenExecutedAtIsSet()
    {
        // Arrange
        var step = new CampaignStep
        {
            StepOrder = 1,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow.AddHours(-1)
        };
        SeedCampaign(steps: [step]);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var stepDto = dashboard.Campaigns[0].Steps[0];

        // Assert
        stepDto.StepStatus.Should().Be("Completed");
        stepDto.ExecutedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDashboardAsync_StepStatus_ActiveWhenScheduledInPast()
    {
        // Arrange
        var step = new CampaignStep
        {
            StepOrder = 1,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow.AddHours(-1),  // past
            ExecutedAt = null
        };
        SeedCampaign(steps: [step]);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var stepDto = dashboard.Campaigns[0].Steps[0];

        // Assert
        stepDto.StepStatus.Should().Be("Active");
    }

    [Fact]
    public async Task GetDashboardAsync_StepStatus_WaitingWhenScheduledInFuture()
    {
        // Arrange
        var step = new CampaignStep
        {
            StepOrder = 1,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid(),
            ScheduledAt = DateTime.UtcNow.AddDays(3),  // future
            ExecutedAt = null
        };
        SeedCampaign(steps: [step]);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var stepDto = dashboard.Campaigns[0].Steps[0];

        // Assert
        stepDto.StepStatus.Should().Be("Waiting");
    }

    [Fact]
    public async Task GetDashboardAsync_StepStatus_PendingWhenNoSchedule()
    {
        // Arrange
        var step = new CampaignStep
        {
            StepOrder = 1,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid(),
            ScheduledAt = null,
            ExecutedAt = null
        };
        SeedCampaign(steps: [step]);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var stepDto = dashboard.Campaigns[0].Steps[0];

        // Assert
        stepDto.StepStatus.Should().Be("Pending");
    }

    // ----------------------------------------------------------------
    // Multi-step ordering
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_MultiStepCampaign_StepsOrderedByStepOrder()
    {
        // Arrange
        var steps = new[]
        {
            new CampaignStep { StepOrder = 3, Channel = ChannelType.Sms,    TemplateId = Guid.NewGuid(), DelayDays = 2 },
            new CampaignStep { StepOrder = 1, Channel = ChannelType.Email,  TemplateId = Guid.NewGuid(), DelayDays = 0 },
            new CampaignStep { StepOrder = 2, Channel = ChannelType.Letter, TemplateId = Guid.NewGuid(), DelayDays = 1 }
        };
        SeedCampaign(steps: steps);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var returnedSteps = dashboard.Campaigns[0].Steps;

        // Assert
        returnedSteps.Should().HaveCount(3);
        returnedSteps[0].StepOrder.Should().Be(1);
        returnedSteps[1].StepOrder.Should().Be(2);
        returnedSteps[2].StepOrder.Should().Be(3);
    }

    [Fact]
    public async Task GetDashboardAsync_StepChannel_MappedCorrectly()
    {
        // Arrange
        var steps = new[]
        {
            new CampaignStep { StepOrder = 1, Channel = ChannelType.Email,  TemplateId = Guid.NewGuid() },
            new CampaignStep { StepOrder = 2, Channel = ChannelType.Sms,    TemplateId = Guid.NewGuid() },
            new CampaignStep { StepOrder = 3, Channel = ChannelType.Letter, TemplateId = Guid.NewGuid() }
        };
        SeedCampaign(steps: steps);

        // Act
        var dashboard = await _service.GetDashboardAsync();
        var returnedSteps = dashboard.Campaigns[0].Steps;

        // Assert
        returnedSteps[0].Channel.Should().Be("Email");
        returnedSteps[1].Channel.Should().Be("Sms");
        returnedSteps[2].Channel.Should().Be("Letter");
    }

    // ----------------------------------------------------------------
    // Dashboard summary fields
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetDashboardAsync_ComputedAtUtc_IsRecentTimestamp()
    {
        // Arrange — no campaigns needed for this check
        var before = DateTime.UtcNow;

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.ComputedAtUtc.Should().BeOnOrAfter(before);
        dashboard.ComputedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetDashboardAsync_ActiveCampaignCount_MatchesCampaignsListLength()
    {
        // Arrange
        SeedCampaign(status: CampaignStatus.Running);
        SeedCampaign(status: CampaignStatus.Running);
        SeedCampaign(status: CampaignStatus.StepInProgress);

        // Act
        var dashboard = await _service.GetDashboardAsync();

        // Assert
        dashboard.ActiveCampaignCount.Should().Be(dashboard.Campaigns.Count);
        dashboard.ActiveCampaignCount.Should().Be(3);
    }
}

using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Tests for CampaignStatusService (TASK-027-08).
/// Validates allowed/disallowed status transitions, terminal/active checks,
/// and status history logging.
///
/// Business rules under test:
///   Draft → Scheduled (allowed)
///   Scheduled → Running (allowed)
///   Running → StepInProgress (allowed)
///   StepInProgress → WaitingNext | Completed | PartialFailure | ManualReview (allowed)
///   WaitingNext → StepInProgress | Completed | PartialFailure | ManualReview (allowed)
///   Any backward or cross-branch transition (disallowed)
///   Terminal states: Completed, PartialFailure, ManualReview (no further transitions)
///   Active states: Running, StepInProgress, WaitingNext
/// </summary>
public class CampaignStatusServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly CampaignStatusService _sut;

    public CampaignStatusServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);
        var historyRepository = new CampaignStatusHistoryRepository(_context);
        var unitOfWork = new UnitOfWork(_context);
        var loggerMock = new Mock<IAppLogger<CampaignStatusService>>();
        _sut = new CampaignStatusService(historyRepository, unitOfWork, loggerMock.Object);
    }

    public void Dispose() => _context.Dispose();

    // ----------------------------------------------------------------
    // IsTransitionAllowed — forward / valid transitions
    // ----------------------------------------------------------------

    [Fact]
    public void IsTransitionAllowed_DraftToScheduled_ReturnsTrue()
    {
        _sut.IsTransitionAllowed(CampaignStatus.Draft, CampaignStatus.Scheduled)
            .Should().BeTrue();
    }

    [Fact]
    public void IsTransitionAllowed_ScheduledToRunning_ReturnsTrue()
    {
        _sut.IsTransitionAllowed(CampaignStatus.Scheduled, CampaignStatus.Running)
            .Should().BeTrue();
    }

    [Fact]
    public void IsTransitionAllowed_RunningToStepInProgress_ReturnsTrue()
    {
        _sut.IsTransitionAllowed(CampaignStatus.Running, CampaignStatus.StepInProgress)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(CampaignStatus.StepInProgress, CampaignStatus.WaitingNext)]
    [InlineData(CampaignStatus.StepInProgress, CampaignStatus.Completed)]
    [InlineData(CampaignStatus.StepInProgress, CampaignStatus.PartialFailure)]
    [InlineData(CampaignStatus.StepInProgress, CampaignStatus.ManualReview)]
    public void IsTransitionAllowed_StepInProgressToTerminalOrWait_ReturnsTrue(
        CampaignStatus from, CampaignStatus to)
    {
        _sut.IsTransitionAllowed(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(CampaignStatus.WaitingNext, CampaignStatus.StepInProgress)]
    [InlineData(CampaignStatus.WaitingNext, CampaignStatus.Completed)]
    [InlineData(CampaignStatus.WaitingNext, CampaignStatus.PartialFailure)]
    [InlineData(CampaignStatus.WaitingNext, CampaignStatus.ManualReview)]
    public void IsTransitionAllowed_WaitingNextToAllowedTargets_ReturnsTrue(
        CampaignStatus from, CampaignStatus to)
    {
        _sut.IsTransitionAllowed(from, to).Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // IsTransitionAllowed — invalid / disallowed transitions
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(CampaignStatus.Draft,          CampaignStatus.Running)]
    [InlineData(CampaignStatus.Draft,          CampaignStatus.Completed)]
    [InlineData(CampaignStatus.Scheduled,      CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Scheduled,      CampaignStatus.Completed)]
    [InlineData(CampaignStatus.Running,        CampaignStatus.Scheduled)]
    [InlineData(CampaignStatus.Running,        CampaignStatus.Completed)]
    [InlineData(CampaignStatus.StepInProgress, CampaignStatus.Draft)]
    [InlineData(CampaignStatus.StepInProgress, CampaignStatus.Running)]
    public void IsTransitionAllowed_InvalidTransitions_ReturnsFalse(
        CampaignStatus from, CampaignStatus to)
    {
        _sut.IsTransitionAllowed(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(CampaignStatus.Completed)]
    [InlineData(CampaignStatus.PartialFailure)]
    [InlineData(CampaignStatus.ManualReview)]
    public void IsTransitionAllowed_FromTerminalStatus_AlwaysReturnsFalse(CampaignStatus terminal)
    {
        foreach (CampaignStatus target in Enum.GetValues<CampaignStatus>())
        {
            _sut.IsTransitionAllowed(terminal, target)
                .Should().BeFalse(
                    because: $"{terminal} is terminal and cannot transition to {target}");
        }
    }

    // ----------------------------------------------------------------
    // GetAllowedTransitions
    // ----------------------------------------------------------------

    [Fact]
    public void GetAllowedTransitions_Draft_ReturnsScheduledOnly()
    {
        var allowed = _sut.GetAllowedTransitions(CampaignStatus.Draft);
        allowed.Should().ContainSingle()
            .Which.Should().Be(CampaignStatus.Scheduled);
    }

    [Fact]
    public void GetAllowedTransitions_Scheduled_ReturnsRunningOnly()
    {
        var allowed = _sut.GetAllowedTransitions(CampaignStatus.Scheduled);
        allowed.Should().ContainSingle()
            .Which.Should().Be(CampaignStatus.Running);
    }

    [Fact]
    public void GetAllowedTransitions_StepInProgress_ReturnsFourOptions()
    {
        var allowed = _sut.GetAllowedTransitions(CampaignStatus.StepInProgress);
        allowed.Should().HaveCount(4);
        allowed.Should().Contain(CampaignStatus.WaitingNext);
        allowed.Should().Contain(CampaignStatus.Completed);
        allowed.Should().Contain(CampaignStatus.PartialFailure);
        allowed.Should().Contain(CampaignStatus.ManualReview);
    }

    [Theory]
    [InlineData(CampaignStatus.Completed)]
    [InlineData(CampaignStatus.PartialFailure)]
    [InlineData(CampaignStatus.ManualReview)]
    public void GetAllowedTransitions_TerminalStatus_ReturnsEmpty(CampaignStatus terminal)
    {
        var allowed = _sut.GetAllowedTransitions(terminal);
        allowed.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // IsTerminal
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(CampaignStatus.Completed,     true)]
    [InlineData(CampaignStatus.PartialFailure, true)]
    [InlineData(CampaignStatus.ManualReview,  true)]
    [InlineData(CampaignStatus.Draft,         false)]
    [InlineData(CampaignStatus.Scheduled,     false)]
    [InlineData(CampaignStatus.Running,       false)]
    [InlineData(CampaignStatus.StepInProgress, false)]
    [InlineData(CampaignStatus.WaitingNext,   false)]
    public void IsTerminal_ReturnsCorrectResult(CampaignStatus status, bool expected)
    {
        _sut.IsTerminal(status).Should().Be(expected);
    }

    // ----------------------------------------------------------------
    // IsActive
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(CampaignStatus.Running,        true)]
    [InlineData(CampaignStatus.StepInProgress, true)]
    [InlineData(CampaignStatus.WaitingNext,    true)]
    [InlineData(CampaignStatus.Draft,         false)]
    [InlineData(CampaignStatus.Scheduled,     false)]
    [InlineData(CampaignStatus.Completed,     false)]
    [InlineData(CampaignStatus.PartialFailure, false)]
    [InlineData(CampaignStatus.ManualReview,  false)]
    public void IsActive_ReturnsCorrectResult(CampaignStatus status, bool expected)
    {
        _sut.IsActive(status).Should().Be(expected);
    }

    // ----------------------------------------------------------------
    // LogTransitionAsync — persists history entry
    // ----------------------------------------------------------------

    [Fact]
    public async Task LogTransitionAsync_PersistsHistoryEntryWithCorrectFields()
    {
        // Arrange
        var campaign = new Campaign
        {
            Name = "Test Campaign",
            Status = CampaignStatus.Scheduled
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var before = DateTime.UtcNow;

        // Act
        await _sut.LogTransitionAsync(
            campaign.Id,
            CampaignStatus.Draft,
            CampaignStatus.Scheduled,
            "Scheduled by operator");

        // Assert
        var entry = await _context.CampaignStatusHistories
            .FirstOrDefaultAsync(h => h.CampaignId == campaign.Id);

        entry.Should().NotBeNull();
        entry!.CampaignId.Should().Be(campaign.Id);
        entry.FromStatus.Should().Be(CampaignStatus.Draft);
        entry.ToStatus.Should().Be(CampaignStatus.Scheduled);
        entry.Reason.Should().Be("Scheduled by operator");
        entry.OccurredAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task LogTransitionAsync_WithNoReason_PersistsNullReason()
    {
        // Arrange
        var campaign = new Campaign { Name = "Test Campaign 2", Status = CampaignStatus.Running };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        // Act
        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Scheduled, CampaignStatus.Running);

        // Assert
        var entry = await _context.CampaignStatusHistories
            .FirstOrDefaultAsync(h => h.CampaignId == campaign.Id);

        entry.Should().NotBeNull();
        entry!.Reason.Should().BeNull();
    }

    [Fact]
    public async Task LogTransitionAsync_MultipleTransitions_AllPersisted()
    {
        // Arrange
        var campaign = new Campaign { Name = "Multi-Transition Campaign", Status = CampaignStatus.Running };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        // Act — log three transitions
        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Draft,          CampaignStatus.Scheduled);
        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Scheduled,      CampaignStatus.Running);
        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Running,        CampaignStatus.StepInProgress);

        // Assert
        var history = await _context.CampaignStatusHistories
            .Where(h => h.CampaignId == campaign.Id)
            .OrderBy(h => h.OccurredAt)
            .ToListAsync();

        history.Should().HaveCount(3);
        history[0].FromStatus.Should().Be(CampaignStatus.Draft);
        history[0].ToStatus.Should().Be(CampaignStatus.Scheduled);
        history[1].FromStatus.Should().Be(CampaignStatus.Scheduled);
        history[1].ToStatus.Should().Be(CampaignStatus.Running);
        history[2].FromStatus.Should().Be(CampaignStatus.Running);
        history[2].ToStatus.Should().Be(CampaignStatus.StepInProgress);
    }

    // ----------------------------------------------------------------
    // GetHistoryAsync — ordered results
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_ReturnsTransitionsOrderedByTimestamp()
    {
        // Arrange
        var campaign = new Campaign { Name = "History Campaign", Status = CampaignStatus.Completed };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Draft,     CampaignStatus.Scheduled, "T1");
        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Scheduled, CampaignStatus.Running,   "T2");
        await _sut.LogTransitionAsync(campaign.Id, CampaignStatus.Running,   CampaignStatus.Completed, "T3");

        // Act
        var history = await _sut.GetHistoryAsync(campaign.Id);

        // Assert
        history.Should().HaveCount(3);
        // Ascending order
        history[0].Reason.Should().Be("T1");
        history[1].Reason.Should().Be("T2");
        history[2].Reason.Should().Be("T3");
        history.Select(h => h.OccurredAt)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetHistoryAsync_WithNoHistory_ReturnsEmptyList()
    {
        // Arrange
        var campaign = new Campaign { Name = "Empty History Campaign", Status = CampaignStatus.Draft };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        // Act
        var history = await _sut.GetHistoryAsync(campaign.Id);

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_DtoFields_MappedCorrectly()
    {
        // Arrange
        var campaign = new Campaign { Name = "DTO Mapping Campaign", Status = CampaignStatus.Running };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var before = DateTime.UtcNow;
        await _sut.LogTransitionAsync(
            campaign.Id, CampaignStatus.Scheduled, CampaignStatus.Running, "Started by system");

        // Act
        var history = await _sut.GetHistoryAsync(campaign.Id);

        // Assert
        history.Should().ContainSingle();
        var dto = history[0];
        dto.CampaignId.Should().Be(campaign.Id);
        dto.FromStatus.Should().Be("Scheduled");
        dto.ToStatus.Should().Be("Running");
        dto.Reason.Should().Be("Started by system");
        dto.OccurredAt.Should().BeOnOrAfter(before);
    }
}

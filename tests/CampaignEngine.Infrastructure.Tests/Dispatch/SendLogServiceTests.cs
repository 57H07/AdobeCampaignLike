using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Dispatch;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Tests.Persistence;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Integration tests for SendLogService using the EF Core in-memory database.
/// Verifies that SEND_LOG entries are created and updated correctly (TASK-034-07).
///
/// Completeness assertions:
///   - Pending entry created with all required fields
///   - Sent status applied with SentAt timestamp
///   - Failed status with error detail and retry count
///   - Retrying status with incremented retry count
///   - Query/filter operations return correct subsets
/// </summary>
public class SendLogServiceTests : DbContextTestBase
{
    private readonly SendLogService _service;

    public SendLogServiceTests()
    {
        var mockLogger = new Mock<IAppLogger<SendLogService>>();
        var sendLogRepository = new SendLogRepository(Context);
        var unitOfWork = new UnitOfWork(Context);
        _service = new SendLogService(sendLogRepository, unitOfWork, mockLogger.Object);
    }

    // ----------------------------------------------------------------
    // LogPendingAsync — entry creation
    // ----------------------------------------------------------------

    [Fact]
    public async Task LogPendingAsync_CreatesEntryWithPendingStatus()
    {
        var campaignId = Guid.NewGuid();

        var id = await _service.LogPendingAsync(
            campaignId: campaignId,
            campaignStepId: null,
            channel: ChannelType.Email,
            recipientAddress: "user@example.com",
            recipientId: "rec-001",
            correlationId: "corr-xyz");

        id.Should().NotBeEmpty();

        var entry = await Context.SendLogs.FindAsync(id);
        entry.Should().NotBeNull();
        entry!.Status.Should().Be(SendStatus.Pending);
        entry.CampaignId.Should().Be(campaignId);
        entry.Channel.Should().Be(ChannelType.Email);
        entry.RecipientAddress.Should().Be("user@example.com");
        entry.RecipientId.Should().Be("rec-001");
        entry.CorrelationId.Should().Be("corr-xyz");
        entry.RetryCount.Should().Be(0);
        entry.SentAt.Should().BeNull();
        entry.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task LogPendingAsync_WithCampaignStepId_PersistsStepId()
    {
        var campaignId = Guid.NewGuid();
        var stepId = Guid.NewGuid();

        var id = await _service.LogPendingAsync(campaignId, stepId, ChannelType.Sms, "+1234567890");

        var entry = await Context.SendLogs.FindAsync(id);
        entry!.CampaignStepId.Should().Be(stepId);
    }

    // ----------------------------------------------------------------
    // LogSentAsync — status transition to Sent
    // ----------------------------------------------------------------

    [Fact]
    public async Task LogSentAsync_UpdatesStatusToSentAndSetsSentAt()
    {
        var id = await _service.LogPendingAsync(Guid.NewGuid(), null, ChannelType.Email, "a@b.com");
        var sentAt = DateTime.UtcNow;

        await _service.LogSentAsync(id, sentAt);

        var entry = await Context.SendLogs.FindAsync(id);
        entry!.Status.Should().Be(SendStatus.Sent);
        entry.SentAt.Should().Be(sentAt);
        entry.ErrorDetail.Should().BeNull();
    }

    [Fact]
    public async Task LogSentAsync_ClearsErrorDetailOnSuccess()
    {
        var id = await _service.LogPendingAsync(Guid.NewGuid(), null, ChannelType.Email, "a@b.com");
        // Simulate a prior failure
        await _service.LogFailedAsync(id, "Previous error", retryCount: 1);

        // Now succeeded
        await _service.LogSentAsync(id, DateTime.UtcNow);

        var entry = await Context.SendLogs.FindAsync(id);
        entry!.Status.Should().Be(SendStatus.Sent);
        entry.ErrorDetail.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // LogFailedAsync — status transition to Failed
    // ----------------------------------------------------------------

    [Fact]
    public async Task LogFailedAsync_UpdatesStatusToFailedWithErrorDetail()
    {
        var id = await _service.LogPendingAsync(Guid.NewGuid(), null, ChannelType.Email, "a@b.com");

        await _service.LogFailedAsync(id, "Invalid email address", retryCount: 0);

        var entry = await Context.SendLogs.FindAsync(id);
        entry!.Status.Should().Be(SendStatus.Failed);
        entry.ErrorDetail.Should().Be("Invalid email address");
        entry.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task LogFailedAsync_AfterRetries_PreservesRetryCount()
    {
        var id = await _service.LogPendingAsync(Guid.NewGuid(), null, ChannelType.Email, "a@b.com");
        await _service.LogRetryingAsync(id, "Timeout", retryCount: 1);
        await _service.LogRetryingAsync(id, "Timeout", retryCount: 2);

        await _service.LogFailedAsync(id, "Max retries exceeded", retryCount: 3);

        var entry = await Context.SendLogs.FindAsync(id);
        entry!.Status.Should().Be(SendStatus.Failed);
        entry.RetryCount.Should().Be(3);
    }

    // ----------------------------------------------------------------
    // LogRetryingAsync — status transition to Retrying
    // ----------------------------------------------------------------

    [Fact]
    public async Task LogRetryingAsync_UpdatesStatusToRetryingWithIncrementedCount()
    {
        var id = await _service.LogPendingAsync(Guid.NewGuid(), null, ChannelType.Email, "a@b.com");

        await _service.LogRetryingAsync(id, "SMTP timeout", retryCount: 1);

        var entry = await Context.SendLogs.FindAsync(id);
        entry!.Status.Should().Be(SendStatus.Retrying);
        entry.ErrorDetail.Should().Be("SMTP timeout");
        entry.RetryCount.Should().Be(1);
    }

    // ----------------------------------------------------------------
    // Error on unknown SendLog ID
    // ----------------------------------------------------------------

    [Fact]
    public async Task LogSentAsync_UnknownId_ThrowsInvalidOperationException()
    {
        var unknownId = Guid.NewGuid();

        var act = async () => await _service.LogSentAsync(unknownId, DateTime.UtcNow);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{unknownId}*");
    }

    // ----------------------------------------------------------------
    // QueryAsync — filtering
    // ----------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_FilterByCampaignId_ReturnsOnlyMatchingEntries()
    {
        var targetCampaign = Guid.NewGuid();
        var otherCampaign = Guid.NewGuid();

        await _service.LogPendingAsync(targetCampaign, null, ChannelType.Email, "a@a.com");
        await _service.LogPendingAsync(targetCampaign, null, ChannelType.Email, "b@b.com");
        await _service.LogPendingAsync(otherCampaign, null, ChannelType.Email, "c@c.com");

        var results = await _service.QueryAsync(campaignId: targetCampaign);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.CampaignId.Should().Be(targetCampaign));
    }

    [Fact]
    public async Task QueryAsync_FilterByStatus_ReturnsOnlyMatchingEntries()
    {
        var campaignId = Guid.NewGuid();
        var id1 = await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "a@a.com");
        var id2 = await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "b@b.com");
        await _service.LogSentAsync(id2, DateTime.UtcNow);

        var pendingResults = await _service.QueryAsync(status: SendStatus.Pending);
        var sentResults = await _service.QueryAsync(status: SendStatus.Sent);

        pendingResults.Should().Contain(r => r.Id == id1);
        sentResults.Should().Contain(r => r.Id == id2);
        pendingResults.Should().NotContain(r => r.Id == id2);
    }

    [Fact]
    public async Task QueryAsync_FilterByRecipientAddress_ReturnsPartialMatches()
    {
        var campaignId = Guid.NewGuid();
        await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "alice@example.com");
        await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "bob@example.com");
        await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "carol@other.com");

        var results = await _service.QueryAsync(recipientAddress: "example.com");

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.RecipientAddress.Should().Contain("example.com"));
    }

    [Fact]
    public async Task QueryAsync_NoFilters_ReturnsAllEntries()
    {
        var campaignId = Guid.NewGuid();
        await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "a@a.com");
        await _service.LogPendingAsync(campaignId, null, ChannelType.Sms, "+111");
        await _service.LogPendingAsync(campaignId, null, ChannelType.Letter, "John Doe");

        var results = await _service.QueryAsync();

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueryAsync_Pagination_ReturnsCorrectPage()
    {
        var campaignId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
            await _service.LogPendingAsync(campaignId, null, ChannelType.Email, $"user{i}@example.com");

        var page1 = await _service.QueryAsync(pageNumber: 1, pageSize: 3);
        var page2 = await _service.QueryAsync(pageNumber: 2, pageSize: 3);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    // ----------------------------------------------------------------
    // CountAsync — filter count consistency
    // ----------------------------------------------------------------

    [Fact]
    public async Task CountAsync_MatchesQueryAsyncCount()
    {
        var campaignId = Guid.NewGuid();
        var id1 = await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "a@a.com");
        await _service.LogPendingAsync(campaignId, null, ChannelType.Email, "b@b.com");
        await _service.LogSentAsync(id1, DateTime.UtcNow);

        var sentCount = await _service.CountAsync(status: SendStatus.Sent);
        var sentResults = await _service.QueryAsync(status: SendStatus.Sent);

        sentCount.Should().Be(sentResults.Count);
    }

    // ----------------------------------------------------------------
    // GetByIdAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsEntry()
    {
        var id = await _service.LogPendingAsync(Guid.NewGuid(), null, ChannelType.Email, "a@b.com");

        var entry = await _service.GetByIdAsync(id);

        entry.Should().NotBeNull();
        entry!.Id.Should().Be(id);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var entry = await _service.GetByIdAsync(Guid.NewGuid());

        entry.Should().BeNull();
    }
}

using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests for Campaign entity persistence using the in-memory database.
/// Validates entity mappings, soft delete, relationships, and progress tracking.
/// </summary>
public class CampaignRepositoryTests : DbContextTestBase
{
    [Fact]
    public async Task CanSaveAndRetrieveCampaign()
    {
        // Arrange
        var campaign = new Campaign
        {
            Name = "Welcome Campaign 2026",
            Status = CampaignStatus.Draft,
            TotalRecipients = 1000
        };

        // Act
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        var retrieved = await Context.Campaigns.FirstOrDefaultAsync(c => c.Id == campaign.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Welcome Campaign 2026");
        retrieved.Status.Should().Be(CampaignStatus.Draft);
        retrieved.TotalRecipients.Should().Be(1000);
    }

    [Fact]
    public async Task SoftDeletedCampaigns_AreExcludedFromDefaultQuery()
    {
        // Arrange
        var activeCampaign = new Campaign
        {
            Name = "Active Campaign",
            Status = CampaignStatus.Draft
        };
        var deletedCampaign = new Campaign
        {
            Name = "Deleted Campaign",
            Status = CampaignStatus.Draft,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };

        Context.Campaigns.AddRange(activeCampaign, deletedCampaign);
        await Context.SaveChangesAsync();

        // Act
        var campaigns = await Context.Campaigns.ToListAsync();

        // Assert
        campaigns.Should().HaveCount(1);
        campaigns.First().Name.Should().Be("Active Campaign");
    }

    [Fact]
    public async Task CampaignSteps_AreSavedWithCampaign()
    {
        // Arrange
        var campaign = new Campaign
        {
            Name = "Multi-Step Campaign",
            Status = CampaignStatus.Draft
        };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        var step1 = new CampaignStep
        {
            CampaignId = campaign.Id,
            StepOrder = 1,
            Channel = ChannelType.Email,
            TemplateId = Guid.NewGuid(),
            DelayDays = 0
        };
        var step2 = new CampaignStep
        {
            CampaignId = campaign.Id,
            StepOrder = 2,
            Channel = ChannelType.Sms,
            TemplateId = Guid.NewGuid(),
            DelayDays = 7
        };

        Context.CampaignSteps.AddRange(step1, step2);
        await Context.SaveChangesAsync();

        // Act
        var retrievedCampaign = await Context.Campaigns
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == campaign.Id);

        // Assert
        retrievedCampaign.Should().NotBeNull();
        retrievedCampaign!.Steps.Should().HaveCount(2);
        retrievedCampaign.Steps.Should().ContainSingle(s => s.StepOrder == 1 && s.Channel == ChannelType.Email);
        retrievedCampaign.Steps.Should().ContainSingle(s => s.StepOrder == 2 && s.DelayDays == 7);
    }

    [Fact]
    public async Task SendLogs_AreLinkedToCampaign()
    {
        // Arrange
        var campaign = new Campaign
        {
            Name = "Campaign With Logs",
            Status = CampaignStatus.Running,
            TotalRecipients = 3,
            ProcessedCount = 2
        };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        var log1 = new SendLog
        {
            CampaignId = campaign.Id,
            Channel = ChannelType.Email,
            RecipientAddress = "alice@example.com",
            Status = SendStatus.Sent,
            SentAt = DateTime.UtcNow
        };
        var log2 = new SendLog
        {
            CampaignId = campaign.Id,
            Channel = ChannelType.Email,
            RecipientAddress = "bob@example.com",
            Status = SendStatus.Failed,
            ErrorDetail = "SMTP connection refused"
        };

        Context.SendLogs.AddRange(log1, log2);
        await Context.SaveChangesAsync();

        // Act
        var sendLogs = await Context.SendLogs
            .Where(l => l.CampaignId == campaign.Id)
            .ToListAsync();

        // Assert
        sendLogs.Should().HaveCount(2);
        sendLogs.Should().ContainSingle(l => l.Status == SendStatus.Sent);
        sendLogs.Should().ContainSingle(l => l.Status == SendStatus.Failed);
    }
}

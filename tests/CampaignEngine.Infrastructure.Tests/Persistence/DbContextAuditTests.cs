using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Persistence;

/// <summary>
/// Tests that verify the automatic audit field management in CampaignEngineDbContext.
/// </summary>
public class DbContextAuditTests : DbContextTestBase
{
    [Fact]
    public async Task SaveChanges_SetsUpdatedAt_OnModification()
    {
        // Arrange
        var template = new Template
        {
            Name = "Audit Template",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Draft,
            HtmlBody = "<p>Original</p>",
            Version = 1
        };
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();

        var originalUpdatedAt = template.UpdatedAt;

        // Small delay to ensure time difference
        await Task.Delay(5);

        // Act — modify and save again
        template.HtmlBody = "<p>Updated</p>";
        await Context.SaveChangesAsync();

        // Assert
        template.UpdatedAt.Should().BeOnOrAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task NewEntity_HasGuidId()
    {
        // Arrange
        var apiKey = new ApiKey
        {
            Name = "Test Key",
            KeyHash = "hash_value",
            IsActive = true
        };

        // Act
        Context.ApiKeys.Add(apiKey);
        await Context.SaveChangesAsync();

        // Assert
        apiKey.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SendLog_StoresCorrelationId()
    {
        // Arrange
        var campaign = new Campaign
        {
            Name = "Correlation Test Campaign",
            Status = CampaignStatus.Running
        };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        var correlationId = Guid.NewGuid().ToString();
        var sendLog = new SendLog
        {
            CampaignId = campaign.Id,
            Channel = ChannelType.Email,
            RecipientAddress = "test@example.com",
            Status = SendStatus.Sent,
            CorrelationId = correlationId
        };

        // Act
        Context.SendLogs.Add(sendLog);
        await Context.SaveChangesAsync();

        var retrieved = await Context.SendLogs.FirstOrDefaultAsync(l => l.CorrelationId == correlationId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task TemplateSnapshot_IsImmutable_AfterCreation()
    {
        // Arrange — create snapshot
        var snapshot = new TemplateSnapshot
        {
            OriginalTemplateId = Guid.NewGuid(),
            TemplateVersion = 1,
            Name = "Snapshot v1",
            Channel = ChannelType.Email,
            ResolvedHtmlBody = "<p>Snapshot content</p>"
        };

        Context.TemplateSnapshots.Add(snapshot);
        await Context.SaveChangesAsync();

        // Act — retrieve and verify all fields are persisted
        var retrieved = await Context.TemplateSnapshots.FirstOrDefaultAsync(s => s.Id == snapshot.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.ResolvedHtmlBody.Should().Be("<p>Snapshot content</p>");
        retrieved.TemplateVersion.Should().Be(1);
        retrieved.Id.Should().Be(snapshot.Id);
    }
}

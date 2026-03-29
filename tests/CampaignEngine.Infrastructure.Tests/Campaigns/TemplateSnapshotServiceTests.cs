using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Tests.Persistence;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Tests for TemplateSnapshotService covering snapshot creation, isolation,
/// sub-template resolution, and retrieval.
/// </summary>
public class TemplateSnapshotServiceTests : DbContextTestBase
{
    private readonly Mock<ISubTemplateResolverService> _resolverMock;
    private readonly Mock<IAppLogger<TemplateSnapshotService>> _loggerMock;
    private readonly TemplateSnapshotService _service;

    public TemplateSnapshotServiceTests()
    {
        _resolverMock = new Mock<ISubTemplateResolverService>();
        _loggerMock = new Mock<IAppLogger<TemplateSnapshotService>>();

        // Default: resolver returns the body unchanged (no sub-templates)
        _resolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string body, CancellationToken _) => body);

        var campaignRepository = new CampaignRepository(Context);
        var templateRepository = new TemplateRepository(Context);
        var unitOfWork = new UnitOfWork(Context);

        _service = new TemplateSnapshotService(
            campaignRepository, templateRepository, unitOfWork,
            _resolverMock.Object, _loggerMock.Object);
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task<Template> SeedPublishedTemplateAsync(
        string name = "Test Template",
        string bodyPath = "templates/test/v1.html",
        ChannelType channel = ChannelType.Email)
    {
        var template = new Template
        {
            Name = name,
            Channel = channel,
            BodyPath = bodyPath,
            Status = TemplateStatus.Published,
            Version = 1
        };
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();
        return template;
    }

    private async Task<Campaign> SeedCampaignWithStepsAsync(params Template[] templates)
    {
        var campaign = new Campaign
        {
            Name = $"Campaign {Guid.NewGuid():N}",
            Status = CampaignStatus.Draft
        };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        for (int i = 0; i < templates.Length; i++)
        {
            var step = new CampaignStep
            {
                CampaignId = campaign.Id,
                StepOrder = i + 1,
                Channel = templates[i].Channel,
                TemplateId = templates[i].Id,
                DelayDays = i * 7
            };
            Context.CampaignSteps.Add(step);
        }

        await Context.SaveChangesAsync();
        return campaign;
    }

    // ----------------------------------------------------------------
    // CreateSnapshotsForCampaignAsync — success cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_SingleStep_CreatesOneSnapshot()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync("Welcome Email", "templates/welcome-email/v1.html");
        var campaign = await SeedCampaignWithStepsAsync(template);

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        var snapshots = Context.TemplateSnapshots.ToList();
        snapshots.Should().HaveCount(1);
        snapshots[0].Name.Should().Be("Welcome Email");
        snapshots[0].ResolvedHtmlBody.Should().Be("templates/welcome-email/v1.html");
        snapshots[0].OriginalTemplateId.Should().Be(template.Id);
        snapshots[0].TemplateVersion.Should().Be(1);
        snapshots[0].Channel.Should().Be(ChannelType.Email);
    }

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_SingleStep_LinksStepToSnapshot()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var campaign = await SeedCampaignWithStepsAsync(template);

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        var step = Context.CampaignSteps.First(s => s.CampaignId == campaign.Id);
        step.TemplateSnapshotId.Should().NotBeNull();

        var snapshot = Context.TemplateSnapshots.Find(step.TemplateSnapshotId!.Value);
        snapshot.Should().NotBeNull();
        snapshot!.OriginalTemplateId.Should().Be(template.Id);
    }

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_MultipleStepsDifferentTemplates_CreatesOneSnapshotPerTemplate()
    {
        // Arrange
        var emailTemplate = await SeedPublishedTemplateAsync("Email Tpl", "templates/email-tpl/v1.html", ChannelType.Email);
        var smsTemplate = await SeedPublishedTemplateAsync("SMS Tpl", "templates/sms-tpl/v1.txt", ChannelType.Sms);
        var campaign = await SeedCampaignWithStepsAsync(emailTemplate, smsTemplate);

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        var snapshots = Context.TemplateSnapshots.ToList();
        snapshots.Should().HaveCount(2);
        snapshots.Should().Contain(s => s.Channel == ChannelType.Email);
        snapshots.Should().Contain(s => s.Channel == ChannelType.Sms);
    }

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_MultipleStepsSameTemplate_ReusesSingleSnapshot()
    {
        // Arrange — two steps referencing the same template
        var template = await SeedPublishedTemplateAsync("Shared Template", "<p>Shared</p>");

        var campaign = new Campaign
        {
            Name = $"Campaign {Guid.NewGuid():N}",
            Status = CampaignStatus.Draft
        };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        Context.CampaignSteps.AddRange(
            new CampaignStep { CampaignId = campaign.Id, StepOrder = 1, Channel = ChannelType.Email, TemplateId = template.Id, DelayDays = 0 },
            new CampaignStep { CampaignId = campaign.Id, StepOrder = 2, Channel = ChannelType.Email, TemplateId = template.Id, DelayDays = 7 }
        );
        await Context.SaveChangesAsync();

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert — only one snapshot, but both steps point to it
        var snapshots = Context.TemplateSnapshots.ToList();
        snapshots.Should().HaveCount(1, "two steps sharing the same template should re-use one snapshot");

        var steps = Context.CampaignSteps
            .Where(s => s.CampaignId == campaign.Id)
            .ToList();
        steps.Should().AllSatisfy(s => s.TemplateSnapshotId.Should().Be(snapshots[0].Id));
    }

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_CallsSubTemplateResolverForEachUniqueTemplate()
    {
        // Arrange
        var tpl1 = await SeedPublishedTemplateAsync("T1", "templates/t1/v1.html");
        var tpl2 = await SeedPublishedTemplateAsync("T2", "templates/t2/v1.html");
        var campaign = await SeedCampaignWithStepsAsync(tpl1, tpl2);

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert — resolver invoked once per unique template
        _resolverMock.Verify(
            r => r.ResolveAsync(tpl1.Id, tpl1.BodyPath, It.IsAny<CancellationToken>()),
            Times.Once);
        _resolverMock.Verify(
            r => r.ResolveAsync(tpl2.Id, tpl2.BodyPath, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_StoresResolvedBodyFromSubTemplateResolver()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync("Header Template", "templates/header-template/v1.html");
        const string resolvedBody = "<p><header>RESOLVED HEADER</header></p>";

        _resolverMock
            .Setup(r => r.ResolveAsync(template.Id, template.BodyPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedBody);

        var campaign = await SeedCampaignWithStepsAsync(template);

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert — snapshot stores the resolved (not original) body
        var snapshot = Context.TemplateSnapshots.Single();
        snapshot.ResolvedHtmlBody.Should().Be(resolvedBody,
            "snapshot should contain fully resolved sub-template content");
    }

    // ----------------------------------------------------------------
    // Snapshot isolation — template edits don't affect snapshots
    // ----------------------------------------------------------------

    [Fact]
    public async Task TemplateSnapshot_IsIsolated_SubsequentTemplateEditDoesNotAffectSnapshot()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync("Newsletter", "templates/newsletter/v1.html");
        var campaign = await SeedCampaignWithStepsAsync(template);

        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Verify snapshot was captured
        var snapshotBefore = Context.TemplateSnapshots.Single();
        snapshotBefore.ResolvedHtmlBody.Should().Be("templates/newsletter/v1.html");

        // Act — simulate template edit after scheduling
        template.BodyPath = "templates/newsletter/v2.html";
        template.Version = 2;
        await Context.SaveChangesAsync();

        // Assert — reload snapshot, it should remain unchanged
        Context.ChangeTracker.Clear();
        var snapshotAfter = Context.TemplateSnapshots.Single();
        snapshotAfter.ResolvedHtmlBody.Should().Be("templates/newsletter/v1.html",
            "snapshot should be immutable after creation");
        snapshotAfter.TemplateVersion.Should().Be(1,
            "snapshot version should reflect the version at freeze time");
    }

    // ----------------------------------------------------------------
    // CreateSnapshotsForCampaignAsync — error cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_WithNonExistentCampaign_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.CreateSnapshotsForCampaignAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateSnapshotsForCampaignAsync_WithNoSteps_CompletesWithoutCreatingSnapshots()
    {
        // Arrange — campaign with zero steps
        var campaign = new Campaign
        {
            Name = "Empty Campaign",
            Status = CampaignStatus.Draft
        };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        // Act
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        Context.TemplateSnapshots.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // GetSnapshotsForCampaignAsync — success cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetSnapshotsForCampaignAsync_AfterScheduling_ReturnsSnapshotsOrderedByStepOrder()
    {
        // Arrange
        var emailTemplate = await SeedPublishedTemplateAsync("Email Tpl", "templates/email-tpl2/v1.html", ChannelType.Email);
        var smsTemplate = await SeedPublishedTemplateAsync("SMS Tpl", "templates/sms-tpl2/v1.txt", ChannelType.Sms);
        var campaign = await SeedCampaignWithStepsAsync(emailTemplate, smsTemplate);

        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Act
        var snapshots = await _service.GetSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        snapshots.Should().HaveCount(2);
        snapshots[0].Channel.Should().Be("Email");
        snapshots[1].Channel.Should().Be("Sms");
    }

    [Fact]
    public async Task GetSnapshotsForCampaignAsync_TwoStepsSameTemplate_ReturnsOneSnapshot()
    {
        // Arrange — two steps sharing same template
        var template = await SeedPublishedTemplateAsync("Shared", "templates/shared/v1.html");

        var campaign = new Campaign { Name = $"Cam {Guid.NewGuid():N}", Status = CampaignStatus.Draft };
        Context.Campaigns.Add(campaign);
        await Context.SaveChangesAsync();

        Context.CampaignSteps.AddRange(
            new CampaignStep { CampaignId = campaign.Id, StepOrder = 1, Channel = ChannelType.Email, TemplateId = template.Id, DelayDays = 0 },
            new CampaignStep { CampaignId = campaign.Id, StepOrder = 2, Channel = ChannelType.Email, TemplateId = template.Id, DelayDays = 14 }
        );
        await Context.SaveChangesAsync();

        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Act
        var snapshots = await _service.GetSnapshotsForCampaignAsync(campaign.Id);

        // Assert — deduplicated
        snapshots.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSnapshotsForCampaignAsync_BeforeScheduling_ReturnsEmptyList()
    {
        // Arrange — campaign with no snapshots yet
        var template = await SeedPublishedTemplateAsync();
        var campaign = await SeedCampaignWithStepsAsync(template);

        // Act — do NOT call CreateSnapshotsForCampaignAsync
        var snapshots = await _service.GetSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        snapshots.Should().BeEmpty("no snapshots created yet — campaign is still in Draft");
    }

    [Fact]
    public async Task GetSnapshotsForCampaignAsync_WithNonExistentCampaign_ReturnsEmptyList()
    {
        // Act
        var snapshots = await _service.GetSnapshotsForCampaignAsync(Guid.NewGuid());

        // Assert
        snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshotsForCampaignAsync_ReturnsDtoWithCorrectFields()
    {
        // Arrange
        const string bodyPath = "templates/newsletter-v3/v1.html";
        const string resolvedContent = "<p>Newsletter body</p>";
        var template = await SeedPublishedTemplateAsync("Newsletter v3", bodyPath, ChannelType.Email);
        template.Version = 3;
        await Context.SaveChangesAsync();

        _resolverMock
            .Setup(r => r.ResolveAsync(template.Id, bodyPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedContent);

        var campaign = await SeedCampaignWithStepsAsync(template);
        await _service.CreateSnapshotsForCampaignAsync(campaign.Id);

        // Act
        var snapshots = await _service.GetSnapshotsForCampaignAsync(campaign.Id);

        // Assert
        var dto = snapshots.Single();
        dto.Id.Should().NotBeEmpty();
        dto.OriginalTemplateId.Should().Be(template.Id);
        dto.TemplateVersion.Should().Be(3);
        dto.Name.Should().Be("Newsletter v3");
        dto.Channel.Should().Be("Email");
        dto.ResolvedHtmlBody.Should().Be(resolvedContent);
        dto.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

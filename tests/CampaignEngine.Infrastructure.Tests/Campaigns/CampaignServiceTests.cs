using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Campaigns;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Unit tests for CampaignService using an in-memory database.
/// Covers creation, validation rules, and retrieval.
/// </summary>
public class CampaignServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly Mock<IAppLogger<CampaignService>> _loggerMock;
    private readonly Mock<ITemplateSnapshotService> _snapshotServiceMock;
    private readonly CampaignService _service;

    public CampaignServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);
        _loggerMock = new Mock<IAppLogger<CampaignService>>();
        _snapshotServiceMock = new Mock<ITemplateSnapshotService>();

        var campaignRepository = new CampaignRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        _service = new CampaignService(
            campaignRepository, unitOfWork, _snapshotServiceMock.Object, _loggerMock.Object);
    }

    public void Dispose() => _context.Dispose();

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task<Template> SeedPublishedTemplateAsync(ChannelType channel = ChannelType.Email)
    {
        var template = new Template
        {
            Name = $"Test Template {Guid.NewGuid():N}",
            Channel = channel,
            HtmlBody = "<p>Hello {{name}}</p>",
            Status = TemplateStatus.Published,
            Version = 1
        };
        _context.Templates.Add(template);
        await _context.SaveChangesAsync();
        return template;
    }

    private async Task<Template> SeedDraftTemplateAsync()
    {
        var template = new Template
        {
            Name = $"Draft Template {Guid.NewGuid():N}",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Hello</p>",
            Status = TemplateStatus.Draft,
            Version = 1
        };
        _context.Templates.Add(template);
        await _context.SaveChangesAsync();
        return template;
    }

    private async Task<DataSource> SeedDataSourceAsync()
    {
        var ds = new DataSource
        {
            Name = "CRM DB",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(conn)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();
        return ds;
    }

    private CreateCampaignRequest BuildValidRequest(Guid templateId) => new()
    {
        Name = $"Campaign {Guid.NewGuid():N}",
        Steps = new List<CreateCampaignStepRequest>
        {
            new()
            {
                StepOrder = 1,
                Channel = ChannelType.Email,
                TemplateId = templateId,
                DelayDays = 0
            }
        }
    };

    // ----------------------------------------------------------------
    // CreateAsync — success cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithValidRequest_CreatesCampaignInDraftStatus()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var request = BuildValidRequest(template.Id);
        request.Name = "My First Campaign";

        // Act
        var result = await _service.CreateAsync(request, "operator@test.com");

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("My First Campaign");
        result.Status.Should().Be("Draft");
        result.CreatedBy.Should().Be("operator@test.com");
        result.Steps.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateAsync_WithDataSource_SetsDataSourceId()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var ds = await SeedDataSourceAsync();
        var request = BuildValidRequest(template.Id);
        request.DataSourceId = ds.Id;

        // Act
        var result = await _service.CreateAsync(request, "op");

        // Assert
        result.DataSourceId.Should().Be(ds.Id);
        result.DataSourceName.Should().Be("CRM DB");
    }

    [Fact]
    public async Task CreateAsync_WithMultipleSteps_OrdersStepsCorrectly()
    {
        // Arrange
        var tpl1 = await SeedPublishedTemplateAsync(ChannelType.Email);
        var tpl2 = await SeedPublishedTemplateAsync(ChannelType.Sms);
        var request = new CreateCampaignRequest
        {
            Name = "Multi-Step Campaign",
            Steps = new List<CreateCampaignStepRequest>
            {
                new() { StepOrder = 2, Channel = ChannelType.Sms,   TemplateId = tpl2.Id, DelayDays = 15 },
                new() { StepOrder = 1, Channel = ChannelType.Email, TemplateId = tpl1.Id, DelayDays = 0  }
            }
        };

        // Act
        var result = await _service.CreateAsync(request, null);

        // Assert
        result.Steps.Should().HaveCount(2);
        result.Steps[0].StepOrder.Should().Be(1);
        result.Steps[0].Channel.Should().Be("Email");
        result.Steps[1].StepOrder.Should().Be(2);
        result.Steps[1].DelayDays.Should().Be(15);
    }

    [Fact]
    public async Task CreateAsync_WithScheduledAt_PersistsScheduledAt()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var scheduledAt = DateTime.UtcNow.AddHours(1);
        var request = BuildValidRequest(template.Id);
        request.ScheduledAt = scheduledAt;

        // Act
        var result = await _service.CreateAsync(request, null);

        // Assert
        result.ScheduledAt.Should().BeCloseTo(scheduledAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CreateAsync_WithFreeFieldValues_PersistsFreeFieldJson()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var request = BuildValidRequest(template.Id);
        request.FreeFieldValues = """{"offerCode":"SPRING2026","discount":"20%"}""";

        // Act
        var result = await _service.CreateAsync(request, null);

        // Assert
        result.FreeFieldValues.Should().Be("""{"offerCode":"SPRING2026","discount":"20%"}""");
    }

    // ----------------------------------------------------------------
    // CreateAsync — validation failures
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsValidationException()
    {
        // Arrange — seed existing campaign with that name
        var template = await SeedPublishedTemplateAsync();
        var first = BuildValidRequest(template.Id);
        first.Name = "Duplicate Campaign";
        await _service.CreateAsync(first, null);

        var second = BuildValidRequest(template.Id);
        second.Name = "Duplicate Campaign";

        // Act
        var act = () => _service.CreateAsync(second, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("name");
        ex.Which.Errors["name"].Should().ContainMatch("*Duplicate Campaign*");
    }

    [Fact]
    public async Task CreateAsync_WithNoSteps_ThrowsValidationException()
    {
        // Arrange
        var request = new CreateCampaignRequest
        {
            Name = "Empty Steps Campaign",
            Steps = new List<CreateCampaignStepRequest>()
        };

        // Act
        var act = () => _service.CreateAsync(request, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("steps");
        ex.Which.Errors["steps"].Should().ContainMatch("*step*");
    }

    [Fact]
    public async Task CreateAsync_WithDraftTemplate_ThrowsValidationException()
    {
        // Arrange
        var draftTemplate = await SeedDraftTemplateAsync();
        var request = BuildValidRequest(draftTemplate.Id);

        // Act
        var act = () => _service.CreateAsync(request, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("steps");
        ex.Which.Errors["steps"].Should().ContainMatch("*Published*");
    }

    [Fact]
    public async Task CreateAsync_WithNonExistentTemplate_ThrowsValidationException()
    {
        // Arrange
        var request = BuildValidRequest(Guid.NewGuid()); // non-existent GUID

        // Act
        var act = () => _service.CreateAsync(request, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("steps");
        ex.Which.Errors["steps"].Should().ContainMatch("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WithScheduledAtLessThan5Minutes_ThrowsValidationException()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var request = BuildValidRequest(template.Id);
        request.ScheduledAt = DateTime.UtcNow.AddMinutes(2); // only 2 min, not 5

        // Act
        var act = () => _service.CreateAsync(request, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("scheduledAt");
        ex.Which.Errors["scheduledAt"].Should().ContainMatch("*5 minutes*");
    }

    [Fact]
    public async Task CreateAsync_WithScheduledAtInPast_ThrowsValidationException()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var request = BuildValidRequest(template.Id);
        request.ScheduledAt = DateTime.UtcNow.AddHours(-1); // in the past

        // Act
        var act = () => _service.CreateAsync(request, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("scheduledAt");
        ex.Which.Errors["scheduledAt"].Should().ContainMatch("*5 minutes*");
    }

    [Fact]
    public async Task CreateAsync_WithNonExistentDataSource_ThrowsValidationException()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var request = BuildValidRequest(template.Id);
        request.DataSourceId = Guid.NewGuid(); // non-existent

        // Act
        var act = () => _service.CreateAsync(request, null);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("dataSourceId");
        ex.Which.Errors["dataSourceId"].Should().ContainMatch("*not found*");
    }

    // ----------------------------------------------------------------
    // GetByIdAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ReturnsCampaign()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var created = await _service.CreateAsync(BuildValidRequest(template.Id), "user");

        // Act
        var result = await _service.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Name.Should().Be(created.Name);
        result.Steps.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // GetPagedAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_WithStatusFilter_ReturnsOnlyMatchingCampaigns()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        for (int i = 0; i < 3; i++)
        {
            var req = BuildValidRequest(template.Id);
            await _service.CreateAsync(req, null);
        }

        // Act
        var result = await _service.GetPagedAsync(new CampaignFilter
        {
            Status = CampaignStatus.Draft,
            Page = 1,
            PageSize = 20
        });

        // Assert — all newly created campaigns are Draft
        result.Items.Should().AllSatisfy(c => c.Status.Should().Be("Draft"));
        result.Total.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetPagedAsync_WithNameFilter_ReturnsMatchingCampaigns()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        var req = BuildValidRequest(template.Id);
        req.Name = "UNIQUE_SPRING_2026_TEST";
        await _service.CreateAsync(req, null);

        // Act
        var result = await _service.GetPagedAsync(new CampaignFilter
        {
            NameContains = "SPRING_2026",
            Page = 1,
            PageSize = 20
        });

        // Assert
        result.Items.Should().ContainSingle(c => c.Name.Contains("SPRING_2026"));
    }

    [Fact]
    public async Task GetPagedAsync_Pagination_RespectsPageSizeAndPage()
    {
        // Arrange
        var template = await SeedPublishedTemplateAsync();
        for (int i = 0; i < 5; i++)
        {
            await _service.CreateAsync(BuildValidRequest(template.Id), null);
        }

        // Act
        var result = await _service.GetPagedAsync(new CampaignFilter
        {
            Page = 1,
            PageSize = 2
        });

        // Assert
        result.Items.Should().HaveCount(2);
        result.TotalPages.Should().BeGreaterThanOrEqualTo(3);
    }
}

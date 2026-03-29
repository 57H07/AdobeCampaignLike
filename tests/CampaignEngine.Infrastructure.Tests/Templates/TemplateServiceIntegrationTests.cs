using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using CampaignEngine.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Templates;

/// <summary>
/// Integration tests for TemplateService using the EF Core in-memory provider.
/// Tests the service layer in conjunction with the DbContext to validate
/// complete persistence round-trips and query filter behaviour.
/// </summary>
public class TemplateServiceIntegrationTests : DbContextTestBase
{
    private readonly TemplateService _service;

    public TemplateServiceIntegrationTests()
    {
        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        var templateRepository = new TemplateRepository(Context);
        var unitOfWork = new UnitOfWork(Context);
        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object, manifestService.Object, parserService.Object);
    }

    // ----------------------------------------------------------------
    // Create + Read round-trip
    // ----------------------------------------------------------------

    [Fact]
    public async Task Create_ThenGetById_ReturnsPersistedTemplate()
    {
        // Arrange
        var request = new CreateTemplateRequest
        {
            Name = "API Integration Test",
            Channel = ChannelType.Email,
            BodyPath = "templates/api-integration-test/v1.html",
            Description = "Integration test template"
        };

        // Act
        var created = await _service.CreateAsync(request);
        var retrieved = await _service.GetByIdAsync(created.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.Name.Should().Be("API Integration Test");
        retrieved.BodyPath.Should().Be("templates/api-integration-test/v1.html");
        retrieved.Status.Should().Be(TemplateStatus.Draft);
        retrieved.Version.Should().Be(1);
    }

    // ----------------------------------------------------------------
    // Update
    // ----------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesNameAndBodyPath()
    {
        // Arrange
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Old Name",
            Channel = ChannelType.Letter,
            BodyPath = "templates/old-name/v1.docx"
        });

        // Act
        await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "New Name",
            BodyPath = "templates/new-name/v2.docx",
            Description = "Updated"
        });

        // Re-fetch from context
        var updated = await _service.GetByIdAsync(created.Id);

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("New Name");
        updated.BodyPath.Should().Be("templates/new-name/v2.docx");
        updated.Description.Should().Be("Updated");
    }

    // ----------------------------------------------------------------
    // Soft delete
    // ----------------------------------------------------------------

    [Fact]
    public async Task Delete_MarksIsDeletedTrue_PreservesRecordInDb()
    {
        // Arrange
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Soft Delete Test",
            Channel = ChannelType.Sms,
            BodyPath = "templates/soft-delete-test/v1.txt"
        });

        // Act
        await _service.DeleteAsync(created.Id);

        // Assert — service GetById returns null (filtered out)
        var viaNormalQuery = await _service.GetByIdAsync(created.Id);
        viaNormalQuery.Should().BeNull();

        // Assert — raw DB query bypassing filter shows record still exists
        var rawRecord = await Context.Templates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == created.Id);

        rawRecord.Should().NotBeNull();
        rawRecord!.IsDeleted.Should().BeTrue();
        rawRecord.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ----------------------------------------------------------------
    // Filtered list
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetPaged_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest { Name = "Draft One", Channel = ChannelType.Email, BodyPath = "templates/draft-one/v1.html" });
        await _service.CreateAsync(new CreateTemplateRequest { Name = "Draft Two", Channel = ChannelType.Email, BodyPath = "templates/draft-two/v1.html" });

        // Manually set one template to Published via context (bypassing service for test setup)
        var draftTwo = Context.Templates.First(t => t.Name == "Draft Two");
        draftTwo.Status = TemplateStatus.Published;
        await Context.SaveChangesAsync();

        // Act
        var drafts = await _service.GetPagedAsync(null, TemplateStatus.Draft, 1, 50);
        var published = await _service.GetPagedAsync(null, TemplateStatus.Published, 1, 50);

        // Assert
        drafts.Total.Should().Be(1);
        drafts.Items.Single().Name.Should().Be("Draft One");

        published.Total.Should().Be(1);
        published.Items.Single().Name.Should().Be("Draft Two");
    }

    [Fact]
    public async Task GetPaged_FilterByChannelAndStatus_CombinesFilters()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest { Name = "Email Draft", Channel = ChannelType.Email, BodyPath = "templates/email-draft/v1.html" });
        await _service.CreateAsync(new CreateTemplateRequest { Name = "SMS Draft", Channel = ChannelType.Sms, BodyPath = "templates/sms-draft/v1.txt" });

        // Act
        var result = await _service.GetPagedAsync(ChannelType.Email, TemplateStatus.Draft, 1, 50);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Single().Name.Should().Be("Email Draft");
    }

    // ----------------------------------------------------------------
    // Business rule: unique name per channel
    // ----------------------------------------------------------------

    [Fact]
    public async Task Create_DuplicateNameInSameChannel_ThrowsValidationException()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Promo Newsletter",
            Channel = ChannelType.Email,
            BodyPath = "templates/promo-newsletter/v1.html"
        });

        // Act
        var act = () => _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Promo Newsletter",
            Channel = ChannelType.Email,
            BodyPath = "templates/promo-newsletter-dup/v1.html"
        });

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Create_SameNameDifferentChannel_Succeeds()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Promo Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/promo-email/v1.html"
        });

        // Act — SMS with same name should NOT throw
        var act = () => _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Promo Template",
            Channel = ChannelType.Sms,
            BodyPath = "templates/promo-sms/v1.txt"
        });

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ----------------------------------------------------------------
    // Error cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task Update_NonExistentTemplate_ThrowsNotFoundException()
    {
        var act = () => _service.UpdateAsync(Guid.NewGuid(), new UpdateTemplateRequest
        {
            Name = "Ghost",
            BodyPath = "templates/ghost/v1.html"
        });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_NonExistentTemplate_ThrowsNotFoundException()
    {
        var act = () => _service.DeleteAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for TemplateService using an EF Core in-memory database.
/// Validates business rules: uniqueness constraint, soft delete, CRUD operations.
/// </summary>
public class TemplateServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly TemplateService _service;

    public TemplateServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);
        _service = new TemplateService(
            templateRepository, unitOfWork, logger.Object, manifestService.Object, parserService.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // Create
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesTemplateDraft()
    {
        // Arrange
        var request = new CreateTemplateRequest
        {
            Name = "Welcome Email",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Hello {{ name }}</p>",
            Description = "Welcome message"
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.Name.Should().Be("Welcome Email");
        result.Channel.Should().Be(ChannelType.Email);
        result.Status.Should().Be(TemplateStatus.Draft);
        result.Version.Should().Be(1);
        result.HtmlBody.Should().Be("<p>Hello {{ name }}</p>");
        result.Description.Should().Be("Welcome message");
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameAndChannel_ThrowsValidationException()
    {
        // Arrange
        var firstRequest = new CreateTemplateRequest
        {
            Name = "Newsletter",
            Channel = ChannelType.Email,
            HtmlBody = "<p>First</p>"
        };
        await _service.CreateAsync(firstRequest);

        var duplicateRequest = new CreateTemplateRequest
        {
            Name = "Newsletter",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Duplicate</p>"
        };

        // Act
        var act = () => _service.CreateAsync(duplicateRequest);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Newsletter*Email*");
    }

    [Fact]
    public async Task CreateAsync_SameNameDifferentChannel_Succeeds()
    {
        // Arrange — same name is allowed in different channels
        var emailRequest = new CreateTemplateRequest
        {
            Name = "Monthly Report",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Email report</p>"
        };
        var smsRequest = new CreateTemplateRequest
        {
            Name = "Monthly Report",
            Channel = ChannelType.Sms,
            HtmlBody = "SMS report"
        };

        // Act
        var email = await _service.CreateAsync(emailRequest);
        var sms = await _service.CreateAsync(smsRequest);

        // Assert
        email.Id.Should().NotBe(sms.Id);
        email.Channel.Should().Be(ChannelType.Email);
        sms.Channel.Should().Be(ChannelType.Sms);
    }

    // ----------------------------------------------------------------
    // GetByIdAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingTemplate_ReturnsTemplate()
    {
        // Arrange
        var request = new CreateTemplateRequest
        {
            Name = "Find Me",
            Channel = ChannelType.Letter,
            HtmlBody = "<p>Letter body</p>"
        };
        var created = await _service.CreateAsync(request);

        // Act
        var retrieved = await _service.GetByIdAsync(created.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.Name.Should().Be("Find Me");
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        // Act
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_SoftDeletedTemplate_ReturnsNull()
    {
        // Arrange
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "To Be Deleted",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Gone</p>"
        });
        await _service.DeleteAsync(created.Id);

        // Act — global query filter should exclude soft-deleted records
        var result = await _service.GetByIdAsync(created.Id);

        // Assert
        result.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // GetPagedAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_ReturnsAllNonDeleted()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest { Name = "T1", Channel = ChannelType.Email, HtmlBody = "a" });
        await _service.CreateAsync(new CreateTemplateRequest { Name = "T2", Channel = ChannelType.Sms, HtmlBody = "b" });
        var t3 = await _service.CreateAsync(new CreateTemplateRequest { Name = "T3", Channel = ChannelType.Email, HtmlBody = "c" });
        await _service.DeleteAsync(t3.Id);

        // Act
        var result = await _service.GetPagedAsync(null, null, 1, 50);

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items.Should().NotContain(t => t.Name == "T3");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByChannel_ReturnsMatchingOnly()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest { Name = "EmailT", Channel = ChannelType.Email, HtmlBody = "a" });
        await _service.CreateAsync(new CreateTemplateRequest { Name = "SmsT", Channel = ChannelType.Sms, HtmlBody = "b" });

        // Act
        var result = await _service.GetPagedAsync(ChannelType.Email, null, 1, 50);

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().OnlyContain(t => t.Channel == "Email");
    }

    [Fact]
    public async Task GetPagedAsync_Pagination_ReturnsCorrectPage()
    {
        // Arrange — create 5 templates
        for (int i = 1; i <= 5; i++)
            await _service.CreateAsync(new CreateTemplateRequest { Name = $"T{i}", Channel = ChannelType.Email, HtmlBody = $"body{i}" });

        // Act — page 2 with pageSize 2
        var result = await _service.GetPagedAsync(null, null, 2, 2);

        // Assert
        result.Total.Should().Be(5);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(2);
        result.TotalPages.Should().Be(3);
    }

    // ----------------------------------------------------------------
    // UpdateAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ValidRequest_UpdatesFields()
    {
        // Arrange
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "Original",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Old body</p>"
        });

        // Act
        var updated = await _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "Renamed",
            HtmlBody = "<p>New body</p>",
            Description = "Updated description"
        });

        // Assert
        updated.Name.Should().Be("Renamed");
        updated.HtmlBody.Should().Be("<p>New body</p>");
        updated.Description.Should().Be("Updated description");
        updated.Channel.Should().Be(ChannelType.Email); // Channel unchanged
    }

    [Fact]
    public async Task UpdateAsync_NonExistentId_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.UpdateAsync(Guid.NewGuid(), new UpdateTemplateRequest
        {
            Name = "Ghost",
            HtmlBody = "body"
        });

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_DuplicateNameInSameChannel_ThrowsValidationException()
    {
        // Arrange
        await _service.CreateAsync(new CreateTemplateRequest { Name = "Alpha", Channel = ChannelType.Email, HtmlBody = "a" });
        var beta = await _service.CreateAsync(new CreateTemplateRequest { Name = "Beta", Channel = ChannelType.Email, HtmlBody = "b" });

        // Act — try to rename Beta to Alpha (conflict with existing)
        var act = () => _service.UpdateAsync(beta.Id, new UpdateTemplateRequest
        {
            Name = "Alpha",
            HtmlBody = "b"
        });

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_SameName_DoesNotThrow()
    {
        // Arrange — updating a template's HtmlBody with same Name should not conflict with itself
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "SameName",
            Channel = ChannelType.Email,
            HtmlBody = "<p>Old</p>"
        });

        // Act
        var act = () => _service.UpdateAsync(created.Id, new UpdateTemplateRequest
        {
            Name = "SameName",
            HtmlBody = "<p>New</p>"
        });

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ----------------------------------------------------------------
    // DeleteAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingTemplate_SetsSoftDeleteFlag()
    {
        // Arrange
        var created = await _service.CreateAsync(new CreateTemplateRequest
        {
            Name = "To Delete",
            Channel = ChannelType.Email,
            HtmlBody = "<p>delete me</p>"
        });

        // Act
        await _service.DeleteAsync(created.Id);

        // Assert — directly query with IgnoreQueryFilters to see soft-deleted record
        var deleted = await _context.Templates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == created.Id);

        deleted.Should().NotBeNull();
        deleted!.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.DeleteAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests for Template entity persistence using the in-memory database.
/// Validates entity mappings, query filters, and audit field behaviour.
/// </summary>
public class TemplateRepositoryTests : DbContextTestBase
{
    [Fact]
    public async Task CanSaveAndRetrieveTemplate()
    {
        // Arrange
        var template = new Template
        {
            Name = "Test Email Template",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Draft,
            HtmlBody = "<p>Hello {{ name }}</p>",
            Version = 1
        };

        // Act
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();

        var retrieved = await Context.Templates.FirstOrDefaultAsync(t => t.Id == template.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test Email Template");
        retrieved.Channel.Should().Be(ChannelType.Email);
        retrieved.Status.Should().Be(TemplateStatus.Draft);
        retrieved.HtmlBody.Should().Be("<p>Hello {{ name }}</p>");
    }

    [Fact]
    public async Task SoftDeletedTemplates_AreExcludedFromDefaultQuery()
    {
        // Arrange
        var activeTemplate = new Template
        {
            Name = "Active Template",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Published,
            HtmlBody = "<p>Active</p>",
            Version = 1
        };
        var deletedTemplate = new Template
        {
            Name = "Deleted Template",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Archived,
            HtmlBody = "<p>Deleted</p>",
            Version = 1,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };

        Context.Templates.AddRange(activeTemplate, deletedTemplate);
        await Context.SaveChangesAsync();

        // Act — default query (has global query filter)
        var templates = await Context.Templates.ToListAsync();

        // Assert
        templates.Should().HaveCount(1);
        templates.First().Name.Should().Be("Active Template");
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsIncludingSoftDeleted()
    {
        // Arrange
        var activeTemplate = new Template
        {
            Name = "Active",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Published,
            HtmlBody = "<p>A</p>",
            Version = 1
        };
        var deletedTemplate = new Template
        {
            Name = "Deleted",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Archived,
            HtmlBody = "<p>D</p>",
            Version = 1,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };

        Context.Templates.AddRange(activeTemplate, deletedTemplate);
        await Context.SaveChangesAsync();

        // Act
        var allTemplates = await Context.Templates.IgnoreQueryFilters().ToListAsync();

        // Assert
        allTemplates.Should().HaveCount(2);
    }

    [Fact]
    public async Task AuditFields_AreSetOnCreate()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var template = new Template
        {
            Name = "Audit Test Template",
            Channel = ChannelType.Sms,
            Status = TemplateStatus.Draft,
            HtmlBody = "Hello {{ name }}",
            Version = 1
        };

        // Act
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();

        // Assert
        template.CreatedAt.Should().BeAfter(before);
        template.UpdatedAt.Should().BeAfter(before);
        template.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task PlaceholderManifests_AreCascadeDeleted_WithTemplate()
    {
        // Arrange
        var template = new Template
        {
            Name = "Template With Placeholders",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Draft,
            HtmlBody = "<p>Hello {{ firstname }}</p>",
            Version = 1
        };
        Context.Templates.Add(template);
        await Context.SaveChangesAsync();

        var placeholder = new PlaceholderManifestEntry
        {
            TemplateId = template.Id,
            Key = "firstname",
            Type = PlaceholderType.Scalar,
            IsFromDataSource = true
        };
        Context.PlaceholderManifests.Add(placeholder);
        await Context.SaveChangesAsync();

        // Act — delete template (hard delete for cascade test)
        Context.Templates.Remove(template);
        await Context.SaveChangesAsync();

        // Assert
        var placeholders = await Context.PlaceholderManifests
            .Where(p => p.TemplateId == template.Id)
            .ToListAsync();

        placeholders.Should().BeEmpty();
    }
}

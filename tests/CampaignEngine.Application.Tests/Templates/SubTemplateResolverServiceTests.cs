using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for SubTemplateResolverService.
/// Covers:
///   - ExtractReferences: parse {{> name}} syntax
///   - ResolveAsync: recursive substitution up to MaxDepth
///   - Circular reference detection (ValidateNoCircularReferencesAsync and ResolveAsync)
///
/// US-007: ITemplateBodyStore is mocked to return the BodyPath string as content
/// (preserving existing test data where BodyPath holds the HTML for simplicity).
/// </summary>
public class SubTemplateResolverServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly SubTemplateResolverService _service;

    public SubTemplateResolverServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        var templateRepository = new TemplateRepository(_context);

        // US-007 TASK-007-03: ITemplateBodyStore mock that returns the path string
        // itself as stream content — tests store HTML directly in BodyPath.
        var bodyStoreMock = new Mock<ITemplateBodyStore>();
        bodyStoreMock
            .Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
                (Stream)new MemoryStream(System.Text.Encoding.UTF8.GetBytes(path ?? string.Empty)));

        var logger = new Mock<IAppLogger<SubTemplateResolverService>>();
        _service = new SubTemplateResolverService(templateRepository, bodyStoreMock.Object, logger.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    // ----------------------------------------------------------------
    // ExtractReferences tests
    // ----------------------------------------------------------------

    [Fact]
    public void ExtractReferences_EmptyBody_ReturnsEmpty()
    {
        var result = _service.ExtractReferences(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractReferences_NullBody_ReturnsEmpty()
    {
        var result = _service.ExtractReferences(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractReferences_NoSubTemplates_ReturnsEmpty()
    {
        var html = "<p>Hello {{ name }}</p>";
        var result = _service.ExtractReferences(html);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractReferences_SingleReference_ReturnsSingleEntry()
    {
        var html = "<header>{{> header_block}}</header><p>Body</p>";
        var result = _service.ExtractReferences(html);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("header_block");
    }

    [Fact]
    public void ExtractReferences_MultipleDistinctReferences_ReturnsAll()
    {
        var html = "{{> header}}\n<p>Content</p>\n{{> footer}}";
        var result = _service.ExtractReferences(html);
        result.Should().HaveCount(2);
        result.Select(r => r.Name).Should().BeEquivalentTo(new[] { "header", "footer" });
    }

    [Fact]
    public void ExtractReferences_DuplicateReference_ReturnedOnce()
    {
        var html = "{{> header}} content {{> header}}";
        var result = _service.ExtractReferences(html);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("header");
    }

    [Fact]
    public void ExtractReferences_WithSpaces_ParsesCorrectly()
    {
        var html = "{{>  my_header  }} and {{> footer}}";
        var result = _service.ExtractReferences(html);
        result.Should().HaveCount(2);
        result.Select(r => r.Name).Should().Contain("my_header").And.Contain("footer");
    }

    // ----------------------------------------------------------------
    // ResolveAsync tests — successful resolution
    // ----------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_NoSubTemplateReferences_ReturnsBodyUnchanged()
    {
        var parentId = Guid.NewGuid();
        var html = "<p>Hello {{ name }}</p>";

        var result = await _service.ResolveAsync(parentId, html);

        result.Should().Be(html);
    }

    [Fact]
    public async Task ResolveAsync_SingleSubTemplateReference_InjectsSubTemplateBody()
    {
        // Arrange
        var subTemplate = CreateSubTemplate("company_header", "<header><h1>ACME Corp</h1></header>");
        _context.Templates.Add(subTemplate);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "{{> company_header}}<p>Welcome {{ name }}</p>";

        // Act
        var result = await _service.ResolveAsync(parentId, html);

        // Assert
        result.Should().Contain("<header><h1>ACME Corp</h1></header>");
        result.Should().Contain("<p>Welcome {{ name }}</p>");
        result.Should().NotContain("{{> company_header}}");
    }

    [Fact]
    public async Task ResolveAsync_MultipleDistinctSubTemplates_ResolvesAll()
    {
        // Arrange
        var header = CreateSubTemplate("std_header", "<header>HEADER</header>");
        var footer = CreateSubTemplate("std_footer", "<footer>FOOTER</footer>");
        _context.Templates.AddRange(header, footer);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "{{> std_header}}<p>Body</p>{{> std_footer}}";

        // Act
        var result = await _service.ResolveAsync(parentId, html);

        // Assert
        result.Should().Contain("<header>HEADER</header>");
        result.Should().Contain("<footer>FOOTER</footer>");
        result.Should().Contain("<p>Body</p>");
    }

    [Fact]
    public async Task ResolveAsync_SubTemplateNotFound_LeavesPlaceholderUnchanged()
    {
        // No sub-templates in DB
        var parentId = Guid.NewGuid();
        var html = "{{> missing_block}}<p>Body</p>";

        // Act: should NOT throw — just leave unresolvable placeholders
        var result = await _service.ResolveAsync(parentId, html);

        // The placeholder remains because the sub-template doesn't exist
        result.Should().Contain("{{> missing_block}}");
        result.Should().Contain("<p>Body</p>");
    }

    [Fact]
    public async Task ResolveAsync_TemplateExistsButNotSubTemplate_LeavesPlaceholderUnchanged()
    {
        // A template exists but IsSubTemplate = false
        var template = new Template
        {
            Id = Guid.NewGuid(),
            Name = "regular_template",
            Channel = ChannelType.Email,
            BodyPath = "<p>Regular</p>",
            IsSubTemplate = false,
            Status = TemplateStatus.Draft,
            Version = 1
        };
        _context.Templates.Add(template);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "{{> regular_template}}<p>Body</p>";

        var result = await _service.ResolveAsync(parentId, html);

        // Should not resolve because IsSubTemplate = false
        result.Should().Contain("{{> regular_template}}");
    }

    [Fact]
    public async Task ResolveAsync_RecursiveResolution_ResolvesNestedSubTemplate()
    {
        // Arrange: footer references signature sub-template
        //   parent -> footer -> signature
        var signature = CreateSubTemplate("email_signature", "<p>Best regards, Team</p>");
        var footer = CreateSubTemplate("std_footer", "<footer>{{> email_signature}}</footer>");
        _context.Templates.AddRange(signature, footer);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "<p>Content</p>{{> std_footer}}";

        // Act
        var result = await _service.ResolveAsync(parentId, html);

        // Assert: full recursive resolution
        result.Should().Contain("<footer><p>Best regards, Team</p></footer>");
        result.Should().NotContain("{{> std_footer}}");
        result.Should().NotContain("{{> email_signature}}");
    }

    [Fact]
    public async Task ResolveAsync_SameSubTemplateUsedMultipleTimes_ResolvesAllOccurrences()
    {
        // Arrange
        var divider = CreateSubTemplate("divider", "<hr class=\"divider\" />");
        _context.Templates.Add(divider);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "<p>Section 1</p>{{> divider}}<p>Section 2</p>{{> divider}}<p>Section 3</p>";

        // Act
        var result = await _service.ResolveAsync(parentId, html);

        // Assert
        result.Should().NotContain("{{> divider}}");
        result.Split(new[] { "<hr class=\"divider\" />" }, StringSplitOptions.None)
              .Length.Should().Be(3); // 2 occurrences = 3 parts
    }

    // ----------------------------------------------------------------
    // TASK-007-08: Circular reference detection tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_DirectCircularReference_ThrowsValidationException()
    {
        // Arrange: A references A (self-reference)
        var templateAId = Guid.NewGuid();
        var templateA = new Template
        {
            Id = templateAId,
            Name = "self_referencing",
            Channel = ChannelType.Email,
            BodyPath = "<p>Start</p>{{> self_referencing}}<p>End</p>",
            IsSubTemplate = true,
            Status = TemplateStatus.Draft,
            Version = 1
        };
        _context.Templates.Add(templateA);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "{{> self_referencing}}";

        // Act
        var act = async () => await _service.ResolveAsync(parentId, html);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Circular*");
    }

    [Fact]
    public async Task ResolveAsync_IndirectCircularReference_ThrowsValidationException()
    {
        // Arrange: A -> B -> A
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        var templateA = new Template
        {
            Id = idA,
            Name = "block_a",
            Channel = ChannelType.Email,
            BodyPath = "<p>Block A</p>{{> block_b}}",
            IsSubTemplate = true,
            Status = TemplateStatus.Draft,
            Version = 1
        };
        var templateB = new Template
        {
            Id = idB,
            Name = "block_b",
            Channel = ChannelType.Email,
            BodyPath = "<p>Block B</p>{{> block_a}}",
            IsSubTemplate = true,
            Status = TemplateStatus.Draft,
            Version = 1
        };
        _context.Templates.AddRange(templateA, templateB);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "{{> block_a}}";

        // Act
        var act = async () => await _service.ResolveAsync(parentId, html);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Circular*");
    }

    [Fact]
    public async Task ResolveAsync_MaxDepthExceeded_ThrowsValidationException()
    {
        // Arrange: Create a chain of 6 sub-templates (exceeds MaxDepth = 5)
        // chain_0 -> chain_1 -> chain_2 -> chain_3 -> chain_4 -> chain_5
        const int chainLength = SubTemplateResolverService.MaxDepth + 2;
        var templates = new List<Template>();

        for (int i = 0; i < chainLength; i++)
        {
            var nextRef = i < chainLength - 1 ? $"{{{{> chain_{i + 1}}}}}" : "<p>Leaf</p>";
            templates.Add(new Template
            {
                Id = Guid.NewGuid(),
                Name = $"chain_{i}",
                Channel = ChannelType.Email,
                BodyPath = $"<p>Level {i}</p>{nextRef}",
                IsSubTemplate = true,
                Status = TemplateStatus.Draft,
                Version = 1
            });
        }

        _context.Templates.AddRange(templates);
        await _context.SaveChangesAsync();

        var parentId = Guid.NewGuid();
        var html = "{{> chain_0}}";

        // Act
        var act = async () => await _service.ResolveAsync(parentId, html);

        // Assert: should throw due to max depth exceeded
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{SubTemplateResolverService.MaxDepth}*");
    }

    [Fact]
    public async Task ValidateNoCircularReferences_NoCircle_DoesNotThrow()
    {
        // Arrange: simple linear chain A -> B (no cycle)
        var templateA = CreateSubTemplate("block_a", "{{> block_b}}");
        var templateB = CreateSubTemplate("block_b", "<p>Leaf</p>");
        _context.Templates.AddRange(templateA, templateB);
        await _context.SaveChangesAsync();

        // Act + Assert: should not throw
        var act = async () => await _service.ValidateNoCircularReferencesAsync(templateA.Id);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateNoCircularReferences_WithCircle_ThrowsValidationException()
    {
        // Arrange: A -> B -> C -> A (3-node cycle)
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();

        var templateA = new Template
        {
            Id = idA, Name = "cycle_a", Channel = ChannelType.Email,
            BodyPath = "{{> cycle_b}}", IsSubTemplate = true, Status = TemplateStatus.Draft, Version = 1
        };
        var templateB = new Template
        {
            Id = idB, Name = "cycle_b", Channel = ChannelType.Email,
            BodyPath = "{{> cycle_c}}", IsSubTemplate = true, Status = TemplateStatus.Draft, Version = 1
        };
        var templateC = new Template
        {
            Id = idC, Name = "cycle_c", Channel = ChannelType.Email,
            BodyPath = "{{> cycle_a}}", IsSubTemplate = true, Status = TemplateStatus.Draft, Version = 1
        };
        _context.Templates.AddRange(templateA, templateB, templateC);
        await _context.SaveChangesAsync();

        // Act
        var act = async () => await _service.ValidateNoCircularReferencesAsync(idA);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Circular*");
    }

    [Fact]
    public async Task ValidateNoCircularReferences_IsolatedTemplate_DoesNotThrow()
    {
        // No sub-template references — should be valid
        var template = new Template
        {
            Id = Guid.NewGuid(), Name = "standalone", Channel = ChannelType.Email,
            BodyPath = "<p>No sub-templates here</p>", IsSubTemplate = true,
            Status = TemplateStatus.Draft, Version = 1
        };
        _context.Templates.Add(template);
        await _context.SaveChangesAsync();

        var act = async () => await _service.ValidateNoCircularReferencesAsync(template.Id);
        await act.Should().NotThrowAsync();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static Template CreateSubTemplate(string name, string bodyPath)
    {
        return new Template
        {
            Id = Guid.NewGuid(),
            Name = name,
            Channel = ChannelType.Email,
            BodyPath = bodyPath,
            IsSubTemplate = true,
            Status = TemplateStatus.Draft,
            Version = 1
        };
    }
}

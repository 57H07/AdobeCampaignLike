using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Application.Services;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Tests.Send;

/// <summary>
/// Unit tests for SendRequestValidator.
/// Verifies all business rules:
///   BR-1: Template must be Published.
///   BR-2: Channel must match template channel.
///   BR-3: All placeholder manifest keys must be present in data dictionary.
///   BR-4: Recipient email/phone validated based on channel.
/// </summary>
public class SendRequestValidatorTests
{
    private readonly SendRequestValidator _sut = new();

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static Template PublishedEmailTemplate(params string[] placeholderKeys) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Email Template",
        Channel = ChannelType.Email,
        Status = TemplateStatus.Published,
        HtmlBody = "<p>Hello {{ name }}</p>",
        PlaceholderManifests = placeholderKeys
            .Select(k => new PlaceholderManifestEntry { Key = k, Type = PlaceholderType.Scalar })
            .ToList()
    };

    private static Template PublishedSmsTemplate(params string[] placeholderKeys) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test SMS Template",
        Channel = ChannelType.Sms,
        Status = TemplateStatus.Published,
        HtmlBody = "Hello {{ name }}",
        PlaceholderManifests = placeholderKeys
            .Select(k => new PlaceholderManifestEntry { Key = k, Type = PlaceholderType.Scalar })
            .ToList()
    };

    private static SendRequest ValidEmailRequest(Dictionary<string, object?>? data = null) => new()
    {
        TemplateId = Guid.NewGuid(),
        Channel = ChannelType.Email,
        Data = data ?? new Dictionary<string, object?> { ["name"] = "Alice" },
        Recipient = new SendRecipient { Email = "alice@example.com" }
    };

    private static SendRequest ValidSmsRequest(Dictionary<string, object?>? data = null) => new()
    {
        TemplateId = Guid.NewGuid(),
        Channel = ChannelType.Sms,
        Data = data ?? new Dictionary<string, object?> { ["name"] = "Alice" },
        Recipient = new SendRecipient { PhoneNumber = "+33612345678" }
    };

    // ----------------------------------------------------------------
    // BR-1: Template must be Published
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_PublishedTemplate_NoStatusError()
    {
        var template = PublishedEmailTemplate("name");
        var request = ValidEmailRequest();

        var errors = _sut.Validate(request, template);

        errors.Should().NotContain(e => e.Contains("Published") || e.Contains("status"));
    }

    [Theory]
    [InlineData(TemplateStatus.Draft)]
    [InlineData(TemplateStatus.Archived)]
    public void Validate_NonPublishedTemplate_ReturnsStatusError(TemplateStatus status)
    {
        var template = PublishedEmailTemplate("name");
        template.Status = status;
        var request = ValidEmailRequest();

        var errors = _sut.Validate(request, template);

        errors.Should().ContainSingle(e => e.Contains("not Published") || e.Contains(status.ToString()));
    }

    // ----------------------------------------------------------------
    // BR-2: Channel must match template channel
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_MatchingChannel_NoChannelError()
    {
        var template = PublishedEmailTemplate("name");
        var request = ValidEmailRequest();

        var errors = _sut.Validate(request, template);

        errors.Should().NotContain(e => e.Contains("Channel mismatch"));
    }

    [Fact]
    public void Validate_MismatchedChannel_ReturnsChannelError()
    {
        var template = PublishedEmailTemplate("name");
        var request = ValidEmailRequest();
        request.Channel = ChannelType.Sms; // mismatch: request says SMS, template is Email

        var errors = _sut.Validate(request, template);

        errors.Should().ContainSingle(e => e.Contains("Channel mismatch"));
    }

    // ----------------------------------------------------------------
    // BR-3: All placeholder keys must be in data dictionary
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_AllPlaceholderKeysProvided_NoMissingKeyError()
    {
        var template = PublishedEmailTemplate("name", "city");
        var request = ValidEmailRequest(new Dictionary<string, object?>
        {
            ["name"] = "Alice",
            ["city"] = "Paris"
        });

        var errors = _sut.Validate(request, template);

        errors.Should().NotContain(e => e.Contains("Missing required placeholder"));
    }

    [Fact]
    public void Validate_MissingPlaceholderKey_ReturnsMissingKeyError()
    {
        var template = PublishedEmailTemplate("name", "city");
        var request = ValidEmailRequest(new Dictionary<string, object?>
        {
            ["name"] = "Alice"
            // 'city' is missing
        });

        var errors = _sut.Validate(request, template);

        errors.Should().ContainSingle(e => e.Contains("Missing required placeholder") && e.Contains("'city'"));
    }

    [Fact]
    public void Validate_MultipleMissingKeys_ListsAllMissingKeys()
    {
        var template = PublishedEmailTemplate("name", "city", "zipcode");
        var request = ValidEmailRequest(new Dictionary<string, object?>()); // all missing

        var errors = _sut.Validate(request, template);

        var missingKeyError = errors.FirstOrDefault(e => e.Contains("Missing required placeholder"));
        missingKeyError.Should().NotBeNull();
        missingKeyError!.Should().Contain("'name'");
        missingKeyError.Should().Contain("'city'");
        missingKeyError.Should().Contain("'zipcode'");
    }

    [Fact]
    public void Validate_PlaceholderKeysAreCaseInsensitive()
    {
        var template = PublishedEmailTemplate("Name"); // manifest uses PascalCase

        // Use the SendRequest's own case-insensitive Data property directly
        var request = new SendRequest
        {
            TemplateId = Guid.NewGuid(),
            Channel = ChannelType.Email,
            Recipient = new SendRecipient { Email = "alice@example.com" }
        };
        request.Data["name"] = "Alice"; // lowercase key; manifest declares "Name" (PascalCase)

        var errors = _sut.Validate(request, template);

        errors.Should().NotContain(e => e.Contains("Missing required placeholder"));
    }

    // ----------------------------------------------------------------
    // BR-4: Recipient validation — Email channel
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_EmailChannel_ValidEmail_NoRecipientError()
    {
        var template = PublishedEmailTemplate("name");
        var request = ValidEmailRequest();

        var errors = _sut.Validate(request, template);

        errors.Should().NotContain(e => e.Contains("email"));
    }

    [Fact]
    public void Validate_EmailChannel_MissingEmail_ReturnsError()
    {
        var template = PublishedEmailTemplate("name");
        var request = ValidEmailRequest();
        request.Recipient.Email = null;

        var errors = _sut.Validate(request, template);

        errors.Should().ContainSingle(e => e.Contains("email address is required"));
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("@nodomain.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmailChannel_InvalidEmail_ReturnsFormatError(string invalidEmail)
    {
        var template = PublishedEmailTemplate("name");
        var request = ValidEmailRequest();
        request.Recipient.Email = invalidEmail;

        var errors = _sut.Validate(request, template);

        errors.Should().HaveCountGreaterThan(0);
        errors.Should().Contain(e =>
            e.Contains("email address is required") ||
            e.Contains("not a valid email format"));
    }

    // ----------------------------------------------------------------
    // BR-4: Recipient validation — SMS channel
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_SmsChannel_ValidPhone_NoRecipientError()
    {
        var template = PublishedSmsTemplate("name");
        var request = ValidSmsRequest();

        var errors = _sut.Validate(request, template);

        errors.Should().NotContain(e => e.Contains("phone"));
    }

    [Fact]
    public void Validate_SmsChannel_MissingPhone_ReturnsError()
    {
        var template = PublishedSmsTemplate("name");
        var request = ValidSmsRequest();
        request.Recipient.PhoneNumber = null;

        var errors = _sut.Validate(request, template);

        errors.Should().ContainSingle(e => e.Contains("phone number is required"));
    }

    [Theory]
    [InlineData("0612345678")]           // Missing leading +
    [InlineData("+0612345678")]          // Starts with +0
    [InlineData("hello")]               // Not a number
    [InlineData("+123")]                // Too short
    public void Validate_SmsChannel_InvalidPhone_ReturnsFormatError(string invalidPhone)
    {
        var template = PublishedSmsTemplate("name");
        var request = ValidSmsRequest();
        request.Recipient.PhoneNumber = invalidPhone;

        var errors = _sut.Validate(request, template);

        errors.Should().Contain(e => e.Contains("E.164 format"));
    }

    // ----------------------------------------------------------------
    // Multiple errors returned simultaneously
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_MultipleViolations_ReturnsAllErrors()
    {
        var template = PublishedEmailTemplate("name", "city");
        template.Status = TemplateStatus.Draft; // BR-1 violation

        var request = new SendRequest
        {
            TemplateId = Guid.NewGuid(),
            Channel = ChannelType.Sms,        // BR-2 violation (template is Email)
            Data = new Dictionary<string, object?>(), // BR-3 violation (missing all keys)
            Recipient = new SendRecipient()     // BR-4 violation (no email)
        };

        var errors = _sut.Validate(request, template);

        errors.Count.Should().BeGreaterThanOrEqualTo(3); // status + channel + missing keys
    }

    // ----------------------------------------------------------------
    // Edge: Letter channel — no strict recipient validation
    // ----------------------------------------------------------------

    [Fact]
    public void Validate_LetterChannel_NoRecipientValidation()
    {
        var template = new Template
        {
            Id = Guid.NewGuid(),
            Name = "Letter Template",
            Channel = ChannelType.Letter,
            Status = TemplateStatus.Published,
            HtmlBody = "<p>Dear {{ name }}</p>",
            PlaceholderManifests = new List<PlaceholderManifestEntry>
            {
                new() { Key = "name", Type = PlaceholderType.Scalar }
            }
        };

        var request = new SendRequest
        {
            TemplateId = Guid.NewGuid(),
            Channel = ChannelType.Letter,
            Data = new Dictionary<string, object?> { ["name"] = "Alice" },
            Recipient = new SendRecipient() // no email or phone
        };

        var errors = _sut.Validate(request, template);

        // Letter channel has no strict address constraint at API level
        errors.Should().BeEmpty();
    }
}

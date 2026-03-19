using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Services;

/// <summary>
/// Validates a SendRequest against business rules prior to dispatch.
/// Business rules enforced:
///   BR-1: Template must be Published.
///   BR-2: Channel in request must match the template's channel.
///   BR-3: All placeholder manifest keys must be present in the data dictionary.
///   BR-4: Recipient email required for Email channel; phone number required for SMS channel.
/// </summary>
public class SendRequestValidator : ISendRequestValidator
{
    private static readonly System.Text.RegularExpressions.Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // E.164 phone: starts with +, 7-15 digits
    private static readonly System.Text.RegularExpressions.Regex PhoneRegex =
        new(@"^\+[1-9]\d{6,14}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(SendRequest request, Template template)
    {
        var errors = new List<string>();

        // BR-1: Template must be Published
        if (template.Status != TemplateStatus.Published)
        {
            errors.Add($"Template '{template.Name}' is not Published (current status: {template.Status}). Only Published templates can be used for sending.");
        }

        // BR-2: Channel must match template channel
        if (request.Channel != template.Channel)
        {
            errors.Add($"Channel mismatch: request specifies '{request.Channel}' but template '{template.Name}' is configured for '{template.Channel}'.");
        }

        // BR-3: All declared placeholder manifest keys must be present in data dictionary
        var missingKeys = template.PlaceholderManifests
            .Where(p => !request.Data.ContainsKey(p.Key))
            .Select(p => p.Key)
            .ToList();

        if (missingKeys.Count > 0)
        {
            errors.Add($"Missing required placeholder data keys: {string.Join(", ", missingKeys.Select(k => $"'{k}'"))}.");
        }

        // BR-4: Recipient validation per channel
        ValidateRecipient(request, errors);

        return errors.AsReadOnly();
    }

    private static void ValidateRecipient(SendRequest request, List<string> errors)
    {
        if (request.Recipient == null)
        {
            errors.Add("Recipient is required.");
            return;
        }

        switch (request.Channel)
        {
            case ChannelType.Email:
                if (string.IsNullOrWhiteSpace(request.Recipient.Email))
                {
                    errors.Add("Recipient email address is required for Email channel.");
                }
                else if (!EmailRegex.IsMatch(request.Recipient.Email))
                {
                    errors.Add($"Recipient email address '{request.Recipient.Email}' is not a valid email format.");
                }
                break;

            case ChannelType.Sms:
                if (string.IsNullOrWhiteSpace(request.Recipient.PhoneNumber))
                {
                    errors.Add("Recipient phone number is required for SMS channel.");
                }
                else if (!PhoneRegex.IsMatch(request.Recipient.PhoneNumber))
                {
                    errors.Add($"Recipient phone number '{request.Recipient.PhoneNumber}' must be in E.164 format (e.g. +33612345678).");
                }
                break;

            case ChannelType.Letter:
                // Letter channel: no strict address validation at API level (physical address handled by dispatcher)
                break;
        }
    }
}

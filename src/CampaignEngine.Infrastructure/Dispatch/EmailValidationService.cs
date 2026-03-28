using CampaignEngine.Application.Interfaces;
using MimeKit;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// Email address validation service using MimeKit's MailboxAddress parser.
///
/// US-029 TASK-029-02: Email validation service.
///
/// Business rules:
///   BR-1: Invalid emails logged but do not fail the send.
///   BR-3: Validation uses RFC 5321-compatible parsing (same library as EmailDispatcher).
///
/// Uses MimeKit's MailboxAddress.TryParse for consistent validation with the
/// actual email sending path (EmailDispatcher.IsValidEmail uses the same method).
/// </summary>
public sealed class EmailValidationService : IEmailValidationService
{
    private readonly IAppLogger<EmailValidationService> _logger;

    public EmailValidationService(IAppLogger<EmailValidationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return MailboxAddress.TryParse(email.Trim(), out _);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> FilterValid(IEnumerable<string> addresses, string context)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        var valid = new List<string>();

        foreach (var address in addresses)
        {
            if (string.IsNullOrWhiteSpace(address))
                continue;

            var trimmed = address.Trim();

            if (IsValid(trimmed))
            {
                valid.Add(trimmed);
            }
            else
            {
                _logger.LogWarning(
                    "Invalid email address in {Context} — skipping: '{Address}'",
                    context, trimmed);
            }
        }

        return valid;
    }
}

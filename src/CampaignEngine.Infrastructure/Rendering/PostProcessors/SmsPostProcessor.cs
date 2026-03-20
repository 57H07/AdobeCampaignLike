using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace CampaignEngine.Infrastructure.Rendering.PostProcessors;

/// <summary>
/// Post-processor for the SMS channel.
/// Strips all HTML tags from rendered content and truncates to 160 characters,
/// preserving whole words at the truncation boundary where possible.
///
/// Business rules (US-013):
///   BR-3: SMS truncation: preserve whole words when possible.
///   BR-2: Default limit is 160 characters (GSM-7 standard single SMS frame).
///         Can be overridden via <see cref="PostProcessingContext.SmsMaxLength"/>.
/// </summary>
public sealed class SmsPostProcessor : IChannelPostProcessor
{
    /// <summary>Default SMS character limit (GSM-7 single message).</summary>
    public const int DefaultMaxLength = 160;

    private static readonly Regex MultipleWhitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly ILogger<SmsPostProcessor> _logger;

    public ChannelType Channel => ChannelType.Sms;

    public SmsPostProcessor(ILogger<SmsPostProcessor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<PostProcessingResult> ProcessAsync(
        string renderedHtml,
        PostProcessingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(renderedHtml))
        {
            return Task.FromResult(PostProcessingResult.Text(string.Empty, "text/plain"));
        }

        try
        {
            _logger.LogDebug(
                "Starting SMS HTML stripping. CampaignId={CampaignId} RecipientId={RecipientId}",
                context?.CampaignId,
                context?.RecipientId);

            // Step 1: Parse HTML and extract inner text using HtmlAgilityPack
            var doc = new HtmlDocument();
            doc.LoadHtml(renderedHtml);

            // Remove script and style nodes entirely (don't include their text content)
            RemoveNodes(doc, "//script");
            RemoveNodes(doc, "//style");

            // Extract inner text — HtmlAgilityPack handles nested tags and entities
            var plainText = doc.DocumentNode.InnerText;

            // Step 2: Decode HTML entities (&amp; → &, &lt; → <, &nbsp; → space)
            plainText = WebUtility.HtmlDecode(plainText);

            // Step 3: Normalize whitespace — collapse runs of spaces/tabs/newlines into a single space
            plainText = MultipleWhitespace.Replace(plainText, " ").Trim();

            // Step 4: Truncate with whole-word preservation
            var maxLength = context?.SmsMaxLength ?? DefaultMaxLength;
            var truncated = TruncateWholeWords(plainText, maxLength);

            if (plainText.Length > maxLength)
            {
                _logger.LogInformation(
                    "SMS content truncated from {OriginalLength} to {MaxLength} characters. " +
                    "CampaignId={CampaignId}",
                    plainText.Length,
                    maxLength,
                    context?.CampaignId);
            }

            _logger.LogDebug(
                "SMS HTML stripping completed. OutputLength={Length}. CampaignId={CampaignId}",
                truncated.Length,
                context?.CampaignId);

            return Task.FromResult(PostProcessingResult.Text(truncated, "text/plain"));
        }
        catch (Exception ex) when (ex is not PostProcessingException)
        {
            _logger.LogError(
                ex,
                "SMS post-processing failed. CampaignId={CampaignId}",
                context?.CampaignId);

            throw new PostProcessingException(
                $"SMS post-processing failed: {ex.Message}",
                ex,
                channel: "Sms",
                isTransient: false);
        }
    }

    /// <summary>
    /// Truncates text to at most <paramref name="maxLength"/> characters.
    /// When the text must be cut mid-word, backs up to the last word boundary
    /// to preserve whole words. If no word boundary exists within the limit,
    /// hard-truncates at exactly maxLength.
    /// </summary>
    public static string TruncateWholeWords(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var candidate = text[..maxLength];

        // If the next character after maxLength is a space (or we're at end),
        // the cut falls on a word boundary — return as-is.
        if (maxLength >= text.Length || text[maxLength] == ' ')
        {
            return candidate;
        }

        // We're mid-word — find the last space in the candidate to preserve whole words.
        var lastSpace = candidate.LastIndexOf(' ');

        return lastSpace > 0
            ? candidate[..lastSpace]
            : candidate; // No word boundary found — hard truncate at maxLength
    }

    private static void RemoveNodes(HtmlDocument doc, string xPath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xPath);
        if (nodes is null) return;

        foreach (var node in nodes.ToList())
        {
            node.Remove();
        }
    }
}

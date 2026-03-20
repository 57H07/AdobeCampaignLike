using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using PreMailer.Net;

namespace CampaignEngine.Infrastructure.Rendering.PostProcessors;

/// <summary>
/// Post-processor for the Email channel.
/// Inlines CSS rules into HTML element style attributes using PreMailer.Net,
/// ensuring compatibility with email clients such as Outlook that do not support
/// external or embedded stylesheets.
///
/// Business rule: Email CSS inlining required for Outlook compatibility (US-013 BR-1).
/// </summary>
public sealed class EmailPostProcessor : IChannelPostProcessor
{
    private readonly ILogger<EmailPostProcessor> _logger;

    public ChannelType Channel => ChannelType.Email;

    public EmailPostProcessor(ILogger<EmailPostProcessor> logger)
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
            return Task.FromResult(PostProcessingResult.Text(string.Empty));
        }

        try
        {
            _logger.LogDebug(
                "Starting Email CSS inlining. CampaignId={CampaignId} RecipientId={RecipientId}",
                context?.CampaignId,
                context?.RecipientId);

            // PreMailer.Net: inline all <style> blocks and linked stylesheets into element attributes.
            // Options:
            //   removeStyleElements: true — removes <style> blocks after inlining (reduces email size)
            //   ignoreElements: "#outlook" — preserve Outlook conditional comments untouched
            //   stripIdAndClassAttributes: false — preserve class/id for analytics tracking pixels
            var result = PreMailer.Net.PreMailer.MoveCssInline(
                html: renderedHtml,
                removeStyleElements: true,
                ignoreElements: "#outlook",
                stripIdAndClassAttributes: false,
                removeComments: false);

            if (result.Warnings.Count > 0)
            {
                foreach (var warning in result.Warnings)
                {
                    _logger.LogWarning(
                        "CSS inlining warning. CampaignId={CampaignId} Warning={Warning}",
                        context?.CampaignId,
                        warning);
                }
            }

            _logger.LogDebug(
                "Email CSS inlining completed. CampaignId={CampaignId}",
                context?.CampaignId);

            return Task.FromResult(PostProcessingResult.Text(result.Html, "text/html"));
        }
        catch (Exception ex) when (ex is not PostProcessingException)
        {
            _logger.LogError(
                ex,
                "Email CSS inlining failed. CampaignId={CampaignId}",
                context?.CampaignId);

            throw new PostProcessingException(
                $"Email CSS inlining failed: {ex.Message}",
                ex,
                channel: "Email",
                isTransient: false);
        }
    }
}

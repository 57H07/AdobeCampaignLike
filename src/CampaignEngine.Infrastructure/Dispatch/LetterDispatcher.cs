using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// IChannelDispatcher implementation for the Letter channel.
///
/// US-023 (F-403): Rewritten dispatcher that writes one DOCX file per recipient
/// via PrintProviderFileDropHandler. No PDF consolidation, no CSV manifest,
/// no FlushBatchAsync. Accepts DispatchRequest.BinaryContent (pre-rendered DOCX bytes).
///
/// Dispatch flow for a single letter:
///   1. Check channel is enabled.
///   2. Validate BinaryContent is not null/empty — permanent failure if so.
///   3. Write DOCX to output directory with naming {campaignId}_{recipientId}_{timestamp}.docx.
///   4. Return DispatchResult.Ok on success; map I/O errors to LetterDispatchException (transient).
///
/// Business rules (US-023):
///   BR-1: One SendAsync call = one DOCX file written.
///   BR-2: No batch accumulation or consolidation.
///   BR-3: File naming: {campaignId}_{recipientId}_{timestamp}.docx
/// </summary>
public class LetterDispatcher : IChannelDispatcher
{
    private readonly PrintProviderFileDropHandler _fileDropHandler;
    private readonly LetterOptions _options;
    private readonly ILogger<LetterDispatcher> _logger;

    public ChannelType Channel => ChannelType.Letter;

    public LetterDispatcher(
        PrintProviderFileDropHandler fileDropHandler,
        IOptions<LetterOptions> options,
        ILogger<LetterDispatcher> logger)
    {
        _fileDropHandler = fileDropHandler;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Writes a single DOCX file for the recipient.
    ///
    /// Returns <see cref="DispatchResult.Fail"/> (permanent) if the channel is disabled or
    /// BinaryContent is null/empty.
    ///
    /// Throws <see cref="LetterDispatchException"/> (transient) on I/O failure — propagates
    /// from PrintProviderFileDropHandler to allow Hangfire retry.
    /// </summary>
    public async Task<DispatchResult> SendAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        // ----------------------------------------------------------------
        // 1. Channel enabled check
        // ----------------------------------------------------------------
        if (!_options.IsEnabled)
        {
            _logger.LogWarning(
                "Letter channel is disabled. Skipping send for recipient '{RecipientId}'.",
                request.Recipient.ExternalRef ?? request.Recipient.DisplayName);
            return DispatchResult.Fail("Letter channel is disabled in configuration.", isTransient: false);
        }

        // ----------------------------------------------------------------
        // 2. BinaryContent validation (TASK-023-04)
        // ----------------------------------------------------------------
        if (request.BinaryContent is null || request.BinaryContent.Length == 0)
        {
            _logger.LogWarning(
                "Letter send skipped: BinaryContent is null or empty. CampaignId={CampaignId}",
                request.CampaignId);
            return DispatchResult.Fail(
                "Letter channel requires non-empty BinaryContent (DOCX bytes).",
                isTransient: false);
        }

        var campaignId = request.CampaignId ?? Guid.Empty;
        var recipientId = request.Recipient.ExternalRef ?? request.Recipient.DisplayName ?? "unknown";

        // ----------------------------------------------------------------
        // 3. Write DOCX file via PrintProviderFileDropHandler (TASK-023-02/03)
        //    File naming: {campaignId}_{recipientId}_{timestamp}.docx
        //    I/O failures propagate as LetterDispatchException (TASK-023-05)
        // ----------------------------------------------------------------
        var timestamp = DateTime.UtcNow;
        var filePath = await _fileDropHandler.WriteFileAsync(
            request.BinaryContent,
            campaignId,
            recipientId,
            timestamp,
            cancellationToken);

        var messageId = $"LETTER-{campaignId:N}-{recipientId}";

        _logger.LogInformation(
            "DOCX written for recipient '{RecipientId}'. Path={FilePath} CampaignId={CampaignId}",
            recipientId,
            filePath,
            campaignId);

        return DispatchResult.Ok(messageId: messageId);
    }
}

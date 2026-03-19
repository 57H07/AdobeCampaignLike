using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Send;

/// <summary>
/// Orchestrates a single transactional send end-to-end:
///   1. Resolves the template from the database (must be Published).
///   2. Validates the request against business rules.
///   3. Renders the template with Scriban.
///   4. Logs the send attempt (Pending) to SEND_LOG.
///   5. Dispatches via the registered channel dispatcher.
///   6. Updates the SEND_LOG entry (Sent / Failed).
///   7. Returns a SendResponse with a unique tracking ID.
///
/// This service is designed for synchronous, inline sends (no background queue).
/// Response time target: &lt; 500ms at p95 (Business Rule BR-4).
/// </summary>
public sealed class SingleSendService : ISingleSendService
{
    // Sentinel campaign ID used for API sends that have no associated campaign.
    // The SEND_LOG.CampaignId is non-nullable in the schema, so we use a well-known zero GUID.
    internal static readonly Guid ApiSendCampaignId = Guid.Empty;

    private readonly CampaignEngineDbContext _dbContext;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IChannelDispatcherRegistry _dispatcherRegistry;
    private readonly ISendRequestValidator _validator;
    private readonly ISendLogService _sendLogService;
    private readonly IAppLogger<SingleSendService> _logger;

    public SingleSendService(
        CampaignEngineDbContext dbContext,
        ITemplateRenderer templateRenderer,
        IChannelDispatcherRegistry dispatcherRegistry,
        ISendRequestValidator validator,
        ISendLogService sendLogService,
        IAppLogger<SingleSendService> logger)
    {
        _dbContext = dbContext;
        _templateRenderer = templateRenderer;
        _dispatcherRegistry = dispatcherRegistry;
        _validator = validator;
        _sendLogService = sendLogService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SendResponse> SendAsync(
        SendRequest request,
        CancellationToken cancellationToken = default)
    {
        var trackingId = Guid.NewGuid();

        _logger.LogInformation(
            "Single send initiated. TrackingId={TrackingId}, TemplateId={TemplateId}, Channel={Channel}",
            trackingId, request.TemplateId, request.Channel);

        // ----------------------------------------------------------------
        // Step 1: Resolve template (must exist and not be soft-deleted)
        // ----------------------------------------------------------------
        var template = await _dbContext.Templates
            .Include(t => t.PlaceholderManifests)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

        if (template == null)
        {
            _logger.LogWarning(
                "Single send failed — template not found. TrackingId={TrackingId}, TemplateId={TemplateId}",
                trackingId, request.TemplateId);

            throw new NotFoundException("Template", request.TemplateId);
        }

        // ----------------------------------------------------------------
        // Step 2: Validate request against business rules
        // ----------------------------------------------------------------
        var errors = _validator.Validate(request, template);
        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Single send validation failed. TrackingId={TrackingId}, Errors={Errors}",
                trackingId, string.Join("; ", errors));

            throw new ValidationException(
                new Dictionary<string, string[]>
                {
                    ["request"] = errors.ToArray()
                });
        }

        // ----------------------------------------------------------------
        // Step 3: Render template with provided data
        // ----------------------------------------------------------------
        var context = new TemplateContext
        {
            Data = request.Data,
            HtmlEncodeValues = request.Channel != ChannelType.Sms
        };

        string renderedContent;
        try
        {
            renderedContent = await _templateRenderer.RenderAsync(template.HtmlBody, context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Template rendering failed. TrackingId={TrackingId}, TemplateId={TemplateId}",
                trackingId, request.TemplateId);

            return SendResponse.Fail(trackingId, request.Channel,
                $"Template rendering failed: {ex.Message}");
        }

        // ----------------------------------------------------------------
        // Step 4: Verify dispatcher is available for the channel
        // ----------------------------------------------------------------
        if (!_dispatcherRegistry.HasDispatcher(request.Channel))
        {
            var noDispatcherError = $"No dispatcher registered for channel '{request.Channel}'.";
            _logger.LogWarning(
                "Dispatch failed — no dispatcher registered. TrackingId={TrackingId}, Channel={Channel}",
                trackingId, request.Channel);

            return SendResponse.Fail(trackingId, request.Channel, noDispatcherError);
        }

        // ----------------------------------------------------------------
        // Step 5: Log as Pending before dispatch
        // ----------------------------------------------------------------
        var recipientAddress = request.Channel == ChannelType.Sms
            ? request.Recipient.PhoneNumber ?? string.Empty
            : request.Recipient.Email ?? string.Empty;

        var sendLogId = await _sendLogService.LogPendingAsync(
            campaignId: ApiSendCampaignId,
            campaignStepId: null,
            channel: request.Channel,
            recipientAddress: recipientAddress,
            recipientId: request.Recipient.ExternalRef,
            correlationId: trackingId.ToString(),
            cancellationToken: cancellationToken);

        // ----------------------------------------------------------------
        // Step 6: Dispatch via channel dispatcher
        // ----------------------------------------------------------------
        var dispatcher = _dispatcherRegistry.GetDispatcher(request.Channel);

        var dispatchRequest = new DispatchRequest
        {
            Channel = request.Channel,
            Content = renderedContent,
            Recipient = new RecipientInfo
            {
                Email = request.Recipient.Email,
                PhoneNumber = request.Recipient.PhoneNumber,
                DisplayName = request.Recipient.DisplayName,
                ExternalRef = request.Recipient.ExternalRef
            }
        };

        DispatchResult dispatchResult;
        try
        {
            dispatchResult = await dispatcher.SendAsync(dispatchRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Dispatcher threw an exception. TrackingId={TrackingId}, Channel={Channel}",
                trackingId, request.Channel);

            await _sendLogService.LogFailedAsync(sendLogId, ex.Message, retryCount: 0, cancellationToken);

            return SendResponse.Fail(trackingId, request.Channel,
                $"Dispatch error: {ex.Message}");
        }

        // ----------------------------------------------------------------
        // Step 7: Update send log and build response
        // ----------------------------------------------------------------
        if (dispatchResult.Success)
        {
            await _sendLogService.LogSentAsync(sendLogId, dispatchResult.SentAt, cancellationToken);

            _logger.LogInformation(
                "Single send succeeded. TrackingId={TrackingId}, Channel={Channel}, MessageId={MessageId}",
                trackingId, request.Channel, dispatchResult.MessageId);

            return SendResponse.Ok(
                trackingId,
                request.Channel,
                dispatchResult.SentAt,
                dispatchResult.MessageId);
        }
        else
        {
            await _sendLogService.LogFailedAsync(
                sendLogId,
                dispatchResult.ErrorDetail ?? "Dispatch failed with an unknown error.",
                retryCount: 0,
                cancellationToken);

            _logger.LogWarning(
                "Single send failed at dispatch. TrackingId={TrackingId}, Channel={Channel}, Error={Error}",
                trackingId, request.Channel, dispatchResult.ErrorDetail);

            return SendResponse.Fail(trackingId, request.Channel,
                dispatchResult.ErrorDetail ?? "Dispatch failed with an unknown error.");
        }
    }
}

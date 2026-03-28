using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Enums;
using Hangfire;
using System.Text.Json;

namespace CampaignEngine.Infrastructure.Batch;

/// <summary>
/// Hangfire background job that processes a single campaign chunk.
///
/// Responsibilities:
///   1. Loads the CampaignChunk including recipient data.
///   2. Resolves the template snapshot for the step.
///   3. Renders the template for each recipient.
///   4. Dispatches via the registered channel dispatcher with logging.
///   5. Reports completion metrics to IChunkCoordinatorService.
///
/// Chunk-level retry policy (TASK-035-03):
///   Hangfire AutomaticRetry is enabled with 3 attempts for transient
///   infrastructure failures (e.g. DbContext failure, job queue issue).
///   Individual send-level retries with exponential backoff (30s/2min/10min)
///   are handled by IRetryPolicy within the dispatch loop.
///   OnAttemptsExceeded = Delete removes the job from failed queue after exhaustion.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 3600)]
[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public sealed class ProcessChunkJob : IProcessChunkJob
{
    private readonly ICampaignChunkRepository _chunkRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ILoggingDispatchOrchestrator _dispatchOrchestrator;
    private readonly IChunkCoordinatorService _coordinator;
    private readonly ICcResolutionService _ccResolution;
    private readonly IAppLogger<ProcessChunkJob> _logger;

    public ProcessChunkJob(
        ICampaignChunkRepository chunkRepository,
        IUnitOfWork unitOfWork,
        ITemplateRenderer templateRenderer,
        ILoggingDispatchOrchestrator dispatchOrchestrator,
        IChunkCoordinatorService coordinator,
        ICcResolutionService ccResolution,
        IAppLogger<ProcessChunkJob> logger)
    {
        _chunkRepository = chunkRepository;
        _unitOfWork = unitOfWork;
        _templateRenderer = templateRenderer;
        _dispatchOrchestrator = dispatchOrchestrator;
        _coordinator = coordinator;
        _ccResolution = ccResolution;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(Guid chunkId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ProcessChunkJob: starting chunk {ChunkId}", chunkId);

        // ----------------------------------------------------------------
        // 1. Load chunk and step with snapshot
        // ----------------------------------------------------------------
        var chunk = await _chunkRepository.GetWithDetailsAsync(chunkId, cancellationToken);

        if (chunk is null)
        {
            _logger.LogError(new InvalidOperationException($"Chunk {chunkId} not found"),
                "ProcessChunkJob: chunk {ChunkId} not found — skipping", chunkId);
            return;
        }

        if (chunk.Status == ChunkStatus.Completed || chunk.Status == ChunkStatus.Failed)
        {
            _logger.LogWarning(
                "ProcessChunkJob: chunk {ChunkId} already in terminal status {Status} — skipping",
                chunkId, chunk.Status);
            return;
        }

        // Mark as processing
        chunk.Status = ChunkStatus.Processing;
        chunk.StartedAt ??= DateTime.UtcNow;
        await _unitOfWork.CommitAsync(cancellationToken);

        var step = chunk.CampaignStep;
        if (step is null)
        {
            await _coordinator.RecordChunkFailureAsync(chunkId, "CampaignStep not found on chunk", cancellationToken);
            return;
        }

        var snapshot = step.TemplateSnapshot;
        if (snapshot is null)
        {
            await _coordinator.RecordChunkFailureAsync(chunkId, "TemplateSnapshot not set on CampaignStep", cancellationToken);
            return;
        }

        // ----------------------------------------------------------------
        // 2. Deserialize recipient data
        // ----------------------------------------------------------------
        List<Dictionary<string, object?>> recipients;
        try
        {
            recipients = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(
                chunk.RecipientDataJson) ?? [];
        }
        catch (JsonException ex)
        {
            await _coordinator.RecordChunkFailureAsync(
                chunkId,
                $"Failed to deserialize recipient data: {ex.Message}",
                cancellationToken);
            return;
        }

        // ----------------------------------------------------------------
        // 3. Process each recipient
        // ----------------------------------------------------------------
        var successCount = 0;
        var failureCount = 0;
        var correlationId = $"chunk-{chunkId:N}";

        foreach (var recipient in recipients)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Render template for this recipient
                var recipientData = recipient.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value);

                var renderedContent = await _templateRenderer.RenderAsync(
                    snapshot.ResolvedHtmlBody,
                    recipientData,
                    cancellationToken);

                // Build dispatch request
                var (recipientEmail, recipientPhone) = ResolveRecipientAddress(recipient, step.Channel);
                if (string.IsNullOrWhiteSpace(recipientEmail) && string.IsNullOrWhiteSpace(recipientPhone))
                {
                    _logger.LogWarning(
                        "ProcessChunkJob: Chunk {ChunkId} — recipient has no address for channel {Channel}, skipping",
                        chunkId, (object)step.Channel.ToString());
                    failureCount++;
                    continue;
                }

                var dispatchRequest = new DispatchRequest
                {
                    Channel = step.Channel,
                    Content = renderedContent,
                    Recipient = new RecipientInfo
                    {
                        Email = recipientEmail,
                        PhoneNumber = recipientPhone
                    },
                    CampaignId = chunk.CampaignId,
                    CampaignStepId = chunk.CampaignStepId
                };

                // Apply CC/BCC from campaign if Email channel (US-029)
                if (step.Channel == ChannelType.Email && chunk.Campaign is not null)
                {
                    // Resolve CC: static + dynamic, validated, deduplicated, capped at 10
                    var ccAddresses = _ccResolution.ResolveCc(
                        chunk.Campaign.StaticCcAddresses,
                        chunk.Campaign.DynamicCcField,
                        recipient);

                    if (ccAddresses.Count > 0)
                        dispatchRequest.CcAddresses = [.. ccAddresses];

                    // Resolve BCC: static, validated, deduplicated
                    var bccAddresses = _ccResolution.ResolveBcc(chunk.Campaign.StaticBccAddresses);

                    if (bccAddresses.Count > 0)
                        dispatchRequest.BccAddresses = [.. bccAddresses];
                }

                var (_, result) = await _dispatchOrchestrator.SendWithLoggingAsync(
                    dispatchRequest,
                    correlationId: correlationId,
                    cancellationToken: cancellationToken);

                if (result.Success)
                    successCount++;
                else
                    failureCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ProcessChunkJob: Chunk {ChunkId} — failed to dispatch recipient: {Error}",
                    chunkId, ex.Message);
                failureCount++;
            }
        }

        // ----------------------------------------------------------------
        // 4. Report completion to coordinator
        // ----------------------------------------------------------------
        _logger.LogInformation(
            "ProcessChunkJob: Chunk {ChunkId} finished. Success={Success}, Failed={Failed}",
            chunkId, successCount, failureCount);

        await _coordinator.RecordChunkCompletionAsync(
            chunkId,
            successCount,
            failureCount,
            cancellationToken);
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns (email, phone) tuple from recipient data based on channel type.
    /// Attempts common field name conventions from the data source.
    /// </summary>
    private static (string? Email, string? Phone) ResolveRecipientAddress(
        Dictionary<string, object?> recipient,
        ChannelType channel)
    {
        if (channel == ChannelType.Email)
        {
            var emailCandidates = new[] { "email", "Email", "EMAIL", "email_address", "EmailAddress", "recipient_email" };
            foreach (var key in emailCandidates)
            {
                if (recipient.TryGetValue(key, out var value) && value is not null)
                    return (value.ToString(), null);
            }
            return (null, null);
        }

        if (channel == ChannelType.Sms)
        {
            var phoneCandidates = new[] { "phone", "Phone", "mobile", "Mobile", "PhoneNumber", "phone_number" };
            foreach (var key in phoneCandidates)
            {
                if (recipient.TryGetValue(key, out var value) && value is not null)
                    return (null, value.ToString());
            }
            return (null, null);
        }

        return (null, null);
    }
}

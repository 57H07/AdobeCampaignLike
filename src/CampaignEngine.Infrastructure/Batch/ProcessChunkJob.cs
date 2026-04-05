using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Application.Interfaces.Storage;
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
///   3. Renders the template for each recipient:
///        - Letter channel: reads DOCX from ITemplateBodyStore, renders with IDocxTemplateRenderer,
///          sets DispatchRequest.BinaryContent, calls LetterDispatcher.SendAsync once per recipient.
///        - Email/SMS: renders HTML/text with ITemplateRenderer, sets Content.
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
    private readonly IDocxTemplateRenderer _docxTemplateRenderer;
    private readonly ITemplateBodyStore _templateBodyStore;
    private readonly ILoggingDispatchOrchestrator _dispatchOrchestrator;
    private readonly IChunkCoordinatorService _coordinator;
    private readonly ICcResolutionService _ccResolution;
    private readonly IAppLogger<ProcessChunkJob> _logger;

    public ProcessChunkJob(
        ICampaignChunkRepository chunkRepository,
        IUnitOfWork unitOfWork,
        ITemplateRenderer templateRenderer,
        IDocxTemplateRenderer docxTemplateRenderer,
        ITemplateBodyStore templateBodyStore,
        ILoggingDispatchOrchestrator dispatchOrchestrator,
        IChunkCoordinatorService coordinator,
        ICcResolutionService ccResolution,
        IAppLogger<ProcessChunkJob> logger)
    {
        _chunkRepository = chunkRepository;
        _unitOfWork = unitOfWork;
        _templateRenderer = templateRenderer;
        _docxTemplateRenderer = docxTemplateRenderer;
        _templateBodyStore = templateBodyStore;
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
        // 2. For Letter channel: load DOCX bytes once (shared across all recipients)
        //    snapshot.ResolvedHtmlBody holds the BodyPath for Letter templates
        // ----------------------------------------------------------------
        byte[]? docxTemplateBytes = null;
        if (step.Channel == ChannelType.Letter)
        {
            var docxBodyPath = snapshot.ResolvedHtmlBody;
            if (string.IsNullOrWhiteSpace(docxBodyPath))
            {
                await _coordinator.RecordChunkFailureAsync(
                    chunkId,
                    "TemplateSnapshot.ResolvedHtmlBody is empty — DOCX body path not set for Letter channel",
                    cancellationToken);
                return;
            }

            try
            {
                using var docxStream = await _templateBodyStore.ReadAsync(docxBodyPath, cancellationToken);
                using var ms = new MemoryStream();
                await docxStream.CopyToAsync(ms, cancellationToken);
                docxTemplateBytes = ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ProcessChunkJob: Chunk {ChunkId} — failed to read DOCX template from store path '{DocxBodyPath}': {Error}",
                    chunkId, snapshot.ResolvedHtmlBody, ex.Message);
                await _coordinator.RecordChunkFailureAsync(
                    chunkId,
                    $"Failed to read DOCX template from store: {ex.Message}",
                    cancellationToken);
                return;
            }
        }

        // ----------------------------------------------------------------
        // 3. Deserialize recipient data
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
        // 4. Process each recipient
        // ----------------------------------------------------------------
        var successCount = 0;
        var failureCount = 0;
        var correlationId = $"chunk-{chunkId:N}";

        foreach (var recipient in recipients)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                DispatchRequest dispatchRequest;

                if (step.Channel == ChannelType.Letter)
                {
                    // Letter channel: render DOCX per recipient (TASK-021-02/03)
                    dispatchRequest = await BuildLetterDispatchRequestAsync(
                        docxTemplateBytes!,
                        recipient,
                        chunk,
                        step,
                        cancellationToken);
                }
                else
                {
                    // Email / SMS channel: render HTML/text template
                    dispatchRequest = await BuildTextDispatchRequestAsync(
                        snapshot.ResolvedHtmlBody,
                        recipient,
                        chunk,
                        step,
                        cancellationToken);
                }

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
        // 5. Report completion to coordinator
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
    /// Builds a DispatchRequest for the Letter channel by rendering the DOCX template
    /// for a single recipient and setting BinaryContent. (TASK-021-02/03/04)
    /// </summary>
    private async Task<DispatchRequest> BuildLetterDispatchRequestAsync(
        byte[] docxTemplateBytes,
        Dictionary<string, object?> recipient,
        CampaignEngine.Domain.Entities.CampaignChunk chunk,
        CampaignEngine.Domain.Entities.CampaignStep step,
        CancellationToken cancellationToken)
    {
        // Extract scalar values from recipient data (string/numeric/bool → string)
        var scalars = ExtractScalars(recipient);

        // Render DOCX for this specific recipient (TASK-021-02)
        var renderedDocxBytes = await _docxTemplateRenderer.RenderAsync(
            docxTemplateBytes,
            scalars,
            collections: [],
            conditions: [],
            cancellationToken);

        // Resolve recipient address (Letter uses ExternalRef or display name)
        var recipientRef = ResolveLetterRecipientRef(recipient);

        // Build dispatch request with BinaryContent (TASK-021-03)
        return new DispatchRequest
        {
            Channel = step.Channel,
            BinaryContent = renderedDocxBytes,
            Content = null,
            Recipient = new RecipientInfo
            {
                ExternalRef = recipientRef,
                DisplayName = ResolveRecipientDisplayName(recipient)
            },
            CampaignId = chunk.CampaignId,
            CampaignStepId = chunk.CampaignStepId
        };
    }

    /// <summary>
    /// Builds a DispatchRequest for Email/SMS channels by rendering the HTML/text template.
    /// </summary>
    private async Task<DispatchRequest> BuildTextDispatchRequestAsync(
        string resolvedHtmlBody,
        Dictionary<string, object?> recipient,
        CampaignEngine.Domain.Entities.CampaignChunk chunk,
        CampaignEngine.Domain.Entities.CampaignStep step,
        CancellationToken cancellationToken)
    {
        var recipientData = recipient.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);

        var renderedContent = await _templateRenderer.RenderAsync(
            resolvedHtmlBody,
            recipientData,
            cancellationToken);

        var (recipientEmail, recipientPhone) = ResolveRecipientAddress(recipient, step.Channel);
        if (string.IsNullOrWhiteSpace(recipientEmail) && string.IsNullOrWhiteSpace(recipientPhone))
        {
            _logger.LogWarning(
                "ProcessChunkJob: recipient has no address for channel {Channel}, skipping",
                (object)step.Channel.ToString());
            throw new InvalidOperationException(
                $"Recipient has no address for channel {step.Channel}.");
        }

        return new DispatchRequest
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
    }

    /// <summary>
    /// Extracts scalar string values from recipient data for DOCX placeholder substitution.
    /// Only scalar (non-collection) values are extracted; nested objects and arrays are skipped.
    /// </summary>
    private static Dictionary<string, string> ExtractScalars(Dictionary<string, object?> recipient)
    {
        var scalars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in recipient)
        {
            if (value is null)
                continue;

            // Skip nested objects (JsonElement with Object or Array kind)
            if (value is System.Text.Json.JsonElement element)
            {
                if (element.ValueKind is System.Text.Json.JsonValueKind.Object
                    or System.Text.Json.JsonValueKind.Array)
                    continue;

                scalars[key] = element.ToString();
            }
            else
            {
                scalars[key] = value.ToString() ?? string.Empty;
            }
        }

        return scalars;
    }

    /// <summary>
    /// Resolves a stable recipient reference string for Letter file naming.
    /// Tries common ID field names before falling back to a generated token.
    /// </summary>
    private static string ResolveLetterRecipientRef(Dictionary<string, object?> recipient)
    {
        var idCandidates = new[]
        {
            "id", "Id", "ID", "recipientId", "RecipientId", "recipient_id",
            "externalRef", "ExternalRef", "external_ref", "ref", "Ref"
        };

        foreach (var key in idCandidates)
        {
            if (recipient.TryGetValue(key, out var value) && value is not null)
            {
                var str = value is System.Text.Json.JsonElement je ? je.ToString() : value.ToString();
                if (!string.IsNullOrWhiteSpace(str))
                    return str!;
            }
        }

        return Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// Resolves a display name for the recipient from common field name conventions.
    /// </summary>
    private static string? ResolveRecipientDisplayName(Dictionary<string, object?> recipient)
    {
        var nameCandidates = new[]
        {
            "fullName", "FullName", "full_name", "name", "Name",
            "displayName", "DisplayName", "firstName", "FirstName", "first_name"
        };

        foreach (var key in nameCandidates)
        {
            if (recipient.TryGetValue(key, out var value) && value is not null)
            {
                var str = value is System.Text.Json.JsonElement je ? je.ToString() : value.ToString();
                if (!string.IsNullOrWhiteSpace(str))
                    return str;
            }
        }

        return null;
    }

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

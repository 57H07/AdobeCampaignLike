using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using Hangfire;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Orchestrates the Chunk Coordinator pattern for parallel batch processing.
///
/// Flow:
///   1. StartCampaignStepAsync: queries recipients, splits into chunks,
///      persists CampaignChunk rows, enqueues one Hangfire job per chunk.
///   2. RecordChunkCompletionAsync: atomically increments the completion
///      counter using SQL UPDATE...OUTPUT. If this was the last chunk,
///      calls ICampaignCompletionService.FinalizeStepAsync.
///   3. RecordChunkFailureAsync: marks the chunk as failed, schedules
///      retry if MaxRetryAttempts not exceeded.
/// </summary>
public sealed class ChunkCoordinatorService : IChunkCoordinatorService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly ICampaignChunkRepository _chunkRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRecipientChunkingService _chunkingService;
    private readonly IDataSourceConnectorRegistry _connectorRegistry;
    private readonly IConnectionStringEncryptor _encryptor;
    private readonly ICampaignCompletionService _completionService;
    private readonly IBackgroundJobClient _jobClient;
    private readonly BatchProcessingOptions _options;
    private readonly IAppLogger<ChunkCoordinatorService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ChunkCoordinatorService(
        ICampaignRepository campaignRepository,
        ICampaignChunkRepository chunkRepository,
        IUnitOfWork unitOfWork,
        IRecipientChunkingService chunkingService,
        IDataSourceConnectorRegistry connectorRegistry,
        IConnectionStringEncryptor encryptor,
        ICampaignCompletionService completionService,
        IBackgroundJobClient jobClient,
        IOptions<CampaignEngineOptions> options,
        IAppLogger<ChunkCoordinatorService> logger)
    {
        _campaignRepository = campaignRepository;
        _chunkRepository = chunkRepository;
        _unitOfWork = unitOfWork;
        _chunkingService = chunkingService;
        _connectorRegistry = connectorRegistry;
        _encryptor = encryptor;
        _completionService = completionService;
        _jobClient = jobClient;
        _options = options.Value.BatchProcessing;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> StartCampaignStepAsync(
        Guid campaignId,
        Guid stepId,
        CancellationToken cancellationToken = default)
    {
        // ----------------------------------------------------------------
        // 1. Load campaign and step
        // ----------------------------------------------------------------
        var campaign = await _campaignRepository.GetWithDataSourceFieldsAndStepsAsync(campaignId, cancellationToken);

        if (campaign is null)
            throw new NotFoundException("Campaign", campaignId);

        var step = campaign.Steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
            throw new NotFoundException("CampaignStep", stepId);

        // ----------------------------------------------------------------
        // 2. Query recipients from data source
        // ----------------------------------------------------------------
        IReadOnlyList<IDictionary<string, object?>> recipients;

        if (campaign.DataSource is null)
        {
            // No data source — treat as zero recipients (supports test campaigns)
            recipients = Array.Empty<IDictionary<string, object?>>();
            _logger.LogWarning(
                "Campaign {CampaignId} has no data source — processing with 0 recipients",
                campaignId);
        }
        else
        {
            var plainCs = _encryptor.Decrypt(campaign.DataSource.EncryptedConnectionString);
            var fieldDtos = campaign.DataSource.Fields
                .Select(f => new FieldDefinitionDto
                {
                    FieldName = f.FieldName,
                    DisplayName = f.FieldName,
                    FieldType = f.DataType,
                    IsFilterable = f.IsFilterable
                })
                .ToList();

            var definition = new DataSourceDefinitionDto
            {
                Id = campaign.DataSource.Id,
                Name = campaign.DataSource.Name,
                Type = campaign.DataSource.Type,
                ConnectionString = plainCs,
                Fields = fieldDtos
            };

            var connector = _connectorRegistry.GetConnector(campaign.DataSource.Type);
            recipients = await connector.QueryAsync(definition, null, cancellationToken);
        }

        // ----------------------------------------------------------------
        // 3. Split recipients into chunks
        // ----------------------------------------------------------------
        var chunks = _chunkingService.Split(recipients, _options.ChunkSize);

        _logger.LogInformation(
            "Campaign {CampaignId} Step {StepId}: splitting {RecipientCount} recipients into {ChunkCount} chunks of {ChunkSize}",
            campaignId, stepId, recipients.Count, chunks.Count, _options.ChunkSize);

        // ----------------------------------------------------------------
        // 4. Persist CampaignChunk entities and update campaign status
        // ----------------------------------------------------------------
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            campaign.Status = CampaignStatus.Running;
            campaign.StartedAt ??= DateTime.UtcNow;
            campaign.TotalRecipients = recipients.Count;
            campaign.ProcessedCount = 0;
            campaign.SuccessCount = 0;
            campaign.FailureCount = 0;

            var chunkEntities = new List<CampaignChunk>();

            foreach (var chunk in chunks)
            {
                var recipientJson = JsonSerializer.Serialize(chunk.Recipients, JsonOptions);

                var chunkEntity = new CampaignChunk
                {
                    CampaignId = campaignId,
                    CampaignStepId = stepId,
                    ChunkIndex = chunk.ChunkIndex,
                    TotalChunks = chunk.TotalChunks,
                    RecipientCount = chunk.Recipients.Count,
                    RecipientDataJson = recipientJson,
                    Status = ChunkStatus.Pending
                };

                chunkEntities.Add(chunkEntity);
                await _chunkRepository.AddAsync(chunkEntity, cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            // ----------------------------------------------------------------
            // 5. Enqueue one Hangfire job per chunk (after transaction commit)
            // ----------------------------------------------------------------
            foreach (var chunkEntity in chunkEntities)
            {
                var jobId = _jobClient.Enqueue<IProcessChunkJob>(
                    job => job.ExecuteAsync(chunkEntity.Id, CancellationToken.None));

                // Store job ID for dashboard traceability
                chunkEntity.HangfireJobId = jobId;
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Campaign {CampaignId} Step {StepId}: enqueued {ChunkCount} Hangfire jobs",
                campaignId, stepId, chunkEntities.Count);

            return chunkEntities.Count;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> RecordChunkCompletionAsync(
        Guid chunkId,
        int successCount,
        int failureCount,
        CancellationToken cancellationToken = default)
        => FinalizeChunkAsync(
            chunkId,
            ChunkStatus.Completed,
            successCount,
            failureCount,
            errorMessage: null,
            cancellationToken);

    /// <inheritdoc />
    public async Task RecordChunkFailureAsync(
        Guid chunkId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetTrackedAsync(chunkId, cancellationToken)
            ?? throw new NotFoundException("CampaignChunk", chunkId);

        var truncated = TruncateError(errorMessage);
        chunk.RetryAttempts++;
        chunk.ErrorMessage = truncated;

        if (chunk.RetryAttempts >= _options.MaxRetryAttempts)
        {
            _logger.LogError(
                new InvalidOperationException(truncated),
                "Campaign {CampaignId} Chunk {ChunkId}: permanently failed after {Attempts} attempts",
                chunk.CampaignId, chunkId, chunk.RetryAttempts);

            // Persist retry counter / error message before the terminal transition
            await _unitOfWork.CommitAsync(cancellationToken);

            // Mark chunk as Failed (not Completed) and trigger finalization in one atomic path
            await FinalizeChunkAsync(
                chunkId,
                ChunkStatus.Failed,
                successCount: 0,
                failureCount: chunk.RecipientCount,
                errorMessage: truncated,
                cancellationToken);
            return;
        }

        // Single transaction: reset to Pending + schedule retry + persist job id together.
        // If any step fails, the transaction rolls back and the caller can safely retry
        // without leaving the chunk orphaned (job scheduled but no HangfireJobId persisted,
        // or chunk marked Pending with no job to process it).
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            chunk.Status = ChunkStatus.Pending;

            var delaySeconds = _options.RetryDelaysSeconds.Length > chunk.RetryAttempts - 1
                ? _options.RetryDelaysSeconds[chunk.RetryAttempts - 1]
                : _options.RetryDelaysSeconds[^1];

            var jobId = _jobClient.Schedule<IProcessChunkJob>(
                job => job.ExecuteAsync(chunkId, CancellationToken.None),
                TimeSpan.FromSeconds(delaySeconds));

            chunk.HangfireJobId = jobId;

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogWarning(
                "Campaign {CampaignId} Chunk {ChunkId}: scheduled retry {Attempt}/{Max} in {Delay}s. Job: {JobId}",
                chunk.CampaignId, chunkId, chunk.RetryAttempts, _options.MaxRetryAttempts, delaySeconds, jobId);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Core atomic chunk finalization. Handles both successful completion and permanent failure
    /// through a single code path to avoid state inversion (a failed chunk incorrectly marked Completed).
    /// Uses atomic SQL for:
    ///   1. Chunk status transition (idempotent via Status NOT IN terminal guard).
    ///   2. Campaign counter increments.
    ///   3. Step finalization claim — a single atomic UPDATE of CampaignSteps.ExecutedAt
    ///      combined with a NOT EXISTS check ensures exactly one caller wins the race
    ///      to trigger FinalizeStepAsync, no matter how many chunks complete concurrently.
    /// </summary>
    private async Task<bool> FinalizeChunkAsync(
        Guid chunkId,
        ChunkStatus finalStatus,
        int successCount,
        int failureCount,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var chunk = await _chunkRepository.GetTrackedAsync(chunkId, cancellationToken)
            ?? throw new NotFoundException("CampaignChunk", chunkId);

        var campaignId = chunk.CampaignId;
        var stepId = chunk.CampaignStepId;

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Atomic, idempotent chunk state transition. If another worker already finalized
            // this chunk (e.g., a duplicate Hangfire invocation), rowcount is 0 and we skip.
            var chunkRowCount = await _unitOfWork.ExecuteSqlRawAsync(
                """
                UPDATE CampaignChunks SET
                    Status = {0},
                    SuccessCount = {1},
                    FailureCount = {2},
                    ProcessedCount = {3},
                    CompletedAt = GETUTCDATE(),
                    ErrorMessage = COALESCE({4}, ErrorMessage)
                WHERE Id = {5} AND Status NOT IN (3, 4)
                """,
                cancellationToken,
                (int)finalStatus,
                successCount,
                failureCount,
                successCount + failureCount,
                (object?)errorMessage ?? DBNull.Value,
                chunkId);

            if (chunkRowCount == 0)
            {
                _logger.LogWarning(
                    "Chunk {ChunkId} already in terminal status — skipping duplicate finalization",
                    chunkId);
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return false;
            }

            // Atomic counter increments on the campaign row.
            await _unitOfWork.ExecuteSqlRawAsync(
                """
                UPDATE Campaigns SET
                    ProcessedCount = ProcessedCount + {0},
                    SuccessCount = SuccessCount + {1},
                    FailureCount = FailureCount + {2},
                    UpdatedAt = GETUTCDATE()
                WHERE Id = {3}
                """,
                cancellationToken,
                successCount + failureCount,
                successCount,
                failureCount,
                campaignId);

            // Atomic step finalization claim. Exactly one caller can transition
            // ExecutedAt from NULL to a timestamp AND observe zero non-terminal
            // chunks in the same statement. All other concurrent callers see @@ROWCOUNT = 0.
            var claimed = await _unitOfWork.ExecuteSqlRawAsync(
                """
                UPDATE CampaignSteps SET ExecutedAt = GETUTCDATE()
                WHERE Id = {0}
                  AND ExecutedAt IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM CampaignChunks
                      WHERE CampaignStepId = {0} AND Status IN (1, 2)
                  )
                """,
                cancellationToken,
                stepId);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            if (claimed > 0)
            {
                _logger.LogInformation(
                    "Campaign {CampaignId} Step {StepId}: all chunks terminal — claimed finalization",
                    campaignId, stepId);

                await _completionService.FinalizeStepAsync(campaignId, stepId, cancellationToken);
                return true;
            }

            return false;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private static string TruncateError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return "Unknown error";

        return errorMessage.Length <= 2000 ? errorMessage : errorMessage[..2000];
    }

    /// <inheritdoc />
    public async Task<CampaignProgressResult> GetProgressAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _campaignRepository.GetNoTrackingAsync(campaignId, cancellationToken);

        if (campaign is null)
            throw new NotFoundException("Campaign", campaignId);

        var chunkStats = await _chunkRepository.GetStatusCountsAsync(campaignId, cancellationToken);

        var totalChunks = chunkStats.Sum(s => s.Count);
        var completedChunks = chunkStats.Where(s => s.Status == ChunkStatus.Completed).Sum(s => s.Count);
        var pendingChunks = chunkStats.Where(s =>
            s.Status == ChunkStatus.Pending || s.Status == ChunkStatus.Processing).Sum(s => s.Count);
        var failedChunks = chunkStats.Where(s => s.Status == ChunkStatus.Failed).Sum(s => s.Count);

        return new CampaignProgressResult(
            CampaignId: campaignId,
            TotalRecipients: campaign.TotalRecipients,
            ProcessedCount: campaign.ProcessedCount,
            SuccessCount: campaign.SuccessCount,
            FailureCount: campaign.FailureCount,
            TotalChunks: totalChunks,
            CompletedChunks: completedChunks,
            PendingChunks: pendingChunks,
            FailedChunks: failedChunks,
            Status: campaign.Status.ToString());
    }
}

using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
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

    // Need direct DbContext access for raw SQL (ExecuteSqlRawAsync)
    private readonly CampaignEngineDbContext _dbContext;

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
        IAppLogger<ChunkCoordinatorService> logger,
        CampaignEngineDbContext dbContext)
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
        _dbContext = dbContext;
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
        var campaign = await _dbContext.Campaigns
            .Include(c => c.DataSource)
                .ThenInclude(ds => ds!.Fields)
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);

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
    public async Task<bool> RecordChunkCompletionAsync(
        Guid chunkId,
        int successCount,
        int failureCount,
        CancellationToken cancellationToken = default)
    {
        // ----------------------------------------------------------------
        // Atomic SQL UPDATE: increment step completion counter.
        // The chunk that brings CompletedChunks == TotalChunks triggers finalization.
        // ----------------------------------------------------------------
        var chunk = await _chunkRepository.GetTrackedAsync(chunkId, cancellationToken);

        if (chunk is null)
        {
            _logger.LogError(new InvalidOperationException($"Chunk {chunkId} not found"),
                "RecordChunkCompletion: chunk {ChunkId} not found", chunkId);
            return false;
        }

        chunk.Status = ChunkStatus.Completed;
        chunk.SuccessCount = successCount;
        chunk.FailureCount = failureCount;
        chunk.ProcessedCount = successCount + failureCount;
        chunk.CompletedAt = DateTime.UtcNow;

        // Atomically increment campaign processed/success/failure counters
        // using raw SQL UPDATE to avoid EF concurrency races
        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            UPDATE Campaigns SET
                ProcessedCount = ProcessedCount + {0},
                SuccessCount = SuccessCount + {1},
                FailureCount = FailureCount + {2},
                UpdatedAt = GETUTCDATE()
            WHERE Id = {3}
            """,
            successCount + failureCount,
            successCount,
            failureCount,
            chunk.CampaignId);

        await _unitOfWork.CommitAsync(cancellationToken);

        // ----------------------------------------------------------------
        // Completion detection: count remaining non-terminal chunks
        // ----------------------------------------------------------------
        var pendingOrProcessingCount = await _chunkRepository.CountPendingOrProcessingAsync(
            chunk.CampaignStepId, cancellationToken);

        var isLastChunk = pendingOrProcessingCount == 0;

        if (isLastChunk)
        {
            _logger.LogInformation(
                "Campaign {CampaignId} Step {StepId}: all chunks completed — triggering finalization",
                chunk.CampaignId, chunk.CampaignStepId);

            await _completionService.FinalizeStepAsync(
                chunk.CampaignId,
                chunk.CampaignStepId,
                cancellationToken);
        }

        return isLastChunk;
    }

    /// <inheritdoc />
    public async Task RecordChunkFailureAsync(
        Guid chunkId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var chunk = await _chunkRepository.GetTrackedAsync(chunkId, cancellationToken);

        if (chunk is null)
        {
            _logger.LogError(new InvalidOperationException($"Chunk {chunkId} not found"),
                "RecordChunkFailure: chunk {ChunkId} not found", chunkId);
            return;
        }

        chunk.RetryAttempts++;
        chunk.ErrorMessage = errorMessage[..Math.Min(errorMessage.Length, 2000)];

        if (chunk.RetryAttempts >= _options.MaxRetryAttempts)
        {
            chunk.Status = ChunkStatus.Failed;
            chunk.CompletedAt = DateTime.UtcNow;

            _logger.LogError(
                new InvalidOperationException(errorMessage),
                "Campaign {CampaignId} Chunk {ChunkId}: permanently failed after {Attempts} attempts",
                chunk.CampaignId, chunkId, chunk.RetryAttempts);

            await _unitOfWork.CommitAsync(cancellationToken);

            // Check if this was the last chunk (even though it failed)
            await RecordChunkCompletionAsync(chunkId, 0, chunk.RecipientCount, cancellationToken);
        }
        else
        {
            chunk.Status = ChunkStatus.Pending;
            await _unitOfWork.CommitAsync(cancellationToken);

            // Re-enqueue with delay based on retry attempt
            var delaySeconds = _options.RetryDelaysSeconds.Length > chunk.RetryAttempts - 1
                ? _options.RetryDelaysSeconds[chunk.RetryAttempts - 1]
                : _options.RetryDelaysSeconds[^1];

            var jobId = BackgroundJob.Schedule<IProcessChunkJob>(
                job => job.ExecuteAsync(chunkId, CancellationToken.None),
                TimeSpan.FromSeconds(delaySeconds));

            chunk.HangfireJobId = jobId;
            await _unitOfWork.CommitAsync(cancellationToken);

            _logger.LogWarning(
                "Campaign {CampaignId} Chunk {ChunkId}: scheduled retry {Attempt}/{Max} in {Delay}s. Job: {JobId}",
                chunk.CampaignId, chunkId, chunk.RetryAttempts, _options.MaxRetryAttempts, delaySeconds, jobId);
        }
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

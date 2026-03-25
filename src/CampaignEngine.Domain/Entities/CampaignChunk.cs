using CampaignEngine.Domain.Common;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Domain.Entities;

/// <summary>
/// Represents a single batch chunk of recipients for a campaign step.
/// Tracks the lifecycle and progress of each parallel processing unit.
/// Used by the Chunk Coordinator pattern to enable parallel processing
/// without requiring Hangfire Batch (Pro) primitives.
/// </summary>
public class CampaignChunk : AuditableEntity
{
    /// <summary>Parent campaign this chunk belongs to.</summary>
    public Guid CampaignId { get; set; }

    /// <summary>The specific campaign step this chunk is executing.</summary>
    public Guid CampaignStepId { get; set; }

    /// <summary>Zero-based chunk index (used for ordering and deduplication).</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Total number of chunks for the parent step (used for completion detection).</summary>
    public int TotalChunks { get; set; }

    /// <summary>Number of recipients assigned to this chunk.</summary>
    public int RecipientCount { get; set; }

    /// <summary>Serialized JSON array of recipient row data for this chunk.</summary>
    public string RecipientDataJson { get; set; } = string.Empty;

    public ChunkStatus Status { get; set; } = ChunkStatus.Pending;

    public int ProcessedCount { get; set; } = 0;
    public int SuccessCount { get; set; } = 0;
    public int FailureCount { get; set; } = 0;

    public int RetryAttempts { get; set; } = 0;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Hangfire job ID for monitoring purposes.</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>Last error message if status is Failed.</summary>
    public string? ErrorMessage { get; set; }

    // Navigation properties
    public Campaign? Campaign { get; set; }
    public CampaignStep? CampaignStep { get; set; }
}

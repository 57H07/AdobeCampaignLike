namespace CampaignEngine.Application.DTOs.Dispatch;

/// <summary>
/// Result returned by a channel dispatcher after a send attempt.
/// </summary>
public class DispatchResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorDetail { get; set; }
    public bool IsTransientFailure { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public static DispatchResult Ok(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static DispatchResult Fail(string errorDetail, bool isTransient = false) =>
        new() { Success = false, ErrorDetail = errorDetail, IsTransientFailure = isTransient };
}

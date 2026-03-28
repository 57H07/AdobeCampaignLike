using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API for querying the SEND_LOG audit trail.
/// Accessible to Operator and Admin roles.
/// SEND_LOG is the source of truth for all dispatch activity (US-034).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
[Produces("application/json")]
public class SendLogsController : ControllerBase
{
    private readonly ISendLogService _sendLogService;

    public SendLogsController(ISendLogService sendLogService)
    {
        _sendLogService = sendLogService;
    }

    /// <summary>
    /// Returns a paginated list of send log entries with optional filtering.
    /// Supports filtering by campaign ID, recipient address, send status, and date range.
    /// </summary>
    /// <param name="campaignId">Filter by campaign ID.</param>
    /// <param name="recipient">Filter by recipient address (partial match).</param>
    /// <param name="status">Filter by send status: Pending=1, Sent=2, Failed=3, Retrying=4.</param>
    /// <param name="from">Filter: entries created on or after this UTC datetime.</param>
    /// <param name="to">Filter: entries created on or before this UTC datetime.</param>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Page size (1–200, default 50).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(SendLogPagedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SendLogPagedResult>> GetSendLogs(
        [FromQuery] Guid? campaignId = null,
        [FromQuery] string? recipient = null,
        [FromQuery] SendStatus? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest("Page must be >= 1.");
        if (pageSize < 1 || pageSize > 200) return BadRequest("PageSize must be between 1 and 200.");

        var total = await _sendLogService.CountAsync(
            campaignId, recipient, status, from, to, cancellationToken);

        var items = await _sendLogService.QueryAsync(
            campaignId, recipient, status, from, to, page, pageSize, cancellationToken);

        var dtos = items.Select(l => new SendLogDto
        {
            Id = l.Id,
            CampaignId = l.CampaignId,
            CampaignStepId = l.CampaignStepId,
            Channel = l.Channel.ToString(),
            Status = l.Status.ToString(),
            RecipientAddress = l.RecipientAddress,
            RecipientId = l.RecipientId,
            SentAt = l.SentAt,
            RetryCount = l.RetryCount,
            ErrorDetail = l.ErrorDetail,
            CorrelationId = l.CorrelationId,
            CreatedAt = l.CreatedAt,
            UpdatedAt = l.UpdatedAt
        }).ToList();

        return Ok(new SendLogPagedResult
        {
            Items = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    /// <summary>
    /// Returns a single send log entry by ID.
    /// </summary>
    /// <param name="id">The SEND_LOG entry ID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SendLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SendLogDto>> GetSendLog(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var log = await _sendLogService.GetByIdAsync(id, cancellationToken);
        if (log is null) return NotFound();

        return Ok(new SendLogDto
        {
            Id = log.Id,
            CampaignId = log.CampaignId,
            CampaignStepId = log.CampaignStepId,
            Channel = log.Channel.ToString(),
            Status = log.Status.ToString(),
            RecipientAddress = log.RecipientAddress,
            RecipientId = log.RecipientId,
            SentAt = log.SentAt,
            RetryCount = log.RetryCount,
            ErrorDetail = log.ErrorDetail,
            CorrelationId = log.CorrelationId,
            CreatedAt = log.CreatedAt,
            UpdatedAt = log.UpdatedAt
        });
    }
}

/// <summary>
/// DTO representing a single SEND_LOG entry.
/// </summary>
public record SendLogDto
{
    /// <summary>Unique identifier of the send log entry.</summary>
    public Guid Id { get; init; }

    /// <summary>The campaign this send attempt belongs to.</summary>
    public Guid CampaignId { get; init; }

    /// <summary>The campaign step this send attempt belongs to (null for single-step campaigns).</summary>
    public Guid? CampaignStepId { get; init; }

    /// <summary>The channel used: Email, Sms, or Letter.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>Current send status: Pending, Sent, Failed, or Retrying.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Recipient email, phone, or identifier depending on channel.</summary>
    public string RecipientAddress { get; init; } = string.Empty;

    /// <summary>External reference ID of the recipient from the data source.</summary>
    public string? RecipientId { get; init; }

    /// <summary>UTC timestamp when the message was successfully sent (null if not yet sent).</summary>
    public DateTime? SentAt { get; init; }

    /// <summary>Number of retry attempts made so far.</summary>
    public int RetryCount { get; init; }

    /// <summary>Error message from the last failed dispatch attempt.</summary>
    public string? ErrorDetail { get; init; }

    /// <summary>Correlation ID linking this send to a campaign chunk or API request.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>UTC timestamp when this log entry was created (= dispatch attempt start).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC timestamp of the last status update.</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Paginated result wrapper for send log queries.
/// </summary>
public record SendLogPagedResult
{
    public IReadOnlyList<SendLogDto> Items { get; init; } = Array.Empty<SendLogDto>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

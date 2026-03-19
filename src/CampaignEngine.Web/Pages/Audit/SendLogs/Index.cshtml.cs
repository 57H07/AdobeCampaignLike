using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Audit.SendLogs;

/// <summary>
/// Send log viewer page — allows Operators and Admins to query the SEND_LOG audit trail.
/// Supports filtering by campaign ID, recipient address, status, and date range (US-034).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
public class SendLogsIndexModel : PageModel
{
    private readonly ISendLogService _sendLogService;

    public SendLogsIndexModel(ISendLogService sendLogService)
    {
        _sendLogService = sendLogService;
    }

    // ----------------------------------------------------------------
    // Bound filter properties
    // ----------------------------------------------------------------

    [BindProperty(SupportsGet = true)]
    public SendLogFilterViewModel Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int Page { get; set; } = 1;

    // ----------------------------------------------------------------
    // Result properties
    // ----------------------------------------------------------------

    public IReadOnlyList<SendLogDto> Items { get; private set; } = Array.Empty<SendLogDto>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public int PageSize { get; private set; } = 50;

    public async Task OnGetAsync()
    {
        // Parse filter values
        Guid? campaignId = null;
        if (Guid.TryParse(Filter.CampaignId, out var cid)) campaignId = cid;

        SendStatus? status = null;
        if (int.TryParse(Filter.Status, out var statusInt) && Enum.IsDefined(typeof(SendStatus), statusInt))
            status = (SendStatus)statusInt;

        DateTime? fromUtc = null;
        if (!string.IsNullOrEmpty(Filter.From) && DateTime.TryParse(Filter.From, out var fromParsed))
            fromUtc = DateTime.SpecifyKind(fromParsed, DateTimeKind.Utc);

        DateTime? toUtc = null;
        if (!string.IsNullOrEmpty(Filter.To) && DateTime.TryParse(Filter.To, out var toParsed))
            toUtc = DateTime.SpecifyKind(toParsed, DateTimeKind.Utc);

        if (Page < 1) Page = 1;

        Total = await _sendLogService.CountAsync(
            campaignId: campaignId,
            recipientAddress: Filter.Recipient,
            status: status,
            from: fromUtc,
            to: toUtc);

        TotalPages = (int)Math.Ceiling((double)Total / PageSize);

        var logs = await _sendLogService.QueryAsync(
            campaignId: campaignId,
            recipientAddress: Filter.Recipient,
            status: status,
            from: fromUtc,
            to: toUtc,
            pageNumber: Page,
            pageSize: PageSize);

        Items = logs.Select(l => new SendLogDto
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
    }
}

/// <summary>
/// Filter view model for send log queries.
/// All fields are optional strings to allow flexible partial input from the form.
/// </summary>
public class SendLogFilterViewModel
{
    /// <summary>Optional GUID string for campaign ID filtering.</summary>
    public string? CampaignId { get; set; }

    /// <summary>Optional partial recipient address (email or phone).</summary>
    public string? Recipient { get; set; }

    /// <summary>Optional status as integer string: 1=Pending, 2=Sent, 3=Failed, 4=Retrying.</summary>
    public string? Status { get; set; }

    /// <summary>Optional from datetime (local time, ISO string from datetime-local input).</summary>
    public string? From { get; set; }

    /// <summary>Optional to datetime (local time, ISO string from datetime-local input).</summary>
    public string? To { get; set; }
}

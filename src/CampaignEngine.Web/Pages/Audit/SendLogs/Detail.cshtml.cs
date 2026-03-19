using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CampaignEngine.Web.Pages.Audit.SendLogs;

/// <summary>
/// Detail view for a single SEND_LOG entry.
/// Shows all fields including error detail, retry count, and correlation IDs.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
public class SendLogDetailModel : PageModel
{
    private readonly ISendLogService _sendLogService;

    public SendLogDetailModel(ISendLogService sendLogService)
    {
        _sendLogService = sendLogService;
    }

    public SendLogDto? Log { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var entity = await _sendLogService.GetByIdAsync(id);
        if (entity is null) return NotFound();

        Log = new SendLogDto
        {
            Id = entity.Id,
            CampaignId = entity.CampaignId,
            CampaignStepId = entity.CampaignStepId,
            Channel = entity.Channel.ToString(),
            Status = entity.Status.ToString(),
            RecipientAddress = entity.RecipientAddress,
            RecipientId = entity.RecipientId,
            SentAt = entity.SentAt,
            RetryCount = entity.RetryCount,
            ErrorDetail = entity.ErrorDetail,
            CorrelationId = entity.CorrelationId,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return Page();
    }
}

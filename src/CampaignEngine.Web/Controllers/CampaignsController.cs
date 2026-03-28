using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = CampaignEngine.Domain.Exceptions.ValidationException;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API for Campaign management.
/// Operators and Admins can create, schedule, and view campaigns.
/// GET endpoints are accessible to any authenticated user.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly IRecipientCountService _recipientCountService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITemplateSnapshotService _snapshotService;
    private readonly IAttachmentService _attachmentService;

    public CampaignsController(
        ICampaignService campaignService,
        IRecipientCountService recipientCountService,
        ICurrentUserService currentUserService,
        ITemplateSnapshotService snapshotService,
        IAttachmentService attachmentService)
    {
        _campaignService = campaignService;
        _recipientCountService = recipientCountService;
        _currentUserService = currentUserService;
        _snapshotService = snapshotService;
        _attachmentService = attachmentService;
    }

    // ----------------------------------------------------------------
    // GET /api/campaigns
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a paginated list of campaigns with optional filtering.
    /// </summary>
    /// <param name="status">Filter by campaign status (optional).</param>
    /// <param name="nameContains">Filter campaigns whose name contains this substring (optional).</param>
    /// <param name="dataSourceId">Filter by data source ID (optional).</param>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Page size (1–100, default 20).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(CampaignPagedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CampaignPagedResult>> GetCampaigns(
        [FromQuery] CampaignStatus? status = null,
        [FromQuery] string? nameContains = null,
        [FromQuery] Guid? dataSourceId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest("Page must be >= 1.");
        if (pageSize < 1 || pageSize > 100) return BadRequest("PageSize must be between 1 and 100.");

        var filter = new CampaignFilter
        {
            Status = status,
            NameContains = nameContains,
            DataSourceId = dataSourceId,
            Page = page,
            PageSize = pageSize
        };

        var result = await _campaignService.GetPagedAsync(filter, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/campaigns/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a single campaign by ID, including its steps.
    /// </summary>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CampaignDto>> GetCampaign(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _campaignService.GetByIdAsync(id, cancellationToken);
        if (campaign is null) return NotFound();
        return Ok(campaign);
    }

    // ----------------------------------------------------------------
    // POST /api/campaigns
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a new campaign in Draft status.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Campaign name must be unique.
    /// - At least one step is required.
    /// - Only Published templates can be used in steps.
    /// - ScheduledAt must be at least 5 minutes in the future if provided.
    /// - Operator must provide values for all freeField placeholders in the selected templates.
    /// </remarks>
    /// <param name="request">Campaign creation data.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CampaignDto>> CreateCampaign(
        [FromBody] CreateCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        try
        {
            var createdBy = _currentUserService.UserName;
            var campaign = await _campaignService.CreateAsync(request, createdBy, cancellationToken);
            return CreatedAtAction(nameof(GetCampaign), new { id = campaign.Id }, campaign);
        }
        catch (ValidationException ex)
        {
            foreach (var (field, errors) in ex.Errors)
            {
                foreach (var error in errors)
                    ModelState.AddModelError(field, error);
            }
            return ValidationProblem(ModelState);
        }
    }

    // ----------------------------------------------------------------
    // POST /api/campaigns/{id}/schedule
    // ----------------------------------------------------------------

    /// <summary>
    /// Transitions a campaign from Draft to Scheduled status.
    /// Creates immutable template snapshots for all campaign steps (US-025).
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Campaign must be in Draft status.
    /// - ScheduledAt must already be set and at least 5 minutes in the future.
    /// - Template snapshots are created atomically with the status change.
    /// </remarks>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpPost("{id:guid}/schedule")]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(typeof(CampaignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CampaignDto>> ScheduleCampaign(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var campaign = await _campaignService.ScheduleAsync(id, cancellationToken);
            return Ok(campaign);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            foreach (var (field, errors) in ex.Errors)
            {
                foreach (var error in errors)
                    ModelState.AddModelError(field, error);
            }
            return ValidationProblem(ModelState);
        }
    }

    // ----------------------------------------------------------------
    // GET /api/campaigns/{id}/snapshot
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns the template snapshots associated with this campaign.
    /// Snapshots are only present once the campaign has been scheduled.
    /// </summary>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet("{id:guid}/snapshot")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<TemplateSnapshotDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TemplateSnapshotDto>>> GetCampaignSnapshot(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Verify campaign exists
        var campaign = await _campaignService.GetByIdAsync(id, cancellationToken);
        if (campaign is null) return NotFound();

        var snapshots = await _snapshotService.GetSnapshotsForCampaignAsync(id, cancellationToken);
        return Ok(snapshots);
    }

    // ----------------------------------------------------------------
    // GET /api/campaigns/{id}/status
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns the real-time status snapshot for a campaign,
    /// including progress counters and the full status transition history (US-027).
    /// </summary>
    /// <remarks>
    /// This endpoint is designed for polling: call it every few seconds during campaign execution
    /// to display live progress. The response includes processed/total counts, failure rate,
    /// and an ordered log of all status transitions with timestamps.
    /// </remarks>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet("{id:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(CampaignStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CampaignStatusDto>> GetCampaignStatus(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var status = await _campaignService.GetStatusAsync(id, cancellationToken);
        if (status is null) return NotFound();
        return Ok(status);
    }

    // ----------------------------------------------------------------
    // POST /api/campaigns/estimate-recipients
    // ----------------------------------------------------------------

    /// <summary>
    /// Estimates the number of recipients for a given data source and filter combination.
    /// Used in the campaign wizard to preview recipient count before creation.
    /// Read-only: no records are written.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - DataSourceId is required.
    /// - Filter expression is optional (null = all records).
    /// - The count is an approximation at the time of the request.
    /// </remarks>
    /// <param name="request">Data source ID and optional filter expression.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpPost("estimate-recipients")]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(typeof(RecipientCountEstimateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RecipientCountEstimateResult>> EstimateRecipients(
        [FromBody] EstimateRecipientsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DataSourceId == Guid.Empty)
            return BadRequest(new { error = "DataSourceId is required." });

        var result = await _recipientCountService.EstimateAsync(request, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/campaigns/{id}/attachments
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns all attachments (static and dynamic) registered for a campaign.
    /// </summary>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpGet("{id:guid}/attachments")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignAttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CampaignAttachmentDto>>> GetAttachments(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        // Verify campaign exists
        var campaign = await _campaignService.GetByIdAsync(id, cancellationToken);
        if (campaign is null) return NotFound();

        var attachments = await _attachmentService.GetByCampaignAsync(id, cancellationToken);
        return Ok(attachments);
    }

    // ----------------------------------------------------------------
    // POST /api/campaigns/{id}/attachments (static — multipart upload)
    // ----------------------------------------------------------------

    /// <summary>
    /// Uploads a static attachment file for a campaign.
    /// The same file will be sent to all recipients.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Extension whitelist: PDF, DOCX, XLSX, PNG, JPG.
    /// - Max 10 MB per file.
    /// - Max 25 MB total across all campaign attachments.
    /// - Campaign must exist.
    /// </remarks>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="file">File to upload (multipart/form-data).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)] // 11 MB limit at transport layer
    [ProducesResponseType(typeof(CampaignAttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CampaignAttachmentDto>> UploadStaticAttachment(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "A non-empty file is required." });

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            var content = ms.ToArray();

            var dto = await _attachmentService.UploadStaticAsync(
                id,
                file.FileName,
                content,
                cancellationToken);

            return CreatedAtAction(nameof(GetAttachments), new { id }, dto);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            foreach (var (field, errors) in ex.Errors)
            {
                foreach (var error in errors)
                    ModelState.AddModelError(field, error);
            }
            return ValidationProblem(ModelState);
        }
    }

    // ----------------------------------------------------------------
    // POST /api/campaigns/{id}/attachments/dynamic
    // ----------------------------------------------------------------

    /// <summary>
    /// Registers a dynamic attachment configuration for a campaign.
    /// The per-recipient file path is resolved at send time from the specified data source field.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - DynamicFieldName must match a field in the campaign's data source.
    /// - No file is uploaded — only the field mapping is stored.
    /// - Missing files at send time are logged as warnings (send is not blocked).
    /// </remarks>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="request">Dynamic field mapping configuration.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpPost("{id:guid}/attachments/dynamic")]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(typeof(CampaignAttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CampaignAttachmentDto>> RegisterDynamicAttachment(
        Guid id,
        [FromBody] AddDynamicAttachmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        try
        {
            var dto = await _attachmentService.RegisterDynamicAsync(
                id,
                request.DynamicFieldName,
                cancellationToken);

            return CreatedAtAction(nameof(GetAttachments), new { id }, dto);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            foreach (var (field, errors) in ex.Errors)
            {
                foreach (var error in errors)
                    ModelState.AddModelError(field, error);
            }
            return ValidationProblem(ModelState);
        }
    }

    // ----------------------------------------------------------------
    // DELETE /api/campaigns/{id}/attachments/{attachmentId}
    // ----------------------------------------------------------------

    /// <summary>
    /// Deletes a campaign attachment (static or dynamic).
    /// For static attachments, the file is also removed from the file share.
    /// </summary>
    /// <param name="id">Campaign GUID.</param>
    /// <param name="attachmentId">Attachment GUID.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteAttachment(
        Guid id,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _attachmentService.DeleteAsync(attachmentId, cancellationToken);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }
}

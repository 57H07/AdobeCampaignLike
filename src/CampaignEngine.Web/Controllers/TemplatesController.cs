using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API for Template CRUD operations.
/// GET endpoints are accessible to any authenticated user.
/// POST, PUT, DELETE require Designer or Admin role (business rule BR-4).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateService _templateService;

    public TemplatesController(ITemplateService templateService)
    {
        _templateService = templateService;
    }

    // ----------------------------------------------------------------
    // GET /api/templates
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a paginated list of templates with optional filtering by channel and status.
    /// </summary>
    /// <param name="channel">Filter by channel type: Email=1, Letter=2, Sms=3.</param>
    /// <param name="status">Filter by lifecycle status: Draft=1, Published=2, Archived=3.</param>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Page size (1–200, default 20).</param>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(TemplatePagedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TemplatePagedResult>> GetTemplates(
        [FromQuery] ChannelType? channel = null,
        [FromQuery] TemplateStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest("Page must be >= 1.");
        if (pageSize < 1 || pageSize > 200) return BadRequest("PageSize must be between 1 and 200.");

        var result = await _templateService.GetPagedAsync(channel, status, page, pageSize, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/templates/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a single template by ID.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TemplateDto>> GetTemplate(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            HtmlBody = template.HtmlBody,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        });
    }

    // ----------------------------------------------------------------
    // POST /api/templates
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a new template in Draft status.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Template name must be unique within the same channel.
    /// - New templates start in Draft status.
    /// - Only Designer and Admin roles can create templates.
    /// </remarks>
    /// <param name="request">Template creation data.</param>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> CreateTemplate(
        [FromBody] CreateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.CreateAsync(request, cancellationToken);

        var dto = new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            HtmlBody = template.HtmlBody,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };

        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, dto);
    }

    // ----------------------------------------------------------------
    // PUT /api/templates/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Updates the name, HTML body, and description of an existing template.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Template name must remain unique within its channel.
    /// - Channel type cannot be changed after creation.
    /// - Only Designer and Admin roles can edit templates.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    /// <param name="request">Updated template data.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> UpdateTemplate(
        Guid id,
        [FromBody] UpdateTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.UpdateAsync(id, request, cancellationToken);

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            HtmlBody = template.HtmlBody,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        });
    }

    // ----------------------------------------------------------------
    // DELETE /api/templates/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Soft-deletes a template (sets IsDeleted = true). The record is preserved for audit.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Soft delete only: sets IsDeleted flag, record is kept for audit trail.
    /// - Only Designer and Admin roles can delete templates.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTemplate(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await _templateService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

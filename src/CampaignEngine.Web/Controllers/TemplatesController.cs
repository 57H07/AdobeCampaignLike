using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ValidationException = CampaignEngine.Domain.Exceptions.ValidationException;

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
    private readonly IPlaceholderManifestService _manifestService;
    private readonly IPlaceholderParserService _parserService;
    private readonly ISubTemplateResolverService _subTemplateResolver;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITemplatePreviewService _previewService;

    public TemplatesController(
        ITemplateService templateService,
        IPlaceholderManifestService manifestService,
        IPlaceholderParserService parserService,
        ISubTemplateResolverService subTemplateResolver,
        ICurrentUserService currentUserService,
        ITemplatePreviewService previewService)
    {
        _templateService = templateService;
        _manifestService = manifestService;
        _parserService = parserService;
        _subTemplateResolver = subTemplateResolver;
        _currentUserService = currentUserService;
        _previewService = previewService;
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
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
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
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
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
        // Populate ChangedBy from the authenticated user for version history (US-008)
        request.ChangedBy = _currentUserService.UserName;
        var template = await _templateService.UpdateAsync(id, request, cancellationToken);

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
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

    // ================================================================
    // Letter Channel Upload Endpoints (US-010)
    // ================================================================

    /// <summary>
    /// Creates a new Letter template by uploading a DOCX file.
    /// Maximum file size is 10 MB (10,485,760 bytes).
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - File must not exceed 10 MB (F-204).
    /// - Template name must be unique within the Letter channel.
    /// - New templates start in Draft status.
    /// - Only Designer and Admin roles can create templates.
    /// </remarks>
    [HttpPost("letter")]
    [RequestSizeLimit(10_485_760)]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413RequestEntityTooLarge)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> CreateLetterTemplate(
        [FromForm] string name,
        [FromForm] IFormFile file,
        [FromForm] string? description = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
            return BadRequest(new { error = "A DOCX file is required." });

        const long maxFileSizeBytes = 10_485_760;
        if (file.Length > maxFileSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge,
                new { error = $"File size {file.Length:N0} bytes exceeds the 10 MB limit ({maxFileSizeBytes:N0} bytes)." });

        var request = new CreateTemplateRequest
        {
            Name = name,
            Channel = ChannelType.Letter,
            BodyPath = file.FileName,
            Description = description,
            FileSizeBytes = file.Length
        };

        var template = await _templateService.CreateAsync(request, cancellationToken);

        var dto = new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        };

        return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, dto);
    }

    /// <summary>
    /// Updates an existing Letter template by uploading a new DOCX file.
    /// Maximum file size is 10 MB (10,485,760 bytes).
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - File must not exceed 10 MB (F-204).
    /// - Template name must remain unique within the Letter channel.
    /// - File is optional: if omitted, existing DOCX is retained.
    /// - Only Designer and Admin roles can edit templates.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    [HttpPut("{id:guid}/letter")]
    [RequestSizeLimit(10_485_760)]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413RequestEntityTooLarge)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> UpdateLetterTemplate(
        Guid id,
        [FromForm] string name,
        [FromForm] IFormFile? file = null,
        [FromForm] string? description = null,
        CancellationToken cancellationToken = default)
    {
        const long maxFileSizeBytes = 10_485_760;
        if (file is not null && file.Length > maxFileSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge,
                new { error = $"File size {file.Length:N0} bytes exceeds the 10 MB limit ({maxFileSizeBytes:N0} bytes)." });

        // Retrieve existing template to retain current BodyPath when no new file is provided
        var existing = await _templateService.GetByIdAsync(id, cancellationToken);
        if (existing is null) return NotFound();

        var request = new UpdateTemplateRequest
        {
            Name = name,
            BodyPath = file is not null ? file.FileName : existing.BodyPath,
            BodyChecksum = existing.BodyChecksum,
            Description = description,
            ChangedBy = _currentUserService.UserName
        };

        var template = await _templateService.UpdateAsync(id, request, cancellationToken);

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        });
    }

    // ================================================================
    // Status Lifecycle Endpoints
    // ================================================================

    // ----------------------------------------------------------------
    // POST /api/templates/{id}/publish
    // ----------------------------------------------------------------

    /// <summary>
    /// Publishes a template, transitioning it from Draft to Published.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Template must currently be in Draft status.
    /// - All placeholders used in the HTML body must be declared in the manifest.
    /// - Only Designer and Admin roles can publish templates.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> PublishTemplate(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.PublishAsync(id, cancellationToken);

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        });
    }

    // ----------------------------------------------------------------
    // POST /api/templates/{id}/archive
    // ----------------------------------------------------------------

    /// <summary>
    /// Archives a template, transitioning it from Draft or Published to Archived.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Template must not already be Archived.
    /// - Archived templates cannot transition back to Published.
    /// - Only Designer and Admin roles can archive templates.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> ArchiveTemplate(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.ArchiveAsync(id, cancellationToken);

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        });
    }

    // ================================================================
    // Placeholder Manifest Endpoints
    // ================================================================

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/placeholders
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns all placeholder manifest entries declared for the specified template.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}/placeholders")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<PlaceholderManifestEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PlaceholderManifestEntryDto>>> GetPlaceholders(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        var entries = await _manifestService.GetByTemplateIdAsync(id, cancellationToken);
        return Ok(entries);
    }

    // ----------------------------------------------------------------
    // POST /api/templates/{id}/placeholders
    // ----------------------------------------------------------------

    /// <summary>
    /// Adds a new placeholder manifest entry to the specified template.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Placeholder key must be unique within the template manifest.
    /// - FreeField type always implies IsFromDataSource = false.
    /// - Only Designer and Admin roles can modify the manifest.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    /// <param name="request">Placeholder entry data.</param>
    [HttpPost("{id:guid}/placeholders")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(PlaceholderManifestEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlaceholderManifestEntryDto>> AddPlaceholder(
        Guid id,
        [FromBody] UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = await _manifestService.AddEntryAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(GetPlaceholders), new { id }, entry);
    }

    // ----------------------------------------------------------------
    // PUT /api/templates/{id}/placeholders/{entryId}
    // ----------------------------------------------------------------

    /// <summary>
    /// Updates an existing placeholder manifest entry.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    /// <param name="entryId">Manifest entry GUID.</param>
    /// <param name="request">Updated placeholder data.</param>
    [HttpPut("{id:guid}/placeholders/{entryId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(PlaceholderManifestEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlaceholderManifestEntryDto>> UpdatePlaceholder(
        Guid id,
        Guid entryId,
        [FromBody] UpsertPlaceholderManifestRequest request,
        CancellationToken cancellationToken = default)
    {
        var entry = await _manifestService.UpdateEntryAsync(id, entryId, request, cancellationToken);
        return Ok(entry);
    }

    // ----------------------------------------------------------------
    // DELETE /api/templates/{id}/placeholders/{entryId}
    // ----------------------------------------------------------------

    /// <summary>
    /// Removes a placeholder manifest entry from the specified template.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    /// <param name="entryId">Manifest entry GUID.</param>
    [HttpDelete("{id:guid}/placeholders/{entryId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeletePlaceholder(
        Guid id,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        await _manifestService.DeleteEntryAsync(id, entryId, cancellationToken);
        return NoContent();
    }

    // ----------------------------------------------------------------
    // PUT /api/templates/{id}/placeholders/bulk
    // ----------------------------------------------------------------

    /// <summary>
    /// Replaces the entire placeholder manifest for a template with the provided set.
    /// Existing entries are removed and replaced atomically.
    /// </summary>
    /// <remarks>
    /// Use this endpoint for bulk save from the manifest editor UI.
    /// All keys in the provided list must be unique.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    /// <param name="entries">Complete replacement manifest entries.</param>
    [HttpPut("{id:guid}/placeholders/bulk")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(IReadOnlyList<PlaceholderManifestEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PlaceholderManifestEntryDto>>> ReplacePlaceholders(
        Guid id,
        [FromBody] IEnumerable<UpsertPlaceholderManifestRequest> entries,
        CancellationToken cancellationToken = default)
    {
        var result = await _manifestService.ReplaceManifestAsync(id, entries, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/placeholders/extract
    // ----------------------------------------------------------------

    /// <summary>
    /// Extracts placeholder keys from the template HTML body without persisting anything.
    /// Use this to auto-detect which placeholders need to be declared.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}/placeholders/extract")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(PlaceholderExtractionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlaceholderExtractionResult>> ExtractPlaceholders(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        var result = _parserService.ExtractPlaceholders(template.BodyPath);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/placeholders/validate
    // ----------------------------------------------------------------

    /// <summary>
    /// Validates that all placeholders used in the template HTML are declared in the manifest.
    /// Returns undeclared keys and orphan manifest entries (informational).
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}/placeholders/validate")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(ManifestValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ManifestValidationResult>> ValidatePlaceholderManifest(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        var manifestEntries = await _manifestService.GetByTemplateIdAsync(id, cancellationToken);
        var result = _parserService.ValidateManifestCompleteness(template.BodyPath, manifestEntries);
        return Ok(result);
    }

    // ================================================================
    // Sub-Template Endpoints
    // ================================================================

    // ----------------------------------------------------------------
    // GET /api/templates/subtemplates
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns all templates marked as sub-templates (IsSubTemplate = true).
    /// Used by the sub-template selector UI in the template editor.
    /// </summary>
    [HttpGet("subtemplates")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<TemplateSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TemplateSummaryDto>>> GetSubTemplates(
        CancellationToken cancellationToken = default)
    {
        var result = await _templateService.GetSubTemplatesAsync(cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/subtemplates/references
    // ----------------------------------------------------------------

    /// <summary>
    /// Extracts all direct sub-template references ({{> name}} syntax) from the template HTML body.
    /// Does not perform recursive resolution — returns only direct references.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}/subtemplates/references")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(SubTemplateReferencesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SubTemplateReferencesResult>> GetSubTemplateReferences(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        var references = _subTemplateResolver.ExtractReferences(template.BodyPath);
        return Ok(new SubTemplateReferencesResult
        {
            TemplateId = id,
            References = references.Select(r => r.Name).ToList().AsReadOnly()
        });
    }

    // ----------------------------------------------------------------
    // POST /api/templates/{id}/subtemplates/resolve
    // ----------------------------------------------------------------

    /// <summary>
    /// Resolves all sub-template references in the template HTML body recursively.
    /// Returns the fully resolved HTML body for preview purposes.
    /// Changes to sub-templates propagate live (not frozen).
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpPost("{id:guid}/subtemplates/resolve")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(SubTemplateResolveResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SubTemplateResolveResult>> ResolveSubTemplates(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        try
        {
            var resolvedBody = await _subTemplateResolver.ResolveAsync(id, template.BodyPath, cancellationToken);
            return Ok(new SubTemplateResolveResult
            {
                TemplateId = id,
                ResolvedHtmlBody = resolvedBody,
                IsFullyResolved = true
            });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/subtemplates/validate
    // ----------------------------------------------------------------

    /// <summary>
    /// Validates that no circular sub-template references exist starting from the specified template.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}/subtemplates/validate")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(SubTemplateValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SubTemplateValidationResult>> ValidateSubTemplateReferences(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var template = await _templateService.GetByIdAsync(id, cancellationToken);
        if (template is null) return NotFound();

        try
        {
            await _subTemplateResolver.ValidateNoCircularReferencesAsync(id, cancellationToken);
            return Ok(new SubTemplateValidationResult
            {
                TemplateId = id,
                IsValid = true,
                Message = "No circular sub-template references detected."
            });
        }
        catch (ValidationException ex)
        {
            return Ok(new SubTemplateValidationResult
            {
                TemplateId = id,
                IsValid = false,
                Message = ex.Message
            });
        }
    }

    // ================================================================
    // Preview Endpoint (US-010)
    // ================================================================

    // ----------------------------------------------------------------
    // POST /api/templates/{id}/preview
    // ----------------------------------------------------------------

    /// <summary>
    /// Renders a template with sample data fetched from the specified data source.
    /// Read-only: no sends occur, no records are written.
    /// </summary>
    /// <remarks>
    /// Business rules:
    /// - Up to 5 sample rows are fetched from the data source.
    /// - The first sample row (RowIndex = 0) is used for rendering by default.
    /// - Channel post-processing is applied: CSS inlining for Email, PDF generation for Letter, text stripping for SMS.
    /// - Missing placeholder keys (present in the template but absent from the sample row) are returned in the result.
    /// - Only Designer and Admin roles can invoke the preview (BR-4).
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    /// <param name="request">Preview parameters: data source ID, sample row count, row index.</param>
    [HttpPost("{id:guid}/preview")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(TemplatePreviewResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplatePreviewResult>> PreviewTemplate(
        Guid id,
        [FromBody] TemplatePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.DataSourceId == Guid.Empty)
            return BadRequest(new { error = "DataSourceId is required." });

        if (request.SampleRowCount < 1 || request.SampleRowCount > 5)
            return BadRequest(new { error = "SampleRowCount must be between 1 and 5." });

        try
        {
            var result = await _previewService.PreviewAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ================================================================
    // Versioning Endpoints (US-008)
    // ================================================================

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/history
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns the full version history of the specified template, ordered by version descending.
    /// Version history is never deleted (audit requirement).
    /// </summary>
    /// <param name="id">Template GUID.</param>
    [HttpGet("{id:guid}/history")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(IReadOnlyList<TemplateHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<TemplateHistoryDto>>> GetHistory(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var history = await _templateService.GetHistoryAsync(id, cancellationToken);
        return Ok(history);
    }

    // ----------------------------------------------------------------
    // GET /api/templates/{id}/history/diff
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a diff between two versions of a template.
    /// If toVersion is not provided, compares fromVersion against the current live version.
    /// </summary>
    /// <param name="id">Template GUID.</param>
    /// <param name="fromVersion">The older (base) version number.</param>
    /// <param name="toVersion">The newer version number (omit to compare against current).</param>
    [HttpGet("{id:guid}/history/diff")]
    [Authorize(Policy = AuthorizationPolicies.RequireAuthenticated)]
    [ProducesResponseType(typeof(TemplateDiffDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TemplateDiffDto>> GetDiff(
        Guid id,
        [FromQuery] int fromVersion,
        [FromQuery] int? toVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (fromVersion < 1)
            return BadRequest("fromVersion must be >= 1.");

        var diff = await _templateService.GetDiffAsync(id, fromVersion, toVersion, cancellationToken);
        return Ok(diff);
    }

    // ----------------------------------------------------------------
    // POST /api/templates/{id}/revert/{version}
    // ----------------------------------------------------------------

    /// <summary>
    /// Reverts a template to a previous version by creating a new version with the historic content.
    /// Business rule: revert creates a new version — it does not overwrite existing history.
    /// </summary>
    /// <remarks>
    /// The current template content is saved to history before the revert is applied.
    /// The template version counter increments normally.
    /// </remarks>
    /// <param name="id">Template GUID.</param>
    /// <param name="version">The version number to revert to.</param>
    [HttpPost("{id:guid}/revert/{version:int}")]
    [Authorize(Policy = AuthorizationPolicies.RequireDesignerOrAdmin)]
    [ProducesResponseType(typeof(TemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TemplateDto>> RevertToVersion(
        Guid id,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (version < 1)
            return BadRequest("version must be >= 1.");

        var changedBy = _currentUserService.UserName;
        var template = await _templateService.RevertToVersionAsync(id, version, changedBy, cancellationToken);

        return Ok(new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Channel = template.Channel.ToString(),
            BodyPath = template.BodyPath,
            BodyChecksum = template.BodyChecksum,
            Status = template.Status.ToString(),
            Version = template.Version,
            IsSubTemplate = template.IsSubTemplate,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt
        });
    }
}

// ================================================================
// Sub-Template Result DTOs (inline — used only by these endpoints)
// ================================================================

/// <summary>Result of extracting sub-template references from a template.</summary>
public class SubTemplateReferencesResult
{
    public Guid TemplateId { get; init; }
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();
}

/// <summary>Result of resolving sub-template references in a template.</summary>
public class SubTemplateResolveResult
{
    public Guid TemplateId { get; init; }
    public string ResolvedHtmlBody { get; init; } = string.Empty;
    public bool IsFullyResolved { get; init; }
}

/// <summary>Result of circular reference validation for a template.</summary>
public class SubTemplateValidationResult
{
    public Guid TemplateId { get; init; }
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
}

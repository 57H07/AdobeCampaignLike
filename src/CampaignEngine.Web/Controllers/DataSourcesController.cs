using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API for data source declaration and management.
/// GET endpoints are accessible to Operator and Admin roles.
/// POST/PUT/PATCH endpoints require Admin role (Business Rule BR-3).
/// </summary>
[ApiController]
[Route("api/datasources")]
[Produces("application/json")]
public class DataSourcesController : ControllerBase
{
    private readonly IDataSourceService _dataSourceService;

    public DataSourcesController(IDataSourceService dataSourceService)
    {
        _dataSourceService = dataSourceService;
    }

    // ----------------------------------------------------------------
    // GET /api/datasources
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a paginated list of data sources with optional filtering.
    /// </summary>
    /// <param name="type">Filter by type: SqlServer=1, RestApi=2.</param>
    /// <param name="isActive">Filter by active status.</param>
    /// <param name="nameContains">Partial name match (case-insensitive).</param>
    /// <param name="page">Page number (1-based, default 1).</param>
    /// <param name="pageSize">Page size (1–100, default 20).</param>
    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(typeof(DataSourcePagedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DataSourcePagedResult>> GetDataSources(
        [FromQuery] DataSourceType? type = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? nameContains = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) return BadRequest("Page must be >= 1.");
        if (pageSize < 1 || pageSize > 100) return BadRequest("PageSize must be between 1 and 100.");

        var filter = new DataSourceFilter
        {
            Type = type,
            IsActive = isActive,
            NameContains = nameContains,
            Page = page,
            PageSize = pageSize
        };

        var result = await _dataSourceService.GetAllAsync(filter, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // GET /api/datasources/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a single data source by ID, including its field schema.
    /// </summary>
    /// <param name="id">The data source ID.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireOperatorOrAdmin)]
    [ProducesResponseType(typeof(DataSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DataSourceDto>> GetDataSource(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var dto = await _dataSourceService.GetByIdAsync(id, cancellationToken);
        if (dto is null) return NotFound();
        return Ok(dto);
    }

    // ----------------------------------------------------------------
    // POST /api/datasources
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a new data source with connection metadata.
    /// The connection string is encrypted at rest.
    /// Admin role required.
    /// </summary>
    /// <param name="request">The data source creation request.</param>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(DataSourceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DataSourceDto>> CreateDataSource(
        [FromBody] CreateDataSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var dto = await _dataSourceService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetDataSource), new { id = dto.Id }, dto);
    }

    // ----------------------------------------------------------------
    // PUT /api/datasources/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Updates an existing data source name, type, connection string, or description.
    /// If ConnectionString is omitted, the stored encrypted string is preserved.
    /// Admin role required.
    /// </summary>
    /// <param name="id">The data source ID.</param>
    /// <param name="request">The update request.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(DataSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DataSourceDto>> UpdateDataSource(
        Guid id,
        [FromBody] UpdateDataSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var dto = await _dataSourceService.UpdateAsync(id, request, cancellationToken);
        return Ok(dto);
    }

    // ----------------------------------------------------------------
    // PUT /api/datasources/{id}/schema
    // ----------------------------------------------------------------

    /// <summary>
    /// Replaces the full field schema for a data source.
    /// Existing fields are deleted and replaced with the provided list.
    /// Admin role required.
    /// </summary>
    /// <param name="id">The data source ID.</param>
    /// <param name="fields">New field definitions.</param>
    [HttpPut("{id:guid}/schema")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(DataSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DataSourceDto>> UpdateSchema(
        Guid id,
        [FromBody] IReadOnlyList<UpsertFieldRequest> fields,
        CancellationToken cancellationToken = default)
    {
        var dto = await _dataSourceService.UpdateSchemaAsync(id, fields, cancellationToken);
        return Ok(dto);
    }

    // ----------------------------------------------------------------
    // POST /api/datasources/{id}/test-connection
    // ----------------------------------------------------------------

    /// <summary>
    /// Tests the connectivity of an existing data source using its stored connection string.
    /// Admin role required.
    /// </summary>
    /// <param name="id">The data source ID.</param>
    [HttpPost("{id:guid}/test-connection")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _dataSourceService.TestConnectionAsync(id, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // POST /api/datasources/test-connection
    // ----------------------------------------------------------------

    /// <summary>
    /// Tests a raw connection string before persisting a data source.
    /// Admin role required.
    /// </summary>
    /// <param name="request">The type and plaintext connection string to test.</param>
    [HttpPost("test-connection")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(ConnectionTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ConnectionTestResult>> TestConnectionRaw(
        [FromBody] TestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _dataSourceService.TestConnectionRawAsync(request, cancellationToken);
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // PATCH /api/datasources/{id}/active
    // ----------------------------------------------------------------

    /// <summary>
    /// Activates or deactivates a data source.
    /// Admin role required.
    /// </summary>
    /// <param name="id">The data source ID.</param>
    /// <param name="isActive">True to activate, false to deactivate.</param>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
    [ProducesResponseType(typeof(DataSourceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DataSourceDto>> SetActive(
        Guid id,
        [FromQuery] bool isActive = true,
        CancellationToken cancellationToken = default)
    {
        var dto = await _dataSourceService.SetActiveAsync(id, isActive, cancellationToken);
        return Ok(dto);
    }
}

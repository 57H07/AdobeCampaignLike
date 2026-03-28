using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Web.OpenApi.Examples;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API for API key management — Admin access only.
/// Provides create, list, revoke, and rotate operations for API keys.
///
/// Business rules:
///   - Only Admin role can manage API keys.
///   - Key values are hashed (BCrypt); plaintext returned only at creation time.
///   - Revoked keys are soft-deleted (IsActive = false), never hard-deleted.
///   - Key rotation revokes the old key and creates a new one.
/// </summary>
[ApiController]
[Route("api/apikeys")]
[Produces("application/json")]
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
public class ApiKeysController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ICurrentUserService _currentUserService;

    public ApiKeysController(
        IApiKeyService apiKeyService,
        ICurrentUserService currentUserService)
    {
        _apiKeyService = apiKeyService;
        _currentUserService = currentUserService;
    }

    // ----------------------------------------------------------------
    // GET /api/apikeys
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns all API keys (active and revoked).
    /// The plaintext key value is never included — only the prefix and metadata.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var keys = await _apiKeyService.GetAllAsync(cancellationToken);
        return Ok(keys);
    }

    // ----------------------------------------------------------------
    // GET /api/apikeys/{id}
    // ----------------------------------------------------------------

    /// <summary>
    /// Returns a single API key by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiKeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiKeyDto>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var key = await _apiKeyService.GetByIdAsync(id, cancellationToken);
        if (key is null)
            return NotFound();

        return Ok(key);
    }

    // ----------------------------------------------------------------
    // POST /api/apikeys
    // ----------------------------------------------------------------

    /// <summary>
    /// Creates a new API key.
    /// The response includes the plaintext key value — this is shown ONCE and cannot be retrieved again.
    /// </summary>
    [HttpPost]
    [SwaggerRequestExample(typeof(CreateApiKeyRequest), typeof(CreateApiKeyRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ApiKeyCreatedResponseExample))]
    [ProducesResponseType(typeof(ApiKeyCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Create(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _apiKeyService.CreateAsync(
            request,
            _currentUserService.UserName ?? "admin",
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Key.Id },
            response);
    }

    // ----------------------------------------------------------------
    // POST /api/apikeys/{id}/revoke
    // ----------------------------------------------------------------

    /// <summary>
    /// Revokes an API key. Revoked keys immediately stop working for authentication.
    /// Revocation is permanent — a new key must be created if access is needed again.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Revoke(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await _apiKeyService.RevokeAsync(id, cancellationToken);
        return NoContent();
    }

    // ----------------------------------------------------------------
    // POST /api/apikeys/{id}/rotate
    // ----------------------------------------------------------------

    /// <summary>
    /// Rotates an API key: revokes the existing key and creates a replacement
    /// with the same name and settings.
    /// The response includes the new key's plaintext value — shown ONCE only.
    /// </summary>
    [HttpPost("{id:guid}/rotate")]
    [ProducesResponseType(typeof(ApiKeyCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiKeyCreatedResponse>> Rotate(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _apiKeyService.RotateAsync(
            id,
            _currentUserService.UserName ?? "admin",
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Key.Id },
            response);
    }
}

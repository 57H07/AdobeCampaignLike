using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.DTOs.Identity;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// REST API for user management — Admin role only.
/// Demonstrates role-based authorization via [Authorize(Policy = ...)] attributes.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IIdentityService _identityService;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        IIdentityService identityService,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _identityService = identityService;
        _auditService = auditService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Returns a list of all application users with their roles.
    /// Requires Admin role.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
    {
        var users = await _identityService.GetAllUsersAsync();
        return Ok(users);
    }

    /// <summary>
    /// Returns a single user by ID.
    /// Requires Admin role.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UserDto>> GetUser(string id)
    {
        var user = await _identityService.GetUserByIdAsync(id);
        if (user == null) return NotFound();

        return Ok(user);
    }

    /// <summary>
    /// Assigns a role to a user.
    /// Replaces all current roles with the specified role.
    /// Requires Admin role.
    /// </summary>
    [HttpPut("{id}/role")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignRole(string id, [FromBody] AssignRoleRequest request)
    {
        if (!UserRole.All.Contains(request.Role))
            return BadRequest($"Invalid role. Must be one of: {string.Join(", ", UserRole.All)}");

        // Get current roles before assignment for audit logging
        var existingUser = await _identityService.GetUserByIdAsync(id);
        if (existingUser == null) return NotFound();

        var previousRoles = existingUser.Roles;

        var result = await _identityService.AssignRoleAsync(id, request.Role);
        if (result == null) return NotFound();

        var (userId, userName) = result.Value;
        var adminUserId = _currentUserService.UserId ?? "system";
        var adminUserName = _currentUserService.UserName ?? "system";

        foreach (var existingRole in previousRoles)
        {
            await _auditService.LogRoleRemovedAsync(
                adminUserId, adminUserName, userId, userName, existingRole);
        }

        await _auditService.LogRoleAssignedAsync(
            adminUserId, adminUserName, userId, userName, request.Role);

        return NoContent();
    }
}

using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthAuditService _auditService;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        IAuthAuditService auditService,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
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
        var users = _userManager.Users.ToList();
        var result = new List<UserDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new UserDto
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
                Roles = roles.ToList()
            });
        }

        return Ok(result);
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
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new UserDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Roles = roles.ToList()
        });
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

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var existingRoles = await _userManager.GetRolesAsync(user);
        foreach (var existingRole in existingRoles)
        {
            await _userManager.RemoveFromRoleAsync(user, existingRole);
            await _auditService.LogRoleRemovedAsync(
                _currentUserService.UserId ?? "system",
                _currentUserService.UserName ?? "system",
                user.Id,
                user.UserName ?? user.Id,
                existingRole);
        }

        await _userManager.AddToRoleAsync(user, request.Role);
        await _auditService.LogRoleAssignedAsync(
            _currentUserService.UserId ?? "system",
            _currentUserService.UserName ?? "system",
            user.Id,
            user.UserName ?? user.Id,
            request.Role);

        return NoContent();
    }
}

/// <summary>
/// DTO representing a user with their assigned roles.
/// </summary>
public record UserDto
{
    public string Id { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Request body for role assignment.
/// </summary>
public record AssignRoleRequest
{
    /// <summary>
    /// Role to assign: Admin, Designer, or Operator.
    /// </summary>
    public string Role { get; init; } = string.Empty;
}

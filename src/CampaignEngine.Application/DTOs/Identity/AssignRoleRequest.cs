namespace CampaignEngine.Application.DTOs.Identity;

/// <summary>
/// Request DTO for assigning a role to a user.
/// </summary>
public record AssignRoleRequest
{
    /// <summary>
    /// Role to assign: Admin, Designer, or Operator.
    /// </summary>
    public string Role { get; init; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.ViewModels.UserManagement;

/// <summary>
/// View model for editing a user's role assignment (Admin only).
/// </summary>
public class EditUserRoleViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }

    [Required]
    [Display(Name = "Role")]
    public string SelectedRole { get; set; } = string.Empty;

    public IReadOnlyList<string> CurrentRoles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> AvailableRoles { get; set; } = CampaignEngine.Domain.Enums.UserRole.All;
}

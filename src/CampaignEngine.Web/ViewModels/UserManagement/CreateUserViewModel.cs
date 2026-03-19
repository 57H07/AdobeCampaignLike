using System.ComponentModel.DataAnnotations;

namespace CampaignEngine.Web.ViewModels.UserManagement;

/// <summary>
/// View model for creating a new user (Admin only).
/// </summary>
public class CreateUserViewModel
{
    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 3)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [StringLength(100)]
    [Display(Name = "Display Name")]
    public string? DisplayName { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Role")]
    public string Role { get; set; } = CampaignEngine.Domain.Enums.UserRole.Default;

    public IReadOnlyList<string> AvailableRoles { get; set; } = CampaignEngine.Domain.Enums.UserRole.All;
}

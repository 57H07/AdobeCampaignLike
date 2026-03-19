namespace CampaignEngine.Web.ViewModels.UserManagement;

/// <summary>
/// View model for the user list page (Admin only).
/// </summary>
public class UserListViewModel
{
    public IReadOnlyList<UserSummaryViewModel> Users { get; init; } = Array.Empty<UserSummaryViewModel>();
    public string? Message { get; init; }
    public bool IsSuccess { get; init; }
}

/// <summary>
/// Summary of a single user for list display.
/// </summary>
public class UserSummaryViewModel
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

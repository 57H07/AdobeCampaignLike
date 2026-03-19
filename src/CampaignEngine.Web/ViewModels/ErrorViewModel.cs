namespace CampaignEngine.Web.ViewModels;

/// <summary>
/// ViewModel for error display pages.
/// </summary>
public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    public string? Detail { get; set; }
}

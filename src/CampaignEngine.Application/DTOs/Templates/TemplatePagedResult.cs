namespace CampaignEngine.Application.DTOs.Templates;

/// <summary>
/// Paginated result wrapper for template list queries.
/// </summary>
public class TemplatePagedResult
{
    public IReadOnlyList<TemplateDto> Items { get; init; } = Array.Empty<TemplateDto>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

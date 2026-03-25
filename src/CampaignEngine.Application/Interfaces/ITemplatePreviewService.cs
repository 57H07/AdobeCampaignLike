using CampaignEngine.Application.DTOs.Templates;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Orchestrates template preview: fetches sample data from a data source,
/// resolves the template with that data, and applies channel post-processing.
///
/// Preview is strictly read-only — no sends occur, no records are written.
/// Business rules:
///   - Up to 5 sample rows are fetched (BR-US010-1).
///   - First row is used for rendering by default (BR-US010-2).
///   - Channel post-processing is applied (CSS inline for Email, PDF for Letter — BR-US010-3).
///   - Missing placeholder keys are identified and returned (BR-US010-4).
/// </summary>
public interface ITemplatePreviewService
{
    /// <summary>
    /// Previews a template by rendering it with sample data from the specified data source.
    /// </summary>
    /// <param name="templateId">ID of the template to preview.</param>
    /// <param name="request">Preview parameters including data source selection and row index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="TemplatePreviewResult"/> containing the rendered output,
    /// sample data rows, and diagnostics about missing placeholders.
    /// </returns>
    Task<TemplatePreviewResult> PreviewAsync(
        Guid templateId,
        TemplatePreviewRequest request,
        CancellationToken cancellationToken = default);
}

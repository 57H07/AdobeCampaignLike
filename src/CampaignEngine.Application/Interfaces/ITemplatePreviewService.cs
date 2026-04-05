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

    /// <summary>
    /// Renders a Letter (DOCX) template using the provided sample data and returns
    /// the resulting DOCX bytes for download.
    ///
    /// F-401 contract:
    ///   - Runs the full rendering pipeline: RunMerger → ConditionalRenderer →
    ///     CollectionRenderer → ScalarReplacer.
    ///   - Preview is ephemeral: no rendered output is persisted.
    ///   - Template must be a Letter channel template.
    ///   - Template must exist and have a stored DOCX body.
    /// </summary>
    /// <param name="templateId">ID of the Letter template to preview.</param>
    /// <param name="request">Sample data to merge into the template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DocxPreviewResult"/> containing the rendered DOCX bytes and
    /// the template name (used for the Content-Disposition filename).
    /// </returns>
    /// <exception cref="CampaignEngine.Domain.Exceptions.NotFoundException">
    /// Thrown when the template does not exist.
    /// </exception>
    /// <exception cref="CampaignEngine.Domain.Exceptions.ValidationException">
    /// Thrown when the template is not a Letter channel template.
    /// </exception>
    Task<DocxPreviewResult> PreviewDocxAsync(
        Guid templateId,
        DocxPreviewRequest request,
        CancellationToken cancellationToken = default);
}

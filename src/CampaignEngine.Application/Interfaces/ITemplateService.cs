using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Application service for Template CRUD operations.
/// Enforces business rules: unique name per channel, soft delete, status management.
/// Only Designer and Admin roles can create/edit templates (enforced at controller level).
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Returns a paginated, optionally-filtered list of non-deleted templates.
    /// </summary>
    Task<TemplatePagedResult> GetPagedAsync(
        ChannelType? channel,
        TemplateStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single template by ID, or null if not found (or soft-deleted).
    /// </summary>
    Task<Template?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new template. Throws if name is not unique within the channel.
    /// </summary>
    Task<Template> CreateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates name, HTML body, and description of an existing template.
    /// Throws NotFoundException if id does not exist. Throws ValidationException if name conflicts.
    /// </summary>
    Task<Template> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a template by setting IsDeleted = true and recording DeletedAt.
    /// Throws NotFoundException if id does not exist.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all non-deleted templates flagged as sub-templates (IsSubTemplate = true).
    /// Used by the sub-template selector UI.
    /// </summary>
    Task<IReadOnlyList<TemplateSummaryDto>> GetSubTemplatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a template from Draft to Published.
    /// Business rules:
    ///   - Template must currently be in Draft status.
    ///   - Template must have a complete placeholder manifest (all HTML placeholders declared).
    /// Throws NotFoundException if template not found.
    /// Throws ValidationException if transition is not allowed.
    /// </summary>
    Task<Template> PublishAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a template to Archived status.
    /// Business rules:
    ///   - Template must be in Draft or Published status (not already Archived).
    ///   - Archived templates cannot transition back to Published.
    /// Throws NotFoundException if template not found.
    /// Throws ValidationException if transition is not allowed.
    /// </summary>
    Task<Template> ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full version history for the specified template, ordered by version descending.
    /// Returns an empty list if the template has no history entries.
    /// Throws NotFoundException if the template does not exist.
    /// </summary>
    Task<IReadOnlyList<TemplateHistoryDto>> GetHistoryAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the diff between two versions of a template.
    /// If toVersion is null, compares fromVersion against the current live version.
    /// Throws NotFoundException if the template or either version does not exist.
    /// </summary>
    Task<TemplateDiffDto> GetDiffAsync(
        Guid id,
        int fromVersion,
        int? toVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts a template to a previous version by creating a new version with the historic content.
    /// Business rule: revert creates a new version — it does not overwrite existing history.
    /// Throws NotFoundException if the template or target version does not exist.
    /// </summary>
    Task<Template> RevertToVersionAsync(
        Guid id,
        int version,
        string? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw DOCX bytes and template name for the given template ID.
    /// Business rules (F-110 / US-008):
    ///   - Template must exist and not be soft-deleted (throws NotFoundException otherwise).
    ///   - Template must be a Letter channel template (throws DomainException otherwise).
    ///   - Template must have a non-empty BodyPath (throws DomainException otherwise).
    /// </summary>
    /// <param name="id">Template GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of (docx bytes stream, template name).</returns>
    Task<(Stream Content, string TemplateName)> GetDocxBodyAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

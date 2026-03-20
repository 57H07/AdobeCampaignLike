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
}

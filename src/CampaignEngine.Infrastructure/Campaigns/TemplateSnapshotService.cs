using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using Mapster;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Creates immutable template snapshots when a campaign is scheduled.
/// Each snapshot captures the fully resolved HTML (including sub-templates)
/// at the exact moment of scheduling to guarantee reproducibility.
/// </summary>
public sealed class TemplateSnapshotService : ITemplateSnapshotService
{
    private readonly ICampaignRepository _campaignRepository;
    private readonly ITemplateRepository _templateRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISubTemplateResolverService _subTemplateResolver;
    private readonly IAppLogger<TemplateSnapshotService> _logger;

    public TemplateSnapshotService(
        ICampaignRepository campaignRepository,
        ITemplateRepository templateRepository,
        IUnitOfWork unitOfWork,
        ISubTemplateResolverService subTemplateResolver,
        IAppLogger<TemplateSnapshotService> logger)
    {
        _campaignRepository = campaignRepository;
        _templateRepository = templateRepository;
        _unitOfWork = unitOfWork;
        _subTemplateResolver = subTemplateResolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSnapshotsForCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await _campaignRepository.GetWithStepsAsync(campaignId, cancellationToken)
            ?? throw new NotFoundException("Campaign", campaignId);

        if (campaign.Steps.Count == 0)
            return;

        // Collect all unique template IDs needed
        var templateIds = campaign.Steps
            .Select(s => s.TemplateId)
            .Distinct()
            .ToList();

        var templates = await _templateRepository.GetByIdsAsync(templateIds, cancellationToken);

        // Fix #10: guard against data corruption / join duplicates that would
        // otherwise make ToDictionary throw a generic ArgumentException, which
        // surfaces to the operator with no actionable context.
        var duplicateId = templates
            .GroupBy(t => t.Id)
            .FirstOrDefault(g => g.Count() > 1)?.Key;

        if (duplicateId.HasValue)
            throw new DomainException(
                $"Template repository returned duplicate template id '{duplicateId.Value}' when building " +
                $"snapshots for campaign '{campaignId}'. Investigate the Templates table for corrupted rows.");

        var templateMap = templates.ToDictionary(t => t.Id);

        // Create one snapshot per step (each step may reference a different template)
        // Re-use snapshots within the same campaign if two steps share the same template.
        var snapshotCache = new Dictionary<Guid, TemplateSnapshot>();

        foreach (var step in campaign.Steps)
        {
            // Re-use snapshot already created for same template in this run
            if (snapshotCache.TryGetValue(step.TemplateId, out var existingSnapshot))
            {
                step.TemplateSnapshotId = existingSnapshot.Id;
                continue;
            }

            if (!templateMap.TryGetValue(step.TemplateId, out var template))
                throw new NotFoundException("Template", step.TemplateId);

            // Resolve sub-templates recursively to produce the fully-flattened body
            var resolvedBody = await _subTemplateResolver.ResolveAsync(
                template.Id,
                template.BodyPath,
                cancellationToken);

            var snapshot = new TemplateSnapshot
            {
                OriginalTemplateId = template.Id,
                TemplateVersion = template.Version,
                Name = template.Name,
                Channel = template.Channel,
                ResolvedHtmlBody = resolvedBody
            };

            await _templateRepository.AddSnapshotAsync(snapshot, cancellationToken);
            snapshotCache[step.TemplateId] = snapshot;
            step.TemplateSnapshotId = snapshot.Id;
        }

        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Created {SnapshotCount} template snapshot(s) for campaign {CampaignId}.",
            snapshotCache.Count,
            campaignId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSnapshotDto>> GetSnapshotsForCampaignAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var steps = await _templateRepository.GetCampaignStepsWithSnapshotsAsync(
            campaignId, cancellationToken);

        // Deduplicate: if two steps share the same snapshot, return it once
        var seen = new HashSet<Guid>();
        var result = new List<TemplateSnapshotDto>();

        foreach (var step in steps)
        {
            if (step.TemplateSnapshot is null) continue;
            if (!seen.Add(step.TemplateSnapshot.Id)) continue;

            result.Add(step.TemplateSnapshot.Adapt<TemplateSnapshotDto>());
        }

        return result;
    }
}

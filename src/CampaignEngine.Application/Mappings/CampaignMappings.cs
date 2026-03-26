using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Domain.Entities;
using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Mapster configuration for Campaign domain aggregate mappings.
/// Handles Campaign, CampaignStep, and TemplateSnapshot DTOs.
/// Enum fields are mapped to their string representations.
/// </summary>
public class CampaignMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CampaignStep, CampaignStepDto>()
            .Map(dest => dest.Channel, src => src.Channel.ToString());

        config.NewConfig<Campaign, CampaignDto>()
            .Map(dest => dest.Status, src => src.Status.ToString())
            .Map(dest => dest.DataSourceName, src => src.DataSource != null ? src.DataSource.Name : null)
            .Map(dest => dest.Steps, src => src.Steps.OrderBy(s => s.StepOrder).ToList());

        config.NewConfig<TemplateSnapshot, TemplateSnapshotDto>()
            .Map(dest => dest.Channel, src => src.Channel.ToString());
    }
}

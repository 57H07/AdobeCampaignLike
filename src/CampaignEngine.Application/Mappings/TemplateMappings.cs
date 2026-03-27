using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Domain.Entities;
using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Mapster configuration for Template domain aggregate mappings.
/// Handles Template, TemplateHistory, TemplateSummary, and PlaceholderManifestEntry DTOs.
/// Enum fields (Channel, Status, Type) are mapped to their string representations.
/// </summary>
public class TemplateMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Template, TemplateDto>()
            .Map(dest => dest.Channel, src => src.Channel.ToString())
            .Map(dest => dest.Status, src => src.Status.ToString());

        config.NewConfig<Template, TemplateSummaryDto>()
            .Map(dest => dest.Channel, src => src.Channel.ToString())
            .Map(dest => dest.Status, src => src.Status.ToString());

        config.NewConfig<TemplateHistory, TemplateHistoryDto>()
            .Map(dest => dest.Channel, src => src.Channel.ToString());

        config.NewConfig<PlaceholderManifestEntry, PlaceholderManifestEntryDto>()
            .Map(dest => dest.Type, src => src.Type.ToString());
    }
}

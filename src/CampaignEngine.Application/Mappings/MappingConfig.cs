using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Central Mapster configuration that registers all mapping profiles.
/// Called during DI bootstrap to produce a shared TypeAdapterConfig instance.
/// </summary>
public static class MappingConfig
{
    public static TypeAdapterConfig GetTypeAdapterConfig()
    {
        var config = new TypeAdapterConfig();
        new TemplateMappings().Register(config);
        new CampaignMappings().Register(config);
        new DataSourceMappings().Register(config);
        new ApiKeyMappings().Register(config);
        return config;
    }
}

using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Central Mapster configuration that registers all mapping profiles.
/// Called during DI bootstrap and test initialization.
/// </summary>
public static class MappingConfig
{
    private static bool _configured;
    private static readonly object _lock = new();

    /// <summary>
    /// Registers all mapping profiles on the provided config instance.
    /// </summary>
    public static void RegisterMappings(TypeAdapterConfig config)
    {
        new TemplateMappings().Register(config);
        new CampaignMappings().Register(config);
        new DataSourceMappings().Register(config);
        new ApiKeyMappings().Register(config);
        new AttachmentMappings().Register(config);
    }

    /// <summary>
    /// Configures <see cref="TypeAdapterConfig.GlobalSettings"/> with all mapping profiles.
    /// Safe to call multiple times; only the first call applies the configuration.
    /// </summary>
    public static void ConfigureGlobalSettings()
    {
        if (_configured) return;
        lock (_lock)
        {
            if (_configured) return;
            RegisterMappings(TypeAdapterConfig.GlobalSettings);

            // Fix #15: compile all registered mappings on startup so mismatches
            // (missing destination members, broken expressions) surface as a
            // loud exception at bootstrap time rather than silently producing
            // incomplete DTOs at runtime.
            TypeAdapterConfig.GlobalSettings.Compile();

            _configured = true;
        }
    }
}

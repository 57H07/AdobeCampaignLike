using CampaignEngine.Application.Mappings;
using System.Runtime.CompilerServices;

namespace CampaignEngine.Infrastructure.Tests;

/// <summary>
/// Ensures Mapster global configuration is initialized before any test runs.
/// This mirrors the Application DI bootstrap that calls MappingConfig.ConfigureGlobalSettings().
/// </summary>
internal static class MapsterTestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        MappingConfig.ConfigureGlobalSettings();
    }
}

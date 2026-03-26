using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Domain.Entities;
using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Mapster configuration for ApiKey domain entity mapping.
/// Note: IsExpired is a computed property on the ApiKey entity and maps directly.
/// </summary>
public class ApiKeyMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<ApiKey, ApiKeyDto>();
    }
}

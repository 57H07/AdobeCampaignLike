using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Domain.Entities;
using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Mapster configuration for CampaignAttachment mappings.
///
/// US-028: Static and dynamic attachment management.
/// </summary>
public class AttachmentMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CampaignAttachment, CampaignAttachmentDto>();
    }
}

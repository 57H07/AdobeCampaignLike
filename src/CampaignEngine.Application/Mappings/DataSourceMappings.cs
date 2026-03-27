using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Domain.Entities;
using Mapster;

namespace CampaignEngine.Application.Mappings;

/// <summary>
/// Mapster configuration for DataSource domain aggregate mappings.
/// Handles DataSource and DataSourceField DTOs.
/// Note: HasConnectionString is a computed field derived from EncryptedConnectionString.
/// Note: Fields are sorted by FieldName in the mapping.
/// </summary>
public class DataSourceMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DataSourceField, DataSourceFieldDto>();

        config.NewConfig<DataSource, DataSourceDto>()
            .Map(dest => dest.HasConnectionString,
                 src => !string.IsNullOrEmpty(src.EncryptedConnectionString))
            .Map(dest => dest.Fields,
                 src => src.Fields.OrderBy(f => f.FieldName).ToList());
    }
}

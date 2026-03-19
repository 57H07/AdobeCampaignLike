using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class DataSourceFieldConfiguration : IEntityTypeConfiguration<DataSourceField>
{
    public void Configure(EntityTypeBuilder<DataSourceField> builder)
    {
        builder.ToTable("DataSourceFields");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.FieldName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(f => f.DataType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(f => f.Description)
            .HasMaxLength(500);

        builder.Property(f => f.IsFilterable)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(f => f.IsRecipientAddress)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(f => f.CreatedAt)
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .IsRequired();

        builder.HasIndex(f => new { f.DataSourceId, f.FieldName })
            .IsUnique()
            .HasDatabaseName("IX_DataSourceFields_DataSourceId_FieldName");
    }
}

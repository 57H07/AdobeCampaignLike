using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class DataSourceConfiguration : IEntityTypeConfiguration<DataSource>
{
    public void Configure(EntityTypeBuilder<DataSource> builder)
    {
        builder.ToTable("DataSources");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.EncryptedConnectionString)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(d => d.Description)
            .HasMaxLength(500);

        builder.Property(d => d.Type)
            .IsRequired();

        builder.Property(d => d.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .IsRequired();

        builder.HasIndex(d => d.Name)
            .IsUnique()
            .HasDatabaseName("IX_DataSources_Name");

        // Relationships
        builder.HasMany(d => d.Fields)
            .WithOne(f => f.DataSource)
            .HasForeignKey(f => f.DataSourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

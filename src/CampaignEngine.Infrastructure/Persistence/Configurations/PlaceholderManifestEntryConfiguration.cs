using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class PlaceholderManifestEntryConfiguration : IEntityTypeConfiguration<PlaceholderManifestEntry>
{
    public void Configure(EntityTypeBuilder<PlaceholderManifestEntry> builder)
    {
        builder.ToTable("PlaceholderManifests");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.Description)
            .HasMaxLength(500);

        builder.Property(p => p.Type)
            .IsRequired();

        builder.Property(p => p.IsFromDataSource)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .IsRequired();

        // Unique key per template
        builder.HasIndex(p => new { p.TemplateId, p.Key })
            .IsUnique()
            .HasDatabaseName("IX_PlaceholderManifests_TemplateId_Key");
    }
}

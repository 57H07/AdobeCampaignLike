using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class TemplateHistoryConfiguration : IEntityTypeConfiguration<TemplateHistory>
{
    public void Configure(EntityTypeBuilder<TemplateHistory> builder)
    {
        builder.ToTable("TemplateHistory");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(h => h.BodyPath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(h => h.BodyChecksum)
            .HasMaxLength(64);

        builder.Property(h => h.ChangedBy)
            .HasMaxLength(200);

        builder.Property(h => h.Channel)
            .IsRequired();

        builder.Property(h => h.Version)
            .IsRequired();

        builder.Property(h => h.CreatedAt)
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .IsRequired();

        builder.HasIndex(h => new { h.TemplateId, h.Version })
            .HasDatabaseName("IX_TemplateHistory_TemplateId_Version");
    }
}

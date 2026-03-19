using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class TemplateSnapshotConfiguration : IEntityTypeConfiguration<TemplateSnapshot>
{
    public void Configure(EntityTypeBuilder<TemplateSnapshot> builder)
    {
        builder.ToTable("TemplateSnapshots");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.ResolvedHtmlBody)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(s => s.Channel)
            .IsRequired();

        builder.Property(s => s.TemplateVersion)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasIndex(s => s.OriginalTemplateId)
            .HasDatabaseName("IX_TemplateSnapshots_OriginalTemplateId");
    }
}

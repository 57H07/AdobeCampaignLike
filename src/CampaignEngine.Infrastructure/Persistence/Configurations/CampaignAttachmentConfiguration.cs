using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class CampaignAttachmentConfiguration : IEntityTypeConfiguration<CampaignAttachment>
{
    public void Configure(EntityTypeBuilder<CampaignAttachment> builder)
    {
        builder.ToTable("CampaignAttachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.FilePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.DynamicFieldName)
            .HasMaxLength(200);

        builder.Property(a => a.IsDynamic)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .IsRequired();

        builder.HasIndex(a => a.CampaignId)
            .HasDatabaseName("IX_CampaignAttachments_CampaignId");
    }
}

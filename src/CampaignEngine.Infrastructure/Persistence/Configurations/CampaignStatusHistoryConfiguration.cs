using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class CampaignStatusHistoryConfiguration : IEntityTypeConfiguration<CampaignStatusHistory>
{
    public void Configure(EntityTypeBuilder<CampaignStatusHistory> builder)
    {
        builder.ToTable("CampaignStatusHistories");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus)
            .IsRequired();

        builder.Property(h => h.ToStatus)
            .IsRequired();

        builder.Property(h => h.Reason)
            .HasMaxLength(500);

        builder.Property(h => h.OccurredAt)
            .IsRequired();

        builder.Property(h => h.CreatedAt)
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .IsRequired();

        // Index for quick lookup by campaign
        builder.HasIndex(h => new { h.CampaignId, h.OccurredAt })
            .HasDatabaseName("IX_CampaignStatusHistories_CampaignId_OccurredAt");

        // Relationship
        builder.HasOne(h => h.Campaign)
            .WithMany(c => c.StatusHistory)
            .HasForeignKey(h => h.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

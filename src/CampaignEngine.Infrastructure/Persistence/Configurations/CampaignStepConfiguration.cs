using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class CampaignStepConfiguration : IEntityTypeConfiguration<CampaignStep>
{
    public void Configure(EntityTypeBuilder<CampaignStep> builder)
    {
        builder.ToTable("CampaignSteps");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.StepOrder)
            .IsRequired();

        builder.Property(s => s.Channel)
            .IsRequired();

        builder.Property(s => s.DelayDays)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.StepFilter)
            .HasColumnType("nvarchar(max)");

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired();

        builder.HasIndex(s => new { s.CampaignId, s.StepOrder })
            .HasDatabaseName("IX_CampaignSteps_CampaignId_StepOrder");

        // Relationship to snapshot
        builder.HasOne(s => s.TemplateSnapshot)
            .WithMany(ts => ts.CampaignSteps)
            .HasForeignKey(s => s.TemplateSnapshotId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

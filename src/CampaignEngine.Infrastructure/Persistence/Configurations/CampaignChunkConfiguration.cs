using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class CampaignChunkConfiguration : IEntityTypeConfiguration<CampaignChunk>
{
    public void Configure(EntityTypeBuilder<CampaignChunk> builder)
    {
        builder.ToTable("CampaignChunks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.CampaignId)
            .IsRequired();

        builder.Property(c => c.CampaignStepId)
            .IsRequired();

        builder.Property(c => c.ChunkIndex)
            .IsRequired();

        builder.Property(c => c.TotalChunks)
            .IsRequired();

        builder.Property(c => c.RecipientCount)
            .IsRequired();

        builder.Property(c => c.RecipientDataJson)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.HangfireJobId)
            .HasMaxLength(200);

        builder.Property(c => c.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        // Index for efficient lookup of chunks by campaign+step
        builder.HasIndex(c => new { c.CampaignId, c.CampaignStepId })
            .HasDatabaseName("IX_CampaignChunks_CampaignId_StepId");

        // Index for completion counting query
        builder.HasIndex(c => new { c.CampaignStepId, c.Status })
            .HasDatabaseName("IX_CampaignChunks_StepId_Status");

        // Relationships
        builder.HasOne(c => c.Campaign)
            .WithMany()
            .HasForeignKey(c => c.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.CampaignStep)
            .WithMany()
            .HasForeignKey(c => c.CampaignStepId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

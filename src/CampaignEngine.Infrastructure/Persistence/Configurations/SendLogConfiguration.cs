using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class SendLogConfiguration : IEntityTypeConfiguration<SendLog>
{
    public void Configure(EntityTypeBuilder<SendLog> builder)
    {
        builder.ToTable("SendLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.RecipientAddress)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(l => l.RecipientId)
            .HasMaxLength(200);

        builder.Property(l => l.Channel)
            .IsRequired();

        builder.Property(l => l.Status)
            .IsRequired();

        builder.Property(l => l.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(l => l.ErrorDetail)
            .HasColumnType("nvarchar(max)");

        builder.Property(l => l.CorrelationId)
            .HasMaxLength(100);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .IsRequired();

        // Indexes for common query patterns
        builder.HasIndex(l => l.CampaignId)
            .HasDatabaseName("IX_SendLogs_CampaignId");

        builder.HasIndex(l => l.Status)
            .HasDatabaseName("IX_SendLogs_Status");

        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("IX_SendLogs_CreatedAt");

        builder.HasIndex(l => l.CorrelationId)
            .HasDatabaseName("IX_SendLogs_CorrelationId");
    }
}

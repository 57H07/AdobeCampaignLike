using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class AuthAuditLogConfiguration : IEntityTypeConfiguration<AuthAuditLog>
{
    public void Configure(EntityTypeBuilder<AuthAuditLog> builder)
    {
        builder.ToTable("AuthAuditLogs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(l => l.UserId)
            .HasMaxLength(450); // ASP.NET Core Identity default max length

        builder.Property(l => l.UserName)
            .HasMaxLength(256);

        builder.Property(l => l.Details)
            .HasMaxLength(1000);

        builder.Property(l => l.IpAddress)
            .HasMaxLength(50);

        builder.Property(l => l.Succeeded)
            .IsRequired();

        builder.Property(l => l.OccurredAt)
            .IsRequired();

        builder.HasIndex(l => l.OccurredAt)
            .HasDatabaseName("IX_AuthAuditLogs_OccurredAt");

        builder.HasIndex(l => l.UserId)
            .HasDatabaseName("IX_AuthAuditLogs_UserId");

        builder.HasIndex(l => l.EventType)
            .HasDatabaseName("IX_AuthAuditLogs_EventType");
    }
}

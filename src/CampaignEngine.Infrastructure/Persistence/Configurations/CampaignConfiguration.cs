using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(c => c.Status)
            .IsRequired();

        builder.Property(c => c.FilterExpression)
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.FreeFieldValues)
            .HasColumnType("nvarchar(max)");

        builder.Property(c => c.StaticCcAddresses)
            .HasMaxLength(2000);

        builder.Property(c => c.DynamicCcField)
            .HasMaxLength(200);

        builder.Property(c => c.StaticBccAddresses)
            .HasMaxLength(2000);

        builder.Property(c => c.CreatedBy)
            .HasMaxLength(200);

        builder.Property(c => c.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .IsRequired();

        // Unique campaign name
        builder.HasIndex(c => c.Name)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Campaigns_Name");

        // Global query filter for soft delete
        builder.HasQueryFilter(c => !c.IsDeleted);

        // Relationships
        builder.HasOne(c => c.DataSource)
            .WithMany(d => d.Campaigns)
            .HasForeignKey(c => c.DataSourceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.Steps)
            .WithOne(s => s.Campaign)
            .HasForeignKey(s => s.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Attachments)
            .WithOne(a => a.Campaign)
            .HasForeignKey(a => a.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.SendLogs)
            .WithOne(l => l.Campaign)
            .HasForeignKey(l => l.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

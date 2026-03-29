using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class TemplateConfiguration : IEntityTypeConfiguration<Template>
{
    public void Configure(EntityTypeBuilder<Template> builder)
    {
        builder.ToTable("Templates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.BodyPath)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(t => t.BodyChecksum)
            .HasMaxLength(64);

        builder.Property(t => t.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        builder.Property(t => t.Description)
            .HasMaxLength(500);

        builder.Property(t => t.Channel)
            .IsRequired();

        builder.Property(t => t.Status)
            .IsRequired();

        builder.Property(t => t.Version)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(t => t.IsSubTemplate)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired();

        // Unique index: template name must be unique within same channel
        builder.HasIndex(t => new { t.Name, t.Channel })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_Templates_Name_Channel");

        // Global query filter for soft delete
        builder.HasQueryFilter(t => !t.IsDeleted);

        // Relationships
        builder.HasMany(t => t.PlaceholderManifests)
            .WithOne(p => p.Template)
            .HasForeignKey(p => p.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.History)
            .WithOne(h => h.Template)
            .HasForeignKey(h => h.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

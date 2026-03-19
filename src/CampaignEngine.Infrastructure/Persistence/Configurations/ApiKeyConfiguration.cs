using CampaignEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CampaignEngine.Infrastructure.Persistence.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(k => k.Id);

        builder.Property(k => k.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(k => k.KeyHash)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(k => k.CreatedBy)
            .HasMaxLength(200);

        builder.Property(k => k.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(k => k.CreatedAt)
            .IsRequired();

        builder.Property(k => k.UpdatedAt)
            .IsRequired();

        builder.HasIndex(k => k.Name)
            .IsUnique()
            .HasDatabaseName("IX_ApiKeys_Name");

        builder.HasIndex(k => k.IsActive)
            .HasDatabaseName("IX_ApiKeys_IsActive");
    }
}

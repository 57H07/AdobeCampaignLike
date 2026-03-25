using CampaignEngine.Domain.Entities;
using CampaignEngine.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core DbContext for CampaignEngine.
/// Extends IdentityDbContext to include ASP.NET Core Identity tables
/// (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.).
/// All entity mappings applied via IEntityTypeConfiguration classes in the Configurations folder.
/// </summary>
public class CampaignEngineDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
{
    public CampaignEngineDbContext(DbContextOptions<CampaignEngineDbContext> options)
        : base(options)
    {
    }

    // Core entities
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<PlaceholderManifestEntry> PlaceholderManifests => Set<PlaceholderManifestEntry>();
    public DbSet<TemplateHistory> TemplateHistories => Set<TemplateHistory>();
    public DbSet<TemplateSnapshot> TemplateSnapshots => Set<TemplateSnapshot>();

    // Data source entities
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<DataSourceField> DataSourceFields => Set<DataSourceField>();

    // Campaign entities
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignStep> CampaignSteps => Set<CampaignStep>();
    public DbSet<CampaignAttachment> CampaignAttachments => Set<CampaignAttachment>();
    public DbSet<CampaignChunk> CampaignChunks => Set<CampaignChunk>();

    // Tracking and audit
    public DbSet<SendLog> SendLogs => Set<SendLog>();

    // API authentication
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    // Security audit trail
    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CampaignEngineDbContext).Assembly);
    }

    /// <summary>
    /// Automatically updates UpdatedAt timestamp before saving changes.
    /// </summary>
    public override int SaveChanges()
    {
        SetAuditDates();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetAuditDates();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetAuditDates()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Properties.Any(p => p.Metadata.Name == "UpdatedAt"))
            {
                entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Added &&
                entry.Properties.Any(p => p.Metadata.Name == "CreatedAt"))
            {
                entry.Property("CreatedAt").CurrentValue = DateTime.UtcNow;
            }
        }
    }
}

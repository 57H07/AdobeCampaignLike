using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CampaignEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds the database with representative development data.
/// Run only in Development environment via IHost extension.
/// Data is idempotent — re-running will not create duplicates.
/// </summary>
public class DatabaseSeeder
{
    private readonly CampaignEngineDbContext _context;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(CampaignEngineDbContext context, ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Applies pending migrations and seeds all reference data.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Applying pending database migrations...");
        await _context.Database.MigrateAsync(cancellationToken);

        await SeedTemplatesAsync(cancellationToken);
        await SeedDataSourcesAsync(cancellationToken);

        _logger.LogInformation("Database seeding completed.");
    }

    private async Task SeedTemplatesAsync(CancellationToken cancellationToken)
    {
        if (await _context.Templates.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Templates already seeded — skipping.");
            return;
        }

        _logger.LogInformation("Seeding sample templates...");

        var emailTemplate = new Template
        {
            Id = Guid.Parse("00000000-0000-0000-0001-000000000001"),
            Name = "Welcome Email",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Published,
            Version = 1,
            Description = "Welcome email sent to new customers",
            BodyPath = "templates/00000000-0000-0000-0001-000000000001/v1.html",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var smsTemplate = new Template
        {
            Id = Guid.Parse("00000000-0000-0000-0001-000000000002"),
            Name = "Appointment Reminder SMS",
            Channel = ChannelType.Sms,
            Status = TemplateStatus.Published,
            Version = 1,
            Description = "SMS reminder for scheduled appointments",
            BodyPath = "templates/00000000-0000-0000-0001-000000000002/v1.txt",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var draftTemplate = new Template
        {
            Id = Guid.Parse("00000000-0000-0000-0001-000000000003"),
            Name = "Monthly Newsletter",
            Channel = ChannelType.Email,
            Status = TemplateStatus.Draft,
            Version = 1,
            Description = "Monthly newsletter template — work in progress",
            BodyPath = "templates/00000000-0000-0000-0001-000000000003/v1.html",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Templates.AddRange(emailTemplate, smsTemplate, draftTemplate);

        // Add placeholder manifests for the published email template
        _context.PlaceholderManifests.AddRange(
            new PlaceholderManifestEntry
            {
                TemplateId = emailTemplate.Id,
                Key = "firstname",
                Type = PlaceholderType.Scalar,
                IsFromDataSource = true,
                Description = "Recipient first name from data source",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new PlaceholderManifestEntry
            {
                TemplateId = emailTemplate.Id,
                Key = "lastname",
                Type = PlaceholderType.Scalar,
                IsFromDataSource = true,
                Description = "Recipient last name from data source",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} sample templates.", 3);
    }

    private async Task SeedDataSourcesAsync(CancellationToken cancellationToken)
    {
        if (await _context.DataSources.AnyAsync(cancellationToken))
        {
            _logger.LogDebug("Data sources already seeded — skipping.");
            return;
        }

        _logger.LogInformation("Seeding sample data source definitions...");

        // Note: In development, the connection string is stored as plaintext placeholder.
        // In production, it would be encrypted via IConnectionStringEncryptor before saving.
        var sampleDataSource = new DataSource
        {
            Id = Guid.Parse("00000000-0000-0000-0002-000000000001"),
            Name = "Sample Customer Database",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "PLACEHOLDER_ENCRYPTED_CONNECTION_STRING",
            Description = "Sample SQL Server data source for development testing",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.DataSources.Add(sampleDataSource);

        _context.DataSourceFields.AddRange(
            new DataSourceField
            {
                DataSourceId = sampleDataSource.Id,
                FieldName = "CustomerId",
                DataType = "uniqueidentifier",
                IsFilterable = true,
                IsRecipientAddress = false,
                Description = "Unique customer identifier",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new DataSourceField
            {
                DataSourceId = sampleDataSource.Id,
                FieldName = "FirstName",
                DataType = "nvarchar",
                IsFilterable = true,
                IsRecipientAddress = false,
                Description = "Customer first name",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new DataSourceField
            {
                DataSourceId = sampleDataSource.Id,
                FieldName = "LastName",
                DataType = "nvarchar",
                IsFilterable = true,
                IsRecipientAddress = false,
                Description = "Customer last name",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new DataSourceField
            {
                DataSourceId = sampleDataSource.Id,
                FieldName = "Email",
                DataType = "nvarchar",
                IsFilterable = true,
                IsRecipientAddress = true,
                Description = "Customer email address (used as recipient for email campaigns)",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new DataSourceField
            {
                DataSourceId = sampleDataSource.Id,
                FieldName = "PhoneNumber",
                DataType = "nvarchar",
                IsFilterable = false,
                IsRecipientAddress = true,
                Description = "Customer phone number in E.164 format (used as recipient for SMS campaigns)",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        );

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded 1 sample data source with {FieldCount} fields.", 5);
    }
}

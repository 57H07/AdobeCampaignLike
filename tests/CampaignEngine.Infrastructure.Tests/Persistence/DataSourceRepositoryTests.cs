using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests for DataSource entity persistence using the in-memory database.
/// </summary>
public class DataSourceRepositoryTests : DbContextTestBase
{
    [Fact]
    public async Task CanSaveAndRetrieveDataSource()
    {
        // Arrange
        var dataSource = new DataSource
        {
            Name = "Customer DB",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENCRYPTED_STRING",
            Description = "Main customer database",
            IsActive = true
        };

        // Act
        Context.DataSources.Add(dataSource);
        await Context.SaveChangesAsync();

        var retrieved = await Context.DataSources.FirstOrDefaultAsync(d => d.Id == dataSource.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Customer DB");
        retrieved.Type.Should().Be(DataSourceType.SqlServer);
        retrieved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DataSourceFields_AreCascadeDeleted_WithDataSource()
    {
        // Arrange
        var dataSource = new DataSource
        {
            Name = "Field Test DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC",
            IsActive = true
        };
        Context.DataSources.Add(dataSource);
        await Context.SaveChangesAsync();

        var field = new DataSourceField
        {
            DataSourceId = dataSource.Id,
            FieldName = "Email",
            DataType = "nvarchar",
            IsFilterable = true,
            IsRecipientAddress = true
        };
        Context.DataSourceFields.Add(field);
        await Context.SaveChangesAsync();

        // Act
        Context.DataSources.Remove(dataSource);
        await Context.SaveChangesAsync();

        // Assert
        var fields = await Context.DataSourceFields
            .Where(f => f.DataSourceId == dataSource.Id)
            .ToListAsync();

        fields.Should().BeEmpty();
    }

    [Fact]
    public async Task CanSaveMultipleFieldsForSameDataSource()
    {
        // Arrange
        var dataSource = new DataSource
        {
            Name = "Multi-Field DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC",
            IsActive = true
        };
        Context.DataSources.Add(dataSource);
        await Context.SaveChangesAsync();

        var fields = new[]
        {
            new DataSourceField { DataSourceId = dataSource.Id, FieldName = "Id", DataType = "int", IsFilterable = true },
            new DataSourceField { DataSourceId = dataSource.Id, FieldName = "Email", DataType = "nvarchar", IsFilterable = true, IsRecipientAddress = true },
            new DataSourceField { DataSourceId = dataSource.Id, FieldName = "Phone", DataType = "nvarchar", IsFilterable = false, IsRecipientAddress = true }
        };

        Context.DataSourceFields.AddRange(fields);
        await Context.SaveChangesAsync();

        // Act
        var dataSourceWithFields = await Context.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == dataSource.Id);

        // Assert
        dataSourceWithFields.Should().NotBeNull();
        dataSourceWithFields!.Fields.Should().HaveCount(3);
        dataSourceWithFields.Fields.Should().ContainSingle(f => f.IsRecipientAddress && f.FieldName == "Email");
    }
}

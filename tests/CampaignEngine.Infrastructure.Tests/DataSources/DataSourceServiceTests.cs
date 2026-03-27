using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.DataSources;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.DataSources;

/// <summary>
/// Unit tests for DataSourceService using an in-memory database.
/// Covers CRUD, schema management, and connection testing delegation.
/// </summary>
public class DataSourceServiceTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly Mock<IConnectionStringEncryptor> _encryptorMock;
    private readonly Mock<IConnectionTestService> _connectionTestMock;
    private readonly Mock<IAppLogger<DataSourceService>> _loggerMock;
    private readonly DataSourceService _service;

    public DataSourceServiceTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);

        _encryptorMock = new Mock<IConnectionStringEncryptor>();
        // Default: round-trip encryption returns the same string for test purposes
        _encryptorMock.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => $"ENC({s})");
        _encryptorMock.Setup(e => e.Decrypt(It.IsAny<string>())).Returns<string>(s =>
            s.StartsWith("ENC(") ? s[4..^1] : s);

        _connectionTestMock = new Mock<IConnectionTestService>();
        _loggerMock = new Mock<IAppLogger<DataSourceService>>();

        var dataSourceRepository = new DataSourceRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        _service = new DataSourceService(
            dataSourceRepository, unitOfWork, _encryptorMock.Object,
            _connectionTestMock.Object, _loggerMock.Object);
    }

    public void Dispose() => _context.Dispose();

    // ----------------------------------------------------------------
    // CreateAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithValidRequest_PersistsAndReturnsDto()
    {
        // Arrange
        var request = new CreateDataSourceRequest
        {
            Name = "CRM Database",
            Type = DataSourceType.SqlServer,
            ConnectionString = "Server=crm;Database=crm;Uid=app;Pwd=pass;",
            Description = "Main CRM source"
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("CRM Database");
        result.Type.Should().Be(DataSourceType.SqlServer);
        result.HasConnectionString.Should().BeTrue();
        result.IsActive.Should().BeTrue();

        var stored = await _context.DataSources.FindAsync(result.Id);
        stored.Should().NotBeNull();
        stored!.EncryptedConnectionString.Should().Be("ENC(Server=crm;Database=crm;Uid=app;Pwd=pass;)");
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateName_ThrowsValidationException()
    {
        // Arrange — seed an existing data source
        _context.DataSources.Add(new DataSource
        {
            Name = "Existing DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(cs)",
            IsActive = true
        });
        await _context.SaveChangesAsync();

        var request = new CreateDataSourceRequest
        {
            Name = "Existing DS",
            Type = DataSourceType.SqlServer,
            ConnectionString = "Server=another;"
        };

        // Act
        var act = () => _service.CreateAsync(request);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*validation*");
    }

    [Fact]
    public async Task CreateAsync_WithFields_PersistsFieldSchema()
    {
        // Arrange
        var request = new CreateDataSourceRequest
        {
            Name = "DS With Fields",
            Type = DataSourceType.SqlServer,
            ConnectionString = "cs",
            Fields = new List<UpsertFieldRequest>
            {
                new() { FieldName = "Email",    DataType = "nvarchar", IsFilterable = true, IsRecipientAddress = true },
                new() { FieldName = "CustomerId", DataType = "int",    IsFilterable = true }
            }
        };

        // Act
        var result = await _service.CreateAsync(request);

        // Assert
        result.Fields.Should().HaveCount(2);
        result.Fields.Should().ContainSingle(f => f.FieldName == "Email" && f.IsRecipientAddress);
    }

    // ----------------------------------------------------------------
    // UpdateAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WithValidChange_UpdatesEntity()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "Old Name",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(old-cs)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        var request = new UpdateDataSourceRequest
        {
            Name = "New Name",
            Type = DataSourceType.RestApi,
            Description = "Updated"
        };

        // Act
        var result = await _service.UpdateAsync(ds.Id, request);

        // Assert
        result.Name.Should().Be("New Name");
        result.Type.Should().Be(DataSourceType.RestApi);

        // Connection string should remain unchanged when not supplied
        var stored = await _context.DataSources.FindAsync(ds.Id);
        stored!.EncryptedConnectionString.Should().Be("ENC(old-cs)");
    }

    [Fact]
    public async Task UpdateAsync_WithNewConnectionString_ReEncrypts()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(old)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        var request = new UpdateDataSourceRequest
        {
            Name = "DS",
            Type = DataSourceType.SqlServer,
            ConnectionString = "new-plaintext-cs"
        };

        // Act
        await _service.UpdateAsync(ds.Id, request);

        // Assert
        var stored = await _context.DataSources.FindAsync(ds.Id);
        stored!.EncryptedConnectionString.Should().Be("ENC(new-plaintext-cs)");
        _encryptorMock.Verify(e => e.Encrypt("new-plaintext-cs"), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ThrowsNotFoundException()
    {
        // Arrange
        var request = new UpdateDataSourceRequest
        {
            Name = "X",
            Type = DataSourceType.SqlServer
        };

        // Act
        var act = () => _service.UpdateAsync(Guid.NewGuid(), request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // GetAllAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_WithNoFilter_ReturnsAll()
    {
        // Arrange
        _context.DataSources.AddRange(
            new DataSource { Name = "DS1", Type = DataSourceType.SqlServer, EncryptedConnectionString = "ENC(a)", IsActive = true },
            new DataSource { Name = "DS2", Type = DataSourceType.RestApi, EncryptedConnectionString = "ENC(b)", IsActive = false }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAsync(new DataSourceFilter { Page = 1, PageSize = 20 });

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAllAsync_WithTypeFilter_ReturnsOnlyMatchingType()
    {
        // Arrange
        _context.DataSources.AddRange(
            new DataSource { Name = "SQL1", Type = DataSourceType.SqlServer, EncryptedConnectionString = "ENC(a)", IsActive = true },
            new DataSource { Name = "REST1", Type = DataSourceType.RestApi, EncryptedConnectionString = "ENC(b)", IsActive = true }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAsync(new DataSourceFilter
        {
            Type = DataSourceType.SqlServer,
            Page = 1, PageSize = 20
        });

        // Assert
        result.Total.Should().Be(1);
        result.Items.Should().ContainSingle(d => d.Name == "SQL1");
    }

    [Fact]
    public async Task GetAllAsync_WithIsActiveFilter_ReturnsOnlyActive()
    {
        // Arrange
        _context.DataSources.AddRange(
            new DataSource { Name = "Active", Type = DataSourceType.SqlServer, EncryptedConnectionString = "ENC(a)", IsActive = true },
            new DataSource { Name = "Inactive", Type = DataSourceType.SqlServer, EncryptedConnectionString = "ENC(b)", IsActive = false }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAsync(new DataSourceFilter
        {
            IsActive = true,
            Page = 1, PageSize = 20
        });

        // Assert
        result.Total.Should().Be(1);
        result.Items[0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetAllAsync_WithNameContainsFilter_ReturnsMatchingNames()
    {
        // Arrange
        _context.DataSources.AddRange(
            new DataSource { Name = "Customer Database", Type = DataSourceType.SqlServer, EncryptedConnectionString = "ENC(a)", IsActive = true },
            new DataSource { Name = "Order Database",    Type = DataSourceType.SqlServer, EncryptedConnectionString = "ENC(b)", IsActive = true },
            new DataSource { Name = "Product Catalog",   Type = DataSourceType.RestApi,   EncryptedConnectionString = "ENC(c)", IsActive = true }
        );
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetAllAsync(new DataSourceFilter
        {
            NameContains = "Database",
            Page = 1, PageSize = 20
        });

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().AllSatisfy(d => d.Name.Should().Contain("Database"));
    }

    // ----------------------------------------------------------------
    // GetByIdAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ReturnsDto()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "Target DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(cs)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.GetByIdAsync(ds.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(ds.Id);
        result.Name.Should().Be("Target DS");
        result.HasConnectionString.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNull()
    {
        // Act
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // TestConnectionAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task TestConnectionAsync_DelegatesToConnectionTestService()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "Test DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(Server=prod;Database=db;)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        _connectionTestMock
            .Setup(s => s.TestAsync(DataSourceType.SqlServer, "Server=prod;Database=db;", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConnectionTestResult.Ok("Connected", 50));

        // Act
        var result = await _service.TestConnectionAsync(ds.Id);

        // Assert
        result.Success.Should().BeTrue();
        result.Message.Should().Be("Connected");
        _encryptorMock.Verify(e => e.Decrypt("ENC(Server=prod;Database=db;)"), Times.Once);
    }

    [Fact]
    public async Task TestConnectionAsync_WithNonExistentId_ThrowsNotFoundException()
    {
        // Act
        var act = () => _service.TestConnectionAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ----------------------------------------------------------------
    // UpdateSchemaAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateSchemaAsync_ReplacesAllExistingFields()
    {
        // Arrange — data source with 2 existing fields (added explicitly to DbSet)
        var ds = new DataSource
        {
            Name = "Schema DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(cs)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        _context.DataSourceFields.AddRange(
            new DataSourceField { DataSourceId = ds.Id, FieldName = "OldField1", DataType = "nvarchar" },
            new DataSourceField { DataSourceId = ds.Id, FieldName = "OldField2", DataType = "int" }
        );
        await _context.SaveChangesAsync();

        var newFields = new List<UpsertFieldRequest>
        {
            new() { FieldName = "Email",  DataType = "nvarchar", IsFilterable = true, IsRecipientAddress = true },
            new() { FieldName = "Age",    DataType = "int",      IsFilterable = true },
            new() { FieldName = "Region", DataType = "nvarchar", IsFilterable = false }
        };

        // Act
        var result = await _service.UpdateSchemaAsync(ds.Id, newFields);

        // Assert
        result.Fields.Should().HaveCount(3);
        result.Fields.Should().NotContain(f => f.FieldName == "OldField1");
        result.Fields.Should().ContainSingle(f => f.FieldName == "Email" && f.IsRecipientAddress);
    }

    [Fact]
    public async Task UpdateSchemaAsync_WithEmptyList_ClearsAllFields()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "Clear Schema DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(cs)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        _context.DataSourceFields.Add(
            new DataSourceField { DataSourceId = ds.Id, FieldName = "Field1", DataType = "nvarchar" });
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.UpdateSchemaAsync(ds.Id, []);

        // Assert
        result.Fields.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // SetActiveAsync tests
    // ----------------------------------------------------------------

    [Fact]
    public async Task SetActiveAsync_ActivatesInactiveDataSource()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "Inactive DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(cs)",
            IsActive = false
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SetActiveAsync(ds.Id, true);

        // Assert
        result.IsActive.Should().BeTrue();
        var stored = await _context.DataSources.FindAsync(ds.Id);
        stored!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SetActiveAsync_DeactivatesActiveDataSource()
    {
        // Arrange
        var ds = new DataSource
        {
            Name = "Active DS",
            Type = DataSourceType.SqlServer,
            EncryptedConnectionString = "ENC(cs)",
            IsActive = true
        };
        _context.DataSources.Add(ds);
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.SetActiveAsync(ds.Id, false);

        // Assert
        result.IsActive.Should().BeFalse();
    }
}

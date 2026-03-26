using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Manages data source CRUD, schema management, and connection testing.
/// Connection strings are encrypted at rest using ASP.NET Core Data Protection.
/// Admin role enforcement is done at the controller/page layer.
/// </summary>
public sealed class DataSourceService : IDataSourceService
{
    private readonly CampaignEngineDbContext _dbContext;
    private readonly IConnectionStringEncryptor _encryptor;
    private readonly IConnectionTestService _connectionTestService;
    private readonly IAppLogger<DataSourceService> _logger;

    public DataSourceService(
        CampaignEngineDbContext dbContext,
        IConnectionStringEncryptor encryptor,
        IConnectionTestService connectionTestService,
        IAppLogger<DataSourceService> logger)
    {
        _dbContext = dbContext;
        _encryptor = encryptor;
        _connectionTestService = connectionTestService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DataSourceDto> CreateAsync(
        CreateDataSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate unique name
        var exists = await _dbContext.DataSources
            .AnyAsync(d => d.Name == request.Name, cancellationToken);

        if (exists)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["name"] = [$"A data source named '{request.Name}' already exists."]
            });

        var encryptedCs = _encryptor.Encrypt(request.ConnectionString);

        var dataSource = new DataSource
        {
            Name = request.Name,
            Type = request.Type,
            EncryptedConnectionString = encryptedCs,
            Description = request.Description,
            IsActive = true
        };

        if (request.Fields.Count > 0)
        {
            foreach (var f in request.Fields)
            {
                dataSource.Fields.Add(new DataSourceField
                {
                    FieldName = f.FieldName,
                    DataType = f.DataType,
                    IsFilterable = f.IsFilterable,
                    IsRecipientAddress = f.IsRecipientAddress,
                    Description = f.Description
                });
            }
        }

        _dbContext.DataSources.Add(dataSource);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DataSource created. Id={DataSourceId}, Name={Name}, Type={Type}",
            dataSource.Id, dataSource.Name, dataSource.Type);

        return dataSource.Adapt<DataSourceDto>();
    }

    /// <inheritdoc />
    public async Task<DataSourceDto> UpdateAsync(
        Guid id,
        UpdateDataSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var dataSource = await _dbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dataSource is null)
            throw new NotFoundException("DataSource", id);

        // Validate unique name (excluding self)
        var nameConflict = await _dbContext.DataSources
            .AnyAsync(d => d.Name == request.Name && d.Id != id, cancellationToken);

        if (nameConflict)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["name"] = [$"A data source named '{request.Name}' already exists."]
            });

        dataSource.Name = request.Name;
        dataSource.Type = request.Type;
        dataSource.Description = request.Description;

        // Re-encrypt connection string only if a new value was supplied
        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            dataSource.EncryptedConnectionString = _encryptor.Encrypt(request.ConnectionString);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DataSource updated. Id={DataSourceId}, Name={Name}",
            dataSource.Id, dataSource.Name);

        return dataSource.Adapt<DataSourceDto>();
    }

    /// <inheritdoc />
    public async Task<DataSourcePagedResult> GetAllAsync(
        DataSourceFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.DataSources
            .Include(d => d.Fields)
            .AsQueryable();

        if (filter.Type.HasValue)
            query = query.Where(d => d.Type == filter.Type.Value);

        if (filter.IsActive.HasValue)
            query = query.Where(d => d.IsActive == filter.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
            query = query.Where(d => d.Name.Contains(filter.NameContains));

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderBy(d => d.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new DataSourcePagedResult
        {
            Items = items.Select(d => d.Adapt<DataSourceDto>()).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<DataSourceDto?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var dataSource = await _dbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        return dataSource is null ? null : dataSource.Adapt<DataSourceDto>();
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestConnectionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var dataSource = await _dbContext.DataSources
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dataSource is null)
            throw new NotFoundException("DataSource", id);

        var plainCs = _encryptor.Decrypt(dataSource.EncryptedConnectionString);
        return await _connectionTestService.TestAsync(dataSource.Type, plainCs, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestConnectionRawAsync(
        TestConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _connectionTestService.TestAsync(
            request.Type,
            request.ConnectionString,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DataSourceDto> UpdateSchemaAsync(
        Guid id,
        IReadOnlyList<UpsertFieldRequest> fields,
        CancellationToken cancellationToken = default)
    {
        var dataSource = await _dbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dataSource is null)
            throw new NotFoundException("DataSource", id);

        // Delete existing fields first in a separate save, then add new ones.
        // This two-step approach avoids InMemory provider tracking conflicts.
        var oldFields = dataSource.Fields.ToList();
        _dbContext.DataSourceFields.RemoveRange(oldFields);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var newFieldEntities = fields.Select(f => new DataSourceField
        {
            DataSourceId = id,
            FieldName = f.FieldName,
            DataType = f.DataType,
            IsFilterable = f.IsFilterable,
            IsRecipientAddress = f.IsRecipientAddress,
            Description = f.Description
        }).ToList();

        _dbContext.DataSourceFields.AddRange(newFieldEntities);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Refresh the navigation collection to reflect the new schema
        await _dbContext.Entry(dataSource)
            .Collection(d => d.Fields)
            .LoadAsync(cancellationToken);

        _logger.LogInformation(
            "DataSource schema updated. Id={DataSourceId}, FieldCount={Count}",
            id, fields.Count);

        return dataSource.Adapt<DataSourceDto>();
    }

    /// <inheritdoc />
    public async Task<DataSourceDto> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var dataSource = await _dbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (dataSource is null)
            throw new NotFoundException("DataSource", id);

        dataSource.IsActive = isActive;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DataSource IsActive changed. Id={DataSourceId}, IsActive={IsActive}",
            id, isActive);

        return dataSource.Adapt<DataSourceDto>();
    }

}

using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Domain.Filters;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Executes filtered preview queries against data sources.
///
/// Flow:
///   1. Resolve and decrypt the data source connection string.
///   2. Validate the filter expressions against the declared schema.
///   3. Translate valid expressions to a parameterized SQL WHERE clause.
///   4. Delegate execution to the appropriate <see cref="IDataSourceConnector"/>.
///   5. Cap the result at 100 rows (Business Rule 4).
/// </summary>
public sealed class DataSourcePreviewService : IDataSourcePreviewService
{
    private const int MaxPreviewRows = 100;

    private readonly CampaignEngineDbContext _dbContext;
    private readonly IConnectionStringEncryptor _encryptor;
    private readonly IDataSourceConnectorRegistry _connectorRegistry;
    private readonly IFilterExpressionValidator _validator;
    private readonly IFilterAstTranslator _translator;
    private readonly IAppLogger<DataSourcePreviewService> _logger;

    public DataSourcePreviewService(
        CampaignEngineDbContext dbContext,
        IConnectionStringEncryptor encryptor,
        IDataSourceConnectorRegistry connectorRegistry,
        IFilterExpressionValidator validator,
        IFilterAstTranslator translator,
        IAppLogger<DataSourcePreviewService> logger)
    {
        _dbContext = dbContext;
        _encryptor = encryptor;
        _connectorRegistry = connectorRegistry;
        _validator = validator;
        _translator = translator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PreviewDataSourceResult> PreviewAsync(
        Guid dataSourceId,
        PreviewDataSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ----------------------------------------------------------------
        // 1. Resolve data source
        // ----------------------------------------------------------------
        var dataSource = await _dbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == dataSourceId, cancellationToken);

        if (dataSource is null)
            throw new NotFoundException("DataSource", dataSourceId);

        // Build field DTO list for validation and translation
        var fieldDtos = dataSource.Fields
            .Select(f => new FieldDefinitionDto
            {
                FieldName = f.FieldName,
                DisplayName = f.FieldName,
                FieldType = f.DataType,
                IsFilterable = f.IsFilterable
            })
            .ToList();

        var filters = request.Filters ?? [];

        // ----------------------------------------------------------------
        // 2. Validate filter expressions
        // ----------------------------------------------------------------
        if (filters.Count > 0)
        {
            var validationResult = _validator.Validate(filters, fieldDtos);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Preview filter validation failed for DataSource {DataSourceId}: {Errors}",
                    dataSourceId, string.Join("; ", validationResult.Errors));

                return new PreviewDataSourceResult
                {
                    DataSourceId = dataSourceId,
                    HasFilters = true,
                    ValidationErrors = validationResult.Errors
                };
            }
        }

        // ----------------------------------------------------------------
        // 3. Translate filter AST to SQL WHERE clause (for transparency)
        // ----------------------------------------------------------------
        string? whereClause = null;
        if (filters.Count > 0)
        {
            var translationResult = _translator.Translate(filters, fieldDtos);
            whereClause = translationResult.WhereClause;
        }

        // ----------------------------------------------------------------
        // 4. Build connector definition and execute query
        // ----------------------------------------------------------------
        var plainCs = _encryptor.Decrypt(dataSource.EncryptedConnectionString);

        var definition = new DataSourceDefinitionDto
        {
            Id = dataSource.Id,
            Name = dataSource.Name,
            Type = dataSource.Type,
            ConnectionString = plainCs,
            Fields = fieldDtos
        };

        // Translate domain AST to FilterExpressionDto list for the connector
        var filterDtos = filters.Count > 0
            ? filters.Select(MapToDto).ToList()
            : null;

        _logger.LogInformation(
            "DataSourcePreviewService: executing preview for DataSource {DataSourceId}, HasFilters={HasFilters}",
            dataSourceId, filters.Count > 0);

        IReadOnlyList<IDictionary<string, object?>> rows;
        try
        {
            var connector = _connectorRegistry.GetConnector(dataSource.Type);
            rows = await connector.QueryAsync(definition, filterDtos, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DataSourcePreviewService: query failed for DataSource {DataSourceId}",
                dataSourceId);
            throw;
        }

        // ----------------------------------------------------------------
        // 5. Cap at MaxPreviewRows (Business Rule 4)
        // ----------------------------------------------------------------
        var maxRows = Math.Min(request.MaxRows ?? MaxPreviewRows, MaxPreviewRows);
        var previewRows = rows
            .Take(maxRows)
            .Select(r => (IReadOnlyDictionary<string, object?>)r.ToDictionary(kv => kv.Key, kv => kv.Value))
            .ToList();

        _logger.LogInformation(
            "DataSourcePreviewService: preview for DataSource {DataSourceId} returned {RowCount} rows (total fetched: {Total})",
            dataSourceId, previewRows.Count, rows.Count);

        return new PreviewDataSourceResult
        {
            DataSourceId = dataSourceId,
            Rows = previewRows,
            TotalCount = rows.Count,
            HasFilters = filters.Count > 0,
            AppliedWhereClause = whereClause
        };
    }

    // ----------------------------------------------------------------
    // Private: map domain FilterExpression to FilterExpressionDto
    // ----------------------------------------------------------------

    private static FilterExpressionDto MapToDto(FilterExpression expr)
    {
        return expr switch
        {
            LeafFilterExpression leaf => new FilterExpressionDto
            {
                FieldName = leaf.FieldName,
                Operator = MapOperatorToString(leaf.Operator),
                Value = leaf.Value
            },
            CompositeFilterExpression composite => new FilterExpressionDto
            {
                LogicalOperator = composite.LogicalOperator == Domain.Enums.LogicalOperator.Or ? "OR" : "AND",
                Children = composite.Children.Select(MapToDto).ToList()
            },
            _ => throw new InvalidOperationException($"Unknown FilterExpression type: {expr.GetType().Name}")
        };
    }

    private static string MapOperatorToString(Domain.Enums.FilterOperator op) => op switch
    {
        Domain.Enums.FilterOperator.Equals => "=",
        Domain.Enums.FilterOperator.NotEquals => "!=",
        Domain.Enums.FilterOperator.GreaterThan => ">",
        Domain.Enums.FilterOperator.LessThan => "<",
        Domain.Enums.FilterOperator.GreaterThanOrEquals => ">=",
        Domain.Enums.FilterOperator.LessThanOrEquals => "<=",
        Domain.Enums.FilterOperator.Like => "LIKE",
        Domain.Enums.FilterOperator.In => "IN",
        Domain.Enums.FilterOperator.IsNull => "IS NULL",
        Domain.Enums.FilterOperator.IsNotNull => "IS NOT NULL",
        _ => throw new InvalidOperationException($"Unsupported FilterOperator: {op}")
    };
}

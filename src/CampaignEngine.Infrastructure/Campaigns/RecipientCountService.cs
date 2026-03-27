using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Domain.Filters;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Serialization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CampaignEngine.Infrastructure.Campaigns;

/// <summary>
/// Estimates the number of recipients that match a campaign's data source and filter.
/// Uses the same connector infrastructure as the data source preview service.
/// The count is an approximation — actual count may differ slightly at execution time.
/// </summary>
public sealed class RecipientCountService : IRecipientCountService
{
    private readonly CampaignEngineDbContext _dbContext;
    private readonly IConnectionStringEncryptor _encryptor;
    private readonly IDataSourceConnector _connector;
    private readonly IFilterExpressionValidator _validator;
    private readonly IAppLogger<RecipientCountService> _logger;

    public RecipientCountService(
        CampaignEngineDbContext dbContext,
        IConnectionStringEncryptor encryptor,
        IDataSourceConnector connector,
        IFilterExpressionValidator validator,
        IAppLogger<RecipientCountService> logger)
    {
        _dbContext = dbContext;
        _encryptor = encryptor;
        _connector = connector;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RecipientCountEstimateResult> EstimateAsync(
        EstimateRecipientsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            // ----------------------------------------------------------------
            // 1. Resolve data source
            // ----------------------------------------------------------------
            var dataSource = await _dbContext.DataSources
                .Include(d => d.Fields)
                .FirstOrDefaultAsync(d => d.Id == request.DataSourceId, cancellationToken);

            if (dataSource is null)
                return new RecipientCountEstimateResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Data source '{request.DataSourceId}' not found."
                };

            var fieldDtos = dataSource.Fields
                .Select(f => new FieldDefinitionDto
                {
                    FieldName = f.FieldName,
                    DisplayName = f.FieldName,
                    FieldType = f.DataType,
                    IsFilterable = f.IsFilterable
                })
                .ToList();

            // ----------------------------------------------------------------
            // 2. Parse and validate filter expression
            // ----------------------------------------------------------------
            List<FilterExpression> filters = new();
            if (!string.IsNullOrWhiteSpace(request.FilterExpression))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<FilterExpression>>(
                        request.FilterExpression,
                        new JsonSerializerOptions
                        {
                            Converters = { new FilterExpressionJsonConverter() }
                        });

                    if (parsed != null)
                        filters = parsed;
                }
                catch (JsonException ex)
                {
                    return new RecipientCountEstimateResult
                    {
                        IsSuccess = false,
                        ErrorMessage = $"Invalid filter expression JSON: {ex.Message}"
                    };
                }

                if (filters.Count > 0)
                {
                    var validationResult = _validator.Validate(filters, fieldDtos);
                    if (!validationResult.IsValid)
                        return new RecipientCountEstimateResult
                        {
                            IsSuccess = false,
                            ErrorMessage = $"Filter validation failed: {string.Join("; ", validationResult.Errors)}"
                        };
                }
            }

            // ----------------------------------------------------------------
            // 3. Execute query and count rows
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

            var filterDtos = filters.Count > 0
                ? filters.Select(MapToFilterDto).ToList()
                : null;

            var rows = await _connector.QueryAsync(definition, filterDtos, cancellationToken);

            _logger.LogInformation(
                "RecipientCountService: estimated {Count} recipients for DataSource {DataSourceId}",
                rows.Count, request.DataSourceId);

            return new RecipientCountEstimateResult
            {
                EstimatedCount = rows.Count,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RecipientCountService: estimation failed for DataSource {DataSourceId}",
                request.DataSourceId);

            return new RecipientCountEstimateResult
            {
                IsSuccess = false,
                ErrorMessage = $"Could not estimate recipient count: {ex.Message}"
            };
        }
    }

    // ----------------------------------------------------------------
    // Private: map domain FilterExpression to DTO
    // ----------------------------------------------------------------

    private static FilterExpressionDto MapToFilterDto(FilterExpression expr)
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
                Children = composite.Children.Select(MapToFilterDto).ToList()
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

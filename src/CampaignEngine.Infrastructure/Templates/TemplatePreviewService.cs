using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Templates;

/// <summary>
/// Infrastructure implementation of ITemplatePreviewService.
///
/// Workflow:
///   1. Load the template (sub-templates resolved recursively).
///   2. Fetch up to SampleRowCount rows from the specified data source (read-only).
///   3. Render the template with the selected sample row.
///   4. Apply channel post-processing (CSS inline for Email, PDF for Letter, strip for SMS).
///   5. Return the rendered output along with diagnostic info (missing placeholders).
///
/// Business rules enforced:
///   - Preview is read-only: no sends, no records written (BR-US010-1).
///   - Maximum 5 sample rows (BR-US010-2).
///   - Channel post-processing applied (BR-US010-3).
///   - Missing placeholder keys identified and returned (BR-US010-4).
/// </summary>
public sealed class TemplatePreviewService : ITemplatePreviewService
{
    private const int MaxSampleRows = 5;

    private readonly CampaignEngineDbContext _dbContext;
    private readonly IConnectionStringEncryptor _encryptor;
    private readonly IDataSourceConnectorRegistry _connectorRegistry;
    private readonly ISubTemplateResolverService _subTemplateResolver;
    private readonly ITemplateRenderer _renderer;
    private readonly IPlaceholderParserService _parserService;
    private readonly IChannelPostProcessorRegistry _postProcessorRegistry;
    private readonly IAppLogger<TemplatePreviewService> _logger;

    public TemplatePreviewService(
        CampaignEngineDbContext dbContext,
        IConnectionStringEncryptor encryptor,
        IDataSourceConnectorRegistry connectorRegistry,
        ISubTemplateResolverService subTemplateResolver,
        ITemplateRenderer renderer,
        IPlaceholderParserService parserService,
        IChannelPostProcessorRegistry postProcessorRegistry,
        IAppLogger<TemplatePreviewService> logger)
    {
        _dbContext = dbContext;
        _encryptor = encryptor;
        _connectorRegistry = connectorRegistry;
        _subTemplateResolver = subTemplateResolver;
        _renderer = renderer;
        _parserService = parserService;
        _postProcessorRegistry = postProcessorRegistry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TemplatePreviewResult> PreviewAsync(
        Guid templateId,
        TemplatePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        // ----------------------------------------------------------------
        // 1. Load the template
        // ----------------------------------------------------------------
        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == templateId && !t.IsDeleted, cancellationToken);

        if (template is null)
            throw new NotFoundException("Template", templateId);

        // ----------------------------------------------------------------
        // 2. Resolve sub-templates recursively
        // ----------------------------------------------------------------
        string resolvedHtml;
        try
        {
            resolvedHtml = await _subTemplateResolver.ResolveAsync(
                templateId, template.HtmlBody, cancellationToken);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(
                "Preview for Template {TemplateId}: sub-template resolution failed — {Error}",
                templateId, ex.Message);
            return FailResult(templateId, template.Channel.ToString(), ex.Message);
        }

        // ----------------------------------------------------------------
        // 3. Load the data source and fetch sample rows
        // ----------------------------------------------------------------
        var clampedRowCount = Math.Clamp(request.SampleRowCount, 1, MaxSampleRows);

        var dataSource = await _dbContext.DataSources
            .Include(d => d.Fields)
            .FirstOrDefaultAsync(d => d.Id == request.DataSourceId && d.IsActive, cancellationToken);

        if (dataSource is null)
            throw new NotFoundException("DataSource", request.DataSourceId);

        var plainCs = _encryptor.Decrypt(dataSource.EncryptedConnectionString);

        var definition = new DataSourceDefinitionDto
        {
            Id = dataSource.Id,
            Name = dataSource.Name,
            Type = dataSource.Type,
            ConnectionString = plainCs,
            Fields = dataSource.Fields
                .Select(f => new FieldDefinitionDto
                {
                    FieldName = f.FieldName,
                    DisplayName = f.FieldName,
                    FieldType = f.DataType,
                    IsFilterable = f.IsFilterable
                })
                .ToList()
        };

        IReadOnlyList<IDictionary<string, object?>> sampleRows;
        try
        {
            var connector = _connectorRegistry.GetConnector(dataSource.Type);
            var allRows = await connector.QueryAsync(definition, filters: null, cancellationToken);
            sampleRows = allRows.Take(clampedRowCount).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Preview for Template {TemplateId}: data source query failed — {Error}",
                templateId, ex.Message);
            return FailResult(templateId, template.Channel.ToString(),
                $"Failed to fetch sample data from data source: {ex.Message}");
        }

        if (sampleRows.Count == 0)
        {
            return FailResult(templateId, template.Channel.ToString(),
                "The selected data source returned no rows. Cannot render preview without sample data.");
        }

        // ----------------------------------------------------------------
        // 4. Select the row to use for rendering
        // ----------------------------------------------------------------
        var rowIndex = Math.Clamp(request.RowIndex, 0, sampleRows.Count - 1);
        var rowData = sampleRows[rowIndex];

        // ----------------------------------------------------------------
        // 5. Identify missing placeholders (BR-US010-4)
        // ----------------------------------------------------------------
        var extraction = _parserService.ExtractPlaceholders(resolvedHtml);
        var missingKeys = extraction.AllKeys
            .Where(k => !rowData.ContainsKey(k))
            .ToList();

        // Build the data context — missing keys are left as empty string so
        // the renderer does not fail; they appear as blank in output.
        var dataWithFallbacks = new Dictionary<string, object?>(
            rowData,
            StringComparer.OrdinalIgnoreCase);

        foreach (var missing in missingKeys)
        {
            if (!dataWithFallbacks.ContainsKey(missing))
                dataWithFallbacks[missing] = string.Empty;
        }

        // ----------------------------------------------------------------
        // 6. Render the template with sample data
        // ----------------------------------------------------------------
        string renderedHtml;
        try
        {
            renderedHtml = await _renderer.RenderAsync(resolvedHtml, dataWithFallbacks, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Preview for Template {TemplateId}: rendering failed — {Error}",
                templateId, ex.Message);
            return FailResult(templateId, template.Channel.ToString(),
                $"Template rendering failed: {ex.Message}");
        }

        // ----------------------------------------------------------------
        // 7. Apply channel post-processing (BR-US010-3)
        // ----------------------------------------------------------------
        PostProcessingResult postResult;
        try
        {
            if (_postProcessorRegistry.HasProcessor(template.Channel))
            {
                var processor = _postProcessorRegistry.GetProcessor(template.Channel);
                postResult = await processor.ProcessAsync(renderedHtml, context: null, cancellationToken);
            }
            else
            {
                // No post-processor registered — return rendered HTML as-is
                postResult = PostProcessingResult.Text(renderedHtml, "text/html");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Preview for Template {TemplateId}: post-processing failed — {Error}",
                templateId, ex.Message);
            return FailResult(templateId, template.Channel.ToString(),
                $"Channel post-processing failed: {ex.Message}");
        }

        _logger.LogInformation(
            "Preview rendered for Template {TemplateId}, Channel={Channel}, RowIndex={RowIndex}, " +
            "MissingPlaceholders={MissingCount}",
            templateId, template.Channel, rowIndex, missingKeys.Count);

        return new TemplatePreviewResult
        {
            TemplateId = templateId,
            Channel = template.Channel.ToString(),
            ContentType = postResult.ContentType,
            TextContent = postResult.IsBinary ? null : postResult.TextContent,
            Base64Content = postResult.IsBinary
                ? Convert.ToBase64String(postResult.BinaryContent!)
                : null,
            SampleRows = sampleRows,
            RowUsed = rowIndex,
            TotalSampleRows = sampleRows.Count,
            MissingPlaceholders = missingKeys.AsReadOnly(),
            IsSuccess = true
        };
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static TemplatePreviewResult FailResult(
        Guid templateId,
        string channel,
        string errorMessage) => new()
    {
        TemplateId = templateId,
        Channel = channel,
        ContentType = "text/plain",
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}

using System.Data;
using System.Data.SqlClient;
using System.Text;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using Dapper;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// SQL Server implementation of IDataSourceConnector using Dapper for lightweight data access.
///
/// Security principles:
///   - All query values are passed as Dapper DynamicParameters (parameterized SQL).
///   - No string concatenation of user-provided values into SQL text.
///   - Only SELECT statements are executed (read-only by design).
///   - Query timeout: 30 seconds per business rule.
///
/// Connection pooling:
///   - SqlConnection uses ADO.NET connection pooling by default.
///   - Pool parameters configured via SqlServerConnectorOptions (Max Pool Size, Connect Timeout).
///   - Connection strings must include "Pooling=True" (default) to enable pooling.
///   - Each call opens and closes a connection; the pool handles reuse transparently.
/// </summary>
public sealed class SqlServerConnector : IDataSourceConnector
{
    private readonly SqlServerConnectorOptions _options;
    private readonly IAppLogger<SqlServerConnector> _logger;

    /// <summary>Default query timeout in seconds (business rule: 30 seconds).</summary>
    private const int QueryTimeoutSeconds = 30;

    public SqlServerConnector(
        SqlServerConnectorOptions options,
        IAppLogger<SqlServerConnector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    // ----------------------------------------------------------------
    // IDataSourceConnector.QueryAsync
    // ----------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// Translates FilterExpressionDto nodes into a parameterized WHERE clause.
    /// Only SELECT is issued; no DDL or DML is possible.
    /// </remarks>
    public async Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(
        DataSourceDefinitionDto definition,
        IReadOnlyList<FilterExpressionDto>? filters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var connectionString = BuildConnectionString(definition.ConnectionString);

        var parameters = new DynamicParameters();
        var whereClause = BuildWhereClause(filters, parameters, definition.Fields);

        // Build SELECT: we select all columns from the declared schema, or * if schema is empty.
        // Column names are validated against the known schema — never injected raw from user input.
        var selectColumns = BuildSelectColumns(definition.Fields);

        // TableName comes from the DataSource definition managed by Admin-only UI.
        // It is not user-input at query time; validate it anyway to guard against injection.
        var tableName = ExtractTableName(definition.ConnectionString);
        ValidateIdentifier(tableName);

        var sql = new StringBuilder();
        sql.Append("SELECT ").Append(selectColumns);
        sql.Append(" FROM ").Append(QuoteIdentifier(tableName));

        if (!string.IsNullOrWhiteSpace(whereClause))
            sql.Append(" WHERE ").Append(whereClause);

        _logger.LogDebug(
            "SqlServerConnector: executing query on DataSource {DataSourceId}, Table={Table}, WhereClause={WhereClause}",
            definition.Id, tableName, whereClause ?? "(none)");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var commandDef = new CommandDefinition(
            commandText: sql.ToString(),
            parameters: parameters,
            commandTimeout: QueryTimeoutSeconds,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<dynamic>(commandDef);

        var result = rows
            .Select(row => (IDictionary<string, object?>)((IDictionary<string, object>)row)
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
            .ToList();

        _logger.LogInformation(
            "SqlServerConnector: query on DataSource {DataSourceId} returned {RowCount} rows",
            definition.Id, result.Count);

        return result;
    }

    // ----------------------------------------------------------------
    // IDataSourceConnector.GetSchemaAsync
    // ----------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// Queries INFORMATION_SCHEMA.COLUMNS for the first table found in the connection.
    /// The table name is extracted from the connection string "Initial Catalog" (database name)
    /// and a configured default table, or the first user table is auto-discovered.
    ///
    /// Column types are mapped to the canonical set used by FieldDefinitionDto.FieldType.
    /// </remarks>
    public async Task<IReadOnlyList<FieldDefinitionDto>> GetSchemaAsync(
        DataSourceDefinitionDto definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var connectionString = BuildConnectionString(definition.ConnectionString);
        var tableName = ExtractTableName(definition.ConnectionString);

        _logger.LogInformation(
            "SqlServerConnector: discovering schema for DataSource {DataSourceId}, Table={Table}",
            definition.Id, tableName);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        string sql;
        DynamicParameters parameters;

        if (!string.IsNullOrWhiteSpace(tableName))
        {
            // Discover schema for the specified table
            sql = @"
                SELECT
                    c.COLUMN_NAME       AS FieldName,
                    c.DATA_TYPE         AS DataType,
                    c.IS_NULLABLE       AS IsNullable,
                    c.ORDINAL_POSITION  AS OrdinalPosition
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @TableName
                ORDER BY c.ORDINAL_POSITION";

            parameters = new DynamicParameters();
            parameters.Add("@TableName", tableName, DbType.String);
        }
        else
        {
            // Auto-discover: return columns for all user tables
            sql = @"
                SELECT
                    c.COLUMN_NAME       AS FieldName,
                    c.DATA_TYPE         AS DataType,
                    c.IS_NULLABLE       AS IsNullable,
                    c.ORDINAL_POSITION  AS OrdinalPosition,
                    c.TABLE_NAME        AS TableName
                FROM INFORMATION_SCHEMA.COLUMNS c
                INNER JOIN INFORMATION_SCHEMA.TABLES t
                    ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION";

            parameters = new DynamicParameters();
        }

        var commandDef = new CommandDefinition(
            commandText: sql,
            parameters: parameters,
            commandTimeout: QueryTimeoutSeconds,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync(commandDef);

        var fields = rows.Select(row =>
        {
            var d = (IDictionary<string, object>)row;
            var fieldName = (string)d["FieldName"];
            var dataType = (string)d["DataType"];

            return new FieldDefinitionDto
            {
                FieldName = fieldName,
                DisplayName = fieldName,
                FieldType = MapSqlTypeToCanonical(dataType),
                IsFilterable = IsDefaultFilterable(dataType)
            };
        }).ToList();

        _logger.LogInformation(
            "SqlServerConnector: schema discovery for DataSource {DataSourceId} returned {FieldCount} fields",
            definition.Id, fields.Count);

        return fields;
    }

    // ----------------------------------------------------------------
    // WHERE clause builder — parameterized, no string concatenation of values
    // ----------------------------------------------------------------

    private static string? BuildWhereClause(
        IReadOnlyList<FilterExpressionDto>? filters,
        DynamicParameters parameters,
        IReadOnlyList<FieldDefinitionDto> knownFields,
        int depth = 0)
    {
        if (filters is null || filters.Count == 0)
            return null;

        // Build a set of known field names for validation
        var knownFieldNames = knownFields
            .Select(f => f.FieldName.ToUpperInvariant())
            .ToHashSet();

        var clauses = new List<string>();
        var paramIndex = 0;

        foreach (var filter in filters)
        {
            var clause = BuildFilterNode(filter, parameters, knownFieldNames, ref paramIndex, depth);
            if (!string.IsNullOrWhiteSpace(clause))
                clauses.Add(clause);
        }

        return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
    }

    private static string? BuildFilterNode(
        FilterExpressionDto node,
        DynamicParameters parameters,
        HashSet<string> knownFieldNames,
        ref int paramIndex,
        int depth)
    {
        if (depth > 5)
            throw new InvalidOperationException("Filter expression exceeds maximum nesting depth of 5.");

        // Composite node (AND / OR)
        if (node.Children is { Count: > 0 })
        {
            var logicalOp = node.LogicalOperator?.ToUpperInvariant() switch
            {
                "OR" => " OR ",
                _ => " AND "   // Default to AND for safety
            };

            var childClauses = new List<string>();
            foreach (var child in node.Children)
            {
                var childClause = BuildFilterNode(child, parameters, knownFieldNames, ref paramIndex, depth + 1);
                if (!string.IsNullOrWhiteSpace(childClause))
                    childClauses.Add(childClause);
            }

            return childClauses.Count == 0
                ? null
                : "(" + string.Join(logicalOp, childClauses) + ")";
        }

        // Leaf node — must have FieldName and Operator
        if (string.IsNullOrWhiteSpace(node.FieldName) || string.IsNullOrWhiteSpace(node.Operator))
            return null;

        // Validate field name against known schema — prevents column name injection
        var upperField = node.FieldName.ToUpperInvariant();
        if (knownFieldNames.Count > 0 && !knownFieldNames.Contains(upperField))
        {
            throw new InvalidOperationException(
                $"Filter field '{node.FieldName}' is not declared in the data source schema.");
        }

        // Validate and whitelist the SQL operator
        var sqlOperator = ValidateSqlOperator(node.Operator);
        var quotedColumn = QuoteIdentifier(node.FieldName);
        var paramName = $"@p{paramIndex++}";

        if (sqlOperator == "IN")
        {
            // IN operator: value must be a list
            var inValues = ExtractInValues(node.Value);
            var inParams = inValues.Select((v, i) =>
            {
                var pn = $"{paramName}_{i}";
                parameters.Add(pn, v);
                return pn;
            }).ToList();

            return inParams.Count == 0
                ? "1=0"  // Empty IN list → no rows match
                : $"{quotedColumn} IN ({string.Join(", ", inParams)})";
        }

        if (sqlOperator == "IS NULL")
            return $"{quotedColumn} IS NULL";

        if (sqlOperator == "IS NOT NULL")
            return $"{quotedColumn} IS NOT NULL";

        // All other operators: add as a typed parameter
        parameters.Add(paramName, node.Value);
        return $"{quotedColumn} {sqlOperator} {paramName}";
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private string BuildConnectionString(string baseConnectionString)
    {
        // Append pool and timeout settings from options to the base connection string
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            ConnectTimeout = _options.ConnectTimeoutSeconds,
            Pooling = true,
            MinPoolSize = _options.MinPoolSize,
            MaxPoolSize = _options.MaxPoolSize,
            ApplicationName = "CampaignEngine"
        };
        return builder.ConnectionString;
    }

    private static string BuildSelectColumns(IReadOnlyList<FieldDefinitionDto> fields)
    {
        if (fields.Count == 0)
            return "*";

        return string.Join(", ", fields.Select(f => QuoteIdentifier(f.FieldName)));
    }

    /// <summary>
    /// Extracts the table name from a connection string.
    /// Convention: append "Table=TableName;" to the connection string for explicit targeting.
    /// Falls back to empty string (triggers full schema discovery).
    /// </summary>
    private static string ExtractTableName(string connectionString)
    {
        try
        {
            // Look for a custom "Table" keyword in the connection string
            var builder = new SqlConnectionStringBuilder();
            // SqlConnectionStringBuilder ignores unknown keywords — parse manually
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part[..eq].Trim().ToLowerInvariant();
                var val = part[(eq + 1)..].Trim();
                if (key is "table" or "tablename" or "table name")
                    return val;
            }
        }
        catch
        {
            // Ignore parsing errors — fall back to discovery
        }
        return string.Empty;
    }

    /// <summary>
    /// Wraps an identifier in square brackets to prevent SQL injection
    /// for structural identifiers (table names, column names).
    /// </summary>
    private static string QuoteIdentifier(string identifier)
    {
        // Strip any existing brackets, then re-wrap
        return "[" + identifier.Replace("]", "]]") + "]";
    }

    /// <summary>
    /// Validates that an identifier contains only safe characters.
    /// Rejects identifiers with semicolons, quotes, or other dangerous characters.
    /// </summary>
    private static void ValidateIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        // Allow alphanumeric, underscore, space, hyphen, dot (for schema.table)
        foreach (var c in identifier)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != ' ' && c != '-' && c != '.')
                throw new InvalidOperationException(
                    $"Identifier '{identifier}' contains invalid characters.");
        }
    }

    /// <summary>
    /// Validates the operator against a whitelist. Returns the canonical SQL operator string.
    /// </summary>
    private static string ValidateSqlOperator(string op)
    {
        return op.Trim().ToUpperInvariant() switch
        {
            "=" or "==" => "=",
            "!=" or "<>" => "<>",
            ">" => ">",
            "<" => "<",
            ">=" => ">=",
            "<=" => "<=",
            "LIKE" => "LIKE",
            "NOT LIKE" => "NOT LIKE",
            "IN" => "IN",
            "IS NULL" => "IS NULL",
            "IS NOT NULL" => "IS NOT NULL",
            _ => throw new InvalidOperationException(
                $"SQL operator '{op}' is not supported. " +
                "Allowed: =, !=, >, <, >=, <=, LIKE, NOT LIKE, IN, IS NULL, IS NOT NULL.")
        };
    }

    private static IReadOnlyList<object?> ExtractInValues(object? value)
    {
        if (value is null)
            return [];

        if (value is System.Collections.IEnumerable enumerable and not string)
            return enumerable.Cast<object?>().ToList();

        // Single value wrapped as list
        return [value];
    }

    /// <summary>
    /// Maps SQL Server data types from INFORMATION_SCHEMA to the canonical type strings
    /// used by FieldDefinitionDto.FieldType.
    /// </summary>
    private static string MapSqlTypeToCanonical(string sqlType)
    {
        return sqlType.ToLowerInvariant() switch
        {
            "nvarchar" or "varchar" or "char" or "nchar" or "text" or "ntext" => "nvarchar",
            "int" => "int",
            "bigint" => "bigint",
            "smallint" or "tinyint" => "int",
            "datetime" or "datetime2" or "smalldatetime" => "datetime",
            "date" => "date",
            "time" => "time",
            "bit" => "bit",
            "decimal" or "numeric" or "money" or "smallmoney" => "decimal",
            "float" or "real" => "float",
            "uniqueidentifier" => "uniqueidentifier",
            "binary" or "varbinary" or "image" => "varbinary",
            _ => sqlType.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Returns true for types that are commonly filterable (scalar, indexable types).
    /// Binary and text blob types default to non-filterable.
    /// </summary>
    private static bool IsDefaultFilterable(string sqlType)
    {
        return sqlType.ToLowerInvariant() switch
        {
            "binary" or "varbinary" or "image" or "text" or "ntext" => false,
            _ => true
        };
    }
}

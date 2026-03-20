using System.Data.SqlClient;
using System.Diagnostics;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.DataSources;

/// <summary>
/// Tests data source connectivity by attempting to open and immediately close a connection.
/// Supports SQL Server (Phase 1) and REST API (Phase 1) per business rules.
/// No data is read or written during the test.
/// </summary>
public sealed class ConnectionTestService : IConnectionTestService
{
    private readonly IAppLogger<ConnectionTestService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ConnectionTestService(
        IAppLogger<ConnectionTestService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<ConnectionTestResult> TestAsync(
        DataSourceType type,
        string plainTextConnectionString,
        CancellationToken cancellationToken = default)
    {
        return type switch
        {
            DataSourceType.SqlServer => await TestSqlServerAsync(plainTextConnectionString, cancellationToken),
            DataSourceType.RestApi   => await TestRestApiAsync(plainTextConnectionString, cancellationToken),
            _ => ConnectionTestResult.Fail($"Unsupported data source type: {type}", 0)
        };
    }

    // ----------------------------------------------------------------
    // SQL Server: open connection, execute trivial query, close.
    // ----------------------------------------------------------------
    private async Task<ConnectionTestResult> TestSqlServerAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 10;
            await cmd.ExecuteScalarAsync(cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "SQL Server connection test succeeded in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            return ConnectionTestResult.Ok(
                $"Connected successfully to SQL Server in {sw.ElapsedMilliseconds}ms.",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(
                "SQL Server connection test failed in {ElapsedMs}ms: {Error}",
                sw.ElapsedMilliseconds, ex.Message);

            return ConnectionTestResult.Fail(
                $"Connection failed: {ex.Message}",
                sw.ElapsedMilliseconds);
        }
    }

    // ----------------------------------------------------------------
    // REST API: treat the connection string as a base URL and do a HEAD
    // (or GET) request to validate reachability.
    // ----------------------------------------------------------------
    private async Task<ConnectionTestResult> TestRestApiAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                return ConnectionTestResult.Fail(
                    "Connection string is not a valid absolute URL.", 0);
            }

            using var client = _httpClientFactory.CreateClient("ConnectionTest");
            client.Timeout = TimeSpan.FromSeconds(15);

            using var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, uri),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            sw.Stop();
            var statusCode = (int)response.StatusCode;

            // Any HTTP response (even 401/403) means the endpoint is reachable.
            if (statusCode < 500)
            {
                _logger.LogInformation(
                    "REST API connection test succeeded. StatusCode={StatusCode}, ElapsedMs={ElapsedMs}",
                    statusCode, sw.ElapsedMilliseconds);

                return ConnectionTestResult.Ok(
                    $"Endpoint reachable (HTTP {statusCode}) in {sw.ElapsedMilliseconds}ms.",
                    sw.ElapsedMilliseconds);
            }

            _logger.LogWarning(
                "REST API connection test returned server error. StatusCode={StatusCode}",
                statusCode);

            return ConnectionTestResult.Fail(
                $"Endpoint returned HTTP {statusCode}.",
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(
                "REST API connection test failed: {Error}", ex.Message);

            return ConnectionTestResult.Fail(
                $"Connection failed: {ex.Message}",
                sw.ElapsedMilliseconds);
        }
    }
}

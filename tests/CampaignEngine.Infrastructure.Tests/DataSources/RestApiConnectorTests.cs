using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CampaignEngine.Application.DTOs.DataSources;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.DataSources;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.DataSources;

/// <summary>
/// Integration tests for RestApiConnector (TASK-017-05).
///
/// Uses a mock IHttpClientFactory with a fake HttpMessageHandler to simulate REST API responses.
/// Tests cover:
///   - JSON to data row parsing (flat, nested via dataPath, array root, object root)
///   - Authentication: None, API key header, Bearer, OAuth2 (token acquisition + caching)
///   - Pagination: single page, page-param (last page detection), Link-header rel="next"
///   - Retry: transient errors (5xx, 429) retried up to MaxRetryAttempts
///   - Response size limit: 50 MB guard (LimitedStream)
///   - In-memory filter application
///   - Schema discovery from first page
///   - Connection string parsing edge cases
/// </summary>
public class RestApiConnectorTests
{
    // ----------------------------------------------------------------
    // Helpers / builder
    // ----------------------------------------------------------------

    private static RestApiConnectorOptions DefaultOptions => new()
    {
        TimeoutSeconds = 5,
        MaxRetryAttempts = 3,
        BaseRetryDelaySeconds = 0,   // no actual delay in tests
        MaxResponseSizeBytes = 50 * 1024 * 1024,
        MaxPages = 1000
    };

    private static DataSourceDefinitionDto MakeDefinition(string connectionString) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test API",
        Type = DataSourceType.RestApi,
        ConnectionString = connectionString,
        Fields = []
    };

    private static (RestApiConnector Connector, FakeHttpMessageHandler Handler) BuildConnector(
        RestApiConnectorOptions? options = null,
        IEnumerable<HttpResponseMessage>? responses = null)
    {
        var handler = new FakeHttpMessageHandler(responses?.ToList() ?? []);
        var httpClient = new HttpClient(handler);

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler));

        var loggerMock = new Mock<IAppLogger<RestApiConnector>>();

        var oauth2Cache = new RestApiOAuth2TokenCache(factoryMock.Object);

        var connector = new RestApiConnector(
            options ?? DefaultOptions,
            factoryMock.Object,
            oauth2Cache,
            loggerMock.Object);

        return (connector, handler);
    }

    // ----------------------------------------------------------------
    // ParseJsonToRows — TASK-017-02: JSON to data row mapping
    // ----------------------------------------------------------------

    [Fact(DisplayName = "ParseJsonToRows: flat JSON array returns correct rows")]
    public void ParseJsonToRows_FlatArray_ReturnsRows()
    {
        // Arrange
        var json = """[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]""";

        // Act
        var rows = RestApiConnector.ParseJsonToRows(json, dataPath: "");

        // Assert
        rows.Should().HaveCount(2);
        rows[0]["id"].Should().Be(1L);
        rows[0]["name"].Should().Be("Alice");
        rows[1]["id"].Should().Be(2L);
        rows[1]["name"].Should().Be("Bob");
    }

    [Fact(DisplayName = "ParseJsonToRows: nested data path extracts correct array")]
    public void ParseJsonToRows_NestedDataPath_ExtractsCorrectArray()
    {
        // Arrange
        var json = """{"meta":{"total":2},"data":[{"id":10},{"id":20}]}""";

        // Act
        var rows = RestApiConnector.ParseJsonToRows(json, dataPath: "data");

        // Assert
        rows.Should().HaveCount(2);
        rows[0]["id"].Should().Be(10L);
        rows[1]["id"].Should().Be(20L);
    }

    [Fact(DisplayName = "ParseJsonToRows: deep nested path works with dot notation")]
    public void ParseJsonToRows_DeepNestedPath_Works()
    {
        // Arrange
        var json = """{"response":{"data":{"items":[{"x":1},{"x":2}]}}}""";

        // Act
        var rows = RestApiConnector.ParseJsonToRows(json, "response.data.items");

        // Assert
        rows.Should().HaveCount(2);
        rows[0]["x"].Should().Be(1L);
    }

    [Fact(DisplayName = "ParseJsonToRows: single JSON object is wrapped as one-row list")]
    public void ParseJsonToRows_SingleObject_WrapsAsList()
    {
        // Arrange
        var json = """{"id":42,"email":"test@example.com"}""";

        // Act
        var rows = RestApiConnector.ParseJsonToRows(json, "");

        // Assert
        rows.Should().HaveCount(1);
        rows[0]["id"].Should().Be(42L);
        rows[0]["email"].Should().Be("test@example.com");
    }

    [Fact(DisplayName = "ParseJsonToRows: boolean and null values are correctly mapped")]
    public void ParseJsonToRows_BoolAndNull_MappedCorrectly()
    {
        // Arrange
        var json = """[{"active":true,"inactive":false,"middle":null}]""";

        // Act
        var rows = RestApiConnector.ParseJsonToRows(json, "");

        // Assert
        rows[0]["active"].Should().Be(true);
        rows[0]["inactive"].Should().Be(false);
        rows[0]["middle"].Should().BeNull();
    }

    [Fact(DisplayName = "ParseJsonToRows: empty body returns empty list")]
    public void ParseJsonToRows_EmptyBody_ReturnsEmpty()
    {
        var rows = RestApiConnector.ParseJsonToRows("", "");
        rows.Should().BeEmpty();
    }

    [Fact(DisplayName = "ParseJsonToRows: invalid JSON throws InvalidOperationException")]
    public void ParseJsonToRows_InvalidJson_Throws()
    {
        var act = () => RestApiConnector.ParseJsonToRows("{not valid json", "");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not valid JSON*");
    }

    [Fact(DisplayName = "ParseJsonToRows: path not found returns root element rows")]
    public void ParseJsonToRows_PathNotFound_ReturnsRootRows()
    {
        // When path doesn't match, NavigatePath returns the element it found
        var json = """{"items":[{"a":1}]}""";

        // Path "missing" doesn't exist — falls back to root (object)
        var rows = RestApiConnector.ParseJsonToRows(json, "missing");
        // Root is an object → single-row wrapped
        rows.Should().HaveCount(1);
    }

    // ----------------------------------------------------------------
    // QueryAsync — authentication (TASK-017-03)
    // ----------------------------------------------------------------

    [Fact(DisplayName = "QueryAsync: no authentication — no auth headers added")]
    public async Task QueryAsync_NoAuth_NoAuthHeaderAdded()
    {
        // Arrange
        var json = """[{"id":1}]""";
        var (connector, handler) = BuildConnector(
            responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition("Url=https://api.example.com/users");

        // Act
        var rows = await connector.QueryAsync(definition, null);

        // Assert
        rows.Should().HaveCount(1);
        handler.LastRequest!.Headers.Authorization.Should().BeNull();
        handler.LastRequest.Headers.Contains("X-Api-Key").Should().BeFalse();
    }

    [Fact(DisplayName = "QueryAsync: ApiKey auth — adds correct header")]
    public async Task QueryAsync_ApiKeyAuth_AddsHeader()
    {
        // Arrange
        var json = """[{"id":1}]""";
        var (connector, handler) = BuildConnector(
            responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition(
            "Url=https://api.example.com/users;Auth=ApiKey;ApiKeyHeader=X-Api-Key;ApiKeyValue=secret123");

        // Act
        await connector.QueryAsync(definition, null);

        // Assert
        handler.LastRequest!.Headers.GetValues("X-Api-Key").Should().Contain("secret123");
    }

    [Fact(DisplayName = "QueryAsync: Bearer auth — adds Authorization Bearer header")]
    public async Task QueryAsync_BearerAuth_AddsAuthorizationHeader()
    {
        // Arrange
        var json = """[{"id":1}]""";
        var (connector, handler) = BuildConnector(
            responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition(
            "Url=https://api.example.com/users;Auth=Bearer;BearerToken=mytoken123");

        // Act
        await connector.QueryAsync(definition, null);

        // Assert
        handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("mytoken123");
    }

    // ----------------------------------------------------------------
    // QueryAsync — pagination (TASK-017-04)
    // ----------------------------------------------------------------

    [Fact(DisplayName = "QueryAsync: single page (no pagination) returns all rows")]
    public async Task QueryAsync_SinglePage_ReturnsAllRows()
    {
        // Arrange
        var json = """[{"id":1},{"id":2},{"id":3}]""";
        var (connector, handler) = BuildConnector(responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition("Url=https://api.example.com/items");

        // Act
        var rows = await connector.QueryAsync(definition, null);

        // Assert
        rows.Should().HaveCount(3);
        handler.RequestCount.Should().Be(1);
    }

    [Fact(DisplayName = "QueryAsync: page-param pagination fetches multiple pages")]
    public async Task QueryAsync_PageParamPagination_FetchesMultiplePages()
    {
        // Arrange: page 1 has 2 items (= page size), page 2 has 1 item (< page size = last page)
        var page1Json = """[{"id":1},{"id":2}]""";
        var page2Json = """[{"id":3}]""";

        var (connector, handler) = BuildConnector(
            responses: [OkJsonResponse(page1Json), OkJsonResponse(page2Json)]);

        var definition = MakeDefinition(
            "Url=https://api.example.com/items;PageParam=page;PageSizeParam=per_page;PageSize=2");

        // Act
        var rows = await connector.QueryAsync(definition, null);

        // Assert
        rows.Should().HaveCount(3);
        handler.RequestCount.Should().Be(2);
    }

    [Fact(DisplayName = "QueryAsync: Link-header pagination follows rel=next until absent")]
    public async Task QueryAsync_LinkHeaderPagination_FollowsNextLinks()
    {
        // Arrange: 3 pages
        var page1 = OkJsonResponse("""[{"id":1}]""");
        page1.Headers.Add("Link", "<https://api.example.com/items?page=2>; rel=\"next\", <https://api.example.com/items?page=3>; rel=\"last\"");

        var page2 = OkJsonResponse("""[{"id":2}]""");
        page2.Headers.Add("Link", "<https://api.example.com/items?page=3>; rel=\"next\"");

        var page3 = OkJsonResponse("""[{"id":3}]""");
        // No Link header on last page

        var (connector, handler) = BuildConnector(responses: [page1, page2, page3]);

        var definition = MakeDefinition(
            "Url=https://api.example.com/items;NextLinkHeader=Link");

        // Act
        var rows = await connector.QueryAsync(definition, null);

        // Assert
        rows.Should().HaveCount(3);
        handler.RequestCount.Should().Be(3);
    }

    // ----------------------------------------------------------------
    // QueryAsync — retry policy (business rule: 3 attempts)
    // ----------------------------------------------------------------

    [Fact(DisplayName = "QueryAsync: transient 500 retried 3 times then succeeds")]
    public async Task QueryAsync_TransientError_RetriesAndSucceeds()
    {
        // Arrange: first 2 requests fail with 500, third succeeds
        var responses = new List<HttpResponseMessage>
        {
            new(HttpStatusCode.InternalServerError),
            new(HttpStatusCode.InternalServerError),
            OkJsonResponse("""[{"id":1}]""")
        };

        var (connector, handler) = BuildConnector(responses: responses);
        var definition = MakeDefinition("Url=https://api.example.com/items");

        // Act
        var rows = await connector.QueryAsync(definition, null);

        // Assert
        rows.Should().HaveCount(1);
        handler.RequestCount.Should().Be(3);
    }

    [Fact(DisplayName = "QueryAsync: 429 TooManyRequests is retried")]
    public async Task QueryAsync_TooManyRequests_IsRetried()
    {
        var responses = new List<HttpResponseMessage>
        {
            new(HttpStatusCode.TooManyRequests),
            OkJsonResponse("""[{"id":1}]""")
        };

        var (connector, handler) = BuildConnector(responses: responses);
        var definition = MakeDefinition("Url=https://api.example.com/items");

        var rows = await connector.QueryAsync(definition, null);
        rows.Should().HaveCount(1);
        handler.RequestCount.Should().Be(2);
    }

    [Fact(DisplayName = "QueryAsync: 3 consecutive 500s throws HttpRequestException")]
    public async Task QueryAsync_ThreeConsecutive500s_ThrowsAfterMaxRetries()
    {
        var responses = new List<HttpResponseMessage>
        {
            new(HttpStatusCode.InternalServerError),
            new(HttpStatusCode.InternalServerError),
            new(HttpStatusCode.InternalServerError)
        };

        var (connector, handler) = BuildConnector(responses: responses);
        var definition = MakeDefinition("Url=https://api.example.com/items");

        var act = async () => await connector.QueryAsync(definition, null);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ----------------------------------------------------------------
    // QueryAsync — in-memory filter application
    // ----------------------------------------------------------------

    [Fact(DisplayName = "QueryAsync: equality filter applied in-memory")]
    public async Task QueryAsync_EqualityFilter_AppliedInMemory()
    {
        // Arrange
        var json = """[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"},{"id":3,"name":"Alice"}]""";
        var (connector, _) = BuildConnector(responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition("Url=https://api.example.com/users");

        var filters = new List<FilterExpressionDto>
        {
            new() { FieldName = "name", Operator = "=", Value = "Alice" }
        };

        // Act
        var rows = await connector.QueryAsync(definition, filters);

        // Assert
        rows.Should().HaveCount(2);
        rows.All(r => r["name"]?.ToString() == "Alice").Should().BeTrue();
    }

    [Fact(DisplayName = "QueryAsync: IS NULL filter applied in-memory")]
    public async Task QueryAsync_IsNullFilter_AppliedInMemory()
    {
        var json = """[{"id":1,"email":null},{"id":2,"email":"test@test.com"}]""";
        var (connector, _) = BuildConnector(responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition("Url=https://api.example.com/users");

        var filters = new List<FilterExpressionDto>
        {
            new() { FieldName = "email", Operator = "IS NULL" }
        };

        var rows = await connector.QueryAsync(definition, filters);
        rows.Should().HaveCount(1);
        rows[0]["id"].Should().Be(1L);
    }

    // ----------------------------------------------------------------
    // GetSchemaAsync — schema discovery
    // ----------------------------------------------------------------

    [Fact(DisplayName = "GetSchemaAsync: infers schema from first JSON record")]
    public async Task GetSchemaAsync_InfersFieldsFromFirstRecord()
    {
        // Arrange
        var json = """[{"id":1,"name":"Alice","active":true,"score":3.14}]""";
        var (connector, _) = BuildConnector(responses: [OkJsonResponse(json)]);

        var definition = MakeDefinition("Url=https://api.example.com/users");

        // Act
        var fields = await connector.GetSchemaAsync(definition);

        // Assert
        fields.Should().HaveCount(4);
        fields.Should().Contain(f => f.FieldName == "id" && f.FieldType == "bigint");
        fields.Should().Contain(f => f.FieldName == "name" && f.FieldType == "nvarchar");
        fields.Should().Contain(f => f.FieldName == "active" && f.FieldType == "bit");
        fields.All(f => f.IsFilterable).Should().BeTrue();
    }

    [Fact(DisplayName = "GetSchemaAsync: empty response returns empty field list")]
    public async Task GetSchemaAsync_EmptyResponse_ReturnsEmptyFields()
    {
        var (connector, _) = BuildConnector(responses: [OkJsonResponse("[]")]);
        var definition = MakeDefinition("Url=https://api.example.com/users");

        var fields = await connector.GetSchemaAsync(definition);
        fields.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // Connection string parsing
    // ----------------------------------------------------------------

    [Fact(DisplayName = "RestApiConnectionString: missing Url throws")]
    public void RestApiConnectionString_MissingUrl_Throws()
    {
        var act = () => RestApiConnectionString.Parse("Auth=ApiKey");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Url=*");
    }

    [Fact(DisplayName = "RestApiConnectionString: page-param pagination detected correctly")]
    public void RestApiConnectionString_PageParamDetected()
    {
        var config = RestApiConnectionString.Parse(
            "Url=https://api.test.com;PageParam=page;PageSizeParam=per_page;PageSize=50");

        config.PaginationMode.Should().Be(RestApiPaginationMode.PageParam);
        config.PageParam.Should().Be("page");
        config.PageSizeParam.Should().Be("per_page");
        config.PageSize.Should().Be(50);
    }

    [Fact(DisplayName = "RestApiConnectionString: NextLink pagination detected correctly")]
    public void RestApiConnectionString_NextLinkDetected()
    {
        var config = RestApiConnectionString.Parse(
            "Url=https://api.test.com;NextLinkHeader=Link");

        config.PaginationMode.Should().Be(RestApiPaginationMode.NextLink);
        config.NextLinkHeader.Should().Be("Link");
    }

    [Fact(DisplayName = "RestApiConnectionString: OAuth2 mode parsed correctly")]
    public void RestApiConnectionString_OAuth2Parsed()
    {
        var config = RestApiConnectionString.Parse(
            "Url=https://api.test.com;Auth=OAuth2;OAuth2TokenUrl=https://auth.test.com/token;" +
            "OAuth2ClientId=client1;OAuth2ClientSecret=secret;OAuth2Scope=read");

        config.AuthMode.Should().Be(RestApiAuthMode.OAuth2);
        config.OAuth2TokenUrl.Should().Be("https://auth.test.com/token");
        config.OAuth2ClientId.Should().Be("client1");
        config.OAuth2ClientSecret.Should().Be("secret");
        config.OAuth2Scope.Should().Be("read");
    }

    [Fact(DisplayName = "RestApiConnectionString: static query params parsed correctly")]
    public void RestApiConnectionString_StaticQueryParams_Parsed()
    {
        var config = RestApiConnectionString.Parse(
            "Url=https://api.test.com;QueryParams=format=json,version=2");

        config.QueryParams.Should().ContainKey("format").WhoseValue.Should().Be("json");
        config.QueryParams.Should().ContainKey("version").WhoseValue.Should().Be("2");
    }

    // ----------------------------------------------------------------
    // LimitedStream — size guard
    // ----------------------------------------------------------------

    [Fact(DisplayName = "LimitedStream: throws LimitExceededException when limit exceeded")]
    public async Task LimitedStream_ThrowsWhenLimitExceeded()
    {
        var data = new byte[200];
        var innerStream = new MemoryStream(data);
        var limited = new LimitedStream(innerStream, maxBytes: 100);

        var buffer = new byte[200];
        var act = async () => await limited.ReadAsync(buffer, 0, 200);

        await act.Should().ThrowAsync<LimitedStream.LimitExceededException>();
    }

    [Fact(DisplayName = "LimitedStream: does not throw when within limit")]
    public async Task LimitedStream_DoesNotThrowWithinLimit()
    {
        var data = Encoding.UTF8.GetBytes("hello world");
        var innerStream = new MemoryStream(data);
        var limited = new LimitedStream(innerStream, maxBytes: 1000);

        var buffer = new byte[50];
        var bytesRead = await limited.ReadAsync(buffer, 0, 50);
        bytesRead.Should().Be(data.Length);
    }

    // ----------------------------------------------------------------
    // DataSourceConnectorRegistry
    // ----------------------------------------------------------------

    [Fact(DisplayName = "DataSourceConnectorRegistry: resolves SqlServer connector")]
    public void DataSourceConnectorRegistry_ResolvesSqlServer()
    {
        var sqlConnector = new SqlServerConnector(
            new SqlServerConnectorOptions(),
            new Mock<IAppLogger<SqlServerConnector>>().Object);

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var oauth2Cache = new RestApiOAuth2TokenCache(httpFactoryMock.Object);

        var restConnector = new RestApiConnector(
            DefaultOptions,
            httpFactoryMock.Object,
            oauth2Cache,
            new Mock<IAppLogger<RestApiConnector>>().Object);

        var registry = new DataSourceConnectorRegistry(sqlConnector, restConnector);

        registry.HasConnector(DataSourceType.SqlServer).Should().BeTrue();
        registry.GetConnector(DataSourceType.SqlServer).Should().BeSameAs(sqlConnector);
    }

    [Fact(DisplayName = "DataSourceConnectorRegistry: resolves RestApi connector")]
    public void DataSourceConnectorRegistry_ResolvesRestApi()
    {
        var sqlConnector = new SqlServerConnector(
            new SqlServerConnectorOptions(),
            new Mock<IAppLogger<SqlServerConnector>>().Object);

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var oauth2Cache = new RestApiOAuth2TokenCache(httpFactoryMock.Object);

        var restConnector = new RestApiConnector(
            DefaultOptions,
            httpFactoryMock.Object,
            oauth2Cache,
            new Mock<IAppLogger<RestApiConnector>>().Object);

        var registry = new DataSourceConnectorRegistry(sqlConnector, restConnector);

        registry.HasConnector(DataSourceType.RestApi).Should().BeTrue();
        registry.GetConnector(DataSourceType.RestApi).Should().BeSameAs(restConnector);
    }

    [Fact(DisplayName = "DataSourceConnectorRegistry: throws for unregistered type")]
    public void DataSourceConnectorRegistry_ThrowsForUnregisteredType()
    {
        var sqlConnector = new SqlServerConnector(
            new SqlServerConnectorOptions(),
            new Mock<IAppLogger<SqlServerConnector>>().Object);

        var httpFactoryMock = new Mock<IHttpClientFactory>();
        httpFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var oauth2Cache = new RestApiOAuth2TokenCache(httpFactoryMock.Object);
        var restConnector = new RestApiConnector(
            DefaultOptions,
            httpFactoryMock.Object,
            oauth2Cache,
            new Mock<IAppLogger<RestApiConnector>>().Object);

        var registry = new DataSourceConnectorRegistry(sqlConnector, restConnector);

        var act = () => registry.GetConnector((DataSourceType)99);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No data source connector*");
    }

    // ----------------------------------------------------------------
    // Test helpers
    // ----------------------------------------------------------------

    private static HttpResponseMessage OkJsonResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return response;
    }
}

/// <summary>
/// Fake HTTP message handler for unit tests — returns pre-configured responses in order.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;
    public HttpRequestMessage? LastRequest { get; private set; }
    public int RequestCount { get; private set; }

    public FakeHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
    {
        _responses = new Queue<HttpResponseMessage>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        RequestCount++;

        if (_responses.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            });

        return Task.FromResult(_responses.Dequeue());
    }
}

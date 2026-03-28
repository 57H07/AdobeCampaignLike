using CampaignEngine.Application.DTOs.ApiKeys;
using Swashbuckle.AspNetCore.Filters;

namespace CampaignEngine.Web.OpenApi.Examples;

/// <summary>
/// Example for the POST /api/apikeys request body.
/// </summary>
public class CreateApiKeyRequestExample : IExamplesProvider<CreateApiKeyRequest>
{
    public CreateApiKeyRequest GetExamples() => new()
    {
        Name = "OrderService integration key",
        Description = "Used by the OrderService microservice to send order confirmation emails.",
        RateLimitPerMinute = 500,
        ExpiresInDays = 365
    };
}

/// <summary>
/// Example for the POST /api/apikeys response (created key).
/// </summary>
public class ApiKeyCreatedResponseExample : IExamplesProvider<ApiKeyCreatedResponse>
{
    public ApiKeyCreatedResponse GetExamples() => new()
    {
        Key = new ApiKeyDto
        {
            Id = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            Name = "OrderService integration key",
            KeyPrefix = "ce_a1b2c3",
            IsActive = true,
            IsExpired = false,
            ExpiresAt = new DateTime(2027, 3, 28, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "admin@campaignengine.local",
            RateLimitPerMinute = 500,
            Description = "Used by the OrderService microservice to send order confirmation emails.",
            CreatedAt = new DateTime(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc)
        },
        PlaintextKey = "ce_a1b2c3d4e5f67890abcdef1234567890abcdef12"
    };
}

using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Domain.Enums;
using Swashbuckle.AspNetCore.Filters;

namespace CampaignEngine.Web.OpenApi.Examples;

/// <summary>
/// Example value for the POST /api/send request body.
/// </summary>
public class SendRequestExample : IExamplesProvider<SendRequest>
{
    public SendRequest GetExamples() => new()
    {
        TemplateId = new Guid("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
        Channel = ChannelType.Email,
        Data = new Dictionary<string, object?>
        {
            ["firstName"] = "Marie",
            ["lastName"] = "Dupont",
            ["orderId"] = "ORD-20240328-001",
            ["amount"] = 149.99
        },
        Recipient = new SendRecipient
        {
            Email = "marie.dupont@example.com",
            DisplayName = "Marie Dupont",
            ExternalRef = "CRM-12345"
        }
    };
}

/// <summary>
/// Example value for a successful POST /api/send response.
/// </summary>
public class SendResponseExample : IExamplesProvider<SendResponse>
{
    public SendResponse GetExamples() => new()
    {
        TrackingId = new Guid("7c9e6679-7425-40de-944b-e07fc1f90ae7"),
        Success = true,
        Status = SendStatus.Sent,
        Channel = ChannelType.Email,
        SentAt = new DateTime(2026, 3, 28, 10, 30, 0, DateTimeKind.Utc),
        MessageId = "<20260328103000.7c9e@smtp.campaignengine.local>"
    };
}

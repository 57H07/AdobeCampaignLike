# Channel Dispatcher Extension Guide

This guide explains how to add a new communication channel (e.g., WhatsApp, Push, Fax) to
the CampaignEngine dispatch system without modifying any existing core code.

## Architecture Overview

The dispatch system uses the **Strategy Pattern** backed by **Dependency Injection**:

```
IChannelDispatcherRegistry
        |
        | resolves by ChannelType
        v
IChannelDispatcher  <-- one implementation per channel
```

The `ChannelDispatcherRegistry` receives all registered `IChannelDispatcher` implementations
via constructor injection (`IEnumerable<IChannelDispatcher>`) and indexes them by `ChannelType`.
There is no `switch/case` — adding a new channel only requires:

1. Extending the `ChannelType` enum
2. Creating a dispatcher class
3. Registering it in DI

---

## Step-by-Step: Adding a New Channel

### Step 1 — Add the enum value

In `src/CampaignEngine.Domain/Enums/ChannelType.cs`:

```csharp
public enum ChannelType
{
    Email  = 1,
    Letter = 2,
    Sms    = 3,
    WhatsApp = 4,   // <-- add here
}
```

### Step 2 — Create the dispatcher class

Create a new file in `src/CampaignEngine.Infrastructure/Dispatch/`:

```csharp
using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Infrastructure.Dispatch;

public sealed class WhatsAppDispatcher : IChannelDispatcher
{
    public ChannelType Channel => ChannelType.WhatsApp;

    public async Task<DispatchResult> SendAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Call external WhatsApp API here
            var messageId = await SendViaWhatsAppApiAsync(request, cancellationToken);
            return DispatchResult.Ok(messageId);
        }
        catch (HttpRequestException ex) when (IsTransient(ex))
        {
            // Transient failures should be returned as IsTransientFailure = true
            // so the retry mechanism can retry them
            return DispatchResult.Fail(ex.Message, isTransient: true);
        }
        catch (Exception ex)
        {
            // Permanent failures (invalid number, account suspended, etc.)
            return DispatchResult.Fail(ex.Message, isTransient: false);
        }
    }

    private static bool IsTransient(HttpRequestException ex)
        => ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.ServiceUnavailable;
}
```

### Step 3 — Create a channel configuration class (optional but recommended)

In `src/CampaignEngine.Infrastructure/Configuration/ChannelConfigurationBase.cs`, add:

```csharp
public class WhatsAppChannelConfiguration : ChannelConfigurationBase
{
    public const string SectionName = "Channels:WhatsApp";

    public override ChannelType Channel => ChannelType.WhatsApp;

    public string ApiEndpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int ThrottlePerSecond { get; set; } = 20;
}
```

Bind it in `appsettings.json`:

```json
{
  "Channels": {
    "WhatsApp": {
      "IsEnabled": true,
      "ApiEndpoint": "https://api.whatsapp.example.com/v1",
      "ApiKey": "<YOUR_API_KEY>",
      "ThrottlePerSecond": 20
    }
  }
}
```

### Step 4 — Register in DI

In `src/CampaignEngine.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
// WhatsApp channel
services.Configure<WhatsAppChannelConfiguration>(
    configuration.GetSection(WhatsAppChannelConfiguration.SectionName));
services.AddScoped<IChannelDispatcher, WhatsAppDispatcher>();
```

That is all that is required. The `ChannelDispatcherRegistry` will automatically
include the new dispatcher without any changes to core code.

---

## DispatchRequest and DispatchResult

### DispatchRequest

| Property        | Type                  | Notes                                      |
|-----------------|-----------------------|--------------------------------------------|
| Channel         | ChannelType           | Must match the dispatcher's Channel value  |
| Content         | string                | Rendered message body (HTML or plain text) |
| Recipient       | RecipientInfo         | Email, PhoneNumber, DisplayName            |
| CcAddresses     | List\<string\>        | Email only                                 |
| Attachments     | List\<AttachmentInfo\>| Email and Letter only                      |
| CampaignId      | Guid?                 | For correlation tracking                   |
| CampaignStepId  | Guid?                 | For correlation tracking                   |

### DispatchResult

| Property         | Type    | Notes                                                     |
|------------------|---------|-----------------------------------------------------------|
| Success          | bool    | True if message was accepted by the provider              |
| MessageId        | string? | Provider-assigned message identifier                      |
| ErrorDetail      | string? | Human-readable error description on failure               |
| IsTransientFailure | bool  | True = retry eligible; False = permanent, do not retry    |
| SentAt           | DateTime | UTC timestamp of the dispatch attempt                    |

Use `DispatchResult.Ok(messageId)` and `DispatchResult.Fail(detail, isTransient)` factory methods.

---

## Error Handling Contract

| Failure type | IsTransientFailure | Behavior               |
|--------------|--------------------|------------------------|
| Network timeout / 429 / 503 | true | Retry with backoff (max 3 attempts) |
| Invalid recipient / bad content | false | Log and mark as permanently failed |
| Unhandled exception | — | Propagates as exception; Hangfire will retry the chunk |

**Important:** Do not throw exceptions for expected failures. Return `DispatchResult.Fail(...)`.
Only throw exceptions for truly unexpected conditions (programming errors, infrastructure loss).
Thrown exceptions trigger Hangfire chunk-level retry rather than per-message retry.

---

## Testing Your Dispatcher

Use `MockChannelDispatcher` from the test utilities to substitute your dispatcher in unit tests:

```csharp
// In tests: verify the registry resolves your dispatcher
var myDispatcher = MockChannelDispatcher.Success(ChannelType.WhatsApp);
var registry = new ChannelDispatcherRegistry([myDispatcher]);

var dispatcher = registry.GetDispatcher(ChannelType.WhatsApp);
var result = await dispatcher.SendAsync(new DispatchRequest { ... });

result.Success.Should().BeTrue();
myDispatcher.CallCount.Should().Be(1);
```

For integration tests, register a real `WhatsAppDispatcher` pointing at a sandbox/test API endpoint.

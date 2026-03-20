# SMS Provider Setup Guide

**User Story:** US-020 — SMS Dispatcher
**TASK-020-07**

---

## Overview

CampaignEngine supports two SMS provider modes:

| Mode     | Description |
|----------|-------------|
| `Generic` | HTTP POST to a REST endpoint with API-key authentication. Works with Nexmo/Vonage, MessageBird, AWS SNS, and most modern SMS APIs. |
| `Twilio`  | HTTP POST with form-encoded body and HTTP Basic Auth (AccountSid:AuthToken). Full Twilio compatibility including delivery receipts. |

---

## Configuration

All SMS settings live under the `"Sms"` key in `appsettings.json` (or environment-specific override files).

```json
{
  "Sms": {
    "Provider": "Generic",
    "ProviderApiUrl": "https://rest.nexmo.com/sms/json",
    "ApiKey": "<your-api-key-or-auth-token>",
    "AccountSid": "",
    "ApiKeyHeaderName": "X-API-Key",
    "DefaultSenderId": "+12025550100",
    "MaxMessageLength": 160,
    "TimeoutSeconds": 30,
    "ValidatePhoneNumbers": true,
    "RequestDeliveryReceipts": false,
    "DeliveryStatusCallbackUrl": "",
    "IsEnabled": true
  }
}
```

### Field Reference

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `Provider` | string | `"Generic"` | Provider mode: `"Generic"` or `"Twilio"`. |
| `ProviderApiUrl` | string | — | Full URL of the SMS send endpoint. |
| `ApiKey` | string | — | API key (Generic) or Auth Token (Twilio). Store in secrets, not in source. |
| `AccountSid` | string | — | Twilio Account SID (ignored for Generic). |
| `ApiKeyHeaderName` | string | `"X-API-Key"` | Header name for API key (Generic mode only). |
| `DefaultSenderId` | string | — | Source phone number or alphanumeric sender name. |
| `MaxMessageLength` | int | `160` | Characters per SMS. Longer messages are truncated preserving whole words. |
| `TimeoutSeconds` | int | `30` | HTTP request timeout per send. |
| `ValidatePhoneNumbers` | bool | `true` | Enforce E.164 format on recipient numbers. Invalid numbers are skipped (BR-4). |
| `RequestDeliveryReceipts` | bool | `false` | Whether to attach a status callback URL to each send request. |
| `DeliveryStatusCallbackUrl` | string | — | Public URL for delivery status callbacks (see section below). |
| `IsEnabled` | bool | `true` | Set to `false` to disable the SMS channel globally. |

---

## Provider-Specific Setup

### Generic Provider (Nexmo/Vonage, MessageBird, etc.)

```json
{
  "Sms": {
    "Provider": "Generic",
    "ProviderApiUrl": "https://rest.nexmo.com/sms/json",
    "ApiKey": "<api_key>",
    "ApiKeyHeaderName": "X-API-Key",
    "DefaultSenderId": "CampaignEngine"
  }
}
```

The generic provider sends a JSON POST:

```json
{
  "to": "+12025551234",
  "from": "CampaignEngine",
  "message": "Hello! ...",
  "statusCallback": "https://yourapp.com/api/sms/delivery-status/generic"
}
```

The response is expected to contain a `messageId`, `message_id`, or `id` field for tracking.

**Common providers and their endpoint URLs:**

| Provider | ProviderApiUrl | ApiKeyHeaderName |
|----------|---------------|-----------------|
| Vonage (Nexmo) | `https://rest.nexmo.com/sms/json` | `X-API-Key` |
| MessageBird | `https://rest.messagebird.com/messages` | `Authorization` (set value to `"AccessKey <your-key>"`) |
| AWS SNS | Use AWS SDK instead of this generic client | — |

### Twilio

```json
{
  "Sms": {
    "Provider": "Twilio",
    "ProviderApiUrl": "https://api.twilio.com/2010-04-01/Accounts/{YOUR_ACCOUNT_SID}/Messages.json",
    "AccountSid": "AC...",
    "ApiKey": "<auth_token>",
    "DefaultSenderId": "+12025550100"
  }
}
```

Replace `{YOUR_ACCOUNT_SID}` in `ProviderApiUrl` with your actual Account SID.

**Authentication:** HTTP Basic Auth (username = AccountSid, password = AuthToken).
**Body format:** `application/x-www-form-urlencoded` with fields `From`, `To`, `Body`.
**Message ID:** Returned as `sid` in the Twilio response.

---

## Phone Number Format (E.164)

All recipient phone numbers must be in **E.164 format**: `+[country code][subscriber number]`.

| Valid | Invalid |
|-------|---------|
| `+12025551234` (US) | `12025551234` (missing `+`) |
| `+441234567890` (UK) | `+1` (too short) |
| `+33123456789` (France) | `+(123) 456-7890` (contains formatting) |

**Business rule BR-4:** Invalid phone numbers are logged as permanent failures but do NOT abort the campaign for other recipients. They appear in the send log with status `Failed` and the error message `"Phone number '...' is not in E.164 format"`.

To disable validation (e.g. for providers that handle normalization themselves):
```json
"ValidatePhoneNumbers": false
```

---

## Message Truncation

**Business rule BR-2:** Messages are truncated to `MaxMessageLength` characters (default 160) before sending.

- Truncation preserves **whole words** where possible.
- If no word boundary exists within the limit, the message is hard-truncated at `MaxMessageLength`.
- A warning is logged when truncation occurs.

To allow longer messages (providers supporting multi-part SMS):
```json
"MaxMessageLength": 1600
```

---

## Delivery Status Tracking

Delivery status callbacks allow CampaignEngine to record whether messages were **delivered** or **failed** after dispatch.

### Enabling Callbacks

1. Set `RequestDeliveryReceipts: true` in configuration.
2. Set `DeliveryStatusCallbackUrl` to a publicly reachable URL.
3. Register the endpoint at your SMS provider dashboard as the status webhook.

```json
{
  "Sms": {
    "RequestDeliveryReceipts": true,
    "DeliveryStatusCallbackUrl": "https://yourapp.example.com/api/sms/delivery-status/twilio"
  }
}
```

### Callback Endpoints

| Provider | Endpoint | Method | Content-Type |
|----------|----------|--------|--------------|
| Twilio | `/api/sms/delivery-status/twilio` | POST | `application/x-www-form-urlencoded` |
| Generic | `/api/sms/delivery-status/generic` | POST | `application/json` |

**Twilio callback fields:**

| Field | Description |
|-------|-------------|
| `MessageSid` | Twilio message identifier |
| `MessageStatus` | `delivered`, `failed`, `undelivered`, `sent`, `queued` |
| `To` | Destination phone number |
| `ErrorCode` | Twilio error code (on failure) |
| `ErrorMessage` | Human-readable error |

**Generic callback JSON:**

```json
{
  "messageId": "abc123",
  "status": "delivered",
  "to": "+12025551234",
  "error": null
}
```

Accepted status values: `delivered`, `failed`, `pending`, `sent`.

### How Status Is Tracked

1. On successful dispatch, the provider message ID is stored as `ExternalMessageId` in the send log.
2. When a callback arrives, the send log entry is looked up by `ExternalMessageId`.
3. If found, `Status` is updated to `Sent` (delivered) or `Failed` (undelivered/failed).
4. If no matching entry is found (e.g. message sent outside CampaignEngine), a warning is logged and the callback is silently ignored.

> **Note:** The callback endpoint always returns HTTP 200, regardless of processing errors, to prevent provider retry floods.

---

## Security Considerations

1. **Store API keys securely.** Use [ASP.NET Core User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for development and environment variables / Azure Key Vault for production. Never commit API keys to source control.

2. **Validate Twilio signatures.** For production Twilio deployments, validate the `X-Twilio-Signature` header on callback requests. This requires adding Twilio's `ValidateTwilioRequest` middleware (not included by default — add in a future hardening task).

3. **Restrict callback endpoint access.** Configure a firewall rule to only accept requests to `/api/sms/delivery-status/*` from known provider IP ranges.

4. **HTTPS required.** All provider API calls and callback URLs must use HTTPS. CampaignEngine enforces HTTPS in production via ASP.NET Core HTTPS redirection middleware.

---

## Rate Limiting

**Business rule BR-3:** Rate limiting per provider contract.

Default throttle for SMS: **10 messages/second** (configured in `CampaignEngine:Dispatch:Sms:ThrottlePerSecond`).

This is enforced at the campaign batch processing level (US-022). Individual sends via the Generic Send API (`POST /api/send`) are not throttled but count toward the provider's own rate limits.

Configure per provider contract:
```json
{
  "CampaignEngine": {
    "Dispatch": {
      "Sms": {
        "ThrottlePerSecond": 10
      }
    }
  }
}
```

---

## Testing with Provider Sandbox

Most providers offer sandbox/test environments:

| Provider | Sandbox | Notes |
|----------|---------|-------|
| Twilio | Use test credentials (AccountSid starts with `AC...test`) | Test magic numbers available in Twilio console |
| Vonage | Test API keys available | Sandbox mode returns success without sending |
| MessageBird | Use `test` as API key prefix | Returns mock responses |

To test without a real provider, set `IsEnabled: false` or use an in-memory test double by overriding `SmsProviderClient` in your integration test setup.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| All SMS sends fail with "SMS channel is disabled" | `IsEnabled: false` | Set `Sms:IsEnabled: true` |
| "Phone number '...' is not in E.164 format" | Recipient data lacks country prefix | Ensure data source returns E.164 numbers, or disable `ValidatePhoneNumbers` |
| HTTP 401 errors | Wrong API key or AccountSid | Verify credentials in configuration |
| HTTP 429 errors | Rate limit exceeded | Reduce `ThrottlePerSecond` or upgrade provider plan |
| HTTP 500 errors | Provider outage | Transient — retried automatically per retry policy (US-035) |
| Delivery callbacks not received | Callback URL unreachable | Ensure `DeliveryStatusCallbackUrl` is publicly accessible and not behind a firewall |

---

## Related Documentation

- [Channel Dispatcher Extension Guide](channel-dispatcher-extension-guide.md)
- [Send Log Schema](send-log-schema.md)
- [Single Send API](single-send-api.md)
- [SMTP Configuration Guide](smtp-configuration-guide.md)

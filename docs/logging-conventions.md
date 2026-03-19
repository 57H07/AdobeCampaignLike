# Logging Conventions — CampaignEngine

**Version:** 1.0
**Date:** 2026-03-19
**Audience:** Developers, Infrastructure team (Sophie)

---

## Overview

CampaignEngine uses **Serilog** for structured logging throughout the application.
All log entries are machine-readable JSON enriched with contextual properties,
enabling efficient querying in log analysis tools (Seq, Azure Monitor, Elastic, etc.).

---

## Architecture

```
Service code
    │
    ▼
IAppLogger<T>          ← Application layer interface (inject this)
    │
    ▼
AppLogger<T>           ← Infrastructure implementation (wraps ILogger<T>)
    │
    ▼
ILogger<T> (MEL)       ← Microsoft.Extensions.Logging bridge
    │
    ▼
Serilog                ← Structured sink pipeline
    ├── Console sink   ← Development output
    ├── File sink      ← Rolling daily files (all levels)
    └── File sink      ← Rolling daily files (Error+ only)
```

---

## Log Levels

| Level | When to Use | Examples |
|-------|-------------|---------|
| `Debug` | Diagnostic details useful during development | Template rendering parameters, SQL queries |
| `Information` | Normal operational events | Request received, campaign started, send completed |
| `Warning` | Unexpected but recoverable situations | Missing dynamic attachment, slow operation (>1s), 4xx responses |
| `Error` | Failures requiring investigation | SMTP connection refused, unhandled exceptions, 5xx responses |
| `Critical` | Application cannot continue | Host startup failure, database unreachable |

---

## Correlation IDs

Every HTTP request gets a unique **Correlation ID** to trace it end-to-end.

### How it works

1. `CorrelationIdMiddleware` runs at the start of the request pipeline.
2. It reads the `X-Correlation-Id` request header if present (caller-supplied).
3. If absent, it generates a new `Guid` formatted as `D` (hyphenated).
4. The ID is pushed into Serilog's `LogContext` — all log entries in that request automatically carry `{CorrelationId}`.
5. The ID is returned in the `X-Correlation-Id` response header for client-side tracing.

### Passing correlation IDs

**Incoming API calls:** include `X-Correlation-Id: <your-trace-id>` in the request.

**Internal service calls:** propagate the correlation ID via `IHttpContextAccessor`:
```csharp
var correlationId = _httpContextAccessor.HttpContext?.Items["X-Correlation-Id"] as string;
```

---

## Using IAppLogger

Inject `IAppLogger<T>` where `T` is the class that uses logging:

```csharp
public class CampaignService
{
    private readonly IAppLogger<CampaignService> _logger;

    public CampaignService(IAppLogger<CampaignService> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Campaign campaign)
    {
        // GOOD: structured message template with named properties
        _logger.LogInformation(
            "Starting campaign execution {CampaignId} with {RecipientCount} recipients",
            campaign.Id, campaign.RecipientCount);

        // BAD: string interpolation breaks structured logging
        // _logger.LogInformation($"Starting campaign {campaign.Id}");
    }
}
```

---

## Structured Message Templates

Always use **named placeholders** in message templates, not string interpolation.

```csharp
// CORRECT — Serilog captures CampaignId and Channel as separate indexed properties
_logger.LogInformation(
    "Dispatching {CampaignId} via {Channel} channel",
    campaignId, channel);

// WRONG — loses structured properties, cannot filter by CampaignId
_logger.LogInformation($"Dispatching {campaignId} via {channel} channel");
```

---

## Performance Logging

Use `PerformanceLogger` or `IAppLogger<T>.LogPerformance` for timing critical operations:

```csharp
// Option 1: using block (automatic timing)
using (new PerformanceLogger(_logger, "RenderTemplate",
    ("TemplateId", templateId),
    ("Channel", channel)))
{
    result = await _renderer.RenderAsync(template, data);
}

// Option 2: manual timing via IAppLogger
var sw = Stopwatch.StartNew();
result = await _renderer.RenderAsync(template, data);
sw.Stop();
_appLogger.LogPerformance("RenderTemplate", sw.ElapsedMilliseconds,
    ("TemplateId", templateId));
```

Operations exceeding **1000ms** are automatically elevated to `Warning` level.

---

## Dispatch Logging

Use the dedicated `LogDispatch` method for all send operations.
This ensures consistent structured properties for SEND_LOG correlation:

```csharp
// Success
_appLogger.LogDispatch(campaignId, templateId, "Email", "Sent");

// Failure
_appLogger.LogDispatch(campaignId, templateId, "Email", "Failed",
    errorMessage: "SMTP connection refused");
```

These entries are the **source of truth** for dispatch activity (as required by SEND_LOG spec).

---

## PII Masking Rules

**PII must never appear in plain text in log messages.**

| Data Type | Action | Helper |
|-----------|--------|--------|
| Email addresses | Mask local part | `PiiMasker.MaskEmail(email)` |
| Phone numbers | Show last 4 digits only | `PiiMasker.MaskPhone(phone)` |
| Full names | Omit from logs entirely | — |
| Postal addresses | Omit from logs entirely | — |
| Free text with emails | Mask all emails | `PiiMasker.MaskEmailsInText(text)` |

```csharp
// CORRECT
_logger.LogInformation("Processing recipient {MaskedEmail}", PiiMasker.MaskEmail(email));

// WRONG
_logger.LogInformation("Processing recipient {Email}", email);
```

---

## Log Sinks Configuration

### Default (all environments)

| Sink | Levels | Rolling | Retention |
|------|--------|---------|-----------|
| Console | All | — | — |
| File `logs/campaignengine-{date}.log` | All | Daily | 30 files |
| File `logs/campaignengine-errors-{date}.log` | Error+ | Daily | 90 files |

### Production SQL sink (optional)

Enable by setting `CampaignEngine:Logging:SqlErrorSinkEnabled: true` in production `appsettings`.
The SQL sink writes `Error+` entries to the `ApplicationLogs` table.

### Configuration override per environment

```json
// appsettings.Development.json — more verbose
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": { "CampaignEngine": "Debug" }
    }
  }
}

// appsettings.Production.json — quieter
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Warning",
      "Override": { "CampaignEngine": "Information" }
    }
  }
}
```

---

## Enriched Properties

Every log entry automatically includes:

| Property | Source | Value |
|----------|--------|-------|
| `CorrelationId` | `CorrelationIdMiddleware` | Per-request GUID |
| `SourceContext` | Serilog | Class name (`CampaignEngine.Application.Services.CampaignService`) |
| `MachineName` | `Serilog.Enrichers.Environment` | Hostname |
| `EnvironmentName` | `Serilog.Enrichers.Environment` | `Development` / `Production` |
| `Application` | `appsettings.json` | `CampaignEngine` |
| `RequestMethod` | Serilog request logging | `GET`, `POST`, etc. |
| `RequestPath` | Serilog request logging | `/api/campaigns` |
| `StatusCode` | Serilog request logging | `200`, `404`, etc. |
| `Elapsed` | Serilog request logging | ms |

---

## EventId Convention

| EventId | Name | Used for |
|---------|------|---------|
| `1000` | `PerformanceMetric` | All timed operations via `PerformanceLogger` |

Use EventIds to enable reliable filtering in monitoring tools:
```
{EventId: {Id: 1000, Name: "PerformanceMetric"}}
```

---

## Business Rules Summary

1. **All API calls** are logged with request/response details via `UseSerilogRequestLogging`.
2. **All send operations** are logged with `IAppLogger.LogDispatch` correlated to campaign and template.
3. **All errors** are logged with full stack traces at `Error` or `Critical` level.
4. **PII is masked** before any field appears in a log message — use `PiiMasker` helpers.
5. **Correlation IDs** are propagated from the `X-Correlation-Id` header through the full request lifecycle.
6. **Performance warnings** fire automatically when any logged operation exceeds 1000ms.

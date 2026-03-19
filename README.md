# CampaignEngine

Multi-channel campaign engine for Email, SMS, and PDF Letter communications.

## Architecture

The solution follows a strict **layered architecture** to enforce separation of concerns and prevent circular dependencies.

```
┌───────────────────────────────────────────────────────────────────────┐
│                         CampaignEngine.Web                            │
│   (ASP.NET Core: Razor Pages + Web API, Middleware, DI bootstrap)     │
├──────────────────────────┬────────────────────────────────────────────┤
│   CampaignEngine.        │  CampaignEngine.Infrastructure              │
│   Application            │  (EF Core, dispatchers, connectors,        │
│   (Use cases, interfaces,│   logging, config options)                  │
│   DTOs, mappings)        │                                            │
├──────────────────────────┴────────────────────────────────────────────┤
│                         CampaignEngine.Domain                          │
│     (Entities, Enums, Domain Exceptions — no external dependencies)    │
└───────────────────────────────────────────────────────────────────────┘
```

### Dependency Rules

| Layer          | May depend on      | Must NOT depend on          |
|----------------|--------------------|-----------------------------|
| Domain         | (nothing)          | Application, Infrastructure, Web |
| Application    | Domain             | Infrastructure, Web         |
| Infrastructure | Domain, Application| Web                         |
| Web            | Application, Infrastructure | (external — entry point) |

### Project Structure

```
/src
  /CampaignEngine.Domain
    /Common          - AuditableEntity, SoftDeletableEntity base classes
    /Entities        - Domain entities (Template, Campaign, SendLog, etc.)
    /Enums           - ChannelType, CampaignStatus, TemplateStatus, etc.
    /Exceptions      - DomainException, NotFoundException, ValidationException

  /CampaignEngine.Application
    /Interfaces      - ITemplateRenderer, IChannelDispatcher, IDataSourceConnector, IAppLogger
    /Services        - Use case services
    /DTOs            - Data transfer objects (Dispatch, DataSources, etc.)
    /Mappings        - Mapster mapping configurations
    /DependencyInjection - AddApplication() extension method

  /CampaignEngine.Infrastructure
    /Configuration   - Strongly-typed options (CampaignEngineOptions, SmtpOptions)
    /Data            - ApplicationDbContext, EF Core configurations, Migrations
    /Repositories    - Repository implementations
    /Logging         - AppLogger<T> wrapping Microsoft.Extensions.Logging
    /DependencyInjection - AddInfrastructure(config) extension method

  /CampaignEngine.Web
    /Middleware      - GlobalExceptionMiddleware, correlation ID middleware
    /Pages           - Razor Pages
    /Controllers     - API controllers
    /ViewModels      - View models
    Program.cs       - Entry point, DI bootstrap

/tests
  /CampaignEngine.Domain.Tests
  /CampaignEngine.Application.Tests
  /CampaignEngine.Infrastructure.Tests
```

## Tech Stack

| Concern              | Technology                          |
|----------------------|-------------------------------------|
| Runtime              | .NET 8 (LTS)                        |
| Web framework        | ASP.NET Core (Razor Pages + Web API)|
| ORM                  | Entity Framework Core 8             |
| Database             | SQL Server                          |
| Background jobs      | Hangfire Community                  |
| Template engine      | Scriban                             |
| Object mapping       | Mapster                             |
| CSS inlining         | PreMailer.Net                       |
| Testing              | xUnit + Moq + FluentAssertions      |
| Hosting              | IIS on Windows Server               |

## Key Design Patterns

### Strategy Pattern — Channel Dispatchers

Each communication channel implements `IChannelDispatcher`. The DI container holds all implementations and the dispatcher registry resolves by `ChannelType` at runtime — no `switch/case`.

```csharp
// In Infrastructure DI registration
services.AddScoped<IChannelDispatcher, SmtpEmailDispatcher>();
services.AddScoped<IChannelDispatcher, SmsApiDispatcher>();
services.AddScoped<IChannelDispatcher, PdfLetterDispatcher>();
```

### Strategy Pattern — Data Source Connectors

Data sources (SQL Server, REST API) implement `IDataSourceConnector`. Operators declare data sources; the engine fetches data schema-agnostically.

### Chunk Coordinator Pattern — Batch Processing

Hangfire Community does not support batch primitives. For large campaigns (100K+ recipients), the `CampaignBatchService` splits recipients into chunks of 500, enqueues one Hangfire job per chunk, and uses an atomic SQL counter to detect completion. Target: 100K recipients in < 60 minutes with 8 workers.

### Template Snapshots

When a campaign transitions to `Scheduled`, the template content (body, sub-templates, placeholder manifest) is frozen in `TemplateSnapshot`. All sends use the snapshot — live template edits do not affect running campaigns.

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server (local or remote)
- Visual Studio 2022 or VS Code with C# extension

### Run Locally

```bash
# Restore and build
dotnet build CampaignEngine.sln

# Update database (once EF Core migrations are added)
dotnet ef database update --project src/CampaignEngine.Infrastructure --startup-project src/CampaignEngine.Web

# Run the web application
dotnet run --project src/CampaignEngine.Web
```

### Run Tests

```bash
dotnet test CampaignEngine.sln
```

### Configuration

Copy `appsettings.Development.json` and configure:
- `ConnectionStrings:DefaultConnection` — SQL Server connection string
- `Smtp` — SMTP server settings
- `Sms` — SMS provider API settings

## DI Bootstrap Pattern

```csharp
// Program.cs
builder.Services.AddApplication();           // Application layer services
builder.Services.AddInfrastructure(config);  // Infrastructure: DB, dispatchers, logging
```

Each layer exposes an extension method that encapsulates its own registrations. The Web layer only calls these two methods — no knowledge of internal implementations.

## Acceptance Criteria Status

- [x] Solution structure follows Domain / Application / Infrastructure / Web layers
- [x] All projects properly reference each other with correct dependencies
- [x] Microsoft.Extensions.DependencyInjection configured as DI container
- [x] Cross-cutting concerns (logging, error handling) abstracted behind interfaces
- [x] Unit test projects created with xUnit + Moq + FluentAssertions

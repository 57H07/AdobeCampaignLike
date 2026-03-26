# CampaignEngine

Marketing campaign engine (Adobe Campaign-like) built on .NET 8, ASP.NET Core, EF Core + SQL Server.

## Commands

```bash
dotnet build CampaignEngine.sln
dotnet test CampaignEngine.sln
dotnet run --project src/CampaignEngine.Web

# Migrations
dotnet ef migrations add <Name> --project src/CampaignEngine.Infrastructure --startup-project src/CampaignEngine.Infrastructure --output-dir Persistence/Migrations
dotnet ef database update --project src/CampaignEngine.Infrastructure --startup-project src/CampaignEngine.Infrastructure
```

## Architecture (strict layer deps)

```
Web → Application → Domain
Web → Infrastructure → Application → Domain
```

- **Domain** — entities, enums, exceptions, filter models. No external deps.
- **Application** — interfaces, DTOs, services, Mapster mappings. Depends only on Domain.
- **Infrastructure** — EF Core, dispatchers, connectors, Hangfire jobs. Implements Application interfaces.
- **Web** — Razor Pages + REST controllers, middleware, DI bootstrap.

DI registration: each layer has an extension method (`AddApplication()`, `AddInfrastructure()`) called from `Program.cs`.

## Key Patterns

**Strategy (runtime dispatch)**
- Channel dispatchers: `IChannelDispatcher` → `SmtpEmailDispatcher`, `SmsApiDispatcher`, `PdfLetterDispatcher`
- Data connectors: `IDataSourceConnector` → `SqlServerConnector`, `RestApiConnector`
- Resolved via DI keyed services by `ChannelType` / connector type.

**Chunk Coordinator** (batch campaigns)
- `RecipientChunkingService` splits recipients into 500-per-chunk.
- One Hangfire job per chunk; `ChunkCoordinatorService` uses atomic SQL counters for completion detection.
- No Hangfire Pro required.

**Template Snapshots**
- When a campaign moves to `Scheduled`, template content is frozen in the campaign record.
- Live template edits never affect running campaigns.

## Conventions

- Base entities: `AuditableEntity` (created/modified), `SoftDeletableEntity` (+ IsDeleted).
- Exceptions: `DomainException`, `NotFoundException`, `ValidationException` — caught by `GlobalExceptionMiddleware`.
- Logging: inject `IAppLogger<T>` (not `ILogger<T>`) — wraps Serilog with correlation ID.
- Auth: cookie-based Identity for Razor Pages; `X-Api-Key` header for REST API endpoints.
- Mapping: Mapster (not AutoMapper). Config in `Application/Mappings/`.

## Testing

```
tests/CampaignEngine.Domain.Tests        — entity/enum unit tests
tests/CampaignEngine.Application.Tests   — service tests (Moq + FluentAssertions)
tests/CampaignEngine.Infrastructure.Tests — EF Core + dispatcher integration tests
```

Follow existing test file structure. Use `FluentAssertions` for assertions, `Moq` for mocks.

## Backlog & Docs

- User stories: `.userstories/BACKLOG.md` (37 stories, machine-readable: `backlog_parsed.json`)
- Completed stories: `.userstories/completed/`
- Technical docs: `docs/` (25 files — channel config, template syntax, batch architecture, API auth, etc.)

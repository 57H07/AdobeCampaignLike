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

- **Domain** — entities (with business behaviour), enums, exceptions, filter models. No external deps.
- **Application** — interfaces (incl. `IUnitOfWork`, `IRepository<T>`, `IIdentityService`), DTOs, Mapster mappings. Depends only on Domain.
- **Infrastructure** — EF Core, repositories, dispatchers, connectors, Hangfire jobs. Implements Application interfaces.
- **Web** — Razor Pages + REST controllers, middleware, DI bootstrap.

DI registration: each layer has an extension method (`AddApplication()`, `AddInfrastructure()`) called from `Program.cs`.

## Key Patterns

**Repository + Unit of Work**
- Repository interfaces in `Application/Interfaces/Repositories/` (`IRepository<T>`, `ICampaignRepository`, etc.).
- Implementations in `Infrastructure/Persistence/Repositories/`.
- `IUnitOfWork` (with `CommitAsync` / `BeginTransactionAsync` / `RollbackTransactionAsync` / `IAsyncDisposable`) wraps `DbContext.SaveChangesAsync` and explicit transactions.
- Services inject repositories + `IUnitOfWork` — never `CampaignEngineDbContext` directly.

**Domain Behaviour (not anemic)**
- `Campaign.Schedule()` — enforces Draft status, ScheduledAt set, ≥5 min ahead; transitions to Scheduled.
- `Campaign.AddStep()` — enforces 10-step maximum.
- `Template.Publish()` / `Template.Archive()` — state-machine guards.
- Throw `DomainException` for invariant violations.

**Strategy (runtime dispatch)**
- Channel dispatchers: `IChannelDispatcher` → `EmailDispatcher`, `SmsDispatcher`, `LetterDispatcher`
- Data connectors: `IDataSourceConnector` → `SqlServerConnector`
- Resolved via DI keyed services by `ChannelType` / connector type.

**Identity abstraction**
- `IIdentityService` in Application wraps ASP.NET Core Identity managers.
- Web layer only injects `IIdentityService` — never `UserManager<ApplicationUser>` or `SignInManager<ApplicationUser>`.

**Chunk Coordinator** (batch campaigns)
- `RecipientChunkingService` splits recipients into 500-per-chunk.
- One Hangfire job per chunk; `ChunkCoordinatorService` uses atomic SQL counters for completion detection.
- No Hangfire Pro required.

**Template Snapshots**
- When a campaign moves to `Scheduled`, template content is frozen in the campaign record.
- Live template edits never affect running campaigns.

## Conventions

- Base entities: `AuditableEntity` (created/modified), `SoftDeletableEntity` (+ IsDeleted).
- Exceptions: `DomainException` (entity invariants, 422), `ValidationException` (input/state, 400), `NotFoundException` (404) — caught by `GlobalExceptionMiddleware`.
- Logging: inject `IAppLogger<T>` (not `ILogger<T>`) — wraps Serilog with correlation ID.
- Auth: cookie-based Identity for Razor Pages (via `IIdentityService`); `X-Api-Key` header for REST API endpoints.
- Mapping: Mapster (not AutoMapper). Config in `Application/Mappings/` (one file per domain aggregate + `MappingConfig.cs`). Use `.Adapt<TDto>()` in services.
- JSON serialization of filter ASTs: use `FilterExpressionJsonConverter` from `Infrastructure/Serialization/` — never put JSON attributes on Domain classes.

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

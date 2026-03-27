# CampaignEngine — Architecture Reference

## Overview

CampaignEngine follows **Clean Architecture** (Uncle Bob) with four layers. The dependency rule is strictly enforced via `.csproj` project references:

```
Domain          ← no external packages at all
Application     ← Domain only
Infrastructure  ← Domain + Application
Web             ← Application + Infrastructure (DI bootstrap only)
```

---

## Layers

### Domain (`CampaignEngine.Domain`)

Pure C# — zero NuGet packages.

| Type | Contents |
|---|---|
| Entities | `Campaign`, `Template`, `CampaignStep`, `DataSource`, `ApiKey`, `SendLog`, … |
| Value objects | `TemplateReference` |
| Enums | `CampaignStatus`, `TemplateStatus`, `ChannelType`, `SendStatus`, … |
| Exceptions | `DomainException` (invariants → 422), `ValidationException` (input → 400), `NotFoundException` (→ 404) |
| Filter AST | `FilterExpression`, `LeafFilterExpression`, `CompositeFilterExpression` |

**Domain behaviour (not anemic):**
- `Campaign.Schedule()` — validates Draft status + ScheduledAt ≥ 5 minutes ahead; transitions to Scheduled.
- `Campaign.AddStep(step)` — enforces the 10-step maximum.
- `Template.Publish()` — validates Draft-only guard.
- `Template.Archive()` — validates not-already-archived.

Entity methods throw `DomainException` for invariant violations. Cross-entity constraints (e.g., name uniqueness) are checked at the service level before calling entity methods.

---

### Application (`CampaignEngine.Application`)

Contracts and use-case orchestration. No implementations.

| Directory | Contents |
|---|---|
| `Interfaces/` | All service interfaces (`ICampaignService`, `ITemplateService`, etc.) |
| `Interfaces/Repositories/` | `IRepository<T>`, `ICampaignRepository`, `ITemplateRepository`, `IDataSourceRepository`, `ISendLogRepository`, `IApiKeyRepository`, `ICampaignChunkRepository` |
| `Interfaces/IUnitOfWork.cs` | Unit of Work: `CommitAsync`, `BeginTransactionAsync`, `CommitTransactionAsync`, `RollbackTransactionAsync`, `IAsyncDisposable` |
| `Interfaces/IIdentityService.cs` | Wraps ASP.NET Core Identity — login, logout, user CRUD, role management |
| `DTOs/` | Input/output data transfer objects (never expose Domain entities) |
| `Mappings/` | Mapster configuration (one `IRegister` class per aggregate + `MappingConfig.cs`) |
| `DependencyInjection/` | `AddApplication()`, authorization policies |

---

### Infrastructure (`CampaignEngine.Infrastructure`)

All framework/library implementations. Implements every Application interface.

| Directory | Contents |
|---|---|
| `Persistence/` | `CampaignEngineDbContext`, EF Core entity configurations, migrations |
| `Persistence/Repositories/` | `RepositoryBase<T>`, concrete repositories (Campaign, Template, DataSource, SendLog, ApiKey, CampaignChunk) |
| `Persistence/UnitOfWork.cs` | Wraps `DbContext.SaveChangesAsync` and `IDbContextTransaction` |
| `Campaigns/` | `CampaignService`, `ChunkCoordinatorService`, `RecipientChunkingService`, etc. |
| `Templates/` | `TemplateService`, `TemplatePreviewService`, `PlaceholderManifestService`, etc. |
| `Dispatch/` | `EmailDispatcher`, `SmsDispatcher`, `LetterDispatcher`, `ChannelDispatcherRegistry` |
| `Rendering/` | `ScribanTemplateRenderer`, post-processors (Email/SMS/Letter) |
| `Identity/` | `IdentityService` (implements `IIdentityService`), `ApplicationUser`, `ApplicationRole` |
| `Serialization/` | `FilterExpressionJsonConverter` — polymorphic JSON for the filter AST |
| `Logging/` | `AppLogger<T>`, `PerformanceLogger`, `PiiMasker` |
| `DependencyInjection/` | `AddInfrastructure(config)` — registers all implementations |

**Services inject repositories + `IUnitOfWork`** — never `CampaignEngineDbContext` directly (except the repositories themselves).

---

### Web (`CampaignEngine.Web`)

Thin presentation layer. No business logic.

| Directory | Contents |
|---|---|
| `Controllers/` | REST API controllers (delegate to Application services) |
| `Pages/` | Razor Pages for the UI (delegate to API controllers for mutations) |
| `Middleware/` | `GlobalExceptionMiddleware`, `ApiKeyAuthenticationMiddleware`, `ApiKeyRateLimitingMiddleware`, `CorrelationIdMiddleware` |

The Web layer only imports `Application` interfaces. The only `using CampaignEngine.Infrastructure` import allowed is in `Program.cs` for DI bootstrap.

---

## Key Design Decisions

### Repository Pattern + Unit of Work

Services operate through repository interfaces (`ICampaignRepository`, etc.) and commit changes through `IUnitOfWork`. This keeps services testable (mock repositories) and decouples them from EF Core.

```csharp
// Infrastructure service (correct)
public class CampaignService : ICampaignService
{
    public CampaignService(
        ICampaignRepository campaignRepository,
        IUnitOfWork unitOfWork, ...)

// Not this (wrong - couples service to EF Core)
public CampaignService(CampaignEngineDbContext dbContext, ...)
```

**Unit of Work lifecycle:** `UnitOfWork` is `IAsyncDisposable`. If a transaction is opened but `CommitTransactionAsync` is never called (e.g., exception thrown), `DisposeAsync` automatically rolls it back.

### Identity Abstraction

`IIdentityService` (Application layer) wraps ASP.NET Core Identity managers. Web layer controllers and pages inject `IIdentityService`, never `UserManager<ApplicationUser>` or `SignInManager<ApplicationUser>`. This maintains the dependency rule (Web → Application, not Web → Infrastructure).

### Mapster Mappings

All mapping configuration lives in `Application/Mappings/`:

```
Application/Mappings/
  CampaignMappings.cs       IRegister for Campaign → CampaignDto
  TemplateMappings.cs       IRegister for Template → TemplateDto
  DataSourceMappings.cs     IRegister for DataSource → DataSourceDto
  ApiKeyMappings.cs
  SendLogMappings.cs
  MappingConfig.cs          Central TypeAdapterConfig registration
```

Register in DI via `MappingConfig.ConfigureGlobalSettings()`. Use `.Adapt<TDto>()` in services.

### Filter AST Serialization

Domain filter classes (`FilterExpression`, `LeafFilterExpression`, `CompositeFilterExpression`) are pure C# with no JSON attributes. Polymorphic serialization is handled by `Infrastructure/Serialization/FilterExpressionJsonConverter`, which:
- Writes a `"type"` discriminator field explicitly in `Write()`
- Reads the `"type"` field to select the concrete subclass in `Read()`

Pass the converter explicitly when calling `JsonSerializer`:
```csharp
var options = new JsonSerializerOptions
{
    Converters = { new FilterExpressionJsonConverter() }
};
```

### Domain Exceptions vs Validation Exceptions

| Exception | HTTP | When to throw |
|---|---|---|
| `DomainException` | 422 | Entity invariant violated (e.g., schedule in wrong state) |
| `ValidationException` | 400 | Input data invalid (e.g., template not found, name duplicate) |
| `NotFoundException` | 404 | Requested resource does not exist |

All three are caught and mapped by `GlobalExceptionMiddleware`.

---

## Testing Strategy

| Project | Focus | Tools |
|---|---|---|
| `Domain.Tests` | Entity behaviour, exception classes | xUnit, FluentAssertions |
| `Application.Tests` | Service logic with mocked repositories | Moq, FluentAssertions |
| `Infrastructure.Tests` | Repository implementations, dispatchers, renderers | EF Core InMemory, FluentAssertions |

Infrastructure tests use `DbContextTestBase` which provides an isolated in-memory `CampaignEngineDbContext` per test class. Service tests construct concrete repositories + `UnitOfWork` from the same context instance.

Application tests mock repository interfaces (`Mock<ITemplateRepository>`) so they run without EF Core.

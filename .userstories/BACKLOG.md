﻿﻿﻿﻿﻿﻿﻿﻿# ðŸ“‹ Backlog - CampaignEngine

**Source:** _docs/prd.md
**Created:** 2026-03-19
**Last updated:** 2026-03-27

---

## ðŸ“Š Overview

**Total User Stories:** 38
**Estimated complexity:** 52-68 person-weeks (260-340 person-days)
**Overall status:** ðŸŸ¡ Planning

### Statistics by Priority
- 🔴 High priority (MVP): 25 stories
- 🟠 Medium priority (Phase 2): 9 stories
- 🟢 Low priority (Phase 3): 1 story

### Statistics by Complexity
- **S (Small)**: 8 stories
- **M (Medium)**: 19 stories
- **L (Large)**: 7 stories
- **XL (Extra Large)**: 1 story

---

## ðŸŽ­ Personas / Actors

### [DESIGNER] - Marie, Template Designer
**Role:** Communication/marketing team member with HTML/CSS skills
**Needs:**
- Create polished, brand-compliant templates with dynamic content
- Preview templates with real sample data
- Manage template versioning and composition
- Work with sub-templates for consistency

### [OPERATOR] - Thomas, Campaign Operator
**Role:** Operations/business team member responsible for customer communications
**Needs:**
- Launch targeted campaigns quickly
- Define multi-step sequences with delays
- Monitor campaign progress in real time
- Manage attachments and CC recipients
- Apply filters on data repositories

### [DEVELOPER] - Julien, Integration Developer
**Role:** Backend developer on internal business applications
**Needs:**
- Trigger transactional messages via simple API
- Use existing templates without duplicating logic
- Reliable delivery with status tracking
- Clear OpenAPI documentation

### [ADMIN] - Sophie, IT Administrator
**Role:** Infrastructure/ops team member
**Needs:**
- Monitor system health
- Manage user roles and permissions
- Configure SMTP servers, SMS providers, file share access
- Review audit logs and monitor Hangfire dashboard

---

## ðŸ—ï¸ Architecture & Technical Stack

**Runtime:** .NET 8 (LTS)
**Web Framework:** ASP.NET Core (Razor Pages + Web API)
**ORM:** Entity Framework Core
**Database:** SQL Server
**Background Jobs:** Hangfire Community
**Template Engine:** Scriban
**Object Mapping:** Mapster
**PDF Generation:** wkhtmltopdf / DinkToPdf (POC required)
**PDF Consolidation:** PdfSharp (MIT)
**CSS Inlining:** PreMailer.Net
**File Storage:** Internal file share (UNC paths)
**Hosting:** IIS on Windows Server
**Testing:** xUnit + Moq + FluentAssertions

**Performance Targets:**
- Single API send: < 500ms p95
- Batch campaign: 100,000 recipients in < 60 minutes (8 Hangfire workers)
- Delivery rate: > 98%
- Retry success: > 90% within 3 attempts

---

## ðŸŽ¯ User Stories

### Epic 1: Foundation & Infrastructure

> Core infrastructure setup including database, authentication, and layered architecture

#### [US-001] - Project scaffold and layered architecture setup
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Foundation & Infrastructure

**As a** Developer
**I want** a clean layered architecture with proper separation of concerns
**So that** the application is maintainable and extensible

**Specification context:**
> Layered architecture (Domain / Application / Infrastructure / Web). All cross-cutting concerns behind interfaces. DI-based strategy pattern for channels and data connectors.

**Acceptance criteria:**
- [x] Solution structure follows Domain / Application / Infrastructure / Web layers âœ…
- [x] All projects properly reference each other with correct dependencies âœ…
- [x] Microsoft.Extensions.DependencyInjection configured as DI container âœ…
- [x] Cross-cutting concerns (logging, error handling) abstracted behind interfaces âœ…
- [x] Unit test projects created with xUnit + Moq + FluentAssertions âœ…

**Technical tasks:**
- [x] `TASK-001-01` - **[Setup]** Create solution with 4 projects (Domain, Application, Infrastructure, Web) âœ… 2026-03-19
- [x] `TASK-001-02` - **[Setup]** Configure project dependencies and package references âœ… 2026-03-19
- [x] `TASK-001-03` - **[Config]** Set up DI container registration patterns âœ… 2026-03-19
- [x] `TASK-001-04` - **[Setup]** Add test projects (Domain.Tests, Application.Tests, Infrastructure.Tests) âœ… 2026-03-19
- [x] `TASK-001-05` - **[Config]** Configure appsettings.json structure for environments âœ… 2026-03-19
- [x] `TASK-001-06` - **[Doc]** Create README with architecture diagram âœ… 2026-03-19

**Business rules:**
1. No circular dependencies between layers
2. Infrastructure and Web depend on Application; Application depends on Domain only
3. All external dependencies injected via interfaces

**Dependencies:** None
**Estimation:** 3-4 days

---

#### [US-002] - Database provisioning and EF Core setup
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Foundation & Infrastructure

**As a** Developer
**I want** SQL Server database with Entity Framework Core migrations
**So that** all entities can be persisted reliably

**Specification context:**
> SQL Server as enterprise standard with EF Core for migrations and LINQ support

**Acceptance criteria:**
- [x] SQL Server database created with appropriate connection string âœ…
- [x] DbContext configured with all entity mappings âœ…
- [x] Initial migration created and applied âœ…
- [x] Seed data mechanism for development environment âœ…
- [x] Connection string encryption configured âœ…

**Technical tasks:**
- [x] `TASK-002-01` - **[Model]** Create DbContext with SQL Server provider âœ… 2026-03-19
- [x] `TASK-002-02` - **[Config]** Configure connection string with encryption âœ… 2026-03-19
- [x] `TASK-002-03` - **[Migration]** Create initial migration with all core tables âœ… 2026-03-19
- [x] `TASK-002-04` - **[Data]** Create seed data service for development âœ… 2026-03-19
- [x] `TASK-002-05` - **[Test]** Add integration tests with in-memory database âœ… 2026-03-19
- [x] `TASK-002-06` - **[Doc]** Document database schema and migration strategy âœ… 2026-03-19

**Business rules:**
1. All entity IDs use GUID for distributed generation
2. All entities have CreatedAt/UpdatedAt audit fields
3. Soft delete pattern for critical entities (templates, campaigns)

**Dependencies:** US-001
**Estimation:** 4-5 days

---

#### [US-003] - Authentication and authorization implementation
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Foundation & Infrastructure

**As a** IT Administrator (Sophie)
**I want** secure authentication with role-based access control
**So that** users can only access features appropriate to their role

**Specification context:**
> Role-based access control: Designer role (template CRUD, preview) vs Operator role (campaign CRUD, monitoring) vs Admin role. ASP.NET Core Identity or Windows Authentication.

**Acceptance criteria:**
- [x] Authentication mechanism implemented (Windows Auth or ASP.NET Core Identity) âœ…
- [x] Three roles defined: Designer, Operator, Admin âœ…
- [x] Authorization policies applied at controller/page level âœ…
- [x] Role assignment UI for Admin users âœ…
- [x] Audit trail for authentication events âœ…

**Technical tasks:**
- [x] `TASK-003-01` - **[Model]** Create User and Role entities âœ… 2026-03-19
- [x] `TASK-003-02` - **[Auth]** Implement authentication middleware (Windows or Identity) âœ… 2026-03-19
- [x] `TASK-003-03` - **[Auth]** Configure role-based authorization policies âœ… 2026-03-19
- [x] `TASK-003-04` - **[Frontend]** Create user management UI for Admin âœ… 2026-03-19
- [x] `TASK-003-05` - **[API]** Add role-checking attributes to controllers âœ… 2026-03-19
- [x] `TASK-003-06` - **[Test]** Unit tests for authorization policies âœ… 2026-03-19
- [x] `TASK-003-07` - **[Doc]** Document role permissions matrix âœ… 2026-03-19

**Business rules:**
1. Designer: Full template CRUD + preview, no campaign access
2. Operator: Full campaign CRUD + monitoring, read-only template access
3. Admin: Full access to all features + user management + configuration
4. Default role for new users: Operator

**Dependencies:** US-002
**Estimation:** 5-6 days

**Implementation notes:**
- Open Question Q4: Windows Authentication vs local Identity accounts - implement strategy pattern to support both
- Consider using Claims-based authorization for flexibility

---

#### [US-004] - Structured logging and observability setup
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Foundation & Infrastructure

**As a** IT Administrator (Sophie)
**I want** structured logging throughout the application
**So that** I can diagnose issues and monitor system health

**Specification context:**
> Structured logging. SEND_LOG as source of truth for all dispatch activity. Observability as core requirement.

**Acceptance criteria:**
- [x] Structured logging configured (Serilog or NLog) âœ…
- [x] Log levels appropriately used (Debug, Info, Warning, Error, Critical) âœ…
- [x] Correlation IDs tracked across request lifecycle âœ…
- [x] Performance metrics logged for critical operations âœ…
- [x] Log sink configured (file, database, or monitoring tool) âœ…

**Technical tasks:**
- [x] `TASK-004-01` - **[Config]** Configure Serilog with structured logging âœ… 2026-03-19
- [x] `TASK-004-02` - **[Middleware]** Add correlation ID middleware âœ… 2026-03-19
- [x] `TASK-004-03` - **[Logging]** Create logging abstractions for core services âœ… 2026-03-19
- [x] `TASK-004-04` - **[Config]** Configure log sinks (file + SQL for errors) âœ… 2026-03-19
- [x] `TASK-004-05` - **[Test]** Verify logging in integration tests âœ… 2026-03-19
- [x] `TASK-004-06` - **[Doc]** Document logging conventions âœ… 2026-03-19

**Business rules:**
1. All API calls logged with request/response details
2. All send operations logged with correlation to campaign/template
3. All errors logged with stack traces
4. PII masked in logs

**Dependencies:** US-001
**Estimation:** 2-3 days

---

### Epic 2: Template Management (Template Registry)

> Create, manage, and version message templates with dynamic content support

#### [US-005] - Template CRUD operations
**Status:** ✅ DONE
**Start date:** 2026-03-20 00:00:00
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Template Management

**As a** Template Designer (Marie)
**I want** to create and edit templates with name, channel, HTML body, and placeholder manifest
**So that** I can define reusable message layouts

**Specification context:**
> Template CRUD: Create, read, update, delete templates with name, channel (Email/Letter/SMS), HTML body, and placeholder manifest.

**Acceptance criteria:**
- [x] Templates can be created with name, channel type, HTML body âœ…
- [x] Templates can be edited and updated âœ…
- [x] Templates can be soft-deleted (archived) âœ…
- [x] Template list view with filtering by channel and status âœ…
- [x] Template detail view showing all metadata âœ…

**Technical tasks:**
- [x] `TASK-005-01` - **[Model]** Create Template entity with channel enum âœ… 2026-03-20
- [x] `TASK-005-02` - **[API]** POST /api/templates endpoint with validation âœ… 2026-03-20
- [x] `TASK-005-03` - **[API]** GET /api/templates with filtering and pagination âœ… 2026-03-20
- [x] `TASK-005-04` - **[API]** PUT /api/templates/{id} endpoint âœ… 2026-03-20
- [x] `TASK-005-05` - **[API]** DELETE /api/templates/{id} (soft delete) âœ… 2026-03-20
- [x] `TASK-005-06` - **[Frontend]** Template list Razor page with grid âœ… 2026-03-20
- [x] `TASK-005-07` - **[Frontend]** Template create/edit form with validation âœ… 2026-03-20
- [x] `TASK-005-08` - **[Test]** Unit tests for template service âœ… 2026-03-20
- [x] `TASK-005-09` - **[Test]** Integration tests for template API âœ… 2026-03-20
- [x] `TASK-005-10` - **[Doc]** API documentation for template endpoints âœ… 2026-03-20

**Business rules:**
1. Template names must be unique within same channel
2. Channel types: Email, Letter, SMS
3. Soft delete: set IsDeleted flag, keep for audit
4. Only Designer and Admin roles can create/edit templates

**Dependencies:** US-002, US-003
**Estimation:** 5-6 days

---

#### [US-006] - Placeholder manifest declaration
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Template Management

**As a** Template Designer (Marie)
**I want** to declare typed placeholders with source indication
**So that** operators know what data is required for each template

**Specification context:**
> Typed placeholder declarations (scalar, table, list, freeField) with source indication (datasource vs operator input).

**Acceptance criteria:**
- [x] Placeholder manifest can be defined for each template
- [x] Placeholder types supported: scalar, table, list, freeField
- [x] Each placeholder indicates source: dataSource or operatorInput
- [x] Manifest validation ensures all template placeholders are declared
- [x] UI shows placeholder manifest in template editor

**Technical tasks:**
- [x] `TASK-006-01` - **[Model]** Create PlaceholderManifest value object with type enum
- [x] `TASK-006-02` - **[Model]** Add PlaceholderManifests collection to Template entity
- [x] `TASK-006-03` - **[Service]** Create placeholder parser to extract from template HTML
- [x] `TASK-006-04` - **[Service]** Validate manifest completeness against template
- [x] `TASK-006-05` - **[Frontend]** Placeholder manifest editor UI component
- [x] `TASK-006-06` - **[Frontend]** Auto-detect placeholders from template HTML
- [x] `TASK-006-07` - **[Test]** Unit tests for placeholder extraction
- [x] `TASK-006-08` - **[Test]** Validation tests for manifest completeness
- [x] `TASK-006-09` - **[Doc]** Placeholder syntax guide

**Business rules:**
1. Placeholder syntax: `{{key}}` for scalar, `{{#table}}...{{/table}}` for iteration
2. All placeholders in template HTML must be declared in manifest
3. FreeField placeholders require operator input at campaign creation
4. DataSource placeholders must map to available data source fields

**Dependencies:** US-005
**Estimation:** 5-6 days

---

#### [US-007] - Sub-template composition
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Template Management

**As a** Template Designer (Marie)
**I want** to compose templates from reusable blocks (header, footer, signature)
**So that** I maintain brand consistency across all communications

**Specification context:**
> Sub-template composition: Support reusable sub-templates (header, footer, signature blocks) that can be embedded in parent templates.

**Acceptance criteria:**
- [x] Sub-templates can be created as standalone templates
- [x] Parent templates can reference sub-templates via placeholder syntax
- [x] Sub-templates are resolved recursively during rendering
- [x] Changes to sub-templates propagate to parent previews (not frozen campaigns)
- [x] Circular reference detection prevents infinite loops

**Technical tasks:**
- [x] `TASK-007-01` - **[Model]** Add IsSubTemplate flag to Template entity
- [x] `TASK-007-02` - **[Model]** Create TemplateReference value object
- [x] `TASK-007-03` - **[Service]** Implement sub-template resolution logic
- [x] `TASK-007-04` - **[Service]** Add circular reference detection
- [x] `TASK-007-05` - **[Frontend]** Sub-template selector in template editor
- [x] `TASK-007-06` - **[Frontend]** Visual indicator for sub-template usage
- [x] `TASK-007-07` - **[Test]** Unit tests for recursive resolution
- [x] `TASK-007-08` - **[Test]** Circular reference detection tests
- [x] `TASK-007-09` - **[Doc]** Sub-template composition guide

**Business rules:**
1. Sub-template syntax: `{{> subtemplate_name}}`
2. Sub-templates can reference other sub-templates (max depth: 5)
3. Circular references throw validation error
4. Sub-templates inherit channel from parent

**Dependencies:** US-005
**Estimation:** 4-5 days

---

#### [US-008] - Template versioning
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🟠 Medium
**Complexity:** M
**Epic:** Template Management

**As a** Template Designer (Marie)
**I want** automatic version history for templates
**So that** I can track changes and revert if needed

**Specification context:**
> Auto-increment version on each update; maintain version history. Template snapshots guarantee campaign reproducibility.

**Acceptance criteria:**
- [x] Version number auto-increments on every template update
- [x] Full version history maintained in separate table
- [x] Version diff view showing changes between versions
- [x] Ability to view/preview any historical version
- [x] Ability to revert to previous version (creates new version)

**Technical tasks:**
- [x] `TASK-008-01` - **[Model]** Add Version field to Template entity ✅ 2026-03-20
- [x] `TASK-008-02` - **[Model]** Create TemplateHistory entity for snapshots ✅ 2026-03-20
- [x] `TASK-008-03` - **[Service]** Auto-snapshot on template update ✅ 2026-03-20
- [x] `TASK-008-04` - **[API]** GET /api/templates/{id}/history endpoint ✅ 2026-03-20
- [x] `TASK-008-05` - **[API]** POST /api/templates/{id}/revert/{version} endpoint ✅ 2026-03-20
- [x] `TASK-008-06` - **[Frontend]** Version history view with diff display ✅ 2026-03-20
- [x] `TASK-008-07` - **[Frontend]** Revert confirmation dialog ✅ 2026-03-20
- [x] `TASK-008-08` - **[Test]** Versioning logic tests ✅ 2026-03-20
- [x] `TASK-008-09` - **[Doc]** Version management guide ✅ 2026-03-20

**Business rules:**
1. Version starts at 1, increments on every save
2. Version history never deleted (audit requirement)
3. Revert creates new version (doesn't overwrite history)
4. Frozen campaign snapshots reference specific version

**Dependencies:** US-005
**Estimation:** 4-5 days

---

#### [US-009] - Template lifecycle workflow
**Status:** ✅ DONE
**Start date:** 2026-03-25
**End date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Template Management

**As a** IT Administrator (Sophie)
**I want** template governance with Draft â†’ Published â†’ Archived states
**So that** incomplete templates cannot be used in production campaigns

**Specification context:**
> Status management: Draft â†’ Published â†’ Archived. Incomplete templates cannot be used in production.

**Acceptance criteria:**
- [x] Templates have status: Draft, Published, Archived ✅
- [x] Only Published templates available for campaign creation ✅
- [x] Draft templates can be edited freely without affecting campaigns ✅
- [x] Archived templates visible for audit but not usable ✅
- [x] Status transition validation and audit logging ✅

**Technical tasks:**
- [x] `TASK-009-01` - **[Model]** Add Status enum to Template entity ✅ 2026-03-20
- [x] `TASK-009-02` - **[Service]** Implement status transition validation ✅ 2026-03-20
- [x] `TASK-009-03` - **[API]** POST /api/templates/{id}/publish endpoint ✅ 2026-03-20
- [x] `TASK-009-04` - **[API]** POST /api/templates/{id}/archive endpoint ✅ 2026-03-20
- [x] `TASK-009-05` - **[Frontend]** Status badges and transition buttons ✅ 2026-03-20
- [x] `TASK-009-06` - **[Test]** Status transition validation tests ✅ 2026-03-20
- [x] `TASK-009-07` - **[Doc]** Template lifecycle documentation ✅ 2026-03-20

**Business rules:**
1. New templates start as Draft
2. Draft â†’ Published requires complete placeholder manifest
3. Published templates can be edited (creates new version) or archived
4. Archived templates cannot transition back to Published
5. Only Admin can force-archive templates in active campaigns

**Dependencies:** US-005, US-006
**Estimation:** 3-4 days

---

#### [US-010] - Template preview with sample data
**Status:** â DONE
**End date:** 2026-03-25 10:00
**Start date:** 2026-03-25 09:00
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Template Management

**As a** Template Designer (Marie)
**I want** to preview my template with real sample data
**So that** I can verify rendering before publication

**Specification context:**
> Resolve a template with sample data from a real data source (read-only). Preview capability essential for designer workflow.

**Acceptance criteria:**
- [x] Preview button available in template editor âœ…
- [x] User can select data source for preview âœ…
- [x] System fetches N sample rows from selected data source âœ…
- [x] Template rendered with first sample row data âœ…
- [x] Preview shows channel-specific output (HTML for email, PDF for letter) âœ…

**Technical tasks:**
- [x] `TASK-010-01` - **[API]** POST /api/templates/{id}/preview endpoint âœ… 2026-03-25
- [x] `TASK-010-02` - **[Service]** Sample data fetcher from data source âœ… 2026-03-25
- [x] `TASK-010-03` - **[Service]** Template resolution with sample data âœ… 2026-03-25
- [x] `TASK-010-04` - **[Frontend]** Preview modal with data source selector âœ… 2026-03-25
- [x] `TASK-010-05` - **[Frontend]** Rendered preview display (HTML/PDF viewer) âœ… 2026-03-25
- [x] `TASK-010-06` - **[Test]** Preview rendering tests âœ… 2026-03-25
- [x] `TASK-010-07` - **[Doc]** Preview workflow guide âœ… 2026-03-25

**Business rules:**
1. Preview is read-only (no actual sends)
2. Sample data: first 5 rows from data source
3. Preview respects channel post-processing (CSS inline, PDF conversion)
4. Missing placeholder values highlighted in preview

**Dependencies:** US-005, US-006, US-015 (Data Source Connector)
**Estimation:** 4-5 days

---

### Epic 3: Rendering Engine

> Transform templates with dynamic data into channel-specific outputs

#### [US-011] - Scriban template engine integration
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Rendering Engine

**As a** Integration Developer (Julien)
**I want** a reliable template engine abstracted behind ITemplateRenderer
**So that** we don't maintain custom parsing code and can swap engines if needed

**Specification context:**
> Use Scriban as the underlying engine behind an ITemplateRenderer abstraction. Lightweight, sandboxed, Liquid-like syntax.

**Acceptance criteria:**
- [x] ITemplateRenderer interface defined in Application layer âœ…
- [x] Scriban implementation in Infrastructure layer âœ…
- [x] Basic scalar substitution working âœ…
- [x] Error handling for malformed templates âœ…
- [x] Performance benchmarks for 1000 renders/sec âœ…

**Technical tasks:**
- [x] `TASK-011-01` - **[Interface]** Define ITemplateRenderer in Application layer âœ… 2026-03-19
- [x] `TASK-011-02` - **[Model]** Create TemplateContext data model âœ… 2026-03-19
- [x] `TASK-011-03` - **[Service]** Implement ScribanTemplateRenderer âœ… 2026-03-19
- [x] `TASK-011-04` - **[Service]** Configure Scriban security settings (sandbox) âœ… 2026-03-19
- [x] `TASK-011-05` - **[Service]** Add error handling and validation âœ… 2026-03-19
- [x] `TASK-011-06` - **[Test]** Unit tests for renderer âœ… 2026-03-19
- [x] `TASK-011-07` - **[Test]** Performance benchmark tests âœ… 2026-03-19
- [x] `TASK-011-08` - **[Doc]** Template syntax reference âœ… 2026-03-19

**Business rules:**
1. All data values HTML-escaped by default to prevent XSS
2. Template HTML itself trusted (Designer role only)
3. Renderer must be stateless and thread-safe
4. Timeout: 10 seconds max per render

**Dependencies:** US-001
**Estimation:** 4-5 days

---

#### [US-012] - Advanced rendering features (tables, lists, conditionals)
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Rendering Engine

**As a** Template Designer (Marie)
**I want** dynamic tables, lists, and conditional content
**So that** templates can adapt to variable data structures

**Specification context:**
> Table rendering: `{{#table}}...{{/table}}` with row iteration. List rendering: `{{#list}}...{{/list}}`. Conditional blocks: `{{#if condition}}...{{/if}}`.

**Acceptance criteria:**
- [x] Table blocks iterate over array data and generate HTML tables âœ…
- [x] List blocks iterate and generate bulleted/numbered lists âœ…
- [x] Conditional blocks evaluate boolean expressions âœ…
- [x] Nested structures supported (table within conditional) âœ…
- [x] Empty collections handled gracefully âœ…

**Technical tasks:**
- [x] `TASK-012-01` - **[Service]** Implement table iteration logic in Scriban âœ… 2026-03-19
- [x] `TASK-012-02` - **[Service]** Implement list iteration logic âœ… 2026-03-19
- [x] `TASK-012-03` - **[Service]** Implement conditional block evaluation âœ… 2026-03-19
- [x] `TASK-012-04` - **[Service]** Add custom Scriban functions (formatDate, formatCurrency) âœ… 2026-03-19
- [x] `TASK-012-05` - **[Test]** Unit tests for table rendering âœ… 2026-03-19
- [x] `TASK-012-06` - **[Test]** Unit tests for list rendering âœ… 2026-03-19
- [x] `TASK-012-07` - **[Test]** Unit tests for conditional logic âœ… 2026-03-19
- [x] `TASK-012-08` - **[Test]** Integration tests for nested structures âœ… 2026-03-19
- [x] `TASK-012-09` - **[Doc]** Advanced syntax examples âœ… 2026-03-19

**Business rules:**
1. Table syntax: `{{#for row in table}} <tr><td>{{row.field}}</td></tr> {{/for}}`
2. List syntax: `{{#for item in list}} <li>{{item}}</li> {{/for}}`
3. Conditional syntax: `{{if condition}} content {{/if}}`
4. Empty tables/lists render nothing (no placeholder text)

**Dependencies:** US-011
**Estimation:** 5-6 days

---

#### [US-013] - Channel-specific post-processing
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Rendering Engine

**As a** Campaign Operator (Thomas)
**I want** channel-appropriate output (CSS inlining for email, PDF for letter, plain text for SMS)
**So that** messages render correctly on each medium

**Specification context:**
> Email: CSS inlining + HTML sanitization. Letter: HTMLâ†’PDF conversion. SMS: plain text extraction + truncation.

**Acceptance criteria:**
- [x] Email channel: inline CSS using PreMailer.Net âœ…
- [x] Letter channel: convert HTML to PDF using chosen tool (POC required) âœ…
- [x] SMS channel: strip HTML tags and truncate to 160 characters âœ…
- [x] Multi-page PDF consolidation for letter batches âœ…
- [x] Error handling for conversion failures âœ…

**Technical tasks:**
- [x] `TASK-013-01` - **[POC]** PDF generation POC (wkhtmltopdf vs DinkToPdf vs Puppeteer) âœ… 2026-03-20
- [x] `TASK-013-02` - **[Service]** Implement EmailPostProcessor with PreMailer.Net âœ… 2026-03-20
- [x] `TASK-013-03` - **[Service]** Implement LetterPostProcessor with chosen PDF tool âœ… 2026-03-20
- [x] `TASK-013-04` - **[Service]** Implement SmsPostProcessor with HTML stripping âœ… 2026-03-20
- [x] `TASK-013-05` - **[Service]** Implement PDF consolidation with PdfSharp âœ… 2026-03-20
- [x] `TASK-013-06` - **[Interface]** Define IChannelPostProcessor abstraction âœ… 2026-03-20
- [x] `TASK-013-07` - **[Test]** Unit tests for each post-processor âœ… 2026-03-20
- [x] `TASK-013-08` - **[Test]** PDF generation performance tests âœ… 2026-03-20
- [x] `TASK-013-09` - **[Test]** Email CSS inlining tests (Outlook compatibility) âœ… 2026-03-20
- [x] `TASK-013-10` - **[Doc]** Channel post-processing documentation âœ… 2026-03-20

**Business rules:**
1. Email CSS inlining required for Outlook compatibility
2. Letter PDF: A4 format, embedded fonts, max 10MB per file
3. SMS truncation: preserve whole words when possible
4. PDF consolidation: max 500 pages per batch file

**Dependencies:** US-011
**Estimation:** 8-10 days

**Implementation notes:**
- Open Question Q1: PDF tool POC required before implementation
- Consider performance impact of PDF generation on Hangfire workers

---

### Epic 4: Data Source Connector

> Connect to external data repositories to fetch recipient data

#### [US-014] - Data source declaration and management
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Data Source Connector

**As a** IT Administrator (Sophie)
**I want** to declare data repositories with connection details and schema
**So that** operators can target different populations for campaigns

**Specification context:**
> Register data sources with name, connection type, connection string, and schema definition (fields, types, filterability).

**Acceptance criteria:**
- [x] Data sources can be created with name, type, connection string âœ…
- [x] Schema can be defined with field names and types âœ…
- [x] Connection testing validates connectivity âœ…
- [x] Field metadata includes filterability and data type âœ…
- [x] Data source list view with status indicators âœ…

**Technical tasks:**
- [x] `TASK-014-01` - **[Model]** Create DataSource entity with connection metadata âœ… 2026-03-20
- [x] `TASK-014-02` - **[Model]** Create FieldDefinition value object for schema âœ… 2026-03-20
- [x] `TASK-014-03` - **[API]** POST /api/datasources endpoint âœ… 2026-03-20
- [x] `TASK-014-04` - **[API]** GET /api/datasources with filtering âœ… 2026-03-20
- [x] `TASK-014-05` - **[Service]** Connection testing service âœ… 2026-03-20
- [x] `TASK-014-06` - **[Frontend]** Data source management UI âœ… 2026-03-20
- [x] `TASK-014-07` - **[Frontend]** Connection string encryption in UI âœ… 2026-03-20
- [x] `TASK-014-08` - **[Test]** Connection validation tests âœ… 2026-03-20
- [x] `TASK-014-09` - **[Doc]** Data source configuration guide âœ… 2026-03-20

**Business rules:**
1. Data source types: SQL Server, REST API (Phase 1)
2. Connection strings encrypted at rest
3. Only Admin role can create/edit data sources
4. Schema can be auto-discovered or manually defined

**Dependencies:** US-002, US-003
**Estimation:** 5-6 days

---

#### [US-015] - SQL Server data connector implementation
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Data Source Connector

**As a** Campaign Operator (Thomas)
**I want** to connect to SQL Server databases for recipient data
**So that** I can target populations from existing business databases

**Specification context:**
> IDataSourceConnector with SQL Server implementation. Schema-agnostic querying with parameterized SQL for security.

**Acceptance criteria:**
- [x] IDataSourceConnector interface defined
 âœ…
- [x] SqlServerConnector implementation with Dapper
 âœ…
- [x] Schema auto-discovery from SQL Server metadata
 âœ…
- [x] Parameterized query generation prevents SQL injection
 âœ…
- [x] Connection pooling and timeout configuration
 âœ…

**Technical tasks:**
- [x] `TASK-015-01` - **[Interface]** Define IDataSourceConnector interface
 âœ… 2026-03-20
- [x] `TASK-015-02` - **[Service]** Implement SqlServerConnector with Dapper
 âœ… 2026-03-20
- [x] `TASK-015-03` - **[Service]** Schema discovery from INFORMATION_SCHEMA
 âœ… 2026-03-20
- [x] `TASK-015-04` - **[Service]** Query builder with parameterization
 âœ… 2026-03-20
- [x] `TASK-015-05` - **[Service]** Connection pool configuration
 âœ… 2026-03-20
- [x] `TASK-015-06` - **[Test]** Integration tests with test database
 âœ… 2026-03-20
- [x] `TASK-015-07` - **[Test]** SQL injection prevention tests
 âœ… 2026-03-20
- [x] `TASK-015-08` - **[Doc]** SQL connector configuration guide
 âœ… 2026-03-20

**Business rules:**
1. All queries must use parameterized SQL (no string concatenation)
2. Read-only connection (SELECT only)
3. Query timeout: 30 seconds default
4. Connection pooling enabled for performance

**Dependencies:** US-014
**Estimation:** 5-6 days

---

#### [US-016] - Filter expression builder (AST)
**Status:** 🟢 DONE
**Start date:** 2026-03-25
**End date:** 2026-03-25
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Data Source Connector

**As a** Campaign Operator (Thomas)
**I want** visual filtering to target specific populations
**So that** I can segment recipients without writing SQL

**Specification context:**
> Operator builds filters as expression trees; connector translates to parameterized SQL. No raw SQL from operators.

**Acceptance criteria:**
- [x] Filter UI supports field selection, operator, and value input ✅
- [x] Supported operators: =, !=, >, <, >=, <=, LIKE, IN ✅
- [x] Multiple filter conditions with AND/OR logic ✅
- [x] Filter AST serialized to JSON for storage ✅
- [x] AST translated to parameterized SQL WHERE clause ✅

**Technical tasks:**
- [x] `TASK-016-01` - **[Model]** Create FilterExpression AST classes ✅ 2026-03-25
- [x] `TASK-016-02` - **[Service]** Filter AST to SQL translator ✅ 2026-03-25
- [x] `TASK-016-03` - **[Service]** Expression validation service ✅ 2026-03-25
- [x] `TASK-016-04` - **[API]** POST /api/datasources/{id}/preview endpoint (apply filters) ✅ 2026-03-25
- [x] `TASK-016-05` - **[Frontend]** Filter builder UI component ✅ 2026-03-25
- [x] `TASK-016-06` - **[Frontend]** Filter preview with row count ✅ 2026-03-25
- [x] `TASK-016-07` - **[Test]** AST to SQL translation tests ✅ 2026-03-25
- [x] `TASK-016-08` - **[Test]** SQL injection prevention in filter values ✅ 2026-03-25
- [x] `TASK-016-09` - **[Test]** Complex filter logic tests (AND/OR) ✅ 2026-03-25
- [x] `TASK-016-10` - **[Doc]** Filter expression syntax guide ✅ 2026-03-25

**Business rules:**
1. All filter values parameterized (prevent SQL injection)
2. Date fields support relative filters (last 30 days, etc.)
3. IN operator supports up to 1000 values
4. Filter preview limited to first 100 rows

**Dependencies:** US-015
**Estimation:** 7-8 days

---

#### [US-017] - REST API data connector (Phase 2)
**Status:** ✅ DONE
**Start date:** 2026-03-28
**End date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** M
**Epic:** Data Source Connector

**As a** Integration Developer (Julien)
**I want** to connect to REST API data sources
**So that** we can consume recipient data from external systems

**Specification context:**
> Additional data source connectors (REST API, CSV import). Open Question Q8: existing REST APIs vs SQL only.

**Acceptance criteria:**
- [x] RestApiConnector implementation of IDataSourceConnector ✅
- [x] Support for GET endpoints with query parameters ✅
- [x] JSON response parsing to data rows ✅
- [x] Authentication support (API key, OAuth2) ✅
- [x] Pagination handling for large datasets ✅

**Technical tasks:**
- [x] `TASK-017-01` - **[Service]** Implement RestApiConnector ✅ 2026-03-28
- [x] `TASK-017-02` - **[Service]** JSON to data row mapping ✅ 2026-03-28
- [x] `TASK-017-03` - **[Service]** Authentication handler (API key, OAuth2) ✅ 2026-03-28
- [x] `TASK-017-04` - **[Service]** Pagination strategy (link header, page param) ✅ 2026-03-28
- [x] `TASK-017-05` - **[Test]** Integration tests with mock API ✅ 2026-03-28
- [x] `TASK-017-06` - **[Doc]** REST API connector guide ✅ 2026-03-28

**Business rules:**
1. API timeout: 60 seconds
2. Retry policy: 3 attempts with exponential backoff
3. Response size limit: 50 MB
4. JSON path selector for nested data

**Dependencies:** US-015
**Estimation:** 5-6 days

---

### Epic 5: Dispatch Engine

> Send messages through email, SMS, and letter channels

#### [US-018] - Channel dispatcher abstraction
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Dispatch Engine

**As a** Integration Developer (Julien)
**I want** pluggable channel dispatchers via strategy pattern
**So that** adding new channels doesn't require core changes

**Specification context:**
> IChannelDispatcher interface with DI-based registry â€” no hardcoded switch/case. Extensibility for WhatsApp, Push.

**Acceptance criteria:**
- [x] IChannelDispatcher interface defined âœ…
- [x] Dispatcher registry with DI-based resolution âœ…
- [x] Channel-specific configuration model âœ…
- [x] Dispatch result model with success/failure indication âœ…
- [x] Error handling abstraction for all channels âœ…

**Technical tasks:**
- [x] `TASK-018-01` - **[Interface]** Define IChannelDispatcher interface âœ… 2026-03-19
- [x] `TASK-018-02` - **[Model]** Create DispatchRequest and DispatchResult models âœ… 2026-03-19
- [x] `TASK-018-03` - **[Service]** Create ChannelDispatcherRegistry with DI âœ… 2026-03-19
- [x] `TASK-018-04` - **[Model]** Channel configuration base class âœ… 2026-03-19
- [x] `TASK-018-05` - **[Test]** Mock dispatcher for testing âœ… 2026-03-19
- [x] `TASK-018-06` - **[Doc]** Channel dispatcher extension guide âœ… 2026-03-19

**Business rules:**
1. Each channel registered in DI container
2. Dispatcher selected by Channel enum value
3. All dispatchers return standardized result
4. Transient failures throw retriable exceptions

**Dependencies:** US-001
**Estimation:** 3-4 days

---

#### [US-019] - Email dispatcher (SMTP)
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Dispatch Engine

**As a** Campaign Operator (Thomas)
**I want** to send formatted emails via SMTP
**So that** recipients receive HTML communications

**Specification context:**
> Send resolved HTML emails via configurable SMTP server with attachment and CC support.

**Acceptance criteria:**
- [x] SMTP configuration in appsettings (server, port, credentials) ✅
- [x] HTML email sending with proper headers ✅
- [x] Attachment support (multiple files) ✅
- [x] CC and BCC support ✅
- [x] SMTP error handling and retry logic ✅

**Technical tasks:**
- [x] `TASK-019-01` - **[Service]** Implement EmailDispatcher with MailKit
 ✅ 2026-03-20
- [x] `TASK-019-02` - **[Config]** SMTP configuration model
 ✅ 2026-03-20
- [x] `TASK-019-03` - **[Service]** Attachment handling from file paths
 ✅ 2026-03-20
- [x] `TASK-019-04` - **[Service]** CC/BCC recipient list handling
 ✅ 2026-03-20
- [x] `TASK-019-05` - **[Service]** SMTP error categorization (transient vs permanent)
 ✅ 2026-03-20
- [x] `TASK-019-06` - **[Test]** Integration tests with local SMTP server
 ✅ 2026-03-20
- [x] `TASK-019-07` - **[Test]** Attachment validation tests
 ✅ 2026-03-20
- [x] `TASK-019-08` - **[Doc]** SMTP configuration guide
 ✅ 2026-03-20

**Business rules:**
1. From address configurable per environment
2. Reply-To address optional per campaign
3. Attachment whitelist: PDF, DOCX, XLSX, PNG, JPG
4. Attachment size limits: 10 MB per file, 25 MB total

**Dependencies:** US-018, US-013 (CSS inlining)
**Estimation:** 5-6 days

**Implementation notes:**
- Open Question Q3: SMTP server limits affect throttling configuration

---

#### [US-020] - SMS dispatcher
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Dispatch Engine

**As a** Campaign Operator (Thomas)
**I want** to send SMS messages to recipients
**So that** I can reach them on mobile devices

**Specification context:**
> Send plain text messages via external SMS provider API. Open Question Q2: provider contract needed.

**Acceptance criteria:**
- [x] SMS provider integration (Twilio, Nexmo, or configurable) ✅
- [x] Plain text message sending ✅
- [x] Phone number validation ✅
- [x] Delivery status tracking (if supported by provider) ✅
- [x] Error handling for invalid numbers ✅

**Technical tasks:**
- [x] `TASK-020-01` - **[Service]** Implement SmsDispatcher ✅ 2026-03-20
- [x] `TASK-020-02` - **[Config]** SMS provider configuration (API key, endpoint) ✅ 2026-03-20
- [x] `TASK-020-03` - **[Service]** Phone number validation (international format) ✅ 2026-03-20
- [x] `TASK-020-04` - **[Service]** Provider API client (Twilio or generic) ✅ 2026-03-20
- [x] `TASK-020-05` - **[Service]** Delivery status callback handler ✅ 2026-03-20
- [x] `TASK-020-06` - **[Test]** Integration tests with provider sandbox ✅ 2026-03-20
- [x] `TASK-020-07` - **[Doc]** SMS provider setup guide ✅ 2026-03-20

**Business rules:**
1. Phone numbers in E.164 format (+1234567890)
2. Message truncation: 160 characters (or configurable per provider)
3. Rate limiting per provider contract
4. Invalid numbers logged but don't fail campaign

**Dependencies:** US-018, US-013 (text extraction)
**Estimation:** 5-6 days

**Implementation notes:**
- Open Question Q2: SMS provider API contract required

---

#### [US-021] - PDF letter dispatcher
**Status:** ✅ DONE
**End date:** 2026-03-20
**Start date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Dispatch Engine

**As a** Campaign Operator (Thomas)
**I want** to generate PDF mailings for print providers
**So that** recipients can receive physical letters

**Specification context:**
> Generate PDF files and transmit to print/mail provider; support multi-page consolidation. Open Question Q5: provider format.

**Acceptance criteria:**
- [x] PDF generation from HTML content ✅
- [x] Multi-page PDF consolidation (500 pages max per batch) ✅
- [x] File transmission to print provider (file drop or API) ✅
- [x] PDF metadata (recipient info, page count) ✅
- [x] Error handling for generation failures ✅

**Technical tasks:**
- [x] `TASK-021-01` - **[Service]** Implement LetterDispatcher
 ✅ 2026-03-20
- [x] `TASK-021-02` - **[Service]** PDF generation wrapper (using US-013 post-processor)
 ✅ 2026-03-20
- [x] `TASK-021-03` - **[Service]** PDF consolidation service with PdfSharp
 ✅ 2026-03-20
- [x] `TASK-021-04` - **[Service]** Print provider file drop handler (UNC or API)
 ✅ 2026-03-20
- [x] `TASK-021-05` - **[Service]** PDF metadata generation (manifest file)
 ✅ 2026-03-20
- [x] `TASK-021-06` - **[Test]** PDF generation tests
 ✅ 2026-03-20
- [x] `TASK-021-07` - **[Test]** Consolidation tests (verify page order)
 ✅ 2026-03-20
- [x] `TASK-021-08` - **[Doc]** Letter channel configuration guide
 ✅ 2026-03-20

**Business rules:**
1. A4 format, portrait orientation
2. Consolidation: ordered by recipient ID or campaign sequence
3. Manifest file: CSV with recipient metadata
4. File naming convention: CAMPAIGN_{id}_{timestamp}.pdf

**Dependencies:** US-018, US-013 (PDF conversion)
**Estimation:** 7-8 days

**Implementation notes:**
- Open Question Q5: Print provider format requirements
- Depends on US-013 PDF tool POC decision

---

#### [US-022] - Channel throttling and rate limiting
**Status:** ✅ DONE
**Start date:** 2026-03-28
**End date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** M
**Epic:** Dispatch Engine

**As a** IT Administrator (Sophie)
**I want** configurable send throttling per channel
**So that** we don't overwhelm mail servers or exceed provider limits

**Specification context:**
> Configurable rate limiting per channel (e.g., 100 msgs/sec for SMTP). Respect server limits to avoid blacklisting.

**Acceptance criteria:**
- [x] Rate limiting configuration per channel (messages/second)
- [x] Throttling applied at dispatcher level
- [x] Queue backpressure when limit reached
- [x] Monitoring metrics for send rate
- [x] Graceful handling of rate limit errors

**Technical tasks:**
- [x] `TASK-022-01` - **[Service]** Implement rate limiter service (token bucket) ✅ 2026-03-28
- [x] `TASK-022-02` - **[Config]** Per-channel rate limit configuration ✅ 2026-03-28
- [x] `TASK-022-03` - **[Service]** Integrate throttling in dispatchers ✅ 2026-03-28
- [x] `TASK-022-04` - **[Service]** Queue backpressure handling ✅ 2026-03-28
- [x] `TASK-022-05` - **[Monitoring]** Rate limit metrics (Prometheus or AppMetrics) ✅ 2026-03-28
- [x] `TASK-022-06` - **[Test]** Rate limiting tests ✅ 2026-03-28
- [x] `TASK-022-07` - **[Doc]** Throttling configuration guide ✅ 2026-03-28

**Business rules:**
1. Default rates: SMTP 100/sec, SMS 10/sec, Letter no limit
2. Configurable per environment
3. Rate limit errors trigger retry with backoff
4. Metrics exposed for monitoring

**Dependencies:** US-018, US-019, US-020, US-021
**Estimation:** 4-5 days

---

### Epic 6: Campaign Orchestrator

> Create and execute multi-step campaigns with recipient targeting

#### [US-023] - Campaign creation and configuration
**Status:** ✅ DONE
**Start date:** 2026-03-25
**End date:** 2026-03-25
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Campaign Orchestrator

**As a** Campaign Operator (Thomas)
**I want** to configure campaigns with templates, data sources, and filters
**So that** I can target specific recipients with the right content

**Specification context:**
> Create campaigns with template selection, data source, filters, free field values, and schedule.

**Acceptance criteria:**
- [x] Campaign creation UI with wizard flow ✅
- [x] Template selection (filtered by channel and status) ✅
- [x] Data source selection with filter builder ✅
- [x] Free field value input for operator-provided data ✅
- [x] Schedule configuration (immediate or future date/time) ✅
- [x] Campaign preview: estimated recipient count before execution ✅

**Technical tasks:**
- [x] `TASK-023-01` - **[Model]** Create Campaign entity with relationships ✅ 2026-03-25
- [x] `TASK-023-02` - **[Model]** Create CampaignStep entity for multi-step support ✅ 2026-03-25
- [x] `TASK-023-03` - **[API]** POST /api/campaigns endpoint ✅ 2026-03-25
- [x] `TASK-023-04` - **[API]** GET /api/campaigns with filtering and pagination ✅ 2026-03-25
- [x] `TASK-023-05` - **[Frontend]** Campaign creation wizard (5 steps) ✅ 2026-03-25
- [x] `TASK-023-06` - **[Frontend]** Template selector component ✅ 2026-03-25
- [x] `TASK-023-07` - **[Frontend]** Data source and filter selector ✅ 2026-03-25
- [x] `TASK-023-08` - **[Frontend]** Free field input form ✅ 2026-03-25
- [x] `TASK-023-09` - **[Frontend]** Schedule picker with validation ✅ 2026-03-25
- [x] `TASK-023-10` - **[Service]** Recipient count estimation service ✅ 2026-03-25
- [x] `TASK-023-11` - **[Test]** Campaign creation workflow tests ✅ 2026-03-25
- [x] `TASK-023-12` - **[Doc]** Campaign creation guide ✅ 2026-03-25

**Business rules:**
1. Only Published templates can be selected
2. Operator must provide values for all freeField placeholders
3. Scheduled campaigns must be at least 5 minutes in future
4. Campaign name must be unique

**Dependencies:** US-005, US-014, US-016
**Estimation:** 8-10 days

---

#### [US-024] - Multi-step campaign sequences
**Status:** ✅ DONE
**Start date:** 2026-03-25
**End date:** 2026-03-25
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Campaign Orchestrator

**As a** Campaign Operator (Thomas)
**I want** to define multi-step campaigns with delays
**So that** I can schedule initial send, email reminder, then SMS follow-up

**Specification context:**
> Define ordered steps with channel, delay (J+N days), and step-specific target filters (e.g., non-respondents only).

**Acceptance criteria:**
- [x] Campaign can have multiple steps (max 10) ✅
- [x] Each step has: channel, template, delay (days), optional filter ✅
- [x] Step order enforced and displayed visually ✅
- [x] Step-specific filters refine base campaign audience ✅
- [x] Delay calculated from campaign start or previous step completion ✅

**Technical tasks:**
- [x] `TASK-024-01` - **[Model]** Add StepOrder, DelayDays, StepFilter to CampaignStep ✅ 2026-03-25
- [x] `TASK-024-02` - **[Service]** Step validation service (order, delays) ✅ 2026-03-25
- [x] `TASK-024-03` - **[Service]** Step scheduling service (calculate execution dates) ✅ 2026-03-25
- [x] `TASK-024-04` - **[Frontend]** Multi-step builder UI component ✅ 2026-03-25
- [x] `TASK-024-05` - **[Frontend]** Step timeline visualization ✅ 2026-03-25
- [x] `TASK-024-06` - **[Frontend]** Step-specific filter builder ✅ 2026-03-25
- [x] `TASK-024-07` - **[Test]** Multi-step scheduling logic tests ✅ 2026-03-25
- [x] `TASK-024-08` - **[Test]** Step filter application tests ✅ 2026-03-25
- [x] `TASK-024-09` - **[Doc]** Multi-step campaign guide ✅ 2026-03-25

**Business rules:**
1. Step delay: 0 = immediate, positive integer = days after previous step
2. Step filters are AND-combined with base campaign filter
3. Each step can use different template (but same data source)
4. Example: Step 1 (Email, Day 0) â†’ Step 2 (Email reminder, Day 15) â†’ Step 3 (SMS, Day 20)

**Dependencies:** US-023
**Estimation:** 7-8 days

---

#### [US-025] - Template snapshot for campaign integrity
**Status:** ✅ DONE
**Start date:** 2026-03-25
**End date:** 2026-03-25
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Campaign Orchestrator

**As a** Campaign Operator (Thomas)
**I want** campaigns to freeze template content at scheduling time
**So that** template edits don't affect running campaigns

**Specification context:**
> Freeze template content at campaign scheduling time; all steps use the snapshot. Template snapshots guarantee campaign reproducibility.

**Acceptance criteria:**
- [x] Template content frozen when campaign is scheduled ✅
- [x] Snapshot includes resolved sub-templates ✅
- [x] All campaign steps reference same snapshot ✅
- [x] Snapshot stored in database for audit ✅
- [x] Campaign display shows snapshot version used ✅

**Technical tasks:**
- [x] `TASK-025-01` - **[Model]** Create TemplateSnapshot entity ✅ 2026-03-25
- [x] `TASK-025-02` - **[Service]** Snapshot creation service (resolve sub-templates) ✅ 2026-03-25
- [x] `TASK-025-03` - **[Service]** Link campaign to snapshot on scheduling ✅ 2026-03-25
- [x] `TASK-025-04` - **[API]** GET /api/campaigns/{id}/snapshot endpoint ✅ 2026-03-25
- [x] `TASK-025-05` - **[Frontend]** Snapshot view in campaign details ✅ 2026-03-25
- [x] `TASK-025-06` - **[Test]** Snapshot creation and isolation tests ✅ 2026-03-25
- [x] `TASK-025-07` - **[Doc]** Template snapshot documentation ✅ 2026-03-25

**Business rules:**
1. Snapshot created when campaign status changes to Scheduled
2. Snapshot never modified after creation
3. If template deleted, snapshot remains for audit
4. Snapshot includes all sub-templates (fully resolved)

**Dependencies:** US-023, US-007 (sub-templates)
**Estimation:** 4-5 days

---

#### [US-026] - Chunk-based batch processing with Hangfire
**Status:** ✅ DONE
**Start date:** 2026-03-25
**End date:** 2026-03-26
**Priority:** 🔴 High
**Complexity:** XL
**Epic:** Campaign Orchestrator

**As a** IT Administrator (Sophie)
**I want** scalable batch processing for large campaigns
**So that** 100K-recipient campaigns complete reliably in under 60 minutes

**Specification context:**
> Split recipients into chunks of 500, process via Hangfire workers in parallel, track completion atomically. Hangfire Community lacks batch primitives â€” use Chunk Coordinator pattern.

**Acceptance criteria:**
- [x] Recipients split into configurable chunks (default 500)
 ✅
- [x] Each chunk processed as separate Hangfire job
 ✅
- [x] Parallel processing across 4-8 workers
 ✅
- [x] Atomic chunk completion tracking
 ✅
- [x] Campaign completes when all chunks processed
 ✅
- [x] Progress visible in real-time (processed/total)
 ✅

**Technical tasks:**
- [x] `TASK-026-01` - **[Service]** Implement ChunkCoordinator service ✅ 2026-03-25
- [x] `TASK-026-02` - **[Service]** Recipient chunking algorithm ✅ 2026-03-25
- [x] `TASK-026-03` - **[Hangfire]** Configure Hangfire with SQL Server storage ✅ 2026-03-25
- [x] `TASK-026-04` - **[Hangfire]** Create ProcessChunkJob background job ✅ 2026-03-25
- [x] `TASK-026-05` - **[Service]** Atomic chunk completion counter (SQL UPDATE...OUTPUT) ✅ 2026-03-25
- [x] `TASK-026-06` - **[Service]** Campaign completion detection service ✅ 2026-03-25
- [x] `TASK-026-07` - **[Service]** Failed chunk retry orchestrator ✅ 2026-03-25
- [x] `TASK-026-08` - **[Frontend]** Hangfire dashboard integration ✅ 2026-03-25
- [x] `TASK-026-09` - **[Test]** Chunk processing integration tests ✅ 2026-03-26
- [x] `TASK-026-10` - **[Test]** Performance test (100K recipients in <60 min) ✅ 2026-03-26
- [x] `TASK-026-11` - **[Test]** Failure recovery tests ✅ 2026-03-26
- [x] `TASK-026-12` - **[Doc]** Batch processing architecture guide ✅ 2026-03-26

**Business rules:**
1. Chunk size configurable (default 500)
2. Worker count configurable (default 8)
3. Each chunk job auto-retries 3 times on failure
4. Campaign status: Running â†’ StepInProgress â†’ Completed/PartialFailure
5. Hangfire dashboard accessible only to Admin role

**Dependencies:** US-023, US-015
**Estimation:** 12-15 days

**Implementation notes:**
- Critical performance target: 100K recipients in <60 minutes
- Chunk Coordinator pattern required due to Hangfire Community limitations
- Requires POC/validation of atomic counter approach

---

#### [US-027] - Campaign status tracking and monitoring
**Status:** ✅ DONE
**Start date:** 2026-03-27 00:00 UTC
**End date:** 2026-03-27 23:59 UTC
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Campaign Orchestrator

**As a** Campaign Operator (Thomas)
**I want** real-time campaign status visibility
**So that** I know what's happening at all times

**Specification context:**
> Real-time progress: Draft â†’ Scheduled â†’ Running â†’ StepInProgress â†’ Completed / PartialFailure / ManualReview.

**Acceptance criteria:**
- [x] Campaign status enum with all lifecycle states ✅
- [x] Real-time progress tracking (processed/total counts) ✅
- [x] Status transitions logged with timestamps ✅
- [x] Dashboard showing active campaigns and progress ✅
- [x] Failed send count visible per campaign ✅

**Technical tasks:**
- [x] `TASK-027-01` - **[Model]** Add Status enum to Campaign entity ✅ 2026-03-27
- [x] `TASK-027-02` - **[Model]** Add progress counters (total, processed, failed) ✅ 2026-03-27
- [x] `TASK-027-03` - **[Service]** Status transition service with validation ✅ 2026-03-27
- [x] `TASK-027-04` - **[API]** GET /api/campaigns/{id}/status endpoint (real-time) ✅ 2026-03-27
- [x] `TASK-027-05` - **[Frontend]** Campaign dashboard with status cards ✅ 2026-03-27
- [x] `TASK-027-06` - **[Frontend]** Progress bar component with live updates ✅ 2026-03-27
- [x] `TASK-027-07` - **[Frontend]** Campaign detail view with status history ✅ 2026-03-27
- [x] `TASK-027-08` - **[Test]** Status transition validation tests ✅ 2026-03-27
- [x] `TASK-027-09` - **[Doc]** Campaign status lifecycle guide ✅ 2026-03-27

**Business rules:**
1. Status flow: Draft â†’ Scheduled â†’ Running â†’ StepInProgress â†’ Completed
2. PartialFailure if >2% sends failed
3. ManualReview if >10% sends failed (requires Admin intervention)
4. Progress updated after each chunk completion

**Dependencies:** US-023, US-026
**Estimation:** 5-6 days

---

#### [US-028] - Static and dynamic attachment management
**Status:** ✅ DONE
**Start date:** 2026-03-28
**End date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** M
**Epic:** Campaign Orchestrator

**As a** Campaign Operator (Thomas)
**I want** to attach documents to campaign sends
**So that** recipients receive reference materials or personal documents

**Specification context:**
> Static attachments: operator uploads common files attached to all sends. Dynamic attachments: per-recipient file path from data source field.

**Acceptance criteria:**
- [x] Static attachments uploaded at campaign creation ✅
- [x] Dynamic attachments specified via data source field mapping
- [x] Attachment validation (type whitelist, size limits)
- [x] Missing dynamic attachments logged but don't fail send
- [x] Attachments stored on file share with DB metadata

**Technical tasks:**
- [x] `TASK-028-01` - **[Model]** Create Attachment entity
- [x] `TASK-028-02` - **[Service]** File upload service (to UNC path)
- [x] `TASK-028-03` - **[Service]** Attachment validation (type, size)
- [x] `TASK-028-04` - **[Service]** Dynamic attachment resolver (from data field)
- [x] `TASK-028-05` - **[API]** POST /api/campaigns/{id}/attachments endpoint
- [x] `TASK-028-06` - **[Frontend]** Static attachment uploader component
- [x] `TASK-028-07` - **[Frontend]** Dynamic attachment field mapper
- [x] `TASK-028-08` - **[Test]** Attachment validation tests
- [x] `TASK-028-09` - **[Test]** Missing dynamic attachment handling tests
- [x] `TASK-028-10` - **[Doc]** Attachment management guide

**Business rules:**
1. Whitelist: PDF, DOCX, XLSX, PNG, JPG
2. Size limits: 10 MB per file, 25 MB total per send
3. Static attachments stored at campaign creation
4. Dynamic attachments resolved at send time
5. Missing dynamic attachments: log warning, send without attachment

**Dependencies:** US-023, US-019 (email dispatcher)
**Estimation:** 5-6 days

**Implementation notes:**
- Open Question Q7: File share infrastructure (UNC vs NAS vs S3)

---

#### [US-029] - CC and BCC management
**Status:** ✅ DONE
**Start date:** 2026-03-28
**End date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Campaign Orchestrator

**As a** Campaign Operator (Thomas)
**I want** to CC stakeholders on campaign emails
**So that** relevant parties receive copies

**Specification context:**
> Static CC (operator-defined) + dynamic CC (from data source field); deduplicated, validated.

**Acceptance criteria:**
- [x] Static CC addresses configured at campaign creation ✅
- [x] Dynamic CC addresses from data source field ✅
- [x] Email validation for all CC addresses ✅
- [x] Deduplication (same address only receives one copy) ✅
- [x] BCC support for hidden copies ✅

**Technical tasks:**
- [x] `TASK-029-01` - **[Model]** Add StaticCC and DynamicCCField to Campaign ✅ 2026-03-28
- [x] `TASK-029-02` - **[Service]** Email validation service ✅ 2026-03-28
- [x] `TASK-029-03` - **[Service]** CC deduplication service ✅ 2026-03-28
- [x] `TASK-029-04` - **[Frontend]** CC/BCC configuration UI ✅ 2026-03-28
- [x] `TASK-029-05` - **[Test]** Email validation tests ✅ 2026-03-28
- [x] `TASK-029-06` - **[Test]** Deduplication tests ✅ 2026-03-28
- [x] `TASK-029-07` - **[Doc]** CC management guide ✅ 2026-03-28

**Business rules:**
1. Static CC: comma-separated email list
2. Dynamic CC: data field containing email or semicolon-separated list
3. Invalid emails logged but don't fail send
4. Max 10 CC recipients per send

**Dependencies:** US-023, US-019
**Estimation:** 3-4 days

---

### Epic 7: Generic Send API

> REST API for transactional message sending

#### [US-030] - Single send API endpoint
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Generic Send API

**As a** Integration Developer (Julien)
**I want** a simple API to send single messages
**So that** any internal app can trigger communications

**Specification context:**
> POST /api/send with templateId, channel, data dictionary, recipient. API-first design for integration consumers.

**Acceptance criteria:**
- [x] POST /api/send endpoint accepts template, channel, data, recipient âœ…
- [x] Template resolved with provided data âœ…
- [x] Message dispatched immediately (synchronous) âœ…
- [x] Response includes send status and tracking ID âœ…
- [x] Error responses with clear validation messages âœ…

**Technical tasks:**
- [x] `TASK-030-01` - **[Model]** Create SendRequest DTO âœ… 2026-03-19
- [x] `TASK-030-02` - **[API]** POST /api/send endpoint âœ… 2026-03-19
- [x] `TASK-030-03` - **[Service]** Single send orchestration service âœ… 2026-03-19
- [x] `TASK-030-04` - **[Service]** Request validation service âœ… 2026-03-19
- [x] `TASK-030-05` - **[API]** Response model with tracking ID âœ… 2026-03-19
- [x] `TASK-030-06` - **[Test]** API integration tests âœ… 2026-03-19
- [x] `TASK-030-07` - **[Test]** Validation tests (missing data, invalid template) âœ… 2026-03-19
- [x] `TASK-030-08` - **[Doc]** API endpoint documentation âœ… 2026-03-19

**Business rules:**
1. Template must be Published
2. All required placeholders must have values in data dictionary
3. Recipient email/phone validated based on channel
4. Response time target: < 500ms at p95
5. Tracking ID returned for status lookup

**Dependencies:** US-011, US-018
**Estimation:** 5-6 days

---

#### [US-031] - API authentication and authorization
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Generic Send API

**As a** IT Administrator (Sophie)
**I want** API authentication via API keys or OAuth2
**So that** only authorized systems can send messages

**Specification context:**
> API key or OAuth2 client credentials for external consumers. HTTPS only. Security critical.

**Acceptance criteria:**
- [x] API key authentication mechanism ✅
- [x] API key management UI (create, revoke, rotate) ✅
- [ ] OAuth2 client credentials flow support (optional)
- [x] HTTPS enforcement for all API calls ✅
- [x] Rate limiting per API key ✅

**Technical tasks:**
- [x] `TASK-031-01` - **[Model]** Create ApiKey entity ✅ 2026-03-20
- [x] `TASK-031-02` - **[Auth]** Implement API key authentication middleware ✅ 2026-03-20
- [x] `TASK-031-03` - **[Service]** API key generation and hashing service ✅ 2026-03-20
- [x] `TASK-031-04` - **[API]** API key management endpoints (Admin only) ✅ 2026-03-20
- [x] `TASK-031-05` - **[Frontend]** API key management UI ✅ 2026-03-20
- [x] `TASK-031-06` - **[Config]** HTTPS enforcement configuration ✅ 2026-03-20
- [x] `TASK-031-07` - **[Test]** Authentication tests ✅ 2026-03-20
- [x] `TASK-031-08` - **[Doc]** API authentication guide ✅ 2026-03-20

**Business rules:**
1. API keys hashed in database (bcrypt)
2. API keys can be scoped to specific templates or channels
3. API keys expire after 1 year (configurable)
4. HTTPS required for all API endpoints
5. Rate limiting: 1000 requests/minute per key (configurable)

**Dependencies:** US-030
**Estimation:** 5-6 days

---

#### [US-032] - OpenAPI/Swagger documentation
**Status:** 🔵 IN PROGRESS
**Start date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Generic Send API

**As a** Integration Developer (Julien)
**I want** auto-generated API documentation
**So that** I can integrate without guessing

**Specification context:**
> Auto-generated Swagger/OpenAPI spec. Essential for developer experience.

**Acceptance criteria:**
- [ ] Swagger UI accessible at /swagger
- [ ] All API endpoints documented with parameters and responses
- [ ] Authentication documented (API key header)
- [ ] Example requests and responses
- [ ] Downloadable OpenAPI JSON/YAML spec

**Technical tasks:**
- [ ] `TASK-032-01` - **[Config]** Install and configure Swashbuckle.AspNetCore
- [ ] `TASK-032-02` - **[API]** Add XML comments to controllers
- [ ] `TASK-032-03` - **[API]** Add example attributes to DTOs
- [ ] `TASK-032-04` - **[Config]** Configure Swagger UI theme
- [ ] `TASK-032-05` - **[Test]** Verify OpenAPI spec validity
- [ ] `TASK-032-06` - **[Doc]** Swagger usage guide

**Business rules:**
1. Swagger UI accessible only in non-production environments (or with authentication)
2. OpenAPI spec version 3.0
3. All DTOs include example values
4. Authentication scheme documented

**Dependencies:** US-030, US-031
**Estimation:** 2-3 days

---

#### [US-033] - API rate limiting per consumer
**Status:** 🟡 TODO
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Generic Send API

**As a** IT Administrator (Sophie)
**I want** rate limiting per API consumer
**So that** no single consumer can overwhelm the system

**Specification context:**
> Configurable rate limits per API key. Prevent abuse and ensure fair resource allocation.

**Acceptance criteria:**
- [ ] Rate limits configurable per API key
- [ ] Default rate limit applied to new API keys
- [ ] 429 Too Many Requests response when limit exceeded
- [ ] Rate limit headers in responses (X-RateLimit-*)
- [ ] Rate limit monitoring and alerting

**Technical tasks:**
- [ ] `TASK-033-01` - **[Service]** Implement API rate limiter (per key)
- [ ] `TASK-033-02` - **[Middleware]** Rate limiting middleware
- [ ] `TASK-033-03` - **[API]** Add rate limit headers to responses
- [ ] `TASK-033-04` - **[Frontend]** Rate limit configuration in API key UI
- [ ] `TASK-033-05` - **[Test]** Rate limiting tests
- [ ] `TASK-033-06` - **[Doc]** Rate limiting documentation

**Business rules:**
1. Default: 1000 requests/minute per key
2. Rate limit window: sliding 1-minute window
3. Rate limit headers: X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset
4. Exceeded requests return 429 with Retry-After header

**Dependencies:** US-031
**Estimation:** 3-4 days

---

### Epic 8: Tracking & Audit

> Log all send operations and provide retry mechanisms

#### [US-034] - Send logging and audit trail
**Status:** ✅ DONE
**Start date:** 2026-03-19
**End date:** 2026-03-19
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Tracking & Audit

**As a** Campaign Operator (Thomas)
**I want** detailed logs for every send attempt
**So that** I can investigate delivery issues

**Specification context:**
> Every send attempt logged with status (Pending/Sent/Failed/Retrying), timestamp, error detail, retry count. SEND_LOG as source of truth.

**Acceptance criteria:**
- [x] Every send logged to SEND_LOG table âœ…
- [x] Send status: Pending, Sent, Failed, Retrying âœ…
- [x] Timestamp, error message, retry count captured âœ…
- [x] Correlation to campaign and recipient âœ…
- [x] Query interface for send log lookup âœ…

**Technical tasks:**
- [x] `TASK-034-01` - **[Model]** Create SendLog entity âœ… 2026-03-19
- [x] `TASK-034-02` - **[Service]** Send logging service âœ… 2026-03-19
- [x] `TASK-034-03` - **[Service]** Log on every send attempt (before and after) âœ… 2026-03-19
- [x] `TASK-034-04` - **[API]** GET /api/sendlogs with filtering âœ… 2026-03-19
- [x] `TASK-034-05` - **[Frontend]** Send log viewer UI âœ… 2026-03-19
- [x] `TASK-034-06` - **[Frontend]** Filter by campaign, recipient, status, date âœ… 2026-03-19
- [x] `TASK-034-07` - **[Test]** Logging completeness tests âœ… 2026-03-19
- [x] `TASK-034-08` - **[Doc]** Send log schema documentation âœ… 2026-03-19

**Business rules:**
1. All sends logged before dispatch attempt
2. Status updated after dispatch result
3. Error details captured in ErrorDetail field
4. Retention: 90 days (configurable)

**Dependencies:** US-018
**Estimation:** 5-6 days

---

#### [US-035] - Retry mechanism with exponential backoff
**Status:** ✅ DONE
**Start date:** 2026-03-27
**End date:** 2026-03-27

**Priority:** 🔴 High
**Complexity:** M
**Epic:** Tracking & Audit

**As a** IT Administrator (Sophie)
**I want** automatic retry for transient failures
**So that** delivery issues are recovered without manual intervention

**Specification context:**
> Configurable exponential backoff (30s / 2min / 10min, 3 attempts) per send; chunk-level retry via Hangfire AutomaticRetry.

**Acceptance criteria:**
- [x] Failed sends automatically retried up to 3 times ✅
- [x] Exponential backoff: 30s, 2min, 10min ✅
- [x] Retry count tracked in SendLog ✅
- [x] Transient vs permanent failure detection ✅
- [x] Chunk-level retry via Hangfire [AutomaticRetry] attribute ✅

**Technical tasks:**
- [x] `TASK-035-01` - **[Service]** Implement retry policy with exponential backoff ✅ 2026-03-27
- [x] `TASK-035-02` - **[Service]** Transient failure detection (SMTP, SMS errors) ✅ 2026-03-27
- [x] `TASK-035-03` - **[Hangfire]** Configure AutomaticRetry attribute on chunk jobs ✅ 2026-03-27
- [x] `TASK-035-04` - **[Service]** Retry count tracking in SendLog ✅ 2026-03-27
- [x] `TASK-035-05` - **[Test]** Retry logic tests ✅ 2026-03-27
- [x] `TASK-035-06` - **[Test]** Exponential backoff timing tests ✅ 2026-03-27
- [x] `TASK-035-07` - **[Doc]** Retry policy documentation ✅ 2026-03-27

**Business rules:**
1. Max 3 retry attempts per send
2. Backoff: 30s, 2min, 10min
3. Transient errors retried (SMTP connection timeout, rate limit)
4. Permanent errors not retried (invalid email, template error)
5. Retry success target: >90% within 3 attempts

**Dependencies:** US-034, US-026 (Hangfire)
**Estimation:** 4-5 days

---

### Epic 9: Reporting & Dashboards (Phase 2)

> Monitoring and analytics for campaigns

#### [US-036] - Campaign progress dashboard
**Status:** 🟡 TODO
**Priority:** 🟠 Medium
**Complexity:** M
**Epic:** Reporting & Dashboards

**As a** Campaign Operator (Thomas)
**I want** an aggregate view of campaign progress
**So that** I can monitor all campaigns in real time

**Specification context:**
> Aggregate view: total recipients, processed count, success/failure breakdown per step.

**Acceptance criteria:**
- [ ] Dashboard shows all active campaigns
- [ ] Per-campaign metrics: total, processed, sent, failed counts
- [ ] Progress percentage and estimated completion time
- [ ] Multi-step campaign timeline visualization
- [ ] Auto-refresh every 10 seconds

**Technical tasks:**
- [ ] `TASK-036-01` - **[API]** GET /api/campaigns/dashboard endpoint
- [ ] `TASK-036-02` - **[Service]** Aggregate metrics calculation service
- [ ] `TASK-036-03` - **[Frontend]** Dashboard page with campaign cards
- [ ] `TASK-036-04` - **[Frontend]** Real-time update via SignalR or polling
- [ ] `TASK-036-05` - **[Frontend]** Timeline visualization for multi-step
- [ ] `TASK-036-06` - **[Test]** Dashboard metrics tests
- [ ] `TASK-036-07` - **[Doc]** Dashboard user guide

**Business rules:**
1. Dashboard shows campaigns in Running or StepInProgress status
2. Metrics updated after each chunk completion
3. Estimated completion based on current send rate
4. Filters: by status, date range, operator

**Dependencies:** US-027, US-034
**Estimation:** 5-6 days

---

### Epic 10: Technical Debt & Maintenance
> Ongoing technical improvements, refactoring, infrastructure work, and maintenance tasks that keep the codebase healthy and the team productive.

#### [US-037] - Fix build warnings: Scriban vulnerabilities and compiler warnings
**Status:** ✅ DONE
**Start date:** 2026-03-20
**End date:** 2026-03-20
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 10
**Type:** [Bug]

**As a** developer
**I want to** fix all current build warnings across Infrastructure and Web projects
**So that** no known security vulnerabilities remain active and the codebase compiles cleanly

**Context:**
> Build output reports 3 Scriban CVEs (1 moderate, 2 high severity), 2 CS8604 null-ref warnings in SingleSendService, 1 CS1998 spurious-async warning in PdfConsolidationService, and 2 CS0108 member-hiding warnings in Web page models.

**Acceptance criteria:**
- [x] Scriban upgraded to a version with no known CVEs (NU1902/NU1903 warnings gone) ✅
- [x] CS8604 null-ref warnings in SingleSendService.cs resolved (lines 197 and 215) ✅
- [x] CS1998 spurious-async warning in PdfConsolidationService.cs resolved (line 36) ✅
- [x] CS0108 member-hiding warnings in SendLogsIndexModel and TemplatesIndexModel resolved ✅
- [x] Build completes with 0 warnings ✅

**Technical tasks:**
- [x] `TASK-037-01` - **[TechDebt]** Upgrade Scriban package to latest non-vulnerable version in CampaignEngine.Infrastructure.csproj ✅ 2026-03-20
- [x] `TASK-037-02` - **[Fix]** Resolve CS8604: add null-guard or null-forgiving operator for `args` in SingleSendService.cs lines 197 & 215 ✅ 2026-03-20
- [x] `TASK-037-03` - **[Fix]** Resolve CS1998: remove `async` keyword or add meaningful await in PdfConsolidationService.cs line 36 ✅ 2026-03-20
- [x] `TASK-037-04` - **[Fix]** Resolve CS0108: add `new` keyword to `Page` property in SendLogsIndexModel and TemplatesIndexModel, or rename to avoid hiding ✅ 2026-03-20
- [x] `TASK-037-05` - **[Test]** Verify build output is warning-free after all fixes ✅ 2026-03-20

**Dependencies:** None
**Estimation:** 1-2 days

---

## ðŸ“‹ Suggested Roadmap

### Phase 1 â€” MVP (Core Engine) â€” 12-16 weeks

**Sprint 1 â€” Foundation (Weeks 1-3)**
- US-001: Project scaffold and architecture
- US-002: Database and EF Core setup
- US-003: Authentication and authorization
- US-004: Structured logging
- US-014: Data source declaration

**Sprint 2 â€” Template Engine (Weeks 4-6)**
- US-005: Template CRUD
- US-006: Placeholder manifest
- US-007: Sub-template composition
- US-011: Scriban integration
- US-012: Advanced rendering (tables, lists, conditionals)

**Sprint 3 â€” Data & Dispatch (Weeks 7-9)**
- US-015: SQL Server connector
- US-016: Filter expression builder
- US-013: Channel post-processing (POC PDF tool first!)
- US-018: Channel dispatcher abstraction
- US-019: Email dispatcher

**Sprint 4 â€” Campaign Core (Weeks 10-12)**
- US-020: SMS dispatcher
- US-021: PDF letter dispatcher
- US-023: Campaign creation
- US-025: Template snapshots
- US-034: Send logging

**Sprint 5 â€” Batch Processing (Weeks 13-14)**
- US-026: Chunk-based batch processing (critical path)
- US-027: Campaign status tracking
- US-035: Retry mechanism

**Sprint 6 â€” API & Polish (Weeks 15-16)**
- US-024: Multi-step sequences
- US-030: Single send API
- US-031: API authentication
- US-010: Template preview
- Integration testing and performance validation

### Phase 2 â€” Operational Excellence (8-10 weeks after Phase 1)

**Sprint 7 â€” Enhanced Features (Weeks 17-19)**
- US-008: Template versioning
- US-009: Template lifecycle workflow
- US-028: Attachment management
- US-029: CC management
- US-022: Channel throttling

**Sprint 8 â€” Developer Experience (Weeks 20-21)**
- US-032: OpenAPI documentation
- US-033: API rate limiting
- US-036: Campaign progress dashboard
- US-017: REST API connector (if needed)

**Sprint 9 â€” Stabilization & Training (Weeks 22-24)**
- Performance optimization
- User training and documentation
- Production deployment
- Monitoring and alerting setup

### Phase 3 â€” Advanced Features (10-12 weeks after Phase 2)

**Future Epics (Out of Scope v1):**
- Visual template editor (WYSIWYG or MJML)
- Email tracking (opens, clicks)
- Webhook notifications
- Additional channels (WhatsApp, Push)
- Advanced analytics and reporting
- A/B testing
- Unsubscribe management

---

## âš ï¸ Risks & Constraints

### Identified Risks

1. **PDF Generation Tool Selection (High Impact)**
   - **Risk:** No PDF tool chosen yet (wkhtmltopdf vs DinkToPdf vs Puppeteer)
   - **Mitigation:** US-013 TASK-001 POC required before Sprint 3
   - **Blocker for:** US-021 (Letter dispatcher)

2. **SMTP Server Throttling (Medium Impact)**
   - **Risk:** High-volume sends may trigger blacklisting
   - **Mitigation:** US-022 throttling implementation + Q3 resolution
   - **Monitoring:** Send rate metrics in observability

3. **Hangfire Community Batch Limitations (Medium Impact)**
   - **Risk:** No built-in batch primitives requires custom Chunk Coordinator
   - **Mitigation:** US-026 Chunk Coordinator pattern (validated in architecture study)
   - **Performance target:** 100K recipients in <60 minutes

4. **Email Rendering Inconsistencies (Medium Impact)**
   - **Risk:** Outlook/Gmail/Apple Mail render HTML differently
   - **Mitigation:** PreMailer.Net CSS inlining + test on major clients
   - **Future:** Consider MJML in Phase 3

5. **Data Source Connector Failures Mid-Campaign (High Impact)**
   - **Risk:** Database connection loss during batch processing
   - **Mitigation:** Chunk-level retry (3 attempts) + PartialFailure status
   - **Manual intervention:** ManualReview status for >10% failure rate

6. **File Share Accessibility (Low Impact)**
   - **Risk:** UNC path inaccessible from Hangfire workers
   - **Mitigation:** Validate at campaign scheduling time + Q7 resolution
   - **Warning:** Missing dynamic attachments logged, send continues

7. **XSS via Template Data (Low Impact)**
   - **Risk:** Unescaped user data in templates
   - **Mitigation:** Scriban auto-escapes all substituted values
   - **Trust:** Template HTML itself trusted (Designer role only)

### Technical Constraints

1. **Windows Server + IIS hosting** (infrastructure standard)
2. **SQL Server database** (team expertise)
3. **Hangfire Community** (no Pro license â€” requires Chunk Coordinator pattern)
4. **File share for attachments** (UNC path or NAS â€” Q7 pending)
5. **Performance target:** 100K recipients in <60 minutes (8 workers)
6. **API response time:** <500ms p95 for single sends

### Open Questions Requiring Resolution

| # | Question | Blocks User Story | Target Resolution |
|---|----------|-------------------|-------------------|
| Q1 | PDF generation tool POC | US-013, US-021 | Sprint 2 end |
| Q2 | SMS provider contract | US-020 | Sprint 3 start |
| Q3 | SMTP server limits | US-022 | Sprint 6 start |
| Q4 | Windows Auth vs Identity | US-003 | Sprint 1 start |
| Q5 | Print provider format | US-021 | Sprint 4 start |
| Q7 | File share infrastructure | US-028 | Sprint 7 start |
| Q8 | REST API data sources needed? | US-017 | Sprint 8 start |

---

## ðŸ“š References

- **Source specification:** _docs/prd.md
- **Analysis date:** 2026-03-19
- **PRD version:** 1.0 (Draft)
- **Project directory:** f:\_Projets\AdobeCampaignLike

---

#### [US-038] - Upgrade Scriban to resolve high severity CVE GHSA-c875-h985-hvrc
**Status:** ✅ DONE
**Start date:** 2026-03-26 00:00:00
**End date:** 2026-03-26 00:00:00
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 10
**Type:** [Bug]

**As a** developer
**I want to** upgrade the Scriban NuGet package past version 6.6.0
**So that** the known high severity vulnerability GHSA-c875-h985-hvrc is eliminated and the build emits no NU1903 security warnings

**Context:**
> The Infrastructure project still references Scriban 6.6.0 which carries a high severity advisory (GHSA-c875-h985-hvrc). A previous fix (US-037) was supposed to address Scriban CVEs but the warning persists, indicating the package was not actually upgraded or has since regressed.

**Acceptance criteria:**
- [x] Scriban package in CampaignEngine.Infrastructure.csproj is upgraded to the latest version that does not carry GHSA-c875-h985-hvrc ✅
- [x] NU1903 warning for Scriban disappears from the build output ✅
- [x] All existing Scriban-based rendering tests pass after the upgrade ✅
- [x] Build completes with 0 security audit warnings related to Scriban ✅

**Technical tasks:**
- [x] `TASK-038-01` - **[Debug]** Verify current Scriban version in CampaignEngine.Infrastructure.csproj and confirm NU1903 warning reproduces on a clean build ✅ 2026-03-26
- [x] `TASK-038-02` - **[TechDebt]** Upgrade Scriban to the latest patched version (check NuGet advisories for GHSA-c875-h985-hvrc fix version) ✅ 2026-03-26
- [x] `TASK-038-03` - **[Test]** Run the full test suite to confirm no rendering regressions after the upgrade ✅ 2026-03-26
- [x] `TASK-038-04` - **[Fix]** Verify build output is warning-free (0 NU1902/NU1903 Scriban warnings) ✅ 2026-03-26

**Dependencies:** None
**Estimation:** 1-2 days

---

#### [US-039] - Fix CS8604 null-reference warnings in IAppLogger calls
**Status:** ✅ DONE
**Start date:** 2026-03-28
**End date:** 2026-03-28
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Epic 10
**Type:** [Bug]

**As a** developer
**I want to** fix all CS8604 null-reference warnings in IAppLogger.LogInformation() and LogWarning() calls
**So that** the solution compiles cleanly with zero nullable-reference warnings and logger arguments are guaranteed non-null at every call site

**Context:**
> CS8604 warnings reveal that potentially null variables are passed as `params object[] args` to `IAppLogger` methods in four locations (ApiKeyService.cs:85, CampaignService.cs:148, CampaignService.cs:197, LoggingDispatchOrchestrator.cs:110). These warnings were introduced after the US-037 pass which only covered SingleSendService.cs.

**Acceptance criteria:**
- [x] CS8604 warning at ApiKeyService.cs:85 resolved with null-safe argument ✅
- [x] CS8604 warnings at CampaignService.cs:148 and :197 resolved with null-safe arguments ✅
- [x] CS8604 warning at LoggingDispatchOrchestrator.cs:110 resolved with null-safe argument ✅
- [x] Full solution build emits zero CS8604 warnings ✅
- [x] All existing unit and integration tests pass unchanged after the fix ✅

**Technical tasks:**
- [x] `TASK-039-01` - **[Debug]** Audit all four warning locations — identify the nullable variables being passed as logger args and the safest null-safe technique for each context ✅ 2026-03-28
- [x] `TASK-039-02` - **[Fix]** Resolve CS8604 in ApiKeyService.cs:85 using null-coalescing operator or explicit `ToString()` call ✅ 2026-03-28
- [x] `TASK-039-03` - **[Fix]** Resolve CS8604 in CampaignService.cs:148 and :197 using null-coalescing or null-conditional string conversion ✅ 2026-03-28
- [x] `TASK-039-04` - **[Fix]** Resolve CS8604 in LoggingDispatchOrchestrator.cs:110 using null-coalescing or null-conditional string conversion ✅ 2026-03-28
- [x] `TASK-039-05` - **[Test]** Verify build output is warning-free (`dotnet build` with no warnings) and all tests pass ✅ 2026-03-28

**Dependencies:** None
**Estimation:** 1-2 days

---

## ðŸš€ Next Steps

1. **Review priorities:** Open [.userstories/BACKLOG.md](.userstories/BACKLOG.md) and adjust story priorities
2. **Resolve Open Questions:** Focus on Q1 (PDF tool) and Q4 (Auth) before Sprint 1
3. **Validate estimations:** Review complexity ratings with team
4. **Start Sprint 1:** Run `/do-userstory US-001` to begin implementation

## ðŸ’¡ Useful Commands

- `/list-userstories` - View all user stories with status
- `/do-userstory US-XXX` - Implement a specific user story
- `/show-progress` - Visual progress report with dependency graph
- `/refine-userstory US-XXX` - Analyze and improve a user story
- `/orchestrate-backlog` - Autonomous implementation of selected features
- `/export-backlog` - Export to PDF, Markdown, HTML, or JSON

---

**Legend:**
- 🔴 High Priority (MVP â€” Phase 1)
- 🟠 Medium Priority (Phase 2)
- 🟢 Low Priority (Phase 3 / Future)
- 🟡 TODO | 🔵 IN PROGRESS | ✅ DONE | ❌ BLOCKED



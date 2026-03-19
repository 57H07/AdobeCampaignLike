# PRD: CampaignEngine — Multichannel Campaign Management Platform

> Version: 1.0 — Generated 2026-03-19
> Status: Draft

---

## 1. Executive Summary

**CampaignEngine** is an internal multichannel campaign management platform (email, SMS, paper mail) designed for organizations that need to send templated, data-driven communications at scale. It serves two distinct user profiles — Designers who create and manage message templates, and Operators who configure and execute campaigns against business data repositories. The platform exposes a generic Send API for external system integration, making it both a standalone campaign tool and a reusable messaging service for the broader IT ecosystem. Built on .NET 8, SQL Server, and Hangfire, it prioritizes extensibility, traceability, and operational simplicity over cloud-native complexity.

---

## 2. Problem Statement

- **Current pain points:** Organizations relying on Adobe Campaign or similar tools face vendor lock-in, licensing costs, and rigid architectures that make customization expensive. Internal teams often resort to ad-hoc scripts, manual Excel-to-mail merges, or disconnected tools for different channels (one for email, another for SMS, manual PDF generation for letters).
- **Why existing solutions are insufficient:** Commercial platforms (Adobe Campaign, Salesforce Marketing Cloud) are over-engineered for internal use cases, expensive, and impose their own data models. Open-source alternatives (Mautic, Listmonk) lack multichannel support (especially paper mail/PDF) and enterprise-grade template management with dynamic tables, conditional content, and sub-template composition.
- **Opportunity:** A lightweight, API-first campaign engine built on a familiar .NET/SQL Server stack that any internal system can consume, with native support for email, SMS, and PDF letter generation — all driven by generic, schema-agnostic data sources.

---

## 3. Target Users & Personas

**Persona 1 — Marie, Template Designer**
- **Role:** Communication/marketing team member with HTML/CSS skills
- **Goals:** Create polished, brand-compliant templates with dynamic content (tables, lists, conditional blocks); preview templates with real sample data; manage template versioning
- **Frustrations:** Current tools require developer intervention for template changes; no preview capability; inconsistent rendering across email clients
- **Key behaviors:** Iterates frequently on template design; needs a visual editor; works with sub-templates (headers, footers, legal blocks)

**Persona 2 — Thomas, Campaign Operator**
- **Role:** Operations/business team member responsible for customer communications
- **Goals:** Launch targeted campaigns quickly; define multi-step sequences (initial send, email reminder at J+15, SMS at J+20); monitor campaign progress in real time
- **Frustrations:** Manual data extraction and mail merge; no visibility into send failures; no retry mechanism; CC and attachment management is manual
- **Key behaviors:** Configures campaigns weekly; applies filters on data repositories; attaches per-recipient documents; needs clear status dashboards

**Persona 3 — Julien, Integration Developer**
- **Role:** Backend developer on internal business applications
- **Goals:** Trigger transactional messages (confirmations, notifications) from any internal system via a simple API call; use existing templates without duplicating rendering logic
- **Frustrations:** Each application has its own email-sending code; no centralized template management; no audit trail
- **Key behaviors:** Calls REST APIs; expects OpenAPI documentation; needs reliable delivery with status tracking

**Persona 4 — Sophie, IT Administrator**
- **Role:** Infrastructure/ops team member
- **Goals:** Monitor system health; manage user roles and permissions; configure SMTP servers, SMS providers, and file share access
- **Frustrations:** No centralized audit log; unclear separation of duties between designers and operators
- **Key behaviors:** Reviews audit logs; manages access control; monitors Hangfire dashboard

---

## 4. Goals & Success Criteria

| KPI | Target | Timeframe |
|-----|--------|-----------|
| Campaign execution capacity | 100,000 recipients per campaign within 60 minutes | MVP |
| API Send response time | < 500ms at p95 for single transactional sends | MVP |
| Template rendering accuracy | 100% placeholder resolution (zero unresolved `{{...}}` in production sends) | MVP |
| Send delivery rate | > 98% successful delivery (excluding invalid recipient addresses) | MVP |
| Retry success rate | > 90% of transient failures recovered within 3 retry attempts | MVP |
| Designer template creation time | < 30 minutes for a standard template with dynamic table and conditional blocks | Phase 2 |

---

## 5. Features & Requirements

### 5.1 Functional Requirements

#### Epic 1 — Template Management (Template Registry)

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Template CRUD | Create, read, update, delete templates with name, channel (Email/Letter/SMS), HTML body, and placeholder manifest | As Marie (Designer), I want to create and edit templates so that I can define reusable message layouts | 🔴 Must-have |
| Sub-template composition | Support reusable sub-templates (header, footer, signature blocks) that can be embedded in parent templates | As Marie, I want to compose templates from reusable blocks so that I maintain brand consistency | 🔴 Must-have |
| Placeholder manifest | Typed placeholder declarations (scalar, table, list, freeField) with source indication (datasource vs operator input) | As Marie, I want to declare expected data fields so that operators know what data is required | 🔴 Must-have |
| Template versioning | Auto-increment version on each update; maintain version history | As Marie, I want version history so that I can track changes and revert if needed | 🟠 Should-have |
| Template preview | Resolve a template with sample data from a real data source (read-only) | As Marie, I want to preview my template with real data so that I can verify rendering before publication | 🔴 Must-have |
| Template lifecycle workflow | Status management: Draft → Published → Archived | As Sophie (Admin), I want template governance so that incomplete templates cannot be used in production | 🟠 Should-have |

#### Epic 2 — Rendering Engine

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Scalar substitution | Replace `{{key}}` placeholders with data values | As Julien (Developer), I want reliable placeholder substitution so that messages contain correct personalized data | 🔴 Must-have |
| Table rendering | Generate HTML tables from `{{#table}}...{{/table}}` blocks with row iteration | As Marie, I want dynamic tables in templates so that I can display line-item data (contracts, invoices) | 🔴 Must-have |
| List rendering | Generate bulleted/numbered lists from `{{#list}}...{{/list}}` blocks | As Marie, I want dynamic lists so that I can display variable-length data | 🔴 Must-have |
| Conditional blocks | Evaluate `{{#if condition}}...{{/if}}` for content visibility | As Marie, I want conditional content so that templates adapt to recipient data | 🔴 Must-have |
| Channel post-processing | Email: CSS inlining + HTML sanitization; Letter: HTML→PDF conversion; SMS: plain text extraction + truncation | As Thomas (Operator), I want channel-appropriate output so that messages render correctly on each medium | 🔴 Must-have |
| Template engine: Scriban | Use Scriban as the underlying engine behind an `ITemplateRenderer` abstraction | As Julien, I want a proven template engine so that we don't maintain custom parsing code | 🔴 Must-have |

#### Epic 3 — Dispatch Engine

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Email dispatch (SMTP) | Send resolved HTML emails via configurable SMTP server with attachment and CC support | As Thomas, I want to send emails so that recipients receive formatted communications | 🔴 Must-have |
| SMS dispatch | Send plain text messages via external SMS provider API | As Thomas, I want to send SMS reminders so that I can reach recipients on mobile | 🔴 Must-have |
| PDF letter dispatch | Generate PDF files and transmit to print/mail provider; support multi-page consolidation | As Thomas, I want to generate consolidated PDF mailings so that the print provider can process them | 🔴 Must-have |
| Channel strategy pattern | `IChannelDispatcher` interface with DI-based registry — no hardcoded switch/case | As Julien, I want pluggable channels so that adding WhatsApp or Push doesn't require core changes | 🔴 Must-have |
| Throttling | Configurable rate limiting per channel (e.g., 100 msgs/sec for SMTP) | As Sophie, I want send throttling so that we don't overwhelm mail servers | 🟠 Should-have |

#### Epic 4 — Campaign Orchestrator

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Campaign creation | Create campaigns with template selection, data source, filters, free field values, and schedule | As Thomas, I want to configure a campaign so that I can target specific recipients with the right content | 🔴 Must-have |
| Multi-step sequences | Define ordered steps with channel, delay (J+N days), and step-specific target filters (e.g., non-respondents only) | As Thomas, I want multi-step campaigns so that I can schedule initial send, email reminder, then SMS | 🔴 Must-have |
| Template snapshot | Freeze template content at campaign scheduling time; all steps use the snapshot | As Thomas, I want campaign integrity so that template edits don't affect running campaigns | 🔴 Must-have |
| Chunk-based batching | Split recipients into chunks of 500, process via Hangfire workers in parallel, track completion atomically | As Sophie, I want scalable batch processing so that 100K-recipient campaigns complete reliably | 🔴 Must-have |
| Campaign status tracking | Real-time progress: Draft → Scheduled → Running → StepInProgress → Completed / PartialFailure / ManualReview | As Thomas, I want campaign status visibility so that I know what's happening at all times | 🔴 Must-have |
| Static attachments | Operator uploads common files (e.g., terms PDF) attached to all sends in a step | As Thomas, I want to attach common documents so that all recipients receive the same reference material | 🟠 Should-have |
| Dynamic attachments | Per-recipient file path from data source field; graceful handling if file is missing | As Thomas, I want per-recipient attachments so that each person gets their personal document (statement, certificate) | 🟠 Should-have |
| CC management | Static CC (operator-defined) + dynamic CC (from data source field); deduplicated, validated | As Thomas, I want CC support so that relevant stakeholders receive copies | 🟠 Should-have |

#### Epic 5 — Data Source Connector

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Data source declaration | Register data sources with name, connection type, connection string, and schema definition (fields, types, filterability) | As Sophie, I want to declare data repositories so that operators can target different populations | 🔴 Must-have |
| Schema-agnostic querying | `IDataSourceConnector` with SQL Server and API connector implementations | As Julien, I want pluggable data connectors so that we can add new data sources without code changes | 🔴 Must-have |
| Filter expression (AST) | Operator builds filters as expression trees; connector translates to parameterized SQL | As Thomas, I want visual filtering so that I can target specific populations without writing SQL | 🔴 Must-have |
| Sample data for preview | Read-only query returning N sample rows for template preview | As Marie, I want sample data so that I can preview templates with realistic content | 🟠 Should-have |

#### Epic 6 — Generic Send API

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Single send endpoint | `POST /api/send` with templateId, channel, data dictionary, recipient | As Julien, I want a simple API to send a single message so that any internal app can trigger communications | 🔴 Must-have |
| API authentication | API key or OAuth2 client credentials for external consumers | As Sophie, I want API authentication so that only authorized systems can send messages | 🔴 Must-have |
| OpenAPI documentation | Auto-generated Swagger/OpenAPI spec | As Julien, I want API documentation so that I can integrate without guessing | 🟠 Should-have |
| Rate limiting per consumer | Configurable rate limits per API key | As Sophie, I want rate limiting so that no single consumer can overwhelm the system | 🟠 Should-have |

#### Epic 7 — Tracking & Audit

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Send logging | Every send attempt logged with status (Pending/Sent/Failed/Retrying), timestamp, error detail, retry count | As Thomas, I want send logs so that I can investigate delivery issues | 🔴 Must-have |
| Campaign progress dashboard | Aggregate view: total recipients, processed count, success/failure breakdown per step | As Thomas, I want a progress dashboard so that I can monitor campaigns in real time | 🟠 Should-have |
| Retry mechanism | Configurable exponential backoff (30s / 2min / 10min, 3 attempts) per send; chunk-level retry via Hangfire | As Sophie, I want automatic retries so that transient failures are recovered without manual intervention | 🔴 Must-have |

#### Epic 8 — Identity & Access

| Feature | Description | User Story | Priority |
|---------|-------------|------------|----------|
| Role-based access control | Designer role (template CRUD, preview) vs Operator role (campaign CRUD, monitoring) vs Admin role | As Sophie, I want role separation so that designers can't launch campaigns and operators can't modify templates | 🔴 Must-have |
| Authentication | ASP.NET Core Identity or Windows Authentication (internal tool) | As Sophie, I want secure authentication appropriate for an internal tool | 🔴 Must-have |

### 5.2 Non-functional Requirements

| Category | Requirement |
|----------|-------------|
| **Performance** | Single API send: < 500ms p95. Batch campaign: 100,000 recipients in < 60 minutes with 8 Hangfire workers |
| **Scalability** | Horizontal scaling via Hangfire worker count (4–8 default, configurable). Chunk size configurable (default 500) |
| **Availability** | Internal tool — standard business hours availability. Hangfire dashboard for job monitoring and manual retry |
| **Data integrity** | Template snapshots guarantee campaign reproducibility. Atomic chunk completion tracking via SQL `UPDATE...OUTPUT` |
| **Maintainability** | Layered architecture (Domain / Application / Infrastructure / Web). All cross-cutting concerns behind interfaces |
| **Observability** | Structured logging. SEND_LOG as source of truth for all dispatch activity |

---

## 6. Out of Scope (v1)

- Email open tracking (pixel) and click tracking (redirect links)
- Visual drag-and-drop template editor (HTML editor only in v1)
- Multi-tenant support (single-organization internal tool)
- Webhook notifications for send status to external consumers
- MJML integration for email rendering abstraction
- WhatsApp / Push notification channels
- Real-time analytics and reporting dashboards beyond basic campaign progress
- CSV/file-based data source import (SQL Server and API connectors only in v1)
- A/B testing of templates
- Unsubscribe management / opt-out handling

---

## 7. Technical Constraints & Stack

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| **Runtime** | .NET 8 (LTS) | Stable, long-term support, team expertise |
| **Web framework** | ASP.NET Core (Razor Pages for UI, Web API for APIs) | Server-side rendering for internal tool; API-first for integrations |
| **ORM** | Entity Framework Core | Migrations, LINQ, established ecosystem |
| **Database** | SQL Server | Enterprise standard, team expertise, SSRS compatibility |
| **Background jobs** | Hangfire Community (free) | No Pro license — batch coordination via Chunk Coordinator pattern |
| **Template engine** | Scriban | Lightweight, sandboxed, Liquid-like syntax, extensible |
| **Object mapping** | Mapster | Fast, convention-based Entity↔DTO mapping |
| **PDF generation** | wkhtmltopdf or DinkToPdf (to be validated via POC) | HTML→PDF conversion for letter channel |
| **PDF consolidation** | PdfSharp (MIT license) | Multi-page PDF concatenation for print provider |
| **CSS inlining** | PreMailer.Net | Email HTML compatibility across clients |
| **File storage** | Internal file share (UNC paths) | Attachments stored on network share, metadata in DB |
| **Hosting** | IIS on Windows Server | Existing infrastructure, Windows Authentication support |
| **DI** | Microsoft.Extensions.DependencyInjection (native) | Standard ASP.NET Core container |
| **Tests** | xUnit + Moq + FluentAssertions | Team standard |

**Platform target:** Web application (server-rendered UI + REST API). No mobile app.

---

## 8. External Dependencies & Integrations

| Dependency | Purpose | Risk Level | Required Setup |
|------------|---------|------------|----------------|
| SMTP Server | Email dispatch | Low | Server address, port, credentials. Must support throttling config |
| SMS Provider API | SMS dispatch | Medium | API key, endpoint URL, rate limits. Provider contract required |
| Print/Mail Provider | Paper letter dispatch (PDF) | Medium | File drop zone or API, format specifications, SLA agreement |
| Internal File Share | Attachment storage (static + dynamic) | Low | UNC path accessible from IIS app pool identity and Hangfire workers |
| SQL Server instance | All persistent data | Low | Database provisioning, connection string, backup policy |
| Business data repositories | Recipient data sources (clients, employees, etc.) | Medium | Read-only SQL Server access or REST API endpoint per repository |

---

## 9. Security & Compliance

| Aspect | Approach |
|--------|----------|
| **Authentication** | Windows Authentication (internal) or ASP.NET Core Identity with local accounts |
| **Authorization** | Role-based: Designer, Operator, Admin. Enforced at API and UI level |
| **API security** | API key or OAuth2 client credentials for external consumers; HTTPS only |
| **Data sensitivity** | Recipient PII in transit and at rest. Connection strings encrypted in configuration |
| **Template security** | Data values are HTML-escaped during substitution to prevent XSS. Template HTML itself is not sanitized (Designer is trusted) |
| **SQL injection prevention** | Filter AST translated to parameterized queries only. No raw SQL from operators |
| **Input validation** | Attachment whitelist (PDF, DOCX, XLSX, PNG, JPG). Size limits (10 MB per file, 25 MB total per send) |
| **Audit trail** | SEND_LOG traces every dispatch action. Template snapshots provide content traceability |
| **GDPR** | Internal tool — data processing aligned with organization's existing GDPR framework. No external data collection |
| **Rate limiting** | Per-consumer API rate limiting to prevent abuse |

---

## 10. Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| SMTP server throttling / blacklisting from high-volume sends | Medium | High | Configurable per-channel throttling; respect server limits; monitor bounce rates |
| HTML email rendering inconsistencies across clients (Outlook, Gmail, Apple Mail) | High | Medium | CSS inlining via PreMailer.Net; test on major clients; consider MJML in Phase 2 |
| Hangfire Community lacks batch primitives | Certain | Medium | Chunk Coordinator pattern with atomic completion counter (validated in architecture study) |
| PDF generation performance bottleneck for large letter campaigns | Medium | Medium | Pre-resolve templates before PDF conversion; parallelize via Hangfire workers; POC to validate tool choice |
| Data source connector fails mid-campaign (DB connection loss) | Low | High | Chunk-level retry (3 attempts via `[AutomaticRetry]`); per-send retry with exponential backoff; PartialFailure status for manual review |
| File share inaccessible from Hangfire workers | Low | Medium | Validate file share access at campaign scheduling time; warn on missing dynamic attachments instead of blocking |
| Template XSS via unescaped user data | Low | High | All substituted values are HTML-escaped by Scriban; template HTML is trusted (Designer role only) |
| Scriban template engine limitations for complex layouts | Low | Low | Scriban is extensible via custom functions; `ITemplateRenderer` abstraction allows future engine swap |
| Single point of failure (monolithic deployment) | Medium | Medium | Hangfire workers can scale independently; database backups; standard IIS resilience (app pool recycling, health checks) |

---

## 11. Roadmap & Milestones

### Phase 1 — MVP (Core Engine)
- Template Registry (CRUD, sub-templates, placeholder manifest)
- Rendering Engine (Scriban, scalar/table/list/conditional, channel post-processing)
- Dispatch Engine (SMTP email, SMS API, PDF letter)
- Generic Send API (`POST /api/send`)
- Campaign Orchestrator (creation, multi-step, snapshot, chunk batching)
- Data Source Connector (SQL Server connector, filter AST)
- Send logging and retry mechanism
- Role-based access control (Designer / Operator / Admin)
- **Target:** 12–16 weeks

### Phase 2 — Operational Excellence
- Template preview with real sample data
- Template lifecycle workflow (Draft → Published → Archived)
- Campaign progress dashboard
- Static and dynamic attachment management
- CC management (static + dynamic)
- OpenAPI/Swagger documentation
- API rate limiting per consumer
- Throttling per channel
- **Target:** 8–10 weeks after Phase 1

### Phase 3 — Advanced Features
- Visual template editor (WYSIWYG or MJML-based)
- Email open tracking (pixel) and click tracking
- Webhook notifications for send status
- Additional data source connectors (REST API, CSV import)
- Additional channels (WhatsApp, Push)
- Advanced reporting and analytics dashboard
- **Target:** 10–12 weeks after Phase 2

---

## 12. Open Questions

| # | Question | Impact | Owner |
|---|----------|--------|-------|
| Q1 | Which PDF generation tool to adopt? wkhtmltopdf vs DinkToPdf vs Puppeteer — requires POC | Affects letter channel implementation and performance | Tech Lead |
| Q2 | Which SMS provider will be used? API contract and pricing model needed | Affects SMS dispatcher implementation | Business / Procurement |
| Q3 | What are the exact SMTP server limits (messages/second, connection pool)? | Affects throttling configuration | Infrastructure team |
| Q4 | Should Windows Authentication or local Identity accounts be used? | Affects authentication implementation | Security / IT |
| Q5 | What is the expected format for the print/mail provider (consolidated PDF, individual files, XML manifest)? | Affects PDF letter dispatch implementation | Business / Provider |
| Q6 | Is there a need for template approval workflow before publication? | Affects template lifecycle complexity | Business stakeholders |
| Q7 | What file share infrastructure is available? UNC path, NAS, or S3-compatible? | Affects attachment storage implementation | Infrastructure team |
| Q8 | Are there existing data sources with REST APIs, or only SQL Server databases? | Affects Phase 1 connector scope | Integration team |
| Q9 | What are the data retention requirements for SEND_LOG and template snapshots? | Affects storage planning and archival strategy | Compliance / Legal |
| Q10 | Should the API support asynchronous sends with status callback, or only synchronous? | Affects API design for high-volume consumers | Tech Lead / Consumers |

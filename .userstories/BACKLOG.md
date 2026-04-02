# 📋 Backlog - Letter Channel Migration (HTML to DOCX Templates)

**Source:** `_docs/prd-docx-letter-channel.md` (Version 1.5)
**Created:** 2026-03-29
**Last updated:** 2026-03-30

---

## 📊 Overview

**Total User Stories:** 25
**Estimated complexity:** 50-60 person-days
**Overall status:** 🟡 Planning

### Statistics
- 🔴 High priority: 18 stories
- 🟠 Medium priority: 4 stories
- 🟢 Low priority: 2 stories

---

## 🎭 Personas / Actors

### Claire — Template Designer
**Role:** Marketing communications designer
**Needs:**
- Create professional, brand-compliant letter templates using Microsoft Word
- Upload DOCX files instead of writing HTML/CSS
- Iterate quickly on layout and typography
- Preview personalized output before publishing
- See version history audit trail

### Marc — Campaign Manager
**Role:** Marketing operations manager
**Needs:**
- Assemble campaigns with letter steps
- Preview personalized DOCX output with sample data
- Monitor batch dispatch status
- Download templates for offline review

### Sarah — System Administrator
**Role:** IT/DevOps engineer
**Needs:**
- Deploy and maintain the CampaignEngine platform
- Configure file storage paths
- Manage dependencies without native DLLs
- Ensure startup validation catches misconfigurations
- Control backup policies for template files

### Thomas — API Integrator
**Role:** Developer at a partner agency
**Needs:**
- Automate template uploads via REST API
- Use multipart form-data for binary DOCX files
- Download templates programmatically
- Read clear API documentation

---

## 🏗️ Architecture & Technical Stack

**Backend:** .NET 8, ASP.NET Core (Razor Pages + REST controllers)
**ORM:** EF Core 8 + SQL Server
**Template Storage:** Local/network file system (configurable via `appsettings.json`)
**DOCX Manipulation:** `DocumentFormat.OpenXml` (Microsoft, MIT license)
**Background Jobs:** Hangfire 1.8
**Template Syntax:** Custom `{{ }}` placeholders in DOCX (plain text); Scriban for Email/SMS HTML
**Architecture:** Clean Architecture (Domain → Application → Infrastructure ← Web)

---

## 🎯 User Stories

### Epic 1: File-System Template Storage (Breaking Change — All Channels)

> Migrate template body storage from database columns to file system for all channels (Letter, Email, SMS). This is a prerequisite for Epic 2.

#### [US-001] - ITemplateBodyStore abstraction layer
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 1

**As a** system architect
**I want** an `ITemplateBodyStore` interface abstracting file read/write operations
**So that** the storage location (local disk, network share, future blob storage) can be swapped without changing service logic

**Specification context:**
> F-103: `ITemplateBodyStore` interface with `ReadAsync(path)` that throws `TemplateBodyNotFoundException` if file doesn't exist, and `TemplateBodyCorruptedException` if file exists but cannot be opened or parsed. Both exception types live in `Application/Interfaces/` as part of the store contract.

**Acceptance criteria:**
- [x] `ITemplateBodyStore` interface created in `Application/Interfaces/` ✅
- [x] `TemplateBodyNotFoundException` exception defined in `Application/Interfaces/` ✅
- [x] `TemplateBodyCorruptedException` exception defined in `Application/Interfaces/` ✅
- [x] `ReadAsync(path)` throws `TemplateBodyNotFoundException` for missing/null/empty path ✅
- [x] `ReadAsync(path)` throws `TemplateBodyCorruptedException` for corrupt files ✅
- [x] `WriteAsync(path, stream)` method defined ✅
- [x] `DeleteAsync(path)` method defined ✅

**Technical tasks:**
- [x] `TASK-001-01` - **[Interface]** Define `ITemplateBodyStore` in `Application/Interfaces/Storage/` ✅ 2026-03-29
- [x] `TASK-001-02` - **[Exception]** Create `TemplateBodyNotFoundException` in `Application/Interfaces/Exceptions/` ✅ 2026-03-29
- [x] `TASK-001-03` - **[Exception]** Create `TemplateBodyCorruptedException` in `Application/Interfaces/Exceptions/` ✅ 2026-03-29
- [x] `TASK-001-04` - **[Method]** Define `Task<Stream> ReadAsync(string path, CancellationToken ct)` ✅ 2026-03-29
- [x] `TASK-001-05` - **[Method]** Define `Task<string> WriteAsync(string path, Stream content, CancellationToken ct)` ✅ 2026-03-29
- [x] `TASK-001-06` - **[Method]** Define `Task DeleteAsync(string path, CancellationToken ct)` ✅ 2026-03-29
- [x] `TASK-001-07` - **[Doc]** XML comments for all interface members ✅ 2026-03-29

**Business rules:**
1. Exceptions are part of the interface contract (Application layer)
2. Infrastructure implementations throw these exceptions; Application services catch and translate them

**Dependencies:** None
**Estimation:** 2-3 days

**Implementation notes:**
- Exception layer ownership is critical — both exceptions live in Application, not Infrastructure
- ReadAsync must distinguish between "file never existed" and "file exists but corrupt"

---

#### [US-002] - FileSystemTemplateBodyStore implementation
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 1

**As a** system
**I want** a concrete file-system implementation of `ITemplateBodyStore`
**So that** template bodies can be stored and retrieved from the local or network file system

**Specification context:**
> F-101: File-system storage for template bodies (HTML for Email/SMS, DOCX for Letter) decouples binary payloads from relational data.

**Acceptance criteria:**
- [x] `FileSystemTemplateBodyStore` implements `ITemplateBodyStore` ✅
- [x] Constructor accepts storage root path via dependency injection ✅
- [x] `WriteAsync` writes stream to file atomically ✅
- [x] `ReadAsync` throws correct exceptions for missing/corrupt files ✅
- [x] `DeleteAsync` removes file if exists (no-op if missing) ✅
- [x] All I/O exceptions wrapped in appropriate custom exceptions ✅

**Technical tasks:**
- [x] `TASK-002-01` - **[Class]** Create `FileSystemTemplateBodyStore` in `Infrastructure/Storage/` ✅ 2026-03-30
- [x] `TASK-002-02` - **[Method]** Implement `WriteAsync` with atomic write (temp file + rename) ✅ 2026-03-30
- [x] `TASK-002-03` - **[Method]** Implement `ReadAsync` with exception mapping ✅ 2026-03-30
- [x] `TASK-002-04` - **[Method]** Implement `DeleteAsync` with safe file deletion ✅ 2026-03-30
- [x] `TASK-002-05` - **[Config]** Add storage root path to DI container ✅ 2026-03-30
- [x] `TASK-002-06` - **[Test]** Unit tests for write/read/delete operations ✅ 2026-03-30
- [x] `TASK-002-07` - **[Test]** Unit tests for exception scenarios ✅ 2026-03-30

**Business rules:**
1. Use atomic file operations (write to temp, then move)
2. Map I/O exceptions to `TemplateBodyNotFoundException` or `TemplateBodyCorruptedException`

**Dependencies:** US-001
**Estimation:** 3-4 days

**Implementation notes:**
- Use `FileStream` with `FileOptions.Asynchronous` for async I/O
- Implement atomic write pattern: write to `.tmp` file, then `File.Move` with overwrite

---

#### [US-003] - Database schema migration for BodyPath
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 1

**As a** system
**I want** `Template` and `TemplateHistory` entities to store `BodyPath` (nvarchar) instead of `HtmlBody`/`DocxBody` columns
**So that** template bodies are decoupled from database storage

**Specification context:**
> F-102: Database stores file paths only. BodyPath exposes a relative path (e.g., `templates/{id}/v3.docx`). Also add `BodyChecksum` (nvarchar(64), nullable) and `RowVersion` (timestamp) concurrency token to `Template`.

**Acceptance criteria:**
- [x] `Template` entity has `BodyPath` property (string, required) ✅
- [x] `Template` entity has `BodyChecksum` property (string?, nullable, max 64 chars) ✅
- [x] `Template` entity has `RowVersion` property (byte[], concurrency token) ✅
- [x] `TemplateHistory` entity has `BodyPath` property (string, required) ✅
- [x] `TemplateHistory` entity has `BodyChecksum` property (string?, nullable, max 64 chars) ✅
- [x] EF Core migration removes old `HtmlBody` / `DocxBody` columns ✅
- [x] Migration applies cleanly on empty database (no data migration needed) ✅

**Technical tasks:**
- [x] `TASK-003-01` - **[Model]** Add `BodyPath` property to `Template` entity ✅ 2026-03-29
- [x] `TASK-003-02` - **[Model]** Add `BodyChecksum` property to `Template` entity ✅ 2026-03-29
- [x] `TASK-003-03` - **[Model]** Add `RowVersion` property with `[Timestamp]` attribute ✅ 2026-03-29
- [x] `TASK-003-04` - **[Model]** Update `TemplateHistory` entity with `BodyPath` and `BodyChecksum` ✅ 2026-03-29
- [x] `TASK-003-05` - **[Migration]** Generate EF migration: `AddBodyPathAndChecksumToTemplates` ✅ 2026-03-29
- [x] `TASK-003-06` - **[Migration]** Test migration up/down on clean database ✅ 2026-03-29
- [x] `TASK-003-07` - **[Doc]** Add deployment note: no down-migration, restore from backup for rollback ✅ 2026-03-29

**Business rules:**
1. BodyPath is a relative path from storage root (never includes server root)
2. No data migration needed (never in production)
3. Rollback requires database restore from backup

**Dependencies:** None (can run parallel with US-001/002)
**Estimation:** 1-2 days

**Implementation notes:**
- Use EF Core `[ConcurrencyCheck]` or `[Timestamp]` for `RowVersion`
- Document that SHA-256 checksum is hex string (64 chars)

---

#### [US-004] - Configurable storage root with startup validation
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 1

**As a** Sarah (System Administrator)
**I want** the application to fail fast at startup if the template storage root is misconfigured
**So that** deployment issues are detected immediately, not at first template upload

**Specification context:**
> F-104 + F-104b: Storage root configured via `appsettings.json` (`TemplateStorage:RootPath`). Startup check implemented as `IHostedService` that throws if path is missing, does not exist, or is not writable.

**Acceptance criteria:**
- [x] `appsettings.json` has `TemplateStorage:RootPath` setting ✅
- [x] `IHostedService` validates storage root at startup ✅
- [x] Application fails to start if `RootPath` is null/empty ✅
- [x] Application fails to start if `RootPath` does not exist ✅
- [x] Application fails to start if `RootPath` is not writable ✅
- [x] Clear error message logged on validation failure ✅

**Technical tasks:**
- [x] `TASK-004-01` - **[Config]** Add `TemplateStorage` section to `appsettings.json` ✅ 2026-03-30
- [x] `TASK-004-02` - **[Class]** Create `TemplateStorageOptions` class with `RootPath` property ✅ 2026-03-30
- [x] `TASK-004-03` - **[HostedService]** Create `TemplateStorageStartupValidator : IHostedService` ✅ 2026-03-30
- [x] `TASK-004-04` - **[Validation]** Check path exists (`Directory.Exists`) ✅ 2026-03-30
- [x] `TASK-004-05` - **[Validation]** Check path writable (attempt temp file write) ✅ 2026-03-30
- [x] `TASK-004-06` - **[DI]** Register hosted service in `Program.cs` ✅ 2026-03-30
- [x] `TASK-004-07` - **[Test]** Unit tests for validation logic ✅ 2026-03-30

**Business rules:**
1. Validation runs before any web requests are accepted
2. Misconfiguration must halt startup (not just log warning)

**Dependencies:** US-002
**Estimation:** 1-2 days

**Implementation notes:**
- Use `IOptions<TemplateStorageOptions>` for config binding
- Test writability by creating and deleting a temp file (`.startup_check`)

---

#### [US-005] - Atomic file write with concurrency guard
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-31
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Epic 1

**As a** system
**I want** template file writes and database path commits to be atomic
**So that** orphaned files are prevented and concurrent updates are safely rejected

**Specification context:**
> F-105: Write file first, commit path to DB; on DB failure, delete orphaned file in same request. On update, copy previous body to `history/v{n}.docx` before writing new version. EF Core `rowversion` prevents concurrent writes; second writer gets HTTP 409.

**Acceptance criteria:**
- [x] File is written before DB transaction commits ✅
- [x] On DB commit failure, file is deleted synchronously ✅
- [x] On template update, previous body is copied to `history/v{n}.docx` ✅
- [x] `TemplateService.UpdateAsync` uses EF optimistic concurrency ✅
- [x] Concurrent update attempts return HTTP 409 Conflict ✅
- [x] `DbUpdateConcurrencyException` translated to appropriate response ✅

**Technical tasks:**
- [x] `TASK-005-01` - **[Service]** Update `TemplateService.CreateAsync` with atomic write pattern ✅ 2026-03-30
- [x] `TASK-005-02` - **[Service]** Update `TemplateService.UpdateAsync` with history copy + concurrency check ✅ 2026-03-30
- [x] `TASK-005-03` - **[Exception]** Add exception handler for `DbUpdateConcurrencyException` ✅ 2026-03-30
- [x] `TASK-005-04` - **[Cleanup]** Implement synchronous orphaned-file deletion on DB failure ✅ 2026-03-30
- [x] `TASK-005-05` - **[Test]** Unit tests for create success path ✅ 2026-03-30
- [x] `TASK-005-06` - **[Test]** Unit tests for DB failure → file cleanup ✅ 2026-03-30
- [x] `TASK-005-07` - **[Test]** Unit tests for concurrent update → 409 response ✅ 2026-03-30
- [x] `TASK-005-08` - **[Test]** Integration test for update with history copy ✅ 2026-03-30

**Business rules:**
1. File write happens inside try-catch; DB commit inside using block
2. Previous body file is copied (not moved) to preserve audit trail
3. Periodic background cleanup of orphans is out of scope

**Dependencies:** US-002, US-003
**Estimation:** 5-6 days

**Implementation notes:**
- Use EF Core `DbContext.SaveChangesAsync` inside try-finally
- Catch `DbUpdateConcurrencyException` and map to `ConcurrencyException` domain exception
- `GlobalExceptionMiddleware` translates to HTTP 409

---

#### [US-006] - DOCX binary body storage
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 1

**As a** Claire (Template Designer)
**I want** my uploaded `.docx` file stored on the server
**So that** I can download it later for editing

**Specification context:**
> F-106: DOCX binary storage. File naming convention: `{storageRoot}/templates/{templateId}/v{version}.docx`

**Acceptance criteria:**
- [x] DOCX files stored in `templates/{templateId}/v{version}.docx` ✅
- [x] Directory structure auto-created on first upload ✅
- [x] File path is relative (excludes storage root) ✅
- [x] DOCX files preserved with correct MIME type metadata ✅

**Technical tasks:**
- [x] `TASK-006-01` - **[Service]** Update `TemplateService.CreateAsync` to save DOCX files ✅ 2026-03-30
- [x] `TASK-006-02` - **[Service]** Update `TemplateService.UpdateAsync` to version DOCX files ✅ 2026-03-30
- [x] `TASK-006-03` - **[Naming]** Implement file naming convention helper ✅ 2026-03-30
- [x] `TASK-006-04` - **[Directory]** Auto-create template directory on upload ✅ 2026-03-30
- [x] `TASK-006-05` - **[Test]** Unit tests for DOCX file storage ✅ 2026-03-30
- [x] `TASK-006-06` - **[Test]** Integration test for version increment ✅ 2026-03-30

**Business rules:**
1. File naming convention is strict: `v{version}.docx` (1-based)
2. Directory structure mirrors template ID hierarchy

**Dependencies:** US-002, US-003
**Estimation:** 2 days

---

#### [US-007] - HTML body storage for Email/SMS
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 1

**As a** system
**I want** HTML template bodies (Email/SMS) stored as `.html` files
**So that** all template bodies use consistent file-system storage

**Specification context:**
> F-107: HTML body storage. File naming convention: `{storageRoot}/templates/{templateId}/v{version}.html`

**Acceptance criteria:**
- [x] HTML files stored in `templates/{templateId}/v{version}.html` ✅
- [x] UTF-8 encoding preserved ✅
- [x] Email/SMS rendering logic reads from file system (not DB) ✅
- [x] Existing Email/SMS templates tested for regression ✅

**Technical tasks:**
- [x] `TASK-007-01` - **[Service]** Update Email/SMS create path to save HTML files ✅ 2026-03-30
- [x] `TASK-007-02` - **[Service]** Update Email/SMS update path to version HTML files ✅ 2026-03-30
- [x] `TASK-007-03` - **[Renderer]** Update Scriban renderer to read from `ITemplateBodyStore` ✅ 2026-03-30
- [x] `TASK-007-04` - **[Test]** Email template unit tests ✅ 2026-03-30
- [x] `TASK-007-05` - **[Test]** SMS template unit tests ✅ 2026-03-30
- [x] `TASK-007-06` - **[Test]** Regression tests for existing Email/SMS workflows ✅ 2026-03-30

**Business rules:**
1. Email/SMS rendering logic unchanged (Scriban-based)
2. Only storage layer changes

**Dependencies:** US-002, US-003
**Estimation:** 2-3 days

---

#### [US-008] - DOCX download endpoint
**Status:** 🟡 TODO
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Epic 1

**As a** Thomas (API Integrator) or Marc (Campaign Manager)
**I want** to download the current DOCX file via `GET /api/templates/{id}/docx`
**So that** I can inspect or re-edit it offline

**Specification context:**
> F-110: DOCX download endpoint. Authorization: Designer, Admin, or CampaignManager. Returns DOCX bytes with `Content-Disposition: attachment`.

**Acceptance criteria:**
- [ ] `GET /api/templates/{id}/docx` endpoint implemented
- [ ] Authorization requires Designer, Admin, or CampaignManager role
- [ ] Returns DOCX bytes with MIME type `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
- [ ] Response header includes `Content-Disposition: attachment; filename="{templateName}.docx"`
- [ ] Returns HTTP 404 if template not found
- [ ] Returns HTTP 422 if template is not Letter channel

**Technical tasks:**
- [ ] `TASK-008-01` - **[API]** Add `GET /api/templates/{id}/docx` endpoint to `TemplatesController`
- [ ] `TASK-008-02` - **[Auth]** Apply `RequireDesignerOrAdminOrCampaignManager` policy
- [ ] `TASK-008-03` - **[Service]** Implement `TemplateService.GetDocxBodyAsync(id)`
- [ ] `TASK-008-04` - **[Response]** Set correct MIME type and Content-Disposition header
- [ ] `TASK-008-05` - **[Test]** API integration tests for success path
- [ ] `TASK-008-06` - **[Test]** API integration tests for 404/422 responses

**Business rules:**
1. CampaignManager has read-only access (cannot upload/update)
2. Download returns current version only (not historical versions)

**Dependencies:** US-006
**Estimation:** 1-2 days

---

### Epic 2: DOCX Template Upload & Validation

> Enable designers to upload `.docx` files via web UI and REST API with comprehensive validation.

#### [US-009] - DOCX structural validation
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 2

**As a** system
**I want** to validate uploaded DOCX files for structural integrity and security
**So that** corrupt or malicious files are rejected before storage

**Specification context:**
> F-203: Validate (1) `.docx` extension only; (2) valid ZIP archive; (3) contains `[Content_Types].xml`; (4) opens as `WordprocessingDocument`; (5) no `vbaProject.bin` (macros).

**Acceptance criteria:**
- [x] Files with non-`.docx` extension (including `.docm`) rejected with HTTP 422 ✅
- [x] Invalid ZIP archives rejected with HTTP 422 ✅
- [x] Files missing `[Content_Types].xml` rejected with HTTP 422 ✅
- [x] Files that cannot open as `WordprocessingDocument` rejected with HTTP 422 ✅
- [x] Files containing `vbaProject.bin` rejected with HTTP 422 ✅
- [x] Clear error messages for each validation failure ✅

**Technical tasks:**
- [x] `TASK-009-01` - **[Service]** Create `DocxValidationService` in `Application/Services/` ✅ 2026-03-29
- [x] `TASK-009-02` - **[Validation]** Check file extension (`.docx` case-insensitive) ✅ 2026-03-29
- [x] `TASK-009-03` - **[Validation]** Validate ZIP archive structure ✅ 2026-03-29
- [x] `TASK-009-04` - **[Validation]** Check for `[Content_Types].xml` part ✅ 2026-03-29
- [x] `TASK-009-05` - **[Validation]** Attempt `WordprocessingDocument.Open()` ✅ 2026-03-29
- [x] `TASK-009-06` - **[Validation]** Check for `vbaProject.bin` (macro detection) ✅ 2026-03-29
- [x] `TASK-009-07` - **[Exception]** Map validation failures to `ValidationException` with clear messages ✅ 2026-03-29
- [x] `TASK-009-08` - **[Test]** Unit tests for each validation rule with fixture files ✅ 2026-03-29

**Business rules:**
1. Extension check is case-insensitive (`.DOCX` is valid)
2. Macro-enabled `.docm` files are explicitly rejected
3. All validation failures return HTTP 422 with structured error

**Dependencies:** None (can run early)
**Estimation:** 3-4 days

**Implementation notes:**
- Use `DocumentFormat.OpenXml` NuGet package
- Test with real malformed DOCX fixtures (corrupt ZIP, missing parts, macro-enabled)

---

#### [US-010] - File size limit enforcement
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 2

**As a** Sarah (System Administrator)
**I want** uploads rejected if they exceed 10 MB
**So that** storage abuse is prevented

**Specification context:**
> F-204: 10 MB limit enforced at Kestrel level (`[RequestSizeLimit]`) and re-validated in service layer.

**Acceptance criteria:**
- [x] Kestrel `[RequestSizeLimit(10_485_760)]` attribute applied to upload endpoints ✅
- [x] Service layer re-validates file size before processing ✅
- [x] Uploads exceeding 10 MB return HTTP 413 Payload Too Large ✅
- [x] Error response includes clear message ✅

**Technical tasks:**
- [x] `TASK-010-01` - **[Attribute]** Add `[RequestSizeLimit(10_485_760)]` to `POST /api/templates/letter` ✅ 2026-03-29
- [x] `TASK-010-02` - **[Attribute]** Add `[RequestSizeLimit(10_485_760)]` to `PUT /api/templates/{id}/letter` ✅ 2026-03-29
- [x] `TASK-010-03` - **[Validation]** Re-validate file size in `TemplateService.CreateAsync` ✅ 2026-03-29
- [x] `TASK-010-04` - **[Test]** API test with 10 MB file (success) ✅ 2026-03-29
- [x] `TASK-010-05` - **[Test]** API test with 11 MB file (rejected) ✅ 2026-03-29

**Business rules:**
1. Limit is 10 MB (10,485,760 bytes)
2. Enforced at both Kestrel and service layers (defense in depth)

**Dependencies:** None
**Estimation:** 1 day

---

#### [US-011] - Multipart upload API for Letter templates
**Status:** ✅ DONE
**Start date:** 2026-03-31
**End date:** 2026-03-31
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Epic 2

**As a** Thomas (API Integrator)
**I want** dedicated endpoints for Letter template upload/update
**So that** I can automate template creation via REST API

**Specification context:**
> F-205: `POST /api/templates/letter` and `PUT /api/templates/{id}/letter` with multipart/form-data. Parts: `name`, `description`, `file`. Authorization: Designer or Admin only.

**Acceptance criteria:**
- [x] `POST /api/templates/letter` endpoint accepts multipart/form-data ✅
- [x] `PUT /api/templates/{id}/letter` endpoint accepts multipart/form-data ✅
- [x] Required parts: `name` (string, max 200 chars), `file` (binary) ✅
- [x] Optional part: `description` (string, max 500 chars) ✅
- [x] Authorization requires Designer or Admin role (not CampaignManager) ✅
- [x] Returns HTTP 201 with `TemplateDto` on successful create ✅
- [x] Returns HTTP 409 on name collision ✅
- [x] Returns HTTP 404 on update if template not found ✅
- [x] Returns HTTP 422 on channel mismatch (update to non-Letter template) ✅
- [x] `TemplateDto` includes `bodyPath` as relative path (no server root exposed) ✅

**Technical tasks:**
- [x] `TASK-011-01` - **[API]** Add `POST /api/templates/letter` to `TemplatesController` ✅ 2026-03-31
- [x] `TASK-011-02` - **[API]** Add `PUT /api/templates/{id}/letter` to `TemplatesController` ✅ 2026-03-31
- [x] `TASK-011-03` - **[Auth]** Apply `RequireDesignerOrAdmin` policy (exclude CampaignManager) ✅ 2026-03-31
- [x] `TASK-011-04` - **[Binding]** Parse multipart form parts (`name`, `description`, `file`) ✅ 2026-03-31
- [x] `TASK-011-05` - **[Service]** Wire to `TemplateService.CreateAsync` / `UpdateAsync` ✅ 2026-03-31
- [x] `TASK-011-06` - **[DTO]** Ensure `TemplateDto.BodyPath` exposes relative path only ✅ 2026-03-31
- [x] `TASK-011-07` - **[Test]** API integration tests for create success path ✅ 2026-03-31
- [x] `TASK-011-08` - **[Test]** API integration tests for update success path ✅ 2026-03-31
- [x] `TASK-011-09` - **[Test]** API integration tests for 409/404/422 error cases ✅ 2026-03-31
- [x] `TASK-011-10` - **[Doc]** Update API documentation with multipart examples ✅ 2026-03-31

**Business rules:**
1. Template name must be unique per channel (enforced at service layer)
2. File is optional on update; if omitted, existing DOCX retained
3. CampaignManager explicitly excluded from upload/update endpoints

**Dependencies:** US-006, US-009, US-010
**Estimation:** 5-6 days

---

#### [US-012] - Conditional UI toggle for file upload
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🟠 Medium
**Complexity:** S
**Epic:** Epic 2

**As a** Claire (Template Designer)
**I want** the template creation form to show a file upload input for Letter channel
**So that** I can upload DOCX files directly from the web UI

**Specification context:**
> F-206: Conditional UI toggle. Letter channel → file upload; Email/SMS → HTML editor.

**Acceptance criteria:**
- [x] Template create page shows channel selector dropdown ✅
- [x] Selecting "Letter" displays file upload input (hides HTML editor) ✅
- [x] Selecting "Email" or "SMS" displays HTML editor (hides file upload) ✅
- [x] File upload input accepts `.docx` files only (HTML input attribute) ✅
- [x] Form validation enforces required file for Letter templates ✅

**Technical tasks:**
- [x] `TASK-012-01` - **[UI]** Update `CreateTemplate.cshtml` with channel selector ✅ 2026-03-29
- [x] `TASK-012-02` - **[UI]** Add conditional file upload input for Letter ✅ 2026-03-29
- [x] `TASK-012-03` - **[UI]** Add JavaScript to toggle input visibility ✅ 2026-03-29
- [x] `TASK-012-04` - **[Validation]** Client-side validation for file requirement ✅ 2026-03-29
- [x] `TASK-012-05` - **[Test]** Manual UI testing for toggle behavior ✅ 2026-03-29

**Business rules:**
1. Channel selector defaults to Email
2. File upload is hidden by default (shown only for Letter)

**Dependencies:** None (can run early for UI)
**Estimation:** 1-2 days

---

### Epic 3: DOCX Placeholder Engine

> Implement the core rendering engine for DOCX template processing: run merging, scalar replacement, collection rendering, and conditional blocks.

#### [US-013] - XML run merging with smart-quote normalization
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Epic 3

**As a** system
**I want** to merge Word's split XML runs so that placeholders are recognized
**So that** `{{ firstName }}` is detected even when Word fragments it across runs

**Specification context:**
> F-301: `DocxRunMerger` merges split `<w:r>` runs, preserves `<w:bookmarkStart>`, `<w:bookmarkEnd>`, `<w:rPr>`, normalizes smart quotes (`"` `"` → `"`). Must traverse main body + HeaderPart + FooterPart.

**Acceptance criteria:**
- [x] `DocxRunMerger` traverses main document body, all HeaderParts, all FooterParts ✅
- [x] Adjacent `<w:r>` elements with identical `<w:rPr>` are merged ✅
- [x] Bookmark elements (`<w:bookmarkStart>`, `<w:bookmarkEnd>`) preserved during merge ✅
- [x] Smart quotes normalized: `"` `"` → `"` (U+201C/U+201D → U+0022) ✅
- [x] Split placeholder `{{ first` + `Name }}` recognized after merge ✅
- [x] Edge case: bookmarks inside split placeholder discarded (documented) ✅

**Technical tasks:**
- [x] `TASK-013-01` - **[Class]** Create `DocxRunMerger` in `Infrastructure/Rendering/` ✅ 2026-03-30
- [x] `TASK-013-02` - **[Method]** Implement `MergeRuns(WordprocessingDocument doc)` ✅ 2026-03-30
- [x] `TASK-013-03` - **[Traversal]** Traverse main document body paragraphs ✅ 2026-03-30
- [x] `TASK-013-04` - **[Traversal]** Traverse HeaderPart XML for all sections ✅ 2026-03-30
- [x] `TASK-013-05` - **[Traversal]** Traverse FooterPart XML for all sections ✅ 2026-03-30
- [x] `TASK-013-06` - **[Merge]** Merge adjacent runs with same `<w:rPr>` ✅ 2026-03-30
- [x] `TASK-013-07` - **[Normalization]** Apply smart-quote normalization (U+201C/U+201D → U+0022) ✅ 2026-03-30
- [x] `TASK-013-08` - **[Preservation]** Preserve bookmark elements during merge ✅ 2026-03-30
- [x] `TASK-013-09` - **[Test]** Unit tests with fragmented placeholder fixtures ✅ 2026-03-30
- [x] `TASK-013-10` - **[Test]** Unit tests for headers/footers traversal ✅ 2026-03-30
- [x] `TASK-013-11` - **[Test]** Unit tests for smart-quote normalization ✅ 2026-03-30

**Business rules:**
1. Merge only consecutive runs with identical formatting
2. Bookmark elements inside split placeholders are discarded (edge case, not expected in valid authoring)
3. Headers/footers must be processed (prerequisite for F-308)

**Dependencies:** US-009 (for `WordprocessingDocument` usage)
**Estimation:** 6-7 days

**Implementation notes:**
- Use `DocumentFormat.OpenXml.Packaging` for part enumeration
- Use LINQ to XML for run grouping and merging
- Test with real Word files authored with auto-correct enabled

---

#### [US-014] - Scalar placeholder replacement
**Status:** ✅ DONE
**Start date:** 2026-03-31
**End date:** 2026-03-31
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 3

**As a** Claire (Template Designer)
**I want** `{{ firstName }}`, `{{ address }}`, etc. replaced with recipient data
**So that** letters are personalized

**Specification context:**
> F-302: Scalar placeholder replacement. Values are XML-escaped before insertion. Missing keys replaced with empty string.

**Acceptance criteria:**
- [x] `{{ key }}` placeholders replaced with corresponding values from recipient data ✅
- [x] Values are XML-escaped (prevent OpenXML injection) ✅
- [x] Missing keys replaced with empty string (no exception) ✅
- [x] Replacement works in main body, headers, footers ✅
- [x] Nested placeholders not supported (documented) ✅

**Technical tasks:**
- [x] `TASK-014-01` - **[Class]** Create `DocxPlaceholderReplacer` in `Infrastructure/Rendering/` ✅ 2026-03-31
- [x] `TASK-014-02` - **[Method]** Implement `ReplaceScalars(WordprocessingDocument doc, Dictionary<string, string> data)` ✅ 2026-03-31
- [x] `TASK-014-03` - **[Regex]** Match `{{ key }}` pattern in merged text runs ✅ 2026-03-31
- [x] `TASK-014-04` - **[Escape]** XML-escape values before insertion ✅ 2026-03-31
- [x] `TASK-014-05` - **[Fallback]** Replace missing keys with empty string ✅ 2026-03-31
- [x] `TASK-014-06` - **[Test]** Unit tests for scalar replacement ✅ 2026-03-31
- [x] `TASK-014-07` - **[Test]** Unit tests for XML escaping (e.g., `<>&"`) ✅ 2026-03-31
- [x] `TASK-014-08` - **[Test]** Unit tests for missing keys ✅ 2026-03-31

**Business rules:**
1. Placeholder syntax: `{{ key }}` (spaces inside braces optional)
2. Keys are case-sensitive
3. No nested placeholders (e.g., `{{ {{ key }} }}` is not valid)

**Dependencies:** US-013 (run merging must happen first)
**Estimation:** 3-4 days

---

#### [US-015] - Collection rendering via table rows
**Status:** ✅ DONE
**Start date:** 2026-04-02 15:30
**End date:** 2026-04-02
**Actual duration:** 1 day
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Epic 3

**As a** Claire (Template Designer)
**I want** to render a collection as repeated table rows
**So that** I can display invoice line items, order details, etc.

**Specification context:**
> F-303: Marker row `{{ collection_key }}`, template row with `{{ item.field }}`, end row `{{ end }}`. Engine duplicates template row per item. Missing `{{ end }}` throws `TemplateRenderException` with message "Missing {{ end }} for collection '{key}'."

**Acceptance criteria:**
- [ ] Marker row `{{ collection_key }}` identifies start of collection
- [ ] Template row contains `{{ item.field }}` placeholders
- [ ] End row `{{ end }}` marks end of collection block
- [ ] Engine duplicates template row once per item in collection
- [ ] Marker and end rows are removed from output
- [ ] Missing `{{ end }}` throws `TemplateRenderException` with clear message
- [ ] Empty collections result in no rows (marker/end rows removed)

**Technical tasks:**
- [x] `TASK-015-01` - **[Class]** Create `DocxTableCollectionRenderer` in `Infrastructure/Rendering/` ✅ 2026-04-02
- [x] `TASK-015-02` - **[Method]** Implement `RenderCollections(WordprocessingDocument doc, Dictionary<string, List<Dictionary<string, string>>> collections)` ✅ 2026-04-02
- [x] `TASK-015-03` - **[Detection]** Find marker rows `{{ collection_key }}` ✅ 2026-04-02
- [x] `TASK-015-04` - **[Validation]** Validate matching `{{ end }}` row exists ✅ 2026-04-02
- [x] `TASK-015-05` - **[Duplication]** Clone template row for each item ✅ 2026-04-02
- [x] `TASK-015-06` - **[Replacement]** Replace `{{ item.field }}` with item values ✅ 2026-04-02
- [x] `TASK-015-07` - **[Cleanup]** Remove marker and end rows ✅ 2026-04-02
- [x] `TASK-015-08` - **[Exception]** Throw `TemplateRenderException` for missing `{{ end }}` ✅ 2026-04-02
- [x] `TASK-015-09` - **[Test]** Unit tests for collection rendering (3-5 items) ✅ 2026-04-02
- [x] `TASK-015-10` - **[Test]** Unit tests for empty collection ✅ 2026-04-02
- [x] `TASK-015-11` - **[Test]** Unit tests for missing `{{ end }}` error ✅ 2026-04-02

**Business rules:**
1. Only table-row duplication supported (no paragraph-level loops)
2. Nested tables inside template row are not officially supported
3. Collection keys and item field names are case-sensitive

**Dependencies:** US-013, US-014
**Estimation:** 6-7 days

**Implementation notes:**
- Use `TableRow.CloneNode(deep: true)` for row duplication
- Detect marker/end rows by scanning all `<w:t>` text within row

---

#### [US-016] - Conditional block support (non-nested)
**Status:** ✅ DONE
**Start date:** 2026-04-02
**End date:** 2026-04-02
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 3

**As a** Claire (Template Designer)
**I want** `{{ if condition_key }}...{{ end }}` to show/hide paragraphs or table rows
**So that** I can conditionally display sections based on recipient data

**Specification context:**
> F-304: `{{ if condition_key }}` and `{{ end }}` markers each occupy a dedicated paragraph or table row. Boolean value determines visibility. No `{{ else }}` support. Nested `{{ if }}` blocks throw `TemplateRenderException` with message "Nested {{ if }} blocks are not supported (found inside '{{ if condition_key }}')."

**Acceptance criteria:**
- [ ] `{{ if condition_key }}` marker starts conditional block
- [ ] `{{ end }}` marker ends conditional block
- [ ] Content between markers is removed if condition is false
- [ ] Content between markers is kept if condition is true
- [ ] Markers work for paragraphs and table rows
- [ ] No `{{ else }}` support (documented)
- [ ] Nested `{{ if }}` blocks throw `TemplateRenderException` with clear message

**Technical tasks:**
- [x] `TASK-016-01` - **[Class]** Create `DocxConditionalBlockRenderer` in `Infrastructure/Rendering/` ✅ 2026-04-02
- [x] `TASK-016-02` - **[Method]** Implement `RenderConditionals(WordprocessingDocument doc, Dictionary<string, bool> conditions)` ✅ 2026-04-02
- [x] `TASK-016-03` - **[Detection]** Find `{{ if key }}` and `{{ end }}` markers ✅ 2026-04-02
- [x] `TASK-016-04` - **[Validation]** Detect nested `{{ if }}` blocks and throw exception ✅ 2026-04-02
- [x] `TASK-016-05` - **[Removal]** Remove block content if condition is false ✅ 2026-04-02
- [x] `TASK-016-06` - **[Cleanup]** Remove marker paragraphs/rows ✅ 2026-04-02
- [x] `TASK-016-07` - **[Test]** Unit tests for true condition (content kept) ✅ 2026-04-02
- [x] `TASK-016-08` - **[Test]** Unit tests for false condition (content removed) ✅ 2026-04-02
- [x] `TASK-016-09` - **[Test]** Unit tests for nested `{{ if }}` error ✅ 2026-04-02

**Business rules:**
1. Conditional keys are case-sensitive
2. Values must be boolean (true/false) — non-boolean values treated as false
3. Nested conditionals explicitly not supported

**Dependencies:** US-013
**Estimation:** 4-5 days

**Implementation notes:**
- Scan for `{{ if }}` markers, track depth to detect nesting
- Remove all elements between markers if condition is false

---

#### [US-017] - Placeholder extraction from DOCX
**Status:** 🔵 IN PROGRESS
**Start date:** 2026-04-02 12:00
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 3

**As a** system
**I want** to auto-detect all `{{ key }}` placeholders in a DOCX file
**So that** the manifest can be populated automatically

**Specification context:**
> F-306: Placeholder extraction handles split runs and headers/footers. Used for manifest population and validation warnings.

**Acceptance criteria:**
- [ ] Extracts all `{{ key }}` placeholders from main body
- [ ] Extracts placeholders from headers and footers
- [ ] Handles split runs via run merger
- [ ] Returns deduplicated list of placeholder keys
- [ ] Collection markers (`{{ collection_key }}`, `{{ end }}`) excluded
- [ ] Conditional markers (`{{ if key }}`, `{{ end }}`) excluded
- [ ] Item placeholders (`{{ item.field }}`) included

**Technical tasks:**
- [x] `TASK-017-01` - **[Class]** Create `DocxPlaceholderParser` in `Infrastructure/Rendering/` ✅ 2026-04-02
- [x] `TASK-017-02` - **[Method]** Implement `ExtractPlaceholders(WordprocessingDocument doc) → List<string>` ✅ 2026-04-02
- [x] `TASK-017-03` - **[Regex]** Match `{{ key }}` pattern (exclude `{{ if }}`, `{{ end }}`) ✅ 2026-04-02
- [x] `TASK-017-04` - **[Traversal]** Scan main body, headers, footers (after run merging) ✅ 2026-04-02
- [x] `TASK-017-05` - **[Deduplication]** Return unique set of keys ✅ 2026-04-02
- [ ] `TASK-017-06` - **[Test]** Unit tests for placeholder extraction
- [ ] `TASK-017-07` - **[Test]** Unit tests for headers/footers extraction

**Business rules:**
1. Placeholder extraction runs after run merging (prerequisite)
2. Collection/conditional markers are filtered out (not data placeholders)

**Dependencies:** US-013
**Estimation:** 3-4 days

---

#### [US-018] - Manifest validation with upload-time warnings
**Status:** 🟡 TODO
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 3

**As a** Claire (Template Designer)
**I want** the system to warn me about undeclared placeholders at upload time
**So that** I can catch typos or missing manifest entries before publishing

**Specification context:**
> F-307: Validation runs at upload time only (non-blocking). API response includes `warnings` array for undeclared placeholders. No hard block at publish.

**Acceptance criteria:**
- [ ] Upload response includes `warnings` array in `TemplateDto`
- [ ] Warnings list placeholders found in DOCX but not in manifest
- [ ] Upload succeeds even if warnings present (non-blocking)
- [ ] No validation at publish time (deferred to upload)

**Technical tasks:**
- [ ] `TASK-018-01` - **[DTO]** Add `Warnings` property to `TemplateDto`
- [ ] `TASK-018-02` - **[Service]** Compare extracted placeholders to manifest at upload
- [ ] `TASK-018-03` - **[Service]** Populate warnings for undeclared keys
- [ ] `TASK-018-04` - **[Test]** Unit tests for validation warnings
- [ ] `TASK-018-05` - **[Test]** API integration test with undeclared placeholders

**Business rules:**
1. Validation is informational only (does not block upload)
2. Warnings shown in API response, not in UI (deferred to future iteration)

**Dependencies:** US-017
**Estimation:** 1-2 days

---

### Epic 3b: HTML Renderer Enhancements (Email/SMS)

> Extend the existing Scriban-based HTML renderer for Email and SMS channels with custom functions.

#### [US-019] - Custom Scriban functions for HTML templates
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 3b

**As a** Claire (Template Designer)
**I want** `{{ format_date }}` and `{{ format_currency }}` functions in Email/SMS templates
**So that** I can format data consistently without manual string manipulation

**Specification context:**
> F-305: Custom Scriban functions `format_date` and `format_currency` registered at startup. Email/SMS only (not available in DOCX renderer).

**Acceptance criteria:**
- [x] `format_date` function: `{{ format_date invoiceDate "dd/MM/yyyy" }}` ✅
- [x] `format_currency` function: `{{ format_currency amount "€" }}` ✅
- [x] Functions registered in Scriban template context at startup ✅
- [x] Functions work in Email and SMS templates (tested) ✅
- [x] Functions not available in DOCX renderer (documented) ✅

**Technical tasks:**
- [x] `TASK-019-01` - **[Function]** Implement `FormatDateFunction` in `Application/Rendering/CustomFunctions/` ✅ 2026-03-29
- [x] `TASK-019-02` - **[Function]** Implement `FormatCurrencyFunction` in `Application/Rendering/CustomFunctions/` ✅ 2026-03-29
- [x] `TASK-019-03` - **[Registration]** Register functions in Scriban `TemplateContext` at startup ✅ 2026-03-29
- [x] `TASK-019-04` - **[Test]** Unit tests for `format_date` with various formats ✅ 2026-03-29
- [x] `TASK-019-05` - **[Test]** Unit tests for `format_currency` with various symbols ✅ 2026-03-29
- [x] `TASK-019-06` - **[Doc]** Update HTML template designer guide with function usage ✅ 2026-03-29

**Business rules:**
1. Functions are global (available in all Email/SMS templates)
2. Date format strings use .NET standard format specifiers
3. Currency symbol is a prefix (e.g., `€100.00`)

**Dependencies:** None (independent of DOCX engine)
**Estimation:** 2 days

---

### Epic 4: Preview & Dispatch Integration

> Integrate DOCX rendering into the preview and dispatch pipelines, replacing the old HTML→PDF flow.

#### [US-020] - DOCX template preview
**Status:** 🟡 TODO
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 4

**As a** Marc (Campaign Manager)
**I want** to preview a Letter template with sample data and download the rendered DOCX
**So that** I can verify layout before scheduling

**Specification context:**
> F-401: DOCX template preview. Marc receives a rendered `.docx` file for download and opens it in Word to verify layout.

**Acceptance criteria:**
- [ ] Preview endpoint accepts sample data for Letter templates
- [ ] Renders DOCX using full rendering pipeline (run merge + scalar + collections + conditionals)
- [ ] Returns DOCX bytes for download
- [ ] MIME type: `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
- [ ] Response header: `Content-Disposition: attachment; filename="preview-{templateName}.docx"`

**Technical tasks:**
- [ ] `TASK-020-01` - **[API]** Add `POST /api/templates/{id}/preview` endpoint
- [ ] `TASK-020-02` - **[Service]** Implement `TemplatePreviewService.PreviewDocxAsync(id, sampleData)`
- [ ] `TASK-020-03` - **[Rendering]** Wire to `IDocxTemplateRenderer`
- [ ] `TASK-020-04` - **[Response]** Return DOCX bytes with correct headers
- [ ] `TASK-020-05` - **[Test]** API integration test with sample data
- [ ] `TASK-020-06` - **[Test]** Manual test: download and open in Word

**Business rules:**
1. Sample data structure must match template manifest
2. Preview does not save rendered output (ephemeral)

**Dependencies:** US-013, US-014, US-015, US-016
**Estimation:** 3-4 days

---

#### [US-021] - Per-recipient DOCX dispatch
**Status:** 🟡 TODO
**Priority:** 🔴 High
**Complexity:** L
**Epic:** Epic 4

**As a** system
**I want** `ProcessChunkJob` to render one DOCX per recipient
**So that** each recipient gets a personalized letter

**Specification context:**
> F-402: One DOCX per recipient. No batch accumulation. Old PDF accumulation + `FlushBatchAsync` removed. Each `SendAsync` call is atomic.

**Acceptance criteria:**
- [ ] `ProcessChunkJob` reads DOCX body from file system
- [ ] Calls `IDocxTemplateRenderer.RenderAsync` once per recipient
- [ ] Passes rendered DOCX bytes via `DispatchRequest.BinaryContent`
- [ ] Calls `LetterDispatcher.SendAsync` once per recipient (no batching)
- [ ] Old PDF accumulation logic removed

**Technical tasks:**
- [ ] `TASK-021-01` - **[Job]** Update `ProcessChunkJob` to read DOCX from `ITemplateBodyStore`
- [ ] `TASK-021-02` - **[Job]** Call `IDocxTemplateRenderer.RenderAsync(docxStream, recipientData)`
- [ ] `TASK-021-03` - **[Job]** Set `DispatchRequest.BinaryContent` with rendered bytes
- [ ] `TASK-021-04` - **[Job]** Call `LetterDispatcher.SendAsync` per recipient
- [ ] `TASK-021-05` - **[Cleanup]** Remove old PDF accumulation code
- [ ] `TASK-021-06` - **[Test]** Integration test for chunk job with DOCX template
- [ ] `TASK-021-07` - **[Test]** Verify one file per recipient in output

**Business rules:**
1. Rendering must complete within 10 s timeout (existing setting)
2. Render failures are retried per Hangfire policy

**Dependencies:** US-013, US-014, US-015, US-016, US-022
**Estimation:** 5-6 days

**Implementation notes:**
- Reuse existing timeout configuration (`TemplateRenderTimeoutSeconds`)
- Remove `PdfBatchAccumulator` and related classes

---

#### [US-022] - DispatchRequest schema change for BinaryContent
**Status:** ✅ DONE
**Start date:** 2026-03-29
**End date:** 2026-03-29
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 4

**As a** system
**I want** `DispatchRequest` to carry pre-rendered DOCX bytes
**So that** the dispatcher can write files directly without re-rendering

**Specification context:**
> F-403b: Extend `DispatchRequest` record with `BinaryContent` (`byte[]?`). `Content` (string) remains for Email/SMS. Letter sets `BinaryContent`; Email/SMS set `Content`.

**Acceptance criteria:**
- [x] `DispatchRequest` record has `BinaryContent` property (`byte[]?`, nullable) ✅
- [x] `Content` property remains (`string?`, nullable) ✅
- [x] For Letter dispatch: `BinaryContent` set, `Content` null ✅
- [x] For Email/SMS dispatch: `Content` set, `BinaryContent` null ✅
- [x] Existing Email/SMS dispatchers unaffected ✅

**Technical tasks:**
- [x] `TASK-022-01` - **[Model]** Add `BinaryContent` property to `DispatchRequest` record ✅ 2026-03-29
- [x] `TASK-022-02` - **[Validation]** Document mutual exclusivity (only one set at a time) ✅ 2026-03-29
- [x] `TASK-022-03` - **[Test]** Unit tests for record creation with both properties ✅ 2026-03-29

**Business rules:**
1. `BinaryContent` and `Content` are mutually exclusive (only one set per request)
2. Null checks in dispatchers determine which is set

**Dependencies:** None
**Estimation:** 1 day

---

#### [US-023] - LetterDispatcher DOCX file drop
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** M
**Epic:** Epic 4

**As a** system
**I want** `LetterDispatcher` to write one `.docx` file per recipient
**So that** the print provider receives individual DOCX files

**Specification context:**
> F-403: Rewritten `LetterDispatcher` accepts `DispatchRequest.BinaryContent` and writes one `.docx` file per recipient via `PrintProviderFileDropHandler`. No PDF consolidation, no CSV manifest, no `FlushBatchAsync`. Delete `LetterPostProcessor` and DinkToPdf references.

**Acceptance criteria:**
- [x] `LetterDispatcher.SendAsync` accepts `DispatchRequest` with `BinaryContent` ✅
- [x] Writes one `.docx` file via `PrintProviderFileDropHandler` ✅
- [x] File naming: `{campaignId}_{recipientId}_{timestamp}.docx` ✅
- [x] Returns `DispatchResult.Success` on successful write ✅
- [x] Returns `DispatchResult.Fail` if `BinaryContent` is null/empty ✅
- [x] Disabled channel returns failure without writing ✅
- [x] I/O failure throws `LetterDispatchException` (transient, retriable) ✅
- [x] Old `LetterPostProcessor` removed from codebase ✅
- [x] DinkToPdf/wkhtmltox references removed from Letter channel ✅

**Technical tasks:**
- [x] `TASK-023-01` - **[Service]** Rewrite `LetterDispatcher.SendAsync` for DOCX ✅ 2026-03-30
- [x] `TASK-023-02` - **[Handler]** Wire to `PrintProviderFileDropHandler.WriteFileAsync` ✅ 2026-03-30
- [x] `TASK-023-03` - **[Naming]** Implement file naming convention ✅ 2026-03-30
- [x] `TASK-023-04` - **[Validation]** Check `BinaryContent` not null/empty ✅ 2026-03-30
- [x] `TASK-023-05` - **[Exception]** Map I/O failures to `LetterDispatchException` ✅ 2026-03-30
- [x] `TASK-023-06` - **[Cleanup]** Remove `LetterPostProcessor` class ✅ 2026-03-30
- [x] `TASK-023-07` - **[Cleanup]** Remove DinkToPdf package reference from Letter channel ✅ 2026-03-30
- [x] `TASK-023-08` - **[Cleanup]** Remove `FlushBatchAsync` logic ✅ 2026-03-30
- [x] `TASK-023-09` - **[Test]** Unit tests per F-404 coverage requirements ✅ 2026-03-30

**Business rules:**
1. One `SendAsync` call = one DOCX file written
2. No batch accumulation or consolidation
3. Print provider confirmed acceptance of `.docx` format (Q8 resolved)

**Dependencies:** US-022
**Estimation:** 4-5 days

**Implementation notes:**
- Use `PrintProviderFileDropHandler` (existing abstraction)
- Remove references to `PdfConsolidationService` and `PdfSharp` in Letter dispatcher

---

### Epic 5: Version History & Diff

> Track template version history and provide audit trail with binary diff indicator.

#### [US-024] - DOCX version history audit trail
**Status:** 🟡 TODO
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 5

**As a** Claire (Template Designer)
**I want** to see the version history of a Letter template
**So that** I have an audit trail of who changed what and when

**Specification context:**
> F-501: Version history as audit trail only. No per-version download, no revert action. Displays version number, author, timestamp, file path.

**Acceptance criteria:**
- [ ] `GET /api/templates/{id}/history` returns version history
- [ ] Each entry includes: version number, `ChangedBy`, timestamp, `BodyPath`
- [ ] History entries sorted by version descending (newest first)
- [ ] UI displays version history table on template edit page
- [ ] No download link for historical versions (current version only)
- [ ] No revert action (documented as out of scope)

**Technical tasks:**
- [ ] `TASK-024-01` - **[API]** Add `GET /api/templates/{id}/history` endpoint
- [ ] `TASK-024-02` - **[Service]** Implement `TemplateService.GetVersionHistoryAsync(id)`
- [ ] `TASK-024-03` - **[DTO]** Create `TemplateVersionDto` with required fields
- [ ] `TASK-024-04` - **[UI]** Add version history table to `EditTemplate.cshtml`
- [ ] `TASK-024-05` - **[Test]** API integration test for history retrieval

**Business rules:**
1. All versions retained indefinitely (no cleanup job)
2. History is read-only (no revert, no per-version download)

**Dependencies:** US-006, US-008
**Estimation:** 2 days

---

#### [US-025] - Binary diff indicator with SHA-256 checksum
**Status:** 🟡 TODO
**Priority:** 🟢 Low
**Complexity:** S
**Epic:** Epic 5

**As a** Claire (Template Designer)
**I want** to see whether DOCX content changed between versions
**So that** I can identify which updates were content vs. metadata-only

**Specification context:**
> F-503: SHA-256 checksum stored in `BodyChecksum` column. Computed from uploaded stream before writing to disk. Version history shows "content changed: yes/no" based on checksum comparison.

**Acceptance criteria:**
- [ ] SHA-256 checksum computed on upload (before file write)
- [ ] Checksum stored in `Template.BodyChecksum` (nvarchar(64))
- [ ] Checksum stored in `TemplateHistory.BodyChecksum` (nvarchar(64))
- [ ] Version history API response includes `ContentChanged` boolean flag
- [ ] Flag is true if checksum differs from previous version
- [ ] Flag is false if checksum matches previous version

**Technical tasks:**
- [ ] `TASK-025-01` - **[Service]** Implement SHA-256 computation in `TemplateService.CreateAsync`
- [ ] `TASK-025-02` - **[Service]** Implement SHA-256 computation in `TemplateService.UpdateAsync`
- [ ] `TASK-025-03` - **[Computation]** Compute checksum from stream (single-pass, before write)
- [ ] `TASK-025-04` - **[DTO]** Add `ContentChanged` flag to `TemplateVersionDto`
- [ ] `TASK-025-05` - **[Service]** Compare checksums between consecutive versions
- [ ] `TASK-025-06` - **[Test]** Unit tests for checksum computation
- [ ] `TASK-025-07` - **[Test]** Unit tests for content change detection

**Business rules:**
1. Checksum computed once on upload (no extra disk read)
2. Hex string format (64 characters)
3. Checksum is nullable (historical records may not have it)

**Dependencies:** US-003, US-024
**Estimation:** 2 days

**Implementation notes:**
- Use `System.Security.Cryptography.SHA256` with stream input
- Store as lowercase hex string

---

### Epic 6: Technical Debt & Maintenance
> Ongoing technical improvements, infrastructure work, and maintenance tasks that keep the codebase healthy and the application operational.

#### [US-026] - Initialize database with a default admin user
**Status:** ✅ DONE
**Start date:** 2026-03-30
**End date:** 2026-03-30
**Priority:** 🔴 High
**Complexity:** S
**Epic:** Epic 6
**Type:** [Requirement]

**As a** product team
**I want to** ensure the database is seeded with a default admin user on first deployment
**So that** the application is immediately accessible without requiring manual account creation in an empty database

**Context:**
> On a fresh deployment the application has no users, making it impossible to log in or configure the system. A default admin account seeded at startup removes this bootstrap problem.

**Acceptance criteria:**
- [x] A default admin user is created on application startup if no admin exists ✅
- [x] Credentials (username/email + password) are configurable via `appsettings.json` or environment variables ✅
- [x] Default admin is assigned the Administrator role ✅
- [x] Seeding is idempotent — re-running does not create duplicate accounts ✅
- [x] Default credentials are documented and a warning is logged at startup if defaults are still in use ✅

**Technical tasks:**
- [x] `TASK-026-01` - **[Config]** Add `DefaultAdmin` section to `appsettings.json` (email, password, username) ✅ 2026-03-30
- [x] `TASK-026-02` - **[Model]** Create `DatabaseSeeder` service in Infrastructure layer ✅ 2026-03-30
- [x] `TASK-026-03` - **[Script]** Implement idempotent seeding logic using `IIdentityService` ✅ 2026-03-30
- [x] `TASK-026-04` - **[CI]** Register and invoke `DatabaseSeeder` in `Program.cs` after migrations ✅ 2026-03-30
- [x] `TASK-026-05` - **[Test]** Unit tests for seeder: no-op when admin already exists, creates admin when absent ✅ 2026-03-30

**Dependencies:** None
**Estimation:** 1-2 days

---

## 📋 Suggested Roadmap

### Sprint 1 — Foundation & Storage Layer (Weeks 1-2)
- US-001 — ITemplateBodyStore abstraction
- US-002 — FileSystemTemplateBodyStore implementation
- US-003 — Database schema migration
- US-004 — Startup validation
- US-005 — Atomic file write with concurrency guard

### Sprint 2 — DOCX Core Engine (Weeks 3-4)
- US-009 — DOCX structural validation
- US-010 — File size limit
- US-013 — XML run merging
- US-014 — Scalar placeholder replacement
- US-017 — Placeholder extraction

### Sprint 3 — Advanced Rendering & Upload (Weeks 5-6)
- US-015 — Collection rendering
- US-016 — Conditional blocks
- US-018 — Manifest validation
- US-006 — DOCX binary storage
- US-011 — Multipart upload API

### Sprint 4 — Integration & Dispatch (Weeks 7-8)
- US-007 — HTML body storage (Email/SMS)
- US-019 — Custom Scriban functions
- US-020 — DOCX preview
- US-021 — Per-recipient dispatch
- US-022 — DispatchRequest schema
- US-023 — LetterDispatcher rewrite

### Sprint 5 — Polish & Documentation (Week 9)
- US-008 — DOCX download endpoint
- US-012 — Conditional UI toggle
- US-024 — Version history audit trail
- US-025 — Binary diff indicator
- Performance benchmarks
- Edge case testing
- Designer guide (release gate)

---

## ⚠️ Risks & Constraints

### Identified Risks

1. **Word splits placeholders across XML runs**
   - Impact: High
   - Mitigation: `DocxRunMerger` with smart-quote normalization; extensive test coverage

2. **Print provider compatibility**
   - Impact: Critical
   - Resolution: Q8 confirmed — provider accepts `.docx`

3. **File system unavailability**
   - Impact: High
   - Mitigation: Startup fail-fast validation; Hangfire retry on transient failures

4. **Concurrent file writes**
   - Impact: Medium
   - Mitigation: EF Core optimistic concurrency (rowversion) → HTTP 409

### Technical Constraints

- No native DLLs required (pure .NET via `DocumentFormat.OpenXml`)
- Email/SMS rendering logic unchanged (only storage layer modified)
- Storage root must be writable at startup
- Initial deployment: local single-instance file system only
- No down-migration provided (rollback requires DB restore)

---

## 📚 References

- **Source specification:** `_docs/prd-docx-letter-channel.md` (Version 1.5)
- **Analysis date:** 2026-03-29
- **Architecture:** Clean Architecture (Domain → Application → Infrastructure ← Web)
- **Repository pattern:** `IUnitOfWork` + `IRepository<T>` (no direct `DbContext` injection in services)

---

## 📝 Notes

### Breaking Changes
- **Template body storage:** All channels (Letter, Email, SMS) migrate from DB columns to file system
- **Database schema:** `BodyPath` replaces `HtmlBody`/`DocxBody`; adds `BodyChecksum` and `RowVersion`
- **Letter channel:** Complete rewrite — old HTML→PDF pipeline removed, replaced with DOCX→DOCX

### Out of Scope
- Online DOCX editor
- Sub-template composition for DOCX
- Visual diff between DOCX versions
- PDF output for Letter channel (output is DOCX)
- `{{ else }}` blocks in conditionals
- Nested `{{ if }}` blocks
- Word text boxes (placeholders not processed)
- Template version revert
- Multi-instance file system (network share support deferred)
- Blob storage / cloud storage (future extension)

### Key Technologies
- `DocumentFormat.OpenXml` (Microsoft, MIT license) — DOCX manipulation
- Scriban — HTML template rendering (Email/SMS only)
- Hangfire — Background job orchestration
- EF Core 8 — ORM with optimistic concurrency

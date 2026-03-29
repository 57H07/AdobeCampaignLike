# PRD: Letter Channel Migration — HTML to DOCX Templates

> Version: 1.3 — Updated 2026-03-29
> Status: Draft

---

## Changelog

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-03-28 | Initial draft |
| 1.1 | 2026-03-29 | Removed PDF/Syncfusion output (output stays DOCX). Added file-system storage for template bodies (breaking change: DB stores paths, not content). Simplified rendering engine (scalar replacement + table collection support only, no paragraph duplication, no exotic fields). Locked upload to `.docx` only. Clarified validation rules. Marked headers/footers as Should-have. Added HTML custom functions requirement. Expanded multipart upload API spec. Added LetterDispatcher test coverage requirement. |
| 1.2 | 2026-03-29 | Clarified snapshot body storage (moves to file system, no DB column). Specified `ITemplateBodyStore` read semantics (throw on missing/corrupt, distinguish NotFound vs. Corrupted). Moved F-305 (Scriban custom functions) to new Epic 3b. Fixed DOCX dispatch model: one recipient = one DOCX file, no batch accumulation. Clarified orphaned file cleanup (external background job, out-of-scope). Promoted F-308 (headers/footers) to Must-have. Declared nested conditionals out of scope. Moved Word text boxes (Q4) to Out of Scope. Changed F-503 diff indicator to SHA-256 checksum. Added `DispatchRequest` schema change. Added startup fail-fast check for storage root. No data migration needed (never in production). |
| 1.3 | 2026-03-29 | Resolved Q8: print provider confirmed to accept `.docx`. Added `BodyChecksum` (nvarchar(64), nullable) to `Template`, `TemplateHistory`, and `TemplateSnapshot` entities (F-503). Clarified file-on-update semantics: previous body is **copied** (not moved) to `history/v{n}.docx` before new version is written. Clarified `TemplateDto.bodyPath` exposes a relative path only (no server root). Amended F-301: `DocxRunMerger` must traverse `HeaderPart` and `FooterPart` in addition to the main document body. Specified F-303 missing-`{{ end }}` behavior: throw `TemplateRenderException` with "Missing {{ end }} for collection '...'." Specified F-104b startup check wiring: `IHostedService` (not DI registration). Resolved Q8 in Open Questions. |

---

## 1. Executive Summary

CampaignEngine's Letter channel currently forces designers to author print-ready letter templates in HTML — a format designed for screens, not paper. This PRD describes the migration of the Letter channel from an HTML→PDF pipeline to a DOCX→DOCX pipeline. Designers will create letter templates in Microsoft Word using familiar `{{ placeholder }}` syntax, upload the `.docx` file through the web UI, and the engine will handle scalar placeholder substitution and table-driven collection rendering. The final output delivered to the print provider file drop is a **rendered `.docx` file** — no PDF conversion step. Email and SMS channels remain unchanged. There are no existing letter templates in production, making this a clean-break migration with no backward-compatibility burden.

### Breaking Change: Template Body Storage

This initiative also introduces a **breaking change to template storage** that affects all channels (Letter, Email, SMS): template body content (DOCX binary and HTML text) is no longer stored in the database. Instead, template bodies are stored as files on the server file system, and the database stores only the file path. This decouples binary payload size from database row size, improves query performance, and aligns with file-oriented workflows.

This applies to **all three body-storing entities**: `Template`, `TemplateHistory`, and `TemplateSnapshot`. Snapshot bodies (frozen at campaign scheduling time) are also stored as files on the file system — the database stores only the path. There is no `ResolvedHtmlBody` / `ResolvedDocxBody` column in the database for snapshots.

> **Note:** No data migration is required. The application has never been in production.

---

## 2. Problem Statement

### Current Pain Points

- **HTML is not a print medium.** Designers must hand-craft HTML/CSS with precise A4 margins, page breaks, and print-friendly fonts. This requires web development skills that most marketing/template designers don't have.
- **WYSIWYG gap.** What designers see in an HTML editor does not reliably match the PDF output produced by wkhtmltox. Margins shift, fonts substitute, page breaks land unexpectedly.
- **Layout limitations.** Complex letter layouts (multi-column addresses, logos with text wrapping, official letterheads, signature blocks) are tedious or fragile to implement in HTML for print.
- **Tooling mismatch.** Designers are proficient in Microsoft Word. Forcing them into HTML authoring increases error rates, slows iteration, and creates a dependency on developers for template fixes.
- **DB bloat.** Storing HTML and DOCX body content in `nvarchar(max)` / `varbinary(max)` columns inflates database row sizes, makes backup/restore slower, and conflates content storage with metadata queries.

### Why Existing Solutions Are Insufficient

- The current DinkToPdf/wkhtmltox pipeline converts HTML faithfully as a webpage, but it cannot replicate the precise typographic and layout control that Word provides for printed documents.
- There is no intermediate DOCX step — the system is architecturally coupled to HTML at every layer (entity storage, rendering, snapshotting, versioning, preview, placeholder extraction).
- Template body content is stored directly in the database, coupling binary payload management to the ORM layer.

### Opportunity

Migrating to DOCX gives designers full control over letter layout using their primary tool (Word), eliminates the HTML/CSS expertise requirement, and produces higher-fidelity printed output. Moving template body storage to the file system decouples payload management from relational data and is a prerequisite for handling DOCX binaries efficiently.

---

## 3. Target Users & Personas

### Persona 1: Claire — Template Designer
- **Role:** Marketing communications designer
- **Goals:** Create professional, brand-compliant letter templates with dynamic data fields. Iterate quickly on layout and typography.
- **Frustrations:** Currently must write HTML/CSS for print layout. Preview often doesn't match PDF output. Cannot use Word features she's expert in (styles, tables, text boxes, headers/footers).
- **Key behaviors:** Works primarily in Microsoft Word. Uploads assets via the CampaignEngine web UI. Tests with preview before publishing.

### Persona 2: Marc — Campaign Manager
- **Role:** Marketing operations manager
- **Goals:** Assemble campaigns with letter steps, select templates, preview personalized output, schedule sends.
- **Frustrations:** Template preview for letters is unreliable (HTML rendering vs. actual output). Must frequently go back to designers for layout corrections.
- **Key behaviors:** Uses the campaign builder UI. Reviews previews with sample data. Monitors batch dispatch status.

### Persona 3: Sarah — System Administrator
- **Role:** IT/DevOps engineer
- **Goals:** Deploy and maintain the CampaignEngine platform. Manage dependencies, monitor performance, handle upgrades.
- **Frustrations:** wkhtmltox native library deployment is fragile (platform-specific DLL, version conflicts). Wants pure-.NET dependencies where possible.
- **Key behaviors:** Manages NuGet packages, configures deployment pipelines, monitors infrastructure. Also manages file-system storage paths and backup policies.

### Persona 4: Thomas — API Integrator
- **Role:** Developer at a partner agency
- **Goals:** Automate template uploads and campaign creation via the REST API.
- **Frustrations:** Current API only accepts HTML body as a JSON string. Needs multipart upload support for binary files.
- **Key behaviors:** Uses `POST /api/templates` and campaign endpoints. Tests via Postman/curl. Reads API documentation.

---

## 4. Goals & Success Criteria

| # | KPI | Target |
|---|-----|--------|
| G1 | Letter template creation time (designer) | ≤ 15 minutes for a standard 1-page letter (down from ~45 min for HTML) |
| G2 | Template upload success rate | > 99% of valid `.docx` files upload without error |
| G3 | Placeholder extraction accuracy | 100% of `{{ key }}` placeholders detected, including those split across Word XML runs |
| G4 | DOCX output fidelity | Word features (styles, tables, images, headers/footers) are preserved intact in the rendered output DOCX |
| G5 | Per-recipient DOCX rendering time | < 500 ms per letter at p95 |
| G6 | Zero regression on Email/SMS channels | All existing Email and SMS template tests pass without modification |
| G7 | File system storage migration | All template bodies (HTML and DOCX) stored as files; database contains only paths |

---

## 5. Features & Requirements

### 5.1 Functional Requirements

#### Epic 1: File-System Template Storage (Breaking Change — All Channels)

This epic applies to **all channels** (Letter, Email, SMS). It is a prerequisite for Epic 2.

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-101 | File-system storage for template bodies | As the system, I need to store template body files (HTML for Email/SMS, DOCX for Letter) on the server file system instead of in the database, so that binary payloads are decoupled from relational data | Must-have |
| F-102 | Database stores file paths only | As the system, the `Template`, `TemplateHistory`, and `TemplateSnapshot` entities must store a `BodyPath` (`nvarchar`, file relative path) instead of `HtmlBody` / `DocxBody` columns, and a `BodyChecksum` (`nvarchar(64)`, nullable) for SHA-256 content comparison. `BodyPath` exposes a **relative path only** in DTOs (e.g. `templates/{id}/v3.docx`) — the server storage root is never included in API responses. | Must-have |
| F-103 | ITemplateBodyStore interface | As the system, I need an `ITemplateBodyStore` interface abstracting file read/write operations, so the storage location (local disk, network share, future blob storage) can be swapped without changing service logic. **Read semantics:** `ReadAsync(path)` throws `TemplateBodyNotFoundException` if the file does not exist (file was never written or path is null/empty); throws `TemplateBodyCorruptedException` if the file exists but cannot be opened or parsed as valid content. The distinction between these two exceptions allows callers to surface appropriate error messages. | Must-have |
| F-104 | Configurable storage root | As Sarah, I want the template storage root directory configured via `appsettings.json` (`TemplateStorage:RootPath`) so that I can point it to a network share or mounted volume | Must-have |
| F-104b | Startup fail-fast for storage root | As Sarah, I want the application to fail at startup with a clear error message if `TemplateStorage:RootPath` is not configured, does not exist, or is not writable by the application service account — so misconfiguration is detected immediately at deploy time rather than at first template upload. **Wiring:** implemented as an `IHostedService` that runs at application start and throws if the validation fails, preventing the host from starting. | Must-have |
| F-105 | Atomic write + path commit | As the system, I need template file writes and database path commits to be atomic: write the file first, then commit the path to the DB; on DB failure, delete the orphaned file immediately in the same request. Periodic orphaned-file scanning (for files that survived despite a DB failure) is handled by an external background job — that job is out of scope for this initiative. **On update:** the current body file is **copied** (not moved) to `history/v{n}.docx` before the new version is written as `v{n+1}.docx`. Both files coexist on disk; TemplateHistory stores the history path. This ensures safe revert without relying on a file rename being atomic. | Must-have |
| F-106 | DOCX binary storage | As Claire, I want my uploaded `.docx` file stored on the server so that I can download it later for editing | Must-have |
| F-107 | HTML body storage | As the system, I need HTML template bodies (Email/SMS) stored as `.html` files on the server, with the path saved in the database | Must-have |
| F-108 | DOCX versioning via file system | As Claire, I want each template update to save the previous DOCX version as a separate file so that I can revert to an earlier version | Must-have |
| F-109 | Body snapshot on schedule | As Marc, I want the template body file (DOCX for Letter, HTML for Email/SMS) copied to a snapshot path when a campaign is scheduled, so that later template edits don't affect running campaigns. The `TemplateSnapshot` entity stores only `BodyPath` (string) — no body content in the database. | Must-have |
| F-110 | DOCX download endpoint | As Thomas, I want to download the current DOCX file via `GET /api/templates/{id}/docx` so that I can inspect or re-edit it offline | Should-have |

**File naming convention:**
- Template bodies: `{storageRoot}/templates/{templateId}/v{version}.docx` (Letter) or `v{version}.html` (Email/SMS)
- Snapshots: `{storageRoot}/snapshots/{snapshotId}.docx` or `.html`
- History entries share the template directory: `{storageRoot}/templates/{templateId}/history/v{version}.docx`

#### Epic 2: DOCX Template Upload & Validation

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-201 | File upload via web UI | As Claire, I want to upload a `.docx` file when creating a Letter template so that I can author templates in Word | Must-have |
| F-202 | File re-upload on edit | As Claire, I want to re-upload a new `.docx` file to update an existing Letter template | Must-have |
| F-203 | DOCX structural validation | As the system, I need to validate uploaded files as follows: (1) file extension must be `.docx` — `.docm` and other formats are rejected with HTTP 422; (2) file must be a valid ZIP archive; (3) the ZIP must contain `[Content_Types].xml` at the root — missing this part is treated as a corrupt file and rejected; (4) the ZIP must open successfully as a `WordprocessingDocument` via the OpenXML SDK; (5) files containing a `vbaProject.bin` part (VBA macros) are rejected | Must-have |
| F-204 | File size limit | As Sarah, I want uploads rejected if they exceed 10 MB to prevent storage abuse. Enforced at Kestrel level (`[RequestSizeLimit]`) and re-validated in the service layer | Must-have |
| F-205 | API multipart upload | As Thomas, I want dedicated endpoints for Letter template upload/update | Must-have |
| F-206 | Conditional UI toggle | As Claire, I want the template creation form to show a file upload input when I select "Letter" channel and an HTML editor for other channels | Should-have |

**F-205 — Multipart Upload API Specification:**

`POST /api/templates/letter` — Create a new Letter template
- Content-Type: `multipart/form-data`
- Authorization: `X-Api-Key` header (existing auth) + `RequireDesignerOrAdmin` policy
- Parts:
  - `name` (string, required, max 200 chars): template display name — must be unique per channel
  - `description` (string, optional, max 500 chars)
  - `file` (binary, required): the `.docx` file
- Validation:
  - `file` extension must be `.docx` (case-insensitive). Returns HTTP 422 with `{ "error": "Only .docx files are accepted." }` for any other format including `.docm`.
  - `file` content type should be `application/vnd.openxmlformats-officedocument.wordprocessingml.document` — also accept `application/octet-stream` (curl default) and `application/zip`. Do not reject based on Content-Type alone; always validate by attempting `WordprocessingDocument.Open()`.
  - `file` size ≤ 10 MB. Returns HTTP 413 if exceeded.
  - `[Content_Types].xml` must be present inside the ZIP. Returns HTTP 422 with `{ "error": "Invalid DOCX: missing [Content_Types].xml." }`.
  - VBA macros (`vbaProject.bin` part present): returns HTTP 422 with `{ "error": "Macro-enabled documents are not accepted. Please save as .docx." }`.
- On success: HTTP 201, body = `TemplateDto` (includes `bodyPath` as a **relative path**, e.g. `templates/{id}/v1.docx` — the server storage root is never exposed; no binary content)
- On name collision: HTTP 409 with `{ "error": "A Letter template named '{name}' already exists." }`
- On validation failure: HTTP 422 with structured error

`PUT /api/templates/{id}/letter` — Update an existing Letter template
- Same Content-Type and Authorization as POST
- Parts: same as POST
- `file` is optional — if not provided, existing DOCX body is retained unchanged
- Behavior: creates a new TemplateHistory entry with the previous body path; writes new DOCX file; updates DB path
- On `id` not found: HTTP 404
- On channel mismatch (template is not Letter channel): HTTP 422 with `{ "error": "Template {id} is not a Letter template." }`

`GET /api/templates/{id}/docx` — Download the DOCX file
- Authorization: `RequireDesignerOrAdmin`
- Returns: DOCX bytes as `application/vnd.openxmlformats-officedocument.wordprocessingml.document` with `Content-Disposition: attachment; filename="{templateName}.docx"`
- On `id` not found: HTTP 404
- On template not being a Letter template: HTTP 422

#### Epic 3: DOCX Placeholder Engine

The rendering engine is intentionally **simple**. It operates on a single Word document, replacing `{{ key }}` placeholders with string values. The only supported "complex" feature is collection rendering via Word tables. There is no paragraph duplication, no exotic field support, no OLE execution.

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-301 | XML run merging | As the system, I need to merge Word's split XML runs (`<w:r>`) so that `{{ firstName }}` is recognized even when Word fragments it across multiple runs. The merger must preserve `<w:bookmarkStart>`, `<w:bookmarkEnd>`, and `<w:rPr>` run properties. Smart-quote normalization (`"` `"` → `"`) must be applied during merging. **Scope:** the merger must traverse the main document body **and** all `HeaderPart` and `FooterPart` XML parts — consistent with F-308 which requires placeholder replacement in headers/footers. Bookmark elements (`<w:bookmarkStart>`, `<w:bookmarkEnd>`) that fall inside a split placeholder are discarded during the merge; this is not expected in valid authoring workflows. | Must-have |
| F-302 | Scalar placeholder replacement | As Claire, I want `{{ firstName }}`, `{{ address }}`, etc. in my Word document replaced with recipient data. Values are XML-escaped before insertion to prevent OpenXML injection. Missing keys are replaced with an empty string. | Must-have |
| F-303 | Collection rendering via table rows | As Claire, I want to place a marker row `{{ collection_key }}` in a Word table, followed by a template row containing `{{ item.field }}` placeholders, followed by an `{{ end }}` row. The engine duplicates the template row once per item in the collection and removes the marker rows. This is the only supported collection pattern — no paragraph-level looping. **Error handling:** if a `{{ collection_key }}` marker row is found with no matching `{{ end }}` row, the renderer throws `TemplateRenderException` with the message "Missing {{ end }} for collection '{key}'." | Must-have |
| F-304 | Conditional support | As Claire, I want `{{ if condition_key }}...{{ end }}` to show or hide **whole paragraphs or whole table rows** based on a boolean value in recipient data. The `{{ if }}` and `{{ end }}` markers each occupy a dedicated paragraph or table row. `{{ else }}` is not supported in this version. **Nested conditionals are not supported** — `{{ if }}` blocks may not be nested inside other `{{ if }}` blocks. This is enforced at render time with a clear error. | Should-have |
| F-306 | Placeholder extraction from DOCX | As the system, I need to auto-detect all `{{ key }}` placeholders in a DOCX file (handling split runs and headers/footers) to populate the manifest | Must-have |
| F-307 | Manifest validation for DOCX | As Claire, I want the system to warn me if my DOCX contains undeclared placeholders before I publish | Must-have |
| F-308 | Headers/footers placeholder replacement | As Claire, I want placeholders in Word headers and footers to be replaced with recipient data, so that running headers (e.g., `{{ recipientName }}`) and footers (e.g., `{{ pageRef }}`) are personalized | Must-have |

**Rendering engine rules (non-negotiable):**
- One DOCX template file → one DOCX output file per recipient
- Scalar replacement: `{{ key }}` → value string (XML-escaped)
- Collection rendering: table-row duplication only (see F-303)
- Conditionals: paragraph/row block removal only (see F-304)
- No `{{ else }}` blocks
- No nested `{{ if }}` blocks — attempting to nest conditionals produces a render-time error
- No sub-template composition (`{{> name }}` is not supported for DOCX)
- No embedded image manipulation — images in the template are preserved as-is in the output
- No execution of Word fields (e.g., `=SUM(ABOVE)` table formulas, `DATE` fields, TOC fields) — these are left as-is in the output
- Word features preserved as-is: styles, themes, section breaks, page numbering, headers/footers (F-308), embedded images, tables, text boxes

#### Epic 3b: HTML Renderer Enhancements (Email/SMS)

These features extend the existing Scriban-based HTML renderer for Email and SMS channels. They have no dependency on the DOCX engine.

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-305 | Custom functions for HTML templates | As Claire, I want `{{ format_date invoiceDate "dd/MM/yyyy" }}` and `{{ format_currency amount "€" }}` to work in Email/SMS HTML templates, implemented as Scriban custom functions registered at startup. These functions are not available in the DOCX renderer. | Must-have |

#### Epic 4: Preview & Dispatch Integration

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-401 | DOCX template preview | As Marc, I want to preview a Letter template with sample data and receive a rendered `.docx` file for download, so I can open it in Word to verify layout before scheduling | Must-have |
| F-402 | Per-recipient DOCX dispatch | As the system, I need `ProcessChunkJob` to render one DOCX per recipient and call `LetterDispatcher.SendAsync` once per recipient. There is no batch accumulation — each `SendAsync` call is a complete, atomic dispatch unit that writes one file. The old PDF accumulation + `FlushBatchAsync` pattern is removed from the Letter channel. | Must-have |
| F-403 | LetterDispatcher DOCX file drop | As the system, I need the rewritten `LetterDispatcher` to accept pre-rendered DOCX bytes (via `DispatchRequest.BinaryContent`) and write one `.docx` file per recipient to the configured output directory via `PrintProviderFileDropHandler`. No PDF consolidation, no CSV manifest, no `FlushBatchAsync`. | Must-have |
| F-403b | DispatchRequest schema change | As the system, the `DispatchRequest` record must be extended with a `BinaryContent` (`byte[]?`) property to carry pre-rendered DOCX bytes. `Content` (string) remains for Email/SMS. For Letter dispatch, `BinaryContent` is set and `Content` is null; for Email/SMS, `Content` is set and `BinaryContent` is null. | Must-have |
| F-404 | LetterDispatcher test coverage | As the engineering team, I need the rewritten LetterDispatcher covered by unit tests verifying: (1) `SendAsync` with valid DOCX bytes writes a `.docx` file via `PrintProviderFileDropHandler`; (2) `SendAsync` with null/empty `BinaryContent` returns `DispatchResult.Fail`; (3) disabled channel returns failure without writing any file; (4) I/O failure from the file drop handler propagates as a transient `LetterDispatchException` | Must-have |

#### Epic 5: Version History & Diff

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-501 | DOCX version history | As Claire, I want to see the version history of a Letter template (version number, who changed, when, file path) | Must-have |
| F-502 | DOCX revert | As Claire, I want to revert a Letter template to a previous DOCX version (copies the historical file to a new current version) | Should-have |
| F-503 | Binary diff indicator | As Claire, I want to see whether the DOCX content changed between versions (yes/no — based on SHA-256 checksum comparison of the stored files), even though visual diff is not available for binary files. The checksum is computed at upload time and stored in a `BodyChecksum` column (`nvarchar(64)`, nullable) on `Template`, `TemplateHistory`, and `TemplateSnapshot`. | Nice-to-have |

### 5.2 Non-functional Requirements

| Category | Requirement |
|----------|-------------|
| **Performance** | Per-recipient DOCX rendering must complete in < 500 ms at p95 for a typical 1-3 page letter |
| **Performance** | Batch throughput must remain >= 250 letters/minute |
| **Scalability** | File-system storage must support templates up to 10 MB without degradation. Storage root must be configurable to a network share or mounted volume for multi-instance deployments |
| **Scalability** | Template history file accumulation must not cause noticeable query slowdowns — the database only stores paths (strings), not binaries |
| **Reliability** | Corrupt or malformed DOCX uploads must be rejected with a clear error, not crash the application |
| **Reliability** | Run-merging edge cases must not silently drop placeholders — unrecognized patterns must be logged at Warning level |
| **Reliability** | File write and DB path commit must be atomic: orphaned files (write succeeded, DB commit failed) must be cleaned up |
| **Maintainability** | Email/SMS pipeline code paths must remain completely unchanged in their rendering logic. Only their storage layer changes (HTML body moves from DB column to file path) |
| **Deployment** | No native DLLs or external server processes required (pure .NET via DocumentFormat.OpenXml only) |
| **Deployment** | Storage root directory must exist and be writable at startup — application must fail fast with a clear error if it is not (see F-104b) |

---

## 6. Out of Scope

The following are explicitly **not** part of this initiative:

- **Email/SMS rendering changes.** These channels continue to use HTML/Scriban rendering. Only their storage layer changes (body moved from DB to file system).
- **Online DOCX editor.** No in-browser Word editing. Designers use desktop Word and upload files.
- **Sub-template composition for DOCX.** The `{{> subtemplate_name }}` feature is not supported for DOCX — each letter template is a self-contained file.
- **Visual diff between DOCX versions.** Binary files cannot be meaningfully diffed in the UI. Only metadata and a "content changed: yes/no" indicator are shown.
- **Migration of existing HTML letter templates.** There are no letter templates in production. This is a clean break — the old HTML→PDF path for Letter is removed.
- **PDF output for the Letter channel.** Output is DOCX. No PDF conversion step is performed. DinkToPdf/wkhtmltox is removed as a dependency from the Letter channel.
- **`{{ else }}` blocks in conditionals.** Deferred to a future iteration.
- **Nested conditionals in DOCX.** `{{ if }}` blocks may not be nested inside other `{{ if }}` blocks. Attempting to nest them results in a render-time error.
- **Word text boxes (wps:txbx).** Placeholders inside Word text boxes (a separate XML part) are not processed by the run merger or placeholder engine. Text boxes are preserved as-is in the output.
- **Paragraph-level loop duplication.** Collections are rendered exclusively via table-row duplication. No paragraph cloning.
- **Word Mail Merge fields or Content Controls.** Placeholders use the same `{{ key }}` text syntax, not Word-native merge fields or structured document tags.
- **Dynamic image insertion.** Images embedded in the Word document are preserved as-is. No runtime image replacement.
- **Exotic Word fields.** `=SUM(ABOVE)`, `DATE`, `TOC`, and other Word calculated fields are not processed — they remain unchanged in the output DOCX.
- **Macro-enabled templates (`.docm`, `.dotm`).** Only `.docx` format is accepted. `.docm` files are rejected at upload with a clear error.
- **Blob storage / cloud file storage.** The `ITemplateBodyStore` interface supports this as a future extension, but the initial implementation targets local/network file system only.
- **Removal of DinkToPdf as a project dependency.** The `LetterPostProcessor` using DinkToPdf becomes dead code but is retained temporarily for rollback safety. It can be removed in a follow-up cleanup.
- **PDF batch consolidation changes.** `PdfConsolidationService` and PdfSharp are not used by the Letter channel after this migration. They are not removed in this initiative.
- **Supported Word features for designers:** SmartArt, WordArt, OLE objects, ActiveX controls, and advanced drawing objects are not officially supported — they are preserved in the output but rendering fidelity is not guaranteed.

---

## 7. Technical Constraints & Stack

| Component | Technology | Notes |
|-----------|-----------|-------|
| Runtime | .NET 8 | Existing platform, non-negotiable |
| Web Framework | ASP.NET Core (Razor Pages + REST controllers) | Existing |
| ORM | EF Core 8 + SQL Server | Existing; DB stores `BodyPath` (nvarchar), not binary content |
| Template body storage | Local or network file system | Configurable root path via `appsettings.json`. `ITemplateBodyStore` abstraction |
| DOCX Manipulation | `DocumentFormat.OpenXml` (Microsoft, MIT license) | Official OpenXML SDK. Used for run merging, scalar replacement, table-row collection rendering, conditional block removal |
| Background Jobs | Hangfire 1.8 (existing) | Unchanged — ProcessChunkJob orchestrates per-recipient rendering |
| Template Syntax | Scriban `{{ }}` in plain text within Word | DOCX uses custom OpenXML-based renderer. HTML/SMS continue using Scriban |
| Custom functions | Scriban custom function pipeline | `format_date`, `format_currency` registered as Scriban functions at startup — Email/SMS only |
| Architecture | Clean Architecture: Domain → Application → Infrastructure ← Web | Strict layer dependencies enforced |

---

## 8. External Dependencies & Integrations

| Dependency | Purpose | Risk Level | Required Setup |
|------------|---------|------------|----------------|
| `DocumentFormat.OpenXml` (NuGet) | DOCX XML manipulation (run merging, element traversal, document modification) | Low | `dotnet add package DocumentFormat.OpenXml` — MIT license, Microsoft-maintained, no native deps |
| Microsoft Word (designer workstation) | Template authoring tool | Low | Designers must have Word installed. Any version producing `.docx` (Office 2007+) |
| Server file system / network share | Template body storage | Medium | Storage root must be writable by the application service account. Must be included in backup policy. For multi-instance deployments, must be a shared network path (UNC or mounted volume) |
| PdfSharp 6.2.4 (existing) | PDF batch consolidation — not used by Letter after migration | Low | No changes required |

---

## 9. Security & Compliance

| Area | Requirement |
|------|-------------|
| **File upload validation** | (1) Extension must be `.docx` — reject `.docm`, `.dotx`, `.dotm`, and all non-DOCX formats with HTTP 422. (2) Must be a valid ZIP archive. (3) Must contain `[Content_Types].xml` at the ZIP root — missing this part → HTTP 422 "Invalid DOCX: missing [Content_Types].xml." (4) Must open successfully as `WordprocessingDocument` via OpenXML SDK. (5) Must not contain a `vbaProject.bin` part — macro-enabled files → HTTP 422 "Macro-enabled documents are not accepted." |
| **File size limit** | Maximum 10 MB per uploaded file. Enforced at Kestrel level via `[RequestSizeLimit(10_485_760)]` on the upload actions and re-validated in the service layer. |
| **Content sanitization** | Placeholder values inserted into DOCX are plain text. Values must be XML-escaped when written into `<w:t>` elements to prevent OpenXML injection. |
| **Smart-quote normalization** | The run merger normalizes `"` `"` → `"` before placeholder scanning, making the engine resilient to Word auto-formatting. |
| **No code execution** | The DOCX rendering engine performs text substitution and structural operations only (row duplication, block removal). No macros, scripts, or OLE objects are executed. |
| **Authentication** | Template upload endpoints require Designer or Admin role (existing authorization). API endpoints require `X-Api-Key` header (existing). |
| **File system access control** | Template body files are stored outside the web root. They are not served as static files. Access is controlled by reading the file in application code only after authorization checks. |
| **Audit trail** | All template changes (upload, update, revert) are tracked in TemplateHistory with `ChangedBy` attribution — unchanged from current behavior. The stored path confirms which file was active at each version. |
| **Orphaned file cleanup** | If a DB transaction fails after a file write, the orphaned file must be deleted immediately within the same request (synchronous cleanup). Periodic scanning for any files that survived despite a DB failure is the responsibility of an external background job — that job is out of scope for this initiative. |

---

## 10. Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Word splits placeholders across XML runs** | High | High | DocxRunMerger pre-processes all paragraphs; smart-quote normalization included; extensive test coverage with fragmented DOCX fixtures |
| **Bookmark/field elements break run merger** | Medium | High | Merger must explicitly skip `<w:bookmarkStart>`, `<w:bookmarkEnd>`, `<w:fldChar>`, `<w:instrText>` elements during run grouping without removing them |
| **Print provider does not accept DOCX** | Unknown | Critical | **Confirm with print provider before implementation begins.** If provider requires PDF, output format decision must be revisited. |
| **Collection table row collision** (template row contains nested table) | Low | Medium | Define rule: the `{{ collection_key }}` marker row and the template row must be in a flat (non-nested) table. Nested tables are not supported for collection rendering. Document for designers. |
| **Conditional spanning multiple table rows or paragraphs incorrectly** | Medium | Medium | Define rule: the `{{ if }}` and `{{ end }}` markers each occupy one dedicated paragraph or one dedicated table row. Content between them is removed/kept as a unit. Document for designers. |
| **Large DOCX templates cause memory pressure in batch** | Medium | Medium | Process one recipient at a time (existing pattern). Warn at upload time if file > 5 MB. |
| **File system unavailability** | Low | High | Application fails fast at startup if storage root is unwritable. Hangfire jobs fail with a transient error if file read fails at render time, triggering automatic retry. |
| **Orphaned files accumulate on disk** | Low | Low | Atomic write pattern prevents most cases. Startup cleanup job logs orphans. Manual cleanup process documented. |
| **Word auto-correct corrupts `{{ }}`** | Medium | Medium | Smart-quote normalization in run merger handles this automatically. Designer guide advises disabling auto-correct for placeholder text as a belt-and-suspenders measure. |

---

## 11. Roadmap & Milestones

### Phase 1 — Foundation (File Storage + DOCX Core Engine)
**Target: Sprint 1-2**

- Add `DocumentFormat.OpenXml` NuGet package
- Implement `ITemplateBodyStore` + `FileSystemTemplateBodyStore` (with `TemplateBodyNotFoundException` / `TemplateBodyCorruptedException`)
- Startup validation of `TemplateStorage:RootPath` (F-104b) — fail fast if missing or not writable
- EF Core migration: replace `HtmlBody` / `DocxBody` columns with `BodyPath` (nvarchar) on `Templates`, `TemplateHistory`, `TemplateSnapshots`. No data migration needed (never in production).
- Implement `DocxRunMerger` (XML run merging + smart-quote normalization)
- Implement `IDocxTemplateRenderer` (scalar replacement + table collection rendering + conditional block removal; no nested conditionals)
- Implement `IDocxPlaceholderParser` (post-merge placeholder extraction from body + headers/footers)
- Unit tests for all new components

### Phase 2 — Service Layer Integration
**Target: Sprint 3**

- Modify `TemplateService`: write/read DOCX and HTML bodies via `ITemplateBodyStore`; update Create/Update/Revert/Diff for file-backed storage
- Modify `TemplateSnapshotService`: copy DOCX/HTML file to snapshot path at campaign schedule time
- Modify `TemplatePreviewService`: read body file, render DOCX, return bytes for download
- Register Scriban custom functions (`format_date`, `format_currency`) for HTML renderer
- Service-layer tests

### Phase 3 — API, UI & Dispatch Pipeline
**Target: Sprint 4**

- Add Letter-specific API endpoints: `POST /api/templates/letter`, `PUT /api/templates/{id}/letter`, `GET /api/templates/{id}/docx`
- Update `TemplatesController`: existing POST/PUT endpoints write HTML bodies via `ITemplateBodyStore`
- Update Razor Pages: conditional file upload UI for Letter channel; DOCX download link on Edit page
- Extend `DispatchRequest` with `BinaryContent` (`byte[]?`) property (F-403b)
- Modify `ProcessChunkJob`: read DOCX body from file system, call `IDocxTemplateRenderer` per recipient, pass bytes via `DispatchRequest.BinaryContent` — one call to `SendAsync` per recipient, no accumulation
- Rewrite `LetterDispatcher`: remove PDF accumulation + `FlushBatchAsync`; accept `BinaryContent`, write one `.docx` file per `SendAsync` call via `PrintProviderFileDropHandler`
- Register Scriban custom functions (`format_date`, `format_currency`) for HTML renderer (F-305)
- Write LetterDispatcher unit tests (F-404 coverage)
- DI registration of all new services

### Phase 4 — Hardening & Documentation
**Target: Sprint 5**

- Designer guide: supported Word features, placeholder syntax, collection table pattern, conditional pattern, smart-quote note
- Performance benchmarks: DOCX rendering throughput
- Edge case testing: complex tables, merged cells, images, headers/footers, bookmarks between runs
- Security review: DOCX upload validation, XML injection testing
- Regression testing: verify Email/SMS channels completely unaffected

---

## 12. Open Questions

| # | Question | Impact | Owner | Status |
|---|----------|--------|-------|--------|
| Q1 | Should the system strip VBA macros from uploaded DOCX files, or reject macro-enabled files entirely? | Security policy | Product / Security | **Resolved: Reject macro-enabled files (`.docm`, files containing `vbaProject.bin`). Return HTTP 422.** |
| Q2 | What Word features should be officially "supported" vs. "use at your own risk"? | Designer documentation | Product / Design | Partially resolved — see Section 6 Out of Scope |
| Q3 | Should `{{ else }}` blocks be supported in conditionals from day 1, or deferred? | Complexity | Engineering | **Resolved: Deferred. Not in scope for this initiative.** |
| Q4 | How should the system handle placeholders inside Word text boxes (separate XML part `wps:txbx`)? | Run-merger scope | Engineering | **Resolved: Out of scope. Text boxes are preserved as-is in the output. See Section 6.** |
| Q5 | Should the old `LetterPostProcessor` (HTML-to-PDF via DinkToPdf) be removed or kept as dead code? | Code cleanliness | Engineering | Open — retained for rollback safety; removal deferred |
| Q6 | Is there a maximum number of template versions to retain in history, or should all versions be kept indefinitely? | File storage planning | Product / Ops | Open — recommend defining a default retention policy (e.g., last 50 versions per template) |
| Q7 | Should the rendering timeout for DOCX templates be the same as HTML (10 seconds) or configurable separately? | Performance configuration | Engineering | Open — DOCX-only rendering should be well under 500 ms; 10 s is likely sufficient |
| Q8 | **Does the print provider accept `.docx` files as the file drop format?** | Critical business dependency | Product / Ops | **Resolved: Yes — print provider confirmed acceptance of `.docx` files. Implementation may proceed.** |
| Q9 | For multi-instance deployments, is the storage root a UNC share or a mounted volume? Who is responsible for HA/backup of this path? | Infrastructure | Ops | Open |

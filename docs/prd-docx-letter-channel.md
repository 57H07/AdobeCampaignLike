# PRD: Letter Channel Migration — HTML to DOCX Templates

> Version: 1.0 — Generated 2026-03-28
> Status: Draft

---

## 1. Executive Summary

CampaignEngine's Letter channel currently forces designers to author print-ready letter templates in HTML — a format designed for screens, not paper. This PRD describes the migration of the Letter channel from an HTML→PDF pipeline to a DOCX→PDF pipeline. Designers will create letter templates in Microsoft Word using familiar `{{ placeholder }}` syntax, upload the `.docx` file through the web UI, and the engine will handle placeholder substitution (including loops and conditionals), then convert the final document to PDF using Syncfusion's DocIO library. Email and SMS channels remain unchanged. There are no existing letter templates in production, making this a clean-break migration with no backward-compatibility burden.

---

## 2. Problem Statement

### Current Pain Points

- **HTML is not a print medium.** Designers must hand-craft HTML/CSS with precise A4 margins, page breaks, and print-friendly fonts. This requires web development skills that most marketing/template designers don't have.
- **WYSIWYG gap.** What designers see in an HTML editor does not reliably match the PDF output produced by wkhtmltox. Margins shift, fonts substitute, page breaks land unexpectedly.
- **Layout limitations.** Complex letter layouts (multi-column addresses, logos with text wrapping, official letterheads, signature blocks) are tedious or fragile to implement in HTML for print.
- **Tooling mismatch.** Designers are proficient in Microsoft Word. Forcing them into HTML authoring increases error rates, slows iteration, and creates a dependency on developers for template fixes.

### Why Existing Solutions Are Insufficient

- The current DinkToPdf/wkhtmltox pipeline converts HTML faithfully as a webpage, but it cannot replicate the precise typographic and layout control that Word provides for printed documents.
- There is no intermediate DOCX step — the system is architecturally coupled to HTML at every layer (entity storage, rendering, snapshotting, versioning, preview, placeholder extraction).

### Opportunity

Migrating to DOCX gives designers full control over letter layout using their primary tool (Word), eliminates the HTML/CSS expertise requirement, and produces higher-fidelity printed output. The Syncfusion pure-.NET converter avoids server-side dependencies like LibreOffice while providing reliable DOCX→PDF conversion.

---

## 3. Target Users & Personas

### Persona 1: Claire — Template Designer
- **Role:** Marketing communications designer
- **Goals:** Create professional, brand-compliant letter templates with dynamic data fields. Iterate quickly on layout and typography.
- **Frustrations:** Currently must write HTML/CSS for print layout. Preview often doesn't match PDF output. Cannot use Word features she's expert in (styles, tables, text boxes, headers/footers).
- **Key behaviors:** Works primarily in Microsoft Word and Adobe InDesign. Uploads assets via the CampaignEngine web UI. Tests with preview before publishing.

### Persona 2: Marc — Campaign Manager
- **Role:** Marketing operations manager
- **Goals:** Assemble campaigns with letter steps, select templates, preview personalized output, schedule sends.
- **Frustrations:** Template preview for letters is unreliable (HTML rendering vs. actual PDF). Must frequently go back to designers for layout corrections.
- **Key behaviors:** Uses the campaign builder UI. Reviews previews with sample data. Monitors batch dispatch status.

### Persona 3: Sarah — System Administrator
- **Role:** IT/DevOps engineer
- **Goals:** Deploy and maintain the CampaignEngine platform. Manage dependencies, monitor performance, handle upgrades.
- **Frustrations:** wkhtmltox native library deployment is fragile (platform-specific DLL, version conflicts). Wants pure-.NET dependencies where possible.
- **Key behaviors:** Manages NuGet packages, configures deployment pipelines, monitors infrastructure.

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
| G4 | PDF rendering fidelity | Visual output matches Word's own PDF export within acceptable tolerance (fonts, margins, tables) |
| G5 | Per-recipient PDF generation time | < 2 seconds per letter at p95 (comparable to current HTML→PDF pipeline) |
| G6 | Zero regression on Email/SMS channels | All existing Email and SMS template tests pass without modification |

---

## 5. Features & Requirements

### 5.1 Functional Requirements

#### Epic 1: DOCX Template Storage

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-101 | Binary DOCX storage | As Claire, I want the system to store my uploaded Word file so that I can download it later for editing | Must-have |
| F-102 | Channel-aware template model | As the system, I need to store `DocxBody` (byte[]) for Letter templates alongside `HtmlBody` for Email/SMS, so both channels coexist | Must-have |
| F-103 | DOCX versioning | As Claire, I want each template update to save the previous DOCX version so that I can revert to an earlier version | Must-have |
| F-104 | DOCX snapshot on schedule | As Marc, I want the DOCX content frozen when a campaign is scheduled so that later template edits don't affect running campaigns | Must-have |
| F-105 | DOCX download endpoint | As Thomas, I want to download the current DOCX file via `GET /api/templates/{id}/docx` so that I can inspect or re-edit it offline | Should-have |

#### Epic 2: DOCX Template Upload & Validation

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-201 | File upload via web UI | As Claire, I want to upload a `.docx` file when creating a Letter template so that I can author templates in Word | Must-have |
| F-202 | File re-upload on edit | As Claire, I want to re-upload a new `.docx` file to update an existing Letter template | Must-have |
| F-203 | DOCX validation | As the system, I need to validate that uploaded files are valid OpenXML documents (not renamed ZIP, corrupt, or malicious) | Must-have |
| F-204 | File size limit | As Sarah, I want uploads rejected if they exceed 10 MB to prevent storage abuse | Must-have |
| F-205 | API multipart upload | As Thomas, I want `POST /api/templates/letter` and `PUT /api/templates/{id}/letter` endpoints accepting `multipart/form-data` | Must-have |
| F-206 | Conditional UI toggle | As Claire, I want the template creation form to show a file upload input when I select "Letter" channel and an HTML editor for other channels | Should-have |

#### Epic 3: DOCX Placeholder Engine

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-301 | XML run merging | As the system, I need to merge Word's split XML runs (`<w:r>`) so that `{{ firstName }}` is recognized even when Word fragments it across multiple runs | Must-have |
| F-302 | Scalar placeholder replacement | As Claire, I want `{{ firstName }}`, `{{ address }}`, etc. in my Word document replaced with recipient data | Must-have |
| F-303 | Loop support | As Claire, I want `{{ for row in orderLines }}...{{ end }}` to duplicate table rows (or paragraph blocks) for each item in a collection | Must-have |
| F-304 | Conditional support | As Claire, I want `{{ if isPremium }}...{{ end }}` to show or hide content blocks based on recipient data | Must-have |
| F-305 | Custom functions | As Claire, I want `{{ format_date invoiceDate "dd/MM/yyyy" }}` and `{{ format_currency amount "€" }}` to work in DOCX just like in HTML templates | Should-have |
| F-306 | Placeholder extraction from DOCX | As the system, I need to auto-detect all placeholders in a DOCX file (handling split runs) to populate the manifest | Must-have |
| F-307 | Manifest validation for DOCX | As Claire, I want the system to warn me if my DOCX contains undeclared placeholders before I publish | Must-have |
| F-308 | Headers/footers processing | As Claire, I want placeholders in Word headers and footers to be replaced too | Should-have |

#### Epic 4: DOCX-to-PDF Conversion

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-401 | Syncfusion DOCX-to-PDF | As the system, I need to convert rendered DOCX documents to PDF using Syncfusion DocIORenderer | Must-have |
| F-402 | PDF size validation | As the system, I need to reject PDFs exceeding 10 MB per recipient letter | Must-have |
| F-403 | Batch consolidation (unchanged) | As the system, I need the existing PDF consolidation pipeline (PdfSharp, 500-page batches, CSV manifests) to work with DOCX-generated PDFs | Must-have |
| F-404 | Syncfusion license registration | As Sarah, I need the Syncfusion Community license key configured via `appsettings.json` | Must-have |

#### Epic 5: Preview & Dispatch Integration

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-501 | DOCX template preview | As Marc, I want to preview a Letter template with sample data and see the resulting PDF, so I can verify layout before scheduling | Must-have |
| F-502 | Batch dispatch with DOCX | As the system, I need ProcessChunkJob to render DOCX templates per-recipient and produce PDFs for the LetterDispatcher | Must-have |
| F-503 | LetterDispatcher binary path | As the system, I need LetterDispatcher to accept pre-rendered PDF bytes (via `DispatchRequest.BinaryContent`) instead of HTML content for Letter channel | Must-have |

#### Epic 6: Version History & Diff

| ID | Feature | User Story | Priority |
|----|---------|------------|----------|
| F-601 | DOCX version history | As Claire, I want to see the version history of a Letter template (version number, who changed, when) | Must-have |
| F-602 | DOCX revert | As Claire, I want to revert a Letter template to a previous DOCX version | Should-have |
| F-603 | Binary diff indicator | As Claire, I want to see whether the DOCX content changed between versions (yes/no), even though visual diff is not available for binary files | Nice-to-have |

### 5.2 Non-functional Requirements

| Category | Requirement |
|----------|-------------|
| **Performance** | Per-recipient DOCX rendering + PDF conversion must complete in < 2 seconds at p95 for a typical 1-3 page letter |
| **Performance** | Batch throughput must remain >= 250 letters/minute (current HTML pipeline benchmark) |
| **Scalability** | DOCX binary storage (varbinary(max)) must handle templates up to 10 MB without performance degradation on SQL Server |
| **Scalability** | Template history accumulation (binary per version) must not cause noticeable query slowdowns over 100+ versions |
| **Reliability** | Corrupt or malformed DOCX uploads must be rejected with a clear error, not crash the application |
| **Reliability** | Run-merging edge cases must not silently drop placeholders — unrecognized patterns must be logged |
| **Maintainability** | Email/SMS pipeline code paths must remain completely unchanged (zero coupling to DOCX logic) |
| **Deployment** | No native DLLs or external server processes required (pure .NET solution via Syncfusion) |

---

## 6. Out of Scope

The following are explicitly **not** part of this initiative:

- **Email/SMS channel changes.** These channels continue to use HTML/Scriban templates unchanged.
- **Online DOCX editor.** No in-browser Word editing (e.g., OnlyOffice, Collabora). Designers use desktop Word and upload files.
- **Sub-template composition for DOCX.** The `{{> subtemplate_name }}` feature is not supported for DOCX — each letter template is a self-contained file.
- **Visual diff between DOCX versions.** Binary files cannot be meaningfully diffed in the UI. Only metadata (version, author, date) and a "content changed: yes/no" flag are shown.
- **Migration of existing HTML letter templates.** There are no letter templates in production. This is a clean break — the old HTML→PDF path for Letter is removed.
- **Removal of DinkToPdf.** The HTML→PDF pipeline (DinkToPdf/wkhtmltox) remains for potential future use and for any transitional needs. It is not actively used by the Letter channel after migration.
- **Word Mail Merge fields or Content Controls.** Placeholders use the same `{{ key }}` text syntax, not Word-native merge fields or structured document tags.
- **Embedded image management in DOCX.** Images embedded in the Word document are preserved as-is during rendering. There is no dynamic image insertion feature.

---

## 7. Technical Constraints & Stack

| Component | Technology | Notes |
|-----------|-----------|-------|
| Runtime | .NET 8 | Existing platform, non-negotiable |
| Web Framework | ASP.NET Core (Razor Pages + REST controllers) | Existing |
| ORM | EF Core 8 + SQL Server | Existing; DOCX stored as `varbinary(max)` |
| DOCX Manipulation | `DocumentFormat.OpenXml` (Microsoft, MIT license) | Official OpenXML SDK. Used for run merging, placeholder replacement, loop/conditional processing |
| DOCX-to-PDF Conversion | Syncfusion Community License (`Syncfusion.DocIO.Net.Core` + `Syncfusion.DocIORenderer.Net.Core`) | Free for individuals and companies < $1M revenue. Pure .NET, no native dependencies |
| PDF Consolidation | PdfSharp 6.2.4 (existing) | Unchanged — merges individual PDFs into batches |
| Background Jobs | Hangfire 1.8 (existing) | Unchanged — ProcessChunkJob orchestrates per-recipient rendering |
| Template Syntax | Scriban-like `{{ }}` in plain text within Word | Not using the Scriban library directly for DOCX — custom OpenXML-based renderer |
| Architecture | Clean Architecture: Domain -> Application -> Infrastructure <- Web | Strict layer dependencies enforced |

### Licensing Constraint

Syncfusion Community License requires:
- Annual gross revenue < $1,000,000 USD, **or** individual developer (not representing a company)
- Free registration at syncfusion.com required to obtain a license key
- License key must be configured in `appsettings.json` and registered at application startup

---

## 8. External Dependencies & Integrations

| Dependency | Purpose | Risk Level | Required Setup |
|------------|---------|------------|----------------|
| `DocumentFormat.OpenXml` (NuGet) | DOCX XML manipulation (run merging, element traversal, document modification) | Low | `dotnet add package DocumentFormat.OpenXml` — MIT license, Microsoft-maintained, no native deps |
| `Syncfusion.DocIO.Net.Core` (NuGet) | DOCX loading and manipulation | Medium | NuGet install + Community License key registration. Risk: license terms may change in future versions |
| `Syncfusion.DocIORenderer.Net.Core` (NuGet) | DOCX-to-PDF conversion | Medium | Same license as above. Risk: rendering fidelity for complex Word features (SmartArt, advanced OLE objects) is untested |
| `Syncfusion.Pdf.Net.Core` (NuGet) | PDF output from DocIORenderer | Low | Transitive dependency of DocIORenderer |
| Microsoft Word (designer workstation) | Template authoring tool | Low | Designers must have Word installed. No specific version required — any version producing `.docx` (Office 2007+) |
| PdfSharp 6.2.4 (existing) | PDF batch consolidation | Low | Already in the project. Must handle PDFs produced by Syncfusion (standard PDF/A output) |

---

## 9. Security & Compliance

| Area | Requirement |
|------|-------------|
| **File upload validation** | Uploaded files must be validated as genuine OpenXML documents (not renamed executables, ZIP bombs, or files containing macros). Attempt to open with `WordprocessingDocument.Open()` as validation gate. Reject files with VBA macros (`VbaProject` part present). |
| **File size limit** | Maximum 10 MB per uploaded DOCX file. Enforced at both API (request size limit) and service layer. |
| **Content sanitization** | Placeholder values inserted into DOCX are plain text (no XML injection). Values must be XML-escaped when written into `<w:t>` elements to prevent OpenXML injection. |
| **No code execution** | The DOCX rendering engine must not execute macros, scripts, or OLE objects embedded in templates. Only text substitution and structural operations (row duplication, block removal) are performed. |
| **Authentication** | Template upload endpoints require Designer or Admin role (existing authorization). API endpoints require `X-Api-Key` header (existing). |
| **Binary storage** | DOCX content stored in SQL Server `varbinary(max)`. Access controlled by existing EF Core repository pattern and role-based authorization. No direct file system storage (reduces attack surface). |
| **Audit trail** | All template changes (upload, update, revert) are tracked in TemplateHistory with `ChangedBy` attribution — unchanged from current behavior. |

---

## 10. Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Word splits placeholders across XML runs** (e.g., `{{ first` + `Name }}` in separate `<w:r>` elements) | High | High — placeholders silently ignored | DocxRunMerger pre-processes all paragraphs to merge split runs before placeholder detection. Extensive test coverage with intentionally fragmented DOCX fixtures. |
| **Syncfusion Community License terms change** | Medium | Medium — forced to find alternative or pay | Isolate Syncfusion behind `IDocxToPdfConverter` interface. Switching to LibreOffice headless or a paid library requires only one new implementation class. |
| **Complex Word features render poorly in PDF** (SmartArt, text effects, advanced tables) | Medium | Medium — visual defects in output | Document supported Word features for designers. Provide a "supported features" guide. Test with representative template samples during development. |
| **Loop/conditional parsing ambiguity** (e.g., `{{ for }}` in a table cell vs. spanning multiple paragraphs) | Medium | High — incorrect output | Define clear rules: loops inside table rows duplicate rows; loops outside tables duplicate paragraph blocks. Document for designers. Validate at upload time. |
| **Large DOCX templates with images cause memory pressure** (10 MB template x 10,000 recipients) | Medium | Medium — OOM in batch job | Stream-based processing where possible. Process one recipient at a time (existing pattern). Monitor memory in batch jobs. Consider template size warnings for files > 5 MB. |
| **Binary storage bloats database** (history accumulates many DOCX versions) | Low | Low — slow queries over time | Typical templates are 50-500 KB. 100 versions = 50 MB — manageable. Add index on TemplateId for history queries. Consider future cleanup policy for old versions. |
| **Scriban-like syntax collisions with Word auto-formatting** (Word may "smart-quote" `{{ }}` or auto-correct text inside placeholders) | Medium | Medium — placeholders not recognized | Document that designers must disable auto-correct for placeholder text. Run-merger handles curly/smart quote variants. Validate placeholders at upload time and warn if none found. |

---

## 11. Roadmap & Milestones

### Phase 1 — Foundation (MVP Core Engine)
**Target: Sprint 1-2**

- Add NuGet packages (`DocumentFormat.OpenXml`, Syncfusion)
- Implement `DocxRunMerger` (XML run merging)
- Implement `IDocxTemplateRenderer` (scalar replacement, loops, conditionals)
- Implement `IDocxToPdfConverter` (Syncfusion wrapper)
- Unit tests for all new components with DOCX test fixtures
- EF Core migration: add `DocxBody` / `ResolvedDocxBody` columns

### Phase 2 — Service Layer Integration
**Target: Sprint 3**

- Modify `TemplateService` for DOCX create/update/revert/diff
- Modify `TemplateSnapshotService` for DOCX snapshot creation
- Implement `DocxPlaceholderParserService` for placeholder extraction
- Modify `TemplatePreviewService` for DOCX preview pipeline
- Service-layer tests

### Phase 3 — API, UI & Dispatch Pipeline
**Target: Sprint 4**

- Add Letter-specific API endpoints (multipart upload, DOCX download)
- Update Razor Pages (conditional file upload for Letter channel)
- Modify `ProcessChunkJob` for DOCX rendering branch
- Modify `LetterDispatcher` for binary content path
- DI registration of all new services
- End-to-end integration tests

### Phase 4 — Hardening & Documentation
**Target: Sprint 5**

- Designer guide: supported Word features, placeholder syntax in DOCX, best practices
- Performance benchmarks: rendering throughput comparison vs. old HTML pipeline
- Edge case testing: complex tables, multi-page letters, merged cells, images, headers/footers
- Security review: DOCX upload validation, XML injection testing
- Regression testing: verify Email/SMS channels are completely unaffected

---

## 12. Open Questions

| # | Question | Impact | Owner |
|---|----------|--------|-------|
| Q1 | Should the system strip VBA macros from uploaded DOCX files, or reject macro-enabled files entirely? | Security policy for template uploads | Product / Security |
| Q2 | What Word features should be officially "supported" vs. "use at your own risk"? (e.g., SmartArt, WordArt, embedded OLE objects, text effects) | Designer documentation and support expectations | Product / Design |
| Q3 | Should `{{ else }}` blocks be supported in conditionals from day 1, or deferred? | Complexity of the DOCX conditional engine | Engineering |
| Q4 | How should the system handle placeholders inside Word text boxes (separate OpenXML part)? | Run-merger scope and complexity | Engineering |
| Q5 | Should the old `LetterPostProcessor` (HTML-to-PDF via DinkToPdf) be removed or kept as dead code? | Code cleanliness vs. rollback safety | Engineering |
| Q6 | Is there a maximum number of template versions to retain in history, or should all versions be kept indefinitely? | Database storage planning | Product / Ops |
| Q7 | Should the rendering timeout for DOCX templates be the same as HTML (10 seconds) or longer given the heavier processing? | Performance configuration | Engineering |
| Q8 | Does the Syncfusion Community License need to be renewed annually, and who owns the registration? | License management process | Ops / Legal |

# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-10-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Add centralized document upload, organization, browsing, search, sharing, and management to ContosoDashboard. Employees upload files (PDF/Office/text/JPEG/PNG, ≤25 MB) with required title/category and optional description/project/tags; files are virus-scanned (simulated EICAR-based stub), stored under a GUID-based path outside `wwwroot`, and referenced by a `Document` metadata record. Documents are browsable via "My Documents" and per-project "Project Documents" views, searchable, previewable in-browser (PDF/images), downloadable, editable, and deletable (uploader or managing Project Manager, cascading to shares and task attachments). Individual-user sharing surfaces documents in a "Shared with Me" view with in-app notification. Documents integrate with tasks (attachment), the dashboard (Recent Documents widget + count), and notifications (share + new-project-document events). All storage operations are implemented behind an `IFileStorageService` abstraction (local filesystem now, Azure Blob Storage later) per the project constitution, and authorization is independently enforced at the service layer (IDOR protection) regardless of UI state.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`net10.0`, existing `ContosoDashboard.csproj`)
**Primary Dependencies**: ASP.NET Core Blazor Server + Razor Pages (existing), EF Core SqlServer 10.0.12 (existing), Bootstrap 5.3 + Bootstrap Icons (existing UI). No new NuGet packages required — malware-scan simulation and local file storage use only BCL `System.IO`/`System.Security.Cryptography`.
**Storage**: SQL Server LocalDB via EF Core (`ApplicationDbContext`) for all document metadata, shares, and activity-log rows; local filesystem under a configurable root (default `AppData/uploads`, outside `wwwroot`) for file bytes, accessed exclusively through `IFileStorageService`.
**Testing**: No automated test project exists in this repository yet (constitution: manual security verification is the current norm). This feature does not introduce a new test framework; `quickstart.md` documents the manual end-to-end verification steps (upload, browse/search, download/preview, edit/replace/delete with cascade, share, task attachment, dashboard widget, IDOR/negative-permission checks) required before the feature is considered complete, consistent with the Development Workflow & Quality Gates section of the constitution.
**Target Platform**: ASP.NET Core Blazor Server web application (existing), served to modern desktop browsers (Chrome/Edge/Firefox) that support in-browser PDF/image rendering via `<iframe>`/`<img>`.
**Project Type**: Single project — existing monolithic `ContosoDashboard/` Blazor Server app (Models/Data/Services/Pages/Shared layering). No new top-level project or frontend/backend split is introduced.
**Performance Goals**: Document search and list pages (up to 500 documents) respond within 2s; document preview renders within 3s; document upload completes within 30s for files up to 25 MB under typical network conditions (per spec FR-028, SC-006, SC-007).
**Constraints**: MUST run fully offline (no required network/cloud calls); 25 MB per-file hard limit; MUST work with the existing mock cookie authentication and `Database.EnsureCreated()` initialization (no EF Core migrations introduced); storage implementation MUST be swappable to Azure Blob Storage via DI with zero changes to business logic, pages, or schema.
**Scale/Scope**: Training-scale application; current seed data has 4 users and 1 project. Feature must correctly support list/search pagination semantics up to 500 documents per view as a performance target, not as a hard scale ceiling.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Result |
|---|---|---|
| I. Security by Default | Service-layer authorization re-verified independently of UI/`[Authorize]` (uploader/Project Manager/Administrator/share-recipient checks in `DocumentService`); GUID-based storage identifiers generated before persistence; file type allow-list + 25 MB size enforced before storage; files stored under `AppData/uploads` outside `wwwroot`; authenticated Razor Page handler required to serve any file content (no direct static-file access) | PASS |
| II. Infrastructure Abstraction for Cloud Migration | All file I/O goes through `IFileStorageService` (new `LocalFileStorageService` impl using `System.IO`); no cloud SDK references added; malware scanning behind `IMalwareScanService` so a future real-AV implementation can be swapped in the same way | PASS |
| III. Clean Separation of Concerns | New entities in `Models/`; `ApplicationDbContext` updated in `Data/` only for persistence mapping; all business rules and authorization in `Services/` (`DocumentService`, `LocalFileStorageService`, `SimulatedMalwareScanService`); Razor pages/components call services only, no inline `DbContext` queries or authorization logic | PASS |
| IV. Educational Clarity & Simplicity | Malware scanning is an explicitly-documented simulated stub (EICAR signature) rather than a real AV integration; tags stored as a simple delimited string (no speculative tagging subsystem); category modeled as a C# enum persisted as text (`HasConversion<string>()`) to satisfy the stakeholder's "text not integer" constraint without sacrificing compile-time safety; no new abstractions beyond what two real call sites (local + future Azure) justify | PASS |
| V. Spec-Driven Development Workflow | This plan follows `/speckit.specify` → `/speckit.clarify` → `/speckit.plan`; Constitution Check included below and re-checked post-design; no violations requiring Complexity Tracking | PASS |

No violations identified. Complexity Tracking table is not needed.

**Post-Design Re-check** (after Phase 1 data-model.md/contracts/quickstart.md): The finalized design — `TaskDocument` join entity, nullable-FK `DocumentActivityLogEntry` with `DeleteBehavior.SetNull`, enum-as-string conversions for `Category`/`ActionType`, and the Razor Page download/preview endpoint — introduces no new infrastructure dependencies, no cloud SDK references, no bypass of the `Services/` authorization layer, and no deviation from the existing `EnsureCreated()`-based schema workflow. All five principles remain PASS; Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs                      # NEW - Document entity + DocumentCategory enum
│   ├── DocumentShare.cs                 # NEW - DocumentShare entity
│   ├── DocumentActivityLogEntry.cs      # NEW - DocumentActivityLogEntry entity + DocumentActivityType enum
│   ├── TaskDocument.cs                  # NEW - join entity for task↔document attachments
│   └── Notification.cs                  # MODIFIED - add DocumentShared/DocumentAdded to NotificationType
├── Data/
│   └── ApplicationDbContext.cs          # MODIFIED - new DbSets, FK/index/cascade configuration, enum-as-string conversion
├── Services/
│   ├── IFileStorageService.cs / LocalFileStorageService.cs           # NEW - storage abstraction (Principle II)
│   ├── IMalwareScanService.cs / SimulatedMalwareScanService.cs       # NEW - simulated EICAR-based scan
│   ├── IDocumentService.cs / DocumentService.cs                      # NEW - upload/list/search/share/delete orchestration + authorization
│   └── NotificationService.cs           # MODIFIED - new notification triggers (share, project document added)
├── Pages/
│   ├── Documents.razor                  # NEW - "My Documents" + "Shared with Me" views
│   ├── ProjectDetails.razor             # MODIFIED - add "Project Documents" section/tab
│   ├── Tasks.razor / ProjectDetails.razor (task detail) # MODIFIED - attach/upload document from task
│   ├── Index.razor                      # MODIFIED - "Recent Documents" widget + document count summary card
│   ├── DocumentDownload.cshtml / .cshtml.cs   # NEW - authenticated PageModel handler serving file bytes (download + inline preview), mirrors Login.cshtml/.cs convention
├── Shared/                              # Optional small components (e.g., document upload modal) if extracted for reuse
└── wwwroot/                             # UNCHANGED — file content is never stored here
```

**Structure Decision**: Single-project layout (Option 1), extending the existing `ContosoDashboard/` Blazor Server application in place. No new top-level project, Controllers folder, or frontend/backend split is introduced; the file-serving endpoint follows the repository's existing Razor Page + `PageModel` code-behind convention (as used by `Login.cshtml`/`Login.cshtml.cs`) rather than introducing MVC controllers or Minimal API endpoints.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No violations identified — table intentionally left empty.*


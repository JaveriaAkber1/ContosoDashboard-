# Phase 0 Research: Document Upload and Management

All items below were either resolved during `/speckit.clarify` (recorded in [spec.md](./spec.md#clarifications)) or decided here by matching the feature to existing repository conventions. No `NEEDS CLARIFICATION` markers remain in the Technical Context.

## 1. File storage layout and path generation

- **Decision**: Store file content under a configurable root directory (`FileStorage:RootPath` in `appsettings.json`, default `AppData/uploads`), located outside `wwwroot`. Each file's relative storage path is generated as `{userId}/{projectId-or-"personal"}/{guid}.{extension}`, where the GUID is generated server-side before any disk write.
- **Rationale**: Matches the stakeholder document's explicit pattern and Constitution Principle I (GUID-based identifiers generated before persistence, never trust user-supplied names/paths, store outside any publicly served directory). The same relative-path shape is directly reusable as an Azure Blob Storage blob name, satisfying Principle II (zero-code-change migration).
- **Alternatives considered**: Storing under `wwwroot/uploads` for simpler static-file serving — rejected, violates the "no publicly/directly web-accessible directory" requirement (FR-008) and Principle I. Using the original filename directly — rejected, explicitly forbidden by FR-007 and enables path traversal.

## 2. Serving downloads and in-browser previews

- **Decision**: Add a new Razor Page (`Pages/DocumentDownload.cshtml` + `DocumentDownload.cshtml.cs`) with an `[Authorize]`-protected `PageModel`. `OnGetAsync` loads the document, calls `IDocumentService` to independently re-verify the requesting user's access (IDOR protection per Principle I), then streams the file via `IFileStorageService.DownloadAsync()`. A `mode` query parameter (`download` vs `inline`) controls the `Content-Disposition` header, supporting both FR-014 (download) and FR-015 (in-browser preview for PDF/JPEG/PNG) from a single endpoint.
- **Rationale**: The repository already uses the Razor Page + code-behind `PageModel` pattern for direct HTTP semantics outside the Blazor circuit (`Pages/Login.cshtml`/`.cs`, `Pages/Logout.cshtml`/`.cs`). Reusing this pattern keeps the new endpoint consistent with Principle III (Clean Separation of Concerns) and avoids introducing a parallel ASP.NET Core MVC Controllers layer or Minimal API surface that doesn't exist anywhere else in the project.
- **Alternatives considered**: New MVC Controller (`Controllers/DocumentsController.cs`) — rejected, would introduce a project convention (Controllers folder, `AddControllers()`/`MapControllers()`) not used anywhere else in this training codebase, counter to Principle IV (simplicity/no speculative generality). Minimal API endpoint registered in `Program.cs` — rejected, would pull authorization/business logic out of the layered `Services/` pattern mandated by Principle III.

## 3. Malware scanning

- **Decision**: Introduce `IMalwareScanService` with a single method (`ScanAsync(Stream, string fileName)` → pass/fail result) and a `SimulatedMalwareScanService` implementation that flags any file whose content contains the standard EICAR anti-malware test string as "infected," passing everything else.
- **Rationale**: Directly resolves the clarification recorded in spec.md (Session 2026-10-02). EICAR is the industry-standard, harmless signature used precisely for testing AV detection paths without requiring a real virus. Keeping this behind an interface means a real scanner (e.g., ClamAV) can be substituted later without touching `DocumentService`.
- **Alternatives considered**: Real local AV engine integration (e.g., ClamAV via a NuGet client) — rejected for this iteration; violates the offline/dependency-free training constraint and Principle IV (YAGNI — no second real use case yet justifies the added operational complexity of running a scanner process in a training environment).

## 4. Database schema evolution strategy

- **Decision**: New entities are added as additional `DbSet<T>` properties and `OnModelCreating` configuration in the existing `ApplicationDbContext`; schema changes are picked up via the existing `Database.EnsureCreated()` call in `Program.cs` (no EF Core Migrations are introduced).
- **Rationale**: The constitution's Technology Stack section requires schema changes to "remain compatible with automatic database creation/seeding on first run," and the repository does not currently use EF Core Migrations anywhere. Introducing migrations now would be an unrelated process change outside this feature's scope.
- **Alternatives considered**: Adding an EF Core Migrations project/workflow — rejected as out of scope for this feature; would be a cross-cutting change to the whole repository's data-access workflow, not something a single feature should introduce unilaterally.

## 5. Category representation

- **Decision**: Model `Category` as a C# enum (`DocumentCategory`: `ProjectDocuments`, `TeamResources`, `PersonalFiles`, `Reports`, `Presentations`, `Other`) but configure EF Core to persist it as its string name via `.HasConversion<string>()`.
- **Rationale**: Satisfies the stakeholder document's explicit constraint ("Category must store text values, not integer enum, for simplicity") while still giving compile-time safety and IntelliSense in Razor pages/services, consistent with how other fixed-choice fields (`UserRole`, `ProjectStatus`) are modeled elsewhere in the codebase as enums.
- **Alternatives considered**: Plain `string` property validated only in the service layer — rejected, loses compile-time safety for no benefit now that EF's string conversion achieves the same on-disk representation.

## 6. Tags representation

- **Decision**: Store tags as a single delimited `string?` column (comma-separated, max 500 chars) on `Document`, parsed/joined in the service layer; search matches via a simple `Contains` check against the raw column.
- **Rationed**: FR-013 requires search by tag but does not require structured tag management (e.g., tag autocomplete, tag reuse analytics). A separate `Tag`/`DocumentTag` many-to-many schema would be speculative generality not justified by any current requirement (Principle IV — YAGNI).
- **Alternatives considered**: Normalized `Tag` + `DocumentTag` join tables — rejected for now as over-engineering relative to the stated requirements; can be introduced later without breaking the public `IDocumentService` contract if tag-specific features (e.g., tag browsing) are added.

## 7. Task attachment modeling

- **Decision**: Model task↔document attachment as a join entity `TaskDocument` (`TaskId`, `DocumentId`, `AttachedByUserId`, `AttachedDate`) rather than a single nullable `DocumentId` column on `TaskItem`.
- **Rationale**: Mirrors the existing `ProjectMember` join-entity pattern already used in this codebase for many-to-many-shaped relationships, and naturally supports a document being attached to more than one task (not precluded by the spec) without a future schema change.
- **Alternatives considered**: Single `DocumentId` FK column on `TaskItem` — rejected, artificially limits a document to one task and would require a breaking schema change if that limitation needed lifting later.

## 8. Document deletion cascade scope

- **Decision**: Configure `OnDelete(DeleteBehavior.Cascade)` from `Document` to `DocumentShare` and from `Document` to `TaskDocument`. `DocumentActivityLogEntry` rows are intentionally **not** cascade-deleted (`DeleteBehavior.Restrict` with a nullable `DocumentId` FK, plus a denormalized `DocumentTitleSnapshot` column) so that the deletion event itself remains in the audit trail after the document is gone.
- **Rationale**: Directly implements the clarification that deletion cascades shares and task-attachment links (spec.md Session 2026-10-02). The activity-log exception is necessary to satisfy FR-024/FR-025 (audit reporting on deletions) — an audit record that disappears when the document it describes is deleted would defeat the purpose of the log.
- **Alternatives considered**: Cascading the activity log too — rejected, would silently erase the deletion event from audit reports, undermining FR-025's compliance/reporting goal.

## 9. File upload UI mechanics (Blazor Server)

- **Decision**: Use Blazor's `InputFile` component with the stakeholder-recommended `MemoryStream` buffering pattern (copy the browser-selected file into an in-memory stream, bounded by `maxAllowedSize`, before handing it to `IDocumentService`), rather than passing the `IBrowserFile` stream directly through multiple layers.
- **Rationale**: Avoids the well-known Blazor Server issue where `IBrowserFile` streams can be disposed/invalidated across await boundaries or SignalR circuit hiccups; this is called out explicitly in the stakeholder source document as the recommended pattern.
- **Alternatives considered**: Streaming directly without buffering — rejected per stakeholder guidance; higher risk of disposed-stream errors in Blazor Server specifically (not an issue in Blazor WebAssembly, but this project uses Server hosting).

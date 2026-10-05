---

description: "Task list template for feature implementation"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: No automated test project exists in this repository yet (per plan.md Technical Context), and the spec does not request a TDD approach. Verification is performed via the manual scenarios in [quickstart.md](./quickstart.md), referenced as explicit validation tasks at the end of each phase.

**Organization**: Tasks are grouped by user story (US1/US2/US3, matching spec.md priorities P1/P2/P3) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in each description

## Path Conventions

Single project (existing `ContosoDashboard/` Blazor Server app — see plan.md Project Structure). All paths below are relative to the repository root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration needed before any document feature code can be written

- [X] T001 Add a `FileStorage` configuration section (`RootPath`, default `AppData/uploads`) to ContosoDashboard/appsettings.json and ContosoDashboard/appsettings.Development.json
- [X] T002 [P] Add `AppData/uploads/` to the root `.gitignore` so locally-stored uploaded files are never committed
- [X] T003 [P] Create `FileStorageOptions` configuration class in ContosoDashboard/Services/FileStorageOptions.cs bound to the `FileStorage` config section (`RootPath` property)
- [X] T004 Register `FileStorageOptions` via `builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"))` in ContosoDashboard/Program.cs

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core entities and infrastructure services that ALL three user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 [P] Create `Document` entity and `DocumentCategory` enum in ContosoDashboard/Models/Document.cs per data-model.md: `Title` required max length 255, `Description` optional max length 2000, `Category` required enum (`ProjectDocuments`, `TeamResources`, `PersonalFiles`, `Reports`, `Presentations`, `Other`), `Tags` optional max length 500, `ProjectId` optional FK, `UploadedByUserId` required FK, `UploadDate`/`UpdatedDate`, `FileSizeBytes` required (>0 and ≤ 26,214,400), `FileType` required max length 255, `OriginalFileName` required max length 255, `StoragePath` required max length 500
- [X] T006 [P] Create `DocumentActivityLogEntry` entity and `DocumentActivityType` enum (`Upload`, `Download`, `Delete`, `Share`) in ContosoDashboard/Models/DocumentActivityLogEntry.cs per data-model.md, with a **nullable** `DocumentId` FK and a required `DocumentTitleSnapshot` (max length 255) so the row survives document deletion
- [X] T007 Update ContosoDashboard/Data/ApplicationDbContext.cs: add `DbSet<Document> Documents` and `DbSet<DocumentActivityLogEntry> DocumentActivityLogEntries`; configure `Document.Category` and `DocumentActivityLogEntry.ActionType` via `.HasConversion<string>()`; configure `Document → User` (`UploadedByUserId`, Restrict) and `Document → Project` (`ProjectId`, optional, Restrict); configure `Document → DocumentActivityLogEntry` as `DeleteBehavior.SetNull`; add indexes on `Document(UploadedByUserId)`, `Document(ProjectId)`, `Document(Category)`, `Document(UploadDate)` (depends on T005, T006)
- [X] T008 [P] Create `IFileStorageService` interface and `LocalFileStorageService` implementation in ContosoDashboard/Services/IFileStorageService.cs per contracts/IFileStorageService.md (`UploadAsync`/`DeleteAsync`/`DownloadAsync`/`GetUrlAsync`), using `System.IO` against `FileStorageOptions.RootPath`, idempotent `DeleteAsync`
- [X] T009 [P] Create `IMalwareScanService` interface, `MalwareScanResult` record, and `SimulatedMalwareScanService` implementation in ContosoDashboard/Services/IMalwareScanService.cs per contracts/IMalwareScanService.md (detects the EICAR test string signature; all other content passes)
- [X] T010 Register `IFileStorageService` → `LocalFileStorageService` and `IMalwareScanService` → `SimulatedMalwareScanService` as scoped services in ContosoDashboard/Program.cs (depends on T008, T009)
- [X] T011 Create `DocumentUploadRequest`, `DocumentListFilter`, and `DocumentMetadataUpdate` DTOs in ContosoDashboard/Services/DocumentModels.cs (depends on T005)
- [X] T012 Create `IDocumentService` interface (full method surface from contracts/IDocumentService.md) and a `DocumentService` class skeleton with constructor DI (`ApplicationDbContext`, `IFileStorageService`, `IMalwareScanService`, `INotificationService`) in ContosoDashboard/Services/IDocumentService.cs (depends on T007, T008, T009, T011)
- [X] T013 Register `IDocumentService` → `DocumentService` as a scoped service in ContosoDashboard/Program.cs (depends on T012)

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Upload and Categorize a Document (Priority: P1) 🎯 MVP

**Goal**: Employees can upload a file with required title/category and optional description/project/tags; the file is validated, virus-scanned, stored, and the document is retrievable.

**Independent Test**: Log in as an employee, select a supported file, fill in required metadata, submit the upload, and verify the document appears in "My Documents" with correct metadata and that the underlying file is retrievable (quickstart.md Scenario 1).

### Implementation for User Story 1

- [X] T014 [US1] Implement `DocumentService.UploadAsync` in ContosoDashboard/Services/DocumentService.cs: validate `Title`/`Category` present, file type against the allow-list (PDF, DOC/DOCX, XLS/XLSX, PPT/PPTX, TXT, JPEG/JPG, PNG), size ≤ 25 MB; if `ProjectId` is set, verify the requesting user is a member/manager of that project or an Administrator; call `IMalwareScanService.ScanAsync` and reject on failure; generate a GUID-based storage path (`{userId}/{projectId-or-"personal"}/{guid}.{ext}`); call `IFileStorageService.UploadAsync`; insert the `Document` row; on DB failure call `IFileStorageService.DeleteAsync` to remove the orphaned file (FR-009); write a `DocumentActivityLogEntry` with `ActionType = Upload` (depends on T012)
- [X] T015 [US1] Implement `DocumentService.GetByIdAsync` and `GetMyDocumentsAsync` (unsorted/unfiltered for now) in ContosoDashboard/Services/DocumentService.cs, re-verifying server-side that the requester is the uploader, an Administrator, or a Project Manager of the document's project (share-recipient check added in US3) (depends on T014)
- [X] T016 [US1] Implement `DocumentService.OpenReadStreamAsync` in ContosoDashboard/Services/DocumentService.cs using the same authorization check as `GetByIdAsync`, returning `null` for unauthorized/non-existent documents (depends on T015)
- [X] T017 [P] [US1] Create ContosoDashboard/Pages/DocumentDownload.cshtml and ContosoDashboard/Pages/DocumentDownload.cshtml.cs: `[Authorize]` `PageModel` with `OnGetAsync(int documentId, string mode)` that calls `IDocumentService.GetByIdAsync`/`OpenReadStreamAsync`, returns `NotFound()` for `null` (no distinction between "missing" and "forbidden"), sets `Content-Disposition: attachment` for `mode=download`, and logs a `DocumentActivityLogEntry` with `ActionType = Download` on success (depends on T016)
- [X] T018 [US1] Create ContosoDashboard/Pages/Documents.razor with an upload form: `InputFile` + `MemoryStream` buffering pattern (research.md §9) bounded at 25 MB, fields for Title/Description/Category/optional Project (from the current user's projects)/Tags, a progress indicator, and a success/error message, calling `IDocumentService.UploadAsync` (depends on T014)
- [X] T019 [US1] Add a basic "My Documents" list section to ContosoDashboard/Pages/Documents.razor rendering `GetMyDocumentsAsync` results (title, category, upload date, file size, associated project) with a download link to `DocumentDownload` (depends on T015, T017, T018)
- [X] T020 [US1] Add a "Documents" link to ContosoDashboard/Shared/NavMenu.razor pointing to `/documents`
- [X] T021 [US1] Add a minimal "Project Documents" section to ContosoDashboard/Pages/ProjectDetails.razor listing documents associated with the current project (basic project-filtered query against `IDocumentService`) with download links (depends on T015, T017)
- [X] T022 [US1] Manual validation: run quickstart.md Scenario 1 (valid upload success, >25 MB rejection, unsupported-type rejection, EICAR malware rejection) and fix any issues found

**Checkpoint**: User Story 1 is fully functional and independently testable

---

## Phase 4: User Story 2 - Browse, Search, and Download Documents (Priority: P2)

**Goal**: Employees can view, sort, filter, search, download, and preview their own documents and documents of projects they belong to.

**Independent Test**: Given existing uploaded documents, navigate to "My Documents" and a project's document view, apply sort/filter/search, and download or preview a result (quickstart.md Scenario 2).

### Implementation for User Story 2

- [X] T023 [US2] Extend `DocumentService.GetMyDocumentsAsync` in ContosoDashboard/Services/DocumentService.cs to apply `DocumentListFilter` (category, project, date range) and support sorting by title, upload date, category, and file size (depends on T015)
- [X] T024 [US2] Implement `DocumentService.GetProjectDocumentsAsync` in ContosoDashboard/Services/DocumentService.cs with an explicit project-membership/manager/Administrator authorization check, returning an empty list (not an exception) when unauthorized or when there are zero matching documents (depends on T015)
- [X] T025 [US2] Implement `DocumentService.SearchAsync` in ContosoDashboard/Services/DocumentService.cs matching title/description/tags/uploader name/project name, pre-filtered to documents the requesting user is authorized to access (depends on T015, T024)
- [X] T026 [P] [US2] Add sort controls (title/date/category/size) and filter controls (category/project/date range) to the "My Documents" view in ContosoDashboard/Pages/Documents.razor (depends on T023)
- [X] T027 [US2] Replace the minimal Project Documents section in ContosoDashboard/Pages/ProjectDetails.razor with the full `GetProjectDocumentsAsync`-backed view, visible to all project members with download links (depends on T024)
- [X] T028 [US2] Add a search input and results list to ContosoDashboard/Pages/Documents.razor calling `SearchAsync`, including an explicit "no results" empty state (depends on T025)
- [X] T029 [US2] Add `mode=inline` handling to ContosoDashboard/Pages/DocumentDownload.cshtml.cs (`Content-Disposition: inline` with the document's `FileType`) and add a preview action (`<iframe>`/`<img>`) limited to PDF/JPEG/PNG documents in ContosoDashboard/Pages/Documents.razor and ContosoDashboard/Pages/ProjectDetails.razor (depends on T017, T019, T027)
- [X] T030 [US2] Manual validation: run quickstart.md Scenario 2 (sorting, filtering, project view visibility, search including empty-result state, in-browser preview, correct downloaded filename) and fix any issues found

**Checkpoint**: User Stories 1 AND 2 both work independently

---

## Phase 5: User Story 3 - Manage, Share, and Integrate Documents (Priority: P3)

**Goal**: Document owners/Project Managers can edit metadata, replace files, delete documents (with cascade), share documents with individual users, and see documents surfaced in tasks, the dashboard, and notifications.

**Independent Test**: Against existing seeded documents, edit metadata, replace a file, delete a document, share it with another user and confirm "Shared with Me" + notification, and confirm it appears in a task's attachment list and the dashboard's "Recent Documents" widget (quickstart.md Scenario 3).

### Implementation for User Story 3

- [X] T031 [P] [US3] Create `DocumentShare` entity in ContosoDashboard/Models/DocumentShare.cs per data-model.md (`DocumentId`, `SharedWithUserId`, `SharedByUserId`, `SharedDate`; unique on `DocumentId`+`SharedWithUserId`)
- [X] T032 [P] [US3] Create `TaskDocument` join entity in ContosoDashboard/Models/TaskDocument.cs per data-model.md (`TaskId`, `DocumentId`, `AttachedByUserId`, `AttachedDate`; unique on `TaskId`+`DocumentId`)
- [X] T033 [US3] Update ContosoDashboard/Data/ApplicationDbContext.cs: add `DbSet<DocumentShare>` and `DbSet<TaskDocument>`; configure `Document → DocumentShare` and `Document → TaskDocument` as `DeleteBehavior.Cascade`; configure `TaskItem → TaskDocument` as `DeleteBehavior.Cascade`; add unique indexes on `DocumentShare(DocumentId, SharedWithUserId)` and `TaskDocument(TaskId, DocumentId)` (depends on T031, T032, T007)
- [X] T034 [P] [US3] Add `DocumentShared` and `DocumentAddedToProject` members to the `NotificationType` enum in ContosoDashboard/Models/Notification.cs
- [X] T035 [US3] Implement `DocumentService.UpdateMetadataAsync` in ContosoDashboard/Services/DocumentService.cs, restricted to the document's uploader only, editing title/description/category/tags (depends on T015)
- [X] T036 [US3] Implement `DocumentService.ReplaceFileAsync` in ContosoDashboard/Services/DocumentService.cs, restricted to the uploader only, re-running the full type/size/malware-scan validation pipeline, regenerating `StoragePath`, and deleting the old file only after the new file and DB update both succeed (depends on T014, T035)
- [X] T037 [US3] Implement `DocumentService.DeleteAsync` in ContosoDashboard/Services/DocumentService.cs, permitted for the uploader OR a Project Manager of the document's project; relies on the EF cascade config to remove `DocumentShare`/`TaskDocument` rows; writes a `DocumentActivityLogEntry` with `ActionType = Delete` and `DocumentId = null` (`DocumentTitleSnapshot` captured before deletion) (depends on T033, T036)
- [X] T038 [US3] Implement `DocumentService.ShareAsync` and `GetSharedWithMeAsync` in ContosoDashboard/Services/DocumentService.cs: only the uploader may share; grants the recipient view/download/preview only (never edit/delete); writes a `DocumentActivityLogEntry` with `ActionType = Share`; triggers a `DocumentShared` notification to the recipient (depends on T033, T034)
- [X] T039 [US3] Update `DocumentService` authorization checks (`GetByIdAsync`, `OpenReadStreamAsync`, `GetProjectDocumentsAsync`, `SearchAsync`) in ContosoDashboard/Services/DocumentService.cs to treat `DocumentShare` recipients as authorized viewers (view/download/preview only — never edit/delete) (depends on T038, T024, T025, T016)
- [X] T040 [US3] Add a notification trigger in `DocumentService.UploadAsync`/`NotificationService` (ContosoDashboard/Services/DocumentService.cs, ContosoDashboard/Services/NotificationService.cs) sending a `DocumentAddedToProject` notification to other members of the target project whenever an upload sets `ProjectId` (depends on T014, T034)
- [X] T041 [US3] Add edit-metadata modal, replace-file action, and delete-with-confirmation action to document rows in ContosoDashboard/Pages/Documents.razor and ContosoDashboard/Pages/ProjectDetails.razor, showing edit/replace only to the uploader and delete to the uploader or the project's Project Manager, matching service-layer authorization (depends on T035, T036, T037)
- [X] T042 [US3] Add a Share action (recipient user picker) and a "Shared with Me" tab to ContosoDashboard/Pages/Documents.razor calling `ShareAsync`/`GetSharedWithMeAsync`, with no edit/delete controls rendered for shared documents (depends on T038)
- [X] T043 [US3] Add document attachment UI to the task detail view in ContosoDashboard/Pages/Tasks.razor: attach an existing document or upload a new one via `IDocumentService.UploadAsync`, creating a `TaskDocument` row and auto-setting `Document.ProjectId` from the task's `ProjectId` when previously unset (depends on T033, T014)
- [X] T044 [US3] Verify that deleting a document (`DeleteAsync`) silently removes it from any task's attachment list via the `TaskDocument` cascade, with no placeholder or "[Deleted document]" marker shown in the task UI (depends on T037, T043)
- [X] T045 [US3] Add a "Recent Documents" widget (5 most recent documents via `GetMyDocumentsAsync` ordered by `UploadDate`) and a document count summary card to ContosoDashboard/Pages/Index.razor, extending `IDashboardService.GetDashboardSummaryAsync` in ContosoDashboard/Services/DashboardService.cs to include a document count (depends on T015)
- [X] T046 [US3] Manual validation: run quickstart.md Scenario 3 (metadata edit, file replace, share with no edit/delete rights for recipient, unauthorized-delete denial, Project-Manager delete with cascade and silent task-attachment removal, task attach/upload, project-upload notification, dashboard widget/count update) and fix any issues found

**Checkpoint**: All user stories are independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Administrator/audit capabilities and final cross-story verification

- [ ] T047 [P] Add an Administrator-only document activity report in ContosoDashboard/Pages/DocumentReports.razor (most-uploaded document types, most active uploaders, document access patterns) querying `DocumentActivityLogEntry` via a new `IDocumentService`/reporting method, per FR-025
- [ ] T048 Verify Administrator full-access override (FR-026) is honored across `GetByIdAsync`, `OpenReadStreamAsync`, `GetProjectDocumentsAsync`, and `DeleteAsync` authorization checks in ContosoDashboard/Services/DocumentService.cs
- [ ] T049 [P] Run quickstart.md Scenario 4 (IDOR/security: crafted `documentId` access denial, Administrator full access) and Scenario 5 (performance sanity check at ~500 documents) and address any gaps found
- [ ] T050 Run the complete quickstart.md validation end-to-end across all five scenarios as the final feature completion gate

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational completion only
- **User Story 2 (Phase 4)**: Depends on Foundational completion; extends US1's `GetMyDocumentsAsync`/`DocumentDownload` work (T023–T029 build on T015/T017/T019/T021/T027)
- **User Story 3 (Phase 5)**: Depends on Foundational completion; extends US1/US2's `DocumentService` and `Documents.razor`/`ProjectDetails.razor` work
- **Polish (Phase 6)**: Depends on all three user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on other stories — the MVP slice
- **User Story 2 (P2)**: Builds on US1's list/download foundation (same files extended, not replaced) but is independently testable once its own tasks are done
- **User Story 3 (P3)**: Builds on US1/US2's `DocumentService` and pages but adds entirely new entities (`DocumentShare`, `TaskDocument`) and is independently testable once its own tasks are done

### Within Each User Story

- Service-layer methods before the Razor UI that calls them
- `DocumentService` authorization logic before any UI exposing the corresponding action
- Entity/DbContext changes before service methods that query the new tables
- Manual quickstart validation task is last in each phase

### Parallel Opportunities

- Setup tasks T002/T003 can run in parallel
- Foundational tasks T005/T006 (different model files) and T008/T009 (different service files) can run in parallel
- Within US1: T017 can run in parallel with T018 (different files), once T016 is done
- Within US2: T026 can run in parallel with other US2 tasks once T023 is done
- Within US3: T031/T032 (different model files) and T034 (different file) can run in parallel at the start of the phase
- Within Polish: T047 and T049 can run in parallel

---

## Parallel Example: Foundational Phase

```bash
# Launch independent foundational model/service tasks together:
Task: "Create Document entity and DocumentCategory enum in ContosoDashboard/Models/Document.cs"
Task: "Create DocumentActivityLogEntry entity and DocumentActivityType enum in ContosoDashboard/Models/DocumentActivityLogEntry.cs"
Task: "Create IFileStorageService interface and LocalFileStorageService implementation in ContosoDashboard/Services/IFileStorageService.cs"
Task: "Create IMalwareScanService interface and SimulatedMalwareScanService implementation in ContosoDashboard/Services/IMalwareScanService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Run quickstart.md Scenario 1 independently
5. Deploy/demo if ready — centralized document upload is already a usable improvement over scattered local/email storage

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Validate (Scenario 1) → Demo (MVP!)
3. Add User Story 2 → Validate (Scenario 2) → Demo
4. Add User Story 3 → Validate (Scenario 3) → Demo
5. Polish: Administrator reporting + IDOR/performance verification (Scenarios 4–5)

### Parallel Team Strategy

With multiple developers, once Foundational is done:
- Developer A: User Story 1 (T014–T022)
- Developer B: User Story 2 (T023–T030, starting once T015/T017/T019/T021 land)
- Developer C: User Story 3 (T031–T046, starting its own new entities immediately, integrating DocumentService extensions once US1/US2 methods stabilize)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- No automated test suite exists in this repository; each phase ends with a manual quickstart.md validation task instead of automated test tasks
- Commit after each task or logical group
- Stop at any checkpoint to validate a story independently
- All document file I/O MUST go through `IFileStorageService`; all malware checks MUST go through `IMalwareScanService` — no task should bypass these abstractions (Constitution Principle II)
- All authorization MUST be re-verified in `DocumentService`/`DocumentDownload.cshtml.cs` independent of UI state (Constitution Principle I, FR-019)

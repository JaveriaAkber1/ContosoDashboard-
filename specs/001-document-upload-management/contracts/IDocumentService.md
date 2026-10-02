# Contract: IDocumentService

This project is a server-rendered Blazor app, not a public API; its "external interfaces" are the service-layer contracts that Razor pages/components depend on. This contract is the primary authorization and orchestration boundary (Constitution Principle I and III).

```csharp
public interface IDocumentService
{
    Task<Document> UploadAsync(DocumentUploadRequest request, int requestingUserId);

    Task<Document?> GetByIdAsync(int documentId, int requestingUserId);

    Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentListFilter? filter = null);

    Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId);

    Task<List<Document>> GetSharedWithMeAsync(int requestingUserId);

    Task<List<Document>> SearchAsync(string query, int requestingUserId);

    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadataUpdate update, int requestingUserId);

    Task<bool> ReplaceFileAsync(int documentId, DocumentUploadRequest replacementFile, int requestingUserId);

    Task<bool> DeleteAsync(int documentId, int requestingUserId);

    Task<bool> ShareAsync(int documentId, int recipientUserId, int requestingUserId);

    Task<Stream?> OpenReadStreamAsync(int documentId, int requestingUserId);
}
```

## Method contracts

### `UploadAsync`
- **Pre-conditions**: `request.FileSizeBytes` ≤ 25 MB; `request.FileType`/extension in allow-list; `request.Title` and `request.Category` present; if `request.ProjectId` set, `requestingUserId` must be a member/manager of that project or an Administrator.
- **Behavior**: Runs `IMalwareScanService.ScanAsync()` first. On pass: generates GUID storage path → `IFileStorageService.UploadAsync()` → on success, inserts `Document` row → on DB failure, calls `IFileStorageService.DeleteAsync()` to remove the orphaned file (FR-009). Logs a `DocumentActivityLogEntry` (`Upload`). If `ProjectId` is set, triggers `DocumentAddedToProject` notifications to other project members.
- **Post-conditions**: Either a fully-persisted `Document` with a retrievable file, or no `Document` row and no stored file — never one without the other.
- **Failure modes**: Throws a validation exception (size/type/malware) caught by the caller and surfaced as a user-facing error message; does not create partial state.

### `GetByIdAsync` / `OpenReadStreamAsync`
- **Authorization** (re-verified server-side, independent of UI, per FR-019): returns data/stream only if `requestingUserId` is the uploader, an Administrator, a Project Manager of the document's project, or a `DocumentShare` recipient. Otherwise returns `null` (caller maps to 403/404 — never reveals existence to unauthorized callers).

### `GetMyDocumentsAsync` / `GetProjectDocumentsAsync` / `GetSharedWithMeAsync` / `SearchAsync`
- Every result set is pre-filtered to documents the `requestingUserId` is authorized to see (FR-013). `GetProjectDocumentsAsync` additionally verifies project membership before returning any rows (empty list, not an exception, if unauthorized — matches edge case "zero accessible documents" behavior).

### `UpdateMetadataAsync` / `ReplaceFileAsync` / `DeleteAsync`
- Only the uploader may call `UpdateMetadataAsync`/`ReplaceFileAsync` successfully (FR-016/FR-017). `DeleteAsync` succeeds for the uploader OR a Project Manager of the document's project (FR-018); all others get `false`/denied. `DeleteAsync` cascades `DocumentShare` and `TaskDocument` rows and writes a `Delete` activity-log entry with `DocumentId = null` after the cascade (research.md §8).
- `ReplaceFileAsync` re-runs the full upload validation pipeline (type/size/malware scan) and only swaps `StoragePath`/file bytes after the new file passes and is written; the original file is deleted only after the DB update commits.

### `ShareAsync`
- Only the uploader may share (FR-020). Creates/updates a `DocumentShare` row, sends a `DocumentShared` notification to the recipient, logs a `Share` activity entry. Grants view/download/preview only — never edit/delete rights (spec Clarifications).

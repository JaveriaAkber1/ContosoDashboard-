# Contract: Document Download / Preview Endpoint

The only true external HTTP surface introduced by this feature (everything else is server-rendered Blazor). Implemented as a Razor Page + `PageModel` (`Pages/DocumentDownload.cshtml.cs`), consistent with the existing `Login`/`Logout` pattern — see research.md §2.

## `GET /documents/{documentId}/file?mode={download|inline}`

| Aspect | Contract |
|---|---|
| Authentication | Requires an authenticated session (`[Authorize]` on the `PageModel`); unauthenticated requests redirect to `/login` per existing cookie-auth configuration |
| Authorization | `OnGetAsync` MUST call `IDocumentService.GetByIdAsync(documentId, currentUserId)` (or an equivalent explicit check) and return `NotFound()`/`Forbid()` if it returns `null` — this is the service-layer re-verification required by Constitution Principle I and FR-019. A crafted/guessed `documentId` for a document the user cannot access MUST NOT leak file content or existence via timing/response differences beyond a generic not-found response. |
| `mode=download` | Response sets `Content-Disposition: attachment; filename="{OriginalFileName}"` — triggers a browser download using the document's original display name (never the internal GUID storage path) |
| `mode=inline` | Response sets `Content-Disposition: inline` with the correct `Content-Type` (`FileType`) — used by the preview UI for PDF/JPEG/PNG documents (FR-015) embedded via `<iframe>`/`<img>` |
| Success response | `200 OK`, streamed file body via `IFileStorageService.DownloadAsync(document.StoragePath)` |
| Not found / forbidden | `404 Not Found` (generic — does not distinguish "doesn't exist" from "exists but you can't see it", preventing information disclosure) |
| Side effects | Logs a `DocumentActivityLogEntry` with `ActionType = Download` on every successful `200` response (FR-024) |

## Consumers

- "My Documents" / "Project Documents" / "Shared with Me" list views link to this endpoint with `mode=download` for the download action.
- The preview modal/panel (triggered from the same views, FR-015) links to this endpoint with `mode=inline` for PDF/JPEG/PNG documents only; other file types do not expose a preview action.

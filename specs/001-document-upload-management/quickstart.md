# Quickstart: Validating Document Upload and Management

This guide exercises the feature end-to-end against the seeded training data. It does not duplicate implementation details — see [data-model.md](./data-model.md) and [contracts/](./contracts/) for schema and interface specifics.

## Prerequisites

- .NET 10 SDK installed; SQL Server LocalDB available (existing project prerequisite).
- Repository builds and runs as-is: `dotnet run --project ContosoDashboard/ContosoDashboard.csproj`.
- Seeded users available via the mock login page (`/login`): `admin@contoso.com` (Administrator), `camille.nicole@contoso.com` (Project Manager, manages "ContosoDashboard Development"), `floris.kregel@contoso.com` (Team Lead), `ni.kang@contoso.com` (Employee, project member).
- A few small local test files: one valid PDF (<25 MB), one valid JPEG/PNG, one file >25 MB, one unsupported type (e.g., `.zip`), and one file containing the EICAR test string (save a `.txt` file containing exactly: `X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*`).

## Scenario 1 — Upload and categorize (User Story 1 / P1)

1. Log in as `ni.kang@contoso.com` (Employee).
2. Navigate to the document upload UI; select the valid PDF, enter a title, choose category "Reports", leave project unset.
3. **Expected**: progress indicator shown, success message, document appears in "My Documents" with correct title/category/size/date.
4. Repeat upload, this time associating the document with the "ContosoDashboard Development" project (user must be a member).
5. **Expected**: document also appears in that project's Project Documents view.
6. Attempt to upload the >25 MB file. **Expected**: rejected with a clear error, no document created.
7. Attempt to upload the `.zip` file. **Expected**: rejected with a clear error, no document created.
8. Upload the EICAR test file. **Expected**: rejected with a malware-scan error message, no document created, no file left on disk.

## Scenario 2 — Browse, search, download, preview (User Story 2 / P2)

1. As `ni.kang@contoso.com`, open "My Documents": verify title, category, upload date, file size, associated project columns; sort by each column.
2. Filter by category and by date range; verify filtered results.
3. Open the "ContosoDashboard Development" Project Documents view as a different project member (e.g., `floris.kregel@contoso.com`); verify the previously-uploaded project document is visible and downloadable.
4. Search by a keyword matching the uploaded document's title; verify it is returned. Search by a keyword that matches nothing; verify an empty "no results" state (not an error).
5. Preview the PDF and the JPEG/PNG document in-browser (no download triggered); verify the content renders.
6. Download a document; verify the downloaded filename matches `OriginalFileName`, not the internal GUID storage path.

## Scenario 3 — Manage, share, integrate (User Story 3 / P3)

1. As the uploader (`ni.kang@contoso.com`), edit the document's title/description/category/tags; verify changes reflected everywhere it's listed.
2. Replace the document's file with a new valid file; verify metadata is preserved and the new file is served on subsequent downloads/previews.
3. Share the document with `floris.kregel@contoso.com`. As that user, verify: an in-app notification was received, the document appears under "Shared with Me", the document can be downloaded/previewed, and that **no edit or delete option is available** to the share recipient.
4. As `ni.kang@contoso.com`, attempt to delete a document uploaded by someone else that is not shared with them and is not in a project they manage. **Expected**: denied.
5. As `camille.nicole@contoso.com` (Project Manager of "ContosoDashboard Development"), delete a document uploaded by another user within that project. **Expected**: deletion succeeds; the document disappears from all views, its `DocumentShare` rows are removed, and if it was attached to a task, it silently disappears from that task's attachment list with no placeholder (per spec Clarifications).
6. Open a task in the "ContosoDashboard Development" project; attach an existing document and/or upload a new one directly from the task detail page. **Expected**: the document appears in the task's attachment list and (if newly uploaded without a project) is automatically associated with the task's project.
7. As a member of a project, upload a new document to that project. **Expected**: other project members receive a notification, and their dashboard's "Recent Documents" widget and document count update.

## Scenario 4 — Security / IDOR verification (Constitution Principle I)

1. As `ni.kang@contoso.com`, note the `documentId` of a document uploaded by `floris.kregel@contoso.com` that is not shared and not in a common project.
2. Manually navigate to the download endpoint URL for that `documentId` (e.g., `/documents/{id}/file?mode=download`).
3. **Expected**: access denied (generic not-found/forbidden response) — the file is not served, regardless of any UI-level link being absent.
4. As `admin@contoso.com` (Administrator), access any document from any user/project. **Expected**: always succeeds (FR-026).

## Scenario 5 — Performance sanity check (FR-028)

1. Seed or simulate up to ~500 documents across a project (can be done via a one-off script or by uploading in a loop during manual testing).
2. Load the Project Documents view and a search query against that data set.
3. **Expected**: list and search responses feel near-instant and complete well under 2 seconds on local LocalDB; document preview opens within ~3 seconds.

## Done criteria

All scenarios above pass manually (no automated test project exists yet in this repository — see Technical Context in [plan.md](./plan.md)). This checklist should be re-run for any subsequent change touching authorization, file handling, or document data access, per the constitution's Development Workflow & Quality Gates section.

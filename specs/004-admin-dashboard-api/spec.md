# Feature Specification: Admin Dashboard API Endpoints

**Feature Branch**: `004-admin-dashboard-api`  
**Created**: 2025-07-18  
**Status**: Draft  
**Updated**: 2026-05-03  
**Input**: User description: "Implement missing API backend endpoints and business logic to support the frontend admin dashboard's new capabilities — scan results browsing, TMDB reassignment, TV show grouping, batch enrichment, and file renaming."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Scan Results Browser Endpoint (Priority: P1)

As an admin using the dashboard, I want to retrieve all scan item decisions for a given scan run so I can browse every file the scanner processed — not just review items — and verify automatic TMDB matches.

The API provides a paginated endpoint that returns all `ScanItemDecision` records for a specified scan run. Each record includes the file path, decision type, assigned TMDB entry (ID, title, year, poster), TMDB candidates that were considered, parsed metadata (title, year, season, episode number), media type, and library root reference. The endpoint supports filtering by decision type, media type, and library root.

**Why this priority**: The scan results browser is the central data pipeline for all downstream features (reassignment, TV grouping, enrichment, renaming). Without this endpoint, no other new feature can function.

**Independent Test**: Can be tested by running a scan, then calling `GET /api/v1/admin/scan/{scanId}/decisions` with various filter combinations and verifying correct paginated results with all required fields populated.

**Acceptance Scenarios**:

1. **Given** a completed scan with file decisions, **When** the admin requests `GET /api/v1/admin/scan/{scanId}/decisions` with no filters, **Then** the response contains a paginated list of all scan item decisions for that scan run, each including file path, decision type, assigned TMDB entry, TMDB candidates, parsed metadata, media type, and library root reference.
2. **Given** a completed scan, **When** the admin filters by decision type (e.g., `Added`), **Then** only decisions matching that type are returned.
3. **Given** a completed scan, **When** the admin filters by media type (e.g., `Film`), **Then** only decisions for that media type are returned.
4. **Given** a completed scan, **When** the admin filters by library root ID, **Then** only decisions from files in that library root are returned.
5. **Given** a completed scan, **When** the admin combines multiple filters (decision type + media type + library root), **Then** only decisions matching all filters are returned.
6. **Given** a scan ID that does not exist, **When** the admin requests its decisions, **Then** the response returns a not-found error within the standard API envelope.
7. **Given** a completed scan with many decisions, **When** the admin requests page 2 with a page size of 25, **Then** the response contains the correct subset of results with pagination metadata (total count, page number, page size).

---

### User Story 2 — TMDB Reassignment Endpoint (Priority: P1)

As an admin, I want to change the TMDB assignment for an already-matched scan item decision so I can correct automatic matches that were wrong.

The API provides an endpoint to reassign the TMDB source for a specific scan item decision. The admin supplies the new TMDB ID and media type. The system updates the linked media file's association to point to the correct TMDB entry and persists the change.

**Why this priority**: Reassignment is the primary corrective action in the scan results workflow. Without it, admins cannot fix wrong automatic matches, undermining the quality control purpose of the scan results browser.

**Independent Test**: Can be tested by calling `PUT /api/v1/admin/scan-decisions/{id}/reassign` with a valid TMDB ID and media type, then verifying the decision's assigned TMDB entry and the linked media file's association are updated.

**Acceptance Scenarios**:

1. **Given** a scan item decision with an existing TMDB assignment, **When** the admin calls `PUT /api/v1/admin/scan-decisions/{id}/reassign` with a new TMDB ID and media type, **Then** the decision's TMDB assignment is updated and the linked media file's media association is changed to the new TMDB entry.
2. **Given** a scan item decision with no existing TMDB assignment, **When** the admin calls reassign with a valid TMDB ID and media type, **Then** the TMDB source is assigned and the media file is linked accordingly.
3. **Given** an invalid scan item decision ID, **When** the admin calls reassign, **Then** the response returns a not-found error.
4. **Given** a reassign request with a missing or invalid TMDB ID, **When** the request is submitted, **Then** the response returns a validation error specifying the issue.
5. **Given** a reassign request with an invalid media type value, **When** the request is submitted, **Then** the response returns a validation error.

---

### User Story 3 — TV Show Groups Endpoint (Priority: P2)

As an admin, I want to retrieve TV show episode groupings for a scan run so I can see which episodes belong to which show and manage TMDB assignments at the show level.

The API provides an endpoint that computes TV show groups on-the-fly by grouping `ScanItemDecision` rows by parsed show name within a specified scan run. No dedicated database table exists for these groups — they are transient, computed at query time. Each group includes a derived group ID (hash of scan ID + parsed show name), the parsed show name, episode count, and TMDB assignment status.

**Why this priority**: TV show grouping enables bulk operations (show-level assignment, batch rename) that make managing large TV libraries practical. Without grouping, admins must handle hundreds of episodes individually.

**Independent Test**: Can be tested by running a scan on a library containing TV show files, then calling `GET /api/v1/admin/scan-decisions/tv-groups?scanId={scanId}` and verifying that episodes are correctly grouped by parsed show name with accurate counts.

**Acceptance Scenarios**:

1. **Given** a completed scan containing TV show episode files, **When** the admin requests `GET /api/v1/admin/scan-decisions/tv-groups?scanId={scanId}`, **Then** the response contains a list of TV show groups, each with a derived group ID, parsed show name, episode count, and TMDB assignment status.
2. **Given** a scan with episodes from multiple shows, **When** the admin requests TV groups, **Then** each show has its own group with the correct episode count.
3. **Given** a scan with no TV show files, **When** the admin requests TV groups, **Then** the response returns an empty list.
4. **Given** a TV show group where a TMDB source has been assigned at the show level, **When** the admin requests TV groups, **Then** the group's TMDB assignment status reflects the assigned entry (ID, title, year, poster).
5. **Given** an invalid or non-existent scan ID, **When** the admin requests TV groups, **Then** the response returns a not-found error.

---

### User Story 4 — TV Show Group Assignment Endpoint (Priority: P2)

As an admin, I want to assign a TMDB source at the TV show group level so the assignment propagates to all episode files in the group without manual per-episode work.

The API provides an endpoint to assign a TMDB TV show entry to an entire TV show group. The system identifies all `ScanItemDecision` rows belonging to the group (same parsed show name + scan ID), updates each one's TMDB assignment, and updates the linked media file associations.

**Why this priority**: Show-level assignment is the key efficiency feature for TV libraries. It transforms a task that could take hours (assigning hundreds of episodes individually) into a single action.

**Independent Test**: Can be tested by calling `PUT /api/v1/admin/tv-groups/{groupId}/assign` with a TMDB TV show ID, then verifying all episode decisions in that group have the TMDB assignment and their media files are linked correctly.

**Acceptance Scenarios**:

1. **Given** a TV show group with 10 unassigned episodes, **When** the admin calls `PUT /api/v1/admin/tv-groups/{groupId}/assign` with a TMDB TV show ID, **Then** all 10 episode decisions are updated with the TMDB assignment and their linked media files are associated with the TMDB series.
2. **Given** a TV show group already assigned to TMDB series A, **When** the admin reassigns to TMDB series B, **Then** all episodes are updated to series B.
3. **Given** an invalid group ID, **When** the admin calls assign, **Then** the response returns a not-found error.
4. **Given** a request with a missing TMDB ID, **When** the request is submitted, **Then** the response returns a validation error.
5. **Given** a TV show group with episodes from multiple library roots, **When** the admin assigns a TMDB source, **Then** all episodes across all roots in the group are updated.

---

### User Story 5 — Batch TMDB Enrichment Endpoints (Priority: P2)

As an admin, I want to launch a batch TMDB enrichment process that fetches full metadata for all validated media entries, and monitor its progress, so the media library has rich metadata for user-facing display.

The API provides two endpoints: one to start the enrichment process and one to poll its progress. Enrichment is incremental by default — only new or changed-assignment entries are processed; already-enriched unchanged entries are skipped. The system prevents concurrent enrichment runs. Progress reporting includes current item, completed count, total count, and upon completion a summary of enriched/failed/skipped entries.

**Why this priority**: Enrichment is the final step in the scan → review → validate → enrich pipeline. Without it, matched files have only basic TMDB IDs but no rich metadata (descriptions, cast, genres, posters) for end-user display.

**Independent Test**: Can be tested by assigning TMDB sources to several media entries, calling `POST /api/v1/admin/enrichment/start`, polling `GET /api/v1/admin/enrichment/status` until completion, and verifying media entries are populated with full TMDB metadata.

**Acceptance Scenarios**:

1. **Given** validated media entries exist with TMDB assignments, **When** the admin calls `POST /api/v1/admin/enrichment/start`, **Then** the enrichment process starts as a background operation and the response confirms the process has been initiated.
2. **Given** the enrichment process is running, **When** the admin calls `GET /api/v1/admin/enrichment/status`, **Then** the response includes the current status (Running), current item being processed, completed count, and total count.
3. **Given** the enrichment process completes, **When** the admin polls status, **Then** the response includes a summary: enriched count, failed count, skipped count, and error details for any failed entries.
4. **Given** an enrichment process is already running, **When** the admin calls `POST /api/v1/admin/enrichment/start` again, **Then** the response returns a conflict error indicating enrichment is already in progress.
5. **Given** some entries were previously enriched and their TMDB assignment has not changed, **When** the admin starts a new enrichment run, **Then** those entries are skipped and only new or changed-assignment entries are processed.
6. **Given** no validated media entries exist, **When** the admin calls start, **Then** the response indicates there are no entries to enrich.
7. **Given** the TMDB API returns errors for some entries during enrichment, **When** the process completes, **Then** the failed entries are listed with error reasons and the enrichment reports partial completion.

---

### User Story 6 — File Rename Endpoint (Priority: P3)

As an admin, I want to rename a media file on the NAS to match its assigned TMDB source, with a preview option, so future scans can match the file correctly.

The API provides an endpoint that renames a physical file on the locally-mounted NAS using standard filesystem I/O. In preview mode, the endpoint returns the proposed new name without executing the rename. The rename is file-in-place only — no folder restructuring. Naming conventions are "Movie Title (Year)" for movies and "Show Name - SXXEXX - Episode Title" for TV episodes. The database file path record is updated after a successful rename. The operation is atomic — no partial rename state.

**Why this priority**: File renaming is a quality-of-life improvement that prevents repeat scan issues. It modifies physical files on the NAS, so it carries inherent risk and is lower priority than the core browsing/assignment/enrichment workflow.

**Independent Test**: Can be tested by calling `POST /api/v1/admin/files/{id}/rename?preview=true` to verify the proposed name, then calling without preview to execute, and confirming both the filesystem and database are updated.

**Acceptance Scenarios**:

1. **Given** a media file with a TMDB movie assignment, **When** the admin calls `POST /api/v1/admin/files/{id}/rename?preview=true`, **Then** the response returns the current file name and the proposed new name in "Movie Title (Year)" format without executing the rename.
2. **Given** a media file with a TMDB TV episode assignment, **When** the admin calls preview, **Then** the proposed name follows "Show Name - SXXEXX - Episode Title" format.
3. **Given** a media file with a TMDB assignment, **When** the admin calls `POST /api/v1/admin/files/{id}/rename?preview=false`, **Then** the physical file is renamed on the NAS, the database file path record is updated, and a success response is returned.
4. **Given** the target file name already exists on the NAS, **When** the admin attempts the rename, **Then** the response returns a conflict error and no rename occurs.
5. **Given** the source file has been moved or deleted since the last scan, **When** the admin attempts the rename, **Then** the response returns a not-found error indicating the file was not found at the expected path.
6. **Given** a rename fails due to a filesystem error (permissions, disk full), **When** the error occurs, **Then** the file retains its original name and path, the database is not updated, and the response returns an appropriate error message.
7. **Given** a media file with no TMDB assignment, **When** the admin attempts to rename, **Then** the response returns a validation error indicating a TMDB assignment is required before renaming.

---

### User Story 7 — TV Show Group Batch Rename Endpoint (Priority: P3)

As an admin, I want to batch rename all episode files under a TV show group so I can standardize naming for an entire show in one operation.

The API provides an endpoint to rename all episode files within a TV show group. In preview mode, it returns the proposed names for all episodes. In execution mode, each file is renamed in-place within its current directory following the "Show Name - SXXEXX - Episode Title" convention. No folder restructuring occurs.

**Why this priority**: Batch rename extends the single-file rename to TV shows. It is a convenience feature that depends on both TV grouping and single-file rename being implemented first.

**Independent Test**: Can be tested by calling `POST /api/v1/admin/tv-groups/{groupId}/rename?preview=true` to see all proposed names, then executing without preview and verifying all episode files are renamed on the NAS and in the database.

**Acceptance Scenarios**:

1. **Given** a TV show group with 10 episodes, **When** the admin calls `POST /api/v1/admin/tv-groups/{groupId}/rename?preview=true`, **Then** the response returns a list of 10 entries, each showing the current file name and proposed new name.
2. **Given** a TV show group with TMDB assignment, **When** the admin calls rename without preview, **Then** all episode files are renamed following "Show Name - SXXEXX - Episode Title" format, each file in-place within its current directory.
3. **Given** one episode file's target name already exists on the NAS, **When** the admin attempts batch rename, **Then** the response returns a conflict error identifying the problematic file, and no files in the batch are renamed (atomic batch operation).
4. **Given** a TV show group with no TMDB assignment, **When** the admin attempts batch rename, **Then** the response returns a validation error indicating TMDB assignment is required first.
5. **Given** an invalid group ID, **When** the admin calls batch rename, **Then** the response returns a not-found error.

---

### User Story 8 — ScanItemDecision Entity Enhancement (Priority: P1)

As a system, the `ScanItemDecision` entity must be extended with additional fields so the scan results browser and downstream features have the data they need.

The existing `ScanItemDecision` entity tracks every file decision per scan run. It needs to be enhanced with: assigned TMDB ID and kind (from the matched media record or direct assignment), TMDB candidates JSON (the candidates considered during matching), parsed metadata (title, year, season, episode number), media type (Film/TvShow), and library root reference (derivable from MediaFile → LibraryRoot).

**Why this priority**: This is a foundational data model change that all other new features depend on. Without these fields, the scan results browser cannot display required information and reassignment/grouping have no data to work with.

**Independent Test**: Can be tested by running a scan and verifying the `ScanItemDecision` records in the database contain the new fields populated with correct values — assigned TMDB info, candidates JSON, parsed metadata, media type, and library root reference.

**Acceptance Scenarios**:

1. **Given** the scanner processes a movie file and finds a TMDB match, **When** the scan decision is persisted, **Then** the `ScanItemDecision` record includes the assigned TMDB ID and kind (`Film`), the TMDB candidates JSON, and parsed metadata (title, year).
2. **Given** the scanner processes a TV episode file, **When** the scan decision is persisted, **Then** the record includes parsed metadata (show title, season number, episode number), media type (`TvShow`), and the library root reference.
3. **Given** the scanner processes a file with no TMDB match, **When** the scan decision is persisted, **Then** the assigned TMDB fields are null but candidates JSON and parsed metadata are still populated.
4. **Given** a scan decision is linked to a media file in a specific library root, **When** the decision is queried, **Then** the library root reference is correctly populated (derived from MediaFile → LibraryRoot).

---

### Edge Cases

- What happens when the admin requests scan decisions for a scan that is still running? The endpoint returns the decisions recorded so far with a note that the scan is still in progress; pagination and filters work normally on the available data.
- What happens when the admin reassigns a TMDB entry to a file that has already been enriched? The old TMDB metadata association is replaced. The entry is flagged as "changed assignment" so the next enrichment run will re-enrich it automatically.
- What happens when the TMDB API rate limit is hit during enrichment? The enrichment process pauses and retries with exponential backoff. If rate limiting persists beyond a reasonable threshold, the enrichment reports partial completion with the count of remaining items and specific error details.
- What happens when the admin tries to rename a file and the target name conflicts with an existing file? The endpoint performs a **case-insensitive** comparison (`StringComparison.OrdinalIgnoreCase`) against existing filenames in the target directory before executing any rename. If a match is found, the endpoint returns a conflict error with a clear message identifying the collision, and no rename is performed. This convention protects against collisions on case-insensitive filesystems (FAT32, exFAT, Samba) as well as case-sensitive ones.
- What happens when a TV show group batch rename partially fails (e.g., file 5 of 10 has a conflict)? The batch rename is validated in preview mode first. If any file would conflict, the entire batch is rejected before any renames are executed — no partial rename state.
- What happens when the admin calls enrichment start but no entries need enrichment (all already enriched and unchanged)? The response returns a success with zero items to process and an informational message.
- What happens when a scan item decision's linked media file has been deleted from the database? The decision still exists but operations that require the media file (rename, reassignment) return a not-found error for the media file, with a recommendation to re-scan.
- What happens when the computed TV show group has episodes with inconsistent parsed show names due to variant naming? Episodes are grouped by exact parsed show name match. Variants (e.g., "The Office" vs "The Office US") form separate groups. The admin can individually reassign episodes between groups via per-episode TMDB reassignment.
- What happens when a non-admin user calls any of these endpoints? All endpoints require AdminOnly authorization. Unauthorized requests receive a 403 Forbidden response.
- What happens when the enrichment process crashes mid-run? On next application startup, any `EnrichmentRun` rows with `Status = Running` are automatically transitioned to `Status = Failed` with `FailureReason = "Process restarted unexpectedly"` by `DatabaseInitializer` or an `IHostedService` startup hook. This clears the concurrency lock. Entries already enriched in that run retain their metadata. The admin can start a new enrichment run, which will pick up remaining un-enriched entries.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a paginated endpoint (`GET /api/v1/admin/scan/{scanId}/decisions`) that returns all scan item decisions for a given scan run, including file path, decision type, assigned TMDB entry, TMDB candidates, parsed metadata, media type, and library root reference.
- **FR-002**: System MUST support filtering scan decisions by decision type (`ScanDecisionKind`), media type (`Film`/`TvShow`), and library root ID.
- **FR-003**: System MUST provide an endpoint (`PUT /api/v1/admin/scan-decisions/{id}/reassign`) to change the TMDB assignment for a scan item decision, accepting a TMDB ID and media type in the request body.
- **FR-004**: System MUST update the linked media file's media association when a TMDB reassignment occurs.
- **FR-005**: System MUST provide an endpoint (`GET /api/v1/admin/scan-decisions/tv-groups?scanId={scanId}`) that computes TV show groups on-the-fly by grouping `ScanItemDecision` rows by parsed show name + scan ID, returning group ID (hash), parsed show name, episode count, and TMDB assignment status.
- **FR-006**: System MUST NOT create a dedicated database table for TV show groups — grouping is transient and computed at query time.
- **FR-007**: System MUST provide an endpoint (`PUT /api/v1/admin/tv-groups/{groupId}/assign`) to assign a TMDB TV show entry at the group level, propagating the assignment to all episode decisions and their linked media files.
- **FR-008**: System MUST provide an endpoint (`POST /api/v1/admin/enrichment/start`) to launch a batch TMDB enrichment process as a background operation.
- **FR-009**: System MUST process enrichment incrementally — only new or changed-assignment entries are enriched; already-enriched unchanged entries are skipped.
- **FR-010**: System MUST prevent concurrent enrichment runs by querying for an active `Running` row in the `EnrichmentRuns` SQL Server table before starting a new run; if such a row exists, the system returns a conflict error. The `EnrichmentRun` entity is persisted to a dedicated DB table via EF Core (not in-memory), enabling crash recovery and reliable lock detection across restarts.
- **FR-011**: System MUST provide an endpoint (`GET /api/v1/admin/enrichment/status`) that returns enrichment progress (status, current item, completed count, total count) and completion summary (enriched, failed, skipped counts with error details).
- **FR-012**: System MUST provide an endpoint (`POST /api/v1/admin/files/{id}/rename?preview={bool}`) for file rename operations on the locally-mounted NAS using standard filesystem I/O.
- **FR-013**: System MUST return the proposed file name without executing the rename when preview mode is enabled.
- **FR-014**: System MUST rename files in-place within their current directory — no folder creation or file movement to other directories.
- **FR-015**: System MUST follow naming conventions: "Movie Title (Year)" for movies, "Show Name - SXXEXX - Episode Title" for TV episodes.
- **FR-016**: System MUST update the database file path record after a successful rename.
- **FR-017**: System MUST detect and prevent naming conflicts before executing a rename using **case-insensitive** comparison (`string.Equals(existingName, newName, StringComparison.OrdinalIgnoreCase)`). This ensures safety on case-insensitive filesystems (FAT32, exFAT, Samba-mounted NAS); on Linux ext4 the check is conservative but safe — it prevents renames that would collide on case-insensitive targets. If the target name already exists under this comparison, the system returns a conflict error and no rename is performed.
- **FR-018**: System MUST ensure rename operations are atomic — no partial rename state; if a rename fails, the file retains its original name and the database is not updated.
- **FR-019**: System MUST provide an endpoint (`POST /api/v1/admin/tv-groups/{groupId}/rename?preview={bool}`) for batch renaming all episode files in a TV show group with the same in-place rename rules as single-file rename.
- **FR-020**: System MUST validate all batch rename targets before executing any renames — if any file would conflict, the entire batch is rejected.
- **FR-021**: System MUST extend the `ScanItemDecision` entity with: assigned TMDB ID and kind, TMDB candidates JSON, parsed metadata (title, year, season, episode number), media type, and library root reference.
- **FR-022**: System MUST require AdminOnly authorization for all new endpoints, returning 403 Forbidden for unauthorized requests.
- **FR-023**: System MUST use request validation for all new endpoints, returning structured validation errors for invalid input.
- **FR-024**: System MUST wrap all responses in the standard `ApiResponse<T>` envelope, using the Result pattern for business errors (no exceptions for expected failure cases).
- **FR-025**: System MUST handle TMDB API rate limiting during enrichment with exponential backoff retry logic: **5 retries maximum, initial delay 1 second, multiplier 2×, cap 30 seconds, applied to HTTP 429 and 503 responses**. This is implemented via the existing `AddStandardResilienceHandler()` Polly pipeline already configured on `ITmdbService` — no additional Polly configuration is required.
- **FR-026**: System MUST report enrichment failures per-entry with specific error reasons, allowing partial completion.
- **FR-027**: On application startup, any `EnrichmentRun` rows with `Status = Running` MUST be automatically transitioned to `Failed` with `FailureReason = "Process restarted unexpectedly"` — implemented in `DatabaseInitializer` or an `IHostedService` startup hook — to prevent false concurrency locks after a crash. This mirrors the pattern used for stale scan runs.
- **FR-028**: Enrichment MUST populate the following fields on the `Media` entity from TMDB: `Title`, `OriginalTitle`, `Overview`, `ReleaseDate` (films and TV shows alike — TMDB `first_air_date` maps to this existing column), `Genres` (upserted as normalized `MediaGenre` child records — NOT stored as a JSON array), `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language` (the existing `Media.Language` property stores TMDB `original_language` — field name is `Language`, NOT `OriginalLanguage`), `Status` (e.g., Released / Ended), `Runtime` (films only), `NumberOfSeasons` / `NumberOfEpisodes` (TV only). For TV shows, enrichment MUST also create/update `TvSeason` and `TvEpisode` child records (season number, episode number, episode title from `TvEpisode.Name`, air date, overview). Cast and crew (credits) are **out of scope** for this iteration.

### Key Entities

- **ScanItemDecision (enhanced)**: Represents the scanner's decision for a single file in a scan run. Existing fields: file path, decision type, scan run reference, timestamps. New fields: assigned TMDB ID and kind, TMDB candidates JSON, parsed metadata (title, year, season number, episode number), media type (Film/TvShow), library root reference. Relationships: belongs to a ScanRun, linked to a MediaFile, references a LibraryRoot. **`CandidatesJson` schema** (`nvarchar(max)` column storing a JSON string, mapped via EF Core value converter — same pattern as existing `ReviewItem.CandidatesJson` and `LibraryRoot.SearchLanguages`): each candidate object contains `tmdbId` (int), `kind` (string: `"Film"` or `"TvShow"`), `title` (string), `year` (int?), `posterPath` (string?), `overview` (string?), `score` (float — match confidence 0.0–1.0).
- **TvShowGroup (transient)**: A computed grouping of ScanItemDecision rows sharing the same parsed show name within a scan run. Not persisted in the database. Identity: derived hash of scanId + parsedShowName. Attributes: group ID, parsed show name, episode count, TMDB assignment status, list of member decision IDs.
- **EnrichmentRun**: Tracks a batch TMDB enrichment execution. **Persisted to a dedicated SQL Server table via EF Core** — durability is required for crash recovery and reliable concurrency locking. Attributes: status (`Pending`, `Running`, `Completed`, `Failed`), `FailureReason` (string, nullable), start/finish timestamps, total items, enriched count, failed count, skipped count, current item identifier, error details for failed entries (JSON string as `nvarchar(max)`). Concurrency lock: a new run is allowed only when no row with `Status = Running` exists. On next startup after a crash, any `Running` rows are transitioned to `Failed` with `FailureReason = "Process restarted unexpectedly"` by the startup hook.
- **MediaFile (existing)**: A tracked file on the NAS. Key attribute for this feature: file path (updated during rename operations), media association (updated during TMDB reassignment).
- **Media (existing)**: Represents a TMDB media entry. Key relationship: linked to MediaFile via TMDB assignment. Updated when reassignment or enrichment occurs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Admins can retrieve and filter scan decisions for a completed scan within 2 seconds for libraries of up to 10,000 files.
- **SC-002**: TMDB reassignment updates both the scan decision and linked media file association in a single operation, completing within 1 second.
- **SC-003**: TV show grouping computation returns accurate group headers with correct episode counts within 2 seconds for scans containing up to 5,000 TV episode files.
- **SC-004**: Show-level TMDB assignment propagates to all episodes in the group within 3 seconds, regardless of group size (up to 500 episodes per show).
- **SC-005**: Batch enrichment processes at least 50 media entries per minute under normal TMDB API conditions.
- **SC-006**: Enrichment runs incrementally — re-running enrichment with no new or changed entries completes in under 5 seconds with zero TMDB API calls.
- **SC-007**: File rename operations complete atomically — 100% of rename operations either fully succeed (filesystem + database updated) or fully fail (no changes).
- **SC-008**: Batch rename preview for a TV show group of 100 episodes returns all proposed names within 2 seconds.
- **SC-009**: All new endpoints return structured error responses within the `ApiResponse<T>` envelope — zero unhandled exceptions in production for expected error cases.
- **SC-010**: Concurrent enrichment prevention is enforced — attempting to start a second enrichment run while one is active always returns a conflict error.

## Assumptions

- The existing `ScanItemDecision`, `MediaFile`, `Media`, `ScanRun`, and `LibraryRoot` domain entities are in place and mapped in EF Core with `Microsoft.EntityFrameworkCore.SqlServer` (SQL Server).
- The existing `AdminController`, `AdminScanController`, `AdminReviewController`, `AdminLibraryRootsController`, `TmdbController`, and `HealthController` are operational and follow the established Clean Architecture pattern (Domain → Application → Infrastructure → API).
- MediatR is configured for CQRS and all new endpoints will follow the same command/query handler pattern used by existing admin endpoints.
- FluentValidation is configured and all new request DTOs will have corresponding validators.
- The `ApiResponse<T>` envelope and Result pattern are established conventions used by all existing endpoints.
- AdminOnly authorization is enforced via an existing policy or attribute applied at the controller level.
- The NAS is locally mounted as a filesystem path accessible to the application process. File rename operations use `File.Move` or equivalent standard .NET filesystem APIs — no NAS-specific protocol (SMB, NFS SDK) is required.
- The TMDB API client and import logic (used by `TmdbController`) are available as application services and can be reused by the enrichment process.
- The `ScanItemDecision` entity enhancement requires a database migration to add the new columns. Existing records will have null values for the new fields until a new scan populates them.
- TV show group identity (hash of scanId + parsedShowName) is deterministic — the same inputs always produce the same group ID, enabling stable references across requests.
- The enrichment background process runs within the application process (hosted service or similar) — no external job scheduler is required.
- Episode metadata (season number, episode number, episode title) is derived from parsed file names during scanning; episode-level TMDB API lookups for individual episode metadata are handled during enrichment.
- The file extension is preserved during rename operations (e.g., `.mkv`, `.mp4` remain unchanged).
- All new endpoints are versioned under `/api/v1/` consistent with existing API routes.
- **Rename conflict convention**: All rename conflict checks use `StringComparison.OrdinalIgnoreCase`. This is conservative but safe — it prevents collisions on case-insensitive NAS filesystems (FAT32, exFAT, Samba). On Linux ext4, this may block renames that differ only by case, which is an accepted tradeoff documented here.
- **EnrichmentRun persistence**: `EnrichmentRun` rows are stored in a dedicated SQL Server table via EF Core. The concurrency lock is row-based (query for `Status = Running`). Stale `Running` rows from crashed runs are cleared to `Failed` on startup via `DatabaseInitializer` or an `IHostedService`.
  - **Enrichment metadata scope**: "Full metadata" means: `Title`, `OriginalTitle`, `Overview`, `ReleaseDate` (TMDB `release_date` for films / `first_air_date` for TV — both map to the existing `Media.ReleaseDate` column, no new column needed), `Genres` (upserted as normalized `MediaGenre` child records — NOT a JSON array), `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language` (field name is `Language` on `Media` entity — stores TMDB `original_language` value), `Status`, `Runtime` (films), `NumberOfSeasons`/`NumberOfEpisodes` (TV). TV shows additionally populate `TvSeason` and `TvEpisode` child records. Cast/crew (credits) are out of scope for this iteration.
- **CandidatesJson schema**: The `CandidatesJson` column on `ScanItemDecision` is a `nvarchar(max)` JSON string (same pattern as `ReviewItem.CandidatesJson` and `LibraryRoot.SearchLanguages`), mapped via EF Core value converter. Schema: `{ tmdbId: int, kind: "Film"|"TvShow", title: string, year: int?, posterPath: string?, overview: string?, score: float }`.

## Clarifications

### Session 2026-05-03

- Q: How is the `EnrichmentRun` entity persisted and how is the concurrency lock implemented? → A: SQL Server table via EF Core. The `EnrichmentRun` entity is persisted to a dedicated DB table. Crash recovery requires durability (the crashed run row is updated to `Failed` on next startup). The concurrency lock (prevent duplicate runs) is implemented by querying for an active `Running` row in the table.
- Q: What is the exact JSON schema for each candidate object in `CandidatesJson` on `ScanItemDecision`? → A: Each candidate contains: `tmdbId` (int), `kind` (string: `"Film"` or `"TvShow"`), `title` (string), `year` (int?), `posterPath` (string?), `overview` (string?), `score` (float — match confidence 0.0–1.0). Same structure as `ReviewItem.CandidatesJson`. Stored as `nvarchar(max)` JSON string via EF Core value converter (same pattern as `LibraryRoot.SearchLanguages`).
- Q: How should stale `Running` enrichment rows be handled on application restart? → A: On application startup, any `EnrichmentRun` rows with `Status = Running` are automatically transitioned to `Failed` with `FailureReason = "Process restarted unexpectedly"`. Performed in `DatabaseInitializer` or an `IHostedService` startup hook — the same pattern used for stale scan runs if any.
- Q: What fields constitute "full metadata" populated during enrichment, and what is explicitly out of scope? → A: Full metadata means populating on `Media`: `Title`, `OriginalTitle`, `Overview`, `ReleaseDate` (TMDB `release_date` / `first_air_date` both map to the existing `Media.ReleaseDate` column), `Genres` (upserted as normalized `MediaGenre` child records), `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language` (existing field — stores TMDB `original_language`; field name is `Language`, NOT `OriginalLanguage`), `Status`, `Runtime` (films), `NumberOfSeasons`/`NumberOfEpisodes` (TV). For TV shows, also populate `TvSeason` and `TvEpisode` child records (season number, episode number, episode title, air date, overview). Cast and crew (credits) are **out of scope** for this iteration.
- Q: Should rename conflict detection be case-sensitive or case-insensitive? → A: Case-insensitive (`StringComparison.OrdinalIgnoreCase`). The NAS may be mounted on a case-insensitive filesystem (FAT32, exFAT, Samba), so the backend must check for existing files case-insensitively. On Linux ext4 (case-sensitive), this is conservative but safe — it prevents renames that would collide on case-insensitive filesystems. Documented as a convention.

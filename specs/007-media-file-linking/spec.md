# Feature Specification: Media File Linking & Missing Content Detection — Backend API

**Feature Branch**: `007-media-file-linking`
**Created**: 2025-07-25
**Status**: Draft
**Input**: User description: "Link media files to each TV show or film from detail pages. Link the root folder and open file explorer. Detect missing episodes & seasons (ignore season 0). Scan result page filter: 'TMDB Assigned' should only show entries with a TMDB match but not yet imported into the collection."

## Clarifications

All decisions were established in the corresponding frontend spec session:

- **Media file linking scope**: A `MediaFile` record is linked or unlinked to a `Media` item by updating the nullable `MediaFile.MediaId` foreign key. A file may be linked to at most one `Media` item at a time. Attempting to link a file that is already linked to a different media item returns 422. Unlinking a file that does not belong to the specified media item returns 404.
- **Root folder derivation**: The effective root folder exposed in `MediaDto` is the stored `Media.RootFolder` override if non-null, otherwise the computed common parent directory derived from the `FilePath` values of all linked `MediaFile` records. When no files are linked, the computed value is null. Both the override and computed paths are plain filesystem paths (strings).
- **Completeness detection scope**: Completeness is calculated only for TV shows (`MediaType.TvShow`). Seasons where `SeasonNumber == 0` or whose `Name` contains "specials" (case-insensitive) are excluded. Episode count per season uses `TvSeason.EpisodeCount` when set; otherwise it falls back to the count of `TvEpisode` rows for that season. "Owned" episodes are those having at least one `EpisodeFileLink` on any of their linked `MediaFile` records.
- **Unlinked files query**: Returns all `MediaFile` records where `MediaId` is null, paged. Only admin callers may access this endpoint.
- **Authorization model**: All link/unlink, root-folder update, and unlinked-files endpoints require the `AdminOnly` policy. The completeness endpoint uses standard authenticated-user (`[Authorize]`) authorization, consistent with other read-only media detail endpoints.
- **EF Core migration**: A single migration (`AddMediaRootFolder`) adds the nullable `root_folder text NULL` column to the `Media` table.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Link a Media File to a Media Item (Priority: P1)

An admin navigates to a media detail page and manually associates a physical media file (already discovered by the scanner) with that media item. Once linked, the file is considered "owned" for that media entry and contributes to completeness checks. An admin can also unlink a file if the association was made in error.

**Why this priority**: Without the ability to link files to media items, neither completeness detection nor root folder derivation can function. This is the foundational write operation for the entire feature.

**Independent Test**: Submit `PUT /api/v1/admin/media/{mediaId}/files/{fileId}/link` and verify the `MediaFile.MediaId` foreign key is set to `mediaId`. Then call `DELETE /api/v1/admin/media/{mediaId}/files/{fileId}/link` and verify `MediaFile.MediaId` is null. Both operations deliver standalone value.

**Acceptance Scenarios**:

1. **Given** an admin and a `MediaFile` with `MediaId = null`, **When** the admin calls `PUT /api/v1/admin/media/{mediaId}/files/{fileId}/link`, **Then** `MediaFile.MediaId` is set to `mediaId` and the response is 200 OK.
2. **Given** an admin and a `MediaFile` already linked to the same `mediaId`, **When** the admin calls the link endpoint again, **Then** the operation is idempotent and the response is 200 OK (no state change, no error).
3. **Given** an admin and a `MediaFile` linked to a **different** `Media` item, **When** the admin calls `PUT /api/v1/admin/media/{mediaId}/files/{fileId}/link`, **Then** the response is 422 Unprocessable Entity with error code `FILE_ALREADY_LINKED`.
4. **Given** an admin and a `MediaFile` currently linked to `mediaId`, **When** the admin calls `DELETE /api/v1/admin/media/{mediaId}/files/{fileId}/link`, **Then** `MediaFile.MediaId` is set to null and the response is 200 OK.
5. **Given** an admin and a `MediaFile` not linked to `mediaId`, **When** the admin calls the unlink endpoint, **Then** the response is 404 Not Found.
6. **Given** either endpoint is called for a non-existent `mediaId` or `fileId`, **When** the request is processed, **Then** the response is 404 Not Found.
7. **Given** a non-admin authenticated user, **When** they call either endpoint, **Then** the response is 403 Forbidden.
8. **Given** an unauthenticated caller, **When** they call either endpoint, **Then** the response is 401 Unauthorized.

---

### User Story 2 — Root Folder Association on a Media Item (Priority: P1)

An admin sets or clears the root folder path for a media item. The root folder is the top-level directory of the media content on the NAS. When set, it can be used by the frontend to offer a "open folder" shortcut. When not explicitly set, the API derives it automatically from the common parent directory of all linked files.

**Why this priority**: Admins need a reliable root folder path to open the correct directory in the file explorer from the detail page. The auto-derive logic always provides a sensible default as long as files are linked, reducing the manual overhead.

**Independent Test**: Call `PATCH /api/v1/admin/media/{mediaId}/root-folder` with `{ "rootFolder": "/mnt/nas/tv/Breaking Bad" }` and verify `MediaDto.rootFolder` returns that value. Then call again with `{ "rootFolder": null }` and verify `MediaDto.rootFolder` falls back to the computed common-parent of linked files.

**Acceptance Scenarios**:

1. **Given** a media item with no explicit override and two linked files at `/nas/shows/Breaking Bad/S01/E01.mkv` and `/nas/shows/Breaking Bad/S01/E02.mkv`, **When** the client calls `GET /api/v1/media/{id}`, **Then** `MediaDto.rootFolder` is `/nas/shows/Breaking Bad/S01` (longest common parent path).
2. **Given** an admin sets `rootFolder` to `/nas/shows/Breaking Bad` via `PATCH /api/v1/admin/media/{mediaId}/root-folder`, **When** the client calls `GET /api/v1/media/{id}`, **Then** `MediaDto.rootFolder` returns the overridden value regardless of file paths.
3. **Given** an admin clears the override by PATCHing `rootFolder: null`, **When** the client calls the detail endpoint, **Then** `MediaDto.rootFolder` reverts to the computed common-parent value.
4. **Given** a media item with no linked files and no override, **When** the client calls the detail endpoint, **Then** `MediaDto.rootFolder` is null.
5. **Given** a non-existent `mediaId`, **When** the admin calls the PATCH endpoint, **Then** the response is 404 Not Found.
6. **Given** a non-admin authenticated user, **When** they call the PATCH endpoint, **Then** the response is 403 Forbidden.

---

### User Story 3 — Missing Episodes & Seasons Detection (Priority: P1)

An admin or user reviews a TV show detail page and wants to know which episodes are missing from their physical collection for each season. The API computes per-season completeness: the number of episodes expected (from TMDB metadata), the number owned (backed by a linked file), and the specific episode numbers that are absent.

**Why this priority**: This is the primary content-discovery feature. Without completeness data, the frontend cannot render gap indicators or missing episode lists.

**Independent Test**: For a TV show with season 1 having `EpisodeCount = 5` and linked files for episodes 1, 3, and 5, call `GET /api/v1/media/{id}/completeness` and verify the response contains one `SeasonCompletenessDto` with `seasonNumber: 1`, `ownedCount: 3`, `missingEpisodeNumbers: [2, 4]`, `isComplete: false`.

**Acceptance Scenarios**:

1. **Given** a TV show with season 1 (`EpisodeCount = 5`) and linked files covering episodes 1, 3, 5, **When** the client calls `GET /api/v1/media/{id}/completeness`, **Then** the response includes `{ seasonNumber: 1, totalExpected: 5, ownedCount: 3, missingEpisodeNumbers: [2, 4], isComplete: false }`.
2. **Given** a TV show with all episodes of season 2 linked to files, **When** the client calls the completeness endpoint, **Then** season 2 entry has `isComplete: true` and `missingEpisodeNumbers: []`.
3. **Given** a TV show with a season 0 ("Specials"), **When** the client calls the completeness endpoint, **Then** season 0 is absent from the response.
4. **Given** a TV show with a season named "Specials" (any case), **When** the client calls the completeness endpoint, **Then** that season is absent from the response.
5. **Given** a TV show where `TvSeason.EpisodeCount` is null for season 3 but the season has 4 `TvEpisode` rows, **When** completeness is computed, **Then** `totalExpected` for season 3 is 4 (falls back to row count).
6. **Given** a request for a **Film** media item, **When** the client calls `GET /api/v1/media/{id}/completeness`, **Then** the response is 400 Bad Request.
7. **Given** an authenticated non-admin user, **When** they call the completeness endpoint, **Then** the response is 200 OK (this is a standard authenticated endpoint, not admin-only).
8. **Given** a non-existent `mediaId`, **When** the completeness endpoint is called, **Then** the response is 404 Not Found.

---

### User Story 4 — Browse Unlinked Media Files (Priority: P2)

An admin wants to see all physical media files that have not yet been associated with any media item, so they can manually assign them. The API provides a paged list of unlinked files with key metadata (path, size, format, resolution).

**Why this priority**: Unlinked files cannot be discovered through the normal media detail pages. A dedicated browsing endpoint lets admins find orphaned files and link them where appropriate. It depends on the link endpoint (US1) being available.

**Independent Test**: Add a `MediaFile` with `MediaId = null`, call `GET /api/v1/admin/media/unlinked-files?page=1&pageSize=20`, and verify the file appears in the paged result. After linking it to a media item, verify the same file is absent from the next call.

**Acceptance Scenarios**:

1. **Given** multiple `MediaFile` records where some have `MediaId = null` and some have a linked `MediaId`, **When** the admin calls `GET /api/v1/admin/media/unlinked-files?page=1&pageSize=20`, **Then** only the unlinked files appear in the response.
2. **Given** 50 unlinked files and `pageSize=20`, **When** the admin requests page 1, **Then** 20 items are returned along with total count and pagination metadata.
3. **Given** no unlinked files exist, **When** the admin calls the endpoint, **Then** the response is 200 OK with an empty items array and `totalCount: 0`.
4. **Given** a non-admin authenticated user, **When** they call the unlinked-files endpoint, **Then** the response is 403 Forbidden.

---

### Edge Cases

- What happens when `TvSeason.EpisodeCount` is 0 for a season? All `[1..0]` episodes in the "expected" range is an empty set, so `missingEpisodeNumbers` is empty and `isComplete` is true. This is treated as a data quality issue in TMDB metadata, not a domain error.
- What happens if a `MediaFile` linked to a media item has an empty or null `FilePath`? The root-folder computation skips files with null or empty paths. If all paths are unusable, the computed root folder is null and the override (if set) is used.
- What happens when a TV show has episodes numbered non-sequentially (e.g., episodes 1, 2, 5 exist but 3–4 are absent from `TvEpisode` rows)? Missing episode detection uses the range `[1..totalExpected]` and computes the set difference against owned episode numbers. Episodes in the range that have no corresponding `TvEpisode` row are still reported as missing.
- What happens if a link command and an unlink command are sent concurrently for the same file? EF Core's optimistic concurrency and the uniqueness of `MediaFile.MediaId` ensure at most one write wins. The losing request returns a 409 Conflict or 422, depending on the resulting state.
- What happens when the `rootFolder` override value provided in the PATCH body is an empty string? An empty string is treated as null (cleared override), consistent with the behavior for other nullable string fields.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose `PUT /api/v1/admin/media/{mediaId}/files/{fileId}/link` to set `MediaFile.MediaId = mediaId`. Requires `AdminOnly` policy.
- **FR-002**: System MUST expose `DELETE /api/v1/admin/media/{mediaId}/files/{fileId}/link` to set `MediaFile.MediaId = null`. Requires `AdminOnly` policy.
- **FR-003**: The link command MUST return 422 Unprocessable Entity with error code `FILE_ALREADY_LINKED` if the target `MediaFile.MediaId` is non-null and points to a different `Media` item.
- **FR-004**: The link and unlink commands MUST return 404 Not Found when the specified `mediaId` or `fileId` does not exist in the database.
- **FR-005**: System MUST add a nullable `RootFolder` string property to the `Media` domain entity and create an EF Core migration (`AddMediaRootFolder`) to add the corresponding `root_folder text NULL` column to the `Media` table.
- **FR-006**: System MUST expose `PATCH /api/v1/admin/media/{mediaId}/root-folder` to set or clear the `Media.RootFolder` override. Requires `AdminOnly` policy. An empty string body value MUST be treated as null.
- **FR-007**: System MUST add a `rootFolder` field (string?, nullable) to `MediaDto`. The value is the stored `Media.RootFolder` override when non-null; otherwise it is the computed common parent directory of all linked `MediaFile.FilePath` values. When no files are linked and no override is set, the value is null.
- **FR-008**: System MUST expose `GET /api/v1/media/{id}/completeness` that returns a list of `SeasonCompletenessDto` records. Requires standard authenticated-user authorization (`[Authorize]`).
- **FR-009**: The completeness endpoint MUST return 400 Bad Request when the requested `Media` item has `MediaType = Film`.
- **FR-010**: The completeness endpoint MUST return 404 Not Found when the specified `mediaId` does not exist.
- **FR-011**: Completeness computation MUST exclude any `TvSeason` where `SeasonNumber == 0` OR whose `Name` contains the substring "specials" (case-insensitive comparison).
- **FR-012**: For each included `TvSeason`, the system MUST compute: `totalExpected` = `TvSeason.EpisodeCount` if non-null, else count of `TvEpisode` rows; `ownedCount` = count of episodes in that season that have at least one `EpisodeFileLink`; `missingEpisodeNumbers` = set difference of `[1..totalExpected]` and owned episode numbers; `isComplete` = `missingEpisodeNumbers.Count == 0`.
- **FR-013**: System MUST expose `GET /api/v1/admin/media/unlinked-files` returning a paged list of `UnlinkedFileDto` records (all `MediaFile` records with `MediaId = null`). Requires `AdminOnly` policy.
- **FR-014**: `UnlinkedFileDto` MUST include: `id` (Guid), `filePath` (string), `fileSizeBytes` (long?), `format` (string?), `resolution` (string?).
- **FR-015**: `SeasonCompletenessDto` MUST include: `seasonNumber` (int), `seasonName` (string), `totalExpected` (int), `ownedCount` (int), `missingEpisodeNumbers` (IReadOnlyList\<int\>), `isComplete` (bool).
- **FR-016**: All new endpoints MUST be implemented as MediatR queries or commands with corresponding handlers in the Application layer.
- **FR-017**: All new command/query request DTOs MUST have FluentValidation validators wired into the MediatR pipeline.
- **FR-018**: All new handlers MUST return `Result<T>` and MUST NOT throw exceptions for expected failure cases (404, 400, 422).
- **FR-019**: All new and modified endpoints MUST wrap responses in the standard `ApiResponse<T>` envelope.
- **FR-020**: The new `AdminMediaFilesController` MUST be decorated with `[Authorize(Policy = "AdminOnly")]` at the controller level.
- **FR-021**: The completeness endpoint addition to `MediaController` MUST use `[Authorize]` (not AdminOnly).

### Key Entities *(changed or new only)*

- **Media (modified entity)**: Extended with `RootFolder` (string?, nullable). Stored as `root_folder text NULL` in the database via migration `AddMediaRootFolder`.
- **MediaDto (modified DTO)**: Extended with `RootFolder` (string?, nullable). The value is the effective root folder: stored override when set, computed common-parent directory of linked files otherwise. Null when neither is available.
- **SeasonCompletenessDto (new DTO)**: `record SeasonCompletenessDto(int SeasonNumber, string SeasonName, int TotalExpected, int OwnedCount, IReadOnlyList<int> MissingEpisodeNumbers, bool IsComplete)`. Returned by the completeness endpoint.
- **UnlinkedFileDto (new DTO)**: `record UnlinkedFileDto(Guid Id, string FilePath, long? FileSizeBytes, string? Format, string? Resolution)`. Returned by the unlinked-files browsing endpoint.
- **GetMediaCompletenessQuery (new MediatR query)**: Parameters: `Guid MediaId`. Returns `Result<IReadOnlyList<SeasonCompletenessDto>>`. Handler queries `TvSeason` + `TvEpisode` + `EpisodeFileLink` for the given media item and computes per-season completeness.
- **GetUnlinkedFilesQuery (new MediatR query)**: Parameters: `int Page`, `int PageSize`. Returns `Result<PagedResult<UnlinkedFileDto>>`. Handler queries `MediaFile` where `MediaId == null`, ordered by `FilePath`.
- **LinkMediaFileCommand (new MediatR command)**: Parameters: `Guid MediaId`, `Guid FileId`. Returns `Result<Unit>`. Handler sets `MediaFile.MediaId = MediaId`; returns `FILE_ALREADY_LINKED` error if already linked to a different item.
- **UnlinkMediaFileCommand (new MediatR command)**: Parameters: `Guid MediaId`, `Guid FileId`. Returns `Result<Unit>`. Handler sets `MediaFile.MediaId = null`; returns not-found error if the file is not linked to the specified media item.
- **UpdateMediaRootFolderCommand (new MediatR command)**: Parameters: `Guid MediaId`, `string? RootFolder`. Returns `Result<Unit>`. Handler sets `Media.RootFolder` to the provided value (null if empty string).
- **AdminMediaFilesController (new controller)**: Houses all admin file-linking endpoints (`link`, `unlink`, `root-folder`, `unlinked-files`). Decorated with `[Authorize(Policy = "AdminOnly")]` at controller level.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can complete the full link-file-to-media workflow (find an unlinked file, link it, verify it appears on the media detail and disappears from the unlinked-files list) in under 30 seconds via API calls alone.
- **SC-002**: The completeness endpoint returns accurate missing-episode data for 100% of TV show requests — verified by unit tests covering all boundary cases (season 0 exclusion, specials exclusion, fallback episode count, fully-complete seasons, fully-missing seasons).
- **SC-003**: The completeness endpoint returns a 400 error for 100% of Film-type requests — zero film items are ever processed through season logic.
- **SC-004**: The `rootFolder` field in `MediaDto` reflects the correct effective value (override when set, computed common-parent otherwise) for 100% of media detail responses — verified by unit tests for both branches.
- **SC-005**: The unlinked-files endpoint returns only files with `MediaId = null` — zero linked files appear in results, verified by integration or unit tests.
- **SC-006**: All new endpoints respond within 500 ms for datasets of up to 10,000 media files and 5,000 TV episodes — no N+1 query patterns in handler implementations.
- **SC-007**: The `AddMediaRootFolder` EF Core migration applies without data loss to any existing row in the `Media` table.
- **SC-008**: All new and modified endpoints return responses within the `ApiResponse<T>` envelope — zero unhandled exceptions for expected error cases (400, 401, 403, 404, 422).
- **SC-009**: All changes are non-breaking — existing clients that do not read `rootFolder` from `MediaDto` continue to function without modification.

## Assumptions

- `MediaFile.MediaId` is already a nullable foreign key to `Media` on the existing entity. The link/unlink operations update this field directly. No new join table is required.
- The `EpisodeFileLink` relationship is the authoritative indicator that an episode is "owned". An episode is considered owned if it has at least one `EpisodeFileLink` record where the linked `MediaFile.MediaId` matches the media item being evaluated.
- `TvEpisode.EpisodeNumber` is populated from TMDB data for all episodes. The completeness logic relies on this field being accurate and non-null.
- Common-parent path derivation uses a pure string operation on `FilePath` values (split on directory separator, find the longest common prefix). No filesystem I/O is performed — the computation is deterministic given the stored path strings.
- The `PagedResult<T>` generic wrapper already exists in the Application layer, consistent with patterns used by other list endpoints (e.g., scan result listing).
- All new routes are versioned under `/api/v1/` consistent with existing controller routes.
- The `Media.RootFolder` migration uses a nullable `text` (nvarchar(max) equivalent) column. Path length is not constrained at the database level for this field.
- FluentValidation for `LinkMediaFileCommand` and `UnlinkMediaFileCommand` validates only that `MediaId` and `FileId` are non-empty GUIDs. Business-rule validation (FILE_ALREADY_LINKED, ownership check) is performed in the handler, not the validator.
- US4 (Parent-Folder Filter Label Clarification) requires no backend changes. The existing scan-result status separation between `Assigned` and `InCollection` is already correctly implemented. No work is captured in this spec for that story.
- xUnit test coverage is required for all new handler logic: `GetMediaCompletenessQueryHandlerTests`, `FileLinkCommandHandlerTests` (covering both link and unlink), `UpdateMediaRootFolderCommandHandlerTests`, and `GetUnlinkedFilesQueryHandlerTests`.


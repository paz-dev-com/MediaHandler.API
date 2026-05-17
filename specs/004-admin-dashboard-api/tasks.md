# Tasks: Admin Dashboard API Endpoints

**Input**: Design documents from `/specs/004-admin-dashboard-api/`
**Prerequisites**: spec.md (user stories), data-model.md, contracts/api-endpoints.md, quickstart.md

**Tests**: Not explicitly requested in the feature specification. Test tasks are excluded.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Domain**: `MediaHandler.Domain/`
- **Application**: `MediaHandler.Application/`
- **Infrastructure**: `MediaHandler.Infrastructure/`
- **API**: `MediaHandler.API/`
- **Unit Tests**: `MediaHandler.Tests/`
- **Integration Tests**: `MediaHandler.IntegrationTests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain entities, enums, and EF Core migration — data layer changes that all user stories depend on

- [x] T001 Add `EnrichmentStatus` enum (`Pending`, `Running`, `Completed`, `Failed`) in MediaHandler.Domain/Enums/EnrichmentStatus.cs
- [x] T002 [P] Add new fields to `ScanItemDecision` entity (`AssignedTmdbId`, `AssignedTmdbKind`, `CandidatesJson`, `ParsedTitle`, `ParsedYear`, `ParsedSeason`, `ParsedEpisode`, `ParsedMediaType`, `LibraryRootId` + `LibraryRoot` navigation) in MediaHandler.Domain/Entities/ScanItemDecision.cs
- [x] T003 [P] Add new fields to `Media` entity (`Status`, `NumberOfSeasons`, `NumberOfEpisodes`) in MediaHandler.Domain/Entities/Media.cs
- [x] T004 [P] Create `EnrichmentRun` entity with all fields (`Status`, `FailureReason`, `StartedAt`, `FinishedAt`, `TotalItems`, `EnrichedCount`, `FailedCount`, `SkippedCount`, `CurrentItem`, `ErrorDetailsJson`) inheriting `BaseEntity` in MediaHandler.Domain/Entities/EnrichmentRun.cs
- [x] T005 Update `ScanItemDecisionConfiguration` to add new column mappings, enum conversions (`HasConversion<string>()` for `AssignedTmdbKind` and `ParsedMediaType`), FK to `LibraryRoots`, `CandidatesJson` default `"[]"`, and new indexes (`IX_ScanItemDecisions_ScanRunId_Kind`, `IX_ScanItemDecisions_ScanRunId_ParsedMediaType`, `IX_ScanItemDecisions_LibraryRootId`, `IX_ScanItemDecisions_ScanRunId_ParsedTitle`) in MediaHandler.Infrastructure/Persistence/Configurations/ScanItemDecisionConfiguration.cs
- [x] T006 [P] Create `EnrichmentRunConfiguration` with column mappings, enum conversion for `Status`, indexes (`IX_EnrichmentRuns_Status`), and filtered unique index (`WHERE Status = 'Running'`) in MediaHandler.Infrastructure/Persistence/Configurations/EnrichmentRunConfiguration.cs
- [x] T007 [P] Update `MediaConfiguration` to add mappings for `Status`, `NumberOfSeasons`, `NumberOfEpisodes` columns in MediaHandler.Infrastructure/Persistence/Configurations/MediaConfiguration.cs
- [x] T008 Add `DbSet<EnrichmentRun> EnrichmentRuns` to `IApplicationDbContext` in MediaHandler.Application/Common/Interfaces/IApplicationDbContext.cs and the concrete `ApplicationDbContext`
- [x] T009 Generate EF Core migration `AddDashboardApiFields` via `dotnet ef migrations add AddDashboardApiFields --project MediaHandler.Infrastructure --startup-project MediaHandler.API`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared DTOs, transient models, request/response contracts, and services that multiple user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T010 Create `ScanItemDecisionDto` record (all fields from API contract: `id`, `scanRunId`, `filePath`, `kind`, `reason`, `assignedTmdbId`, `assignedTmdbKind`, `assignedTitle`, `assignedYear`, `assignedPosterPath`, `candidatesJson`, `parsedTitle`, `parsedYear`, `parsedSeason`, `parsedEpisode`, `parsedMediaType`, `libraryRootId`, `libraryRootPath` [= `LibraryRoot.Path`], `mediaFileId`) in MediaHandler.Application/Features/Dashboard/DTOs/ScanItemDecisionDto.cs
- [x] T011 [P] Create `TvShowGroupDto` record (`GroupId`, `ParsedShowName`, `EpisodeCount`, `AssignedTmdbId`, `AssignedTmdbKind`, `AssignedTitle`, `AssignedYear`, `AssignedPosterPath`); `AssignTvGroupResponse` uses the **same shape** — field `EpisodeCount` (NOT `episodesUpdated`) plus all TMDB assignment fields (`AssignedTmdbKind`, `AssignedYear`, `AssignedPosterPath`) matching the richer frontend contract in MediaHandler.Application/Features/Dashboard/DTOs/TvShowGroupDto.cs
- [x] T012 [P] Create `EnrichmentRunDto` record (all status/progress fields from API contract) in MediaHandler.Application/Features/Dashboard/DTOs/EnrichmentRunDto.cs
- [x] T013 [P] Create `FileRenameResultDto` record (`MediaFileId`, `CurrentFileName`, `ProposedFileName`, `CurrentPath`, `ProposedPath`, `Executed`) in MediaHandler.Application/Features/Dashboard/DTOs/FileRenameResultDto.cs
- [x] T014 [P] Create `TvShowGroup` transient domain model with deterministic `GroupId` computation (`DeterministicGuid(SHA256(scanId + "|" + parsedShowName.ToLowerInvariant()))`) and `DecisionIds` list in MediaHandler.Application/Common/Models/TvShowGroup.cs
- ~~T015~~ — *Intentionally omitted* (merged into T016/T016b — request/response contracts covered as a pair)
- [x] T016 [P] Create request contracts (`ReassignTmdbRequest`, `AssignTvGroupRequest`) in MediaHandler.API/Contracts/Admin/DashboardRequests.cs
- [x] T016b [P] Create response contracts (`ReassignTmdbResponse`, `AssignTvGroupResponse` [must include `GroupId`, `ParsedShowName`, `EpisodeCount`, `AssignedTmdbId`, `AssignedTmdbKind`, `AssignedTitle`, `AssignedYear`, `AssignedPosterPath`], `StartEnrichmentResponse`, `BatchRenameResponse`) in MediaHandler.API/Contracts/Admin/DashboardResponses.cs
- [x] T017 [P] Create `IFileRenameService` interface with `PreviewRenameAsync` and `ExecuteRenameAsync` methods in MediaHandler.Application/Common/Interfaces/IFileRenameService.cs
- [x] T018 [P] Create `IEnrichmentCoordinator` interface with `StartAsync` and `GetStatusAsync` methods in MediaHandler.Application/Common/Interfaces/IEnrichmentCoordinator.cs
- [x] T019 Add stale enrichment run cleanup to `DatabaseInitializer` — transition any `EnrichmentRun` rows with `Status = Running` to `Failed` with `FailureReason = "Process restarted unexpectedly"` on startup in MediaHandler.API/Extensions/DatabaseInitializer.cs

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 8 — ScanItemDecision Entity Enhancement (Priority: P1)  MVP

**Goal**: Ensure the scan pipeline populates the new `ScanItemDecision` fields so downstream features have data to work with

**Independent Test**: Run a scan and verify `ScanItemDecision` records contain `AssignedTmdbId`, `CandidatesJson`, parsed metadata, `ParsedMediaType`, and `LibraryRootId` populated with correct values

### Implementation for User Story 8

- [x] T020 [US8] Update `ScanPipeline` to populate new `ScanItemDecision` fields (`AssignedTmdbId`, `AssignedTmdbKind`, `CandidatesJson`, `ParsedTitle`, `ParsedYear`, `ParsedSeason`, `ParsedEpisode`, `ParsedMediaType`, `LibraryRootId`) when creating decision records in MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs
- [x] T021 [US8] Update `TmdbMatcher` to pass candidate results (as JSON array matching `ReviewItem.CandidatesJson` schema) to the decision record in MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs

**Checkpoint**: Scan pipeline now populates all new fields. Existing scans can be re-run to populate data for US1.

---

## Phase 4: User Story 1 — Scan Results Browser Endpoint (Priority: P1)

**Goal**: Paginated endpoint to browse all `ScanItemDecision` records for a scan run with filters

**Independent Test**: Run a scan, call `GET /api/v1/admin/scan/{scanId}/decisions` with various filter combinations, verify correct paginated results with all required fields

### Implementation for User Story 1

- [x] T022 [US1] Create `ListScanDecisionsQuery` record (ScanRunId, DecisionType?, MediaType?, LibraryRootId?, Page, PageSize) with `ListScanDecisionsQueryValidator` (FluentValidation: `page ≥ 1`, `pageSize` in `[1, 100]`) and `ListScanDecisionsQueryHandler` (paginated `AsNoTracking()` query with filters, joins `ScanItemDecision → MediaFile → Media` and `→ LibraryRoot` to populate `assignedTitle`, `assignedYear`, `assignedPosterPath`, and `libraryRootPath` = `LibraryRoot.Path`) in MediaHandler.Application/Features/Dashboard/Queries/ListScanDecisions/ListScanDecisionsQuery.cs
- [x] T023 [US1] Add `GET /api/v1/admin/scan/{scanId}/decisions` endpoint to `AdminScanController` (already has `[Authorize(Policy = "AdminOnly")]`), mapping query params to `ListScanDecisionsQuery`, returning `ApiResponse<PagedResult<ScanItemDecisionDto>>` in MediaHandler.API/Controllers/AdminScanController.cs

**Checkpoint**: Admins can browse and filter scan decisions for any completed scan run.

---

## Phase 5: User Story 2 — TMDB Reassignment Endpoint (Priority: P1)

**Goal**: Endpoint to change the TMDB assignment for a scan item decision and update the linked media file

**Independent Test**: Call `PUT /api/v1/admin/scan-decisions/{id}/reassign` with a valid TMDB ID and media type, verify the decision and linked media file are updated

### Implementation for User Story 2

- [x] T024 [US2] Create `ReassignTmdbCommand` record (DecisionId, TmdbId, MediaType) with `ReassignTmdbCommandValidator` and `ReassignTmdbCommandHandler` (loads decision, verifies TMDB ID via `ITmdbService`, updates `AssignedTmdbId`/`AssignedTmdbKind`, updates linked `MediaFile.MediaId`, saves) in MediaHandler.Application/Features/Dashboard/Commands/ReassignTmdb/ReassignTmdbCommand.cs
- [x] T025 [US2] Create `AdminScanDecisionsController` (`[Route("api/v1/admin/scan-decisions")]`, `[Authorize(Policy = "AdminOnly")]`) with `PUT /{id}/reassign` endpoint accepting `ReassignTmdbRequest` body, returning `ApiResponse<ReassignTmdbResponse>` in MediaHandler.API/Controllers/AdminScanDecisionsController.cs

**Checkpoint**: Admins can correct wrong TMDB matches on individual scan decisions.

---

## Phase 6: User Story 3 — TV Show Groups Endpoint (Priority: P2)

**Goal**: Endpoint that computes TV show episode groupings on-the-fly from `ScanItemDecision` rows

**Independent Test**: Run a scan on a TV library, call `GET /api/v1/admin/scan-decisions/tv-groups?scanId={scanId}`, verify episodes are grouped by parsed show name with correct counts

### Implementation for User Story 3

- [x] T026 [US3] Create `ListTvShowGroupsQuery` record (ScanId) with `ListTvShowGroupsQueryValidator` and `ListTvShowGroupsQueryHandler` (groups `ScanItemDecision` rows by `ParsedTitle` where `ParsedMediaType = TvShow` via EF Core `GroupBy`; note: `ParsedTitle` stores the **show name** for TV decisions, not episode title — episode title is in `TvEpisode.Name`; computes deterministic `GroupId = SHA256(scanId|parsedTitle.ToLowerInvariant())`; resolves TMDB assignment from `Media` table) in MediaHandler.Application/Features/Dashboard/Queries/ListTvShowGroups/ListTvShowGroupsQuery.cs
- [x] T027 [US3] Add `GET /api/v1/admin/scan-decisions/tv-groups` endpoint to `AdminScanDecisionsController`, accepting `scanId` query param, returning `ApiResponse<List<TvShowGroupDto>>` in MediaHandler.API/Controllers/AdminScanDecisionsController.cs

**Checkpoint**: Admins can view TV show groupings for any scan run.

---

## Phase 7: User Story 4 — TV Show Group Assignment Endpoint (Priority: P2)

**Goal**: Assign a TMDB TV show entry at the group level, propagating to all episode decisions and their linked media files

**Independent Test**: Call `PUT /api/v1/admin/tv-groups/{groupId}/assign?scanId={scanId}` with a TMDB TV show ID, verify all episode decisions are updated and media files linked

### Implementation for User Story 4

- [x] T028 [US4] Create `AssignTvGroupCommand` record (GroupId, ScanId, TmdbId) with `AssignTvGroupCommandValidator` and `AssignTvGroupCommandHandler` (resolves group members by recomputing deterministic `GroupId` for each `ParsedTitle` group, verifies TMDB ID via `ITmdbService`, bulk-updates `AssignedTmdbId`/`AssignedTmdbKind` on all member decisions, updates linked `MediaFile.MediaId` for each) in MediaHandler.Application/Features/Dashboard/Commands/AssignTvGroup/AssignTvGroupCommand.cs
- [x] T029 [US4] Add `PUT /api/v1/admin/tv-groups/{groupId}/assign` endpoint to `AdminScanDecisionsController`, accepting `scanId` query param and `AssignTvGroupRequest` body, returning `ApiResponse<AssignTvGroupResponse>` (includes `GroupId`, `ParsedShowName`, `EpisodeCount`, `AssignedTmdbId`, `AssignedTmdbKind`, `AssignedTitle`, `AssignedYear`, `AssignedPosterPath`) in MediaHandler.API/Controllers/AdminScanDecisionsController.cs

**Checkpoint**: Admins can assign TMDB sources at the show level, propagating to all episodes.

---

## Phase 8: User Story 5 — Batch TMDB Enrichment Endpoints (Priority: P2)

**Goal**: Background enrichment process that fetches full TMDB metadata for validated media entries, with start and status polling endpoints

**Independent Test**: Assign TMDB sources to media entries, call `POST /api/v1/admin/enrichment/start`, poll `GET /api/v1/admin/enrichment/status` until completion, verify media entries have full TMDB metadata

### Implementation for User Story 5

- [x] T030 [US5] Create `StartEnrichmentCommand` record with `StartEnrichmentCommandHandler`: (1) check for active `Running` row in `EnrichmentRuns` → return conflict if exists; (2) count eligible media entries (new or assignment-changed since last enrichment); **(3) if count = 0 → return 200 OK with `totalItems: 0`, do NOT insert a row or return 202**; (4) insert `EnrichmentRun` row with `Status=Pending`; (5) trigger `IEnrichmentCoordinator.StartAsync(runId)` in MediaHandler.Application/Features/Dashboard/Commands/StartEnrichment/StartEnrichmentCommand.cs
- [x] T031 [US5] Create `GetEnrichmentStatusQuery` record with `GetEnrichmentStatusQueryHandler` (returns latest `EnrichmentRun` row mapped to `EnrichmentRunDto`, or null if none) in MediaHandler.Application/Features/Dashboard/Queries/GetEnrichmentStatus/GetEnrichmentStatusQuery.cs
- [x] T032a [US5] Scaffold `EnrichmentCoordinator` class: singleton service implementing `IEnrichmentCoordinator`, with `Task.Run` background execution via `IServiceScopeFactory`, state transitions (`Pending → Running → Completed|Failed`), and stale-run guard. Register in DI in MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs
- [x] T032b [US5] Implement incremental entry selection in `EnrichmentCoordinator`: query `Media` rows where assignment exists AND (`Overview IS NULL` OR `UpdatedAt > LastEnrichmentFinishedAt`). Skip already-enriched unchanged entries. in MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs
- [x] T032c [US5] Implement `Media` field population in `EnrichmentCoordinator`: call `ITmdbService.GetMediaDetailsAsync`, map response to `Title`, `OriginalTitle`, `Overview`, `ReleaseDate`, `Runtime`, `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language` [NOT `OriginalLanguage`], `Status`, `NumberOfSeasons` (TV), `NumberOfEpisodes` (TV), upsert `MediaGenre` child records in MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs
- [x] T032d [US5] Implement `TvSeason`/`TvEpisode` upsert in `EnrichmentCoordinator` for TV shows: call `ITmdbService.GetTvShowSeasonsAsync`, upsert `TvSeason` + `TvEpisode` records (match on `SeasonNumber`/`EpisodeNumber`). `TvEpisode.Name` is required for TV file rename in MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs
- [x] T032e [US5] Implement per-entry error tracking and progress reporting in `EnrichmentCoordinator`: handle exceptions per-entry (do not abort batch), append to `ErrorDetailsJson`, update `EnrichedCount`/`FailedCount`/`CurrentItem` every 10 entries or 5 seconds in MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs
- [x] T033 [US5] Create `AdminEnrichmentController` (`[Route("api/v1/admin/enrichment")]`, `[Authorize(Policy = "AdminOnly")]`) with `POST /start` (returns 202 Accepted or 200 OK when 0 entries) and `GET /status` endpoints in MediaHandler.API/Controllers/AdminEnrichmentController.cs

**Checkpoint**: Admins can trigger batch enrichment and monitor progress. Media entries get full TMDB metadata.

---

## Phase 9: User Story 6 — File Rename Endpoint (Priority: P3)

**Goal**: Rename a single media file on the NAS to match TMDB naming conventions, with preview support

**Independent Test**: Call `POST /api/v1/admin/files/{id}/rename?preview=true` to verify proposed name, then call without preview and confirm filesystem + database updated

### Implementation for User Story 6

- [x] T034 [US6] Implement `FileRenameService` (implements `IFileRenameService`) — generates names per convention ("Movie Title (Year)" for films, "Show Name - SXXEXX - Episode Title" for TV); performs case-insensitive conflict check (`StringComparison.OrdinalIgnoreCase` against `Directory.GetFiles()`); executes atomic `File.Move`; updates `MediaFile.FilePath` in DB; compensates (moves file back) if DB save fails in MediaHandler.Infrastructure/Services/FileRenameService.cs
- [x] T035 [US6] Create `RenameFileCommand` record (MediaFileId, Preview) with `RenameFileCommandValidator` and `RenameFileCommandHandler`: loads `MediaFile` + associated `Media`; **for TV episodes, also loads `TvEpisode` matching `ScanItemDecision.ParsedSeason` + `ParsedSeason.ParsedEpisode` to get episode title (`TvEpisode.Name`) — return 422 validation error "Episode title not available — run TMDB enrichment first" if no `TvEpisode` record found**; validates TMDB assignment exists; delegates to `IFileRenameService`; returns `FileRenameResultDto` in MediaHandler.Application/Features/Dashboard/Commands/RenameFile/RenameFileCommand.cs
- [x] T036 [US6] Create `AdminFilesController` (`[Route("api/v1/admin/files")]`, `[Authorize(Policy = "AdminOnly")]`) with `POST /{id}/rename` endpoint accepting `preview` query param (default `true`), returning `ApiResponse<FileRenameResultDto>` in MediaHandler.API/Controllers/AdminFilesController.cs

**Checkpoint**: Admins can preview and execute single-file renames with TMDB naming conventions.

---

## Phase 10: User Story 7 — TV Show Group Batch Rename Endpoint (Priority: P3)

**Goal**: Batch rename all episode files in a TV show group with preview and atomic validation

**Independent Test**: Call `POST /api/v1/admin/tv-groups/{groupId}/rename?scanId={scanId}&preview=true` to see all proposed names, then execute and verify all files renamed

### Implementation for User Story 7

- [x] T037 [US7] Create `BatchRenameTvGroupCommand` record (GroupId, ScanId, Preview) with `BatchRenameTvGroupCommandValidator` and `BatchRenameTvGroupCommandHandler` (resolves group members, validates TMDB assignment on group, loads `TvEpisode` records for all episodes in group — return 422 if any missing, validates ALL rename targets before executing ANY — rejects entire batch on conflict, delegates per-file rename to `IFileRenameService`, returns list of `FileRenameResultDto`) in MediaHandler.Application/Features/Dashboard/Commands/BatchRenameTvGroup/BatchRenameTvGroupCommand.cs
- [x] T038 [US7] Add `POST /api/v1/admin/tv-groups/{groupId}/rename` endpoint to **`AdminScanDecisionsController`** (consistent with T029 TV group assign, per research.md R7 decision), accepting `scanId` and `preview` query params, returning `ApiResponse<BatchRenameResponse>`. Use `[HttpPost("~/api/v1/admin/tv-groups/{groupId}/rename")]` route override since the controller base route is `/api/v1/admin/scan-decisions` in MediaHandler.API/Controllers/AdminScanDecisionsController.cs

**Checkpoint**: Admins can preview and execute batch renames for entire TV shows.

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, cleanup, and cross-cutting improvements

- [x] T039 [P] Register `FileRenameService` and `EnrichmentCoordinator` in DI container in MediaHandler.Infrastructure/DependencyInjection.cs
- [x] T040 [P] ~~Auth verification~~ — **Superseded**: `[Authorize(Policy = "AdminOnly")]` is already applied at controller level during T023 (AdminScanController), T025 (AdminScanDecisionsController), T033 (AdminEnrichmentController), T036 (AdminFilesController). Verify coverage in T044 quickstart checklist.
- [x] T041 [P] Verify all responses use `ApiResponse<T>` envelope and Result pattern — no unhandled exceptions for expected error cases
- [x] T042 Run `dotnet build MediaHandler.slnx` and fix any compilation errors across all projects
- [x] T043 Run `dotnet format --verify-no-changes` and fix any formatting violations
- [x] T044 Run quickstart.md verification checklist — apply migration, start API, verify all 8 endpoint contracts respond correctly

---

---

## Phase 12: User Story 13 — Bulk Review Item Resolution by Parent Folder (Priority: P1)

**Goal**: Resolve all open review items sharing the same parent folder path in a single operation

**Independent Test**: Call `POST /api/v1/admin/review-items/bulk-resolve` with `{ parentFolderPath, action, tmdbId, kind }`, verify all Open ReviewItems under that folder path are resolved and `resolvedCount` is returned

### Implementation for User Story 13

- [x] T045 [US13] Create `BulkResolveReviewItemsCommand` record (`ParentFolderPath`, `Action`, `TmdbId?`, `Kind?`) with `BulkResolveReviewItemsCommandValidator` and `BulkResolveReviewItemsCommandHandler` (loads all Open `ReviewItem` rows where `FilePath` starts with `parentFolderPath`, applies same resolution action to each — mirrors single-item logic (Assign verifies TMDB via `ITmdbService`, Dismiss/Delete supported), returns `resolvedCount`) in MediaHandler.Application/Features/Review/Commands/BulkResolveReviewItems/BulkResolveReviewItemsCommand.cs
- [x] T046 [US13] Add `POST /api/v1/admin/review-items/bulk-resolve` endpoint to `AdminReviewController`, accepting `BulkResolveReviewRequest` body (`ParentFolderPath`, `Action`, `TmdbId?`, `Kind?`), returning `ApiResponse<BulkResolveResult>` (`ResolvedCount`) in MediaHandler.API/Controllers/AdminReviewController.cs

**Checkpoint**: Admins can resolve all sibling review items in a folder in one call.

---

## Phase 13: User Story 14 — Parent Folder TMDB Validation Endpoints (Priority: P2)

**Goal**: Provide a paginated view of unique NAS parent folders aggregated from `MediaFile.FilePath`, with per-folder TMDB assignment status and an assign endpoint

**Independent Test**: Call `GET /api/v1/admin/parent-folders`, verify folders are grouped by parent directory with correct `status` (`NotAssigned`/`Assigned`/`InCollection`), `episodeCount`, and `detectedShowName`. Call `PUT /api/v1/admin/parent-folders/{folderId}/assign` with a TMDB TV show ID, verify all `ScanItemDecision` records linked to media files in that folder get updated.

### Implementation for User Story 14

- [x] T047 [US14] [P] Create `ParentFolderGroupDto` record (`Id`, `FolderPath`, `DetectedShowName`, `EpisodeCount`, `Status` [enum string: `NotAssigned`/`Assigned`/`InCollection`], `TmdbId?`, `TmdbTitle?`) in MediaHandler.Application/Features/Dashboard/DTOs/ParentFolderGroupDto.cs
- [x] T048 [US14] Create `ListParentFoldersQuery` record (`Status?`, `Page`, `PageSize`) with `ListParentFoldersQueryValidator` and `ListParentFoldersQueryHandler`: groups `MediaFile.FilePath` by parent directory (using `Path.GetDirectoryName`-equivalent string slicing on SQL), computes deterministic `Id = SHA256(folderPath.ToLowerInvariant())`, derives `DetectedShowName` from last path segment, counts `EpisodeCount`, determines `Status` from TMDB coverage (`InCollection` if any linked `Media.TmdbId` is non-null AND overview is populated; `Assigned` if `ScanItemDecision.AssignedTmdbId` is set; otherwise `NotAssigned`), returns paginated `ParentFolderGroupDto` list in MediaHandler.Application/Features/Dashboard/Queries/ListParentFolders/ListParentFoldersQuery.cs
- [x] T049 [US14] Create `AssignParentFolderCommand` record (`FolderId`, `FolderPath`, `TmdbId`, `Kind`) with `AssignParentFolderCommandValidator` and `AssignParentFolderCommandHandler`: verifies TMDB via `ITmdbService`, finds all `MediaFile` rows whose `FilePath` starts with `FolderPath`, bulk-updates linked `ScanItemDecision.AssignedTmdbId`/`AssignedTmdbKind`, upserts `Media` row via `MediaImportService`, updates `MediaFile.MediaId`, returns updated `ParentFolderGroupDto` in MediaHandler.Application/Features/Dashboard/Commands/AssignParentFolder/AssignParentFolderCommand.cs
- [x] T050 [US14] Create `AdminParentFoldersController` (`[Route("api/v1/admin/parent-folders")]`, `[Authorize(Policy = "AdminOnly")]`) with `GET /` (query params: `status?`, `page`, `pageSize`, returning `ApiResponse<PagedResult<ParentFolderGroupDto>>`) and `PUT /{folderId}/assign` (body: `AssignParentFolderRequest` with `FolderPath`, `TmdbId`, `Kind`, returning `ApiResponse<ParentFolderGroupDto>`) in MediaHandler.API/Controllers/AdminParentFoldersController.cs

**Checkpoint**: Admins can view all TV show parent folders, filter by assignment status, and bulk-assign TMDB entries at the folder level.

---

## Phase 14: Frontend Support — Grouped Scan Decisions Endpoint (Priority: P2)

**Goal**: Server-side TV show episode deduplication and grouping for the scan results browser. Eliminates need for client-side deduplication.

### Implementation

- [x] T051 [P] Create `ScanDecisionShowGroupDto` record (`ShowName`, `EpisodeCount`, `AssignedTmdbId`, `AssignedKind`, `AssignedTitle`, `AssignedYear`, `AssignedPosterPath`, `Episodes` list of `ScanItemDecisionDto`) in MediaHandler.Application/Features/Dashboard/DTOs/ScanDecisionShowGroupDto.cs
- [x] T052 Create `ListGroupedScanDecisionsQuery` with handler — deduplicates by file path, groups TV episodes by normalized `ParsedTitle` (strips language suffixes), movies stay as single-item groups, majority TMDB assignment per group — in MediaHandler.Application/Features/Dashboard/Queries/ListGroupedScanDecisions/ListGroupedScanDecisionsQuery.cs
- [x] T053 Add `GET /api/v1/admin/scan/{scanId}/decisions/grouped` endpoint to `AdminScanController`, accepting same filters as flat decisions endpoint, returning `ApiResponse<List<ScanDecisionShowGroupDto>>` in MediaHandler.API/Controllers/AdminScanController.cs

**Checkpoint**: Frontend can call grouped endpoint instead of doing client-side deduplication.

---

## Phase 15: Frontend Support — Enrichment History Endpoint (Priority: P2)

**Goal**: Paginated history of past enrichment runs for the enrichment page

### Implementation

- [x] T054 Create `ListEnrichmentHistoryQuery` with handler — paginated, ordered by `StartedAt` descending, deserializes `ErrorDetailsJson` into typed `EnrichmentErrorDetailDto` list — in MediaHandler.Application/Features/Dashboard/Queries/ListEnrichmentHistory/ListEnrichmentHistoryQuery.cs
- [x] T055 Add `GET /api/v1/admin/enrichment/history` endpoint to `AdminEnrichmentController`, accepting `page` and `pageSize` query params, returning `ApiResponse<IReadOnlyList<EnrichmentRunDto>>` with pagination meta in MediaHandler.API/Controllers/AdminEnrichmentController.cs

**Checkpoint**: Frontend can display paginated enrichment run history with error details.

---

## Phase 16: Issue Fix — Reassign TMDB for Already-Enriched Media (Priority: P1)

**Goal**: Fix the reassign endpoint to correctly handle media entries that have already been enriched with full TMDB metadata. Currently reassigning an already-enriched media to a new TMDB ID may fail or leave stale data.

**Independent Test**: Assign a media file to TMDB ID X, run enrichment (populating overview, genres, seasons, etc.), then call `PUT /api/v1/admin/scan-decisions/{id}/reassign` with TMDB ID Y — verify the decision's `AssignedTmdbId` is updated, the linked `MediaFile.MediaId` points to the new `Media` row (created or existing), and the old enrichment data is cleared or a re-enrichment is flagged.

**Depends on**: Phase 5 (US2 - Reassignment Endpoint)

### Implementation for Reassign Fix

- [x] T056 [P] Investigate `ReassignTmdbCommandHandler` in MediaHandler.Application/Features/Dashboard/Commands/ReassignTmdb/ReassignTmdbCommand.cs — verify it handles the case where `MediaFile.MediaId` already points to an enriched `Media` entity. Check if: (a) the handler creates a new `Media` row or reuses an existing one for the new TMDB ID, (b) old `Media` row orphans are cleaned up if no other files reference it, (c) `Media.Overview` and related enrichment fields are cleared when TMDB ID changes to flag re-enrichment
- [x] T057 Fix `ReassignTmdbCommandHandler` to correctly handle already-enriched media: (1) look up existing `Media` row for the new `tmdbId` — reuse if found, create new if not, (2) update `MediaFile.MediaId` to the new/existing `Media` row, (3) if the old `Media` row has no remaining `MediaFile` references, optionally mark it for cleanup, (4) update `ScanItemDecision` fields (`AssignedTmdbId`, `AssignedTmdbKind`, assigned title/year/poster from TMDB lookup) — modify MediaHandler.Application/Features/Dashboard/Commands/ReassignTmdb/ReassignTmdbCommand.cs
- [x] T058 [P] Fix `AssignTvGroupCommandHandler` to apply the same enriched-media handling as T057 — when re-assigning a TV show group to a different TMDB ID, ensure all member `MediaFile` rows are updated and old orphan `Media` rows are handled — modify MediaHandler.Application/Features/Dashboard/Commands/AssignTvGroup/AssignTvGroupCommand.cs

**Checkpoint**: Reassigning TMDB for already-enriched media works correctly; no orphan or stale data

---

## Phase 17: Issue Fix — Enrichment Per-Media Details Endpoint (Priority: P2)

**Goal**: Provide a detailed breakdown of each enrichment run showing which media entries were enriched/failed/skipped, with their file names and counts

**Independent Test**: Run an enrichment, call `GET /api/v1/admin/enrichment/{runId}/details` — verify response contains a list of per-media entries with `mediaId`, `tmdbId`, `title`, `type`, `status` (Enriched/Failed/Skipped), `fileCount`, `fileNames`, and `error` (for failed entries)

**Depends on**: Phase 8 (US5 — Enrichment), Phase 15 (Enrichment History)

### Implementation for Enrichment Per-Media Details

- [x] T059 [P] Create `EnrichmentMediaDetailDto` record (`MediaId`, `TmdbId`, `Title`, `Type` [Film/TvShow], `Status` [Enriched/Failed/Skipped], `FileCount`, `FileNames` [list of string], `Error` [nullable]) in MediaHandler.Application/Features/Dashboard/DTOs/EnrichmentMediaDetailDto.cs
- [x] T060 [P] Add `EnrichedMediaIdsJson` column to `EnrichmentRun` entity to track which media IDs were processed and their status during enrichment — modify MediaHandler.Domain/Entities/EnrichmentRun.cs. Add column mapping in MediaHandler.Infrastructure/Persistence/Configurations/EnrichmentRunConfiguration.cs. Generate migration.
- [x] T061 Update `EnrichmentCoordinator` to record per-media processing results in `EnrichedMediaIdsJson` — for each media entry processed, append `{ mediaId, status }` to the JSON field. This enables the details endpoint to reconstruct what happened per media — modify MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs
- [x] T062 Create `GetEnrichmentRunDetailsQuery` record (`RunId`) with handler: (1) load `EnrichmentRun` by ID, (2) parse `EnrichedMediaIdsJson` to get list of processed media IDs and statuses, (3) for each media ID, join to `Media` table (get title, tmdbId, type) and `MediaFile` table (get file count and file names), (4) merge with `ErrorDetailsJson` for failed entries, (5) return `List<EnrichmentMediaDetailDto>` — in MediaHandler.Application/Features/Dashboard/Queries/GetEnrichmentRunDetails/GetEnrichmentRunDetailsQuery.cs
- [x] T063 Add `GET /api/v1/admin/enrichment/{runId}/details` endpoint to `AdminEnrichmentController`, returning `ApiResponse<List<EnrichmentMediaDetailDto>>` — modify MediaHandler.API/Controllers/AdminEnrichmentController.cs

**Checkpoint**: Frontend can fetch detailed per-media breakdown for any enrichment run

---

## Dependencies & Execution Order (Issue Fixes — Phases 16–17)

### Phase Dependencies

- **Phase 16 (Reassign Fix)**: Depends on Phase 5 (US2 — Reassignment endpoint must exist). Independent of Phase 17.
- **Phase 17 (Enrichment Details)**: Depends on Phase 8 (US5 — Enrichment must exist) and Phase 15 (History endpoint). Independent of Phase 16.

### Parallel Opportunities

- **Phases 16 and 17 can run fully in parallel** — they touch different subsystems
- T056, T058 (Phase 16 investigation + TV group fix) — parallel after T057
- T059, T060 (Phase 17 DTO + entity change) — parallel

````

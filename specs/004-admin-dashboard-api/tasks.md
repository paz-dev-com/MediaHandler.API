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

## Phase 3: User Story 8 — ScanItemDecision Entity Enhancement (Priority: P1) 🎯 MVP

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

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US8 (Phase 3)**: Depends on Foundational — must complete before US1 (provides data for scan decisions browser)
- **US1 (Phase 4)**: Depends on US8 (needs populated `ScanItemDecision` fields)
- **US2 (Phase 5)**: Depends on Foundational — can run in parallel with US1
- **US3 (Phase 6)**: Depends on Foundational — can run in parallel with US1/US2
- **US4 (Phase 7)**: Depends on US3 (needs TV group computation logic)
- **US5 (Phase 8)**: Depends on Foundational — can run in parallel with US1-US4
- **US6 (Phase 9)**: Depends on Foundational — can run in parallel with US1-US5
- **US7 (Phase 10)**: Depends on US3 (TV group resolution) and US6 (single-file rename service)
- **Polish (Phase 11)**: Depends on all user stories being complete

### User Story Dependencies

- **US8 (P1)**: Foundational only — MUST complete first (provides data for all other stories)
- **US1 (P1)**: Depends on US8 — scan decisions must have new fields populated
- **US2 (P1)**: Foundational only — independently testable
- **US3 (P2)**: Foundational only — independently testable
- **US4 (P2)**: Depends on US3 — needs TV group computation
- **US5 (P2)**: Foundational only — independently testable
- **US6 (P3)**: Foundational only — independently testable
- **US7 (P3)**: Depends on US3 + US6 — needs group resolution and rename service

### Within Each User Story

- Models/DTOs before services
- Services before handlers
- Handlers before controller endpoints
- Core implementation before integration

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel (T002, T003, T004 — entity changes in different files; T005, T006, T007 — configurations in different files)
- All Foundational DTOs marked [P] can run in parallel (T010–T018)
- After Foundational: US2, US3, US5, US6 can all start in parallel (no inter-story dependencies)
- Within US5: T030 and T031 can be developed in parallel (command vs query)

---

## Parallel Example: After Foundational Phase

```
# These user stories can be worked on simultaneously by different developers:
Developer A: US8 → US1 (scan pipeline + browser — critical path)
Developer B: US2 + US5 (reassignment + enrichment — independent)
Developer C: US3 → US4 (TV groups + group assignment — dependent pair)
Developer D: US6 → US7 (single rename + batch rename — dependent pair)
```

## Parallel Example: Setup Phase

```
# Launch all entity changes together:
Task T002: Add fields to ScanItemDecision entity
Task T003: Add fields to Media entity
Task T004: Create EnrichmentRun entity

# Launch all EF configurations together (after entities):
Task T005: Update ScanItemDecisionConfiguration
Task T006: Create EnrichmentRunConfiguration
Task T007: Update MediaConfiguration
```

---

## Implementation Strategy

### MVP First (US8 + US1 + US2)

1. Complete Phase 1: Setup (entity + migration changes)
2. Complete Phase 2: Foundational (DTOs, interfaces, startup cleanup)
3. Complete Phase 3: US8 (scan pipeline populates new fields)
4. Complete Phase 4: US1 (scan results browser)
5. Complete Phase 5: US2 (TMDB reassignment)
6. **STOP and VALIDATE**: Test scan browser + reassignment end-to-end

### Incremental Delivery

1. Setup + Foundational → Data layer ready
2. US8 → Scan pipeline enhanced → Re-scan to populate data
3. US1 → Admins can browse scan decisions → Deploy/Demo (MVP!)
4. US2 → Admins can fix wrong matches → Deploy/Demo
5. US3 + US4 → TV show grouping + assignment → Deploy/Demo
6. US5 → Batch enrichment → Deploy/Demo
7. US6 + US7 → File renaming → Deploy/Demo
8. Polish → Final validation → Release

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US8 → US1 (critical path — data pipeline)
   - Developer B: US2 (reassignment — independent)
   - Developer C: US3 → US4 (TV groups — dependent pair)
3. After US8 + US1 validated:
   - Developer A: US5 (enrichment)
   - Developer D: US6 → US7 (rename — dependent pair)
4. Stories integrate independently via separate controllers (`AdminScanController`, `AdminScanDecisionsController`, `AdminEnrichmentController`, `AdminFilesController`)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- New endpoints are spread across 3 new controllers + 1 modified (`AdminScanController`, `AdminScanDecisionsController`, `AdminEnrichmentController`, `AdminFilesController`) following SRP
- Follow existing patterns: `ListScanHistoryQuery` for paginated queries, `ResolveReviewItemCommand` for commands, `ScanRunCoordinator` for background services
- `TvShowGroup` is transient (computed at query time) — NO database table
- `EnrichmentRun` IS persisted — dedicated table with concurrency lock
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently


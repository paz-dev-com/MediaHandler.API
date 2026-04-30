---
description: "Task list for Kodi-Style NAS Library Scanner"
---

# Tasks: Kodi-Style NAS Library Scanner

**Input**: Design documents from `/specs/001-kodi-style-scanner/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: REQUIRED — Constitution principle II (Testing Standards) + spec SC-001..SC-008 + plan §Constitution Check (II) mandate unit + integration coverage. All test tasks are first-class, must be written first per story, and must FAIL before the matching implementation lands.
**Organization**: Tasks are grouped by user story so each can be implemented, tested and demoed independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelizable — touches a different file from its siblings and has no incomplete dependency.
- **[Story]**: User-story tag (`US1`..`US4`); Setup / Foundational / Polish carry no story tag.
- All paths are repo-relative (repo root = `MediaHandler.API/`).

## Path Conventions

- Domain: `MediaHandler.Domain/`
- Application: `MediaHandler.Application/`
- Infrastructure: `MediaHandler.Infrastructure/`
- API: `MediaHandler.API/`
- Unit tests: `MediaHandler.Tests/`
- Integration tests: `MediaHandler.IntegrationTests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Folder skeleton, in-tree licensing guardrails, and test harness wiring used by every later phase.

- [X] T001 Create directory skeleton: `MediaHandler.Infrastructure/Nas/Scanner/`, `MediaHandler.Application/Features/Scan/{Commands,Queries}`, `MediaHandler.Application/Features/LibraryRoots/{Commands,Queries}`, `MediaHandler.Application/Features/Review/{Commands,Queries}`, `MediaHandler.API/Contracts/Admin/`, `MediaHandler.Tests/Scanner/`, `MediaHandler.Tests/Features/{Scan,Review,LibraryRoots}/`, `MediaHandler.IntegrationTests/Scanner/Fixtures/`. Add a `.gitkeep` where the folder will not yet contain a file.
- [X] T002 [P] Author `MediaHandler.Infrastructure/Nas/Scanner/README.md` restating the **R-001 clean-room policy** (no verbatim copy of GPL Kodi source, derivation only from documented behavior + observed black-box behavior of `/home/tpfeifer/Repos/xbmc-master/`). Include an in-file checklist every PR touching Scanner/ must satisfy.
- [X] T003 [P] Add `MediaHandler.IntegrationTests/Scanner/Fixtures/benchmark.schema.md` describing the YAML manifest format (paths, expected classification, expected TMDB id, expected exclusion reason) consumed by `FixtureBuilder` (per quickstart §1.2).
- [X] T004 [P] Extend the integration-test web-app factory (`MediaHandler.IntegrationTests/Common/`) with a `WithFakeNasService(...)` hook so scanner tests can substitute `INasService` with an in-memory tree without touching Freebox code.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain shapes, EF wiring, the single migration, and the Application-layer interface contracts that every user story depends on.

**⚠️ CRITICAL**: No US1–US4 task may start until this phase is complete and `dotnet ef database update` succeeds against a Testcontainers SQL Server instance.

### Domain — Enums (all `MediaHandler.Domain/Enums/`)

- [X] T005 [P] Create `LibraryRootKind.cs` (`Movies | TvShows | Mixed`).
- [X] T006 [P] Create `ScanMode.cs` (`Full | Incremental`).
- [X] T007 [P] Create `ScanStatus.cs` (`Pending | Running | Completed | Failed | Cancelled`).
- [X] T008 [P] Create `ScanDecisionKind.cs` (`Added | Updated | Unchanged | Removed | Excluded | NeedsReview`).
- [X] T009 [P] Create `ReviewStatus.cs` (`Open | Resolved | Dismissed`).
- [X] T010 [P] Create `ReviewReason.cs` (`NoTmdbResult | MultipleCandidates | YearMismatch | UnparseableEpisode | NfoMalformed | UnknownFormat`).
- [X] T011 [P] Create `MediaFileRole.cs` (`Main | StackedPart | Episode`).
- [X] T012 [P] Create `ReviewResolutionAction.cs` (`Assign | Dismiss | Delete`) — referenced by review-items contract.

### Domain — New Entities (all `MediaHandler.Domain/Entities/`, inheriting `BaseEntity`)

- [X] T013 [P] Create `LibraryRoot.cs` per data-model.md (Path, Kind, Label, IsEnabled).
- [X] T014 [P] Create `ScanRun.cs` (Mode, Status, StartedAt, FinishedAt, FailureReason, LibraryRootIds JSON, denormalised count columns).
- [X] T015 [P] Create `ScanItemDecision.cs` (ScanRunId FK, FilePath, Kind, Reason, RuleId, MediaFileId?).
- [X] T016 [P] Create `ReviewItem.cs` (FilePath, Reason, Status, ParsedTitle/Year/Season/Episode, Candidates JSON, ResolvedTmdbId, ResolvedKind, ResolvedBy, ResolvedAt, FirstSeenScanRunId).
- [X] T017 [P] Create `ExclusionRule.cs` (Pattern, Kind, RuleId, Origin, IsEnabled).
- [X] T018 [P] Create `StackGroup.cs` (MediaId, FolderPath, Discriminator, PartCount).
- [X] T019 [P] Create `NfoMetadata.cs` (SourcePath, RawXml hash, ParsedTitle, ParsedYear, TmdbId, Kind, ParsedAt).
- [X] T020 [P] Create `EpisodeFileLink.cs` (TvEpisodeId, MediaFileId, OrdinalInFile) — many-to-many for multi-episode files.

### Domain — Modified Entities

- [X] T021 Modify `MediaHandler.Domain/Entities/Media.cs` — add `Year?`, `NfoMetadataId?`, `ReviewState` (nullable enum reference to ReviewStatus). No behaviour beyond data shape.
- [X] T022 Modify `MediaHandler.Domain/Entities/MediaFile.cs` — add `Fingerprint` (SHA-256 hex of size+mtime+absolute path normalised), `MtimeUtc`, `StackGroupId?`, `Role`, `LibraryRootId?`, `FirstSeenScanRunId`, `LastSeenScanRunId`, `MissingSince?`.
- [X] T023 Modify `MediaHandler.Domain/Entities/TvEpisode.cs` — expose navigation collection `EpisodeFileLinks` (replaces single `MediaFileId`), keep convenience `PrimaryFile` resolver.

### Application — Interfaces (all `MediaHandler.Application/Common/Interfaces/`)

- [X] T024 [P] Create `IKodiNameParser.cs` — `MovieNameParseResult ParseMovie(string fullPath)`, `EpisodeNameParseResult ParseEpisode(string fullPath, LibraryRootKind hint)`.
- [X] T025 [P] Create `INfoParser.cs` — `Task<NfoParseResult> ParseAsync(string nfoPath, CancellationToken ct)`.
- [X] T026 [P] Create `IStackingDetector.cs` — `IReadOnlyList<StackGroupCandidate> Group(IEnumerable<NasFileEntry> filesInFolder)`.
- [X] T027 [P] Create `IExclusionEvaluator.cs` — `ExclusionVerdict Evaluate(NasFileEntry entry, ExclusionContext ctx)`.
- [X] T028 [P] Create `ITvEpisodeMatcher.cs` — `IReadOnlyList<EpisodeNumber> Match(string filename, EpisodeNumberingHint hint)`.
- [X] T029 [P] Create `INasFileEnumerator.cs` — `IAsyncEnumerable<NasFileEntry> EnumerateAsync(LibraryRoot root, CancellationToken ct)`.
- [X] T030 [P] Create `IScanRunCoordinator.cs` — `Task<ScanRunHandle> StartAsync(StartScanRequest req, CancellationToken ct)`, `Task RequestCancellationAsync(Guid id)`, `ChannelReader<ScanProgressDto> Subscribe(Guid id)`.
- [X] T031 [P] Create `ITmdbMatcher.cs` — `Task<TmdbMatchResult> ResolveAsync(MatchQuery q, CancellationToken ct)` honouring R-001 precedence (NfoTmdbId → ExplicitTokenId → Title+Year → Title).
- [X] T032 Modify `MediaHandler.Application/Common/Interfaces/IApplicationDbContext.cs` — add `DbSet<>` for the eight new entities (depends on T013–T020).

### Application — Shared DTOs (all `MediaHandler.Application/Common/DTOs/`)

- [X] T033 [P] Create `LibraryRootDto.cs`.
- [X] T034 [P] Create `ScanRunDto.cs` + `ScanCountsDto`.
- [X] T035 [P] Create `ScanProgressDto.cs` (channel payload — phase, processed, total, last decision).
- [X] T036 [P] Create `ReviewItemDto.cs` + `TmdbCandidateDto`.

### Infrastructure — Persistence

- [X] T037 Modify `MediaHandler.Infrastructure/Persistence/MediaHandlerDbContext.cs` — register the eight new `DbSet<>`s and apply configurations from assembly (depends on T032).
- [X] T038 [P] Create `Persistence/Configurations/LibraryRootConfiguration.cs` (unique index on `Path`, max-length 1024).
- [X] T039 [P] Create `Persistence/Configurations/ScanRunConfiguration.cs` — index `StartedAt`, **filtered unique index** `WHERE Status = 'Running'` enforcing single active scan.
- [X] T040 [P] Create `Persistence/Configurations/ScanItemDecisionConfiguration.cs` — index `ScanRunId`, `FilePath`.
- [X] T041 [P] Create `Persistence/Configurations/ReviewItemConfiguration.cs` — index `Status`, `FilePath`, JSON column for `Candidates`.
- [X] T042 [P] Create `Persistence/Configurations/ExclusionRuleConfiguration.cs` — index `RuleId`.
- [X] T043 [P] Create `Persistence/Configurations/StackGroupConfiguration.cs`.
- [X] T044 [P] Create `Persistence/Configurations/NfoMetadataConfiguration.cs`.
- [X] T045 [P] Create `Persistence/Configurations/EpisodeFileLinkConfiguration.cs` — composite key `(TvEpisodeId, MediaFileId, OrdinalInFile)`.
- [X] T046 Modify `Persistence/Configurations/MediaConfiguration.cs` — column mapping + index for new fields from T021.
- [X] T047 Modify `Persistence/Configurations/MediaFileConfiguration.cs` — index on `Fingerprint`, `LibraryRootId`, `MissingSince`; drop direct `TvEpisodeId` FK and replace with `EpisodeFileLink` join.
- [X] T048 Generate single migration `MediaHandler.Infrastructure/Migrations/20260320000000_KodiScannerSchema.cs` covering ALL deltas above. Run `dotnet ef migrations add KodiScannerSchema --project MediaHandler.Infrastructure --startup-project MediaHandler.API`. Inspect generated SQL for the filtered unique index from T039 and amend with raw SQL if EF emits a non-filtered version.

### Infrastructure — Skeleton implementations (no logic yet, just compilable shells)

- [X] T049 [P] Scaffold `MediaHandler.Infrastructure/Nas/NasFileEnumerator.cs` implementing `INasFileEnumerator` over the existing `INasService` (returns the async stream, no exclusion logic yet).
- [X] T050 [P] Scaffold `MediaHandler.Infrastructure/Services/ScanRunCoordinator.cs` (singleton, owns `Dictionary<Guid, (CancellationTokenSource, Channel<ScanProgressDto>)>`) — methods throw `NotImplementedException` until US1 fills them.
- [X] T051 Wire DI in `MediaHandler.Infrastructure/DependencyInjection.cs` — register all interfaces from T024–T031, the coordinator (singleton) and enumerator (scoped). Verify `dotnet build` passes solution-wide.
- [X] T052 Add startup recovery hook in `MediaHandler.Infrastructure/Persistence/MediaHandlerDbContext.cs` (or DI bootstrap) that, on application start, transitions any `ScanRun.Status = Running` rows to `Failed` with `FailureReason = "Process restarted before scan finished"` (per quickstart §6 last row).

**Checkpoint**: Solution builds, migration applies cleanly to a Testcontainers SQL Server, all interfaces resolvable from DI. User-story phases may now begin.

---

## Phase 3: User Story 1 — Reliable Movie & TV Show Discovery (Priority: P1) 🎯 MVP

**Goal**: Administrator can register NAS roots, trigger a scan, and receive a complete and correctly classified inventory (movies, episodes, stacks, specials, exclusions, idempotent re-scan).

**Independent Test**: Register a `LibraryRoot` against the integration-test fixture (Phase 3 tasks T064–T065), `POST /api/v1/admin/scan` with mode=Full, poll `GET /api/v1/admin/scan/{id}` until `Completed`, then assert the persisted `Media`, `MediaFile`, `StackGroup`, `EpisodeFileLink` rows match the manifest exactly.

### Tests for User Story 1 — write FIRST, must FAIL

- [X] T053 [P] [US1] Author `MediaHandler.Tests/Scanner/KodiNameParserTests.cs` as a `[Theory]` table covering ≥ 60 movie filename patterns and ≥ 40 TV episode filename patterns derived clean-room per **R-001** (do not paste any string from Kodi `.cpp/.h`; document each row's source as "Kodi wiki: file naming" or "observed default behaviour" in an XML doc comment on the theory).
- [X] T054 [P] [US1] Author `MediaHandler.Tests/Scanner/ExclusionEvaluatorTests.cs` covering: video-extension allow-list, sample/trailer/extras/featurettes patterns, `Sample/`/`Extras/`/`Trailers/`/`Featurettes/` subfolders, hidden files, `.nomedia` subtree skip — every row tied to a `RuleId`.
- [X] T055 [P] [US1] Author `MediaHandler.Tests/Scanner/StackingDetectorTests.cs` for `cd1/cd2`, `part1/part2`, `disc1/disc2`, `(a)/(b)`, `pt1/pt2` — expect a single `StackGroupCandidate` per pair.
- [X] T056 [P] [US1] Author `MediaHandler.Tests/Scanner/TvEpisodeMatcherTests.cs` for `SxxExx`, `SxxExx-Eyy`, `xXy`, `1x05`, date-based `YYYY.MM.DD`, absolute-numbering fallback; multi-episode rows yield ≥ 2 `EpisodeNumber` outputs.
- [X] T057 [P] [US1] Author `MediaHandler.Tests/Features/Scan/StartScanCommandHandlerTests.cs` — happy path returns `ScanRunHandle`, second concurrent call returns `Result.Conflict("SCAN_IN_PROGRESS")`.
- [X] T058 [P] [US1] Author `MediaHandler.Tests/Features/Scan/GetScanRunQueryHandlerTests.cs` — returns mapped DTO; not-found returns `Result.NotFound`.
- [X] T059 [P] [US1] Author `MediaHandler.Tests/Features/LibraryRoots/AddLibraryRootCommandHandlerTests.cs` — duplicate path → `Conflict("LIBRARY_ROOT_DUPLICATE")`, path outside configured base paths → validation failure.
- [X] T060 [P] [US1] Author `MediaHandler.IntegrationTests/Scanner/FullScanEndToEndTests.cs::Sc001_ClassificationAccuracy_AtLeast98Percent` against the Phase-1 fixture seed (will be filled in by T064–T065) using Testcontainers SQL Server + fake `INasService`.
- [X] T061 [P] [US1] Author `MediaHandler.IntegrationTests/Scanner/IncrementalScanIdempotencyTests.cs::Sc005_IncrementalScan_UnchangedAndFast` — second scan in `Incremental` mode against unchanged tree must report Added=Updated=Removed=0 and wall-clock < 25 % of first.

### Implementation for User Story 1

- [X] T062 [US1] Implement `MediaHandler.Infrastructure/Nas/Scanner/KodiRegexCatalog.cs` — the clean-room re-derived regex tables (movie cleanup tokens, year extractors, episode patterns, stacking suffixes). Each pattern carries an inline `// SOURCE:` comment naming the public Kodi behaviour it reproduces (wiki page, advancedsettings default, observed black-box). **No string in this file may be copied verbatim from `/home/tpfeifer/Repos/xbmc-master/`** (R-001). Makes T053 / T056 begin to pass.
- [X] T063 [US1] Implement `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs` consuming `KodiRegexCatalog`. Folder-name takes precedence over filename per spec edge case. Makes all of T053 pass.
- [X] T064 [P] [US1] Implement `MediaHandler.Infrastructure/Nas/Scanner/ExclusionEvaluator.cs` (extension allow-list, regex sample/trailer matchers, folder-name exclusion set, hidden + `.nomedia` subtree handling). Pull initial rule rows into a static `ExclusionRule` seed used by EF migration (or runtime registration). Makes T054 pass.
- [X] T065 [P] [US1] Implement `MediaHandler.Infrastructure/Nas/Scanner/StackingDetector.cs`. Makes T055 pass.
- [X] T066 [P] [US1] Implement `MediaHandler.Infrastructure/Nas/Scanner/TvEpisodeMatcher.cs`. Makes T056 pass.
- [X] T067 [US1] Implement `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs` — orchestrate `enumerate → exclude → group(stacks) → parse(folder+file) → classify(movie/episode) → fingerprint → persist → emit ScanItemDecision`. **TMDB stage in this story is a stub** that records every item as `Added` with `TmdbId=null` (US2 fills it). Computes `Fingerprint = SHA256(absPath|size|mtimeUnix)` for idempotency.
- [X] T068 [US1] Flesh out `MediaHandler.Infrastructure/Services/ScanRunCoordinator.cs` (replace T050 stubs) — owns `Channel<ScanProgressDto>`, runs `ScanPipeline` on `Task.Run`, enforces single-active-scan via DB filtered index probe + in-memory mutex, supports `RequestCancellationAsync`.
- [X] T069 [US1] Implement `MediaHandler.Application/Features/LibraryRoots/Commands/AddLibraryRoot/AddLibraryRootCommand.cs` + `Handler` + `Validator` (per `contracts/library-roots.md`).
- [X] T070 [P] [US1] Implement `Features/LibraryRoots/Commands/RemoveLibraryRoot/` (Command + Handler + Validator) including the soft-delete + `MissingSince` cascade and the `Conflict("SCAN_IN_PROGRESS")` guard.
- [X] T071 [P] [US1] Implement `Features/LibraryRoots/Queries/ListLibraryRoots/` (paginated, filters: `kind`, `enabledOnly`).
- [X] T072 [US1] Implement `Features/Scan/Commands/StartScan/StartScanCommand.cs` + `Handler` + `Validator` (distinct ids, existing+enabled roots, single-active enforcement → `Conflict("SCAN_IN_PROGRESS")`).
- [X] T073 [P] [US1] Implement `Features/Scan/Commands/CancelScan/` — looks up coordinator, calls `RequestCancellationAsync`, returns post-cancel summary.
- [X] T074 [P] [US1] Implement `Features/Scan/Queries/GetScanRun/` with `includeReview` flag plumbing (review list returns null in this story; populated in US4).
- [X] T075 [P] [US1] Implement `Features/Scan/Queries/GetActiveScan/`.
- [X] T076 [P] [US1] Create `MediaHandler.API/Contracts/Admin/LibraryRootRequests.cs` (`AddLibraryRootRequest`).
- [X] T077 [P] [US1] Create `MediaHandler.API/Contracts/Admin/ScanRequests.cs` (`StartScanRequest`) and `ScanResponses.cs` (`ScanRunSummaryResponse`, `ScanRunDetailResponse`).
- [X] T078 [US1] Implement `MediaHandler.API/Controllers/AdminLibraryRootsController.cs` — `GET`, `POST`, `DELETE` per `contracts/library-roots.md`. `[Authorize(Policy = "AdminOnly")]` + `[EnableRateLimiting("fixed")]` + `[ApiVersion("1.0")]` + full `[ProducesResponseType]` set + `ApiResponse<T>` envelope.
- [X] T079 [US1] Implement `MediaHandler.API/Controllers/AdminScanController.cs` — `POST /scan` (returns 202 + Location), `GET /scan/{id}`, `GET /scan/active`, `POST /scan/{id}/cancel` per `contracts/scan.md`.
- [X] T080 [US1] Implement `MediaHandler.IntegrationTests/Scanner/Fixtures/FixtureBuilder.cs` + `benchmark.yaml` containing **≥ 200 movies + ≥ 50 TV shows** in the layouts mandated by quickstart §1.1 (per-folder + flat + stacked + multi-disc movies, Season folders + Specials + multi-episode + 1x05 + date-based TV shows, exclusion bait, NFO sidecars, misnamed review-bait). Wires the fake `INasService` to read from this manifest.
- [X] T081 [US1] Run T060 + T061 to green; iterate on `KodiRegexCatalog` + `ScanPipeline` to satisfy SC-001 (≥ 98 % auto-classification) and SC-005 (incremental < 25 % of full).**Checkpoint**: User Story 1 complete — admin can register roots, run scans, see counts, re-scan idempotently. TMDB ids are still null; review queue is empty.

---

## Phase 4: User Story 2 — Accurate TMDB Mapping (Priority: P1)

**Goal**: Every classified item is matched to TMDB using the precedence NFO-id → token-id → title+year → title; ambiguous / missing matches become `ReviewItem`s instead of silent mis-mappings.

**Independent Test**: Run scan over a fixture subset including: a clean `(title, year)` filename, a filename with an explicit `{tmdbid=12345}` token, a noisy release-tag filename, a no-year-multiple-candidates filename. Assert: first three become `Media` rows with the right `TmdbId`; fourth becomes a `ReviewItem` with `Reason = MultipleCandidates` and ≥ 2 candidates.

### Tests for User Story 2 — write FIRST

- [X] T082 [P] [US2] Author `MediaHandler.Tests/Scanner/TmdbMatcherTests.cs` — table covering: id-token wins over title+year, title+year wins over title, multi-candidate same-score → `Reason=MultipleCandidates`, year mismatch beyond ±1 → `Reason=YearMismatch`, no result → `Reason=NoTmdbResult`, transient HTTP failure → matcher surfaces `Result.Error` without throwing.
- [X] T083 [P] [US2] Author `MediaHandler.Tests/Features/Review/ResolveReviewItemCommandHandlerTests.cs` — `Assign` with valid TMDB id → status `Resolved`, persists `ResolvedTmdbId`/`ResolvedKind`/`ResolvedBy`/`ResolvedAt`; `Assign` against TMDB miss → `Result.UnprocessableEntity("TMDB_ID_NOT_FOUND")`; `Dismiss` → status `Dismissed`; `Delete` → underlying `MediaFile` gone, orphans cleaned; non-Open item → `Conflict("REVIEW_ALREADY_RESOLVED")`.
- [X] T084 [P] [US2] Extend `FullScanEndToEndTests.cs` with `Sc002_SilentMisclassRate_AtMost0p5Percent` — every divergence from the fixture's expected `(tmdbId, kind, season, episode)` MUST also yield a `ReviewItem` for the same path; otherwise it counts as silent.
- [X] T085 [P] [US2] Author `MediaHandler.IntegrationTests/Scanner/ReviewQueueResolutionTests.cs` exercising the round-trip: scan → review item created → `POST /api/v1/admin/review-items/{id}/resolve` Assign → next scan respects the resolution (no re-flag, mapped via saved id without TMDB title query).

### Implementation for User Story 2

- [X] T086 [US2] Implement `MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs` — wraps the existing `ITmdbService`; in-process LRU cache keyed by `(query, year, kind)`; per-scan dedup; precedence per **R-001**: NfoTmdbId → ExplicitTokenId (recognised id token in file/folder name) → Title+Year → Title; ambiguity policy: ≥ 2 candidates within 5 % popularity score → `MultipleCandidates`; year mismatch > ±1 → `YearMismatch`. Tolerates transient failures by returning `Result.Error` without aborting the run (FR-017).
- [X] T087 [US2] Extend `MediaHandler.Infrastructure/Tmdb/TmdbService.cs` (and its `ITmdbService` interface) with id-based movie/show lookup, episode lookup `(showId, season, episode)`, and a multi-candidate search variant returning popularity score + poster path for `TmdbCandidateDto`.
- [X] T088 [US2] Replace the TMDB stub from T067 inside `ScanPipeline.cs` with a real `ITmdbMatcher` call. On `MultipleCandidates`/`NoTmdbResult`/`YearMismatch`/`UnparseableEpisode`/`UnknownFormat`, create a `ReviewItem` (status `Open`) carrying parsed fields + candidates; on success, persist the `Media`/`TvShow`/`TvEpisode` link via `EpisodeFileLink` for multi-episode files.
- [X] T089 [US2] Implement `Features/Review/Commands/ResolveReviewItem/` (Command + Handler + Validator) per `contracts/review-items.md` — supports `Assign | Dismiss | Delete`; `Assign` re-runs only the TMDB-resolution stage for the file path using the supplied id, persists the resolution, and writes a row to a "resolution memory" table (or column on `ReviewItem`) so a subsequent scan honours it.
- [X] T090 [P] [US2] Implement `Features/Review/Queries/ListReviewItems/` (paginated, filters `status` default `Open`, `reason`, `scanRunId`).
- [X] T091 [P] [US2] Create `MediaHandler.API/Contracts/Admin/ReviewRequests.cs` (`ResolveReviewRequest`).
- [X] T092 [US2] Implement `MediaHandler.API/Controllers/AdminReviewController.cs` — `GET /review-items`, `POST /review-items/{id}/resolve` per `contracts/review-items.md`. `[Authorize(Policy = "AdminOnly")]`, `ApiResponse<T>` envelope, `[ProducesResponseType]` for 200/400/401/403/404/409/422.
- [X] T093 [US2] Implement scan-pipeline read-back of resolved `ReviewItem`s so a subsequent scan matching the same `FilePath` (or fingerprint) reuses the saved `(TmdbId, Kind)` without re-querying TMDB title search (closes the loop tested by T085).
- [X] T094 [US2] Run T084 to green; tune `TmdbMatcher` thresholds (popularity gap, year tolerance) until SC-002 (≤ 0.5 % silent misclassification) is satisfied on the benchmark fixture.

**Checkpoint**: User Stories 1 + 2 both green. Library is correctly populated and TMDB-mapped, with ambiguous items routed to the review queue and admin-resolvable.

---

## Phase 5: User Story 3 — NFO Sidecar Files Override Auto-Detection (Priority: P2)

**Goal**: When `movie.nfo` / `tvshow.nfo` / per-episode `.nfo` exists, its `<tmdbid>`/`<title>`/`<year>` overrides filename guesses; malformed NFO falls back gracefully and logs a warning.

**Independent Test**: Place a movie file whose filename suggests title `"Foo (2010)"` next to a `movie.nfo` containing `<tmdbid>27205</tmdbid>` (Inception). Run scan. Assert the persisted `Media.TmdbId == 27205`, `NfoMetadataId` is set, and that mutating the NFO to invalid XML on a re-scan logs a warning and falls back to filename without aborting.

### Tests for User Story 3 — write FIRST

- [X] T095 [P] [US3] Author `MediaHandler.Tests/Scanner/NfoParserTests.cs` — well-formed `movie.nfo` with `<tmdbid>` parsed correctly; malformed XML returns `NfoParseResult.Malformed` (not throws); missing optional fields return null without failing; `tvshow.nfo` and per-episode `.nfo` shapes covered.
- [X] T096 [P] [US3] Add `Sc_Nfo_OverridesFilenameGuess` integration scenario to `FullScanEndToEndTests.cs` covering acceptance scenarios 1–3 of US3.

### Implementation for User Story 3

- [X] T097 [US3] Implement `MediaHandler.Infrastructure/Nas/Scanner/NfoParser.cs` using `XDocument`; tolerant of unknown elements; returns `NfoParseResult { Title, Year, TmdbId, Kind, ParsedSuccessfully, Warning? }`. Makes T095 pass.
- [X] T098 [US3] Wire NFO discovery into `ScanPipeline`: per-folder discover `movie.nfo`/`tvshow.nfo`; per-file discover `<basename>.nfo`. On parse success, persist a `NfoMetadata` row, attach to the `Media`, and surface the result to `ITmdbMatcher` so its precedence chain (NfoTmdbId first) takes effect. On `Warning`, write a `ScanItemDecision` of `Kind=NeedsReview, Reason=NfoMalformed` ONLY if filename fallback also fails; otherwise emit a Serilog warning + decision row with `Reason=NfoMalformed` but proceed.
- [X] T099 [US3] Add the override-precedence rows to `KodiNameParserTests` / `TmdbMatcherTests` ensuring NFO id always wins (per plan US3 mapping note).
- [X] T100 [US3] Run T096 to green.

**Checkpoint**: NFO escape-hatch works; libraries with curated NFOs map deterministically.

---

## Phase 6: User Story 4 — Visibility Into Scan Outcomes & Errors (Priority: P2)

**Goal**: Admin can see counts (added/updated/unchanged/removed/excluded/needs-review) for any scan run, drill into the needs-review list, and diagnose any individual file's outcome in < 30 s (SC-006).

**Independent Test**: Run a scan over a fixture deliberately containing a sample, an extras file, a misnamed movie, and an episode in the wrong season folder. Hit `GET /api/v1/admin/scan/{id}?includeReview=true`. Assert: counts are non-zero in every relevant bucket; the misnamed movie appears under `reviewItems` with a human-readable reason; for any chosen file path, a `ScanItemDecision` row exists with `Kind`, `Reason`, and `RuleId`.

### Tests for User Story 4 — write FIRST

- [ ] T101 [P] [US4] Add `Sc006_AnyFileDiagnosable_Under30Seconds` to `FullScanEndToEndTests.cs` — for every fixture file path, the GET-detail response (with `includeReview=true`) MUST contain either a matching `MediaFile` reference OR a `ScanItemDecision` row OR a `ReviewItem` row, each carrying a `Reason` / `RuleId`. Asserts presence in O(1) lookup per path.
- [ ] T102 [P] [US4] Unit test `MediaHandler.Tests/Features/Scan/GetScanRunQueryHandlerTests.cs::IncludeReview_ReturnsOpenItemsForRun` (extension of T058).

### Implementation for User Story 4

- [ ] T103 [US4] Extend `GetScanRun` query handler + DTO mapping so `includeReview=true` returns up to 100 most recent open `ReviewItem`s scoped to the run (per `contracts/scan.md`).
- [ ] T104 [US4] Ensure `ScanPipeline` writes a `ScanItemDecision` row for **every** processed path (Added/Updated/Unchanged/Removed/Excluded/NeedsReview) including `RuleId` for exclusions and `Reason` for review items (FR-023 data-side).
- [ ] T105 [US4] Implement removed-file detection: at the end of a scan, every `MediaFile` belonging to a scanned `LibraryRoot` whose `LastSeenScanRunId != currentRunId` gets `MissingSince = UtcNow` and a `ScanItemDecision { Kind=Removed }` row (FR-019). Failing root reads (NAS unreachable) MUST suppress this step for that root and write a single `Reason="NAS unreachable"` decision instead — no mass-removal on transient failure.
- [ ] T106 [US4] Run T101, T102 to green.

**Checkpoint**: All four user stories independently demonstrable.

---

## Phase 7: Cross-Cutting & Polish

**Purpose**: FR-023 logging, the remaining success-criteria tests, authorisation coverage, and clean-up.

### Logging (FR-023)

- [ ] T107 Add structured Serilog enrichers in `ScanPipeline` — every per-file decision logs at `Information` with properties `{ScanRunId, FilePath, Kind, Reason?, RuleId?, TmdbId?}`. Stage transitions log at `Debug`. Confirm log volume is bounded for a 10 000-file scan (no per-file `Warning`+ unless actual problem).

### Success-criteria tests (remaining)

- [ ] T108 [P] Implement `MediaHandler.IntegrationTests/Scanner/Sc003_ExclusionFidelity_NoFalsePositivesOrFalseNegatives` per quickstart §SC-003 (every `expected: excluded` path → `ScanItemDecision.Kind=Excluded` + zero `MediaFile`; every `expected: included` → `MediaFile` exists).
- [ ] T109 [P] Implement `MediaHandler.IntegrationTests/Scanner/Sc004_KodiBehavioralParity` — runs scanner over a curated parity-fixture subset (paths annotated with the *observed* Kodi classification recorded out-of-band); asserts ≥ 99 % matching outcomes.
- [ ] T110 [P] Implement `MediaHandler.IntegrationTests/Scanner/Sc007_ManualCorrectionReduction_AtLeast80Percent` — operates against the synthetic benchmark plus an injected baseline number representing the previous implementation's review count.

### Authorisation (SC-008)

- [ ] T111 Implement `MediaHandler.IntegrationTests/Scanner/AdminAuthorizationTests.cs` covering, for **every** endpoint in `contracts/scan.md`, `contracts/library-roots.md`, `contracts/review-items.md`:
  - Anonymous → 401.
  - Authenticated non-admin (`User` role) JWT → 403 (the explicit SC-008 case).
  - Admin JWT → 2xx.
  - Anonymous `POST /api/v1/admin/scan` does **not** create any `ScanRun` row in the DB.

### Documentation & cleanup

- [ ] T112 [P] Update `MediaHandler.Infrastructure/Nas/Scanner/README.md` (final pass) listing every regex/heuristic source citation accumulated in `KodiRegexCatalog.cs`, and the Kodi-version reference (commit hash from `/home/tpfeifer/Repos/xbmc-master/`).
- [ ] T113 [P] Update top-level `README.md` / `CONTRIBUTING.md` with: how to run a scan locally, where to add a failing parser case, the no-GPL-paste rule.
- [ ] T114 [P] If a Postman / Bruno collection exists in the repo, add the four new endpoint groups (scan, scan/{id}, scan/{id}/cancel, scan/active, library-roots CRUD, review-items list+resolve) with example admin JWT auth. If no such collection exists, skip and note in PR.
- [ ] T115 Retire `MediaHandler.Infrastructure/Nas/MediaFileNameParser.cs`: confirm no callers remain in the `main` solution graph (only test fixtures may keep references during transition), then delete and remove DI registration. Run `dotnet build` and full test suite.
- [ ] T116 Final benchmark pass — execute `quickstart.md` end-to-end against the Phase-3 fixture, capture SC-001..SC-008 numbers in a markdown report under `specs/001-kodi-style-scanner/benchmark-report.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. Blocks everything else.
- **US1 (Phase 3)**: depends on Foundational. MVP.
- **US2 (Phase 4)**: depends on Foundational; functionally extends US1's `ScanPipeline` and `ReviewItem` model — start once US1's pipeline skeleton (T067) compiles, even before US1 reaches green.
- **US3 (Phase 5)**: depends on Foundational; integrates with `ScanPipeline` (T067) and `TmdbMatcher` (T086 precedence chain).
- **US4 (Phase 6)**: depends on Foundational; needs `ScanPipeline` writing decision rows (US1) and `ReviewItem`s (US2) to be meaningful.
- **Polish (Phase 7)**: depends on US1+US2+US3+US4 reaching green for full coverage; T107 (logging) and T111 (authz) can begin once US1 controllers exist.

### Cross-Story Dependencies

- T088 (US2 pipeline integration) requires T067 (US1 pipeline skeleton).
- T089 / T093 (US2 review resolve + persistence) require T032/T037/T048 (Foundational DB).
- T098 (US3 NFO branch) requires T067 + T086 (precedence chain in matcher).
- T103 / T104 (US4) require T067 + T088 (decisions + review items being written).
- T111 (authz) requires T078, T079, T092 (controllers exist).

### Within Each User Story

- Test tasks first; verify red.
- Domain → EF → Application handlers → API controllers.
- Pipeline component → DI registration → integration test.

### Parallel Opportunities

- T005–T020 (enums + new entities, separate files) can all run in parallel.
- T038–T045 (eight EF configurations for new entities) are independent files, fully parallel.
- T024–T031 (eight Application interfaces) are independent files, fully parallel.
- T053–T056 (four scanner unit-test files) are independent.
- T064–T066 (Exclusion/Stacking/Episode component implementations) are independent.
- T070, T071, T073, T074, T075 are different handler folders — parallel.
- T108, T109, T110 (SC-003/004/007) live in separate test files and can be tackled in parallel by different developers.

---

## Parallel Example: User Story 1

```bash
# After T067 (ScanPipeline skeleton) lands, launch parser implementations in parallel:
Task: "T064 Implement ExclusionEvaluator.cs"
Task: "T065 Implement StackingDetector.cs"
Task: "T066 Implement TvEpisodeMatcher.cs"

# After T072 lands, launch independent handlers in parallel:
Task: "T070 Implement RemoveLibraryRoot handler"
Task: "T071 Implement ListLibraryRoots handler"
Task: "T073 Implement CancelScan handler"
Task: "T074 Implement GetScanRun handler"
Task: "T075 Implement GetActiveScan handler"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 → Phase 2 → Phase 3.
2. Stop at the US1 checkpoint and demo: an admin can register a NAS root, run a scan, see counts, re-scan idempotently. TMDB ids may be null at this point.
3. SC-001 + SC-005 should already be measurable.

### Incremental delivery

- Add US2 → SC-002 measurable, review queue functional.
- Add US3 → curated NFO libraries map deterministically.
- Add US4 → admin diagnose-any-file in < 30 s.
- Polish phase wraps SC-003/004/007/008 + logging + docs.

### Parallel team strategy

- Dev A on US1 implementation (T062–T081).
- Dev B on US2 once T067 compiles (T086–T094).
- Dev C on US3 once T067 + T086 compile (T097–T100).
- All three converge on US4 (T103–T106) and Polish.

---

## Notes

- `[P]` = different files, no incomplete dependency.
- Every task with a regex / heuristic component MUST cite its public source in code comments per **R-001**; reviewers reject any PR pasting from `/home/tpfeifer/Repos/xbmc-master/` GPL files.
- All API tasks must produce `ApiResponse<T>`, carry `[Authorize(Policy = "AdminOnly")]`, `[EnableRateLimiting("fixed")]`, full `[ProducesResponseType]` set, and live under `/api/v1/admin/`.
- Tests must FAIL before the matching implementation lands (TDD per Constitution II).
- Commit after each task or logical group; the `before_implement` git hook will prompt accordingly.


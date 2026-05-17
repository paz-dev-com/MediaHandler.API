# Implementation Plan: Kodi-Style NAS Library Scanner

**Branch**: `001-kodi-style-scanner` | **Date**: 2026-03-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-kodi-style-scanner/spec.md`

## Summary

Replace the current best-effort NAS scanner with a Kodi-parity classification pipeline that:

1. Walks configured NAS roots (typed `Movies` / `TvShows` / `Mixed`).
2. Applies Kodi-equivalent inclusion/exclusion, NFO override, filename/folder parsing, season/episode detection, multi-episode and multi-part (stacked) grouping.
3. Resolves each logical item to TMDB using the most authoritative signal available (NFO id → token id → title+year → title), flagging ambiguous results to a **needs-review** queue rather than guessing.
4. Persists per-scan run history (`ScanRun`, `ScanItemDecision`) and per-file fingerprints to make incremental scans cheap and idempotent, and to give the administrator a diagnostic report covering every file's outcome.
5. Exposes admin-gated CQRS endpoints under `POST /api/v1/admin/scan`, `GET /api/v1/admin/scan/{id}`, library-root CRUD, and a review-resolution workflow.

The implementation is a **clean-room re-derivation** of Kodi's scanning heuristics — the regex sets, exclusion lists, stacking rules, and NFO semantics are reproduced from documented Kodi behavior (advancedsettings defaults, wiki pages, file-naming conventions) without copying GPL-2.0 source. Verbatim ports of Kodi `.cpp`/`.h` are explicitly forbidden by this plan; see `research.md` for the licensing decision and the in-tree attribution policy.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (`<TargetFramework>net10.0</TargetFramework>` confirmed in every `*.csproj`).

**Primary Dependencies**:

- MediatR (CQRS handlers)
- FluentValidation + `ValidationBehavior` pipeline
- EF Core 10 + `Microsoft.EntityFrameworkCore.SqlServer`
- Serilog (structured scan progress + per-file decision logs)
- `Microsoft.Extensions.Http.Resilience` (Polly retry + circuit breaker, already wired)
- `System.Threading.Channels` (scan progress fan-out — no new package)
- `System.Xml.Linq` (`XDocument`) for NFO parsing — no new dependency

**Storage**: SQL Server (existing `MediaHandlerDbContext`), one new EF Core migration covering all schema additions.

**Testing**: xUnit + NSubstitute + EF Core InMemory (unit, `MediaHandler.Tests`); Testcontainers.MsSql (integration, `MediaHandler.IntegrationTests`).

**Target Platform**: Linux server (Docker / docker-compose), .NET 10 runtime.

**Project Type**: Web service (ASP.NET Core API) using the existing four-project Clean Architecture solution.

**Performance Goals**:

- Initial full scan: ≥ 200 files/sec of *parsing & classification work* on cached file metadata (TMDB calls excluded; bounded by external RPS).
- Incremental scan of unchanged library: < 25 % of full-scan wall time (SC-005).
- TMDB call budget: in-process LRU cache keyed by `(query, year, kind)`; per-scan dedup so the same title is never queried twice (FR-017).

**Constraints**:

- All existing constitution rules (Result pattern, CQRS one-handler-per-file, FluentValidation, AsNoTracking on reads, AdminOnly policy, ApiResponse envelope, `/api/v1/` prefix, rate limiter, resilience handlers).
- No new runtime dependency on a Kodi binary or process.
- No GPL-licensed source copied verbatim into the repository.
- Existing `INasService` (Freebox) is the only NAS access path; this feature extends it via `INasFileEnumerator` but does not replace it.

**Scale/Scope**:

- Benchmark fixture: ~ 200 movies, ~ 50 TV shows; production NAS may reach low thousands of files.
- Single-tenant / single-administrator deployment; concurrent scans are forbidden (one active `ScanRun` at a time, enforced by a unique filtered index).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Verdict |
|---|---|---|
| I. Code Quality — Clean Architecture & CQRS | Scan orchestration is implemented as MediatR commands/queries in `MediaHandler.Application/Features/Scan`, `Features/LibraryRoots`, `Features/Review`. Infrastructure owns IO (NAS, regex, NFO XML, TMDB). Domain receives only new entities + enums, no logic that depends on EF or HTTP. Result pattern used everywhere. FluentValidation validators alongside every command. EF entities configured via `IEntityTypeConfiguration<T>`. | ✅ Pass |
| II. Testing Standards | Unit tests for every handler (success + primary failure path) in `MediaHandler.Tests`. Heavy table-driven tests for the Kodi-equivalent parsers (golden fixtures derived clean-room from Kodi's documented behavior). Integration tests in `MediaHandler.IntegrationTests` exercise the full scan endpoint against Testcontainers SQL Server with a faked `INasService`. | ✅ Pass |
| III. UX Consistency | All new endpoints return `ApiResponse<T>`, paginated where applicable, full `[ProducesResponseType]` coverage, admin-gated via `[Authorize(Policy = "AdminOnly")]`, prefixed `/api/v1/`. Idempotency: `POST /api/v1/admin/scan` rejects with HTTP 409 when an active scan exists. | ✅ Pass |
| IV. Performance | Scan runs as a background worker triggered by the command (returns 202 + `ScanRun` id immediately). The NAS file list is consumed as a stream where the underlying API allows. EF reads use `AsNoTracking()`. New indexes on `MediaFile.Fingerprint`, `ScanRun.StartedAt`, `ReviewItem.Status`, `LibraryRoot.Path`, `ScanItemDecision.ScanRunId`. TMDB calls go through the existing resilient `HttpClient`. | ✅ Pass |
| Architecture & Security | Dependency rule preserved (Domain has no new external refs; Application gains one new interface per Infrastructure capability — `IKodiNameParser`, `INfoParser`, `IStackingDetector`, `IExclusionEvaluator`, `ITvEpisodeMatcher`, `INasFileEnumerator`, `IScanRunCoordinator`, `ITmdbMatcher`). Secrets unchanged. AdminOnly policy on every new endpoint. `BaseEntity` audit fields on every new entity. | ✅ Pass |
| Workflow & Quality Gates | One feature branch (`001-kodi-style-scanner`). All new files use file-scoped namespaces, `#nullable enable`, primary constructors, `record` DTOs. Conventional Commits enforced at commit time by the `before_*` git hooks. | ✅ Pass |

**No constitution violations to justify.** `Complexity Tracking` table left empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-kodi-style-scanner/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 — decisions (licensing, regex sources, NFO, TMDB, concurrency, fingerprints)
├── data-model.md        # Phase 1 — entity deltas + new entities + migration plan
├── quickstart.md        # Phase 1 — fixture library + verification of SC-001..SC-008
├── contracts/           # Phase 1 — admin scan / review / library-root API contracts
│   ├── scan.md
│   ├── library-roots.md
│   └── review-items.md
└── tasks.md             # Phase 2 output (/speckit.tasks command — NOT created here)
```

### Source Code (repository root)

The feature lives entirely inside the existing four-project solution; no new projects.

```text
MediaHandler.Domain/
├── Entities/
│   ├── LibraryRoot.cs                # NEW — configured NAS path + content type
│   ├── ScanRun.cs                    # NEW — single scan execution + summary counts
│   ├── ScanItemDecision.cs           # NEW — per-file decision row
│   ├── ReviewItem.cs                 # NEW — admin-resolvable unmatched/ambiguous item
│   ├── ExclusionRule.cs              # NEW — pattern row (extension/folder/filename/marker)
│   ├── StackGroup.cs                 # NEW — groups multi-part movie files
│   ├── NfoMetadata.cs                # NEW — parsed NFO snapshot attached to a Media or TvSeason
│   ├── EpisodeFileLink.cs            # NEW — many-to-many TvEpisode ↔ MediaFile (multi-episode files)
│   ├── Media.cs                      # MODIFIED — add NfoMetadataId?, Year?, ReviewState
│   ├── MediaFile.cs                  # MODIFIED — add Fingerprint, MtimeUtc, StackGroupId?, Role, LibraryRootId, FirstSeenScanRunId, LastSeenScanRunId, MissingSince?
│   └── TvEpisode.cs                  # MODIFIED — reachable from MediaFile via EpisodeFileLink
└── Enums/
    ├── LibraryRootKind.cs            # NEW — Movies | TvShows | Mixed
    ├── ScanMode.cs                   # NEW — Full | Incremental
    ├── ScanStatus.cs                 # NEW — Pending | Running | Completed | Failed | Cancelled
    ├── ScanDecisionKind.cs           # NEW — Added | Updated | Unchanged | Removed | Excluded | NeedsReview
    ├── ReviewStatus.cs               # NEW — Open | Resolved | Dismissed
    ├── ReviewReason.cs               # NEW — NoTmdbResult | MultipleCandidates | YearMismatch | UnparseableEpisode | NfoMalformed | UnknownFormat
    └── MediaFileRole.cs              # NEW — Main | StackedPart | Episode

MediaHandler.Application/
├── Common/Interfaces/
│   ├── IKodiNameParser.cs            # NEW
│   ├── INfoParser.cs                 # NEW
│   ├── IStackingDetector.cs          # NEW
│   ├── IExclusionEvaluator.cs        # NEW
│   ├── ITvEpisodeMatcher.cs          # NEW
│   ├── INasFileEnumerator.cs         # NEW
│   ├── IScanRunCoordinator.cs        # NEW
│   ├── ITmdbMatcher.cs               # NEW
│   └── IApplicationDbContext.cs      # MODIFIED — adds DbSets for new entities
├── Features/
│   ├── LibraryRoots/                 # NEW
│   │   ├── Commands/AddLibraryRoot/
│   │   ├── Commands/RemoveLibraryRoot/
│   │   └── Queries/ListLibraryRoots/
│   ├── Scan/                         # NEW
│   │   ├── Commands/StartScan/
│   │   ├── Commands/CancelScan/
│   │   ├── Queries/GetScanRun/
│   │   └── Queries/GetActiveScan/
│   └── Review/                       # NEW
│       ├── Commands/ResolveReviewItem/
│       └── Queries/ListReviewItems/
└── Common/DTOs/
    ├── ScanRunDto.cs                 # NEW
    ├── ScanProgressDto.cs            # NEW (channel payload)
    ├── ReviewItemDto.cs              # NEW
    └── LibraryRootDto.cs             # NEW

MediaHandler.Infrastructure/
├── Nas/
│   ├── FreeboxNasService.cs          # (unchanged)
│   ├── MediaFileNameParser.cs        # to be retired once KodiNameParser ships (kept for migration only)
│   ├── NasFileEnumerator.cs          # NEW — implements INasFileEnumerator over INasService
│   └── Scanner/                      # NEW — scanner pipeline, internal stages
│       ├── README.md                 # NEW — restates the no-GPL-paste policy at folder level
│       ├── KodiNameParser.cs         # NEW — clean-room regex pipeline
│       ├── KodiRegexCatalog.cs       # NEW — re-derived regex tables (no GPL paste)
│       ├── ExclusionEvaluator.cs     # NEW
│       ├── StackingDetector.cs       # NEW
│       ├── TvEpisodeMatcher.cs       # NEW
│       ├── NfoParser.cs              # NEW — XDocument-based, tolerant
│       ├── TmdbMatcher.cs            # NEW — wraps ITmdbService with cache + ambiguity policy
│       └── ScanPipeline.cs           # NEW — orchestrates: enumerate → exclude → group → parse → NFO → TMDB → persist
├── Services/
│   └── ScanRunCoordinator.cs         # NEW — singleton; owns CancellationTokenSource + Channel<ScanProgressDto>; runs scan on a background Task
├── Persistence/
│   ├── MediaHandlerDbContext.cs      # MODIFIED — new DbSet<>s
│   └── Configurations/
│       ├── LibraryRootConfiguration.cs       # NEW
│       ├── ScanRunConfiguration.cs           # NEW
│       ├── ScanItemDecisionConfiguration.cs  # NEW
│       ├── ReviewItemConfiguration.cs        # NEW
│       ├── ExclusionRuleConfiguration.cs     # NEW
│       ├── StackGroupConfiguration.cs        # NEW
│       ├── NfoMetadataConfiguration.cs       # NEW
│       ├── EpisodeFileLinkConfiguration.cs   # NEW
│       ├── MediaConfiguration.cs             # MODIFIED — new columns + indexes
│       └── MediaFileConfiguration.cs         # MODIFIED — new columns + indexes
└── Migrations/
    └── 20260320000000_KodiScannerSchema.cs   # NEW — single migration covering ALL additions

MediaHandler.API/
├── Controllers/
│   ├── AdminScanController.cs              # NEW — /api/v1/admin/scan*
│   ├── AdminLibraryRootsController.cs      # NEW — /api/v1/admin/library-roots
│   └── AdminReviewController.cs            # NEW — /api/v1/admin/review-items
└── Contracts/Admin/
    ├── ScanRequests.cs                # NEW — StartScanRequest(roots[], mode)
    ├── ScanResponses.cs               # NEW — ScanRunSummaryResponse, ScanRunDetailResponse
    ├── LibraryRootRequests.cs         # NEW
    └── ReviewRequests.cs              # NEW — ResolveReviewRequest(tmdbId, kind)

MediaHandler.Tests/                    # unit
├── Scanner/
│   ├── KodiNameParserTests.cs        # large table-driven golden file
│   ├── ExclusionEvaluatorTests.cs
│   ├── StackingDetectorTests.cs
│   ├── TvEpisodeMatcherTests.cs
│   ├── NfoParserTests.cs
│   └── TmdbMatcherTests.cs
└── Features/
    ├── Scan/StartScanCommandHandlerTests.cs
    ├── Scan/GetScanRunQueryHandlerTests.cs
    ├── Review/ResolveReviewItemCommandHandlerTests.cs
    └── LibraryRoots/AddLibraryRootCommandHandlerTests.cs

MediaHandler.IntegrationTests/
└── Scanner/
    ├── FullScanEndToEndTests.cs               # walks a fake INasService against Testcontainers SQL
    ├── IncrementalScanIdempotencyTests.cs     # SC-005
    ├── ReviewQueueResolutionTests.cs
    └── AdminAuthorizationTests.cs             # SC-008
```

**Structure Decision**: The feature reuses the existing four-project Clean Architecture solution. No new project is created. Layer ownership of the new scanner pipeline is strictly:

- **Domain** owns only data shapes (entities + enums + the immutable identity invariant of a `MediaFile.Fingerprint`). Zero references to EF Core, HTTP, regex tables, or file IO.
- **Application** owns orchestration, CQRS commands/queries, the `IScanRunCoordinator` contract, validation, the Result pipeline, and the matcher / parser *interfaces*. No regex source code lives here; only abstractions.
- **Infrastructure** owns every concrete implementation: regex tables (`KodiRegexCatalog`), NFO XML parsing, NAS IO, TMDB HTTP wiring with cache, the persistent `ScanRunCoordinator`, EF entity configurations, and the migration. The clean-room re-derived heuristics are physically located in `MediaHandler.Infrastructure/Nas/Scanner/` so any auditor can find them in one folder, with a top-of-folder `README.md` restating the no-GPL-paste policy.
- **API** owns the three new controllers, the contracts under `Contracts/Admin/`, the `[Authorize(Policy = "AdminOnly")]` decoration, and the `ApiResponse<T>` wrapping. It contains no scanning logic of its own.

### User-story → code mapping

| User Story | Primary code locations |
|---|---|
| **US1** — Reliable Movie & TV discovery (P1) | `Scanner/ScanPipeline.cs`, `Scanner/KodiNameParser.cs`, `Scanner/StackingDetector.cs`, `Scanner/ExclusionEvaluator.cs`, `Scanner/TvEpisodeMatcher.cs`, `Features/Scan/Commands/StartScan/`, `Features/LibraryRoots/*`, `AdminScanController`, `AdminLibraryRootsController` |
| **US2** — Accurate TMDB mapping (P1) | `Scanner/TmdbMatcher.cs`, extended `TmdbService.cs`, `ReviewItem` flow inside `ScanPipeline`, `Features/Review/*` |
| **US3** — NFO sidecar overrides (P2) | `Scanner/NfoParser.cs`, `NfoMetadata` entity, NFO branch inside `ScanPipeline`, override precedence asserted in `KodiNameParserTests` |
| **US4** — Visibility into scan outcomes (P2) | `ScanRun`, `ScanItemDecision`, `ReviewItem` entities; `Features/Scan/Queries/GetScanRun`, `Features/Review/Queries/ListReviewItems`; `AdminScanController.GetScanRun`, `AdminReviewController.List` |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*(none — Constitution Check passed cleanly)*


# Implementation Plan: Kodi Video Database Import

**Branch**: `008-kodi-db-import` | **Date**: 2026-06-21 | **Spec**: [specs/008-kodi-db-import/spec.md](spec.md)  
**Input**: Feature specification from `specs/008-kodi-db-import/spec.md` (scope decisions confirmed by user — see §13)

---

## Summary

Adds an admin-only, on-demand Kodi video database (`MyVideos<version>.db`) import: the admin uploads the SQLite file (real run or preview), a background run — modeled 1:1 on the existing `ScanRun`/`ScanRunCoordinator` pattern — reads movies/TV shows/episodes via a new `Microsoft.Data.Sqlite`-based reader, resolves identity (Kodi TMDB id → external-id lookup → title search via the existing `TmdbMatcher` policy), dedupes against existing `Media` by `(Type, TmdbId)`, translates Kodi URIs to NAS paths through persisted admin-managed prefix mappings, and links scanner-known files (movie files incl. `stack://` parts, episode files via `EpisodeFileLink` with position). Three new tables (`ImportRuns`, `ImportItemOutcomes`, `KodiPathMappings`), one additive column (`ReviewItem.Source`), one migration (`AddKodiDbImport`), two new admin controllers, and one new `ITmdbService` method (`FindByExternalIdAsync`) are required. Re-imports are idempotent; conflicts always preserve existing links and are reported; the uploaded file is discarded when the run reaches a terminal state. On successful completion of a real import run, the existing TMDB enrichment mechanism is triggered automatically (§1.6 "Post-import enrichment trigger") so imported entries get their TMDB metadata without a manual step.

---

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: MediatR, FluentValidation, EF Core 9/10 (SQL Server), ASP.NET Core, Serilog, **Microsoft.Data.Sqlite 10.0.11 (NEW — not currently referenced anywhere in the solution)**  
**Storage**: SQL Server via EF Core (app data); uploaded Kodi SQLite file processed transiently, read-only, from temp storage  
**Testing**: xUnit + NSubstitute + FluentAssertions + EF Core InMemory (unit); Testcontainers.MsSql (integration); synthetic SQLite fixture DBs built in-test via `Microsoft.Data.Sqlite` (no real Kodi DB committed)  
**Target Platform**: Linux/Windows server — ASP.NET Core 10  
**Performance Goals** (SC-006): 5,000-item import < 10 min with ≤ 500 provider lookups; preview < 2 min (zero provider traffic). Achieved via: direct-TMDB-id short-circuit (no provider call), `TmdbMatcher` per-run cache, three bulk pre-loads (files, medias, baseline outcome keys), batched `SaveChangesAsync` every 50 items.  
**Constraints**: Clean Architecture 4-layer rule; no domain logic in controllers; `AsNoTracking()` on reads; uploaded DB is untrusted input (read-only SQLite connection, hardcoded SQL only, size-capped).  
**Scale/Scope**: Personal NAS-scale (~10k `MediaFile` rows, ~5k Kodi items/run).

---

## Constitution Check

*GATE: Must pass before implementation begins.*

| Principle | Requirement | This Feature | Status |
|-----------|-------------|--------------|--------|
| **I. Clean Architecture** | Domain → App → Infra → API; no upward refs | Entities/enums in Domain; handlers/DTOs/interfaces in Application; SQLite reader, pipeline, coordinator, EF configs in Infrastructure; controllers/contracts in API. `KodiImportOptions` stays in Infrastructure — Application handlers never touch it (all option-dependent checks delegated to Infrastructure services behind Application interfaces) | ✅ PASS |
| **I. CQRS via MediatR** | One handler file + one validator file per subfolder | 9 handlers in dedicated subfolders under `Features/KodiImport/`; validators in separate files per AGENTS.md (note: some older features inline validators — this feature follows the AGENTS.md layout strictly) | ✅ PASS |
| **I. Result pattern** | `Result<T>`; no exceptions for expected failures | All handlers return `Result<T>`; string-prefix error codes (`UNSUPPORTED_VERSION`, `IMPORT_IN_PROGRESS`, …) | ✅ PASS |
| **I. FluentValidation pipeline** | Validator for every command/query with user input | Validators for StartKodiImport, Create/Update mapping, both paged list queries; id-only commands/queries (GetKodiImportRun, DeleteKodiPathMapping) have none, matching the `GetScanRunQuery` precedent | ✅ PASS |
| **I. Entity configuration** | Fluent API `IEntityTypeConfiguration<T>` per entity | 3 new configuration classes + 1 modified (`ReviewItemConfiguration`) | ✅ PASS |
| **I. Code style** | File-scoped namespaces, primary ctors, `record`, nullable | All new files follow conventions | ✅ PASS |
| **I. No-GPL (R-001)** | Schema facts from public docs only; `// SOURCE:` comments | Reader SQL uses only documented schema concepts (Kodi wiki "Databases"/"File stacking" pages); every SQL constant, the `stack://` split rule, and the filename-version regex carry `// SOURCE:` comments. No Kodi source is copied | ✅ PASS |
| **II. Unit tests** | Handler success+failure paths; TestDbContext | 10 new unit test classes (~60 tests) incl. pipeline behavior tests over `TestDbContext` | ✅ PASS |
| **II. Integration tests** | Testcontainers.MsSql for multi-step workflows | End-to-end import/re-import/preview tests + API authorization tests | ✅ PASS |
| **III. ApiResponse envelope / versioned routes / AdminOnly** | Standard envelope, `/api/v1/`, class-level auth | Two controllers, both `[Authorize(Policy = "AdminOnly")]` + `[EnableRateLimiting("fixed")]` under `api/v1/admin/kodi-import` | ✅ PASS |
| **IV. Query performance / no N+1** | Eager loads, server-side pagination | History/items endpoints paginate server-side; pipeline pre-loads dictionaries (no per-item queries) | ✅ PASS |

**Verdict**: No violations. Implementation may proceed.

---

## Project Structure

### Documentation (this feature)

```text
specs/008-kodi-db-import/
├── spec.md              ← validated functional spec (exists)
└── plan.md              ← this file (only artifact produced by the architect phase)
```

### Source Code — Files to Create or Modify

```text
MediaHandler.Domain/
├── Entities/
│   ├── ImportRun.cs                                  [CREATE — run record + denormalized counters]
│   ├── ImportItemOutcome.cs                          [CREATE — per-Kodi-item outcome row]
│   ├── KodiPathMapping.cs                            [CREATE — persisted prefix mapping]
│   └── ReviewItem.cs                                 [MODIFY — add Source property]
└── Enums/
    ├── KodiImportMode.cs                             [CREATE — Import | Preview]
    ├── ImportRunStatus.cs                            [CREATE — Pending | Running | Completed | Failed]
    ├── ImportItemStatus.cs                           [CREATE — 9 values, see §1.1]
    ├── ImportLinkStatus.cs                           [CREATE — 7 values, see §1.1]
    ├── KodiItemKind.cs                               [CREATE — Movie | TvShow | Episode | MusicVideo]
    └── ReviewItemSource.cs                           [CREATE — Scan | KodiImport]

MediaHandler.Application/
├── Common/
│   ├── Interfaces/
│   │   ├── IApplicationDbContext.cs                  [MODIFY — add 3 DbSets]
│   │   ├── IKodiVideoDbReader.cs                     [CREATE]
│   │   ├── IKodiImportFileStore.cs                   [CREATE]
│   │   ├── IImportRunCoordinator.cs                  [CREATE]
│   │   └── ITmdbService.cs                           [MODIFY — add FindByExternalIdAsync]
│   └── Models/
│       └── Kodi/
│           ├── KodiLibraryModels.cs                  [CREATE — snapshot + item records]
│           ├── KodiImportCoordinatorModels.cs        [CREATE — handle, parameters, mapping snapshot, stored upload, validation result]
│           └── KodiDbFileName.cs                     [CREATE — static filename→version parser]
└── Features/
    └── KodiImport/
        ├── DTOs/
        │   └── KodiImportDtos.cs                     [CREATE — ImportRunDto, ImportRunDetailDto, ImportCountsDto, ImportItemOutcomeDto, KodiPathMappingDto]
        ├── Commands/
        │   ├── StartKodiImport/
        │   │   ├── StartKodiImportCommandHandler.cs      [CREATE]
        │   │   └── StartKodiImportCommandValidator.cs    [CREATE]
        │   ├── CreateKodiPathMapping/
        │   │   ├── CreateKodiPathMappingCommandHandler.cs[CREATE]
        │   │   └── CreateKodiPathMappingCommandValidator.cs [CREATE]
        │   ├── UpdateKodiPathMapping/
        │   │   ├── UpdateKodiPathMappingCommandHandler.cs[CREATE]
        │   │   └── UpdateKodiPathMappingCommandValidator.cs [CREATE]
        │   └── DeleteKodiPathMapping/
        │       └── DeleteKodiPathMappingCommandHandler.cs[CREATE]
        └── Queries/
            ├── GetKodiImportRun/
            │   └── GetKodiImportRunQueryHandler.cs       [CREATE]
            ├── GetActiveKodiImport/
            │   └── GetActiveKodiImportQueryHandler.cs    [CREATE]
            ├── ListKodiImportHistory/
            │   ├── ListKodiImportHistoryQueryHandler.cs  [CREATE]
            │   └── ListKodiImportHistoryQueryValidator.cs[CREATE]
            ├── ListKodiImportItems/
            │   ├── ListKodiImportItemsQueryHandler.cs    [CREATE]
            │   └── ListKodiImportItemsQueryValidator.cs  [CREATE]
            └── ListKodiPathMappings/
                └── ListKodiPathMappingsQueryHandler.cs   [CREATE]

MediaHandler.Infrastructure/
├── MediaHandler.Infrastructure.csproj                [MODIFY — add Microsoft.Data.Sqlite 10.0.11]
├── Kodi/
│   ├── KodiVideoDbReader.cs                          [CREATE — IKodiVideoDbReader impl]
│   ├── KodiDbQueries.cs                              [CREATE — SQL constants w/ // SOURCE: comments]
│   ├── KodiPathTranslator.cs                         [CREATE — static pure translator]
│   └── KodiImportPipeline.cs                         [CREATE — scoped; mirrors ScanPipeline structure]
├── Services/
│   ├── ImportRunCoordinator.cs                       [CREATE — singleton; mirrors ScanRunCoordinator]
│   └── KodiImportFileStore.cs                        [CREATE — temp upload store w/ size cap]
├── Options/
│   └── KodiImportOptions.cs                          [CREATE]
├── Persistence/
│   ├── MediaHandlerDbContext.cs                      [MODIFY — add 3 DbSets]
│   └── Configurations/
│       ├── ImportRunConfiguration.cs                 [CREATE]
│       ├── ImportItemOutcomeConfiguration.cs         [CREATE]
│       ├── KodiPathMappingConfiguration.cs           [CREATE]
│       └── ReviewItemConfiguration.cs                [MODIFY — map Source]
├── Tmdb/
│   └── TmdbService.cs                                [MODIFY — implement FindByExternalIdAsync]
├── DependencyInjection.cs                            [MODIFY — options + services + recovery method]
└── Migrations/
    └── <timestamp>_AddKodiDbImport.cs               [CREATE via dotnet ef migrations add]

MediaHandler.API/
├── Controllers/
│   ├── AdminKodiImportController.cs                  [CREATE]
│   └── AdminKodiPathMappingsController.cs            [CREATE]
├── Contracts/Admin/
│   └── KodiImportRequests.cs                         [CREATE — mapping upsert + override request records]
└── Program.cs                                        [MODIFY — call ApplyImportRunRecoveryAsync]

MediaHandler.Tests/
├── MediaHandler.Tests.csproj                         [MODIFY — add Microsoft.Data.Sqlite 10.0.11]
├── Common/TestDbContext.cs                           [MODIFY — add 3 DbSets]
├── Kodi/
│   ├── KodiDbFileNameTests.cs                        [CREATE]
│   ├── KodiVideoDbReaderTests.cs                     [CREATE — synthetic SQLite fixtures built in-test]
│   ├── KodiPathTranslatorTests.cs                    [CREATE]
│   ├── KodiImportPipelineTests.cs                    [CREATE — core behavior coverage]
│   └── KodiTestDbBuilder.cs                          [CREATE — test helper writing fixture .db files]
└── Features/KodiImport/
    ├── StartKodiImportCommandHandlerTests.cs         [CREATE]
    ├── KodiPathMappingHandlerTests.cs                [CREATE]
    ├── ListKodiImportHistoryQueryHandlerTests.cs     [CREATE]
    ├── GetKodiImportRunQueryHandlerTests.cs          [CREATE]
    └── ListKodiImportItemsQueryHandlerTests.cs       [CREATE]

MediaHandler.IntegrationTests/
├── MediaHandler.IntegrationTests.csproj              [MODIFY — add Microsoft.Data.Sqlite 10.0.11]
└── KodiImport/
    ├── KodiImportEndToEndTests.cs                    [CREATE]
    ├── KodiImportApiTests.cs                         [CREATE — auth + endpoints over WebApplicationFactory, mirroring Scanner/AdminAuthorizationTests infrastructure]
    └── KodiPathMappingsApiTests.cs                   [CREATE]
```

**Structure Decision**: Standard 4-layer layout, no new projects. The import mirrors the scanner's proven architecture: Application interfaces + Infrastructure implementations + singleton coordinator + run/outcome tables.

---

## Complexity Tracking

> No constitution violations — informational only.

No new projects, no new architectural patterns. Every mechanism reuses an established one: coordinator-with-mutex (`ScanRunCoordinator`), filtered unique index (`ScanRun.Status`), denormalized counters (`ScanRun`), per-item audit rows (`ScanItemDecision`), options pattern (`TmdbOptions`/`NasOptions`), review queue (`ReviewItem`), ambiguity policy (`TmdbMatcher`).

---

## Phase 0: Research — Key Findings

All findings verified against the current code (not the 007 plan):

| Question | Resolution |
|----------|-----------|
| SQLite ADO.NET provider | **None referenced today.** Add `Microsoft.Data.Sqlite` 10.0.11 (aligns with `Microsoft.EntityFrameworkCore` 10.0.11) to Infrastructure, Tests, IntegrationTests. Chosen over `System.Data.SQLite` (Microsoft-maintained, already the EF-adjacent default, no native packaging concerns). |
| Background execution pattern | `ScanRunCoordinator`: singleton, `SemaphoreSlim` mutex + filtered unique index on `Status='Running'`, fire-and-forget task with own `IServiceScopeFactory` scope, batched counter saves. **Mirror it exactly** as `ImportRunCoordinator`. |
| Run/history/detail API pattern | `AdminScanController` (POST→202 + poll `GET {id}`, `GET ""` history, `GET {id}/decisions` paged) and `AdminEnrichmentController` (`history`, `{runId}/details`) — import endpoints mirror both. |
| Enrichment trigger | `StartEnrichmentCommandHandler` (inserts Pending `EnrichmentRun` + fires `IEnrichmentCoordinator.StartAsync`); `EnrichmentCoordinator` eligibility is `Overview IS NULL OR UpdatedAt > lastCompleted` — import-created `Media` rows (`Overview` null) are **automatically eligible**, so no enrichment-code changes are needed. The import coordinator re-triggers the same command on successful run completion (§1.6). `ENRICHMENT_ALREADY_RUNNING` / zero-eligible outcomes are benign skips. |
| Identity ambiguity policy | `TmdbMatcher` (scoped, per-run `ConcurrentDictionary` cache): 5% popularity-gap ambiguity, ±1-year tolerance, transient `HttpRequestException` → NeedsReview. **Reused as-is** for the title-search leg only. Direct Kodi TMDB ids and external-id lookups bypass it (see §1.4). |
| Linking logic from 007 | `LinkMediaFileCommandHandler` sets `MediaFile.MediaId` only, with idempotent same-media and `FILE_ALREADY_LINKED` different-media semantics. **Semantics are reused; the command itself is not called** — the pipeline works on pre-loaded tracked entities in bulk (10k files; per-item MediatR calls would be an N+1 anti-pattern). **Discrepancy noted**: no production code today writes `EpisodeFileLink` or `StackGroup` rows (both are test-only constructs so far); this feature is their first writer — see §13. |
| Review queue reuse | `ReviewItem` keyed by `FilePath` with filtered unique index on `(FilePath, Status='Open')`. Import uses the Kodi file URI as `FilePath` (never collides with NAS paths) and a new `Source = KodiImport` discriminator. The scanner's "resolved review item is honored on next pass" flow is mirrored (§1.4 step 0). |
| Path comparison semantics | Scanner: `StringComparer.OrdinalIgnoreCase` dictionaries over `MediaFile.FilePath`; separators normalized `\`→`/`. Translator produces normalized NAS-path candidates; matching is OrdinalIgnoreCase dictionary lookup (§1.3). |
| Season/episode merge | `TvSeason` unique on `(MediaId, SeasonNumber)`; `TvEpisode` unique on `(SeasonId, EpisodeNumber)`; enrichment upserts by those keys and overwrites `Name`. Import materializes the same keys with placeholder names → enrichment merges without duplication (FR-010). |
| Review resolution flow | `ResolveReviewItemCommand` Assign stores `ResolvedTmdbId/ResolvedKind`; scanner honors resolved items on its next pass. Import does the same (§1.4 step 0), which closes the loop for import-originated items. |
| Upload size transport | Kestrel default request body limit is 30 MB < 100 MB default app limit. Action gets `[RequestSizeLimit(524_288_000)]` + `[RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]` (500 MB hard transport ceiling); the configurable limit is enforced by the file store with a clean 400. |
| Scan-vs-spec divergence | `ScanRunCoordinator` has a Pending→Running race window (row inserted as Pending inside the mutex, set Running in the background task). The new coordinator closes it: insert Pending **and** transition to Running **before releasing the mutex**; the filtered unique index remains the DB backstop (see §13, D6). |

---

## Phase 1: Design & Contracts

### 1.1 Domain Model

#### New enums

```csharp
public enum KodiImportMode { Import, Preview }

public enum ImportRunStatus { Pending, Running, Completed, Failed }   // no Cancelled — no cancel endpoint in scope

public enum KodiItemKind { Movie, TvShow, Episode, MusicVideo }

// Item-level outcome
public enum ImportItemStatus
{
    Created,                // new Media (or TvSeason/TvEpisode) row created
    Reused,                 // existing (Type, TmdbId) Media associated (first time this Kodi item is seen)
    Unchanged,              // item was present in the baseline run; nothing changed
    NeedsReview,            // identity unresolved → ReviewItem created/reused
    RequiresIdentityLookup, // PREVIEW ONLY: would need provider traffic
    IdentityLookupFailed,   // transient provider failure — retry on next run
    Conflict,               // identity discrepancy discovered via existing file link (FR-022)
    SkippedMusicVideo,      // music-video row ignored (FR-011)
    NoLongerInKodi          // synthesized row: present in baseline run, absent from this upload (FR-021)
}

// File-link outcome (nullable — null when no link was attempted/applicable)
public enum ImportLinkStatus
{
    Linked,              // ≥1 new link created, no part missing
    AlreadyLinked,       // all required links already present
    PartiallyLinked,     // stack: some parts linked, ≥1 part missing/unmatched (reason names it)
    UnmatchedPath,       // no mapping covers the Kodi prefix
    NoScannedFile,       // translated OK but scanner has no such file
    UnsupportedLocation, // pvr://, http://, upnp://, … non-filesystem scheme
    Conflict             // file already linked to a different Media — preserved, never stolen (FR-017)
}

public enum ReviewItemSource { Scan, KodiImport }
```

#### `ImportRun : BaseEntity` (new)

Mirrors `ScanRun` (required props, denormalized counters, lifecycle).

| Property | Type | Notes |
|----------|------|-------|
| `Mode` | `KodiImportMode` (required) | string conversion |
| `Status` | `ImportRunStatus` (required) | string conversion; filtered unique index `= 'Running'` |
| `SourceFileName` | `string` (required) | e.g. `MyVideos121.db` — kept permanently (audit) |
| `SchemaVersion` | `int` (required) | parsed from file name |
| `UploadedFilePath` | `string?` | temp path; **nulled when the file is discarded** at terminal state |
| `PathMappingsJson` | `string`, default `"[]"` | snapshot of the effective ordered mappings (run is self-contained, mirrors `ScanRun.LibraryRootIdsJson`) |
| `UnmatchedPrefixesJson` | `string`, default `"[]"` | distinct uncovered Kodi directory prefixes (max 100; US2-AC3) |
| `StartedAt` / `FinishedAt` / `FailureReason` | as `ScanRun` | index on `StartedAt` |
| Counters (all `int`, default 0) | `TotalItems, MoviesCreated, ShowsCreated, EpisodesCreated, ItemsReused, ItemsUnchanged, FilesLinked, UnmatchedPaths, NoScannedFiles, UnsupportedLocations, Conflicts, NoLongerInKodi, NeedsReview, IdentityLookupFailures, SkippedMusicVideos` | updated in batches during the run; satisfy FR-026 |
| `Outcomes` | `ICollection<ImportItemOutcome>` | cascade delete |

**Counter reconciliation rule (SC-008)** — counters are pure functions of the outcome rows; the pipeline increments a counters struct per item (same source), and the report endpoint may always re-derive: `TotalItems` = outcome rows where `Outcome != NoLongerInKodi`; `FilesLinked` = `Σ LinkedFileCount`; `Conflicts` = rows with `Outcome = Conflict` **or** `LinkOutcome = Conflict`; the rest are per-`Outcome`/per-`LinkOutcome` counts. Music videos produce exactly one `SkippedMusicVideo` row each, so nothing is unaccounted for.

#### `ImportItemOutcome : BaseEntity` (new)

| Property | Type | Notes |
|----------|------|-------|
| `ImportRunId` | `Guid` (required, FK cascade) | |
| `KodiItemKind` | `KodiItemKind` (required) | string conversion |
| `KodiItemId` | `int` (required) | `idMovie` / `idShow` / `idEpisode` / `idMVideo` |
| `Title` | `string` (required, 500) | Kodi title (baseline title for `NoLongerInKodi`) |
| `MediaKind` | `MediaType?` | null for music videos |
| `Outcome` | `ImportItemStatus` (required) | |
| `LinkOutcome` | `ImportLinkStatus?` | |
| `LinkedFileCount` | `int` | files newly linked for this item |
| `Reason` | `string?` (1000) | human-readable, required for non-success outcomes |
| `KodiPathPrefix` | `string?` (500) | normalized Kodi **directory** URI involved in link failure |
| `MediaId` / `MediaFileId` | `Guid?` | resolved entry / primary file, when applicable |

Indexes: `(ImportRunId)`, `(ImportRunId, Outcome)`, `(KodiItemKind, KodiItemId)` (baseline lookups).

#### `KodiPathMapping : BaseEntity` (new)

`KodiPrefix` (required, 500, unique index), `NasPrefix` (required, 500), `SortOrder` (int; evaluation order = `SortOrder` ascending, ties by `CreatedAt`). Prefixes are normalized on write (§1.3) so matching is a plain OrdinalIgnoreCase prefix test.

#### `ReviewItem` (modify)

Add `public ReviewItemSource Source { get; set; } = ReviewItemSource.Scan;` — mapped string, required, default `'Scan'` (migration backfills existing rows). No DTO/query changes required (all DTO construction is manual and additive-safe); exposing `Source` in the review API is **not** in scope.

---

### 1.2 Kodi DB Reading Layer (`MediaHandler.Infrastructure/Kodi/`)

#### `IKodiVideoDbReader` (Application interface)

```csharp
Task<KodiDbValidationResult> ValidateAsync(string filePath, int schemaVersion, CancellationToken ct = default);
Task<KodiLibrarySnapshot> ReadAsync(string filePath, int schemaVersion, CancellationToken ct = default);
```

`KodiDbValidationResult(bool IsValid, string? ErrorCode, string? ErrorMessage)` where `ErrorCode ∈ { "UNSUPPORTED_VERSION", "INVALID_KODI_DB" }`.

**`KodiVideoDbReader` implementation rules (FR-031 untrusted input):**
- Connection string built as `Data Source=<path>;Mode=ReadOnly` (Microsoft.Data.Sqlite never writes; additionally the file is opened after the store's size cap — no in-memory load of the whole file).
- Only hardcoded `SELECT` statements from `KodiDbQueries`; no dynamic SQL, no ATTACH, no execution of file contents.
- Streaming via `SqliteDataReader` (no `DataTable`); all columns read with `IsDBNull` guards; `CommandTimeout` set defensively (e.g. 60 s).
- `ValidateAsync` steps: (1) `schemaVersion ∈ options.SupportedSchemaVersions` → else `UNSUPPORTED_VERSION` naming the detected version and the supported set; (2) open + `SELECT name FROM sqlite_master …` → required tables `movie, tvshow, episode, files, path, uniqueid` must exist → else `INVALID_KODI_DB` ("not a Kodi video database" — this also rejects renamed music DBs); (3) `PRAGMA table_info(...)` check that every column the queries reference exists → else `INVALID_KODI_DB` with the offending table named. Any `SqliteException` from open/read (corrupt, truncated, locked-copy garbage) → `INVALID_KODI_DB` with guidance "close Kodi before copying the file". Non-SQLite files fail at step 2 with "not a SQLite database".
- `ReadAsync` returns a `KodiLibrarySnapshot` (see below). Per-version differences: `KodiDbQueries.ForVersion(int version)` returns the query set; today one canonical set covers 119/121/131 for the used concepts (the c-columns used have been stable across Kodi 19/20/21 per the public schema documentation); the method is the single extension point if a divergence is found. **Exact column names must be re-verified against the public Kodi wiki "Databases" page during implementation — the fixture tests (§Phase 2) encode them.**

#### `KodiDbQueries` (static, SQL constants — every constant carries `// SOURCE:`)

Canonical joins (concept level; `// SOURCE: Kodi wiki – Databases` on each):

| Concept | Query shape |
|---------|-------------|
| Movies | `SELECT m.idMovie, m.c00, m.c07 /* year */, f.strFilename, p.strPath FROM movie m JOIN files f ON f.idFile = m.idFile JOIN path p ON p.idPath = f.idPath` (+ original-title column read when present per version; tolerate absence → null) |
| TV shows | `SELECT s.idShow, s.c00, s.c05 /* premiered */ FROM tvshow s` (year = parsed from premiered prefix) |
| Episodes | `SELECT e.idEpisode, e.idShow, e.c00, e.c12 /* season */, e.c13 /* episode */, f.strFilename, p.strPath FROM episode e JOIN files f ON f.idFile = e.idFile JOIN path p ON p.idPath = f.idPath` |
| External ids | `SELECT media_id, media_type, type, value FROM uniqueid WHERE media_type IN ('movie','tvshow')` — episode uniqueids are ignored (identity is resolved at show level only) |
| Music videos | `SELECT idMVideo, c00 FROM musicvideo` (for skip counting/report rows only) |

Notes:
- Show↔path link tables are **not** needed: linking happens per file (movies) and per episode file (shows); shows themselves have no file reference (US2-AC2).
- The reader returns **raw** strings (no decoding) — normalization is the translator's job (single responsibility, matches scanner layering).
- The Kodi `art` table (poster/fanart/thumb URLs or local cache paths) is **intentionally not read** — user decision: artwork is populated by the app's existing TMDB enrichment (`Media.PosterPath`/`BackdropPath`, season posters, episode stills), not imported from Kodi. The reference document `Revision 1/Anaylisis.md` includes `art` joins in its extraction SQL — do not replicate them.

#### `stack://` expansion (in the reader)

When a movie's `strFilename` starts with `stack://`: strip the prefix and split the remainder into full part URIs (Kodi joins parts with `" , "`; each part is itself a complete URI such as `smb://server/share/Movie CD1.avi`).  
`// SOURCE: Kodi wiki – File stacking (stack:// URI format)`  
Non-stacked items produce a single file ref = `strPath` + `strFilename`.

#### Read models (`Application/Common/Models/Kodi/KodiLibraryModels.cs`)

```csharp
public record KodiExternalId(string Provider, string Value);      // Provider normalized: "tmdb" | "imdb" | "tvdb" | other
public record KodiMovieItem(int KodiMovieId, string Title, string? OriginalTitle, int? Year,
    IReadOnlyList<KodiExternalId> ExternalIds, IReadOnlyList<string> FileRefs);   // FileRefs = expanded stack parts, in order
public record KodiEpisodeItem(int KodiEpisodeId, int SeasonNumber, int EpisodeNumber, string? Title, string FileRef);
public record KodiShowItem(int KodiShowId, string Title, int? Year,
    IReadOnlyList<KodiExternalId> ExternalIds, IReadOnlyList<KodiEpisodeItem> Episodes);
public record KodiMusicVideoItem(int KodiMusicVideoId, string Title);
public record KodiLibrarySnapshot(IReadOnlyList<KodiMovieItem> Movies,
    IReadOnlyList<KodiShowItem> Shows, IReadOnlyList<KodiMusicVideoItem> MusicVideos);
```

#### `KodiDbFileName` (Application static helper)

`TryParseVersion(string? fileName, out int version)` — regex `^MyVideos(?<v>\d+)\.db$`, `RegexOptions.IgnoreCase | Compiled`.  
`// SOURCE: Kodi wiki – Databases (video DB file naming: MyVideos<version>.db in userdata/Database)`  
No match → `INVALID_FILE_NAME` error with guidance to keep the original file name (covers `MyMusic*.db` uploads and browser-renamed copies).

---

### 1.3 Path Translation (`KodiPathTranslator` — static, pure, Infrastructure/Kodi)

```csharp
public enum PathTranslationKind { Translated, NoMapping, UnsupportedScheme }
public record PathTranslation(PathTranslationKind Kind, string? TranslatedPath, string? KodiDirectoryPrefix);
public static PathTranslation Translate(string kodiFileUri, IReadOnlyList<KodiPathMappingSnapshot> mappings);
```

Algorithm (all steps documented constants):
1. **Scheme gate**: extract scheme (text before `://`). Schemes `smb`, `nfs`, `file`, and scheme-less absolute paths proceed; anything else (`pvr`, `http`, `https`, `upnp`, `plugin`, …) → `UnsupportedScheme`.  
   `// SOURCE: Kodi wiki – Databases / observed Kodi strPath formats; non-filesystem protocols documented in spec §Edge Cases`
2. **Normalize**: percent-decode once (`Uri.UnescapeDataString`), `\` → `/`, collapse duplicate slashes, trim trailing slash.
3. **Match**: first mapping whose normalized `KodiPrefix` is an OrdinalIgnoreCase prefix of the normalized URI wins (mappings arrive pre-ordered; per-upload overrides are prepended by the handler, so they win on ties).
4. **Rewrite**: replace matched prefix with normalized `NasPrefix`, joining on exactly one `/`. Result preserves the remainder's case; case-insensitivity is applied at match time (OrdinalIgnoreCase dictionary over `MediaFile.FilePath`, scanner convention).
5. No match → `NoMapping` with `KodiDirectoryPrefix` = normalized directory portion of the URI (the actionable prefix for the report).
Mapping write-time normalization (create/update handlers): same percent-decode + separator + trailing-slash rules, so stored prefixes are directly comparable. `NasPrefix` must start with `/` (validator).

---

### 1.4 Identity Resolution & Dedupe (inside `KodiImportPipeline`)

Precedence chain (FR-006), evaluated **per movie / per show** (episodes never resolve independently):

- **Step 0 — saved admin resolution**: if an open-review avoidance map lookup fails below, first check the pre-loaded `Resolved` `ReviewItem`s with `Source = KodiImport` keyed by the item's Kodi URI; a hit supplies `(ResolvedTmdbId, ResolvedKind)` directly (mirrors the scanner's resolved-review reuse). No provider call.
- **Step 1 — Kodi TMDB id** (`uniqueid type='tmdb'`): use it **directly, no provider call** (US1-AC2; this is what makes SC-006 feasible). `Kind` = Kodi item kind.
- **Step 2 — non-TMDB external id** (`imdb` → `imdb_id`, `tvdb` → `tvdb_id`; other providers ignored): `ITmdbService.FindByExternalIdAsync(externalId, source, kindHint)`. `null` → title search fallback (step 3). `HttpRequestException` → outcome `IdentityLookupFailed` (transient; run continues — provider-outage edge case).
- **Step 3 — title(+year) search**: build `MatchQuery(title, year, kindHint, NfoTmdbId: null, ExplicitTokenId: null, Language: "en-US", SearchLanguages: <from Scanner:DefaultSearchLanguages config, same as ScanPipeline>)` and call the existing `ITmdbMatcher.ResolveAsync` — inheriting the 5% ambiguity policy, ±1-year tolerance, per-run cache, and transient-failure→NeedsReview behavior for free. `NeedsReview` → review-queue path.

**Review-queue path** (FR-006 tail): no `Media` created. Create one `ReviewItem` { FilePath = item's Kodi URI, Reason = `NoTmdbResult|MultipleCandidates|YearMismatch`, Status = Open, ParsedTitle/ParsedYear from Kodi, CandidatesJson from matcher, Source = KodiImport } unless an open item already exists for that URI (pre-loaded set + filtered unique index backstop). Outcome `NeedsReview`, counter incremented.

**Dedupe (FR-007, SC-003)**: run-local `Dictionary<(MediaType Type, int TmdbId), Media>` seeded once from `db.Medias` (tracked) at pipeline start. Lookups/inserts go through this dictionary; single-active-run invariant makes races impossible. If the **database already contains** duplicate `(Type, TmdbId)` rows (possible today — no unique constraint), the first row wins and a warning is logged; a third is never created (see §13, R2). Two Kodi items sharing one TMDB identity inside the same upload (duplicate-editions edge case): second item reuses the dictionary entry, both files are linked, and its outcome `Reason` carries the informational note "duplicate TMDB identity within Kodi".

**New `Media` population (FR-008)**: `{ TmdbId, Type, Title = Kodi title, OriginalTitle = Kodi original title, Year = Kodi year }` — everything else left null for enrichment (which is triggered automatically at run completion — see §1.6 "Post-import enrichment trigger"). **Never modify pre-existing entries (FR-009).**

**Show structure (FR-005/FR-010)**: find-or-create `TvSeason (MediaId, SeasonNumber)` with `Name = $"Season {n}"` and `TvEpisode (SeasonId, EpisodeNumber)` with `Name = Kodi episode title ?? $"Episode {n}"` — **set names only on creation** (enrichment owns them afterwards; it upserts by the same keys, so no duplication). Season 0 materialized like any other.

**Seen-before determination** (drives `Unchanged` vs `Reused`): baseline = the most recent `Completed` run with `Mode = Import`. Its outcome rows (minus `MusicVideo` kind) are loaded at pipeline start as `(KodiItemKind, KodiItemId) → Title`. Item in baseline → `Unchanged` (unless links changed — still `Unchanged`, link counters carry the delta); not in baseline → `Created`/`Reused`. Failed runs never become baselines (their outcome list may be partial).

---

### 1.5 Linking (FR-014…FR-018)

Pre-load **all** `MediaFile` rows once (tracked) into `Dictionary<string, MediaFile>(OrdinalIgnoreCase)` keyed by `FilePath` (~10k rows — same scale as the scanner's per-root pre-load). Missing-marked files (`MissingSince != null`) are linkable like any other (spec edge case — no special handling).

**Deterministic processing order**: music videos (skip rows) → movies (by `KodiMovieId`) → shows (by `KodiShowId`, episodes ordered by season/episode). This defines "first" for the Kodi-internal-duplicate rule: a file claimed as both movie and episode keeps the **movie** link; the episode attempt is reported `Conflict` (spec edge case).

**Movie linking** (per expanded part ref, in part order):
- `UnsupportedScheme`/`NoMapping`/no scanned file → collect for `LinkOutcome` + `Reason` (+ `KodiPathPrefix`).
- File found, `MediaId == null` → set `MediaId = media.Id`. For stacks (>1 ref): find-or-create the media's `StackGroup`; set `part.StackGroupId`; `Role` = `Main` for part 0, `StackedPart` for the rest (mirrors scanner role semantics).
- `MediaId == media.Id` → already linked (idempotent).
- `MediaId` = other media → **preserve, never steal**; part reported `Conflict` (FR-017, SC-004).
- Aggregate: all parts linked → `Linked` (or `AlreadyLinked`); ≥1 linked and ≥1 missing → `PartiallyLinked` with reason naming missing parts; none → first failure kind in precedence `UnmatchedPath` → `UnsupportedLocation` → `NoScannedFile`. `LinkedFileCount` = newly linked parts.

**Episode linking** (single file ref per episode; multi-episode files = several episode rows sharing one file entry):
- File found, `MediaId == null` → set `MediaId = show.Media.Id` **and** create `EpisodeFileLink { TvEpisodeId, MediaFileId, OrderInFile }` where `OrderInFile` = 1-based position of the episode among this run's episodes sharing the same file ref, ordered by (season, episode) (US2-AC6).
- `MediaId == show.Id` → ensure the link row exists (a 007-style manual link may have set `MediaId` without a link row): create if missing (counts as `Linked`), else `AlreadyLinked`.
- `MediaId` = other media, **or** an existing `EpisodeFileLink` on that file points to an episode of another show (defensive) → `Conflict`, preserve.
- File not found / unmapped / unsupported → per-episode `LinkOutcome` + reason.

**Identity discrepancy via existing links (US3-AC3, FR-022)**: before creating/reusing, if the item's **primary file** exists and is linked to entry `A`: resolve the Kodi identity, then compare `(A.Type, A.TmdbId)`. Equal → `Reused`/`Unchanged` + ensure remaining links. Different → item outcome `Conflict`, reason naming both identities, **no new entry created, no link changes**. (Items with no usable file link fall through to normal identity processing — the accepted re-identification limitation, decision 8.)

---

### 1.6 Execution Model

**`IImportRunCoordinator`** (Application) — deliberately smaller than `IScanRunCoordinator` (no cancel, no progress channel — FR-025 polling reads the run row, whose counters are persisted in batches):

```csharp
Task<KodiImportRunHandle> StartAsync(KodiImportStartParameters parameters, CancellationToken ct = default);
// throws InvalidOperationException("IMPORT_IN_PROGRESS") when a run is active
```

`KodiImportStartParameters(Guid ImportRunId, string StoredFilePath, string SourceFileName, int SchemaVersion, KodiImportMode Mode, IReadOnlyList<KodiPathMappingSnapshot> Mappings)`; `KodiImportRunHandle(Guid ImportRunId)`; `KodiPathMappingSnapshot(string KodiPrefix, string NasPrefix)` (pre-normalized, ordered).

**`ImportRunCoordinator`** (Infrastructure/Services, singleton; mirrors `ScanRunCoordinator`):
- `StartAsync`: mutex → DB check for `Status == Running` → insert run row (`Pending`, then immediately `Running` + save, **all inside the mutex** — closes the race the scan coordinator has; filtered unique index remains backstop: `DbUpdateException` on it → `IMPORT_IN_PROGRESS`) → serialize mappings into `PathMappingsJson` → fire-and-forget `ExecuteImportAsync` → return handle.
- `ExecuteImportAsync`: own DI scope → reload run tracked → `KodiImportPipeline.ExecuteAsync(run, parameters, ct)` → `Completed`; `catch (Exception)` → `Failed` + `FailureReason` (crash-recovery and validation already handled upstream); `finally`: save with `CancellationToken.None`, then **discard the uploaded file** via `IKodiImportFileStore.Delete` (best-effort, log on failure), null `UploadedFilePath`, save again (decision 7). A process crash between the two saves leaves an orphan file → startup purge (below).

**Startup recovery** — new `ApplyImportRunRecoveryAsync(IServiceProvider)` in `DependencyInjection.cs`, called from `Program.cs` right after `ApplyScanRunRecoveryAsync`: (1) `Pending`/`Running` import runs → `Failed` with `"Process restarted before import finished"`; (2) delete any still-referenced `UploadedFilePath`s; (3) purge **all** files in `KodiImportOptions.TempDirectory` (no legitimate file can exist at startup). Log counts.

**`IKodiImportFileStore`** (Application) / **`KodiImportFileStore`** (Infrastructure/Services, scoped):
- `Task<Result<StoredUpload>> SaveAsync(Stream content, string fileName, long declaredLength, CancellationToken ct)` — `StoredUpload(string FilePath, long SizeBytes)`. Checks `declaredLength` against `options.MaxUploadSizeBytes` up front, then streams to `<TempDirectory>/kodi-import-{guid}.db` in 80 KB chunks, aborting + deleting the partial file and returning `Result.Fail("UPLOAD_TOO_LARGE: …limit…")` the moment the cap is exceeded. Never buffers the whole file.
- `void Delete(string? filePath)` — best-effort. `void PurgeOrphans()` — used by startup recovery.

**`KodiImportPipeline`** (Infrastructure/Kodi, scoped; structure mirrors `ScanPipeline`): phases = load state (§1.4/§1.5 pre-loads) → process music videos/movies/shows+episodes (§1.5 order) → synthesize `NoLongerInKodi` rows from baseline diff → persist counters. Batched `SaveChangesAsync` every 50 items (counters + outcome rows), mirroring the scanner's batching.

**Preview mode** (FR-028): same pipeline, `Mode = Preview`:
- Loads the same state read-only; **never calls `ITmdbService`** — items that would need step 2/3 resolution get `RequiresIdentityLookup` (US5-AC4). Step 0/1 (saved resolutions, direct TMDB ids) still project normally.
- Writes **only** `ImportRun` + `ImportItemOutcome` rows — no Media/season/episode/link/review writes (US5-AC1). Domain pre-loads use `AsNoTracking`; in-run "would-link" state is tracked in local `HashSet<Guid>`s so two items claiming one file project a conflict correctly.
- `NoLongerInKodi` projection is computed too (read-only diff).
- Preview runs share the single-active-run guard (they are `ImportRun`s; one import *or* preview at a time) and appear in history with `Mode = Preview`. They never become the seen-before baseline (baseline query filters `Mode = Import`).

**Post-import enrichment trigger** (closes the "imported entries never get TMDB metadata" gap — imported `Media` rows are created with `Overview = null`, and the existing `EnrichmentCoordinator` eligibility rule `Overview IS NULL OR UpdatedAt > lastCompleted` makes them automatically eligible; no changes to any enrichment class):
- **Hook location**: `ImportRunCoordinator.ExecuteImportAsync`, after the pipeline has completed and the run row is persisted `Completed`. Skipped entirely for `Mode = Preview` runs (no domain writes → nothing to enrich) and for `Failed` runs.
- **Mechanism reused**: the exact same entry point as `AdminEnrichmentController` — resolve `ISender` from the run's DI scope and `Send(new StartEnrichmentCommand())`. The existing `StartEnrichmentCommandHandler` performs the already-running check, the eligible-count query, the Pending `EnrichmentRun` insert, and `IEnrichmentCoordinator.StartAsync`. MediatR is registered application-wide and resolvable from any scope; Infrastructure → Application references are allowed by the layer rule, so no refactor or new enrichment logic is required.
- **Result handling**: `WasStarted = false` (nothing eligible) and `Result.Fail` with `ENRICHMENT_ALREADY_RUNNING` are both **benign** — log at Information level and continue. (An already-running enrichment won't pick up the just-imported entries, but they stay `Overview = null` and are collected by the next enrichment run.)
- **Failure semantics**: the whole trigger is wrapped in `try/catch (Exception)` → log at Error, never rethrow. The import run's terminal state and the uploaded-file discard in the `finally` block are unaffected — an enrichment-trigger failure can never fail the import run.

**Upload + start flow** (`StartKodiImportCommandHandler`; validator covers presence/emptiness/`Mode` enum/override shape):
1. `KodiDbFileName.TryParseVersion` → `INVALID_FILE_NAME`.
2. `fileStore.SaveAsync` (size cap inside) → `UPLOAD_TOO_LARGE`.
3. `reader.ValidateAsync` → `UNSUPPORTED_VERSION` / `INVALID_KODI_DB`; on any failure → `fileStore.Delete` (FR-004: zero side effects, no run row).
4. Load persisted mappings (ordered) → merge: normalized overrides first, then persisted mappings whose normalized `KodiPrefix` isn't shadowed by an override (decision 6).
5. Single-active check (`db.ImportRuns … Status == Running`) → `IMPORT_IN_PROGRESS`.
6. `coordinator.StartAsync(...)`; catch the `InvalidOperationException` race → `IMPORT_IN_PROGRESS`.

---

### 1.7 Application Features — Handler Summary

| Handler | Type | Returns | Key logic |
|---------|------|---------|-----------|
| `StartKodiImportCommand(string FileName, long DeclaredLengthBytes, Stream Content, KodiImportMode Mode, IReadOnlyList<KodiPathMappingSnapshot>? Overrides)` | Command | `Result<KodiImportRunHandle>` | §1.6 flow |
| `CreateKodiPathMappingCommand(string KodiPrefix, string NasPrefix, int? SortOrder)` | Command | `Result<KodiPathMappingDto>` | normalize prefixes; default `SortOrder = max+1`; duplicate → `DUPLICATE_MAPPING` |
| `UpdateKodiPathMappingCommand(Guid Id, string KodiPrefix, string NasPrefix, int SortOrder)` | Command | `Result<KodiPathMappingDto>` | `NOT_FOUND`; duplicate-prefix check excluding self |
| `DeleteKodiPathMappingCommand(Guid Id)` | Command | `Result<Unit>` | `NOT_FOUND` |
| `GetKodiImportRunQuery(Guid Id)` | Query | `Result<ImportRunDetailDto>` | `NOT_FOUND`; deserializes prefix/mapping JSON |
| `GetActiveKodiImportQuery()` | Query | `Result<ImportRunDto?>` | null when none (mirrors `GetActiveScanQuery`) |
| `ListKodiImportHistoryQuery(int Page = 1, int PageSize = 20)` | Query | `Result<PagedResult<ImportRunDto>>` | `AsNoTracking`, `OrderByDescending(StartedAt)`, server-side paging (FR-027) |
| `ListKodiImportItemsQuery(Guid RunId, ImportItemStatus? Outcome, KodiItemKind? Kind, int Page = 1, int PageSize = 50)` | Query | `Result<PagedResult<ImportItemOutcomeDto>>` | `NOT_FOUND` run; filters; ordered by `CreatedAt, Id` |
| `ListKodiPathMappingsQuery()` | Query | `Result<IReadOnlyList<KodiPathMappingDto>>` | ordered by `SortOrder` |

Validators (separate files): `StartKodiImportCommandValidator` (FileName required; DeclaredLengthBytes > 0 — empty upload rejection per FR-002; `Mode` in enum; overrides' prefixes non-empty), `CreateKodiPathMappingCommandValidator` / `UpdateKodiPathMappingCommandValidator` (prefixes required ≤ 500; `NasPrefix` must start with `/`; `SortOrder ≥ 0`), `ListKodiImportHistoryQueryValidator` / `ListKodiImportItemsQueryValidator` (Page ≥ 1, PageSize 1–100 — mirror `ListScanHistoryQueryValidator`).

### 1.8 DTOs (`Features/KodiImport/DTOs/KodiImportDtos.cs`)

```csharp
public record ImportCountsDto(int TotalItems, int MoviesCreated, int ShowsCreated, int EpisodesCreated,
    int ItemsReused, int ItemsUnchanged, int FilesLinked, int UnmatchedPaths, int NoScannedFiles,
    int UnsupportedLocations, int Conflicts, int NoLongerInKodi, int NeedsReview,
    int IdentityLookupFailures, int SkippedMusicVideos);

public record ImportRunDto(Guid Id, KodiImportMode Mode, ImportRunStatus Status, string SourceFileName,
    int SchemaVersion, DateTime StartedAt, DateTime? FinishedAt, string? FailureReason, ImportCountsDto Counts);

public record ImportRunDetailDto(Guid Id, KodiImportMode Mode, ImportRunStatus Status, string SourceFileName,
    int SchemaVersion, DateTime StartedAt, DateTime? FinishedAt, string? FailureReason, ImportCountsDto Counts,
    IReadOnlyList<string> UnmatchedPrefixes);

public record ImportItemOutcomeDto(Guid Id, KodiItemKind ItemKind, int KodiItemId, string Title,
    MediaType? MediaKind, ImportItemStatus Outcome, ImportLinkStatus? LinkOutcome, int LinkedFileCount,
    string? Reason, string? KodiPathPrefix, Guid? MediaId);

public record KodiPathMappingDto(Guid Id, string KodiPrefix, string NasPrefix, int SortOrder);
```

---

### 1.9 EF Core — Configurations & Migration

- **`ImportRunConfiguration`**: key; `Mode`/`Status` string conversions; `SourceFileName` required max 500; `UploadedFilePath` max 1000; `FailureReason` max 2000; `PathMappingsJson`/`UnmatchedPrefixesJson` required default `"[]"`; `HasIndex(StartedAt)`; **filtered unique index `HasIndex(Status).HasFilter("[Status] = 'Running'").IsUnique()`** (mirrors `ScanRunConfiguration`); `Outcomes` cascade delete.
- **`ImportItemOutcomeConfiguration`**: key; enum string conversions; `Title` required 500, `Reason` 1000, `KodiPathPrefix` 500; FK cascade; indexes `(ImportRunId)`, `(ImportRunId, Outcome)`, `(KodiItemKind, KodiItemId)`.
- **`KodiPathMappingConfiguration`**: key; prefixes required 500; unique index on `KodiPrefix`; index on `SortOrder`.
- **`ReviewItemConfiguration`** (modify): `Source` string conversion, required, `HasDefaultValue(ReviewItemSource.Scan)`.
- **DbSets** added to `IApplicationDbContext`, `MediaHandlerDbContext`, `TestDbContext`: `ImportRuns`, `ImportItemOutcomes`, `KodiPathMappings`.

**Migration**: `AddKodiDbImport` — creates `ImportRuns`, `ImportItemOutcomes`, `KodiPathMappings`; adds `ReviewItems.Source` (`nvarchar`, default `'Scan'`, backfills existing rows).
`dotnet ef migrations add AddKodiDbImport --project MediaHandler.Infrastructure --startup-project MediaHandler.API`

---

### 1.10 API Surface

**`AdminKodiImportController`** — `[ApiController] [Route("api/v1/admin/kodi-import")] [Authorize(Policy = "AdminOnly")] [EnableRateLimiting("fixed")]`, primary-ctor `ISender`.

| Action | Verb/Route | Success | Error codes |
|--------|-----------|---------|-------------|
| `StartImport` | `POST ""` multipart: `file` (IFormFile, required), `mode` (`"import"\|"preview"`, default import), `overrides` (optional JSON array `[{kodiPrefix, nasPrefix}]`) | **202** `ApiResponse<ImportRunDto>` (re-read via `GetKodiImportRunQuery`, mirrors `AdminScanController.StartScan`) | 400 `VALIDATION_ERROR` / `INVALID_FILE_NAME` / `UNSUPPORTED_VERSION` / `UPLOAD_TOO_LARGE` / `INVALID_KODI_DB`; **409** `IMPORT_IN_PROGRESS`; 401/403 |
| `ListHistory` | `GET ""` `?page=&pageSize=` | 200 `ApiResponse<IReadOnlyList<ImportRunDto>>` + meta | 400; 401/403 |
| `GetActive` | `GET "active"` | 200 (null data when idle) | 401/403 |
| `GetRun` | `GET "{id:guid}"` | 200 `ApiResponse<ImportRunDetailDto>` | 404 `NOT_FOUND`; 401/403 |
| `ListItems` | `GET "{id:guid}/items"` `?outcome=&kind=&page=&pageSize=` | 200 `ApiResponse<IReadOnlyList<ImportItemOutcomeDto>>` + meta | 404; 400; 401/403 |

`StartImport` action attributes: `[HttpPost("")] [RequestSizeLimit(524_288_000)] [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)] [Consumes("multipart/form-data")]`. The controller: parses `mode` (invalid → 400 `VALIDATION_ERROR`), deserializes `overrides` JSON (malformed → 400), maps `IFormFile` → command `(file.FileName, file.Length, file.OpenReadStream(), …)`. Full `[ProducesResponseType]` coverage per action per conventions.

**`AdminKodiPathMappingsController`** — `[Route("api/v1/admin/kodi-import/path-mappings")]`, same class-level attributes.

| Action | Verb/Route | Success | Error codes |
|--------|-----------|---------|-------------|
| `List` | `GET ""` | 200 `ApiResponse<IReadOnlyList<KodiPathMappingDto>>` | 401/403 |
| `Create` | `POST ""` body `PathMappingUpsertRequest` | 200 `ApiResponse<KodiPathMappingDto>` | 400; 422 `DUPLICATE_MAPPING`; 401/403 |
| `Update` | `PUT "{id:guid}"` body `PathMappingUpsertRequest` | 200 | 404; 400; 422; 401/403 |
| `Delete` | `DELETE "{id:guid}"` | 200 `ApiResponse<object>` | 404; 401/403 |

**Contracts** (`Contracts/Admin/KodiImportRequests.cs`): `PathMappingUpsertRequest(string KodiPrefix, string NasPrefix, int? SortOrder)`, `PathMappingOverrideRequest(string KodiPrefix, string NasPrefix)`.

---

### 1.11 Configuration

`MediaHandler.Infrastructure/Options/KodiImportOptions.cs` (options pattern + `ValidateDataAnnotations().ValidateOnStart()`, registered in `DependencyInjection.cs`, section `"KodiImport"`):

```csharp
public class KodiImportOptions
{
    public const string Section = "KodiImport";

    [Range(1_048_576, 524_288_000)]                 // 1 MB … 500 MB
    public long MaxUploadSizeBytes { get; set; } = 104_857_600;   // 100 MB (decision 5)

    public int[] SupportedSchemaVersions { get; set; } = [119, 121, 131];  // decision 4

    public string? TempDirectory { get; set; }      // default: Path.Combine(Path.GetTempPath(), "mediahandler", "kodi-imports")
}
```

`appsettings.json`: add an empty `"KodiImport": {}` section with a comment-free minimal entry (defaults suffice; operators override via env vars `KodiImport__MaxUploadSizeBytes` etc.).

**DI additions** (`DependencyInjection.cs`): options registration; `services.AddSingleton<ImportRunCoordinator>(); AddSingleton<IImportRunCoordinator>(sp => sp.GetRequiredService<ImportRunCoordinator>());` `AddScoped<IKodiVideoDbReader, KodiVideoDbReader>()`; `AddScoped<IKodiImportFileStore, KodiImportFileStore>()`; `AddScoped<KodiImportPipeline>()` (resolved per-run through the coordinator's scope — never injected into the singleton, same discipline as `ScanPipeline`).

**`ITmdbService` addition** (Application interface + `TmdbService` implementation; additive, non-breaking):

```csharp
Task<TmdbIdLookupResult?> FindByExternalIdAsync(string externalId, string externalSource,
    MediaType? kindHint, string language = "en-US", CancellationToken cancellationToken = default);
// externalSource: "imdb_id" | "tvdb_id" — TMDB GET /3/find/{id}?external_source=… (public TMDB API docs)
// kindHint: Film → movie_results, TvShow → tv_results, null → movie first then tv (mirrors TmdbMatcher.LookupByIdAsync)
// Returns null on empty results; HttpRequestException propagates (pipeline maps it to IdentityLookupFailed).
```

---

## Phase 2: Tests — Specification

### Unit Tests (`MediaHandler.Tests`)

**`Kodi/KodiDbFileNameTests.cs`** (theories) — `TryParseVersion_ValidName_ReturnsVersion` (`MyVideos121.db`→121, case-insensitive, 119/131), `TryParseVersion_NoVersionSuffix_ReturnsFalse`, `TryParseVersion_MusicDbName_ReturnsFalse`.

**`Kodi/KodiTestDbBuilder.cs`** — helper creating a temp `.db` via `Microsoft.Data.Sqlite`: `CreateVideoDb(path, movies, shows, episodes, uniqueIds, musicVideos)` writing the documented table subset (path/files/movie/tvshow/episode/uniqueid/musicvideo) with the same columns the reader queries; also `CreateGarbageFile`, `CreateSqliteWithoutVideoTables`. Every fixture table definition carries the same `// SOURCE:` reference as the reader. Fixture files deleted in test dispose.

**`Kodi/KodiVideoDbReaderTests.cs`**:
- `ValidateAsync_ValidV121Structure_ReturnsValid` (FR-002)
- `ValidateAsync_MissingVideoTables_ReturnsNotKodiVideoDb` (US1-AC9, wrong-DB edge)
- `ValidateAsync_CorruptFile_ReturnsInvalidWithGuidance` (corrupt edge)
- `ValidateAsync_UnsupportedVersion_ReturnsErrorNamingVersion` (FR-003)
- `ReadAsync_EmptyLibrary_ReturnsEmptySnapshot` (empty-library edge)
- `ReadAsync_MoviesWithUniqueIds_ReturnsTitlesYearsExternalIds` (US1-AC1/AC2)
- `ReadAsync_StackedMovie_ExpandsStackUriIntoOrderedParts` (US2-AC5)
- `ReadAsync_MultiEpisodeFile_ReturnsEpisodesSharingFileRef` (US2-AC6)
- `ReadAsync_SeasonZeroEpisodes_Included` (specials edge)
- `ReadAsync_MusicVideos_ReturnedForCounting` (FR-011)
- `ReadAsync_NonAsciiTitles_RoundTripUnchanged` (non-ASCII edge)
- `ReadAsync_PercentEncodedPaths_ReturnedRaw` (decoding is translator's job)

**`Kodi/KodiPathTranslatorTests.cs`** (theories where noted):
- `Translate_MatchingPrefix_ReturnsRewrittenNasPath` (US2-AC1)
- `Translate_PercentEncodedMixedSeparatorsMixedCase_Normalizes` (US2-AC8; mixed-case asserted at dictionary-match level)
- `Translate_UnsupportedScheme_ReturnsUnsupported` — `pvr://`, `http://`, `upnp://` (non-file edge)
- `Translate_NoMatchingMapping_ReturnsNoMappingWithDirectoryPrefix` (US2-AC3)
- `Translate_OverlappingMappings_FirstInOrderWins`
- `Translate_OverridePrecedingPersistedMapping_OverrideWins` (decision 6)
- `Translate_TrailingSlashAndDuplicateSlashes_Collapsed`
- `Translate_SchemelessAbsolutePath_AttemptsMapping`

**`Features/KodiImport/StartKodiImportCommandHandlerTests.cs`** — `TestDbContext` + NSubstitute for `IKodiVideoDbReader`/`IKodiImportFileStore`/`IImportRunCoordinator` (interfaces, not DB — allowed):
- `StartImport_EmptyUpload_ValidatorRejects` (FR-002)
- `StartImport_UnrecognizedFileName_ReturnsInvalidFileName` (FR-003)
- `StartImport_UnsupportedVersion_ReturnsErrorNamingVersion` (FR-003)
- `StartImport_OversizedUpload_ReturnsUploadTooLarge` (decision 5, oversized edge)
- `StartImport_InvalidDatabase_DeletesUploadAndLeavesNoRun` (FR-004 — verify `Delete` received, `ImportRuns` empty)
- `StartImport_RunAlreadyActive_ReturnsImportInProgress` (US3-AC6)
- `StartImport_Valid_MergesPersistedMappingsWithOverridesAndStartsCoordinator` (decision 6)
- `StartImport_PreviewMode_ForwardsPreviewMode`

**`Features/KodiImport/KodiPathMappingHandlerTests.cs`** — `CreateMapping_Valid_PersistsNormalizedAndReturnsDto`, `CreateMapping_DuplicatePrefix_ReturnsDuplicateMapping`, `UpdateMapping_Existing_UpdatesFields`, `UpdateMapping_Missing_ReturnsNotFound`, `DeleteMapping_Existing_Removes`, `DeleteMapping_Missing_ReturnsNotFound`, `ListMappings_ReturnsOrderedBySortOrder`.

**`Features/KodiImport/ListKodiImportHistoryQueryHandlerTests.cs`** — `ListHistory_ReturnsNewestFirst` (US4-AC3), `ListHistory_RespectsPagination`.

**`Features/KodiImport/GetKodiImportRunQueryHandlerTests.cs`** — `GetRun_Existing_ReturnsDetailWithCountersAndPrefixes` (US4-AC1), `GetRun_Missing_ReturnsNotFound` (US4-AC5).

**`Features/KodiImport/ListKodiImportItemsQueryHandlerTests.cs`** — `ListItems_FiltersByOutcome`, `ListItems_FiltersByKind`, `ListItems_RespectsPagination` (US4-AC2), `ListItems_MissingRun_ReturnsNotFound`.

**`Kodi/KodiImportPipelineTests.cs`** — `TestDbContext`; `IKodiVideoDbReader` stubbed (NSubstitute) returning hand-built `KodiLibrarySnapshot`s; **real `KodiPathTranslator`**; **real `TmdbMatcher`** over stubbed `ITmdbService` (so the ambiguity policy is exercised end-to-end); handler-equivalent invocation: `pipeline.ExecuteAsync(run, parameters, ct)` with a tracked `ImportRun`:
- `Import_MovieWithTmdbId_CreatesMediaWithoutProviderCall` (US1-AC2 — assert `ITmdbService` received zero calls)
- `Import_ExistingSameKindAndTmdb_ReusesEntryNeverDuplicates` (US1-AC6, SC-003 — pre-seed scanner-origin `Media`)
- `Import_ImdbOnlyMovie_ResolvesViaFindByExternalId` (US1-AC3)
- `Import_TitleSearchSingleConfidentMatch_CreatesEntry` (US1-AC4)
- `Import_AmbiguousCandidates_NoMediaCreatedReviewItemWithKodiImportSource` (US1-AC5)
- `Import_ShowWithEpisodes_MaterializesSeasonsEpisodesAtCorrectNumbers` (US1-AC1)
- `Import_ZeroEpisodeShow_CreatesEmptyShow` (US1-AC7)
- `Import_SeasonZeroEpisode_MaterializedAndLinked` (specials edge)
- `Import_MappedMovieFile_LinksScannedFile` (US2-AC1, SC-001)
- `Import_EpisodeFile_CreatesEpisodeLinkAndAssociatesShow` (US2-AC2)
- `Import_UnmappedPrefix_EntryCreatedUnlinkedPrefixReported` (US2-AC3 — check `UnmatchedPrefixesJson`)
- `Import_MappedButNotScanned_ReportedNoScannedFile_LinkedAfterRescan` (US2-AC4, US3-AC5 — two-pipeline-invocation test)
- `Import_StackedMovieAllPartsScanned_LinksAllPartsUnderOneStackGroup` (US2-AC5)
- `Import_StackedMovieOnePartScanned_PartiallyLinkedMissingPartReported` (US2-AC5)
- `Import_MultiEpisodeFile_EachEpisodeLinkedWithPosition` (US2-AC6)
- `Import_FileLinkedToDifferentMedia_PreservesLinkReportsConflict` (US2-AC7, FR-017, SC-004)
- `Import_NormalizedPaths_MatchScannedFiles` (US2-AC8)
- `Import_IdenticalSecondRun_AllUnchangedZeroWrites` (US3-AC1, SC-002 — re-query counts before/after)
- `Import_ReuploadWithNewItems_OnlyNewCreated` (US3-AC2)
- `Import_ReidentifiedItemWithLinkedFile_ConflictNoDuplicate` (US3-AC3, FR-022)
- `Import_ItemRemovedFromKodi_LeftUntouchedAndReported` (US3-AC4, FR-021 — requires a prior completed baseline run)
- `Import_FailedRunThenRerun_ConvergesWithoutDuplicates` (US3-AC7 — seed partial state, re-run)
- `Import_MusicVideos_SkippedAndCounted` (FR-011)
- `Import_DuplicateTmdbWithinKodi_SingleEntryBothFilesLinkedInformational` (duplicate-identity edge)
- `Import_SameFileAsMovieAndEpisode_FirstLinkWinsConflictReported` (Kodi-internal-duplicate edge)
- `Import_MissingMarkedFile_StillLinks` (missing-but-recorded edge)
- `Import_ProviderOutage_LookupItemsFailedOthersImportedRunCompletes` (provider-outage edge — stub throws `HttpRequestException`)
- `Import_AdminResolvedReviewItem_ResolutionReusedOnNextImport` (review loop closure)
- `Preview_ValidSnapshot_PersistsOnlyRunAndOutcomeRows` (US5-AC1 — assert zero domain rows)
- `Preview_ItemWithoutTmdbId_RequiresIdentityLookupZeroProviderCalls` (US5-AC4)
- `Preview_ProjectsConflictsAndUnmatchedPrefixes` (US5-AC1/AC3)
- `Counters_ReconcileExactlyWithOutcomeRows` (SC-008 — run after several scenarios above via shared assertion helper)

### Integration Tests (`MediaHandler.IntegrationTests`, Testcontainers.MsSql)

**`KodiImport/KodiImportEndToEndTests.cs`** — real `KodiVideoDbReader` over builder-created SQLite fixture files + real pipeline + real SQL Server (`IntegrationTestBase`); `ITmdbService` substituted via an additional-services provider (pattern: `ScannerIntegrationTestBase.CreateScanRunCoordinator`):
- `FullImport_FixtureWithMoviesShowStackMultiEpisode_CompletesWithExpectedCounters` (US1/US2, SC-001)
- `Reimport_SameFixture_AllUnchanged` (SC-002)
- `Reimport_UpdatedFixture_AddRemoveReidentify_ConvergesAndReports` (US3-AC2/AC3/AC4)
- `PreviewThenImport_OutcomesMatchForDirectIdItems` (US5-AC3)
- `ImportRun_Completion_EnqueuesEnrichmentForImportedEntries` (enrichment trigger: after the import run reaches `Completed`, an `EnrichmentRun` row exists and the imported `Media` rows are enriched via the stubbed `ITmdbService`)
- `PreviewRun_Completion_NoEnrichmentTriggered` (enrichment trigger skipped for preview — assert no new `EnrichmentRun` row)
- `StartupRecovery_StuckRunningRun_MarkedFailedAndFilePurged` (`ApplyImportRunRecoveryAsync`)

**`KodiImport/KodiImportApiTests.cs`** — WebApplicationFactory-based, mirroring the infrastructure used by `Scanner/AdminAuthorizationTests.cs`:
- Authorization matrix for **all nine** new endpoints: unauthenticated → 401, authenticated non-admin → 403 (US1-AC8, US4-AC4, SC-007)
- `PostImport_ValidMultipart_Returns202ThenRunCompletes` (happy path incl. polling `GET {id}`)
- `PostImport_SecondWhileRunning_Returns409` (US3-AC6)
- `PostImport_OversizedUpload_Returns400` (factory overrides `KodiImport:MaxUploadSizeBytes` to a tiny value)
- `GetRun_UnknownId_Returns404` (US4-AC5)

**`KodiImport/KodiPathMappingsApiTests.cs`** — create/list/update/delete round-trip; duplicate prefix → 422; ordering reflected in list.

---

## Error Contract

| Error prefix (Result string) | HTTP | Emitted by |
|------------------------------|------|-----------|
| `VALIDATION_ERROR` (FluentValidation via `ValidationBehavior`, malformed `mode`/`overrides`) | 400 | all mutating/paged endpoints |
| `INVALID_FILE_NAME` — no `MyVideos<version>.db` suffix; guidance to keep original name | 400 | start |
| `UNSUPPORTED_VERSION` — names detected version + supported set | 400 | start |
| `UPLOAD_TOO_LARGE` — names configured limit | 400 | start |
| `INVALID_KODI_DB` — not SQLite / not a Kodi video DB (music DB, missing tables/columns) / unreadable ("close Kodi before copying") | 400 | start |
| `IMPORT_IN_PROGRESS` | 409 | start |
| `NOT_FOUND` | 404 | get run, list items, update/delete mapping |
| `DUPLICATE_MAPPING` | 422 | create/update mapping |
| (auth) | 401 unauthenticated / 403 non-admin | every endpoint (`AdminOnly`) |

Validation failures persist **nothing** — no run row, no outcomes, no review items, uploaded file deleted (FR-004).

---

## Post-Design Constitution Check

- Layering holds: Application handlers depend only on Application interfaces; `KodiImportOptions` is read exclusively in Infrastructure services; controllers contain no logic beyond parsing/mapping. ✅
- `ImportRunCoordinator` resolves scoped services (`KodiImportPipeline`, `MediaHandlerDbContext`, `IKodiImportFileStore`) through `IServiceScopeFactory` — no captive dependencies (same discipline as `ScanRunCoordinator`). ✅
- All new reads are `AsNoTracking()` except the pipeline's deliberately tracked bulk pre-loads (write path — same as `ScanPipeline`). ✅
- No-GPL: every SQL constant, the `stack://` rule, scheme list, and filename regex get `// SOURCE:` comments citing the public Kodi wiki; correctness is anchored by fixture tests, not copied code. ✅

---

## Implementation Sequence (for the developer sub-agent)

1. **Domain** — 6 enums, 3 entities, `ReviewItem.Source`; EF configurations; DbSets in `IApplicationDbContext`/`MediaHandlerDbContext`/`TestDbContext`; migration `AddKodiDbImport`.
2. **Packages** — `Microsoft.Data.Sqlite` 10.0.11 → Infrastructure, Tests, IntegrationTests.
3. **Options + file store + filename parser** — `KodiImportOptions`, `KodiImportFileStore`, `KodiDbFileName` (+ unit tests).
4. **Reader** — `KodiDbQueries` → `KodiVideoDbReader` → `KodiTestDbBuilder` + reader tests (TDD: fixtures first).
5. **Translator** — `KodiPathTranslator` + theory tests.
6. **TMDB extension** — `FindByExternalIdAsync` on interface + service.
7. **Pipeline** — `KodiImportPipeline` (+ the big test class).
8. **Coordinator + recovery** — `ImportRunCoordinator` (incl. the post-import enrichment trigger, §1.6), `ApplyImportRunRecoveryAsync`, `Program.cs` hook, DI registrations.
9. **Application handlers + validators** — start command first, then mapping CRUD, then queries.
10. **API** — contracts + 2 controllers.
11. **Integration tests**.

---

## Risks, Decisions, and Spec-vs-Code Discrepancies

### Confirmed scope decisions applied (from user — override spec open questions)
Watched status excluded (D1); removed-from-Kodi items untouched + counted (D2); existing links always win (D3); schema versions {119, 121, 131}, 121 mandatory (D4); size limit configurable, default 100 MB (D5); persisted mappings + per-upload overrides (D6); uploaded file discarded at terminal state (D7); re-identified items may create a separate entry (D8).

### Discrepancies found between spec/007-plan and current code (please acknowledge)

1. **No production writer for `EpisodeFileLink`/`StackGroup` exists today.** The scanner detects stacks (sets `Role`) but never persists `StackGroup` rows (a `new StackGroup` local in `ScanPipeline` is discarded unused), and 007's `LinkMediaFileCommand` sets `MediaFile.MediaId` only. Spec 008's "existing linking invariants" (FR-018) are therefore **established by this feature's pipeline**, not reused code. The pipeline follows the entities' documented semantics (composite unique `(TvEpisodeId, MediaFileId, OrderInFile)`, one `StackGroup` per `Media`, part roles per scanner convention).
2. **`LinkMediaFileCommand` is not reused** for import linking: it performs per-call queries (N+1 at import scale) and knows nothing of stacks/episode links. The pipeline reuses its **semantics** (idempotent same-media link; `FILE_ALREADY_LINKED`-equivalent conflict behavior) on pre-loaded tracked entities. No refactoring of the existing command (out of scope).
3. **No unique constraint on `Media(Type, TmdbId)`** exists today (only a non-unique index on `TmdbId`). I did **not** add one: existing databases may already contain duplicates, which would fail the migration. Dedupe is enforced at pipeline level + by the single-active-run invariant. Residual risk: a manually created duplicate outside the import → first row wins, warning logged, never a third. Recommend a follow-up hardening task (data audit + unique index), not this feature.
4. **`ITmdbService` had no find-by-external-id** — added as an additive method (`FindByExternalIdAsync`); existing callers untouched.
5. **Coordinator race tightened**: `ImportRunCoordinator` transitions the run to `Running` inside the mutex (the scan coordinator has a small Pending→Running window where a second start can pass the DB check and later die on the filtered index). Same pattern, strictly safer. The scan coordinator is deliberately left unchanged.
6. **AGENTS.md "Active Feature Spec"** still points at `specs/007-media-file-linking/plan.md`; update it to this plan when implementation starts.

### Design decisions (validate if you disagree)

- **D-A. "Seen-before"/"no longer in Kodi" baseline = most recent `Completed` run with `Mode = Import`.** No Kodi-item ids are stored on `Media`. Consequence: if a user rebuilds their Kodi library from scratch (all Kodi ids renumbered), dedupe by `(Type, TmdbId)` still prevents duplicates, but the "no longer in Kodi" list is noisy for one run. Accepted (aligns with decision 8).
- **D-B. Preview runs persist `ImportRun` + outcome rows** (the projection report is the run report; US5's "nothing persisted" = no domain data) and share the single-active-run guard. They are never the seen-before baseline.
- **D-C. Identity discrepancy via existing file link → item-level `Conflict`, no new entry created** (my reading of US3-AC3 "no duplicate entry is created"). The unlinked-file re-identification case still creates a separate entry (decision 8). Both behaviors are tested.
- **D-D. Kodi-internal duplicate resolution order** is deterministic: movies before shows/episodes, so a file referenced as both keeps the **movie** link and the episode claim is reported `Conflict`.
- **D-E. Review items created by the import use the Kodi file URI as `FilePath`** and `Source = KodiImport`; the import honors admin `Assign` resolutions on its next run (mirrors scanner loop). The review API is not modified.
- **D-F. Column-name verification task**: reader SQL column names (e.g. `movie.c07`, `episode.c12/c13`, `tvshow.c05`, `uniqueid.type/value`) come from the public Kodi wiki "Databases" documentation and must be re-verified there by the developer while writing fixtures (network access to the wiki was unavailable during design). The plan isolates all schema knowledge in `KodiDbQueries.cs` so any correction is a one-file change; tests go red if a name is wrong. **Specific warning about the reference doc**: `Revision 1/Anaylisis.md` uses descriptive pseudo-column names (`uniqueid_value`, `uniqueid_type`) — the real Kodi columns are `value` and `type`; its SQL also joins the `art` table, which is intentionally out of scope. Do not copy its extraction SQL verbatim.

### Open technical risks

| Risk | Mitigation |
|------|-----------|
| **R1. Kodi schema nuance** (a column differs in 119 vs 131) | `KodiDbQueries.ForVersion` extension point + `PRAGMA table_info` validation producing a clean `INVALID_KODI_DB` instead of a crash; fixtures per version in tests. |
| **R2. Pre-existing duplicate `(Type,TmdbId)` rows** | First-wins + warning log; never create more (see discrepancy 3). |
| **R3. File copied while Kodi runs** (WAL garbage, partial pages) | `SqliteException` → `INVALID_KODI_DB` with "close Kodi before copying" guidance; read-only mode never attempts recovery writes. |
| **R4. SC-006 provider budget exceeded** (>500 lookup-needing items) | Direct-id short-circuit + matcher cache; worst case is still bounded (~40 req/10 s TMDB throttle via existing resilience handler) — run simply takes longer; counters/report stay consistent. No batching API exists for `/find`; accepted. |
| **R5. Huge upload memory pressure** | Streamed copy with hard cap; SQLite read streaming; no full-file buffering anywhere. |
| **R6. Orphaned temp files** (crash mid-run) | Coordinator `finally` discard + startup purge of the whole temp directory (`ApplyImportRunRecoveryAsync`). |
| **R7. 30 MB Kestrel default blocks 100 MB uploads** | Action-level `[RequestSizeLimit]`/`[RequestFormLimits]` override (500 MB transport ceiling); configurable app limit enforced by the store with a clean 400. |

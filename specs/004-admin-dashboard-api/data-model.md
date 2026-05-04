# Data Model: Admin Dashboard API Endpoints

**Feature**: 004-admin-dashboard-api  
**Date**: 2025-07-18

## Entity: ScanItemDecision (Enhanced)

**Table**: `ScanItemDecisions`  
**Status**: Existing entity — enhanced with new fields

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Id` | `Guid` | Yes | PK (inherited from `BaseEntity`) |
| `ScanRunId` | `Guid` | Yes | FK → `ScanRuns.Id` |
| `FilePath` | `string(1024)` | Yes | Absolute NAS path |
| `Kind` | `ScanDecisionKind` (enum→string) | Yes | Added, Updated, Unchanged, Removed, Excluded, NeedsReview |
| `Reason` | `string(500)` | No | Human-readable explanation |
| `RuleId` | `string(100)` | No | Exclusion rule identifier |
| `MediaFileId` | `Guid?` | No | FK → `MediaFiles.Id` (SetNull on delete) |
| `ReviewItemId` | `Guid?` | No | FK → `ReviewItems.Id` (SetNull on delete) |
| **`AssignedTmdbId`** | `int?` | No | **NEW** — TMDB ID of the matched media |
| **`AssignedTmdbKind`** | `MediaType?` (enum→string) | No | **NEW** — Film or TvShow |
| **`CandidatesJson`** | `nvarchar(max)` | No | **NEW** — Default `"[]"`. JSON string (value converter), same pattern as `ReviewItem.CandidatesJson` |
| **`ParsedTitle`** | `string?(500)` | No | **NEW** — Title parsed from filename |
| **`ParsedYear`** | `int?` | No | **NEW** — Year parsed from filename |
| **`ParsedSeason`** | `int?` | No | **NEW** — Season number (TV only) |
| **`ParsedEpisode`** | `int?` | No | **NEW** — Episode number (TV only) |
| **`ParsedMediaType`** | `MediaType?` (enum→string) | No | **NEW** — Film or TvShow |
| **`LibraryRootId`** | `Guid?` | No | **NEW** — FK → `LibraryRoots.Id` (SetNull on delete) |
| `CreatedAt` | `DateTime` | Yes | Inherited from `BaseEntity` |
| `UpdatedAt` | `DateTime?` | No | Inherited |
| `CreatedBy` | `string?` | No | Inherited |
| `UpdatedBy` | `string?` | No | Inherited |

**Relationships**:
- `ScanRun` — Many-to-one (required)
- `MediaFile` — Many-to-one (optional)
- `ReviewItem` — Many-to-one (optional)
- `LibraryRoot` — Many-to-one (optional, **NEW**)

**Indexes** (new):
- `IX_ScanItemDecisions_ScanRunId_Kind` — composite for filtered queries by scan + decision type
- `IX_ScanItemDecisions_ScanRunId_ParsedMediaType` — composite for filtered queries by scan + media type
- `IX_ScanItemDecisions_LibraryRootId` — for filtered queries by library root
- `IX_ScanItemDecisions_ScanRunId_ParsedTitle` — for TV show grouping queries

**CandidatesJson schema** (SQL Server `nvarchar(max)`, serialized/deserialized via EF Core value converter — same pattern as `LibraryRoot.SearchLanguages`):
```json
[
  {
    "tmdbId": 550,
    "kind": "Film",
    "title": "Fight Club",
    "year": 1999,
    "posterPath": "/a26cz...",
    "overview": "An insomniac office worker...",
    "score": 0.95
  }
]
```

**Validation rules**:
- `CandidatesJson` defaults to `"[]"` if not provided; stored as `nvarchar(max)` JSON string
- `ParsedTitle` max length 500
- `AssignedTmdbKind` and `ParsedMediaType` stored as strings via `HasConversion<string>()`
- `LibraryRootId` FK uses `SetNull` on delete (matching existing `MediaFile.LibraryRootId` pattern)

**State transitions**:
- `AssignedTmdbId` / `AssignedTmdbKind` can be updated via reassignment (FR-003)
- When reassigned, the linked `MediaFile.MediaId` must also be updated (FR-004)

---

## Entity: EnrichmentRun (New)

**Table**: `EnrichmentRuns`  
**Status**: New entity

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Id` | `Guid` | Yes | PK (inherited from `BaseEntity`) |
| `Status` | `EnrichmentStatus` (enum→string) | Yes | Pending, Running, Completed, Failed |
| `FailureReason` | `string?(1000)` | No | Populated on failure or crash recovery |
| `StartedAt` | `DateTime` | Yes | UTC start time |
| `FinishedAt` | `DateTime?` | No | UTC completion time |
| `TotalItems` | `int` | Yes | Total entries to process |
| `EnrichedCount` | `int` | Yes | Successfully enriched |
| `FailedCount` | `int` | Yes | Failed to enrich |
| `SkippedCount` | `int` | Yes | Already enriched + unchanged |
| `CurrentItem` | `string?(500)` | No | Currently processing (title or ID) |
| `ErrorDetailsJson` | `nvarchar(max)` | No | JSON string (value converter) — array of per-entry errors |
| `CreatedAt` | `DateTime` | Yes | Inherited |
| `UpdatedAt` | `DateTime?` | No | Inherited |
| `CreatedBy` | `string?` | No | Inherited |
| `UpdatedBy` | `string?` | No | Inherited |

**Indexes**:
- `IX_EnrichmentRuns_Status` — for concurrency lock queries (`WHERE Status = 'Running'`)
- Unique filtered index: `CREATE UNIQUE INDEX ... WHERE Status = 'Running'` — SQL Server supports filtered unique indexes; defense-in-depth for the single-active invariant

**ErrorDetailsJson schema**:
```json
[
  {
    "mediaId": "guid",
    "tmdbId": 550,
    "title": "Fight Club",
    "error": "TMDB API returned 404"
  }
]
```

**State transitions**:
- `Pending` → `Running` (coordinator starts processing)
- `Running` → `Completed` (all items processed)
- `Running` → `Failed` (unrecoverable error or crash recovery)

**Concurrency invariant**: At most one row with `Status = Running` at any time.

---

## Entity: Media (Enhanced)

**Table**: `Medias`  
**Status**: Existing entity — enhanced with new fields

**Existing fields** (already in schema, populated during enrichment):
`TmdbId`, `Title`, `OriginalTitle`, `Overview`, `ReleaseDate` (also used as `FirstAirDate` for TV shows — same column), `Runtime`, `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language` (original language code, e.g., `"en"`, `"fr"`), `Type` (Film/TvShow)

> **Field name canonical**: The field is `Language` — NOT `OriginalLanguage`. The existing `Media.Language` property stores the TMDB `original_language` value. No rename needed.

> **FirstAirDate**: TV shows reuse the existing `ReleaseDate` (`DateTime?`) column for first air date. No new column added. The application layer maps TMDB `first_air_date` → `Media.ReleaseDate` for TV shows.

> **Genres**: Stored as normalized `MediaGenre` child records (existing `ICollection<MediaGenre>` navigation on `Media`). Enrichment upserts `MediaGenre` rows — it does NOT use a JSON array. The spec reference to "Genres (JSON array)" refers to the TMDB API response format, not the storage format.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| *(existing fields unchanged — see above)* | | | |
| **`Status`** | `string?(100)` | No | **NEW** — e.g., "Released", "Returning Series", "Ended" |
| **`NumberOfSeasons`** | `int?` | No | **NEW** — TV shows only |
| **`NumberOfEpisodes`** | `int?` | No | **NEW** — TV shows only |

**Enrichment populates (full list)**:
- All existing fields: `Title`, `OriginalTitle`, `Overview`, `ReleaseDate` (incl. TV `first_air_date`), `Runtime`, `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language`
- New fields: `Status`, `NumberOfSeasons` (TV), `NumberOfEpisodes` (TV)
- Child records: `MediaGenre` rows (upserted by name), `TvSeason` + `TvEpisode` records (upserted for TV shows)

---

## Entity: TvSeason (Existing)

**Table**: `TvSeasons`  
**Status**: Existing entity — used by enrichment (no schema changes needed)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK (BaseEntity) |
| `MediaId` | `Guid` | FK → `Medias.Id` (required) |
| `SeasonNumber` | `int` | Required |
| `Name` | `string` | Required |
| `Overview` | `string?` | |
| `AirDate` | `DateTime?` | |
| `PosterPath` | `string?` | |
| `EpisodeCount` | `int?` | |

**Navigation**: `Media` (parent), `TvEpisodes` (children)

---

## Entity: TvEpisode (Existing)

**Table**: `TvEpisodes`  
**Status**: Existing entity — used by enrichment and by TV file rename (episode title = `TvEpisode.Name`)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK (BaseEntity) |
| `SeasonId` | `Guid` | FK → `TvSeasons.Id` (required) |
| `EpisodeNumber` | `int` | Required |
| `Name` | `string` | Required — **used as episode title in rename convention** |
| `Overview` | `string?` | |
| `AirDate` | `DateTime?` | |
| `StillPath` | `string?` | |
| `Runtime` | `int?` | |

**Navigation**: `Season` (parent `TvSeason`), `EpisodeFileLinks` (many-to-many to `MediaFile`)

> **Rename dependency**: TV episode file rename (`RenameFile` command) requires `TvEpisode.Name` for the "Show Name - SXXEXX - Episode Title" format. The handler must load the `TvEpisode` record matching `ParsedSeason` + `ParsedEpisode` from the `ScanItemDecision`. If no enrichment has been run yet (no `TvEpisode` record exists), the rename handler must return a validation error: "Episode title not available — run TMDB enrichment first."

---

## Entity: LibraryRoot (Existing — Reference)

**Table**: `LibraryRoots`  
**Status**: Existing entity — referenced by new `ScanItemDecision.LibraryRootId` FK and used in API responses

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `Path` | `string` | **The path field is named `Path`** — used as `libraryRootPath` in `ScanItemDecisionDto` API response |
| `Kind` | `LibraryRootKind` | Movies, TvShows, Mixed |
| `Label` | `string?` | Optional human-readable label |
| `IsEnabled` | `bool` | |

> **API response mapping**: `ScanItemDecisionDto.libraryRootPath` = `LibraryRoot.Path`. The query handler joins `ScanItemDecision → LibraryRoot` and maps `LibraryRoot.Path` directly.

---

## Enum: EnrichmentStatus (New)

```csharp
public enum EnrichmentStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
```

---

## Transient Model: TvShowGroup (Not Persisted)

**Computed at query time** via `GROUP BY` on `ScanItemDecision` rows.

| Property | Type | Notes |
|----------|------|-------|
| `GroupId` | `Guid` | Deterministic hash of `scanId + parsedShowName` |
| `ParsedShowName` | `string` | The grouped show name |
| `EpisodeCount` | `int` | Count of decisions in group |
| `AssignedTmdbId` | `int?` | TMDB ID if all episodes share the same assignment |
| `AssignedTmdbKind` | `MediaType?` | Film/TvShow |
| `AssignedTitle` | `string?` | Title from the assigned Media |
| `AssignedYear` | `int?` | Year from the assigned Media |
| `AssignedPosterPath` | `string?` | Poster path from the assigned Media |
| `DecisionIds` | `List<Guid>` | IDs of member `ScanItemDecision` rows |

**Identity computation**: `GroupId = DeterministicGuid(SHA256(scanId.ToString() + "|" + parsedShowName.ToLowerInvariant()))`

---

**Migration Summary**

**Migration name**: `AddDashboardApiFields`

**Changes**:
1. `ScanItemDecisions` table: Add 9 nullable columns + 4 indexes + FK to `LibraryRoots`. `CandidatesJson` is `nvarchar(max)` with default `N'[]'`.
2. `Medias` table: Add 3 nullable columns (`Status nvarchar(100)`, `NumberOfSeasons int`, `NumberOfEpisodes int`)
3. `EnrichmentRuns` table: Create new table with all fields + indexes + filtered unique index (`WHERE Status = N'Running'`). `ErrorDetailsJson` is `nvarchar(max)`.
4. `IApplicationDbContext`: Add `DbSet<EnrichmentRun> EnrichmentRuns`

**Backward compatibility**: All new columns are nullable. Existing data unaffected. Old `ScanItemDecision` rows show null for new fields until re-scanned.

> **Note**: The project uses `Microsoft.EntityFrameworkCore.SqlServer`. All column types use SQL Server conventions (`nvarchar`, `int`, `bit`, etc.). There are no PostgreSQL-specific types (no JSONB). JSON values are stored as `nvarchar(max)` strings and deserialized via EF Core value converters, following the existing pattern in `LibraryRootConfiguration` for `SearchLanguages`.


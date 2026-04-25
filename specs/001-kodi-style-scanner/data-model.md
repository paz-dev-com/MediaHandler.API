# Phase 1 — Data Model

**Feature**: Kodi-Style NAS Library Scanner
**Date**: 2026-03-19

All entities inherit `MediaHandler.Domain.Common.BaseEntity` (audit fields
`Id`, `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` auto-populated by
`AuditableEntitySaveChangesInterceptor`). All EF configuration lives in
`MediaHandler.Infrastructure/Persistence/Configurations/` via
`IEntityTypeConfiguration<T>` (no data annotations for schema).

---

## 1. New entities

### 1.1 `LibraryRoot`

```csharp
public class LibraryRoot : BaseEntity
{
    public required string Path { get; set; }            // canonicalized; unique
    public required LibraryRootKind Kind { get; set; }   // Movies | TvShows | Mixed
    public string? Label { get; set; }                   // optional human label
    public bool IsEnabled { get; set; } = true;

    public ICollection<MediaFile> MediaFiles { get; set; } = new List<MediaFile>();
}
```

**Indexes / constraints**:

- `Path` — unique.
- `Kind` — index (admin filtering).

**Validation rules** (FluentValidation on `AddLibraryRootCommand`):

- `Path` non-empty, ≤ 1024 chars, MUST start with one of the
  `INasService.GetConfiguredPathsAsync` base paths (defense-in-depth
  against arbitrary-path attacks).
- `Kind` ∈ enum.

---

### 1.2 `ScanRun`

```csharp
public class ScanRun : BaseEntity
{
    public required ScanMode Mode { get; set; }          // Full | Incremental
    public required ScanStatus Status { get; set; }      // Pending | Running | Completed | Failed | Cancelled
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public string? FailureReason { get; set; }

    // Roots scanned (denormalized JSON list of LibraryRoot ids)
    public string LibraryRootIdsJson { get; set; } = "[]";

    // Counters (updated in batches as the pipeline progresses)
    public int TotalDiscovered { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Removed { get; set; }
    public int Excluded { get; set; }
    public int NeedsReview { get; set; }

    public ICollection<ScanItemDecision> Decisions { get; set; } = new List<ScanItemDecision>();
}
```

**Indexes / constraints**:

- Filtered unique index `WHERE Status = 'Running'` to enforce single-active scan.
- Index on `StartedAt DESC`.

**State transitions**:

```text
Pending → Running → (Completed | Failed | Cancelled)
```

---

### 1.3 `ScanItemDecision`

One row per file the pipeline considered (including excluded ones), so the
admin can answer "why was *this* file ignored?" in <30 s (SC-006).

```csharp
public class ScanItemDecision : BaseEntity
{
    public required Guid ScanRunId { get; set; }
    public required string FilePath { get; set; }        // absolute NAS path
    public required ScanDecisionKind Kind { get; set; }
    public string? Reason { get; set; }                  // human-readable; for Excluded/NeedsReview
    public string? RuleId { get; set; }                  // for Excluded: which ExclusionRule fired
    public Guid? MediaFileId { get; set; }               // when applicable
    public Guid? ReviewItemId { get; set; }              // when Kind = NeedsReview

    public ScanRun ScanRun { get; set; } = null!;
    public MediaFile? MediaFile { get; set; }
    public ReviewItem? ReviewItem { get; set; }
}
```

**Indexes**:

- `(ScanRunId, Kind)` — composite, supports report queries.
- `FilePath` — non-unique (same path appears across multiple scan runs).

---

### 1.4 `ReviewItem`

```csharp
public class ReviewItem : BaseEntity
{
    public required string FilePath { get; set; }
    public required ReviewReason Reason { get; set; }
    public required ReviewStatus Status { get; set; } = ReviewStatus.Open;

    public string? ParsedTitle { get; set; }
    public int? ParsedYear { get; set; }
    public int? ParsedSeason { get; set; }
    public int? ParsedEpisode { get; set; }

    // Up to N candidate TMDB matches (denormalized JSON array of {tmdbId, kind, title, year, score})
    public string CandidatesJson { get; set; } = "[]";

    // Resolution
    public int? ResolvedTmdbId { get; set; }
    public MediaType? ResolvedKind { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }              // user id of admin who resolved
}
```

**Indexes**:

- `(Status, CreatedAt)` — open queue listing.
- `FilePath` — for dedup (one open ReviewItem per path).

**Validation rules** (`ResolveReviewItemCommand`):

- `ResolvedTmdbId > 0`.
- `ResolvedKind` ∈ enum.
- ReviewItem MUST be in `Status = Open`.

---

### 1.5 `ExclusionRule`

Seeded from `KodiRegexCatalog` at first migration (and re-seeded on each
deploy — idempotent), but admin-overridable in a future feature. For this
feature the table is read-only outside seed.

```csharp
public class ExclusionRule : BaseEntity
{
    public required string Name { get; set; }            // e.g., "sample-files"
    public required string Pattern { get; set; }         // .NET regex
    public required ExclusionScope Scope { get; set; }   // Filename | Folder | MarkerFile | Extension
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; }                    // tie-break / display order
}
```

`ExclusionScope` is a new enum (`Filename | Folder | MarkerFile | Extension`).

**Indexes**: `(IsEnabled, Priority)`.

---

### 1.6 `StackGroup`

```csharp
public class StackGroup : BaseEntity
{
    public required Guid MediaId { get; set; }
    public Media Media { get; set; } = null!;
    public ICollection<MediaFile> Parts { get; set; } = new List<MediaFile>();
}
```

**Index**: `MediaId` unique (one stack per movie).

---

### 1.7 `NfoMetadata`

```csharp
public class NfoMetadata : BaseEntity
{
    public required string SourcePath { get; set; }      // .nfo file absolute path
    public required string RawContent { get; set; }      // for diagnostics; truncated to 32 KB
    public string? Title { get; set; }
    public int? Year { get; set; }
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int? Season { get; set; }                     // for episode NFOs
    public int? Episode { get; set; }
    public bool ParseFailed { get; set; }
    public string? ParseError { get; set; }
}
```

**Index**: `SourcePath` unique.

---

### 1.8 `EpisodeFileLink`

A many-to-many join between `TvEpisode` and `MediaFile`. Required because a
single physical file can carry multiple episodes (`S02E05-E06`) and a
single episode can theoretically be split (rare; supported by symmetry).

```csharp
public class EpisodeFileLink : BaseEntity
{
    public required Guid TvEpisodeId { get; set; }
    public required Guid MediaFileId { get; set; }
    public int OrderInFile { get; set; }                 // 1, 2, ... within the same MediaFile

    public TvEpisode TvEpisode { get; set; } = null!;
    public MediaFile MediaFile { get; set; } = null!;
}
```

**Constraints**: composite unique `(TvEpisodeId, MediaFileId)`.

---

## 2. New enums (Domain/Enums)

| Enum | Values |
|---|---|
| `LibraryRootKind` | `Movies`, `TvShows`, `Mixed` |
| `ScanMode` | `Full`, `Incremental` |
| `ScanStatus` | `Pending`, `Running`, `Completed`, `Failed`, `Cancelled` |
| `ScanDecisionKind` | `Added`, `Updated`, `Unchanged`, `Removed`, `Excluded`, `NeedsReview` |
| `ReviewStatus` | `Open`, `Resolved`, `Dismissed` |
| `ReviewReason` | `NoTmdbResult`, `MultipleCandidates`, `YearMismatch`, `UnparseableEpisode`, `NfoMalformed`, `UnknownFormat`, `OrphanedAfterMissing` |
| `MediaFileRole` | `Main`, `StackedPart`, `Episode` |
| `ExclusionScope` | `Filename`, `Folder`, `MarkerFile`, `Extension` |

All persisted as **strings** (not ints) via `.HasConversion<string>()` for
forward compatibility (insertion-order safety on enum edits).

---

## 3. Modified existing entities

### 3.1 `Media` (new fields)

```csharp
// existing fields unchanged

public int? Year { get; set; }                 // NEW — parsed year, distinct from ReleaseDate (which comes from TMDB)
public Guid? NfoMetadataId { get; set; }       // NEW — set when an NFO drove identity
public NfoMetadata? NfoMetadata { get; set; }  // NEW
public StackGroup? StackGroup { get; set; }    // NEW — null for single-file movies
```

`ReviewState` is **not** added to `Media` — review state lives on the
dedicated `ReviewItem` entity, which references `FilePath`, not `Media`,
so an unmatched file does not yet have a `Media` row. This is intentional.

### 3.2 `MediaFile` (new fields)

```csharp
public required string Fingerprint { get; set; }   // NEW — SHA-256 hex (R-006)
public DateTime? MtimeUtc { get; set; }            // NEW
public Guid? StackGroupId { get; set; }            // NEW — null when not stacked
public StackGroup? StackGroup { get; set; }        // NEW
public required Guid LibraryRootId { get; set; }   // NEW — FK
public LibraryRoot LibraryRoot { get; set; } = null!;
public required MediaFileRole Role { get; set; }   // NEW — Main | StackedPart | Episode
public Guid FirstSeenScanRunId { get; set; }       // NEW
public Guid? LastSeenScanRunId { get; set; }       // NEW
public DateTime? MissingSince { get; set; }        // NEW — soft-missing flag (R-007)

public ICollection<EpisodeFileLink> EpisodeLinks { get; set; } = new List<EpisodeFileLink>(); // NEW
```

**Indexes** (new):

- `(LibraryRootId, Fingerprint)` — unique composite (fast incremental lookup).
- `FilePath` — unique (already implicit, made explicit).
- `MissingSince` — sparse index (for the "ghost" cleanup query).

**Migration of existing rows**: existing `MediaFile` rows have no
`Fingerprint`, `MtimeUtc`, or `LibraryRootId`. The migration:

1. Adds the columns as nullable.
2. Backfills `LibraryRootId` by matching `FilePath` against newly
   inserted `LibraryRoot` seeds (or to a synthetic "Legacy" root if no
   match; admin re-points later).
3. Backfills `Fingerprint` from `path|size|0` (mtime unknown — accepted
   as one-time staleness; first incremental scan will refresh).
4. Backfills `Role = Main` (existing rows are all single-file movies).
5. Alters columns to `NOT NULL` where the schema requires.

### 3.3 `TvEpisode` (modified)

`TvEpisode` no longer carries a direct `MediaFileId`; the relationship
moves to `EpisodeFileLink` (many-to-many) to support multi-episode files.
The migration:

1. Adds `EpisodeFileLink` rows for every existing `TvEpisode` whose
   underlying file can be inferred (none exist today, since the current
   schema has no FK between `TvEpisode` and `MediaFile`).
2. No data loss.

### 3.4 `IApplicationDbContext`

```csharp
public interface IApplicationDbContext
{
    // ...existing DbSets...
    DbSet<LibraryRoot> LibraryRoots { get; }
    DbSet<ScanRun> ScanRuns { get; }
    DbSet<ScanItemDecision> ScanItemDecisions { get; }
    DbSet<ReviewItem> ReviewItems { get; }
    DbSet<ExclusionRule> ExclusionRules { get; }
    DbSet<StackGroup> StackGroups { get; }
    DbSet<NfoMetadata> NfoMetadatas { get; }
    DbSet<EpisodeFileLink> EpisodeFileLinks { get; }
    // ...
}
```

---

## 4. Relationships overview

```text
LibraryRoot 1───* MediaFile
ScanRun     1───* ScanItemDecision
ScanRun     1───* ReviewItem               (via ScanItemDecision.ReviewItemId)
Media       1───0..1 StackGroup
Media       1───0..1 NfoMetadata
Media       1───*   MediaFile              (existing — unchanged)
StackGroup  1───*   MediaFile              (Role = StackedPart)
TvEpisode   *───*   MediaFile  (via EpisodeFileLink)
TvSeason    1───*   TvEpisode              (existing — unchanged)
Media       1───*   TvSeason               (existing — unchanged)
```

---

## 5. Migration plan (single migration: `20260320000000_KodiScannerSchema`)

A single EF Core migration encapsulates **all** schema changes. Order of
operations inside `Up()`:

1. `CREATE TABLE LibraryRoots`.
2. `CREATE TABLE ExclusionRules` + seed Kodi-equivalent default rules
   (extension allowlist, sample/trailer/extras patterns, `.nomedia`
   marker, hidden-segment rule).
3. `CREATE TABLE NfoMetadatas`.
4. `CREATE TABLE StackGroups`.
5. `ALTER TABLE Medias`: add `Year`, `NfoMetadataId`.
6. `ALTER TABLE MediaFiles`: add `Fingerprint`, `MtimeUtc`,
   `StackGroupId`, `LibraryRootId`, `Role`, `FirstSeenScanRunId`,
   `LastSeenScanRunId`, `MissingSince` (all initially nullable).
7. Backfill `MediaFiles` (see §3.2).
8. Promote `Fingerprint`, `LibraryRootId`, `Role` to `NOT NULL`.
9. `CREATE TABLE ScanRuns`.
10. `CREATE TABLE ScanItemDecisions`.
11. `CREATE TABLE ReviewItems`.
12. `CREATE TABLE EpisodeFileLinks`.
13. Indexes (in dependency order).
14. Filtered unique index on `ScanRuns(Status) WHERE Status = 'Running'`.

`Down()` reverses in inverse order. The migration MUST be tested against
both an empty database (fresh install) and a populated one (Testcontainers
fixture pre-seeded with current-schema data).

---

## 6. EF configuration notes

- **Audit fields**: inherited from `BaseEntity`; no explicit configuration.
- **Domain events**: `Media`, `MediaFile`, `ReviewItem` may raise events
  (`MediaAddedEvent`, `MediaFileMissingEvent`, `ReviewItemResolvedEvent`)
  — domain-event scaffolding already exists
  (`DomainEventDispatchInterceptor`).
- **Soft delete**: not introduced. Missing files are soft-flagged via
  `MissingSince`, not via a global query filter.
- **AsNoTracking**: all read queries in `Features/Scan/Queries` and
  `Features/Review/Queries` use `AsNoTracking()` per constitution IV.


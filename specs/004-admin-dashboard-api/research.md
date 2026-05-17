# Research: Admin Dashboard API Endpoints

**Feature**: 004-admin-dashboard-api  
**Date**: 2025-07-18

## Research Task 1: EnrichmentRun Concurrency Lock Pattern

**Context**: The spec requires a DB-persisted `EnrichmentRun` entity with a concurrency lock that prevents duplicate runs, plus crash recovery on startup.

**Decision**: Follow the `ScanRunCoordinator` singleton pattern — a new `EnrichmentCoordinator` service registered as singleton, using `IServiceScopeFactory` to create scoped `DbContext` instances per operation. Concurrency is enforced by querying for `EnrichmentRun` rows with `Status = Running` before starting. Stale rows are cleaned up in `DatabaseInitializer`.

**Rationale**: The `ScanRunCoordinator` already solves identical problems (single-active invariant, background execution, progress reporting, crash recovery). Reusing the same architectural pattern keeps the codebase consistent and avoids inventing new concurrency mechanisms.

**Alternatives considered**:
- **In-memory lock only**: Rejected — loses state on crash/restart, cannot detect stale locks.
- **Filtered unique index on `Status = Running`**: Considered as an additional safety net (like `ScanRun`), but the primary enforcement is application-level via the coordinator query pattern. A filtered unique index can be added for defense-in-depth.
- **Distributed lock (Redis)**: Overkill for a single-instance personal server.

---

## Research Task 2: TV Show Group Identity (Computed Hash)

**Context**: TV show groups are transient (not persisted). The spec requires a deterministic group ID derived from `hash(scanId + parsedShowName)`.

**Decision**: Use a deterministic GUID (v5 UUID) derived from `SHA256(scanId.ToString() + "|" + parsedShowName.ToLowerInvariant())`, truncated to 16 bytes for a Guid. This is computed at query time in the `ListTvShowGroups` handler using EF Core `GroupBy` on `ParsedTitle` where `MediaType == TvShow`.

**Rationale**: A deterministic GUID allows stable references across requests without persisting anything. The `GroupBy` in EF Core translates to SQL `GROUP BY` for efficient server-side computation. Using `ToLowerInvariant()` before hashing ensures consistent grouping regardless of case.

**Alternatives considered**:
- **Integer hash**: Rejected — collision risk is higher with 32-bit integers, and the rest of the API uses Guid identifiers.
- **String-based ID (e.g., `scanId__showName`)**: Rejected — inconsistent with the entity ID convention (all entities use `Guid`).
- **Persisted groups table**: Explicitly rejected by FR-006.

---

## Research Task 3: Background Enrichment with TMDB Rate Limiting

**Context**: Enrichment must call the TMDB API for each media entry, handle rate limits (HTTP 429), and support incremental processing.

**Decision**: The `EnrichmentCoordinator` fires a background `Task.Run` (via `IServiceScopeFactory`), iterating over `Media` entities that need enrichment. For each entry:
1. Call `ITmdbService.GetMediaDetailsAsync` for base metadata.
2. For TV shows, also call `ITmdbService.GetTvShowSeasonsAsync` to populate season/episode child records.
3. Handle HTTP 429 with Polly retry (exponential backoff **up to 30s**, max 5 retries) — already configured via `AddStandardResilienceHandler()` on the TMDB `HttpClient`.
4. Track progress on the `EnrichmentRun` row: update `EnrichedCount`, `CurrentItem` periodically (batch of 10 or every 5 seconds).
5. On completion, set `FinishedAt`, `Status`, and summary counts.

**Incremental logic**: Query `Media` entities where:
- `MediaFile` exists with a TMDB assignment, AND
- Either the `Media` row has no enrichment data (e.g., `Overview IS NULL`), OR
- The `Media.UpdatedAt` is newer than the last enrichment (assignment changed).

**Rationale**: Leverages existing `ITmdbService` methods and Polly resilience already configured in `DependencyInjection.cs`. No new HTTP client needed.

**Alternatives considered**:
- **Hangfire/Quartz job scheduler**: Overkill for a single background task; adds dependency.
- **`BackgroundService` / `IHostedService`**: Would run on startup, not on-demand. The coordinator pattern (start on API call, run in background Task) is more appropriate and matches `ScanRunCoordinator`.

---

## Research Task 4: File Rename — Atomic Filesystem Operations

**Context**: File rename must be atomic (no partial state), in-place, with case-insensitive conflict detection.

**Decision**: Use `File.Move(sourcePath, targetPath)` which is atomic on Linux ext4 (single `rename()` syscall). Wrap in a service `IFileRenameService` (Application interface, Infrastructure implementation) to enable mocking in tests.

**Rename flow**:
1. Load `MediaFile` + related `Media` (for title/year) from DB.
2. Compute target filename using naming conventions:
   - Film: `"{Media.Title} ({Media.Year}).{extension}"`
   - TV: `"{ShowName} - S{season:D2}E{episode:D2} - {EpisodeTitle}.{extension}"`
3. If `preview=true`, return current + proposed names without executing.
4. Case-insensitive conflict check: `Directory.GetFiles(directory)` → compare each with `StringComparison.OrdinalIgnoreCase`.
5. Execute `File.Move` (single atomic op).
6. Update `MediaFile.FilePath` in DB.
7. `SaveChangesAsync` — if DB save fails, attempt to move file back (compensating action).

**Rationale**: `File.Move` is the simplest and most reliable approach. The compensating rollback ensures atomicity across filesystem + database. Case-insensitive check protects Samba/FAT32 NAS mounts.

**Alternatives considered**:
- **Copy + delete**: Rejected — not atomic, wastes I/O.
- **Transactional NTFS**: Not available on Linux.
- **Database-first with filesystem catch-up**: More complex, same outcome.

---

## Research Task 5: ScanItemDecision Entity Enhancement — Migration Strategy

**Context**: Existing `ScanItemDecision` rows need new nullable columns added. The scanner pipeline must populate these fields going forward.

**Decision**: Add new nullable columns via EF Core migration:
- `AssignedTmdbId` (int?) — TMDB ID from matched `Media`
- `AssignedTmdbKind` (string? → MediaType enum) — Film or TvShow
- `CandidatesJson` (string, default "[]") — `nvarchar(max)` JSON string via value converter (same pattern as `LibraryRoot.SearchLanguages`)
- `ParsedTitle` (string?, max 500)
- `ParsedYear` (int?)
- `ParsedSeason` (int?)
- `ParsedEpisode` (int?)
- `MediaTypeDiscriminator` (string? → MediaType enum) — renamed to `ParsedMediaType` in all artifacts for clarity
- `LibraryRootId` (Guid?, FK to LibraryRoot)

Existing rows get `null` for all new fields. The scanner pipeline changes (populating these fields during scan) are a prerequisite for the dashboard to show meaningful data — but this is handled in the scanner codebase modification, not the dashboard API itself.

**Rationale**: All new columns are nullable, so the migration is backward-compatible. No data migration needed — old rows simply show empty fields until re-scanned. The `CandidatesJson` column follows the same `nvarchar(max)` + value converter pattern as `ReviewItem.CandidatesJson` and `LibraryRoot.SearchLanguages`.

**Alternatives considered**:
- **Separate table for extended fields**: Rejected — adds unnecessary JOIN complexity. The fields are intrinsic to the decision.
- **Backfilling old data**: Rejected — would require re-running scanner logic retroactively. A new full scan achieves the same result more reliably.

---

## Research Task 6: Enrichment Metadata Fields on Media Entity

**Context**: FR-028 specifies exact fields to populate during enrichment. Need to verify which fields already exist on `Media` vs. which need adding.

**Decision**: After examining `Media.cs`, most base fields already exist: `Title`, `OriginalTitle`, `Overview`, `ReleaseDate`, `Runtime`, `PosterPath`, `BackdropPath`, `VoteAverage`, `VoteCount`, `Language`. Fields that need adding:
- `Genres` — already exists as `ICollection<MediaGenre>` navigation (can be updated during enrichment)
- `Status` (string?, e.g., "Released", "Ended") — **NEW, needs migration**
- `NumberOfSeasons` (int?) — **NEW, needs migration**
- `NumberOfEpisodes` (int?) — **NEW, needs migration**
- `FirstAirDate` — can reuse `ReleaseDate` field for TV shows (already nullable DateTime)

`TvSeason` and `TvEpisode` entities already exist with the needed fields (season number, episode number, name, overview, air date). Enrichment creates or updates these child records.

**Rationale**: Minimizes schema changes by reusing existing fields. Only 3 new columns on `Media`. TV season/episode structures are already in place from the scanner feature.

**Alternatives considered**:
- **Separate `EnrichedMetadata` entity**: Rejected — spreads media data across tables unnecessarily.

---

## Research Task 7: Controller Organization

**Context**: Need to decide how to organize new endpoints across controllers.

**Decision**: Create 3 new controllers:
1. **`AdminScanDecisionsController`** (`/api/v1/admin/scan-decisions`) — scan decision browsing, reassignment, TV groups, TV group assignment. Grouped because these all operate on `ScanItemDecision` data.
   - Added: `GET /api/v1/admin/scan/{scanId}/decisions` on `AdminScanController` (extends existing controller, scoped under scan resource)
2. **`AdminEnrichmentController`** (`/api/v1/admin/enrichment`) — start and status polling.
3. **`AdminFilesController`** — extends or replaces existing `FilesController` for admin rename operations under `/api/v1/admin/files`.

The TV group endpoints (`/api/v1/admin/tv-groups/...`) are placed on `AdminScanDecisionsController` since they're computed from scan decisions.

**Rationale**: Follows the existing pattern where each resource domain gets its own controller (scan → `AdminScanController`, review → `AdminReviewController`). Scan decisions are a new resource domain deserving their own controller.

**Alternatives considered**:
- **Single monolithic `AdminDashboardController`**: Rejected — too many endpoints in one file, breaks SRP.
- **One controller per endpoint**: Rejected — too granular, creates file sprawl.


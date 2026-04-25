# Checklist: Scanner Pipeline Robustness

**Purpose**: Validate the scanner survives realistic NAS, network, data, and concurrency failure modes without data corruption or stuck state.
**Scope**: Scan orchestrator, fingerprinting, NFO parsing, TMDB client, cancellation, single-active-scan invariant, startup recovery.
**How to use**: Tick after running the integration suite (T097–T098, T103–T106) and the failure-injection harness. Each item is a concrete, observable behavior.

## NAS Availability

- [ ] CHK001 - Scan started against a reachable root that becomes unreachable mid-scan transitions `ScanRun.Status` to `Failed` with a structured error (T105)
- [ ] CHK002 - Files previously seen but unreachable are soft-marked (`MediaFile.MissingSince` set) and NOT hard-deleted (T105, FR-019)
- [ ] CHK003 - A subsequent scan that re-discovers the file clears `MissingSince` (no duplicate row created)
- [ ] CHK004 - UNC path with credential failure produces a single `ScanError` row, not a per-file error storm
- [ ] CHK005 - Filesystem permission errors on a single subdir do not abort the whole scan; subdir is recorded and the scan continues

## NFO Parsing

- [ ] CHK006 - Malformed XML in `movie.nfo` produces a `ReviewItem` with reason `NfoParseError` and falls back to filename-derived title/year (T097, T098, FR-018)
- [ ] CHK007 - NFO with unexpected schema (missing `<title>`, `<year>`) falls back to filename parse without throwing (T098)
- [ ] CHK008 - NFO that XML-parses but contains conflicting data with filename emits a `ReviewItem` with both candidates in the JSON candidates column (T041)
- [ ] CHK009 - NFO larger than the documented size cap is rejected with a `ScanError`, not loaded into memory unbounded
- [ ] CHK010 - NFO with non-UTF8 encoding (Windows-1252, UTF-16) is decoded correctly or rejected with a clear error

## TMDB Client

- [ ] CHK011 - TMDB HTTP 429 triggers exponential backoff with jitter, retry budget enforced, and final failure produces a `ReviewItem` (T086, FR-017)
- [ ] CHK012 - TMDB HTTP 5xx is retried per the documented policy; persistent 5xx surfaces as `ReviewItem` not `ScanRun.Failed`
- [ ] CHK013 - TMDB network timeout does not block the pipeline; per-item timeout is enforced (T086)
- [ ] CHK014 - TMDB returns ambiguous matches (multiple candidates) → `ReviewItem` created with full candidate list in `Candidates` JSON (T041)
- [ ] CHK015 - TMDB returns zero matches → `ReviewItem` with reason `NoTmdbMatch` (FR-018)
- [ ] CHK016 - In-process LRU cache hit avoids a TMDB call (verified by counter / log) (T086)

## Idempotency & Incremental

- [ ] CHK017 - Re-scanning an unchanged tree produces zero new `MediaFile` rows and zero `ReviewItem` rows (T067)
- [ ] CHK018 - File fingerprint is SHA-256 of the documented inputs (path + size + mtime, per data-model.md) and stored on `MediaFile` (T067)
- [ ] CHK019 - Changing only mtime (not content) is detected as changed; changing only path (rename) is detected and updates path without losing identity per documented rules
- [ ] CHK020 - Incremental rescan touches < 25% of the work of full rescan on an unchanged tree (SC-005, T061)
- [ ] CHK021 - Per-scan dedup prevents the same path from being processed twice within one `ScanRun`

## Cancellation

- [ ] CHK022 - `POST /api/v1/admin/scans/{id}/cancel` propagates a `CancellationToken` that the worker observes within the documented window (T068)
- [ ] CHK023 - Cancelled scan transitions to `Cancelled` (not `Failed`) and persists partial results already written
- [ ] CHK024 - Cancellation drains the `Channel<>` cleanly without unobserved task exceptions (T068)
- [ ] CHK025 - Cancelled scan releases the single-active-scan slot so a new scan can start immediately

## Single-Active-Scan Invariant

- [ ] CHK026 - Filtered unique index on `ScanRun(Status)` WHERE `Status IN ('Pending','Running')` exists in the migration (T039, T072)
- [ ] CHK027 - Attempting to start a second scan while one is `Running` returns 409 Conflict via `ApiResponse<T>` (not 500)
- [ ] CHK028 - Race condition test: two concurrent `POST /scans` requests result in exactly one `ScanRun` row and one 409 (T072)

## Startup Recovery

- [ ] CHK029 - On application startup, any `ScanRun` left in `Running` state from a prior crash is transitioned to `Failed` with reason `OrphanedAtStartup` (T052)
- [ ] CHK030 - Startup recovery is idempotent (running it twice produces no additional state changes)
- [ ] CHK031 - After startup recovery, the single-active-scan slot is free

## Edge Cases

- [ ] CHK032 - Non-ASCII filenames (UTF-8 paths with é, ñ, 日本語, emoji) round-trip through scan → DB → API response without mojibake
- [ ] CHK033 - Stacked file with one part missing on disk: surviving parts produce a `ReviewItem` with reason `IncompleteStack`
- [ ] CHK034 - Multi-episode file (`S01E05-E07`) creates one `MediaFile` and three `EpisodeFileLink` rows (T020, T088)
- [ ] CHK035 - File with extremely long path (> 260 chars on Windows-style NAS) is handled or rejected with a clear `ScanError`
- [ ] CHK036 - Symlink loops do not cause infinite recursion (depth cap or visited-inode tracking enforced)

## Missing vs Deleted Semantics (FR-019, T105)

- [ ] CHK037 - File present in DB but not on disk this scan: `MissingSince` set to scan start UTC; row NOT deleted
- [ ] CHK038 - File missing for ≥ documented retention window: surfaced via review queue or admin report (not silently purged)
- [ ] CHK039 - File reappears after being marked missing: `MissingSince` cleared, `LastSeenAt` updated, no duplicate


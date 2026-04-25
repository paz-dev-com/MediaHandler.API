# Checklist: Performance & Scale

**Purpose**: Validate scanner performance, query efficiency, log volume, and cache effectiveness against the documented success criteria.
**Scope**: Scan orchestrator, EF query plans, TMDB cache, Channel<> backpressure, indexes, JSON column queries, benchmark fixture.
**How to use**: Tick after running the benchmark fixture (T116) and reviewing `benchmark-report.md`. Numeric items must reference measured values, not promises.

## SC-005 — Incremental vs Full Scan (T061)

- [ ] CHK001 - Benchmark fixture run produces a measured "full scan" duration on the documented N-file corpus and records it in `benchmark-report.md`
- [ ] CHK002 - Same fixture, immediate re-scan: measured duration < 25% of CHK001 (SC-005 met)
- [ ] CHK003 - Incremental rescan touches < 25% of files (verify via per-scan counters: `FilesProcessed`, `FilesSkippedUnchanged`)
- [ ] CHK004 - Fingerprint comparison short-circuits before any TMDB call (verified by zero TMDB request count on unchanged-tree rescan)

## SC-001 — Full Scan Throughput

- [ ] CHK005 - 10k-file synthetic fixture completes within the documented SC-001 time budget; recorded in `benchmark-report.md`
- [ ] CHK006 - Throughput baseline (files/sec) recorded; regressions of > 20% in future PRs gated by reviewing this checklist

## EF Query Efficiency

- [ ] CHK007 - All read queries (list scans, list review items, get scan run) use `AsNoTracking()` (cross-ref `code-review.md` CHK010)
- [ ] CHK008 - All read queries project to DTOs via `Select(...)` — no full-graph materialization
- [ ] CHK009 - `GET /api/v1/admin/scans/{id}?includeReview=true` (T103) uses a single round-trip with explicit projection — no N+1 verified by EF logging in test
- [ ] CHK010 - The `includeReview` payload is capped at **100 review items** with pagination/`hasMore` flag (T103)
- [ ] CHK011 - List endpoints (`GET /scans`, `GET /review-items`) enforce server-side paging with documented max page size

## Indexes (T038–T047)

- [ ] CHK012 - Migration creates indexes on every FK column added by this feature
- [ ] CHK013 - Filtered unique index on `ScanRun(Status) WHERE Status IN ('Pending','Running')` present (T039) — verified by `\d` / migration script inspection
- [ ] CHK014 - Indexes on `MediaFile.LibraryRootId`, `MediaFile.Path`, `MediaFile.Fingerprint`, `MediaFile.MissingSince` (or the documented subset) present
- [ ] CHK015 - `ReviewItem.Status` and `ReviewItem.ScanRunId` indexed (supports list-by-status and per-scan queries)
- [ ] CHK016 - JSON column `ReviewItem.Candidates` (T041): if the feature queries into the JSON, an appropriate computed-column or JSON index exists; otherwise, documented as "no JSON predicates required"
- [ ] CHK017 - EXPLAIN/query plan captured for the top 5 hot queries; no sequential scans on tables expected to grow > 10k rows

## TMDB Cache (T086)

- [ ] CHK018 - In-process LRU cache for TMDB responses present, with documented max size and TTL
- [ ] CHK019 - Cache hit rate measured on the benchmark run and recorded (target documented in plan.md or research.md)
- [ ] CHK020 - Cache key includes title + year (or documented composite) so collisions are impossible across distinct movies
- [ ] CHK021 - Per-scan dedup ensures the same `(title, year)` is queried at most once per scan even on cache miss
- [ ] CHK022 - Cache eviction tested: filling beyond capacity evicts LRU entries without leaking memory

## Logging Volume (T107)

- [ ] CHK023 - 10k-file scan produces a **bounded** log volume — measured byte count per scan recorded in `benchmark-report.md`
- [ ] CHK024 - Per-file log lines are at Debug level (or below); Info-level events emit at most O(scan-phases), not O(files)
- [ ] CHK025 - Sampling or aggregation used for high-frequency events (e.g., "skipped unchanged" emits a summary, not one line per file)
- [ ] CHK026 - Structured properties (not interpolation) confirmed so log shipping/filtering can downsample efficiently

## Channel<> Backpressure (T068)

- [ ] CHK027 - Progress channel has a bounded capacity; producer awaits when full (no unbounded memory growth)
- [ ] CHK028 - Backpressure scenario test: slow consumer does not cause OOM or unbounded queue under the 10k-file run
- [ ] CHK029 - Channel completion semantics correct on cancellation and on normal completion (writer completes, reader drains)

## Fingerprint Cost (T067)

- [ ] CHK030 - Fingerprint computation (SHA-256 over path+size+mtime per data-model.md) measured per file and recorded; does not dominate scan time
- [ ] CHK031 - Fingerprint inputs do NOT include full file content read (verified by code review — would defeat performance)

## Per-Scan Dedup

- [ ] CHK032 - Same path encountered twice within one `ScanRun` is processed once (verified by counter test)
- [ ] CHK033 - Dedup data structure is bounded (HashSet sized appropriately or per-batch flushed) — no unbounded growth on huge roots

## Benchmark Artifact (T116)

- [ ] CHK034 - `benchmark-report.md` exists at the documented path and contains: corpus size, full-scan time, incremental-scan time, TMDB cache hit rate, log byte volume, top-5 query timings
- [ ] CHK035 - Report is regenerated on every PR that touches scanner code (CI step or documented manual gate)
- [ ] CHK036 - Report includes git SHA and machine spec so cross-PR comparisons are valid


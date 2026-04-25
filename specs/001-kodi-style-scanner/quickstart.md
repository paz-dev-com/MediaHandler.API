# Quickstart — Kodi-Style NAS Library Scanner

**Feature**: Kodi-Style NAS Library Scanner
**Audience**: developer / admin verifying SC-001..SC-008 against a benchmark fixture library.

---

## 1. Build a benchmark fixture library

The fixture exists outside the repo (it is a real or simulated NAS share).
For local development, simulate it as a directory tree under
`./fixtures/nas/` and point a fake `INasService` at it (the Freebox
implementation is not used in tests). Production verification uses a real
NAS root registered as a `LibraryRoot`.

### 1.1 Required content

- **≥ 200 movies**, distributed across these layouts:
  - Per-movie folder + year: `Movies/The Matrix (1999)/The Matrix (1999).mkv` (≥ 80 %).
  - Flat folder of movies: `Movies/Inception.2010.1080p.BluRay.x264-GROUP.mkv` (≥ 10 %).
  - **Stacked movies** (2-part): `Movies/Kill.Bill.2003/Kill.Bill.2003.cd1.mkv` + `…cd2.mkv` (≥ 5 examples).
  - Multi-disc: `disc1/disc2`, `(a)/(b)`, `pt1/pt2` (≥ 2 each).
- **≥ 50 TV shows**, each with:
  - Multiple `Season XX/` folders.
  - At least one `Specials/` or `Season 00/` folder for ≥ 5 shows.
  - At least one **multi-episode file** `S02E05-E06.mkv` for ≥ 5 shows.
  - At least one show using `1x05`-style numbering for ≥ 2 shows.
  - At least one date-based-numbered show (`2024.03.19.mkv`) for ≥ 1 show.
- **Exclusion bait** scattered through the tree:
  - `Sample/movie-sample.mkv`, `*-sample.mkv`, `*-trailer.mkv`.
  - `Extras/`, `Featurettes/`, `Trailers/` subfolders.
  - A `.nomedia` marker in one folder that should be entirely skipped.
  - Hidden folders (`.recycle/`).
  - Non-video files (`.txt`, `.jpg`, `.srt`).
- **NFO sidecars** for ≥ 5 movies (`movie.nfo` with `<tmdbid>`) and
  ≥ 3 TV shows (`tvshow.nfo` with `<tmdbid>`).
- A few **misnamed** items meant to land in the review queue:
  - A movie file with no year and an ambiguous title (e.g.,
    `the.movie.mkv`).
  - An episode file at the show root with no season folder and no
    `SxxExx` token.
  - A movie file whose folder says one title and whose filename says
    another.

### 1.2 Fixture authoring tool

`MediaHandler.IntegrationTests/Scanner/Fixtures/FixtureBuilder.cs`
generates the directory tree (and feeds the fake `INasService` enumerator)
deterministically from a YAML manifest at
`MediaHandler.IntegrationTests/Scanner/Fixtures/benchmark.yaml`.

---

## 2. Configure & register the library root

### 2.1 Local (dev)

```bash
# from repo root
dotnet user-secrets --project MediaHandler.API set Nas:BasePaths:0 "/abs/path/to/fixtures/nas"
```

Start the API:

```bash
dotnet run --project MediaHandler.API
```

### 2.2 Register the root via API (admin JWT required)

```bash
curl -X POST http://localhost:5000/api/v1/admin/library-roots \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Content-Type: application/json" \
  -d '{
    "path": "/abs/path/to/fixtures/nas/Movies",
    "kind": "Movies",
    "label": "Benchmark Movies"
  }'

curl -X POST http://localhost:5000/api/v1/admin/library-roots \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Content-Type: application/json" \
  -d '{
    "path": "/abs/path/to/fixtures/nas/TV Shows",
    "kind": "TvShows",
    "label": "Benchmark TV"
  }'
```

---

## 3. Trigger a full scan

```bash
SCAN_RUN_ID=$(curl -sX POST http://localhost:5000/api/v1/admin/scan \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Content-Type: application/json" \
  -d '{ "libraryRootIds": [], "mode": "Full" }' \
  | jq -r '.data.id')

# Poll until status != Running
while true; do
  STATUS=$(curl -sX GET "http://localhost:5000/api/v1/admin/scan/$SCAN_RUN_ID" \
    -H "Authorization: Bearer $ADMIN_JWT" | jq -r '.data.status')
  echo "status=$STATUS"
  [ "$STATUS" != "Running" ] && [ "$STATUS" != "Pending" ] && break
  sleep 2
done

# Final summary
curl -sX GET "http://localhost:5000/api/v1/admin/scan/$SCAN_RUN_ID?includeReview=true" \
  -H "Authorization: Bearer $ADMIN_JWT" | jq .
```

---

## 4. Verify each Success Criterion

### SC-001 — ≥ 98 % correct classification

Compare the scan's `Counts` against the fixture manifest:

```bash
# Expected vs. actual
EXPECTED_MOVIES=$(yq '.movies | length' MediaHandler.IntegrationTests/Scanner/Fixtures/benchmark.yaml)
EXPECTED_EPISODES=$(yq '[.tv_shows[].episodes[]] | length' MediaHandler.IntegrationTests/Scanner/Fixtures/benchmark.yaml)
ACTUAL_MOVIES=$(curl -s ".../api/v1/media?type=Film&pageSize=1" -H "Authorization: Bearer $ADMIN_JWT" | jq '.meta.totalCount')
# ... compare; tolerance = ≤ 2 % miss + ≤ 0.5 % silent-miss
```

Automated equivalent: `MediaHandler.IntegrationTests/Scanner/FullScanEndToEndTests.cs::Sc001_ClassificationAccuracy_AtLeast98Percent`.

### SC-002 — ≤ 0.5 % silent misclassification

`Sc002_SilentMisclassRate_AtMost0p5Percent` cross-checks every fixture
item's expected `(tmdbId, kind, season, episode)` against the persisted
state. Any divergence that did NOT also produce a `ReviewItem` for the
same path counts as silent.

### SC-003 — 100 % exclusion accuracy

`Sc003_ExclusionFidelity_NoFalsePositivesOrFalseNegatives` asserts:

- Every fixture path tagged `expected: excluded` appears as
  `ScanItemDecision.Kind = Excluded` AND has zero corresponding
  `MediaFile`.
- Every fixture path tagged `expected: included` produces a `MediaFile`.

### SC-004 — ≥ 99 % parity with Kodi

`Sc004_KodiBehavioralParity` runs the scanner over the parity fixture
subset (a curated set of paths annotated with the *observed* Kodi
classification). Asserts ≥ 99 % matching outcomes.

### SC-005 — Incremental < 25 % of full

```bash
# Run a second scan immediately
SECOND=$(curl -sX POST http://localhost:5000/api/v1/admin/scan \
  -H "Authorization: Bearer $ADMIN_JWT" -H "Content-Type: application/json" \
  -d '{ "libraryRootIds": [], "mode": "Incremental" }' | jq -r '.data.id')

# Wait, then read counts: Added/Updated/Removed MUST be 0; Unchanged == TotalDiscovered
```

`IncrementalScanIdempotencyTests.cs::Sc005_IncrementalScan_UnchangedAndFast`
measures both wall-clock ratio and zero-delta counters.

### SC-006 — Diagnose any file in < 30 s

Pick a file path, query the scan-run detail endpoint, and search the
returned `ScanItemDecision` rows for that path. Each decision row
includes `Kind`, `Reason`, and `RuleId` (for exclusions). The admin UI
(out of scope) renders this; the contract guarantees the data is there.

Manual verification:

```bash
curl -s ".../api/v1/admin/scan/$SCAN_RUN_ID?includeReview=true" \
  -H "Authorization: Bearer $ADMIN_JWT" \
  | jq '.data.reviewItems[] | select(.filePath | contains("the.movie.mkv"))'
```

### SC-007 — ≥ 80 % reduction in manual corrections

Baseline (current implementation) and new-implementation review counts
are compared against the same production library. The metric is
`(open ReviewItems after fresh full scan) / (manual corrections under
old impl)`. Recorded in `MediaHandler.IntegrationTests/Scanner/`'s
`Sc007_ManualCorrectionReduction_AtLeast80Percent` (operates over the
synthetic benchmark and an injected baseline number, since the prod
library cannot be checked into CI).

### SC-008 — Zero unauthorized scan starts

`MediaHandler.IntegrationTests/Scanner/AdminAuthorizationTests.cs`
verifies, against Testcontainers SQL Server:

- Anonymous → 401 on every endpoint in `contracts/`.
- Authenticated `User`-role JWT → 403 on every endpoint.
- Admin-role JWT → 2xx on every endpoint.
- Anonymous attempt to start a scan does **not** create a `ScanRun` row.

---

## 5. Reset between runs

```bash
# (dev only) wipe scan history + review queue without dropping the DB
dotnet ef database update 0 --project MediaHandler.Infrastructure \
  --startup-project MediaHandler.API
dotnet ef database update --project MediaHandler.Infrastructure \
  --startup-project MediaHandler.API
```

Or, surgically, via SQL:

```sql
DELETE FROM ScanItemDecisions;
DELETE FROM ReviewItems;
DELETE FROM ScanRuns;
UPDATE MediaFiles SET MissingSince = NULL;
```

---

## 6. Where to look when something is wrong

| Symptom | Look here |
|---|---|
| File silently missing from library | `ScanItemDecision` row for that path → `Kind`/`Reason`. |
| Movie matched to wrong TMDB id | `ReviewItem` for that path; if none, parser/matcher heuristic gap → add a `[Theory]` row in `KodiNameParserTests` / `TmdbMatcherTests`. |
| Multi-part movie shows up as N movies | `StackingDetectorTests` — add the failing filename pair as a row. |
| Multi-episode file produced one episode | `TvEpisodeMatcherTests` — confirm SxxExx-Eyy is in the regex catalog. |
| Sample file slipped through | `ExclusionEvaluatorTests` — add the offending path. |
| Scan stuck in `Running` after process kill | Restart API: startup hook in `MediaHandlerDbContext` config transitions stale `Running` rows to `Failed`. |


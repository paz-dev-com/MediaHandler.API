# Phase 0 — Research

**Feature**: Kodi-Style NAS Library Scanner
**Date**: 2026-03-19

This document records every NEEDS CLARIFICATION resolved before design, the
chosen approach, the rationale, and the alternatives that were rejected.

---

## R-001 — Kodi GPL-2.0 licensing posture & porting scope

**Decision**: **Clean-room re-derivation.** All Kodi-equivalent regex sets,
exclusion lists, stacking rules, and NFO semantics will be re-derived from
publicly documented Kodi *behavior* (advancedsettings keys and their default
values, the Kodi wiki naming conventions, and observable input/output pairs)
into newly written C# code. **No `.cpp`, `.h`, or other GPL-2.0 source from
`/home/tpfeifer/Repos/xbmc-master/` is copied or paraphrased line-for-line into
this repository.** The local Kodi tree is consulted only as a behavioral
oracle — i.e., to ask "given input X, what does Kodi *do*?" — never as a
source of code to translate.

**Concrete in-tree policy**:

1. A `MediaHandler.Infrastructure/Nas/Scanner/README.md` will state the policy
   and forbid pasting Kodi source.
2. Source comments in `KodiRegexCatalog.cs` may reference *behavior identifiers*
   (e.g., `// Equivalent to Kodi's default 'tvshowmatching' regex set`) but
   MUST NOT include the original Kodi regex literal copied verbatim. Each
   re-derived regex MUST be authored from the test fixtures, not from
   reading `xbmc/utils/RegExp.cpp`.
3. The unit-test golden table (`KodiNameParserTests.cs`) is the contract:
   tests are written first to encode expected (input → output) tuples
   inferred from Kodi's documented defaults, then the regex catalog is
   authored to satisfy them. The reviewer of the PR is responsible for
   confirming no GPL paste occurred — code review checklist item.
4. NO build-time or runtime linkage to any Kodi binary; no GPL transitive
   dependency is added to any `*.csproj`.

**Rationale**: Kodi is GPL-2.0. Copying or directly translating its source
files into this codebase (Apache/MIT-spirited, currently unlicensed but
not GPL) would *contaminate* the entire repository under GPL terms. A
clean-room re-derivation that uses Kodi only as a behavioral oracle
preserves freedom of licensing for this project while still achieving
behavior parity (FR-024, SC-004). The cost is modestly more authoring
work — paid for by the regex tests we'd want anyway.

**Alternatives considered**:

- **Verbatim port** (translate Kodi `.cpp` → C# line-by-line). Rejected:
  this is a derivative work and would force the entire MediaHandler
  codebase under GPL-2.0.
- **Avoidance** (write our own scheme, ignore Kodi). Rejected: violates
  FR-024 and the entire premise of the feature.
- **Runtime delegation to a Kodi process / Kodi headless container**.
  Rejected: heavy operational dependency, no need to ship Kodi just to
  reuse its naming heuristics.

---

## R-002 — Sources for the re-derived regex sets and how parity is enforced

**Decision**: The behavioral oracle for parity is the set of `<regexp>` /
`<advancedsettings>` defaults that Kodi ships, observed by:

1. Reading the *defaults documentation* on the Kodi wiki
   (`advancedsettings.xml` — `tvshowmatching`, `videostacking`,
   `moviesexclude`, `tvshowexclude`, `episodeexcludes`, `tvshowexclude`,
   `cleanstrings`, `cleandatetime`).
2. Running Kodi locally against fixture inputs and observing the
   resulting library state (to disambiguate edge cases the wiki leaves
   ambiguous). This is a *behavioral* observation, not source consumption.
3. Encoding each observed `(input → expected output)` pair as an xUnit
   `[Theory]` row in `MediaHandler.Tests/Scanner/`.

The `KodiRegexCatalog` is then authored to satisfy the test table. Parity is
measured by SC-004 (≥ 99 % parity on the benchmark fixture).

**Categories to cover**:

- `tvshowmatching`: SxxExx, SxxExx-Eyy, NxNN, "Episode N", date-based
  `YYYY.MM.DD`, absolute-numbering fallback.
- `videostacking`: `cd1/cd2`, `disc1/disc2`, `part1/part2`, `(a)/(b)`,
  numeric `pt1/pt2`.
- `moviesexclude` / `tvshowexclude`: `[Ss]ample`, `-trailer`, `-sample`,
  `Extras/`, `Featurettes/`, `Trailers/`, `Specials/` *(special-cased: not
  excluded; routed to season 0)*.
- `cleanstrings` / `cleandatetime`: release-group tags, resolution tags,
  codec tags, language tags, bracketed groups, leading/trailing dots.
- `.nomedia` marker: any directory containing this file is skipped
  (subtree).
- Hidden file/folder rule: any path segment starting with `.` is skipped.

**Rationale**: Tests-first authoring guarantees parity is *measured*, not
asserted. It also gives the reviewer a single artifact to inspect to
verify the no-GPL-paste policy.

**Alternatives considered**: Hand-curating regexes without a behavioral
oracle (rejected — defeats FR-024 / SC-004).

---

## R-003 — NFO parsing approach

**Decision**: **`System.Xml.Linq` (`XDocument`) with tolerant parsing.** No
new dependency. NFO files are small, the schema is loose, and `XDocument`
handles whitespace and unknown elements gracefully.

- A *strict* NFO is well-formed XML rooted at `<movie>`, `<tvshow>`, or
  `<episodedetails>` and may carry `<title>`, `<year>`, `<uniqueid type="tmdb">`,
  `<tmdbid>`, `<season>`, `<episode>`.
- Some Kodi NFOs in the wild are **not** XML — they are bare text or HTML
  fragments containing a TMDB or IMDB URL. The parser will:
  1. Try `XDocument.Parse` first.
  2. On failure, scan the raw text with a precompiled regex for
     `themoviedb\.org/(movie|tv)/(\d+)` and `imdb\.com/title/(tt\d+)`. If
     found, treat as a hint-only NFO (TMDB id only, no title/year override).
  3. On total failure, log a warning, persist a `ReviewReason.NfoMalformed`
     decision (FR-013), and fall back to filename detection.

The result is materialized as the `NfoMetadata` entity (see `data-model.md`).

**Rationale**: BCL-only path, tolerant by construction, sufficient for the
loose Kodi NFO conventions. A dedicated library (`Aaru`-style or
`MediaInfo.NET`) would add weight for no functional gain.

**Alternatives considered**: third-party YAML/XML libs (overkill);
`XmlDocument` (older API, no advantage); attempt to import Kodi's
`NfoFile.cpp` logic (forbidden by R-001).

---

## R-004 — TMDB matcher strategy

**Decision** (FR-014, FR-016, US2):

Lookup priority (first hit wins; lower-priority sources are still recorded
as evidence on the `ScanItemDecision`):

1. **Explicit TMDB id from NFO** (`<uniqueid type="tmdb">` or `<tmdbid>` or
   embedded URL). No search, direct `GetMediaDetailsAsync`.
2. **Explicit TMDB id from filename token**: pattern `{tmdbid=12345}` or
   `[tmdbid-12345]` (Jellyfin/Kodi-compatible token). Direct lookup.
3. **Title + year**: TMDB search for `title`, year filter exact match.
4. **Title alone** (only when no year was extracted).

**Ambiguity policy**:

- If priority 3 or 4 returns **zero** results → `ReviewReason.NoTmdbResult`.
- If priority 3 returns **multiple** results with comparable popularity
  scores (top-2 popularity ratio < 1.5×) → `ReviewReason.MultipleCandidates`.
- If the only result has a release year that differs from the parsed year
  by more than **±1 year** (Kodi's default tolerance) →
  `ReviewReason.YearMismatch`.
- TV-show identity is resolved at the **show** level once; episodes are
  then mapped by `(showId, season, episode)`. Episodes that fall outside
  the show's TMDB episode range produce `ReviewReason.UnparseableEpisode`.

**Caching**: in-process `MemoryCache` keyed by `(normalizedTitle, year, kind)`
with a per-scan lifetime so the same query is never sent twice during one
`ScanRun` (FR-017).

**Episode groups (TMDB "episode_group")**: **deferred / out of scope** for
this feature. We map by absolute (season, episode) under the canonical
ordering. A future feature can introduce per-show order overrides.

**Rationale**: Mirrors Kodi's id-first preference, keeps the silent-misclass
budget under SC-002 (≤ 0.5 %), and surfaces ambiguity to the operator
rather than guessing.

**Alternatives considered**: fuzzy title match (rejected — risk of silent
misclassification); always-search-then-best-score (rejected — same risk).

---

## R-005 — Scan concurrency, cancellation, and progress reporting

**Decision**:

- **Hosted infrastructure**: a singleton `ScanRunCoordinator` registered in
  `MediaHandler.Infrastructure/Services/`. It is **not** an `IHostedService`
  (no eager background loop); it is an on-demand coordinator that owns:
  - `CancellationTokenSource _activeCts`
  - `Channel<ScanProgressDto> _progress`
  - `Task? _runningScanTask`
  - `Guid? _activeScanRunId`

- `StartScanCommandHandler` calls `coordinator.TryStart(scanRunId, ct)`
  which:
  - If a scan is already running → returns `Result.Fail("scan-in-progress")`
    → API responds 409.
  - Otherwise persists the `ScanRun` row in `Pending`, then launches the
    pipeline on a fire-and-forget `Task.Run`, returning the id immediately.
    The handler resolves with the `ScanRun` summary.

- `CancelScanCommand` calls `coordinator.RequestCancel(scanRunId)` →
  cancels the source. The pipeline checks the token at every stage
  boundary and at every NAS / TMDB call; on cancellation it transitions
  the `ScanRun` row to `Cancelled`, flushes pending decisions, and
  returns.

- **Single-active-scan invariant** is also enforced at the database level
  by a unique filtered index on `ScanRun.Status = Running`. This guards
  against process restarts mid-scan (the next start clears stale `Running`
  rows by transitioning them to `Failed` first — see startup recovery
  hook in `MediaHandlerDbContext` configuration).

- **Progress**: the pipeline writes `ScanProgressDto { phase, processed,
  total, currentPath }` snapshots to the `Channel`. Snapshots are
  consumed and persisted to `ScanRun` in batches of N (default 100) to
  avoid write amplification. The polling endpoint
  `GET /api/v1/admin/scan/{id}` reads the persisted counters; SSE/WebSocket
  streaming is *out of scope* (REST polling sufficient for an admin UI).

**Rationale**: Avoids the operational complexity of a long-lived hosted
worker (which would run even when no scan is requested) while still
delivering single-active-scan semantics, cancellation, and observable
progress.

**Alternatives considered**:

- `BackgroundService` with a queue (rejected — extra moving parts for a
  single-tenant tool).
- SignalR for live progress (rejected — out of scope for backend feature;
  REST polling is enough).

---

## R-006 — Idempotency keys for files & incremental-scan triggers

**Decision**: Each `MediaFile` carries a `Fingerprint` string computed as

```text
Fingerprint = SHA-256( normalized_lowercase_path + "|" + size_bytes + "|" + mtime_unix_seconds )
```

stored as `char(64)` (hex). Indexed unique per `LibraryRootId`.

**Incremental scan triggers**:

- **Untouched**: same path, same size, same mtime → `Unchanged`. No regex
  re-run, no NFO re-parse, no TMDB call.
- **Modified**: same path, different size or mtime → `Updated`. Re-parse,
  re-NFO, re-TMDB.
- **New**: path not seen before → `Added`. Full pipeline.
- **Vanished**: path previously seen, not seen in this scan → `Removed`.
  Apply soft-missing semantics (R-007).
- **Moved/renamed**: vanished-at-A and new-at-B with **same size + same
  mtime within ±2 s** → treat as the same `MediaFile` (path update only),
  preserving its `Media`/`TvEpisode` association. This is a best-effort
  heuristic; ambiguous cases (multiple candidates with the same size) are
  treated as `Removed` + `Added` and the soft-missing flow handles
  reconciliation.

**Rationale**: Path+size+mtime is Kodi's de-facto identity key and is cheap
on every NAS. Hashing path is purely for index size predictability;
content-hashing entire files is rejected (too expensive on a NAS).

**Alternatives considered**: content hashing (rejected — IO-prohibitive);
path alone (rejected — false unchanged after edits).

---

## R-007 — "Missing" vs. "deleted" semantics

**Decision**: A `MediaFile` not seen during a scan is **not** physically
deleted. The pipeline:

1. Sets `MediaFile.MissingSince = ScanRun.StartedAt` (if currently null).
2. Emits a `ScanItemDecision { Kind = Removed, ... }`.
3. Surfaces a `ReviewItem { Reason = ... }` only when the parent `Media`
   would be left with **zero non-missing files** (i.e., the whole movie
   or whole show is gone).
4. The administrator can either:
   - **Confirm deletion** — `POST /api/v1/admin/review-items/{id}/resolve`
     with action `delete` → physically removes the `MediaFile`(s) and
     orphaned parent if applicable.
   - **Wait** — files that reappear in a subsequent scan have
     `MissingSince` cleared automatically (NAS share was unreachable, item
     was temporarily moved, etc.).

**Rationale**: Spec edge case "NAS share temporarily unreachable mid-scan"
(spec line 88). Soft-missing prevents catastrophic data loss from a
flaky mount; explicit admin action removes ghosts.

**Alternatives considered**: hard delete on first vanish (rejected —
unsafe); never delete (rejected — leaks rows forever).

---

## R-008 — Stacking detection algorithm and persistence

**Decision**:

- Stacking is detected within a *single directory* only (Kodi behavior).
- After the directory's video files are listed, the detector strips the
  stacking suffix from each candidate using the `videostacking` regex
  family (R-002). Files that share the same *stripped key* (case-insensitive,
  punctuation-normalized) AND the same parent directory AND yield
  consistent `(title, year)` after parsing form a `StackGroup`.
- The `StackGroup` entity carries `MediaId` and an ordered list of
  `MediaFile`s with `Role = StackedPart` and a numeric `PartNumber`
  derived from the stacking token (`cd1` → 1, `disc2` → 2, `(a)` → 1,
  `(b)` → 2, etc.).
- A single-file movie is **not** wrapped in a `StackGroup` — only multi-part
  groupings get a row, keeping the table small.
- TV episode files containing multi-episode tokens (`S02E05-E06`) are
  modeled differently: the `MediaFile` is single, but multiple `TvEpisode`
  rows link to it via the `EpisodeFileLink` join entity (one row per
  episode in the file). This is **not** stacking — distinct concept.

**Rationale**: Matches Kodi's directory-local stacking rule and produces
a queryable schema for the admin UI to render "X-of-Y parts".

**Alternatives considered**:

- Cross-directory stacking (rejected — Kodi doesn't do it; produces
  false groupings).
- Encoding stacking as a JSON column on `Media` (rejected — not queryable,
  breaks the FK story for `MediaFile.StackGroupId`).

---

## R-009 — Test fixture authoring policy

**Decision**: Fixture inputs (file paths) and expected outputs (parsed
title/year/season/episode/decision-kind) are authored as `[Theory]` data
rows in C# files **derived from public Kodi documentation and
behavioral observation**, not from reading Kodi source. The fixture
files MUST NOT contain copy-pasted Kodi `.cpp` excerpts in comments.

When in doubt about Kodi behavior on a specific input, the procedure is:

1. Run Kodi locally against the input.
2. Record the resulting library state.
3. Encode it as a test row.
4. Comment cites the input + observed behavior, *never* the Kodi source
   line that produced it.

This protects R-001 even at the test level.

---

## Open questions resolved

| Question | Resolution |
|---|---|
| Use `IHostedService` for scan worker? | No — singleton coordinator with on-demand `Task.Run` (R-005). |
| Hash file content for fingerprint? | No — path+size+mtime SHA-256 (R-006). |
| Soft-delete vs. hard-delete missing files? | Soft, with admin confirmation (R-007). |
| Cross-directory stacking? | No — directory-local only (R-008). |
| TMDB episode-group ordering? | Deferred (R-004). |
| New NuGet packages? | None (`System.Xml.Linq`, `System.Threading.Channels`, `MemoryCache` all in the BCL / already referenced). |
| GPL contamination risk? | Mitigated by clean-room policy + folder-level README + code-review checklist (R-001, R-002, R-009). |


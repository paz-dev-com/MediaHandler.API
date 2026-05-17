# Checklist: Definition of Done — Per User Story

**Purpose**: Per-story DoD gate. A story is **not** done until every box for that story is ticked.
**Scope**: US1, US2, US3, US4 from `spec.md`. Cross-references task IDs from `tasks.md` and acceptance scenarios from `spec.md`.
**How to use**: Run at the end of each story's checkpoint. Owner ticks; reviewer countersigns.

---

## US1 — Scan a Library Root and List Movies/Episodes

- [ ] CHK001 - Independent test recipe from `quickstart.md` for US1 executes end-to-end and passes (fresh DB, sample NAS fixture, single command)
- [ ] CHK002 - All US1 unit tests green (parser, stacker, excluder unit suites: T053–T056, T062–T066)
- [ ] CHK003 - All US1 integration tests green (scan orchestrator happy path, idempotency T067, incremental T061)
- [ ] CHK004 - **SC-001** measured and met: full scan of the fixture corpus completes within the documented time budget; metric recorded in `benchmark-report.md` (T116)
- [ ] CHK005 - **SC-005** measured and met: incremental rescan touches < 25% of files vs full scan; recorded in `benchmark-report.md`
- [ ] CHK006 - Acceptance scenario "operator starts a scan and sees a populated library" from spec.md §US1 verified manually
- [ ] CHK007 - Checkpoint criteria for US1 in `tasks.md` Phase 3 met (every task in the phase closed)
- [ ] CHK008 - Demo script for US1 (in quickstart.md or PR description) runs cleanly on a clean checkout
- [ ] CHK009 - No new `[Skip]` or `[Ignore]` attributes added to US1 tests

---

## US2 — Triage Ambiguous Items via Review Queue

- [ ] CHK010 - Independent test recipe from `quickstart.md` for US2 executes end-to-end and passes
- [ ] CHK011 - All US2 unit tests green (review item creation rules, ambiguity classification)
- [ ] CHK012 - All US2 integration tests green, including the **review-queue round-trip** (T085): create scan with ambiguity → ReviewItem appears → admin resolves → MediaFile updated → ReviewItem closed
- [ ] CHK013 - **SC-002** measured and met (ambiguous items surface within the documented latency / accuracy bound); recorded
- [ ] CHK014 - Acceptance scenarios in spec.md §US2 (ambiguous match, no-match, multi-candidate) all verified
- [ ] CHK015 - `ReviewItem.Candidates` (JSON column, T041) populated with the full candidate list, not truncated
- [ ] CHK016 - Resolve and dismiss endpoints (`contracts/review-items.md`) both covered by tests
- [ ] CHK017 - Checkpoint criteria for US2 in `tasks.md` met
- [ ] CHK018 - Demo script for US2 runs cleanly

---

## US3 — Honor NFO Metadata Overrides

- [ ] CHK019 - Independent test recipe from `quickstart.md` for US3 executes end-to-end and passes
- [ ] CHK020 - All US3 unit tests green (NFO discovery, NFO XML mapping)
- [ ] CHK021 - Integration test: valid `movie.nfo` overrides TMDB-derived metadata; verified by reading the resulting `MediaFile`/`Movie` row
- [ ] CHK022 - Integration test: malformed NFO **falls back** to filename parse AND creates a `ReviewItem` with reason `NfoParseError` (T097–T098)
- [ ] CHK023 - Integration test: `tvshow.nfo` at show root applies to the Show entity (not per-episode)
- [ ] CHK024 - Integration test: precedence between `<basename>.nfo` and `movie.nfo` matches the documented rule
- [ ] CHK025 - Acceptance scenarios in spec.md §US3 verified
- [ ] CHK026 - Checkpoint criteria for US3 in `tasks.md` met
- [ ] CHK027 - Demo script for US3 runs cleanly

---

## US4 — Diagnose Scan Issues

- [ ] CHK028 - Independent test recipe from `quickstart.md` for US4 executes end-to-end and passes
- [ ] CHK029 - All US4 unit tests green (error capture, structured logging contracts)
- [ ] CHK030 - **SC-006** measured and met: every file processed during a scan is diagnosable — verified by running a fixture with N known-bad files and asserting N corresponding `ScanError` / `ReviewItem` rows exist
- [ ] CHK031 - For any file in the fixture, an admin can answer "what happened to this file?" using only API responses (no DB access required) — covered by the diagnostic endpoints in `contracts/scan.md`
- [ ] CHK032 - Structured logs (Serilog, T107) include `ScanRunId`, `LibraryRootId`, `FilePath` properties on every per-file log line
- [ ] CHK033 - Acceptance scenarios in spec.md §US4 verified
- [ ] CHK034 - Checkpoint criteria for US4 in `tasks.md` Phase 6 met
- [ ] CHK035 - Demo script for US4 runs cleanly and visibly demonstrates "every file diagnosable"

---

## Cross-Story Gate

- [ ] CHK036 - All four stories' boxes ticked before merging the feature branch
- [ ] CHK037 - `benchmark-report.md` (T116) published with measured SC-001, SC-005, SC-006 numbers
- [ ] CHK038 - Parity report (SC-004) ≥ 99% — see `kodi-behavioral-parity.md` checklist


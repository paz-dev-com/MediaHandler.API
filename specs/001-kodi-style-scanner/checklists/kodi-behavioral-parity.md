# Checklist: Kodi Behavioral Parity (FR-024, SC-004)

**Purpose**: Validate that the parser/stacker/excluder behaviorally matches Kodi for the documented input set, achieving SC-004 ≥ 99% parity on the corpus fixture.
**Scope**: `KodiRegexCatalog.cs`, `MovieParser`, `EpisodeParser`, `Stacker`, `Excluder`, NFO discovery, and the parity test harness (T053–T056, T062–T066, T109).
**How to use**: Tick after the parity fixture run completes. Each unticked item indicates a missing parity case that must be added to the corpus or fixed in the parser.

## Layout Coverage

- [ ] CHK001 - Per-movie-folder layout (`Movies/Inception (2010)/Inception.mkv`) parses to title="Inception", year=2010 (cross-ref T053, T062)
- [ ] CHK002 - Flat movie layout (`Movies/Inception (2010).mkv`) yields identical result as CHK001
- [ ] CHK003 - Mixed layout (movies + extras + subdirs in same root) classifies extras as exclusions, not movies (T064, T109)
- [ ] CHK004 - TV show with `Show Name/Season 01/...` produces season=1 (T054, T063)
- [ ] CHK005 - TV show with `Show Name/Season XX/...` (zero-padded) produces correct season (T063)
- [ ] CHK006 - `Specials/` folder maps to season=0 (T063)
- [ ] CHK007 - Loose episode at show root (no Season folder) is parsed and either matched or sent to review (T063, FR-019)
- [ ] CHK008 - Multi-show single root (siblings) does not cross-contaminate show identity

## Filename Pattern Coverage (Episodes)

- [ ] CHK009 - `S01E05` parses (season=1, episode=5) (T054)
- [ ] CHK010 - `S01E05-E07` parses as multi-episode {5,6,7} and creates multiple `EpisodeFileLink` rows (T020, T088)
- [ ] CHK011 - `S01E05E06` (no dash) parses as multi-episode (T054)
- [ ] CHK012 - `1x05` parses (season=1, episode=5)
- [ ] CHK013 - `2024.03.15` air-date pattern parses to date and resolves via TMDB date lookup
- [ ] CHK014 - Absolute episode numbering (e.g., `Show - 125`) parsed when no SxxExx present, and queued for review when ambiguous (FR-019)
- [ ] CHK015 - Case-insensitive matching: `s01e05`, `S01E05`, `s01E05` all match identically

## Movie Filename Patterns

- [ ] CHK016 - Year in parentheses `Title (2010)` extracted as year=2010
- [ ] CHK017 - Year without parens `Title.2010.1080p` extracted, dots stripped from title (T053)
- [ ] CHK018 - Source/quality tags (`1080p`, `BluRay`, `x264`, `WEB-DL`) stripped from title before TMDB query (T086)
- [ ] CHK019 - Release-group suffix (`-RARBG`, `-YTS`) stripped from title

## Stacking Conventions (T055, T065)

- [ ] CHK020 - `Movie.cd1.mkv` + `Movie.cd2.mkv` stack into a single MediaFile with parts ordered 1,2
- [ ] CHK021 - `Movie.part1.mkv` / `Movie.part2.mkv` stack
- [ ] CHK022 - `Movie.disc1.mkv` / `Movie.disc2.mkv` stack
- [ ] CHK023 - `Movie.a.mkv` / `Movie.b.mkv` stack (single-letter convention)
- [ ] CHK024 - `Movie.pt1.mkv` / `Movie.pt2.mkv` stack
- [ ] CHK025 - Mixed conventions in same folder do NOT incorrectly stack (e.g., `cd1` + `part2`)
- [ ] CHK026 - Stack ordering is deterministic and persisted (`StackPart` index on `MediaFile`)
- [ ] CHK027 - A 3+ part stack (cd1/cd2/cd3) is preserved fully

## Exclusion Patterns (T056, T066, T064)

- [ ] CHK028 - Files matching `*sample*` (case-insensitive) are excluded
- [ ] CHK029 - Files matching `*trailer*` are excluded
- [ ] CHK030 - Any file under `Extras/`, `Featurettes/`, `Trailers/` (any depth) is excluded
- [ ] CHK031 - Hidden files / dot-files (`.DS_Store`, `._foo.mkv`) are excluded
- [ ] CHK032 - Any directory containing `.nomedia` is fully skipped (recursive) (FR-024)
- [ ] CHK033 - Excluded files do NOT appear in `MediaFile`, `ReviewItem`, or scan-error tables
- [ ] CHK034 - Excluded files ARE counted in `ScanRun.FilesSkipped` for diagnosability (SC-006)

## NFO Discovery (FR-009, T097–T098)

- [ ] CHK035 - `movie.nfo` adjacent to a movie file is discovered and applied
- [ ] CHK036 - `<basename>.nfo` (matching the media filename) is discovered and applied
- [ ] CHK037 - `tvshow.nfo` at show-root level is discovered and applied to the Show entity
- [ ] CHK038 - When both `movie.nfo` and `<basename>.nfo` exist, precedence is documented and deterministic
- [ ] CHK039 - NFO discovery walks upward only the documented number of levels (no infinite parent traversal)

## Parity Fixture Gate (SC-004)

- [ ] CHK040 - Parity fixture corpus under `tests/Fixtures/KodiParity/` contains ≥ N items covering CHK001–CHK039 categories (N documented in T109)
- [ ] CHK041 - Parity test run reports ≥ 99% match rate on the corpus (SC-004)
- [ ] CHK042 - Every parity miss has a JIRA/issue link or a documented intentional deviation in `Scanner/README.md`
- [ ] CHK043 - Parity report artifact is published by CI for the PR (T109, T116)


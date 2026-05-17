# Feature Specification: Kodi-Style NAS Library Scanner

**Feature Branch**: `001-kodi-style-scanner`
**Created**: 2026-03-19
**Status**: Draft
**Input**: User description: "Rewrite/refactor MediaHandler API NAS scanning to follow Kodi's proven scanning logic for movies and TV shows (folder/file naming conventions, stacking, multi-part files, season/episode detection, NFO handling where applicable, exclusion rules, library structure recognition), with reliable TMDB mapping and reduced misclassifications. Admin-only operations remain admin-gated."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reliable Movie & TV Show Discovery from a NAS Library (Priority: P1)

As an administrator of the Media Handler instance, I trigger a scan of one or more NAS folders and receive a complete, accurate inventory of every movie and TV show present, with each item correctly classified (movie vs episode), correctly titled, correctly versioned (year), and correctly grouped (movie parts, episodes per season). The classification follows the same conventions used by Kodi so that any library already organized for Kodi is recognized out-of-the-box.

**Why this priority**: This is the core promise of the product. Without a trustworthy, low-error inventory, every other feature (TMDB enrichment, user views, wishlists, watch tracking) is built on bad data. The current implementation produces too many errors, so fixing this is the foundation of all further work.

**Independent Test**: Point a scan at a representative NAS root containing a known mix of correctly-organized movies and TV shows (including edge cases such as multi-part movies, multi-disc releases, specials, sample files, and extras). Verify that every item the user expects to be detected appears exactly once, classified correctly, with the correct title and year (movies) or show/season/episode (TV), and that nothing the user expects to be excluded (samples, trailers, extras, non-video files) is included.

**Acceptance Scenarios**:

1. **Given** a NAS folder containing the directory `Movies/The Matrix (1999)/The Matrix (1999).mkv`, **When** the administrator runs a scan of that root, **Then** the system records exactly one movie titled "The Matrix" with year 1999, mapped to the corresponding TMDB entry.
2. **Given** a TV show folder structured as `TV Shows/Breaking Bad/Season 01/Breaking.Bad.S01E01.mkv` (and additional episodes), **When** the administrator runs a scan, **Then** the system records the show "Breaking Bad" with one season containing the correct number of episodes, each mapped to the right TMDB episode by season+episode number.
3. **Given** a movie split across files such as `Movie.Name.2010.cd1.mkv` and `Movie.Name.2010.cd2.mkv` in the same folder, **When** the scan runs, **Then** the system records a single movie with multiple file parts (a "stacked" movie), not two separate movies.
4. **Given** a folder containing `Movie (2020)/Movie (2020).mkv` plus `Movie (2020)/Sample/movie-sample.mkv` and `Movie (2020)/Extras/behind-the-scenes.mkv`, **When** the scan runs, **Then** only the main movie file is registered; sample and extras files are ignored.
5. **Given** a TV show directory containing a `Specials` (or `Season 00`) folder, **When** the scan runs, **Then** episodes inside it are registered as season 0 (specials) and not confused with regular season 1.
6. **Given** an existing library that has already been scanned, **When** the administrator triggers a re-scan, **Then** previously discovered items are not duplicated, removed files are detected as missing, and only new/changed files cause new processing.
7. **Given** a non-administrator authenticated user attempts to trigger a scan, **When** the request is made, **Then** it is rejected with an authorization error and no scan is initiated.

---

### User Story 2 - Accurate TMDB Mapping with Confident Title/Year Extraction (Priority: P1)

As an administrator, I expect every detected movie and TV show to be matched to the correct TMDB entry, even when filenames are noisy (release group tags, resolution tags, codec tags, language tags, brackets, dots vs spaces). When an unambiguous match cannot be made automatically, the item is flagged so I can review it rather than being silently mismatched to the wrong title.

**Why this priority**: Wrong TMDB mapping is the most user-visible failure (wrong poster, wrong synopsis, wrong episode list). It is just as critical as discovery itself.

**Independent Test**: Provide a fixture set of filenames known to be problematic (e.g., `The.Movie.2018.1080p.BluRay.x264-GROUP.mkv`, `Movie Name [2015] {tmdbid=12345}.mkv`, `Show.S02E05-E06.mkv`) and verify each is parsed into the expected (title, year) or (show, season, episode(s)) tuple and matched to the correct TMDB record. Verify that ambiguous cases are surfaced for review instead of silently mis-mapped.

**Acceptance Scenarios**:

1. **Given** a file `The.Dark.Knight.2008.1080p.BluRay.x264-GROUP.mkv`, **When** parsed, **Then** the extracted title is "The Dark Knight", the year is 2008, and TMDB returns the matching movie.
2. **Given** a file with an explicit TMDB id hint (e.g., a recognized id token in the file/folder name or sidecar), **When** parsed, **Then** the system uses that id directly and skips title-based search.
3. **Given** a TV episode filename `Show.Name.S02E05-E06.mkv`, **When** parsed, **Then** the system records two episodes (S02E05 and S02E06) sharing the same physical file.
4. **Given** an item whose title yields multiple TMDB candidates with comparable scores, **When** the scan completes, **Then** the item is recorded as "unmatched / needs review" with the candidates available, instead of silently being assigned to one of them.
5. **Given** a file lacking a year and matching multiple movies, **When** parsed, **Then** the item is flagged as ambiguous rather than auto-assigned.

---

### User Story 3 - NFO Sidecar Files Override Auto-Detection (Priority: P2)

As an administrator who curates a library, when I place a Kodi-style NFO file next to a movie or TV show (e.g., `movie.nfo`, `tvshow.nfo`, episode-level `.nfo`), the scanner trusts the metadata in the NFO (title, year, TMDB id when present) over its own filename guesses, mirroring Kodi's behavior.

**Why this priority**: NFO support is the standard escape hatch for any library item the parser cannot classify automatically. It is essential for power users but not blocking for first-day usefulness if filename parsing is solid.

**Independent Test**: Place a movie file in a folder with a `movie.nfo` containing a TMDB id that differs from what filename parsing would guess. Run the scan. Verify the entry is mapped using the NFO's TMDB id, not the parser's guess.

**Acceptance Scenarios**:

1. **Given** a folder containing a movie file plus a `movie.nfo` with a `<tmdbid>` element, **When** scanned, **Then** the system uses the NFO's TMDB id as the authoritative match.
2. **Given** a TV show folder containing a `tvshow.nfo` with a TMDB id, **When** scanned, **Then** the show is mapped via that id and episode files are mapped to that show's episodes.
3. **Given** a malformed NFO file, **When** scanned, **Then** the scanner logs the issue, falls back to filename-based detection, and does not abort the overall scan.

---

### User Story 4 - Visibility Into Scan Outcomes and Errors (Priority: P2)

As an administrator, after a scan completes I can see a summary of what happened: how many items were added, updated, removed, skipped (excluded), and flagged as unmatched/ambiguous. I can drill into the unmatched/ambiguous list and see the file path and the reason, so I can fix folder structure or add an NFO and re-scan.

**Why this priority**: Without visibility, "fewer errors" is unverifiable. This story is what lets the administrator trust and improve the library over time.

**Independent Test**: Run a scan over a library containing intentional problem files (a sample, an extras file, a misnamed movie, an episode in the wrong season folder). Confirm the resulting scan report counts each category correctly and that the misnamed movie shows up in the "needs review" list with a useful reason.

**Acceptance Scenarios**:

1. **Given** a completed scan, **When** the administrator views the scan report, **Then** counts are shown for: added, updated, removed, excluded-by-rule, and needs-review.
2. **Given** an item flagged as needs-review, **When** the administrator inspects it, **Then** the file path and a human-readable reason (e.g., "no year, multiple TMDB matches", "could not parse season/episode", "title not found on TMDB") are visible.

---

### Edge Cases

- A file's parent folder name and the file name disagree (e.g., folder says `Inception (2010)` but the file is named `random.mkv`): folder-level metadata takes precedence, consistent with Kodi.
- A TV show contains a "loose" episode file at the show root with no season folder: the season is inferred from the filename (e.g., `S03E04`); if no season info exists, the file is flagged as needs-review.
- Episode numbering schemes other than `SxxExx` (e.g., `1x05`, `episode 5`, date-based `2024.03.19`): supported where Kodi supports them; otherwise flagged.
- A file matches a video extension but is actually a trailer (`*-trailer.mkv`), sample (`sample.*`, `*-sample.*`), or lives in `Extras/`, `Featurettes/`, `Trailers/`: excluded by rule.
- A movie folder contains multiple unrelated video files: the largest "main" file is chosen; secondary files are flagged unless they match stacking conventions (`cd1/cd2`, `part1/part2`, `disc1/disc2`).
- Files inside hidden folders, or folders flagged with a `.nomedia`-style exclusion marker, are skipped.
- A previously scanned file has been moved or renamed: the system detects the disappearance from the old path and the new appearance, and (where reasonably possible) treats it as the same library item rather than removing-and-re-adding.
- The NAS share is temporarily unreachable mid-scan: the scan ends gracefully, reports a partial result, and does not delete previously known items based on a failed read.
- Filenames with non-ASCII characters, accented characters, or alternate scripts must be parsed and matched correctly (no mojibake, no silent drops).
- TMDB API is rate-limited or temporarily unavailable: items still get parsed and stored locally; TMDB enrichment is retried later without forcing a full re-scan.

## Requirements *(mandatory)*

### Functional Requirements

#### Library structure recognition

- **FR-001**: System MUST recognize the standard Kodi library layouts for movies (per-movie folder, flat folder of movies, mixed) and for TV shows (`Show/Season XX/Episode` and common variants).
- **FR-002**: System MUST classify each discovered video as either a movie, a TV episode, or excluded, using the same heuristics Kodi uses (folder context, presence of season/episode tokens, NFO type, exclusion markers).
- **FR-003**: System MUST allow an administrator to register one or more NAS root paths and tag each root as "movies", "tv shows", or "mixed", and apply the appropriate classification rules per root.

#### File and folder parsing

- **FR-004**: System MUST extract a movie's title and year from filename and/or folder name using the same regex/cleanup pipeline Kodi uses (strip release tags, resolution, codec, source, language, brackets; normalize separators; recover year in parentheses or trailing position).
- **FR-005**: System MUST detect TV season and episode numbers from filenames using the patterns Kodi supports (e.g., `SxxExx`, `SxxExx-Eyy`, `xXy`, date-based `YYYY.MM.DD`, and absolute-numbering fallbacks where applicable).
- **FR-006**: System MUST detect "specials" (season 0) via a `Specials` folder or `Season 00` folder, matching Kodi behavior.
- **FR-007**: System MUST detect stacked / multi-part movies (`cd1/cd2`, `part1/part2`, `disc1/disc2`, `a/b` suffix, etc.) and group them as a single movie with multiple physical parts.
- **FR-008**: System MUST treat the same physical episode file containing multiple episodes (e.g., `S02E05-E06`) as multiple logical episodes pointing at the same file.

#### Inclusion / exclusion rules

- **FR-009**: System MUST only consider files whose extensions are in Kodi's recognized video extension list.
- **FR-010**: System MUST exclude files matching Kodi's sample/trailer/extras patterns and files within excluded subfolders (`Extras`, `Featurettes`, `Trailers`, `Sample`, etc.), as well as hidden files and folders.
- **FR-011**: System MUST honor an opt-out marker file (a Kodi-equivalent `.nomedia` / per-folder exclusion) that causes the entire folder subtree to be skipped.

#### NFO handling

- **FR-012**: System MUST detect Kodi-style NFO sidecar files (`movie.nfo`, `tvshow.nfo`, per-episode `.nfo`, per-file `<filename>.nfo`) and use them to override auto-detection of title, year, and TMDB id when present.
- **FR-013**: System MUST gracefully fall back to filename-based detection if an NFO is missing, unreadable, or malformed, recording a warning in the scan report.

#### TMDB mapping

- **FR-014**: System MUST query TMDB for each unique movie or TV show using the most authoritative signal available (NFO TMDB id > explicit id token in name > title+year > title alone), and persist the resulting TMDB id on the library entry.
- **FR-015**: System MUST map TV episodes to TMDB episode records by show id + season + episode number(s).
- **FR-016**: System MUST mark an item as "needs review" when no confident TMDB match can be established (no result, multiple comparable candidates, or year mismatch beyond a tolerance) instead of silently assigning a wrong match.
- **FR-017**: System MUST cache TMDB lookups during a scan so that the same title is not queried repeatedly, and MUST tolerate transient TMDB failures without aborting the whole scan.

#### Scan lifecycle, idempotency, and reporting

- **FR-018**: System MUST support full and incremental scans; an incremental scan MUST avoid reprocessing files that have not changed.
- **FR-019**: System MUST detect files that previously existed and are now missing, and mark the corresponding library entries as missing (rather than immediately deleting them) so the administrator can confirm.
- **FR-020**: System MUST produce a per-scan report including counts of: added, updated, unchanged, removed/missing, excluded-by-rule, and needs-review, plus the list of needs-review items with a reason.
- **FR-021**: System MUST allow an administrator to manually resolve a needs-review item by assigning a TMDB id, and MUST persist that resolution so future scans do not re-flag it.
- **FR-022**: Scan operations and review/resolution actions MUST be restricted to users in the administrator role; non-admin authenticated users MUST be denied.
- **FR-023**: System MUST log scan progress and per-file decisions at a level sufficient for an administrator to diagnose why any individual file was classified, excluded, or flagged.

#### Behavior parity with Kodi

- **FR-024**: For any library structure that Kodi (current stable release as available locally at `/home/tpfeifer/Repos/xbmc-master/`) classifies correctly, the system MUST classify it the same way (same movie/show identity, same season/episode numbers, same inclusion/exclusion decision). Deviations MUST be documented and intentional.

### Key Entities *(include if feature involves data)*

- **Library Root**: A configured NAS path the scanner is authorized to read, with a content-type hint (movies / tv / mixed) and optional per-root overrides.
- **Movie**: A single logical film, identified by title and year (and TMDB id once mapped). May be backed by one or more physical files (stacked parts).
- **TV Show**: A logical series, identified by name (and TMDB id once mapped). Owns one or more Seasons.
- **Season**: A numbered grouping under a TV Show (including season 0 = specials). Owns Episodes.
- **Episode**: A single logical episode identified by show + season + episode number. May share its physical file with adjacent episodes (multi-episode files).
- **Media File**: A physical file on the NAS, with its absolute path, size, modification time, and a link to the logical Movie or Episode it backs (and its role: main, part 1, part 2, etc.).
- **Scan Run**: A single execution of the scanner, with start/end time, mode (full/incremental), root(s) scanned, summary counts, and the list of decisions per file.
- **Review Item**: A scan result that could not be auto-resolved (unmatched, ambiguous, unparseable), with file path, attempted classification, candidate matches, and reason. Resolvable by an administrator.
- **Exclusion Rule**: A pattern (extension, filename, folder name, marker file) that causes a file or subtree to be skipped, sourced from the Kodi-equivalent rule set.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a curated benchmark NAS library containing at least 200 movies and 50 TV shows organized in standard Kodi-recognized layouts (including stacked movies, specials, and multi-episode files), the scanner classifies and TMDB-maps **at least 98%** of items correctly without any manual intervention.
- **SC-002**: The rate of *silent* misclassifications (wrong TMDB match, wrong season/episode, movie classified as TV or vice-versa) on the benchmark library is **at most 0.5%**; the remainder of unresolved items appear in the needs-review queue rather than being silently wrong.
- **SC-003**: Sample files, trailer files, extras, and non-video files in the benchmark library are excluded with **100%** accuracy (zero false inclusions, and zero false exclusions of legitimate main files).
- **SC-004**: For any library item that Kodi classifies correctly, the scanner produces the same classification outcome in **at least 99%** of cases (parity test against Kodi's behavior on the same fixture set).
- **SC-005**: An incremental re-scan of an unchanged library completes in **under 25%** of the time of the initial full scan and produces zero added/updated/removed entries.
- **SC-006**: After a scan, the administrator can determine the reason any specific file was excluded, flagged, or matched in **under 30 seconds** by consulting the scan report (no need to read source code or raw logs).
- **SC-007**: Compared to the current implementation, the number of items requiring manual correction after a fresh full scan of the production library is reduced by **at least 80%**.
- **SC-008**: Zero non-administrator users are able to initiate a scan or modify a review item (verified by authorization tests).

## Assumptions

- The reference for "Kodi behavior" is the source tree available locally at `/home/tpfeifer/Repos/xbmc-master/` (upstream `xbmc/xbmc`). Where Kodi's own behavior has multiple modes (e.g., advanced settings overrides), the *default* Kodi behavior is the target.
- Reuse of Kodi logic is conceptual (porting/adapting algorithms, regex sets, and exclusion lists into the existing C# / .NET codebase under `MediaHandler.Infrastructure/Nas`). No runtime dependency on a Kodi binary or a Kodi process is introduced; no Kodi GPL code is copied verbatim into this codebase without an explicit licensing decision (flagged out of scope here).
- TMDB integration continues to be provided by the existing `MediaHandler.Infrastructure/Tmdb` service, extended as needed to support id-based lookup, episode lookup, and ambiguity reporting. No change of metadata provider.
- Persistence continues via the existing EF Core data layer and existing domain entities (`Media`, `MediaFile`, `TvSeason`, `TvEpisode`, `MediaGenre`, etc.), extended where needed for review-queue state, scan-run history, and stacked-part relationships.
- "Administrator" is the existing `UserRole` admin tier; no new role model is introduced.
- NAS access continues via the existing NAS access layer (e.g., `FreeboxNasService`); credential management, share mounting, and network reachability are pre-existing concerns and are not redesigned by this feature.
- Out of scope for this feature: UI/front-end work, video playback, transcoding, image/poster downloading beyond what TMDB enrichment already provides, music/photo libraries, and any Kodi feature unrelated to scanning (skinning, PVR, add-ons).
- Scans are initiated on demand by an administrator (and/or by an existing scheduled job, if one is already wired up); designing a new scheduling system is out of scope.
- The benchmark library used to validate Success Criteria is the administrator's own production NAS or a curated equivalent; assembling that fixture set is part of the implementation work, not a prerequisite of the spec.


# Feature Specification: Fix Scanner Title Parsing & TMDB Matching

**Feature Branch**: `003-fix-scanner-title-parsing`  
**Created**: 2025-07-15  
**Status**: Draft  
**Input**: User description: "Enhance the kodi-like scanning features — the title parser strips actual show names and keeps release tags, the scanner doesn't leverage folder hierarchy, French/localized titles aren't matched on TMDB, and the TMDB search doesn't try alternative languages."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - TV Show Title Correctly Extracted from Filename and Folder Hierarchy (Priority: P1)

As an administrator scanning a NAS library of TV shows, when the scanner encounters a file such as `Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv`, the extracted show title is "Slow Horses" (the text before the SxxExx pattern, not the release tags after it). When the filename-based title extraction fails or is ambiguous, the scanner falls back to the folder hierarchy (e.g., the parent show-level folder "Slow Horses") to determine the correct show name.

**Why this priority**: This is the root cause of the vast majority of ReviewItem failures. Every example in the ReviewItems table shows the parser returning release-group/quality tags instead of the actual show name. Without fixing this, every downstream TMDB lookup is doomed to fail. This single fix addresses the largest number of scan failures with the smallest scope.

**Independent Test**: Scan the known failing file paths from the ReviewItems table and verify each produces the correct parsed title before any TMDB lookup is attempted.

**Acceptance Scenarios**:

1. **Given** a TV episode file `Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv` inside `/Séries/Slow Horses/S03/`, **When** the scanner parses the title, **Then** the extracted show title is "Slow Horses" (from text before `S03E05`), not "MULTi 1080p WEBRip x264 AC3-MULTiViSiON".
2. **Given** a TV episode file `New.York.Unité.Spéciale.S06E21.1080p.MULTi.WEBRiP.AvALoN.mkv` inside `/Séries/Law and Order/SVU/S06/`, **When** the scanner parses the title, **Then** the extracted show title is "New York Unité Spéciale" (from filename before `S06E21`), or falls back to the folder hierarchy "Law and Order SVU".
3. **Given** a TV episode file `Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv` inside `/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY/`, **When** the scanner parses the title, **Then** the extracted show title is "Une Nounou Denfer" (from filename before `S04E10`), or falls back to "The Nanny" from the grandparent folder.
4. **Given** a TV episode file `The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv`, **When** the scanner parses the title, **Then** the extracted show title is "The Killing US" (text before `S03E10`, with the year `2011` stripped from the title but available as metadata).
5. **Given** a TV episode file with a human-readable name like `Sur écoute S04E01 - La fin de l'été.mkv` inside `/Séries/The Wire/The Wire/`, **When** the scanner parses the title, **Then** the extracted show title is "Sur écoute" (text before `S04E01`), or falls back to "The Wire" from the folder hierarchy.
6. **Given** a file where no SxxExx pattern is found in the filename but the folder structure clearly indicates a TV show (e.g., inside a `Season 03` folder), **When** the scanner parses, **Then** the show title is extracted from the folder hierarchy (show-level folder, typically 2 levels above the file).
7. **Given** a filename where the show title contains a year (e.g., `The.Killing.US.2011.S03E10`), **When** parsing, **Then** the year is NOT mistaken for a title boundary that truncates the actual show name.

---

### User Story 2 - Folder Hierarchy Used as Fallback and Validation for Show Names (Priority: P1)

As an administrator, when the file-level title parsing is unreliable (e.g., the filename has been mangled by encoders or uses a localized title), the scanner uses the folder hierarchy as the primary or validating signal for the show name. The typical TV show folder structure is `/root/Show Name/Season XX/file.mkv`, and the show-level folder name is a reliable, human-curated signal.

**Why this priority**: The folder hierarchy is curated by the library owner and is almost always correct. Many of the reported failures would be instantly resolved if the scanner used the folder name instead of (or to validate) the filename-extracted title. This is co-equal in priority with Story 1 because some filenames are irrecoverably mangled.

**Independent Test**: Scan files where the filename contains no usable show title but the parent folder clearly names the show. Verify the show title is taken from the folder.

**Acceptance Scenarios**:

1. **Given** a file `random-release-name.S02E05.mkv` inside `/Séries/Breaking Bad/Season 02/`, **When** the scanner parses, **Then** the show title is "Breaking Bad" from the folder hierarchy.
2. **Given** a file where the filename-extracted title differs from the folder name (e.g., filename says "New York Unité Spéciale" but folder says "Law and Order"), **When** the scanner parses, **Then** both titles are available for TMDB lookup — the scanner tries the filename title first and falls back to the folder title if TMDB returns no results.
3. **Given** a multi-level folder structure with sub-folders (e.g., `/Séries/Law and Order/SVU/S19/file.mkv`), **When** the scanner parses, **Then** the scanner considers additional parent folders (not just the immediate grandparent) to construct a meaningful show name, recognizing that "SVU" alone may be insufficient but "Law and Order SVU" is meaningful.
4. **Given** a folder structure where the season folder is named `S03` instead of `Season 03`, **When** the scanner determines the show-level folder, **Then** it correctly skips the `S03` folder (recognizing it as a season-level folder) and uses the next parent folder as the show name source.

---

### User Story 3 - Multi-Language TMDB Search for Localized Titles (Priority: P2)

As an administrator with a library containing French (or other localized) titles alongside English originals, when the scanner extracts a title like "Une Nounou D'enfer" or "Sur écoute", the TMDB search finds the correct show even though the title is not in English. The system queries TMDB using the library's configured language(s) and, if no result is found, retries with alternative languages (at minimum: original title and English).

**Why this priority**: This is the second most impactful fix. Many of the reported failures involve French-language filenames that TMDB would match if searched in the correct language. However, this depends on Story 1/2 extracting the correct title first, so it builds on the parsing fixes.

**Independent Test**: Submit known French TV show titles ("Une Nounou D'enfer", "New York Unité Spéciale", "Sur écoute") to the TMDB matcher and verify they resolve to the correct English-language TMDB entries (The Nanny, Law & Order: SVU, The Wire).

**Acceptance Scenarios**:

1. **Given** a parsed show title "Une Nounou Denfer" (or close variant) and a library or system configured with French (`fr-FR`) as a search language, **When** the TMDB matcher runs, **Then** it finds "Une nounou d'enfer" / "The Nanny" on TMDB.
2. **Given** a parsed show title "Sur écoute", **When** TMDB search in French returns a match, **Then** "The Wire" is correctly identified and mapped.
3. **Given** a parsed title that returns no results in the primary configured language, **When** the matcher retries, **Then** it searches TMDB in English (`en-US`) as a fallback language before routing to the review queue.
4. **Given** a parsed title that returns no results in any configured language but the folder-hierarchy title (e.g., "The Wire") is available, **When** the matcher retries with the folder title, **Then** it finds the correct TMDB entry.
5. **Given** a file whose filename title and folder title are both available, **When** the primary title search fails on TMDB, **Then** the fallback title from the folder is tried before the item is sent to the review queue.

---

### User Story 4 - Release Tag Stripping from TV Episode Filenames (Priority: P2)

As an administrator, the scanner correctly identifies and strips common release-group tags, quality identifiers, codec names, and language tags from TV episode filenames so that only the actual show title remains. Tags like `1080p`, `MULTi`, `WEBRip`, `x264`, `FRENCH`, `DVDRip`, `XviD`, `-GROUP` are never included in the parsed title.

**Why this priority**: Even after fixing the "before vs after SxxExx" bug (Story 1), the text before SxxExx may still contain release tags in some naming conventions. Robust tag stripping ensures clean titles reach TMDB.

**Independent Test**: Parse a collection of filenames with various release tag patterns and verify the extracted title contains none of them.

**Acceptance Scenarios**:

1. **Given** a filename `Law.and.Order.SUV.S19E23.FRENCH.DVDRip.XviD-Wawacity.tv.avi`, **When** parsed, **Then** the extracted show title before `S19E23` is "Law and Order SUV" with no quality or language tags.
2. **Given** a filename `Show.Name.S01E01.MULTI.1080p.BluRay.x264.DTS-GROUP.mkv`, **When** parsed, **Then** the show title before `S01E01` is "Show Name".
3. **Given** a filename where release tags appear before the SxxExx pattern (e.g., a pack-style folder name like `Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY`), **When** the scanner encounters this as a folder rather than use it as a title source, **Then** the known release-group tags are stripped to yield "Une Nounou Denfer S04" (or the season token is also recognized and stripped, yielding "Une Nounou Denfer").

---

### Edge Cases

- A filename contains no SxxExx pattern and no season/episode indicators, but resides inside a recognized TV show folder structure: the show title comes from the folder, and the item is flagged for episode number review.
- A filename has the show title in a non-Latin script (e.g., Japanese anime titles): the parser preserves the characters and passes them to TMDB as-is; TMDB's own language-matching handles resolution.
- The show folder name contains special characters or accented characters (e.g., "Séries", "L'Île de la tentation"): the parser handles Unicode correctly without corruption or truncation.
- A filename has multiple year-like numbers (e.g., `The.Killing.US.2011.S03E10`): the scanner does not truncate the title at the year; for TV episodes, the SxxExx pattern is the primary delimiter, and year-like numbers before it are retained as part of the title or stripped only if they match a known year-extraction pattern.
- The folder hierarchy has extra nesting levels (e.g., `/Séries/Law and Order/SVU/S19/file.mkv` — 4 levels deep instead of the typical 3): the scanner walks up past season-level folders to find the show-level folder.
- A show folder name is identical to a generic folder name (e.g., "Video"): the scanner does not use it as a title and instead tries the filename.
- Two show folders have the same base name (e.g., `/Séries/The Killing/` and `/Séries/The Killing US/` as separate folders): each is treated independently; files within each folder inherit that folder's title.
- TMDB returns results for the localized title but the match is a different show entirely (false positive): the year, if available, is used as a disambiguation signal; otherwise the item routes to review.

## Requirements *(mandatory)*

### Functional Requirements

#### Title extraction from TV episode filenames

- **FR-001**: System MUST extract the show title from a TV episode filename by taking the text **before** the first SxxExx (or equivalent season/episode) pattern, after stripping dots/underscores to spaces and cleaning release-group tags.
- **FR-002**: System MUST NOT return the text **after** the SxxExx pattern as the show title. Text after the SxxExx pattern is the episode title (or release tags), not the show name.
- **FR-003**: System MUST strip known release-group tags, quality identifiers (720p, 1080p, 2160p, 4K), codecs (x264, x265, XviD, HEVC, AVC), sources (BluRay, WEBRip, WEB-DL, DVDRip, HDTV, BDRip), language tags (MULTI, MULTi, FRENCH, VOSTFR, TRUEFRENCH), and release group suffixes (e.g., `-GROUP`, `-Wawacity.tv`) from the extracted title portion.
- **FR-004**: When a year-like number (e.g., `2011`) appears before the SxxExx pattern in a TV episode filename, the system MUST include it as part of the show title string passed to TMDB (e.g., "The Killing US 2011") or separately as a year hint, but MUST NOT truncate the title at the year.

#### Folder hierarchy-based title resolution

- **FR-005**: System MUST use the folder hierarchy as a title source for TV episodes. The show-level folder is identified by walking up from the file, skipping known season-level folders (matching patterns like `Season XX`, `Saison XX`, `SXX`, `Specials`, `Season 00`).
- **FR-006**: System MUST treat the folder-hierarchy title as a fallback when the filename-extracted title fails to produce TMDB results, and as a primary source when the filename yields no parseable title before the SxxExx pattern.
- **FR-007**: System MUST handle multi-level nesting (e.g., `/Show Group/Show Name/Season/file`) by skipping not just one but all season-level folders when walking up the hierarchy.
- **FR-008**: System MUST recognize the following folder names as TV-root indicators (not show titles): "Séries", "Series", "TV Shows", "TV", "Shows" (case-insensitive), and skip them when resolving the show-level folder.

#### Multi-language TMDB search

- **FR-009**: System MUST support configuring an ordered list of preferred search languages per library root or globally. Administrators configure a sequence of language codes (e.g., `["fr-FR", "en-US"]`); the system tries each in order until a TMDB match is found or the list is exhausted.
- **FR-010**: When a TMDB title search in the primary language (first in the configured sequence) returns no results, the system MUST retry the search in subsequent configured fallback languages before routing the item to the review queue. The system stops at the first language that produces a match.
- **FR-011**: When the filename-extracted title fails TMDB search across all configured languages and a different folder-hierarchy title is available, the system MUST attempt TMDB search using the folder title (trying the same language sequence) before routing to review.
- **FR-012**: The multi-language retry and folder-title fallback logic MUST NOT create duplicate TMDB lookups for the same (title, language) combination. A per-scan-session in-memory cache (implemented via `ConcurrentDictionary` keyed by `(title, language)`) deduplicates requests within a single scan execution.

#### Title cleaning pipeline

- **FR-013**: The title cleaning pipeline MUST handle accented and special characters correctly (é, è, ê, ë, ï, ü, ö, ñ, etc.) without stripping, corrupting, or transliterating them. These characters are significant for localized TMDB searches.
- **FR-014**: The title cleaning pipeline MUST normalize common filename separators (dots, underscores, hyphens used as word separators) to spaces, but MUST preserve hyphens and apostrophes that are part of actual titles (e.g., "D'enfer", "Spider-Man").

#### Backward compatibility

- **FR-015**: All existing passing scan behaviors (movies, NFO handling, stacking, exclusion rules, incremental scans, review queue) MUST continue to work as specified in the 001-kodi-style-scanner spec. This feature modifies only the title extraction and TMDB search steps.
- **FR-016**: Items previously resolved by an administrator in the review queue MUST NOT be re-flagged when the improved parser produces a different parsed title on re-scan. Resolved status is keyed by **absolute file path** alone; if the same file is scanned again with the same path, the prior resolution is suppressed and not re-inserted into the review queue.

#### Key Entities

- **MatchQuery** (modified): Extended with a single `string? FallbackTitle` property to carry a secondary/fallback title from the folder hierarchy, in addition to the primary filename-extracted title, so the TMDB matcher can try both. Set to `null` if no folder-hierarchy fallback is available.
- **ReviewItem** (unchanged): Continue to record `ParsedTitle` — which will now contain the correctly extracted title instead of release-group garbage.
- **LibraryRoot** (potentially extended): May carry a preferred TMDB search language list (ordered) if per-root language configuration is supported.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The **6** specific ReviewItem failures listed in the feature request (Law and Order SVU, The Nanny, Slow Horses, This Is Going To Hurt, The Killing US, The Wire) all produce correct show titles after parsing and successfully match on TMDB without manual intervention.
  > ℹ️ Count corrected from "7" to "6": the spec lists exactly 6 named shows. "This Is Going To Hurt" is included in the count — it appears in the original ReviewItems table (`/Séries/This Is Going To Hurt/This.is.Going.to.Hurt.S01E06.Multi.1080p.WEBRip.x265-NoTag.mkv`) but lacks a dedicated acceptance scenario; the title extraction (text before `S01E06` = "This Is Going To Hurt") is covered by the general SxxExx parsing rules.
- **SC-002**: On a re-scan of the production library, the number of `NoTmdbResult` ReviewItems caused by incorrect title parsing (release tags in parsed title) drops by at least 90% compared to the current scan results.
- **SC-003**: Files with French/localized titles that correspond to known TMDB entries are matched automatically in at least 85% of cases (compared to the current 0% match rate for the reported examples).
- **SC-004**: The title parser correctly extracts the text before SxxExx as the show title in 100% of filenames that follow the `Show.Name.SxxExx.tags.ext` convention.
- **SC-005**: All existing scanner integration tests continue to pass without modification (backward compatibility).
- **SC-006**: The overall scan duration for a full library scan does not increase by more than 15% due to additional TMDB language retries and folder-title fallbacks. Per-scan-session in-memory deduplication (ConcurrentDictionary cache by `(title, language, year?, kind?)` tuple) ensures that the same lookup is never issued twice within a single scan execution, keeping additional API calls minimal.
- **SC-007**: An administrator can see the improved parsed title in the review queue for any remaining unmatched items, enabling faster manual resolution.

## Clarifications

### Session 2025-01-23

- Q: How to model FallbackTitle in MatchQuery? → A: Add a single `string? FallbackTitle` property to `MatchQuery` to carry the folder-hierarchy-derived title alongside the primary filename-extracted title.
- Q: TMDB language fallback strategy? → A: Configurable ordered list — admin configures a sequence of language codes per library root or globally; system tries each language in order until a hit or end of list.
- Q: Release tag catalog storage & mutability? → A: Config file on disk (e.g., `appsettings.json` or sidecar YAML); reloaded on change without redeployment via .NET `IOptionsMonitor<T>`.
- Q: Re-scan identity & FR-016 suppression key? → A: Resolved status keyed by absolute file path only; same path on re-scan → suppressed, not re-flagged.
- Q: TMDB deduplication scope / SC-006 performance? → A: Per-scan-session in-memory cache (ConcurrentDictionary) keyed by `(title, language)` tuple; ensures no duplicate TMDB calls within a single scan.

## Assumptions

- The folder hierarchy of the production NAS library follows a recognizable pattern where the show name is a folder above the season folders. Libraries that use flat structures (all episodes in one folder) are not the primary target but should still benefit from filename-based parsing improvements.
- The TMDB API supports search in French (`fr-FR`) and returns French-localized titles; this is a documented TMDB capability and does not require special API access.
- The existing `ITmdbService.SearchCandidatesAsync` method already accepts a `language` parameter; the multi-language retry adds additional calls with different language values, not a new API method.
- The set of release-group tags to strip is based on commonly observed patterns in the production library (MULTi, FRENCH, VOSTFR, TRUEFRENCH, 1080p, 720p, WEBRip, BluRay, DVDRip, x264, x265, XviD, HEVC, etc.). The catalog is maintained as a **configuration file** (e.g., `appsettings.json` or a dedicated sidecar YAML file) and is reloaded on change without redeployment via .NET's `IOptionsMonitor<T>` pattern, allowing runtime updates to the tag list.
- "Fixing the parser" means modifying `KodiNameParser.ParseEpisode` and `ExtractEpisodeTitle`/`ExtractShowTitle` and the `TmdbMatcher` resolution chain. No architectural changes to the scan pipeline, domain model, or API surface are expected.
- The `MediaFileNameParser` (legacy, marked `[Obsolete]`) is not in scope for these fixes — only the Kodi-style `KodiNameParser` pipeline is modified.
- NFO-based overrides continue to take highest precedence; the title parsing and TMDB search improvements only affect the fallback chain when no NFO is present.


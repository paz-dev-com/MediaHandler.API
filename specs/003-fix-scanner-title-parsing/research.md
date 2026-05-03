# Research: Fix Scanner Title Parsing & TMDB Matching

## R1: Title Extraction Strategy — Before vs After SxxExx

**Decision**: Extract show title from text **before** the first `SxxExx` pattern in the filename, then strip release tags from that prefix.

**Rationale**: The current `KodiNameParser.ParseEpisode` delegates to `TvEpisodeMatcher` for episode number extraction but then calls `ExtractEpisodeTitle` which returns text **after** `SxxExx` — this is the *episode* title, not the *show* title. The `ExtractShowTitle` method uses a naive "hardcoded grandparent folder" approach (`segments[^3]`). The fix requires:
1. A new `ExtractShowTitleFromFilename` method that takes text before the first `SxxExx` match
2. Replacing dots/underscores with spaces
3. Applying release-tag stripping to that prefix portion

**Alternatives considered**:
- Using only the folder hierarchy: Rejected because many filenames contain valid show titles, and folder-only parsing loses accuracy for well-named files.
- Machine-learning based title extraction: Over-engineered for a personal project with deterministic filename patterns.

## R2: Release Tag Stripping Before SxxExx

**Decision**: Apply a curated regex-based cleanup pass to the text before `SxxExx`, removing known quality/codec/language/release-group tokens.

**Rationale**: Although most release tags appear after `SxxExx`, some naming conventions (especially French re-encoders) place `MULTi`, `FRENCH`, or source tags before the season identifier in pack/folder names. The `KodiRegexCatalog.MovieCleanupTokens` array already contains the relevant patterns — it can be reused in a new `CleanTvShowTitle` helper applied to the pre-SxxExx text.

**Alternatives considered**:
- Whitelist approach (only keep known-good words): Rejected because it would fail on non-English show names with unfamiliar words.
- Configuration-only tag list (no hardcoded patterns): Partially adopted — tags are defined in `appsettings.json` via `IOptionsMonitor<ReleaseTagOptions>` for runtime updates, but a sensible default set is compiled in.

## R3: Folder Hierarchy Resolution

**Decision**: Walk up from the file, skip recognized season-level folders (matching `Season XX`, `Saison XX`, `S##`, `Specials`) and TV-root indicators (`Séries`, `Series`, `TV Shows`, `TV`, `Shows`), and use the first non-season, non-root folder as the show title.

**Rationale**: The current `ExtractShowTitle` always returns `segments[^3]` which is brittle — it breaks with multi-level nesting (e.g., `/Séries/Law and Order/SVU/S19/file.mkv`). The improved algorithm:
1. Split path into segments
2. Walk upward from the file's containing folder
3. Skip any segment matching season patterns (regex) or TV-root indicators (HashSet)
4. Return the first remaining segment as the show-level folder name
5. For multi-segment show names (e.g., `Law and Order/SVU`), concatenate non-season folders between the root indicator and the season folder

**Alternatives considered**:
- Fixed depth (always 2 levels up): Rejected because real-world libraries vary in nesting depth.
- User-configurable depth per root: Over-engineered; the heuristic approach handles all observed layouts.

## R4: Multi-Language TMDB Search Strategy

**Decision**: `MatchQuery` carries an ordered list of languages (from `LibraryRoot.SearchLanguages` or global default `["en-US"]`). `TmdbMatcher` iterates through languages, calling `SearchCandidatesAsync` for each until a result is found or the list is exhausted. A per-scan `ConcurrentDictionary<(string title, string language), TmdbMatchResult>` prevents duplicate lookups.

**Rationale**: The existing `ITmdbService.SearchCandidatesAsync` already accepts a `language` parameter. The fix needs only to:
1. Call it multiple times with different language values
2. Stop on first successful match
3. Deduplicate across the scan run

**Alternatives considered**:
- Batch multi-language query in a single TMDB call: TMDB API does not support this.
- Parallel language queries: Adds complexity for marginal gain; sequential is simpler and respects rate limits.
- Language detection from filename characters: Unreliable for dot-separated ASCII filenames.

## R5: FallbackTitle in MatchQuery

**Decision**: Add `string? FallbackTitle` property to the `MatchQuery` record. When the primary title search fails across all languages, `TmdbMatcher` retries with `FallbackTitle` (derived from folder hierarchy) using the same language sequence.

**Rationale**: The folder-hierarchy title is often in a different language than the filename (e.g., filename is French "Sur écoute" but folder is English "The Wire"). Trying both titles maximizes match rate without requiring language detection.

**Alternatives considered**:
- Separate MatchQuery per title (pipeline creates two queries): Rejected because it complicates the one-file-one-result pipeline contract and doubles ReviewItem creation logic.
- List of candidate titles: Over-designed for the two-title case; a single `FallbackTitle` keeps the model simple.

## R6: Deduplication Cache Scope & Implementation

**Decision**: Replace the existing per-scan `LruCache<(string, int?, MediaType?), TmdbMatchResult>` in `TmdbMatcher` with a `ConcurrentDictionary<(string title, string language), TmdbMatchResult>` that caches by `(title, language)` pair rather than `(title, year, kind)`. This ensures the same title+language is never queried twice within a scan.

**Rationale**: The spec explicitly requires per-scan-session caching keyed by `(title, language)` (FR-012). The LRU cache is good but the key doesn't include language. Switching to `ConcurrentDictionary` is simpler (no eviction needed for ~10k files) and directly satisfies the spec.

**Alternatives considered**:
- Keep LRU with expanded key: Viable but LRU eviction is unnecessary for scan-scoped lifecycles (max ~2-3k unique titles).
- Global persistent cache (Redis/DB): Over-engineered for a personal project where TMDB results can change.

## R7: Year Handling in TV Episode Filenames

**Decision**: When a year-like number (4 digits, 1888-2099) appears in the text before `SxxExx`, preserve it as part of the title string. Do NOT truncate the title at the year the way movie parsing does.

**Rationale**: For movies, the year is a clear title boundary (e.g., "Inception.2010.1080p"). For TV episodes, the `SxxExx` pattern is the authoritative delimiter. Year-like numbers before it (e.g., "The.Killing.US.2011.S03E10") are either part of the title or a production year hint — either way they should NOT truncate the title. The year can be extracted separately as a metadata hint for TMDB disambiguation.

**Alternatives considered**:
- Strip the year but pass it as a separate query parameter: This would truncate "The Killing US 2011" to "The Killing US" which may help TMDB. Adopted as a secondary strategy — extract the year AND keep the full title, try both.

## R8: Configuration for Release Tag Catalog

**Decision**: Define default release tags as a static array in `KodiRegexCatalog` (compile-time defaults). Additionally, expose an `appsettings.json` section `Scanner:ReleaseTags` that can override/extend the list at runtime via `IOptionsMonitor<ReleaseTagOptions>`.

**Rationale**: The spec says tags should be reloadable without redeployment (via `IOptionsMonitor<T>`). A sane default set covers 95% of cases; power users can add niche groups.

**Alternatives considered**:
- Dedicated YAML sidecar file: Adds a non-standard config mechanism; `appsettings.json` with `IOptionsMonitor` is idiomatic .NET and already in use.
- Database-stored tags: Over-complex for a static pattern list.

## R9: Backward Compatibility — FR-016 Re-scan Suppression

**Decision**: The existing ReviewItem model already stores `FilePath` and `Status`. The pipeline already checks `resolvedReviewItems` by path before running TMDB resolution (line 452 of `ScanPipeline.cs`). No changes needed — the current implementation already satisfies FR-016.

**Rationale**: The code at line 452-463 of `ScanPipeline.cs` short-circuits TMDB resolution for any file path with a previously resolved ReviewItem. This behavior is keyed by absolute file path, exactly as specified.

**Alternatives considered**: None needed — existing behavior is correct.


# Tasks: Fix Scanner Title Parsing & TMDB Matching

**Input**: Design documents from `/specs/003-fix-scanner-title-parsing/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/internal-contracts.md, quickstart.md

**Tests**: Tests are included — the spec explicitly requires unit tests (KodiNameParser, TmdbMatcher) and integration tests (end-to-end scan pipeline).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Domain**: `MediaHandler.Domain/`
- **Application**: `MediaHandler.Application/`
- **Infrastructure**: `MediaHandler.Infrastructure/`
- **Unit Tests**: `MediaHandler.Tests/`
- **Integration Tests**: `MediaHandler.IntegrationTests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Extend shared models, configuration records, and domain entities that multiple user stories depend on

- [X] T001 [P] Extend `EpisodeNameParseResult` record with `EpisodeTitle` and `FolderTitle` properties in `MediaHandler.Application/Common/Models/Scanner/NameParserModels.cs`
- [X] T002 [P] Extend `MatchQuery` record with `FallbackTitle` and `SearchLanguages` properties in `MediaHandler.Application/Common/Models/Scanner/TmdbMatchModels.cs`
- [X] T003 [P] Create `ReleaseTagOptions` configuration class in `MediaHandler.Application/Common/Models/Scanner/ReleaseTagOptions.cs`
- [X] T004 [P] Add `SearchLanguages` property (`IReadOnlyList<string>?`) to `LibraryRoot` entity in `MediaHandler.Domain/Entities/LibraryRoot.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database migration, EF configuration, and DI/config wiring that MUST be complete before any user story implementation

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T005 Add `SearchLanguages` jsonb column mapping in `MediaHandler.Infrastructure/Persistence/Configurations/LibraryRootConfiguration.cs`
- [ ] T006 Generate EF Core migration `AddSearchLanguagesToLibraryRoot` via `dotnet ef migrations add` (project: `MediaHandler.Infrastructure`, startup: `MediaHandler.API`)
- [ ] T007 [P] Add `Scanner:ReleaseTags` and `Scanner:DefaultSearchLanguages` sections to `MediaHandler.API/appsettings.json`
- [ ] T008 [P] Register `IOptionsMonitor<ReleaseTagOptions>` in DI and bind to `Scanner:ReleaseTags` config section in `MediaHandler.API/Program.cs` (or service registration extension)

**Checkpoint**: Foundation ready — models extended, migration created, configuration wired. User story implementation can now begin.

---

## Phase 3: User Story 1 — TV Show Title Correctly Extracted from Filename (Priority: P1) 🎯 MVP

**Goal**: Fix `KodiNameParser.ParseEpisode` to extract the show title from text **before** SxxExx instead of returning release tags from after it. Handle year-like numbers correctly (e.g., `The.Killing.US.2011.S03E10` → "The Killing US 2011").

**Independent Test**: Parse all known failing filenames from the ReviewItems table and verify each produces the correct show title before any TMDB lookup.

### Tests for User Story 1

- [ ] T009 [P] [US1] Add unit tests for `ExtractShowTitleFromFilename` covering all acceptance scenarios (Slow Horses, New York Unité Spéciale, Une Nounou Denfer, The Killing US 2011, Sur écoute, year-handling edge cases) in `MediaHandler.Tests/Scanner/KodiNameParserTests.cs`

### Implementation for User Story 1

- [ ] T010 [US1] Add `ExtractShowTitleFromFilename` method to `KodiNameParser` that takes text before the first SxxExx match, replaces dots/underscores with spaces, and trims — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`
- [ ] T011 [US1] Update `ParseEpisode` in `KodiNameParser` to call `ExtractShowTitleFromFilename` and set `Title` to the show name (not episode title), and populate the new `EpisodeTitle` field with text after SxxExx — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`
- [ ] T012 [US1] Handle year-like numbers before SxxExx: preserve them in the title string and optionally extract as a separate year hint for TMDB disambiguation — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`

**Checkpoint**: Title extraction from filenames is correct. Running `ParseEpisode` on known failing filenames returns the show name, not release tags.

---

## Phase 4: User Story 2 — Folder Hierarchy Used as Fallback and Validation (Priority: P1)

**Goal**: Walk the folder hierarchy upward from the file, skip season-level and TV-root folders, and return the show-level folder name as `FolderTitle` in the parse result. This serves as fallback/validation for the filename-extracted title.

**Independent Test**: Parse files where the filename contains no usable show title but the parent folder clearly names the show. Verify `FolderTitle` is populated from the folder.

### Tests for User Story 2

- [ ] T013 [P] [US2] Add unit tests for `ResolveShowFolderTitle` covering season folder skip (`Season XX`, `Saison XX`, `S##`, `Specials`), TV-root skip (`Séries`, `Series`, `TV Shows`, `TV`, `Shows`), multi-level nesting (`/Law and Order/SVU/S19/`), **generic folder names that must not be used as show titles** (e.g., `Video`, `Videos`, `Media`, `Downloads`), and edge cases in `MediaHandler.Tests/Scanner/KodiNameParserTests.cs`

### Implementation for User Story 2

- [ ] T014 [P] [US2] Add TV-root indicators HashSet (`Séries`, `Series`, `TV Shows`, `TV`, `Shows`) **and generic folder names to skip** (`Video`, `Videos`, `Media`, `Downloads` — mirrors the movie-scanner `GenericFolderNames` set) plus season-folder regex patterns to `MediaHandler.Infrastructure/Nas/Scanner/KodiRegexCatalog.cs`
- [ ] T015 [US2] Implement `ResolveShowFolderTitle` method in `KodiNameParser` that walks path segments upward, skips season-level and TV-root folders, concatenates multi-segment show names (e.g., "Law and Order SVU"), and returns the show-level folder name — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`
- [ ] T016 [US2] Update `ParseEpisode` to call `ResolveShowFolderTitle` and populate `FolderTitle` in the returned `EpisodeNameParseResult` — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`

**Checkpoint**: `ParseEpisode` now returns both `Title` (from filename) and `FolderTitle` (from folder hierarchy). Files with mangled filenames get the correct show name from the folder.

---

## Phase 5: User Story 4 — Release Tag Stripping from TV Episode Filenames (Priority: P2)

**Goal**: Strip release-group tags, quality identifiers, codecs, sources, and language markers from the text before SxxExx so only the clean show title remains. Support runtime-configurable additional patterns via `IOptionsMonitor<ReleaseTagOptions>`.

**Independent Test**: Parse filenames with various release tag patterns and verify extracted titles contain none of them.

### Tests for User Story 4

- [ ] T017 [P] [US4] Add unit tests for release tag stripping covering quality tags (1080p, 720p), codecs (x264, XviD), sources (DVDRip, WEBRip), language tags (MULTi, FRENCH, VOSTFR), and release group suffixes (-GROUP, -Wawacity.tv) in `MediaHandler.Tests/Scanner/KodiNameParserTests.cs`

### Implementation for User Story 4

- [ ] T018 [US4] Add pre-SxxExx release tag patterns (quality, codec, source, language, release group) to `KodiRegexCatalog` — expose as a reusable `CleanTvShowTitle` regex array in `MediaHandler.Infrastructure/Nas/Scanner/KodiRegexCatalog.cs`
- [ ] T019 [US4] Inject `IOptionsMonitor<ReleaseTagOptions>` into `KodiNameParser` and merge additional patterns with built-in defaults in `CleanTvShowTitle` application — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`
- [ ] T020 [US4] Apply `CleanTvShowTitle` stripping in `ExtractShowTitleFromFilename` after dot/underscore replacement and before returning — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`
- [ ] T021 [US4] Handle Unicode characters correctly in the cleaning pipeline: preserve accented characters (é, è, ê, etc.), normalize filename separators to spaces but preserve title-internal hyphens and apostrophes (e.g., "D'enfer", "Spider-Man") — in `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs`

**Checkpoint**: The title cleaning pipeline produces clean show titles with no release tags, while preserving accented characters and title-internal punctuation.

---

## Phase 6: User Story 3 — Multi-Language TMDB Search for Localized Titles (Priority: P2)

**Goal**: Implement multi-language TMDB search with configurable language sequences, FallbackTitle retry, and per-scan `ConcurrentDictionary` deduplication cache. Replace the existing LRU cache in `TmdbMatcher`.

**Independent Test**: Submit known French TV show titles ("Une Nounou D'enfer", "Sur écoute") to the TMDB matcher and verify they resolve to the correct TMDB entries (The Nanny, The Wire).

**Depends on**: User Story 1 & 2 (correct titles must reach the matcher), User Story 4 (clean titles)

### Tests for User Story 3

- [ ] T022 [P] [US3] Add unit tests for multi-language TMDB resolution covering: primary language match, fallback language match, FallbackTitle retry after primary title exhausted, deduplication cache preventing duplicate API calls (same title+language+year+kind not called twice), `FallbackTitle == Title` guard (no duplicate call when both are identical), and NeedsReview when all attempts fail — in `MediaHandler.Tests/Scanner/TmdbMatcherTests.cs`

### Implementation for User Story 3

- [ ] T023 [US3] Replace `LruCache<(string, int?, MediaType?), TmdbMatchResult>` with `ConcurrentDictionary<(string title, string language, int? year, MediaType? kind), TmdbMatchResult>` in `TmdbMatcher` — use the full tuple key to avoid cache collisions between queries with same title+language but different year or media type — in `MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs`
  > ⚠️ **I2 fix**: Dropping `year` and `kind` from the cache key would cause incorrect cache hits (e.g., movie vs. TV show with same title). Keep the full 4-tuple key.
  > ⚠️ **M1 — DI lifetime**: Register `TmdbMatcher` as **Scoped** (per scan-invocation), NOT Singleton. The `ConcurrentDictionary` must reset between scan runs. Ensure `ScanPipeline` resolves it from a new scope per scan execution.
- [ ] T024 [US3] Implement multi-language iteration loop in `TmdbMatcher.ResolveAsync`: iterate through `query.SearchLanguages ?? ["en-US"]`, search TMDB with `query.Title + language`, check/update deduplication cache, and return on first match — in `MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs`
- [ ] T025 [US3] Implement FallbackTitle retry in `TmdbMatcher.ResolveAsync`: after primary title exhausts all languages, retry with `query.FallbackTitle` (if non-null and different from Title) using the same language sequence — in `MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs`
- [ ] T026 [US3] Update `ScanPipeline` to populate `FallbackTitle` (from `EpisodeNameParseResult.FolderTitle`, only if different from `Title`) and `SearchLanguages` (from `LibraryRoot.SearchLanguages` or global default) in `MatchQuery` construction — in `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs`
  > ⚠️ **F2 fix**: Set `FallbackTitle = folderTitle != parsedTitle ? folderTitle : null` — never set FallbackTitle equal to Title, or the matcher will issue a duplicate TMDB call with no benefit.
- [ ] T027 [US3] Read `Scanner:DefaultSearchLanguages` from configuration and pass as fallback when `LibraryRoot.SearchLanguages` is null — in `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs`

**Checkpoint**: TMDB matcher now tries multiple languages and the folder-derived fallback title before routing to review. French titles like "Une Nounou Denfer" match via `fr-FR` search.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Integration tests, backward compatibility verification, and end-to-end validation

- [ ] T028 [P] Create end-to-end integration test `Sc008_TitleParsingFix` covering the **6** confirmed spec acceptance scenarios (Slow Horses, Law and Order SVU, The Nanny, The Killing US, The Wire, Sur écoute) plus release-tag-only filename edge case in `MediaHandler.IntegrationTests/Scanner/Sc008_TitleParsingFix.cs`
  > ℹ️ **A1 fix**: SC-001 originally stated "7 specific failures" but only 6 shows are confirmed in the spec. Count corrected to 6 (see spec.md SC-001 note).
- [ ] T032 [P] Add regression test for FR-016 (re-scan suppression): verify that a file with a previously `Resolved` ReviewItem is **not** re-flagged on re-scan when the path is unchanged — guards against T023's cache refactoring accidentally breaking the resolved-path short-circuit in `ScanPipeline.cs` — in `MediaHandler.IntegrationTests/Scanner/Sc008_TitleParsingFix.cs` or dedicated `Fr016_RescanSuppressionTests.cs`
- [ ] T029 Verify all existing scanner unit tests pass without modification (backward compatibility SC-005) — run `dotnet test MediaHandler.Tests --filter "KodiNameParser|TmdbMatcher"`
  > ⚠️ **C2 note**: T011 changes the semantic meaning of `EpisodeNameParseResult.Title` from *episode title* to *show title*. Existing tests happen to NOT assert on `result.Title` value (confirmed at `KodiNameParserTests.cs:L214–222`), so they survive — but this is a near-miss. If any test in this suite starts asserting `result.Title == "<episode title text>"`, it will fail by design. Document this in PR description.
- [ ] T030 Verify all existing integration tests pass without modification — run `dotnet test MediaHandler.IntegrationTests`
- [ ] T031 Run quickstart.md verification checklist: confirm all 8 verification items pass (title extraction for known files, French title TMDB match, FallbackTitle match, existing tests green, scan duration ≤ 15% increase)


## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. All T001–T004 are parallelizable.
- **Foundational (Phase 2)**: Depends on Phase 1 (T004 → T005 → T006). T007 and T008 can parallel with T005–T006.
- **US1 (Phase 3)**: Depends on T001 (extended `EpisodeNameParseResult`). Can start after Phase 1.
- **US2 (Phase 4)**: Depends on T001 (extended `EpisodeNameParseResult`). Can parallel with US1.
- **US4 (Phase 5)**: Depends on US1 (builds on `ExtractShowTitleFromFilename`). Also depends on T003, T008 (ReleaseTagOptions DI).
- **US3 (Phase 6)**: Depends on T002 (extended `MatchQuery`), US1, US2, and US4 (clean titles must reach the matcher). Also depends on T004–T006 (LibraryRoot.SearchLanguages).
- **Polish (Phase 7)**: Depends on all user stories being complete.

---

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 1 — no dependencies on other stories
- **US2 (P1)**: Can start after Phase 1 — no dependencies on other stories, can parallel with US1
- **US4 (P2)**: Depends on US1 (`ExtractShowTitleFromFilename` must exist to apply tag stripping to)
- **US3 (P2)**: Depends on US1 + US2 (correct titles must reach the matcher) and T002 (MatchQuery extension)

### Within Each User Story

- Tests written FIRST, verify they FAIL before implementation
- Infrastructure/catalog changes before parser/matcher changes
- Parser changes before pipeline integration
- Unit tests before integration tests

### Parallel Opportunities

- **Phase 1**: All 4 tasks (T001–T004) target different files — fully parallelizable
- **Phase 2**: T007 and T008 can parallel with T005–T006
- **Phase 3 & 4**: US1 and US2 can be worked in parallel (different methods in the same class, but non-overlapping)
- **Phase 5**: Test task T017 can parallel with T018 (catalog changes)
- **Phase 6**: Test task T022 can parallel with T023 (cache replacement)
- **Phase 7**: T028, T032 can start while T029–T030 run existing tests

---

## Parallel Example: User Stories 1 & 2

```
# After Phase 1 completes, launch US1 and US2 in parallel:

# Developer A — US1 (filename title extraction):
T009: Unit tests for ExtractShowTitleFromFilename in KodiNameParserTests.cs
T010: Implement ExtractShowTitleFromFilename in KodiNameParser.cs
T011: Update ParseEpisode to use new method
T012: Year-handling logic

# Developer B — US2 (folder hierarchy):
T013: Unit tests for ResolveShowFolderTitle in KodiNameParserTests.cs
T014: TV-root indicators and season-folder patterns in KodiRegexCatalog.cs
T015: Implement ResolveShowFolderTitle in KodiNameParser.cs
T016: Wire FolderTitle into ParseEpisode result
```

---

## Implementation Strategy

### MVP First (User Stories 1 & 2 Only)

1. Complete Phase 1: Setup (extend models)
2. Complete Phase 2: Foundational (migration, config)
3. Complete Phase 3: US1 — Title extraction fix
4. Complete Phase 4: US2 — Folder hierarchy fallback
5. **STOP and VALIDATE**: Parse all known failing filenames → verify correct titles extracted
6. This alone resolves the root cause of the vast majority of ReviewItem failures

### Incremental Delivery

1. Setup + Foundational → Models and config ready
2. US1 + US2 → Title extraction fixed → **MVP deployed** (biggest impact)
3. US4 → Release tag stripping → Clean titles for edge cases
4. US3 → Multi-language TMDB search → French/localized titles auto-match
5. Polish → Integration tests, backward compat verification

### Suggested MVP Scope

US1 and US2 together form the natural MVP — they fix the root cause (wrong title extraction) and provide the folder fallback, which together address the vast majority of reported scan failures without requiring TMDB API changes.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US1 and US2 are both P1 and can be worked in parallel
- US3 depends on US1+US2 producing correct titles before TMDB search
- US4 is sequenced after US1 because it enhances the same `ExtractShowTitleFromFilename` method
- Existing `MediaFileNameParser` (legacy, `[Obsolete]`) is NOT in scope
- NFO-based overrides continue to take highest precedence — changes only affect the non-NFO fallback chain


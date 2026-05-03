# Quickstart: Fix Scanner Title Parsing & TMDB Matching

## Overview

This feature fixes the scanner's title extraction (extracting show name BEFORE `SxxExx` instead of release tags after), adds folder-hierarchy fallback, and enables multi-language TMDB search. All changes are internal to the scanner pipeline.

## Prerequisites

- .NET 10 SDK
- PostgreSQL (via `docker-compose up -d`)
- TMDB API key configured in user-secrets

## Key Files to Modify

| File | Change |
|------|--------|
| `MediaHandler.Application/Common/Models/Scanner/TmdbMatchModels.cs` | Add `FallbackTitle` and `SearchLanguages` to `MatchQuery` |
| `MediaHandler.Application/Common/Models/Scanner/ReleaseTagOptions.cs` | New config options class |
| `MediaHandler.Domain/Entities/LibraryRoot.cs` | Add `SearchLanguages` property |
| `MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs` | Rewrite `ParseEpisode` title extraction + folder hierarchy |
| `MediaHandler.Infrastructure/Nas/Scanner/KodiRegexCatalog.cs` | Add TV-root indicators HashSet, expose tag patterns for pre-SxxExx |
| `MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs` | Multi-language loop + FallbackTitle retry + ConcurrentDictionary cache |
| `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs` | Pass `FolderTitle` and `SearchLanguages` into MatchQuery |
| `MediaHandler.Infrastructure/Persistence/Configurations/LibraryRootConfiguration.cs` | Map `SearchLanguages` as jsonb |

## Implementation Order

### Phase 1: Title Parser Fix (Stories 1 & 4)

> ⚠️ **Dependency note**: User Story 4 (release tag stripping) builds on the `ExtractShowTitleFromFilename` method created in User Story 1. **Complete US1 implementation first** before starting US4 tag-stripping work. User Story 2 (folder hierarchy) is independent and can be developed in parallel with US1.

1. **Add `ExtractShowTitleFromFilename`** to `KodiNameParser`:
   - Find first SxxExx match position in filename
   - Take text before that position
   - Replace dots/underscores with spaces
   - Strip known release tags (from `KodiRegexCatalog`) — **US4 step, requires US1 method to exist first**
   - Trim and return

2. **Add `ResolveShowFolderTitle`** to `KodiNameParser` (parallel with step 1):
   - Walk path segments upward from file
   - Skip season patterns (`Season XX`, `Saison XX`, `S##`, `Specials`)
   - Skip TV-root indicators (`Séries`, `Series`, `TV Shows`, `TV`, `Shows`) and generic folders (`Video`, `Videos`, `Media`, `Downloads`)
   - Return first valid show-level folder name

3. **Update `ParseEpisode`** to return both titles in result

4. **Write unit tests** for all acceptance scenarios in spec

### Phase 2: Multi-Language TMDB (Story 3)

1. **Extend `MatchQuery`** with `FallbackTitle` and `SearchLanguages`
2. **Extend `LibraryRoot`** with `SearchLanguages`; add EF migration
3. **Rewrite `TmdbMatcher.ResolveInternalAsync`**:
   - Replace LRU cache with `ConcurrentDictionary<(string title, string language, int? year, MediaType? kind), TmdbMatchResult>` — full 4-tuple key to avoid movie/show collisions
   - Register `TmdbMatcher` as **Scoped** (not Singleton) so the dictionary resets per scan session
   - Add language iteration loop
   - Add FallbackTitle retry after primary title exhausted (only if `FallbackTitle != Title`)
4. **Update `ScanPipeline.BuildMatchQuery`** to populate new fields (set `FallbackTitle = null` when same as Title)
5. **Write unit + integration tests**

### Phase 3: Configuration & Polish

1. Add `ReleaseTagOptions` to DI + `appsettings.json`
2. Wire `IOptionsMonitor<ReleaseTagOptions>` into `KodiNameParser`
3. Add `Scanner:DefaultSearchLanguages` to config
4. Verify backward compatibility (all existing tests pass)

## Running Tests

```bash
# Unit tests (parser + matcher)
dotnet test MediaHandler.Tests --filter "KodiNameParser|TmdbMatcher"

# Integration tests (full scan pipeline)
dotnet test MediaHandler.IntegrationTests --filter "Sc008"

# All tests (CI gate)
dotnet test
```

## Database Migration

```bash
dotnet ef migrations add AddSearchLanguagesToLibraryRoot \
  --project MediaHandler.Infrastructure \
  --startup-project MediaHandler.API
dotnet ef database update \
  --project MediaHandler.Infrastructure \
  --startup-project MediaHandler.API
```

## Verification Checklist

- [ ] `Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv` → Title: "Slow Horses"
- [ ] `Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv` → Title: "Une Nounou Denfer"
- [ ] `The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv` → Title: "The Killing US 2011"
- [ ] `Sur écoute S04E01 - La fin de l'été.mkv` → Title: "Sur écoute"
- [ ] French title "Une Nounou Denfer" + language `fr-FR` → TMDB matches "The Nanny"
- [ ] FallbackTitle "The Wire" matches when "Sur écoute" fails in `en-US`
- [ ] All existing scanner tests pass unchanged
- [ ] Scan duration increase ≤ 15%


# Implementation Plan: Fix Scanner Title Parsing & TMDB Matching

**Branch**: `fix/scanner` | **Date**: 2025-07-15 | **Spec**: specs/003-fix-scanner-title-parsing/spec.md
**Input**: Feature specification from `/specs/003-fix-scanner-title-parsing/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Fix the Kodi-style scanner's title extraction logic so the show name before `SxxExx` is returned (not the release tags after it), leverage folder hierarchy as a fallback/validation signal, and add multi-language TMDB search with configurable language sequences. The implementation modifies `KodiNameParser`, `TmdbMatcher`, and `MatchQuery` within the existing Clean Architecture layers — no new projects or architectural changes.

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: MediatR, FluentValidation, EF Core (Npgsql), ASP.NET Core, Polly (resilience)  
**Storage**: PostgreSQL via EF Core with Npgsql provider  
**Testing**: xUnit, NSubstitute, EF Core InMemory provider, Testcontainers.PostgreSql  
**Target Platform**: Linux server (Docker)  
**Project Type**: Web service (REST API + background scanner pipeline)  
**Performance Goals**: Full library scan increase ≤15%; per-scan deduplication via ConcurrentDictionary  
**Constraints**: No new NuGet projects; changes scoped to Infrastructure (parser/matcher) and Application (models/interfaces)  
**Scale/Scope**: Personal NAS library (~10k files); TMDB API rate-limited naturally by Polly resilience pipeline

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Code Quality — Clean Architecture | ✅ PASS | Changes to `KodiNameParser`, `TmdbMatcher` stay in Infrastructure; `MatchQuery` record in Application. Domain untouched. Dependency rule preserved. |
| I. CQRS via MediatR | ✅ PASS | No new commands/queries needed — changes are internal to the scanner pipeline invoked by existing `StartScanCommand`. |
| I. Result pattern | ✅ PASS | Matcher already returns `TmdbMatchResult` (success or NeedsReview); no exceptions for business errors. |
| I. FluentValidation | ✅ N/A | No new user-input commands introduced. |
| I. Entity configuration (Fluent API) | ✅ PASS | If `LibraryRoot` gets `SearchLanguages` column, will use `IEntityTypeConfiguration<LibraryRoot>`. |
| I. Code style | ✅ PASS | File-scoped namespaces, `record` types, `#nullable enable` throughout. |
| II. Testing Standards | ✅ PASS | Unit tests for parser (title extraction) + matcher (multi-language fallback). Integration tests for end-to-end scan with known failing filenames. |
| III. User Experience | ✅ N/A | No new API endpoints; existing scan/review endpoints unchanged. |
| IV. Performance | ✅ PASS | Per-scan ConcurrentDictionary keyed by `(title, language)` prevents duplicate TMDB calls (FR-012/SC-006). |
| IV. HTTP resilience | ✅ PASS | `ITmdbService` already registered with `.AddStandardResilienceHandler()`. |
| Architecture — Dependency rule | ✅ PASS | `Domain` remains zero-reference. `Application` references Domain. `Infrastructure` references both. |
| Architecture — Secrets | ✅ PASS | TMDB API key already in user-secrets / env vars. No change needed. |
| Workflow — Branching | ✅ PASS | Work on `fix/scanner` branch. |

**Gate result**: PASS — no violations. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/003-fix-scanner-title-parsing/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (internal — no public API changes)
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
MediaHandler.Domain/
├── Entities/
│   └── LibraryRoot.cs              # Extended: SearchLanguages property
└── Enums/

MediaHandler.Application/
├── Common/
│   ├── Interfaces/
│   │   ├── IKodiNameParser.cs      # Unchanged interface
│   │   ├── ITmdbMatcher.cs         # Unchanged interface
│   │   └── ITmdbService.cs         # Unchanged interface
│   └── Models/Scanner/
│       └── TmdbMatchModels.cs      # Modified: MatchQuery gains FallbackTitle

MediaHandler.Infrastructure/
├── Nas/Scanner/
│   ├── KodiNameParser.cs           # Modified: title extraction + folder hierarchy
│   ├── TmdbMatcher.cs              # Modified: multi-language + fallback title
│   ├── KodiRegexCatalog.cs         # Modified: release tag patterns for pre-SxxExx cleaning
│   ├── TvEpisodeMatcher.cs         # Unchanged
│   └── ScanPipeline.cs             # Modified: passes folder title + language config
├── Persistence/Configurations/
│   └── LibraryRootConfiguration.cs # Modified: SearchLanguages column mapping

MediaHandler.Tests/
├── Scanner/
│   ├── KodiNameParserTests.cs      # Extended: new title extraction test cases
│   └── TmdbMatcherTests.cs         # Extended: multi-language fallback tests

MediaHandler.IntegrationTests/
├── Scanner/
│   └── Sc008_TitleParsingFix.cs    # New: end-to-end tests for spec acceptance scenarios
```

**Structure Decision**: Single existing solution structure is preserved. All changes land in existing projects following Clean Architecture boundaries.

## Complexity Tracking

> No violations found — section intentionally left empty.

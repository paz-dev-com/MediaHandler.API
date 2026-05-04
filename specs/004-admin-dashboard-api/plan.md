# Implementation Plan: Admin Dashboard API Endpoints

**Branch**: `feature/004-admin-dashboard-api` | **Date**: 2026-05-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-admin-dashboard-api/spec.md`

## Summary

Extend the MediaHandler REST API with 9 new admin endpoints and 2 entity modifications to power the admin dashboard's Scan Results Browser, TMDB reassignment, TV show grouping, batch enrichment, and file rename workflows. The implementation enriches `ScanItemDecision` records with TMDB match data, adds an `EnrichmentRun` entity for background metadata import, and introduces a `FileRenameService` for in-place NAS file renames. All new features follow the established Clean Architecture, MediatR CQRS, and Result-pattern conventions.

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: MediatR, FluentValidation, EF Core 10 (SQL Server), ASP.NET Core, Polly (resilience via `AddStandardResilienceHandler`)  
**Storage**: SQL Server via EF Core with `Microsoft.EntityFrameworkCore.SqlServer` provider  
**Testing**: xUnit, NSubstitute, EF Core InMemory (unit), Testcontainers.MsSql (integration)  
**Target Platform**: Linux server (Docker)  
**Project Type**: REST API (web service)  
**Performance Goals**: Scan decisions list < 2s for 10,000 rows; TV grouping **< 2s** for 5,000 episodes (spec SC-003); enrichment ≥ 50 entries/min; batch rename preview < 2s for 100 episodes  
**Constraints**: No new NuGet packages beyond existing stack; EF Core migrations must apply cleanly; all endpoints AdminOnly; NAS access is local filesystem (`File.Move`)  
**Scale/Scope**: Personal NAS library (~10k files); single-instance deployment; TMDB rate-limited via existing Polly pipeline

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture — Dependency rule | ✅ PASS | Domain ← Application ← Infrastructure ← API. New entities in Domain, interfaces in Application, implementations in Infrastructure, controllers in API. |
| I. CQRS via MediatR | ✅ PASS | 8 new Command/Query + Handler pairs: `ListScanDecisions`, `ReassignTmdb`, `ListTvShowGroups`, `AssignTvGroup`, `StartEnrichment`, `GetEnrichmentStatus`, `RenameFile`, `BatchRenameTvGroup`. |
| I. Result pattern | ✅ PASS | All handlers return `Result<T>`. Business errors returned as failure results, not exceptions. |
| I. FluentValidation | ✅ PASS | Validators for all commands/queries that accept user input. |
| I. Entity configuration (Fluent API) | ✅ PASS | `EnrichmentRunConfiguration` (new) and `ScanItemDecisionConfiguration` (updated) via `IEntityTypeConfiguration<T>`. No data annotations on entities. |
| I. Code style | ✅ PASS | File-scoped namespaces, `record` types for CQRS, `#nullable enable`, `required` on mandatory properties. |
| II. Testing Standards | ✅ PASS | Unit tests per handler (success + failure paths); integration tests per endpoint group via Testcontainers. |
| III. User Experience | ✅ PASS | All endpoints return `ApiResponse<T>` envelope with structured errors and HTTP codes matching the API contract. |
| IV. Performance | ✅ PASS | New indexes on `ScanItemDecisions` for all query predicates; `AsNoTracking()` on read-only queries; pagination enforced (max 100). |
| IV. HTTP resilience | ✅ PASS | `ITmdbService` already registered with `.AddStandardResilienceHandler()` — enrichment coordinator benefits automatically. |
| Architecture — Secrets | ✅ PASS | No new secrets; TMDB API key already managed. |
| Workflow — Branching | ✅ PASS | Work on `feature/004-admin-dashboard-api` branch. |

**Gate result**: ✅ ALL PASS — no violations. Proceeding to implementation.

## Project Structure

### Documentation (this feature)

```text
specs/004-admin-dashboard-api/
├── plan.md              ← This file
├── spec.md              ← Feature specification
├── research.md          ← Phase 0 research decisions
├── data-model.md        ← Entity and migration reference
├── quickstart.md        ← Dev setup and verification checklist
├── contracts/
│   └── api-endpoints.md ← API contract per endpoint
└── tasks.md             ← 44 implementation tasks
```

### Source Code

```text
MediaHandler.Domain/
├── Entities/
│   ├── ScanItemDecision.cs           ← MODIFIED: 9 new fields
│   ├── Media.cs                      ← MODIFIED: Status, NumberOfSeasons, NumberOfEpisodes
│   └── EnrichmentRun.cs              ← NEW
└── Enums/
    └── EnrichmentStatus.cs           ← NEW (Pending, Running, Completed, Failed)

MediaHandler.Application/
├── Common/
│   ├── Interfaces/
│   │   ├── IApplicationDbContext.cs  ← MODIFIED: add EnrichmentRuns DbSet
│   │   ├── IFileRenameService.cs     ← NEW
│   │   └── IEnrichmentCoordinator.cs ← NEW
│   └── Models/
│       └── TvShowGroup.cs            ← NEW (transient, no DB table)
└── Features/
    └── Dashboard/
        ├── DTOs/                     ← NEW: ScanItemDecisionDto, TvShowGroupDto, EnrichmentRunDto, FileRenameResultDto
        ├── Commands/                 ← NEW: ReassignTmdb, AssignTvGroup, StartEnrichment, RenameFile, BatchRenameTvGroup
        └── Queries/                  ← NEW: ListScanDecisions, ListTvShowGroups, GetEnrichmentStatus

MediaHandler.Infrastructure/
├── Persistence/Configurations/
│   ├── ScanItemDecisionConfiguration.cs  ← MODIFIED: 9 new columns + 4 indexes + LibraryRoot FK
│   ├── MediaConfiguration.cs             ← MODIFIED: Status, NumberOfSeasons, NumberOfEpisodes
│   └── EnrichmentRunConfiguration.cs     ← NEW
└── Services/
    ├── EnrichmentCoordinator.cs          ← NEW (background singleton, follows ScanRunCoordinator)
    └── FileRenameService.cs              ← NEW

MediaHandler.API/
├── Controllers/
│   ├── AdminScanController.cs            ← MODIFIED: add GET /{scanId}/decisions endpoint
│   ├── AdminScanDecisionsController.cs   ← NEW: reassign, TV groups, TV group assign
│   ├── AdminEnrichmentController.cs      ← NEW: start, status
│   └── AdminFilesController.cs           ← NEW: single rename, batch rename
├── Contracts/Admin/
│   └── DashboardRequests.cs / DashboardResponses.cs ← NEW
└── Extensions/
    └── DatabaseInitializer.cs            ← MODIFIED: stale EnrichmentRun cleanup
```

## Phase 0: Research Findings (Summary)

Full research details: [research.md](research.md)

| Decision | Rationale |
|----------|-----------|
| `EnrichmentCoordinator` follows `ScanRunCoordinator` singleton pattern | Identical background-task + DB-lock requirements; reuse proven pattern |
| TV groups via `GROUP BY` on `ScanItemDecision` with deterministic SHA-256 GUIDs | No DB table (FR-006); `GroupId = SHA256(scanId + "|" + parsedTitle.ToLowerInvariant())` |
| `File.Move` for atomic rename | Single `rename()` syscall on Linux ext4; compensating `File.Move` back on DB save failure |
| Reuse existing `ITmdbService` + Polly resilience | No new HTTP client; rate-limit backoff already configured (5 retries, 1s→30s cap) |
| 3 new controllers + 1 modified | SRP — each resource domain gets its own controller |
| `FirstAirDate` reuses `Media.ReleaseDate` | Existing nullable `DateTime?` field; no new column needed |
| `Genres` uses existing `MediaGenre` child records | Already normalized; enrichment upserts `MediaGenre` rows |
| `Language` is the canonical field name | Field already exists as `Language` on `Media` entity |

## Phase 1: Design Artifacts

- [data-model.md](data-model.md) — entity schemas, new fields, migration summary
- [contracts/api-endpoints.md](contracts/api-endpoints.md) — full endpoint contracts
- [quickstart.md](quickstart.md) — dev setup and verification checklist

## Phase 2: Task Breakdown

See [tasks.md](tasks.md) — **48 effective tasks** across 11 phases (T032 split into T032a–e; T016b added; T015 intentionally omitted).

**Dependency order**: Setup (T001–T009) → Foundational (T010–T019) → US8 scan pipeline (T020–T021) → US1 browser (T022–T023) → US2/US3/US5/US6 in parallel → US4/US7 (dependent pairs) → Polish (T039–T044).

**MVP scope**: T001–T025 — delivers scan browser + TMDB reassignment.

## Implementation Notes

### EnrichmentCoordinator lifecycle

```
POST /admin/enrichment/start
  → check EnrichmentRuns WHERE Status = 'Running' → 409 if exists
  → count eligible Media entries
  → if count = 0 → return 200 with totalItems: 0 (NOT 202)
  → insert EnrichmentRun (Status=Pending)
  → IEnrichmentCoordinator.StartAsync(enrichmentRunId)
    → background Task.Run via IServiceScopeFactory
    → Status → Running
    → for each entry: ITmdbService.GetMediaDetailsAsync → update Media fields
    → Polly retry (5 retries, exponential: 1s/2s/4s/8s/16s, cap 30s, HTTP 429+503)
    → TV shows: upsert TvSeason + TvEpisode child records
    → update EnrichedCount / CurrentItem every 10 entries or 5s
    → Status → Completed | Failed
```

### TV Show Group identity

```csharp
static Guid ComputeGroupId(Guid scanId, string parsedShowName)
{
    var input = $"{scanId}|{parsedShowName.ToLowerInvariant()}";
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant
    return new Guid(hash[..16]);
}
```

### File rename conventions

| Type | Format | Example |
|------|--------|---------|
| Film | `{Title} ({Year}).{ext}` | `Fight Club (1999).mkv` |
| TV episode | `{ShowName} - S{s:D2}E{e:D2} - {EpisodeName}.{ext}` | `Breaking Bad - S01E01 - Pilot.mkv` |

Episode title sourced from `TvEpisode.Name` (requires prior enrichment). `ParsedSeason`/`ParsedEpisode` from `ScanItemDecision` used as fallback.

### TMDB retry parameters

| Parameter | Value |
|-----------|-------|
| Max retries | 5 |
| Initial delay | 1 second |
| Multiplier | 2× |
| Cap | 30 seconds |
| Handled status codes | 429, 503 |

Configured via `AddStandardResilienceHandler()` already applied to `ITmdbService`. No additional Polly configuration needed.

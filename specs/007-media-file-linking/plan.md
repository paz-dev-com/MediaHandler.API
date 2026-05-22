# Implementation Plan: Media File Linking & Missing Content Detection

**Branch**: `develop` | **Date**: 2025-07-25 | **Spec**: [specs/007-media-file-linking/spec.md](spec.md)  
**Input**: Feature specification from `specs/007-media-file-linking/spec.md`

---

## Summary

Adds four capabilities to the MediaHandler API: (1) admin-only linking/unlinking of `MediaFile` records to `Media` items, (2) admin-only PATCH endpoint to set/clear a `Media.RootFolder` override, (3) an authenticated completeness endpoint for TV shows computing per-season missing episodes, and (4) a paged admin query of all unlinked files. Requires one EF Core migration (`AddMediaRootFolder`) and one new controller (`AdminMediaFilesController`). All business logic is implemented as MediatR commands/queries with FluentValidation validators and covered by xUnit tests using NSubstitute + EF Core InMemory.

---

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: MediatR 12 (CQRS), FluentValidation, EF Core 9 (SQL Server), ASP.NET Core, Serilog  
**Storage**: SQL Server via EF Core 9 (`Microsoft.EntityFrameworkCore.SqlServer`)  
**Testing**: xUnit + NSubstitute + FluentAssertions + EF Core InMemory (unit); Testcontainers.MsSql (integration)  
**Target Platform**: Linux/Windows server — ASP.NET Core 10  
**Project Type**: Web API service  
**Performance Goals**: < 500 ms p95 for completeness queries over 10,000 files + 5,000 episodes (SC-006). Achieved via eager loading + server-side pagination; no N+1 patterns.  
**Constraints**: Clean Architecture strict 4-layer dependency rule. No domain logic in controllers. No lazy loading. `AsNoTracking()` on all reads.  
**Scale/Scope**: Personal NAS-scale library (~10k files, ~5k episodes). No distributed caching required.

---

## Constitution Check

*GATE: Must pass before implementation begins. Re-checked after Phase 1 design.*

| Principle | Requirement | This Feature | Status |
|-----------|-------------|--------------|--------|
| **I. Clean Architecture** | Domain → App → Infra → API; no upward refs | `RootFolder` added to Domain entity; handlers in Application; EF config in Infrastructure; controllers in API | ✅ PASS |
| **I. CQRS via MediatR** | One handler per file, one validator per command/query in separate file | 5 new handlers + 5 new validators, each in dedicated folders | ✅ PASS |
| **I. Result pattern** | `Result<T>` for business operations; no exceptions for expected failures | All handlers return `Result<T>`; 404/400/422 via `Result.Fail(...)` | ✅ PASS |
| **I. FluentValidation pipeline** | Every command/query with user input has `AbstractValidator<T>` | All 5 commands/queries have validators registered via `ValidationBehavior` | ✅ PASS |
| **I. Entity configuration** | Fluent API in `IEntityTypeConfiguration<T>`; no data annotations | `RootFolder` added to `MediaConfiguration` via Fluent API | ✅ PASS |
| **I. Code style** | File-scoped namespaces, primary constructors, `record` DTOs, `#nullable enable` | All new files follow existing conventions | ✅ PASS |
| **II. Unit tests** | Every handler has success + failure path tests; NSubstitute | 4 new test classes covering all acceptance scenarios | ✅ PASS |
| **II. Integration tests** | Multi-step workflows tested with Testcontainers.MsSql | Link → verify → unlink workflow integration test planned | ✅ PASS |
| **II. Test naming** | `Method_State_Expected` naming pattern | All tests follow pattern (e.g., `LinkFile_WhenFileAlreadyLinkedToDifferentMedia_Returns422`) | ✅ PASS |
| **III. ApiResponse envelope** | All responses wrapped in `ApiResponse<T>` | All 5 new/modified endpoints use `ApiResponse<T>.Success(...)` / `ApiResponse.Fail(...)` | ✅ PASS |
| **III. Pagination** | List endpoints return `PagedResult<T>` with meta | `GetUnlinkedFilesQuery` returns `PagedResult<UnlinkedFileDto>` with `ApiResponseMeta` | ✅ PASS |
| **III. Versioned routes** | All endpoints under `/api/v1/` | All new routes: `/api/v1/admin/media/...` and `/api/v1/media/{id}/completeness` | ✅ PASS |
| **III. Swagger docs** | `[ProducesResponseType]` on every action | All new actions have full `[ProducesResponseType]` coverage | ✅ PASS |
| **III. Role-based access** | `AdminOnly` for admin ops | `AdminMediaFilesController` class-level `[Authorize(Policy = "AdminOnly")]` | ✅ PASS |
| **IV. Query performance** | `AsNoTracking()` for reads; `Skip`/`Take` pagination; no N+1 | Completeness query uses eager `.Include().ThenInclude()`; unlinked-files uses server-side pagination | ✅ PASS |
| **IV. No N+1** | Eager loading or explicit projection | Season completeness loaded in a single query with full `.ThenInclude()` chain | ✅ PASS |

**Verdict**: No violations. All gates pass. Implementation may proceed.

---

## Project Structure

### Documentation (this feature)

```text
specs/007-media-file-linking/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   └── api-contracts.md ← Phase 1 output
└── tasks.md             ← Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code — Files to Create or Modify

```text
MediaHandler.Domain/
└── Entities/
    └── Media.cs                                              [MODIFY — add RootFolder property]

MediaHandler.Application/
├── Common/
│   └── Interfaces/
│       └── IApplicationDbContext.cs                         [no change — EpisodeFileLinks already present]
└── Features/
    └── Media/
        ├── DTOs/
        │   └── MediaDto.cs                                  [MODIFY — add RootFolder to MediaDto; add SeasonCompletenessDto + UnlinkedFileDto]
        ├── Queries/
        │   ├── GetMediaById/
        │   │   └── GetMediaByIdQueryHandler.cs              [MODIFY — compute/pass RootFolder in projection]
        │   ├── GetMediaCompleteness/
        │   │   ├── GetMediaCompletenessQueryHandler.cs      [CREATE]
        │   │   └── GetMediaCompletenessQueryValidator.cs    [CREATE]
        │   └── GetUnlinkedFiles/
        │       ├── GetUnlinkedFilesQueryHandler.cs          [CREATE]
        │       └── GetUnlinkedFilesQueryValidator.cs        [CREATE]
        └── Commands/
            ├── LinkMediaFile/
            │   ├── LinkMediaFileCommandHandler.cs           [CREATE]
            │   └── LinkMediaFileCommandValidator.cs         [CREATE]
            ├── UnlinkMediaFile/
            │   ├── UnlinkMediaFileCommandHandler.cs         [CREATE]
            │   └── UnlinkMediaFileCommandValidator.cs       [CREATE]
            └── UpdateMediaRootFolder/
                ├── UpdateMediaRootFolderCommandHandler.cs   [CREATE]
                └── UpdateMediaRootFolderCommandValidator.cs [CREATE]

MediaHandler.Infrastructure/
└── Persistence/
    ├── Configurations/
    │   └── MediaConfiguration.cs                           [MODIFY — add RootFolder column mapping]
    └── Migrations/
        └── <timestamp>_AddMediaRootFolder.cs               [CREATE via dotnet ef migrations add]

MediaHandler.API/
├── Controllers/
│   ├── AdminMediaFilesController.cs                        [CREATE]
│   └── MediaController.cs                                  [MODIFY — add GetCompleteness action]
└── Contracts/
    └── Media/
        └── UpdateRootFolderRequest.cs                      [CREATE]

MediaHandler.Tests/
└── Features/
    └── Media/
        ├── GetMediaCompletenessQueryHandlerTests.cs         [CREATE]
        ├── FileLinkCommandHandlerTests.cs                   [CREATE]
        ├── UpdateMediaRootFolderCommandHandlerTests.cs      [CREATE]
        └── GetUnlinkedFilesQueryHandlerTests.cs             [CREATE]

MediaHandler.IntegrationTests/
└── Features/
    └── Media/
        └── MediaFileLinkingIntegrationTests.cs             [CREATE]
```

**Structure Decision**: Standard Clean Architecture 4-layer layout. No new projects. All new code slots into existing project folders. One new controller (`AdminMediaFilesController`), one EF Core migration, and one new request contract.

---

## Complexity Tracking

> No constitution violations — this section is informational only.

No fourth project, no Repository pattern, no additional abstraction layers. All complexity is inherent in the feature requirements (season completeness computation, root folder derivation). Both computations are pure LINQ over already-loaded data.

---

## Phase 0: Research — Complete

See [research.md](research.md) for full findings. All NEEDS CLARIFICATION items resolved:

| Question | Resolution |
|----------|-----------|
| Result pattern | Existing `Result<T>` in `Application.Common.Models` |
| Controller pattern | Follows `AdminFilesController` — class-level `[Authorize(Policy = "AdminOnly")]` |
| Test mock library | NSubstitute (not Moq) — confirmed by existing test code |
| Case-insensitive EF filter | `.ToLower().Contains("specials")` → translates to SQL `LOWER(name) LIKE '%specials%'` |
| Root folder algorithm | Pure string `Path.GetDirectoryName` + longest-common-prefix of split segments |
| Owned episode definition | `EpisodeFileLinks.Any(l => l.MediaFile.MediaId == mediaId)` |
| `EpisodeCount == 0` edge case | Empty `[1..0]` range → `isComplete = true`; treated as data quality issue |

---

## Phase 1: Design & Contracts — Complete

### 1.1 Domain Entity Change

**`Media.cs`** — Add one property:
```csharp
/// <summary>
///     Admin-set override for the root folder path on the NAS.
///     When non-null, returned as-is in MediaDto.RootFolder.
///     When null, the effective root folder is computed from linked MediaFile paths.
/// </summary>
public string? RootFolder { get; set; }
```

### 1.2 New / Modified DTOs

| DTO | Change |
|-----|--------|
| `MediaDto` | Add `string? RootFolder` as last positional param (non-breaking) |
| `SeasonCompletenessDto` | New record — season number, name, expected/owned counts, missing list, isComplete |
| `UnlinkedFileDto` | New record — id, filePath, fileSizeBytes, format, resolution |

Full definitions in [data-model.md](data-model.md) §2.

### 1.3 MediatR Handlers — Summary

| Handler | Type | Returns | Key Logic |
|---------|------|---------|-----------|
| `LinkMediaFileCommand` | Command | `Result<Unit>` | Idempotent link; 422 if already linked elsewhere |
| `UnlinkMediaFileCommand` | Command | `Result<Unit>` | 404 if not linked to this media |
| `UpdateMediaRootFolderCommand` | Command | `Result<Unit>` | Normalize empty→null; save override |
| `GetMediaCompletenessQuery` | Query | `Result<IReadOnlyList<SeasonCompletenessDto>>` | 400 for Film; exclude S0/specials; eager load |
| `GetUnlinkedFilesQuery` | Query | `Result<PagedResult<UnlinkedFileDto>>` | Filter `MediaId == null`; paginate; order by `FilePath` |

### 1.4 RootFolder Computation (in GetMediaByIdQueryHandler)

```csharp
private static string? ComputeCommonParent(IEnumerable<string> filePaths)
{
    var dirs = filePaths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => Path.GetDirectoryName(p.Replace('\\', '/'))?.TrimEnd('/'))
        .Where(d => !string.IsNullOrWhiteSpace(d))
        .Select(d => d!.Split('/'))
        .ToList();

    if (dirs.Count == 0) return null;
    if (dirs.Count == 1) return string.Join("/", dirs[0]);

    var common = dirs[0].AsEnumerable();
    foreach (var segments in dirs.Skip(1))
        common = common.Zip(segments, (a, b) => a == b ? a : null)
                       .TakeWhile(s => s is not null)
                       .Select(s => s!);

    var result = string.Join("/", common);
    return string.IsNullOrEmpty(result) ? null : result;
}
```

### 1.5 Completeness Query (avoiding N+1)

```csharp
var seasons = await context.TvSeasons
    .AsNoTracking()
    .Where(s => s.MediaId == request.MediaId
                && s.SeasonNumber != 0
                && !s.Name.ToLower().Contains("specials"))
    .Include(s => s.TvEpisodes)
        .ThenInclude(e => e.EpisodeFileLinks)
            .ThenInclude(l => l.MediaFile)
    .OrderBy(s => s.SeasonNumber)
    .ToListAsync(cancellationToken);
```

Single round-trip; EF Core produces a JOINed SQL query. No client-side enumeration of full tables.

### 1.6 EF Core Migration

Migration name: `AddMediaRootFolder`  
Column: `RootFolder nvarchar(max) NULL` on `Medias` table  
Command: `dotnet ef migrations add AddMediaRootFolder --project MediaHandler.Infrastructure --startup-project MediaHandler.API`

### 1.7 New Controller: AdminMediaFilesController

```
[ApiController]
[Route("api/v1/admin/media")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminMediaFilesController(ISender sender) : ControllerBase
```

| Action | Verb | Route segment | Status codes |
|--------|------|---------------|-------------|
| `LinkFile` | PUT | `{mediaId:guid}/files/{fileId:guid}/link` | 200, 401, 403, 404, 422 |
| `UnlinkFile` | DELETE | `{mediaId:guid}/files/{fileId:guid}/link` | 200, 401, 403, 404 |
| `UpdateRootFolder` | PATCH | `{mediaId:guid}/root-folder` | 200, 400, 401, 403, 404 |
| `GetUnlinkedFiles` | GET | `unlinked-files` | 200, 400, 401, 403 |

Error discrimination follows the existing `AdminFilesController` pattern: `result.Errors.FirstOrDefault()` string-prefix check.

### 1.8 Modified Controller: MediaController

Add one action to the existing `MediaController`:

```csharp
[HttpGet("{id:guid}/completeness")]
[ProducesResponseType<ApiResponse<IReadOnlyList<SeasonCompletenessDto>>>(StatusCodes.Status200OK)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public async Task<IActionResult> GetCompleteness(Guid id, CancellationToken ct)
{
    var result = await sender.Send(new GetMediaCompletenessQuery(id), ct);
    if (!result.IsSuccess)
    {
        var error = result.Errors.FirstOrDefault() ?? string.Empty;
        if (error.StartsWith("NOT_FOUND"))
            return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));
        if (error.StartsWith("MEDIA_NOT_TV_SHOW"))
            return BadRequest(ApiResponse.Fail(new ApiError("MEDIA_NOT_TV_SHOW",
                "Completeness is only supported for TV shows.")));
        return BadRequest(ApiResponse.Fail(new ApiError("VALIDATION_ERROR", error)));
    }
    return Ok(ApiResponse<IReadOnlyList<SeasonCompletenessDto>>.Success(result.Value));
}
```

---

## Phase 2: Tests — Specification

*Full implementation to be broken into tasks by `/speckit.tasks`.*

### Unit Test Classes

#### `FileLinkCommandHandlerTests`
Covers link + unlink commands. Test methods (naming convention: `Verb_State_Expected`):
- `LinkFile_WhenFileIsUnlinked_SetsMediaIdAndReturnsSuccess`
- `LinkFile_WhenFileAlreadyLinkedToSameMedia_ReturnsSuccessIdempotent`
- `LinkFile_WhenFileAlreadyLinkedToDifferentMedia_ReturnsFileAlreadyLinkedError`
- `LinkFile_WhenMediaIdDoesNotExist_ReturnsNotFound`
- `LinkFile_WhenFileIdDoesNotExist_ReturnsNotFound`
- `UnlinkFile_WhenFileIsLinkedToMedia_ClearsMediaIdAndReturnsSuccess`
- `UnlinkFile_WhenFileIsNotLinkedToMedia_ReturnsNotFound`
- `UnlinkFile_WhenFileIdDoesNotExist_ReturnsNotFound`

#### `UpdateMediaRootFolderCommandHandlerTests`
- `UpdateRootFolder_WithValidPath_SetsOverrideAndReturnsSuccess`
- `UpdateRootFolder_WithNullValue_ClearsOverride`
- `UpdateRootFolder_WithEmptyString_TreatsAsNullAndClearsOverride`
- `UpdateRootFolder_WhenMediaNotFound_ReturnsNotFound`

#### `GetMediaCompletenessQueryHandlerTests`
- `GetCompleteness_TvShowWithMissingEpisodes_ReturnsCorrectMissingList`
- `GetCompleteness_TvShowWithCompleteSeasons_ReturnsIsCompleteTrue`
- `GetCompleteness_TvShowWithSeason0_ExcludesSeason0`
- `GetCompleteness_TvShowWithSpecialsSeason_ExcludesSpecialsSeason` (case-insensitive)
- `GetCompleteness_WhenEpisodeCountIsNull_FallsBackToTvEpisodeRowCount`
- `GetCompleteness_ForFilmMediaType_ReturnsBadRequest`
- `GetCompleteness_WhenMediaNotFound_ReturnsNotFound`
- `GetCompleteness_WhenEpisodeCountIsZero_ReturnsIsCompleteTrue`

#### `GetUnlinkedFilesQueryHandlerTests`
- `GetUnlinkedFiles_ReturnsOnlyFilesWithNullMediaId`
- `GetUnlinkedFiles_RespectsPagination`
- `GetUnlinkedFiles_WhenNoUnlinkedFiles_ReturnsEmptyPagedResult`
- `GetUnlinkedFiles_IsOrderedByFilePath`

#### `GetMediaByIdQueryHandlerTests` (existing file — extend)
- `GetMediaById_WithLinkedFilesAndNoOverride_ReturnsComputedRootFolder`
- `GetMediaById_WithRootFolderOverride_ReturnsOverrideValue`
- `GetMediaById_NoFilesAndNoOverride_ReturnsNullRootFolder`

### Integration Test Class

#### `MediaFileLinkingIntegrationTests`
- Full workflow: scan file → link → verify in media detail → verify absent from unlinked-files → unlink → verify back in unlinked-files
- Verify 422 for double-link attempt
- Verify completeness endpoint accuracy with real SQL Server data

---

## Constitution Check — Post-Design

Re-evaluated after Phase 1 design. All checks remain **PASS**:
- No new project references violate the dependency rule.
- `ComputeCommonParent` is a private static method in `GetMediaByIdQueryHandler` (Application layer — pure string logic, no infra dependencies). ✅
- The single EF Core query for completeness (with `.Include().ThenInclude()`) avoids N+1 and uses `AsNoTracking()`. ✅
- `AdminMediaFilesController` is controller-level `AdminOnly` — no accidental public endpoint exposure. ✅
- `MediaDto` record extension is non-breaking (new parameter at end). Existing code constructing `MediaDto` must be updated (`GetMediaByIdQueryHandler`), but no API consumers break since JSON deserialization ignores unknown/new fields. ✅

---

## Implementation Sequence (for /speckit.tasks)

Recommended delivery order to maximize independent testability at each step:

1. **Domain + EF Core** — Add `Media.RootFolder`, update `MediaConfiguration`, run migration.
2. **GetMediaByIdQueryHandler + MediaDto** — Add `RootFolder` computation + field (US2 read path). Tests: extend existing `GetMediaByIdQueryHandlerTests`.
3. **LinkMediaFileCommand + UnlinkMediaFileCommand** — Foundational write operations (US1). Tests: `FileLinkCommandHandlerTests`.
4. **UpdateMediaRootFolderCommand** — Set/clear override (US2 write path). Tests: `UpdateMediaRootFolderCommandHandlerTests`.
5. **GetMediaCompletenessQuery** — Per-season completeness computation (US3). Tests: `GetMediaCompletenessQueryHandlerTests`.
6. **GetUnlinkedFilesQuery** — Paged unlinked file list (US4). Tests: `GetUnlinkedFilesQueryHandlerTests`.
7. **AdminMediaFilesController** — Wire all admin commands/queries into controller.
8. **MediaController** — Add completeness endpoint action.
9. **Integration tests** — `MediaFileLinkingIntegrationTests` end-to-end workflow.

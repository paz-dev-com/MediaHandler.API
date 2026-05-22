# Tasks: 007 — Media File Linking & Missing Content Detection

**Feature**: `007-media-file-linking`
**Branch**: `develop`
**Generated**: 2025-07-25
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Contracts**: [contracts/api-contracts.md](contracts/api-contracts.md)

---

## Overview

| Metric | Value |
|--------|-------|
| Total tasks | 33 |
| Setup / Foundation tasks | T001–T010 (10) |
| US1 — File Linking | T011–T014, T017–T018, T020 (7) |
| US2 — Root Folder | T022–T025 (4) |
| US3 — Season Completeness | T026–T029 (4) |
| US4 — Browse Unlinked Files | T015–T016, T019, T021 (4) |
| Polish / Integration | T030–T033 (4) |
| Suggested MVP scope | US1 only (T001–T004, T008–T009, T011–T014, T017–T018, T020) |

---

## Dependency Graph

```
Phase 1 (Domain + Migration)
  └─→ Phase 2 (DTOs)
        └─→ Phase 3 (Foundation: handler update, controller skeleton, contract)
              ├─→ Phase 4 (US1: Link/Unlink + US4: Unlinked Files)  ─┐
              ├─→ Phase 5 (US2: Root Folder)                          ├─→ Phase 7 (Polish)
              └─→ Phase 6 (US3: Season Completeness)                 ─┘
```

**Parallel opportunities after Phase 3 completes**:
- Phase 4 (US1 + US4), Phase 5 (US2), and Phase 6 (US3) are fully independent and can be implemented concurrently.
- Within Phase 4: T011–T016 can all start simultaneously (separate files). T020 and T021 can also run in parallel with each other once their respective handlers (T011/T013 and T015) are done.
- Within Phase 5: T022 and T023 start in parallel; T024 and T025 can run in parallel once T022/T023 are done.
- Within Phase 6: T026 and T027 start in parallel; T028 and T029 can run in parallel once T026/T027 are done.

---

## Phase 1 — Domain & Infrastructure Setup

**Goal**: Extend the `Media` entity with `RootFolder`, register the column in EF Core Fluent API, and generate the migration.
**Blocks**: Every subsequent phase.
**Dependencies**: None — start here.

- [X] T001 Add `public string? RootFolder { get; set; }` property to `Media` entity in the `// Dashboard API / Enrichment additions` region (after `NumberOfEpisodes`) with XML doc comment matching data-model.md §1.1 — `MediaHandler.Domain/Entities/Media.cs`
- [X] T002 Add `builder.Property(m => m.RootFolder);` inside `MediaConfiguration.Configure` after `NumberOfEpisodes` config, with `// File linking additions` comment — `MediaHandler.Infrastructure/Persistence/Configurations/MediaConfiguration.cs`
- [X] T003 Generate the EF Core migration by running from the repo root: `dotnet ef migrations add AddMediaRootFolder --project MediaHandler.Infrastructure --startup-project MediaHandler.API` — verify output file at `MediaHandler.Infrastructure/Migrations/<timestamp>_AddMediaRootFolder.cs` contains `AddColumn<string>(name: "RootFolder", table: "Medias", nullable: true)`

**✅ Phase 1 Checkpoint**: `dotnet build` exits with 0 errors. Migration file `<timestamp>_AddMediaRootFolder.cs` exists with `AddColumn` for `RootFolder` on `Medias`. `Media.cs` has the new property.

---

## Phase 2 — Application DTOs

**Goal**: Update `MediaDto` with `RootFolder` and add the two new DTO records required by US3 and US4.
**Blocks**: Phase 3 (T008 needs updated `MediaDto` to compile).
**Dependencies**: Phase 1 complete; `dotnet build` must be green before touching DTOs.

- [X] T004 Append `string? RootFolder` as the last positional parameter to the `MediaDto` record (after `int? NumberOfSeasons`) — existing callers that construct `MediaDto` will break at compile time; that breakage is expected and fixed in T008 — `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`
- [X] T005 Add `SeasonCompletenessDto` record after `MediaDto`: `public record SeasonCompletenessDto(int SeasonNumber, string SeasonName, int TotalExpected, int OwnedCount, IReadOnlyList<int> MissingEpisodeNumbers, bool IsComplete);` with XML doc as in data-model.md §2.2 — `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`
- [X] T006 Add `UnlinkedFileDto` record after `SeasonCompletenessDto`: `public record UnlinkedFileDto(Guid Id, string FilePath, long? FileSizeBytes, string? Format, string? Resolution);` with XML doc as in data-model.md §2.3 — `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`

**✅ Phase 2 Checkpoint**: `MediaDto.cs` has three records: updated `MediaDto`, new `SeasonCompletenessDto`, new `UnlinkedFileDto`. Build will show one compile error in `GetMediaByIdQueryHandler` (missing `rootFolder` arg) — expected, fixed in T008.

---

## Phase 3 — Foundation

**Goal**: Fix the broken `GetMediaByIdQueryHandler` (add `rootFolder` computation), create the `AdminMediaFilesController` skeleton, and add the `UpdateRootFolderRequest` API contract.
**Blocks**: All user-story controller actions (Phases 4–6).
**Dependencies**: Phase 2 complete (`MediaDto` has `RootFolder`).

- [X] T007 [P] Create new file `UpdateRootFolderRequest.cs` with content: `namespace MediaHandler.API.Contracts.Media; public record UpdateRootFolderRequest(string? RootFolder);` — `MediaHandler.API/Contracts/Media/UpdateRootFolderRequest.cs`
- [X] T008 [P] Update `GetMediaByIdQueryHandler` to (a) add private static `ComputeCommonParent(IEnumerable<string> filePaths)` helper (algorithm from data-model.md §4.1 / plan.md §1.4), (b) compute `var rootFolder = media.RootFolder ?? ComputeCommonParent(media.MediaFiles.Select(f => f.FilePath));`, (c) pass `rootFolder` as final arg in the `MediaDto(...)` constructor call — `MediaHandler.Application/Features/Media/Queries/GetMediaById/GetMediaByIdQueryHandler.cs`
- [X] T009 [P] Create `AdminMediaFilesController.cs` skeleton: `[ApiController]`, `[Route("api/v1/admin/media")]`, `[Authorize(Policy = "AdminOnly")]`, `[EnableRateLimiting("fixed")]`, primary constructor `(ISender sender) : ControllerBase` — **no action methods yet** — add file-scoped namespace `MediaHandler.API.Controllers`, required usings for MediatR, ASP.NET Core, and `ApiResponse` — `MediaHandler.API/Controllers/AdminMediaFilesController.cs`
- [X] T010 Add 3 new unit tests to the existing `GetMediaByIdQueryHandlerTests` class: `GetMediaById_WithLinkedFilesAndNoOverride_ReturnsComputedRootFolder` (seeds 2 files in same season folder, expects common parent path), `GetMediaById_WithRootFolderOverride_ReturnsOverrideValue` (sets `Media.RootFolder`, expects it returned verbatim regardless of file paths), `GetMediaById_NoFilesAndNoOverride_ReturnsNullRootFolder` (no files, no override, expects null) — `MediaHandler.Tests/Features/Media/GetMediaByIdQueryHandlerTests.cs`

**✅ Phase 3 Checkpoint**: `dotnet build` exits with 0 errors. `dotnet test MediaHandler.Tests --filter GetMediaByIdQueryHandlerTests` — all 3 new tests pass. `GET /api/v1/media/{id}` response includes `rootFolder` field. Swagger shows `AdminMediaFilesController` group (empty for now).

---

## Phase 4 — US1: File Linking & US4: Browse Unlinked Files

**Goal**: Implement link/unlink commands, unlinked-files query, all validators, controller actions, and unit tests.
**Independent Test (US1)**: `PUT /api/v1/admin/media/{mediaId}/files/{fileId}/link` → `MediaFile.MediaId = mediaId` (200); repeat → 200 idempotent; file linked elsewhere → 422 `FILE_ALREADY_LINKED`. `DELETE` → `MediaFile.MediaId = null` (200); file not owned → 404.
**Independent Test (US4)**: `GET /api/v1/admin/media/unlinked-files?page=1&pageSize=20` returns only files with `MediaId = null`; after linking a file it disappears from next call.
**Dependencies**: Phase 3 complete (controller skeleton at `AdminMediaFilesController.cs` exists).

### US1 — Command Handlers & Validators

- [X] T011 [P] [US1] Create `LinkMediaFileCommandHandler.cs` containing: `public record LinkMediaFileCommand(Guid MediaId, Guid FileId) : IRequest<Result<Unit>>;` and `LinkMediaFileCommandHandler` implementing `IRequestHandler<LinkMediaFileCommand, Result<Unit>>` — handler logic: (1) load `MediaFile` by `FileId` with EF tracking → `NOT_FOUND` if null; (2) verify `Media` exists by `MediaId` → `NOT_FOUND` if null; (3) if `mediaFile.MediaId == command.MediaId` return `Result.Success()` (idempotent); (4) if `mediaFile.MediaId != null` return `Result.Fail("FILE_ALREADY_LINKED: ...")` (422); (5) set `mediaFile.MediaId = command.MediaId`, `SaveChangesAsync`, return `Result.Success()` — `MediaHandler.Application/Features/Media/Commands/LinkMediaFile/LinkMediaFileCommandHandler.cs`
- [X] T012 [P] [US1] Create `LinkMediaFileCommandValidator.cs`: `AbstractValidator<LinkMediaFileCommand>` — `RuleFor(x => x.MediaId).NotEmpty()` and `RuleFor(x => x.FileId).NotEmpty()` — `MediaHandler.Application/Features/Media/Commands/LinkMediaFile/LinkMediaFileCommandValidator.cs`
- [X] T013 [P] [US1] Create `UnlinkMediaFileCommandHandler.cs` containing: `public record UnlinkMediaFileCommand(Guid MediaId, Guid FileId) : IRequest<Result<Unit>>;` and handler — handler logic: (1) load `MediaFile` by `FileId` with EF tracking → `NOT_FOUND` if null; (2) if `mediaFile.MediaId != command.MediaId` return `Result.Fail("NOT_FOUND: file not linked to this media item")` (404 — covers both wrong media and unlinked cases); (3) set `mediaFile.MediaId = null`, `SaveChangesAsync`, return `Result.Success()` — `MediaHandler.Application/Features/Media/Commands/UnlinkMediaFile/UnlinkMediaFileCommandHandler.cs`
- [X] T014 [P] [US1] Create `UnlinkMediaFileCommandValidator.cs`: `AbstractValidator<UnlinkMediaFileCommand>` — `RuleFor(x => x.MediaId).NotEmpty()` and `RuleFor(x => x.FileId).NotEmpty()` — `MediaHandler.Application/Features/Media/Commands/UnlinkMediaFile/UnlinkMediaFileCommandValidator.cs`

### US4 — Query Handler & Validator

- [X] T015 [P] [US4] Create `GetUnlinkedFilesQueryHandler.cs` containing: `public record GetUnlinkedFilesQuery(int Page, int PageSize) : IRequest<Result<PagedResult<UnlinkedFileDto>>>;` and handler — handler logic: (1) `var query = context.MediaFiles.AsNoTracking().Where(f => f.MediaId == null).OrderBy(f => f.FilePath)`; (2) `var count = await query.CountAsync(ct)`; (3) `var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).Select(f => new UnlinkedFileDto(f.Id, f.FilePath, f.FileSizeBytes, f.Format, f.Resolution)).ToListAsync(ct)`; (4) return `Result.Success(new PagedResult<UnlinkedFileDto>(items, count, request.Page, request.PageSize))` — `MediaHandler.Application/Features/Media/Queries/GetUnlinkedFiles/GetUnlinkedFilesQueryHandler.cs`
- [X] T016 [P] [US4] Create `GetUnlinkedFilesQueryValidator.cs`: `AbstractValidator<GetUnlinkedFilesQuery>` — `RuleFor(x => x.Page).GreaterThanOrEqualTo(1)` and `RuleFor(x => x.PageSize).InclusiveBetween(1, 100)` — `MediaHandler.Application/Features/Media/Queries/GetUnlinkedFiles/GetUnlinkedFilesQueryValidator.cs`

### Controller Actions

- [X] T017 [US1] Add `LinkFile` PUT action to `AdminMediaFilesController`: route `{mediaId:guid}/files/{fileId:guid}/link`, no request body, `[HttpPut]`, `[ProducesResponseType<ApiResponse<object>>(200)]`, `[ProducesResponseType<ApiResponse>(401)]`, `[ProducesResponseType<ApiResponse>(403)]`, `[ProducesResponseType<ApiResponse>(404)]`, `[ProducesResponseType<ApiResponse>(422)]`; send `new LinkMediaFileCommand(mediaId, fileId)`; discriminate `NOT_FOUND` → 404, `FILE_ALREADY_LINKED` → 422 via `UnprocessableEntity`; success → `Ok(ApiResponse<object>.Success(new {}))` — `MediaHandler.API/Controllers/AdminMediaFilesController.cs`
- [X] T018 [US1] Add `UnlinkFile` DELETE action to `AdminMediaFilesController`: route `{mediaId:guid}/files/{fileId:guid}/link`, no request body, `[HttpDelete]`, `[ProducesResponseType<ApiResponse<object>>(200)]`, `[ProducesResponseType<ApiResponse>(401)]`, `[ProducesResponseType<ApiResponse>(403)]`, `[ProducesResponseType<ApiResponse>(404)]`; send `new UnlinkMediaFileCommand(mediaId, fileId)`; discriminate `NOT_FOUND` → 404; success → `Ok(ApiResponse<object>.Success(new {}))` — `MediaHandler.API/Controllers/AdminMediaFilesController.cs`
- [X] T019 [US4] Add `GetUnlinkedFiles` GET action to `AdminMediaFilesController`: route `unlinked-files`, `[HttpGet]`, `[FromQuery] int page = 1, [FromQuery] int pageSize = 20`, `[ProducesResponseType<ApiResponse<IReadOnlyList<UnlinkedFileDto>>>(200)]`, `[ProducesResponseType<ApiResponse>(400)]`, `[ProducesResponseType<ApiResponse>(401)]`, `[ProducesResponseType<ApiResponse>(403)]`; send `new GetUnlinkedFilesQuery(page, pageSize)`; on validation failure → `BadRequest`; success → `Ok(ApiResponse<IReadOnlyList<UnlinkedFileDto>>.Success(result.Value.Items)).WithMeta(...)` using `ApiResponseMeta(page, pageSize, result.Value.TotalCount, result.Value.TotalPages)` following the existing `MediaController.List` pattern — `MediaHandler.API/Controllers/AdminMediaFilesController.cs`

### Unit Tests

- [X] T020 [P] [US1] Create `FileLinkCommandHandlerTests.cs` using `TestDbContext.Create()` and `NSubstitute` — implement all 8 tests listed in plan.md §2: `LinkFile_WhenFileIsUnlinked_SetsMediaIdAndReturnsSuccess`, `LinkFile_WhenFileAlreadyLinkedToSameMedia_ReturnsSuccessIdempotent`, `LinkFile_WhenFileAlreadyLinkedToDifferentMedia_ReturnsFileAlreadyLinkedError`, `LinkFile_WhenMediaIdDoesNotExist_ReturnsNotFound`, `LinkFile_WhenFileIdDoesNotExist_ReturnsNotFound`, `UnlinkFile_WhenFileIsLinkedToMedia_ClearsMediaIdAndReturnsSuccess`, `UnlinkFile_WhenFileIsNotLinkedToMedia_ReturnsNotFound`, `UnlinkFile_WhenFileIdDoesNotExist_ReturnsNotFound`; seed data with `_context.MediaFiles.Add(...)` + `SaveChangesAsync`; use `FluentAssertions` for assertions; use `TestContext.Current.CancellationToken` — `MediaHandler.Tests/Features/Media/FileLinkCommandHandlerTests.cs`
- [X] T021 [P] [US4] Create `GetUnlinkedFilesQueryHandlerTests.cs` using `TestDbContext.Create()` — implement all 4 tests: `GetUnlinkedFiles_ReturnsOnlyFilesWithNullMediaId` (seed mix of linked + unlinked files, assert only unlinked returned), `GetUnlinkedFiles_RespectsPagination` (seed 5 unlinked files, request page 1 pageSize 2, assert 2 items + correct TotalCount), `GetUnlinkedFiles_WhenNoUnlinkedFiles_ReturnsEmptyPagedResult` (no unlinked files → empty Items + TotalCount 0), `GetUnlinkedFiles_IsOrderedByFilePath` (seed unlinked files with unsorted paths, assert response is alphabetically ordered) — `MediaHandler.Tests/Features/Media/GetUnlinkedFilesQueryHandlerTests.cs`

**✅ Phase 4 Checkpoint**: `dotnet test MediaHandler.Tests --filter "FileLinkCommandHandlerTests|GetUnlinkedFilesQueryHandlerTests"` — 12 tests pass. `PUT /api/v1/admin/media/{mediaId}/files/{fileId}/link`, `DELETE /api/v1/admin/media/{mediaId}/files/{fileId}/link`, and `GET /api/v1/admin/media/unlinked-files` all visible in Swagger. Build is green.

---

## Phase 5 — US2: Root Folder Override

**Goal**: Implement `UpdateMediaRootFolderCommand` with validator, PATCH controller action, and unit tests.
**Independent Test**: `PATCH /api/v1/admin/media/{id}/root-folder` with `{ "rootFolder": "/mnt/nas/tv/Breaking Bad" }` → subsequent `GET /api/v1/media/{id}` returns `rootFolder = "/mnt/nas/tv/Breaking Bad"`. PATCH with `null` → reverts to computed common parent (or `null` if no files).
**Dependencies**: Phase 3 complete (controller skeleton and `UpdateRootFolderRequest` contract exist).

### Command Handler & Validator

- [X] T022 [P] [US2] Create `UpdateMediaRootFolderCommandHandler.cs` containing: `public record UpdateMediaRootFolderCommand(Guid MediaId, string? RootFolder) : IRequest<Result<Unit>>;` and handler — handler logic: (1) load `Media` by `MediaId` with EF **tracking** → `Result.Fail("NOT_FOUND: ...")` if null; (2) normalize: `media.RootFolder = string.IsNullOrWhiteSpace(request.RootFolder) ? null : request.RootFolder.Trim();` (empty string → null per FR-006); (3) `await context.SaveChangesAsync(ct)`; (4) return `Result.Success()` — `MediaHandler.Application/Features/Media/Commands/UpdateMediaRootFolder/UpdateMediaRootFolderCommandHandler.cs`
- [X] T023 [P] [US2] Create `UpdateMediaRootFolderCommandValidator.cs`: `AbstractValidator<UpdateMediaRootFolderCommand>` — `RuleFor(x => x.MediaId).NotEmpty()`; `RuleFor(x => x.RootFolder).MaximumLength(4096).When(x => x.RootFolder != null)` — `MediaHandler.Application/Features/Media/Commands/UpdateMediaRootFolder/UpdateMediaRootFolderCommandValidator.cs`

### Controller Action

- [X] T024 [US2] Add `UpdateRootFolder` PATCH action to `AdminMediaFilesController`: route `{mediaId:guid}/root-folder`, `[HttpPatch]`, `[FromBody] UpdateRootFolderRequest request`, `[ProducesResponseType<ApiResponse<object>>(200)]`, `[ProducesResponseType<ApiResponse>(400)]`, `[ProducesResponseType<ApiResponse>(401)]`, `[ProducesResponseType<ApiResponse>(403)]`, `[ProducesResponseType<ApiResponse>(404)]`; send `new UpdateMediaRootFolderCommand(mediaId, request.RootFolder)`; discriminate `NOT_FOUND` → 404, validation error → 400 `VALIDATION_ERROR`; success → `Ok(ApiResponse<object>.Success(new {}))` — add `using MediaHandler.API.Contracts.Media;` to usings — `MediaHandler.API/Controllers/AdminMediaFilesController.cs`

### Unit Tests

- [X] T025 [P] [US2] Create `UpdateMediaRootFolderCommandHandlerTests.cs` using `TestDbContext.Create()` — implement all 4 tests: `UpdateRootFolder_WithValidPath_SetsOverrideAndReturnsSuccess` (send `/mnt/nas/tv/Breaking Bad`, assert `media.RootFolder == "/mnt/nas/tv/Breaking Bad"` after reload), `UpdateRootFolder_WithNullValue_ClearsOverride` (set override first, send null, assert `media.RootFolder == null`), `UpdateRootFolder_WithEmptyString_TreatsAsNullAndClearsOverride` (send `""`, assert `media.RootFolder == null`), `UpdateRootFolder_WhenMediaNotFound_ReturnsNotFound` (non-existent GUID, assert `!result.IsSuccess` and error starts with `NOT_FOUND`) — `MediaHandler.Tests/Features/Media/UpdateMediaRootFolderCommandHandlerTests.cs`

**✅ Phase 5 Checkpoint**: `dotnet test MediaHandler.Tests --filter UpdateMediaRootFolderCommandHandlerTests` — 4 tests pass. `PATCH /api/v1/admin/media/{mediaId}/root-folder` visible in Swagger. Build is green.

---

## Phase 6 — US3: Season Completeness

**Goal**: Implement `GetMediaCompletenessQuery` with validator, add the `GetCompleteness` action to `MediaController`, and write unit tests covering all 8 acceptance scenarios.
**Independent Test**: TV show with season 1 `EpisodeCount = 5` and `EpisodeFileLink` records for episodes 1, 3, 5 → `GET /api/v1/media/{id}/completeness` returns `[{ seasonNumber: 1, totalExpected: 5, ownedCount: 3, missingEpisodeNumbers: [2, 4], isComplete: false }]`.
**Dependencies**: Phase 2 complete (`SeasonCompletenessDto` available in Application layer).

### Query Handler & Validator

- [X] T026 [P] [US3] Create `GetMediaCompletenessQueryHandler.cs` containing: `public record GetMediaCompletenessQuery(Guid MediaId) : IRequest<Result<IReadOnlyList<SeasonCompletenessDto>>>;` and handler — handler logic: (1) `AsNoTracking()` load `Media` where `Id == request.MediaId` → `NOT_FOUND` if null; (2) if `media.Type != MediaType.TvShow` → `Result.Fail("MEDIA_NOT_TV_SHOW: ...")` (400); (3) query seasons: `context.TvSeasons.AsNoTracking().Where(s => s.MediaId == request.MediaId && s.SeasonNumber != 0 && !s.Name.ToLower().Contains("specials")).Include(s => s.TvEpisodes).ThenInclude(e => e.EpisodeFileLinks).ThenInclude(l => l.MediaFile).OrderBy(s => s.SeasonNumber).ToListAsync(ct)`; (4) for each season: `int totalExpected = season.EpisodeCount ?? season.TvEpisodes.Count`, `var owned = season.TvEpisodes.Where(e => e.EpisodeFileLinks.Any(l => l.MediaFile.MediaId == request.MediaId)).Select(e => e.EpisodeNumber).ToHashSet()`, `var missing = Enumerable.Range(1, totalExpected).Except(owned).OrderBy(n => n).ToList()`, `isComplete = missing.Count == 0`; (5) return `Result.Success<IReadOnlyList<SeasonCompletenessDto>>(dtos.AsReadOnly())` — `MediaHandler.Application/Features/Media/Queries/GetMediaCompleteness/GetMediaCompletenessQueryHandler.cs`
- [X] T027 [P] [US3] Create `GetMediaCompletenessQueryValidator.cs`: `AbstractValidator<GetMediaCompletenessQuery>` — `RuleFor(x => x.MediaId).NotEmpty()` — `MediaHandler.Application/Features/Media/Queries/GetMediaCompleteness/GetMediaCompletenessQueryValidator.cs`

### Controller Action

- [X] T028 [US3] Add `GetCompleteness` GET action to `MediaController` (existing file — **no** new `[Authorize]` attribute needed as class-level `[Authorize]` already applies; this is not admin-only per FR-021): `[HttpGet("{id:guid}/completeness")]`, `[ProducesResponseType<ApiResponse<IReadOnlyList<SeasonCompletenessDto>>>(200)]`, `[ProducesResponseType<ApiResponse>(400)]`, `[ProducesResponseType<ApiResponse>(401)]`, `[ProducesResponseType<ApiResponse>(404)]`; method `GetCompleteness(Guid id, CancellationToken ct)`; send `new GetMediaCompletenessQuery(id)`; error discrimination: `NOT_FOUND` → 404, `MEDIA_NOT_TV_SHOW` → 400; success → `Ok(ApiResponse<IReadOnlyList<SeasonCompletenessDto>>.Success(result.Value))`; add `using MediaHandler.Application.Features.Media.DTOs;` and `using MediaHandler.Application.Features.Media.Queries.GetMediaCompleteness;` if not already present — `MediaHandler.API/Controllers/MediaController.cs`

### Unit Tests

- [X] T029 [P] [US3] Create `GetMediaCompletenessQueryHandlerTests.cs` using `TestDbContext.Create()` — seed `Media`, `TvSeason`, `TvEpisode`, `EpisodeFileLink`, `MediaFile` via context, using `SaveChangesAsync`; implement all 8 tests: `GetCompleteness_TvShowWithMissingEpisodes_ReturnsCorrectMissingList` (S1 EpisodeCount=5, files for eps 1/3/5, assert missing=[2,4] ownedCount=3 isComplete=false), `GetCompleteness_TvShowWithCompleteSeasons_ReturnsIsCompleteTrue` (all episodes owned, assert isComplete=true missingEpisodeNumbers=[]), `GetCompleteness_TvShowWithSeason0_ExcludesSeason0` (seed SeasonNumber=0, assert it is absent from response), `GetCompleteness_TvShowWithSpecialsSeason_ExcludesSpecialsSeason` (seed Name="SPECIALS", assert absent — tests case-insensitivity), `GetCompleteness_WhenEpisodeCountIsNull_FallsBackToTvEpisodeRowCount` (EpisodeCount=null, 4 TvEpisode rows, assert totalExpected=4), `GetCompleteness_ForFilmMediaType_ReturnsBadRequest` (MediaType.Film, assert `!result.IsSuccess` and error starts with `MEDIA_NOT_TV_SHOW`), `GetCompleteness_WhenMediaNotFound_ReturnsNotFound` (non-existent GUID, assert `NOT_FOUND` error), `GetCompleteness_WhenEpisodeCountIsZero_ReturnsIsCompleteTrue` (EpisodeCount=0, no files, assert isComplete=true missingEpisodeNumbers=[]) — `MediaHandler.Tests/Features/Media/GetMediaCompletenessQueryHandlerTests.cs`

**✅ Phase 6 Checkpoint**: `dotnet test MediaHandler.Tests --filter GetMediaCompletenessQueryHandlerTests` — 8 tests pass. `GET /api/v1/media/{id}/completeness` visible in Swagger under the `Media` group with correct response schemas. Build is green.

---

## Phase 7 — Polish & Integration Tests

**Goal**: Full integration coverage, migration applied to dev DB, Swagger verification, and CI gate.
**Dependencies**: All previous phases complete; Docker available for Testcontainers.

- [X] T030 [P] Create `MediaFileLinkingIntegrationTests.cs` with class-level Testcontainers.MsSql setup — implement the following integration test methods: `FullLinkWorkflow_LinkVerifyInDetailAndUnlinkedFiles_ThenUnlink` (scan seed → link → GET media/{id} contains file → GET unlinked-files omits file → unlink → GET unlinked-files contains file again), `LinkFile_WhenAlreadyLinkedToDifferentMedia_Returns422WithFileAlreadyLinked`, `GetCompleteness_TvShow_ReturnsAccurateSeasonData` (seed real SQL Server rows, call completeness endpoint, verify missing episodes), `GetUnlinkedFiles_Pagination_ReturnsCorrectPage` (seed 25 unlinked files, assert page 2 of pageSize 10 returns 10 items with correct offsets) — `MediaHandler.IntegrationTests/Features/Media/MediaFileLinkingIntegrationTests.cs`
- [X] T031 [P] Run full unit test suite with `dotnet test MediaHandler.Tests` and confirm ≥ 27 new tests pass (3 rootFolder + 8 link + 4 completeness + 4 unlinked-files + 4 rootFolder-command = expected total increases by 23; ensure zero regressions in pre-existing tests)
- [X] T032 Apply migration to local dev SQL Server database and verify column: `dotnet ef database update --project MediaHandler.Infrastructure --startup-project MediaHandler.API`; confirm with `SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Medias' AND COLUMN_NAME = 'RootFolder'` → should return `RootFolder | nvarchar | YES`
- [X] T033 Run `dotnet format --verify-no-changes` to confirm no formatting drift (CI gate) — if changes are detected, run `dotnet format` first and commit

**✅ Phase 7 Checkpoint**: `dotnet test MediaHandler.IntegrationTests` green. `dotnet test MediaHandler.Tests` green (all existing tests + new tests). Swagger at `/swagger` shows `AdminMediaFilesController` with 4 admin endpoints and `MediaController` with updated `GET /api/v1/media/{id}` (rootFolder visible in schema) and new `GET /api/v1/media/{id}/completeness`. `dotnet format --verify-no-changes` exits 0.

---

## Implementation Notes

### Key Patterns (from research.md)

| Decision | Pattern |
|----------|---------|
| Result factories | `Result.Success<T>(value)`, `Result.Fail<T>("ERROR_CODE: message")`, `Result.Success()`, `Result.Fail("ERROR_CODE: message")` |
| Error discrimination in controllers | `result.Errors.FirstOrDefault()?.StartsWith("ERROR_CODE", StringComparison.OrdinalIgnoreCase)` |
| EF tracking reads | Use `AsNoTracking()` for all queries; **omit** `AsNoTracking()` for commands that `SaveChangesAsync` |
| Case-insensitive EF filter | `.ToLower().Contains("specials")` — **not** `StringComparison.OrdinalIgnoreCase` (not EF-translatable) |
| Test context | `TestDbContext.Create()` for InMemory; `Substitute.For<ICurrentUserService>()` for mocks; `TestContext.Current.CancellationToken` |
| `ApiResponse` success | `ApiResponse<T>.Success(value)` |
| `ApiResponse` failure | `ApiResponse.Fail(new ApiError("CODE", "message"))` |
| Pagination meta | `ApiResponseMeta(page, pageSize, totalCount, totalPages)` pattern from `MediaController.List` |
| EpisodeFileLinks nav | `TvEpisode.EpisodeFileLinks` → `EpisodeFileLink` → `MediaFile.MediaId` (three-level include chain) |

### File Naming Conventions

All new files use **file-scoped namespaces**, **primary constructors**, and `#nullable enable` (implicit via project). `record` types for DTOs, commands, and queries. One command/query record + one handler class per file.

### Error Code → HTTP Status Mapping

| Error code prefix | HTTP status | Controller method |
|---|---|---|
| `NOT_FOUND` | 404 | `NotFound(...)` |
| `FILE_ALREADY_LINKED` | 422 | `UnprocessableEntity(...)` |
| `MEDIA_NOT_TV_SHOW` | 400 | `BadRequest(...)` |
| `VALIDATION_ERROR` | 400 | `BadRequest(...)` |

### Suggested MVP Scope

Deliver T001–T004, T008–T009, T011–T014, T017–T018, T020 first — this gives a working **link/unlink** workflow that provides immediate standalone value and unblocks the frontend file-association UI. US2, US3, and US4 can follow in any order.


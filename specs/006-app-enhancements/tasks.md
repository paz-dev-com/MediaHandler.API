# Tasks: App Enhancements — Backend API Changes

**Input**: Design documents from `/specs/006-app-enhancements/`
**Prerequisites**: spec.md (user stories), data-model.md, contracts/api-endpoints.md, plan.md

**Tests**: Explicitly requested in the feature specification. Test tasks are included in Phase 7.

**Organization**: Tasks are grouped by phase (domain → scan language → media handlers → profile picture commands → EF migration → API layer → tests) reflecting the dependency order defined in `plan.md`.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Domain**: `MediaHandler.Domain/`
- **Application**: `MediaHandler.Application/`
- **Infrastructure**: `MediaHandler.Infrastructure/`
- **API**: `MediaHandler.API/`
- **Unit Tests**: `MediaHandler.Tests/`
- **Integration Tests**: `MediaHandler.IntegrationTests/`

---

## Phase 1: Domain & DTOs (Shared Foundation)

**Purpose**: Entity and DTO changes that all user stories depend on. `User.ProfilePicturePath` feeds US3, US4, and US5. `MediaDto`/`MediaListItemDto` feed US2. Must be complete before any handler work begins.

- [X] T001 Add `ProfilePicturePath` (string?, nullable) property to the `User` entity in `MediaHandler.Domain/Entities/User.cs`
- [X] T002 [P] Add `string? ProfilePicturePath` as the last positional parameter to the `UserDto` record in `MediaHandler.Application/Features/Auth/DTOs/UserDto.cs`; AutoMapper convention will pick it up without an explicit `.ForMember`
- [X] T003 [P] Add `string? Status` and `int? NumberOfSeasons` as the last two positional parameters to the `MediaDto` record in `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`
- [X] T004 [P] Add `string? Status` and `int? NumberOfSeasons` as the last two positional parameters to the `MediaListItemDto` record in `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs` (same file as T003)

**Checkpoint**: All structural DTO/entity changes are in place. No handler or migration work can introduce compile errors against these new shapes.

---

## Phase 2: User Story 1 — Language-Aware Media Scan (Priority: P1)  MVP

**Goal**: Propagate an optional `language` string from the scan HTTP request body all the way to each `MatchQuery` constructed inside `ScanPipeline`, so every TMDB lookup during a scan uses the requested locale

**Independent Test**: Submit `POST /api/v1/admin/scan` with `"language": "fr"`; verify all TMDB search/detail HTTP calls during that run include `language=fr` as a query parameter

### Implementation for User Story 1

- [X] T005 [US1] Add `string? Language = null` as the last positional parameter to the `ScanStartParameters` record in `MediaHandler.Application/Common/Models/Scanner/ScanCoordinatorModels.cs`
- [X] T006 [P] [US1] Add `string? Language` as an optional field to the `StartScanRequest` record in `MediaHandler.API/Contracts/Admin/ScanRequests.cs`
- [X] T007 [US1] Add `string? Language` to the `StartScanCommand` record in `MediaHandler.Application/Features/Scan/Commands/StartScan/StartScanCommand.cs`; in the handler normalize empty-string to null (`string.IsNullOrWhiteSpace(Language) ? null : Language`) and pass it into `ScanStartParameters`
- [X] T008 [US1] Update `AdminScanController.StartScan` to pass `request.Language` to the `new StartScanCommand(…)` constructor in `MediaHandler.API/Controllers/AdminScanController.cs`
- [X] T009 [US1] Update `ScanRunCoordinator.ExecuteScanAsync` to pass `parameters.Language` when calling `ScanPipeline.ExecuteAsync`; update `ScanPipeline.ExecuteAsync` signature to accept `string? language = null` in `MediaHandler.Infrastructure/Services/ScanRunCoordinator.cs` and `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs`
- [X] T010 [US1] Inside `ScanPipeline.ExecuteAsync`, set `Language = language ?? "en-US"` on every `MatchQuery` constructed during the classify/match stage in `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs`

**Checkpoint**: A scan request with `language: "fr"` now threads the locale all the way to `TmdbMatcher` → `ITmdbService.SearchCandidatesAsync`. Null/empty language falls back to `"en-US"` as before.

---

## Phase 3: User Story 2 — TV Show Status & Season Count in Media Responses (Priority: P1)

**Goal**: Expose the already-persisted `Media.Status` and `Media.NumberOfSeasons` fields in both the detail and list media endpoints so the frontend can render production-status badges without an extra fetch

**Independent Test**: Call `GET /api/v1/media/{id}` for a TMDB-enriched TV show and verify the response includes `"status": "Returning Series"` and `"numberOfSeasons": 4` matching the DB values; call `GET /api/v1/media` and confirm every list item also includes both fields (null for films/unenriched entries)

### Implementation for User Story 2

- [X] T011 [P] [US2] Add `media.Status` and `media.NumberOfSeasons` to the positional `new MediaDto(…)` constructor call in `GetMediaByIdQueryHandler` in `MediaHandler.Application/Features/Media/Queries/GetMediaById/GetMediaByIdQueryHandler.cs`
- [X] T012 [P] [US2] Add `m.Status` and `m.NumberOfSeasons` to the `.Select(m => new MediaListItemDto(…))` EF Core projection in `GetMediaListQueryHandler` in `MediaHandler.Application/Features/Media/Queries/GetMediaList/GetMediaListQueryHandler.cs`

**Checkpoint**: Both media endpoints return `status` and `numberOfSeasons`; null for unenriched or Film entries. No migration or DB change needed — columns already exist.

---

## Phase 4: User Stories 3 & 4 — Profile Picture Commands (Priority: P2)

**Goal**: Implement the two new MediatR commands (`UploadProfilePictureCommand`, `DeleteProfilePictureCommand`) that manage the profile picture lifecycle, each returning `Result<UserDto>` via the existing pipeline

**Independent Test (US3)**: Dispatch `UploadProfilePictureCommand` with a valid JPEG stream; assert `UserDto.ProfilePicturePath` is set to `/api/v1/users/profile-picture/{userId}.jpg` and the file exists on disk  
**Independent Test (US4)**: Dispatch `DeleteProfilePictureCommand` for a user with an existing picture; assert `UserDto.ProfilePicturePath` is null and the file no longer exists; dispatching for a user with no picture returns `Result.Fail`

### Implementation for User Story 3 — Upload

- [X] T013 [US3] Create `UploadProfilePictureCommand` record (`OktaId`, `FileStream`, `FileName`, `ContentType`, `FileSize`) implementing `IRequest<Result<UserDto>>`; add `UploadProfilePictureCommandValidator` (FluentValidation: `ContentType` ∈ `{image/jpeg, image/png, image/webp}`, extension ∈ `{.jpg, .jpeg, .png, .webp}`, `FileSize` ≤ 2097152); implement handler: (1) resolve `User` by `OktaId` → fail if not found, (2) compute `newExt = Path.GetExtension(FileName).ToLower()`, (3) if existing `ProfilePicturePath` has a different extension delete old file, (4) write stream to `{WebRootPath}/uploads/profile-pictures/{userId}{newExt}`, (5) set `user.ProfilePicturePath = $"/api/v1/users/profile-picture/{userId}{newExt}"`, (6) `SaveChangesAsync`, (7) return `Result.Success(mapper.Map<UserDto>(user))` — all in `MediaHandler.Application/Features/Users/Commands/UploadProfilePicture/UploadProfilePictureCommand.cs`

### Implementation for User Story 4 — Delete

- [X] T014 [US4] Create `DeleteProfilePictureCommand` record (`OktaId`) implementing `IRequest<Result<UserDto>>`; implement handler: (1) resolve `User` by `OktaId` → fail if not found, (2) if `ProfilePicturePath is null` → `Result.Fail("USER_HAS_NO_PROFILE_PICTURE")`, (3) resolve filesystem path via `Path.Combine(WebRootPath, "uploads", "profile-pictures", Path.GetFileName(ProfilePicturePath))`, (4) if `File.Exists(fsPath)` call `File.Delete` (missing file is not an error), (5) set `user.ProfilePicturePath = null`, (6) `SaveChangesAsync`, (7) return `Result.Success(mapper.Map<UserDto>(user))` — all in `MediaHandler.Application/Features/Users/Commands/DeleteProfilePicture/DeleteProfilePictureCommand.cs`

**Checkpoint**: Both commands compile and follow the result pattern. Handlers inject `IApplicationDbContext`, `IMapper`, and `IWebHostEnvironment`; no raw exceptions for expected failure paths.

---

## Phase 5: EF Core Configuration & Migration

**Purpose**: Persist the new `ProfilePicturePath` column in the `Users` table — required before the upload/delete endpoints can be tested end-to-end

- [X] T015 Add `ProfilePicturePath` column mapping to `UserConfiguration`: `builder.Property(u => u.ProfilePicturePath).HasMaxLength(500)` (nullable by default — no `.IsRequired()`) in `MediaHandler.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- [X] T016 Generate EF Core migration via `dotnet ef migrations add AddProfilePicturePathToUser -p MediaHandler.Infrastructure -s MediaHandler.API`; verify the generated `Up` method contains `AddColumn<string>(name: "ProfilePicturePath", table: "Users", type: "nvarchar(500)", maxLength: 500, nullable: true)` and no `AlterColumn` on existing rows

**Checkpoint**: Migration applies cleanly; existing `Users` rows receive `NULL`. No data loss. `dotnet ef database update` succeeds on a fresh DB.

---

## Phase 6: User Stories 3, 4 & 5 — API Layer (Priority: P2)

**Goal**: Expose all three profile-picture HTTP actions via a new `UsersController`, and ensure the upload directory exists at startup

**Independent Test (US5)**: Call `GET /api/v1/auth/me` for a user with and without a profile picture; confirm `profilePicturePath` is present in both cases (non-null / null). Call `POST /api/v1/auth/sync`; confirm the same field appears.

### Implementation

- [X] T017 [US3] [US4] Create `UsersController` (`[Route("api/v1/users")]`, `[Authorize]`, `[EnableRateLimiting("fixed")]`) with three actions: (a) `POST profile-picture` — reads `IFormFile file` from `multipart/form-data`, dispatches `UploadProfilePictureCommand(OktaId, file.OpenReadStream(), file.FileName, file.ContentType, file.Length)`, returns `ApiResponse<UserDto>`; (b) `DELETE profile-picture` — dispatches `DeleteProfilePictureCommand(OktaId)`, returns `ApiResponse<UserDto>` or 404; (c) `[AllowAnonymous] GET profile-picture/{fileName}` — validates `fileName` contains no `..` or directory separators, resolves `Path.Combine(env.WebRootPath, "uploads", "profile-pictures", fileName)`, returns `PhysicalFileResult` with inferred `Content-Type` or 404 if file not found — in `MediaHandler.API/Controllers/UsersController.cs`
- [X] T018 [US3] Update `Program.cs` startup to create the upload directory: resolve `uploadsDir = Path.Combine(env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads", "profile-pictures")` and call `Directory.CreateDirectory(uploadsDir)` (idempotent) in `MediaHandler.API/Program.cs`

**Checkpoint (US5)**: `UserDto` already carries `ProfilePicturePath` via AutoMapper (Phase 1, T002). `GET /api/v1/auth/me` and `POST /api/v1/auth/sync` return the field automatically — no handler code changes needed.

---

## Phase 7: Tests

**Purpose**: Verify all handler success/failure paths, validator rules, and the end-to-end upload→auth/me→delete integration flow

### Unit Tests — Upload Handler

- [X] T019 [P] [US3] `UploadProfilePicture_WithValidJpeg_ReturnsUpdatedUserDtoWithProfilePicturePath` — mock `IApplicationDbContext` + `IMapper` + `IWebHostEnvironment`; assert `UserDto.ProfilePicturePath` matches expected route and `SaveChangesAsync` was called in `MediaHandler.Tests/Features/Users/UploadProfilePictureCommandHandlerTests.cs`
- [X] T020 [P] [US3] `UploadProfilePicture_UserNotFound_ReturnsFailureResult` — arrange DB returns null for `OktaId`; assert `Result.IsFailure` and no filesystem write in same file
- [X] T021 [P] [US3] `UploadProfilePicture_ExtensionChanges_DeletesOldFileBeforeSavingNew` — arrange user with existing `.jpg` path; upload `.png`; assert old file was deleted and new file was created with `.png` extension in same file

### Unit Tests — Delete Handler

- [X] T022 [P] [US4] `DeleteProfilePicture_WithExistingPicture_ClearsPathAndReturnsUpdatedDto` — arrange user with `ProfilePicturePath` set + file on disk; assert path cleared in DB and `File.Delete` called in `MediaHandler.Tests/Features/Users/DeleteProfilePictureCommandHandlerTests.cs`
- [X] T023 [P] [US4] `DeleteProfilePicture_NoPicture_ReturnsNotFoundFailure` — arrange user with `ProfilePicturePath = null`; assert `Result.IsFailure` with `USER_HAS_NO_PROFILE_PICTURE` error code in same file
- [X] T024 [P] [US4] `DeleteProfilePicture_FileAlreadyGone_StillClearsDatabasePath` — arrange user with path set but file not on disk; assert `SaveChangesAsync` called with null path, no exception thrown in same file

### Unit Tests — Validator

- [X] T025 [P] [US3] `Validate_ValidJpeg_PassesValidation`, `Validate_ValidPng_PassesValidation`, `Validate_ValidWebp_PassesValidation` — assert no validation errors for supported types ≤ 2 MB in `MediaHandler.Tests/Features/Users/UploadProfilePictureValidatorTests.cs`
- [X] T026 [P] [US3] `Validate_UnsupportedContentType_FailsValidation` (e.g. `image/gif`) and `Validate_UnsupportedExtension_FailsValidation` (e.g. `.bmp`) — assert failure with descriptive message in same file
- [X] T027 [P] [US3] `Validate_FileSizeExceeds2MB_FailsValidation` — assert failure when `FileSize = 2_097_153` in same file

### Unit Tests — Scan Language

- [X] T028 [P] [US1] `StartScan_WithLanguageFr_ForwardsLanguageToScanStartParameters` — assert `ScanStartParameters.Language == "fr"` when command has `Language = "fr"` in `MediaHandler.Tests/Features/Scan/StartScanCommandHandlerTests.cs`
- [X] T029 [P] [US1] `StartScan_WithEmptyStringLanguage_NormalizesToNull` — assert `ScanStartParameters.Language == null` when command has `Language = ""` in same file

### Unit Tests — Media Handlers

- [X] T030 [P] [US2] `GetMediaById_EnrichedTvShow_ReturnsStatusAndNumberOfSeasons` — mock DB with enriched media having `Status = "Returning Series"`, `NumberOfSeasons = 4`; assert both fields present in returned `MediaDto` in `MediaHandler.Tests/Features/Media/GetMediaByIdQueryHandlerTests.cs`
- [X] T031 [P] [US2] `GetMediaById_UnenrichedMedia_ReturnsBothFieldsAsNull` — assert `Status == null` and `NumberOfSeasons == null` for a media entry without enrichment in same file

### Integration Test — Profile Picture Flow

- [X] T032 [US3] [US4] `Upload_ThenGetMe_ThenDelete_FullProfilePictureLifecycle` — authenticate user, call `POST /api/v1/users/profile-picture` with a valid JPEG, assert 200 and `profilePicturePath` non-null; call `GET /api/v1/auth/me` and confirm same path; call `DELETE /api/v1/users/profile-picture`, assert 200 and `profilePicturePath: null`; call `GET /api/v1/users/profile-picture/{fileName}` and assert 404 in `MediaHandler.IntegrationTests/Users/ProfilePictureEndpointTests.cs`

**Checkpoint**: All green. Each handler's happy-path and edge-cases are covered. Integration test verifies the full flow against a real DB (Testcontainers.MsSql).

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, build, format, and contract checklist

- [X] T033 [P] Verify `UserMappingProfile` requires no explicit `.ForMember` for `ProfilePicturePath` by confirming AutoMapper convention resolution in `MediaHandler.Application/Common/Mappings/UserMappingProfile.cs`
- [X] T034 [P] Verify all new and modified endpoints return `ApiResponse<T>` envelope; confirm `UsersController` actions use the standard `ApiResponse.Success(data)` pattern and Result-to-HTTP mapping consistent with other controllers in `MediaHandler.API/Controllers/UsersController.cs`
- [X] T035 Run `dotnet build MediaHandler.slnx` and fix any compilation errors across all projects
- [X] T036 Run `dotnet format --verify-no-changes` and fix any formatting violations
- [x] T037 Apply migration (`dotnet ef database update -p MediaHandler.Infrastructure -s MediaHandler.API`) and run the quickstart verification checklist: (1) POST scan with `language: "fr"` → 202; (2) GET media detail → `status` + `numberOfSeasons` present; (3) POST profile-picture → 200 with path; (4) GET `/auth/me` → path in response; (5) DELETE profile-picture → 200 with null path

---

## Phase 9: Enrichment Language Support

**Purpose**: Propagate an optional `language` parameter through the enrichment stack — from the HTTP request body all the way to every TMDB call made inside `EnrichmentCoordinator` — mirroring the scan language feature delivered in Phase 2. Both hardcoded `"en-US"` strings inside `EnrichMediaFieldsAsync` and `UpsertTvSeasonsAsync` are replaced with the caller-supplied locale.

**Goal**: Admins can trigger `POST /api/v1/admin/enrichment/start` with `{"language":"fr"}` and have every subsequent TMDB detail/season request use `language=fr-FR` as a query parameter. Null or absent language falls back to `"en-US"` as before — existing callers that send no body are unaffected.

**Independent Test**: `POST /api/v1/admin/enrichment/start` with `{"language":"fr"}` → assert 202 Accepted; verify via trace/log or integration test that TMDB HTTP requests made during the run include `language=fr-FR`. Repeat with no body → verify TMDB calls still use `language=en-US`.

### Implementation

- [x] T038 [P] Add `StartEnrichmentRequest` record (`string? Language = null`) to `MediaHandler.API/Contracts/Admin/ScanRequests.cs`; the new record lives in the same file as `StartScanRequest` and mirrors its shape exactly so the pattern is consistent across both admin trigger endpoints
- [x] T039 [P] Update `IEnrichmentCoordinator.StartAsync` signature in `MediaHandler.Application/Common/Interfaces/IEnrichmentCoordinator.cs` to `Task StartAsync(Guid enrichmentRunId, string? language = null, CancellationToken ct = default)`; the default value keeps all existing callers compile-clean without changes
- [x] T040 Extend `StartEnrichmentCommand` from a parameterless record to `record StartEnrichmentCommand(string? Language = null) : IRequest<Result<StartEnrichmentResult>>` in `MediaHandler.Application/Features/Dashboard/Commands/StartEnrichment/StartEnrichmentCommand.cs`; in `StartEnrichmentCommandHandler.Handle`, normalize empty-string to null (`var language = string.IsNullOrWhiteSpace(request.Language) ? null : request.Language;`) and replace the existing `coordinator.StartAsync(run.Id, cancellationToken)` call with `coordinator.StartAsync(run.Id, language, cancellationToken)` — depends on T039
- [x] T041 Update `AdminEnrichmentController.StartEnrichment` in `MediaHandler.API/Controllers/AdminEnrichmentController.cs` to accept `[FromBody] StartEnrichmentRequest? request = null` as the first parameter (before `CancellationToken ct`) and dispatch `new StartEnrichmentCommand(Language: request?.Language)` instead of `new StartEnrichmentCommand()` — depends on T038 and T040; verify that calling the endpoint with no body (existing behavior) still returns 202/200 without any deserialization error
- [x] T042 Update `EnrichmentCoordinator.StartAsync` in `MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs` to accept `string? language = null` and forward it into `ExecuteEnrichmentAsync`; update the private `ExecuteEnrichmentAsync(Guid runId)` signature to `ExecuteEnrichmentAsync(Guid runId, string? language = null)`; add a private static helper `ResolveLocale` that maps short codes to IETF tags: `"fr"` → `"fr-FR"`, `"en"` → `"en-US"`, any value already containing `'-'` is passed through as-is, null/unknown/empty falls back to `"en-US"`; compute `var resolvedLocale = ResolveLocale(language)` once inside `ExecuteEnrichmentAsync` before the processing loop — depends on T039
- [x] T043 Thread `resolvedLocale` through the two private static helper methods in `MediaHandler.Infrastructure/Services/EnrichmentCoordinator.cs`: (1) update `EnrichMediaFieldsAsync` signature to accept `string language` and replace the hardcoded `"en-US"` in `tmdbService.GetMediaDetailsAsync(media.TmdbId, mediaTypeStr, "en-US", CancellationToken.None)` with `language`; (2) update `UpsertTvSeasonsAsync` signature to accept `string language` and replace the hardcoded `"en-US"` in `tmdbService.GetTvShowSeasonsAsync(media.TmdbId, "en-US", CancellationToken.None)` with `language`; update the two call sites inside `ExecuteEnrichmentAsync` to pass `resolvedLocale` — depends on T042
- [x] T044 Run `dotnet build MediaHandler.slnx` and fix any compilation errors; smoke-test the full flow: `POST /api/v1/admin/enrichment/start` with body `{"language":"fr"}` → assert 202 Accepted and confirm TMDB calls carry `language=fr-FR`; `POST` with an empty JSON body `{}` → assert 202 Accepted and TMDB calls default to `language=en-US`; `POST` with no body (raw, no Content-Type) → assert no 400/415 error (nullable request body)

**Checkpoint**: Enrichment language pass-through is complete. TMDB metadata (titles, overviews, genre names) is fetched in the admin's active locale during enrichment, mirroring the scan language feature from Phase 2. Existing clients that omit the body are unaffected.

---

## Phase 10: Sort & Filter on Admin List Endpoints (Unblocks Web US-9)

**Purpose**: Extend all six admin list query handlers with optional `sortField`/`sortOrder` parameters and column-specific text filters so the frontend PrimeNG `p-table` components can delegate sort and filter to the server. Each query already has `page`/`pageSize`; this phase adds the complementary sort/filter surface.

**Goal**: `GET /api/v1/admin/users?sortField=displayName&sortOrder=desc` returns users sorted by `DisplayName` descending. `GET /api/v1/admin/review-items?fileName=Inception` returns only items whose `FilePath` contains `"Inception"`. All six endpoints behave identically to today when the new params are omitted.

**Independent Test**: Call each endpoint with `?sortField=<column>&sortOrder=desc` and verify ordering; call endpoints with a text filter value and verify filtering. Omit params and verify existing behaviour is unchanged.

### Implementation

- [X] T045 [P] Add `string? SortField = null` and `string? SortOrder = "asc"` as the last two positional parameters to `GetUsersQuery` record in `MediaHandler.Application/Features/Admin/Queries/GetUsers/GetUsersQueryHandler.cs`; replace the hardcoded `query.OrderBy(u => u.Email)` with a `switch (SortField?.ToLowerInvariant(), SortOrder?.ToLowerInvariant() == "desc")` that maps `"displayname"`, `"email"`, `"role"`, `"isactive"` to ascending/descending `OrderBy`/`OrderByDescending` calls, with `OrderBy(u => u.Email)` as the default case; update `AdminController.GetUsers()` in `MediaHandler.API/Controllers/AdminController.cs` to accept `[FromQuery] string? sortField = null` and `[FromQuery] string? sortOrder = "asc"` and pass them to the query constructor — verify `dotnet build`
- [X] T046 [P] Add `string? SortField = null`, `string? SortOrder = "asc"`, and `string? FileName = null` as the last three positional parameters to `ListReviewItemsQuery` record in `MediaHandler.Application/Features/Review/Queries/ListReviewItems/ListReviewItemsQuery.cs`; apply `query = query.Where(r => r.FilePath.Contains(request.FileName))` when `FileName` is non-null/non-whitespace; replace the hardcoded `OrderByDescending(r => r.CreatedAt)` with a `switch` covering `"filename"` (maps to `r.FilePath`), `"status"`, `"createdat"`, with `OrderByDescending(r => r.CreatedAt)` as default; update `AdminReviewController.ListReviewItems()` in `MediaHandler.API/Controllers/AdminReviewController.cs` to accept and forward `[FromQuery] string? sortField`, `string? sortOrder`, `string? fileName` — verify `dotnet build`
- [X] T047 [P] Add `string? SortField = null` and `string? SortOrder = "asc"` as the last two positional parameters to `ListScanHistoryQuery` record in `MediaHandler.Application/Features/Scan/Queries/ListScanHistory/ListScanHistoryQuery.cs`; replace the hardcoded `OrderByDescending(r => r.StartedAt)` with a `switch` covering `"startedat"`, `"status"`, `"mode"`, with `OrderByDescending(r => r.StartedAt)` as default; update `AdminScanController.ListHistory()` in `MediaHandler.API/Controllers/AdminScanController.cs` to accept and forward the new `[FromQuery]` params — verify `dotnet build`
- [X] T048 [P] Add `string? SortField = null`, `string? SortOrder = "asc"`, and `string? FileName = null` as the last three positional parameters to `ListScanDecisionsQuery` record in `MediaHandler.Application/Features/Dashboard/Queries/ListScanDecisions/ListScanDecisionsQuery.cs`; apply `Contains(request.FileName)` filter on `d.FilePath` when `FileName` is non-null; replace the hardcoded `OrderBy(d => d.FilePath)` with a `switch` covering `"filename"` (maps to `d.FilePath`), `"status"` (maps to `d.Kind`), `"createdat"` (maps to `d.CreatedAt`), with `OrderBy(d => d.FilePath)` as default; update the scan decisions controller action in `MediaHandler.API/Controllers/AdminScanDecisionsController.cs` to accept and forward the new params — verify `dotnet build`
- [X] T049 [P] Add `string? SortField = null` and `string? SortOrder = "asc"` as the last two positional parameters to `ListEnrichmentHistoryQuery` record in `MediaHandler.Application/Features/Dashboard/Queries/ListEnrichmentHistory/ListEnrichmentHistoryQuery.cs`; replace the hardcoded `OrderByDescending(r => r.StartedAt)` with a `switch` covering `"startedat"`, `"status"`, with `OrderByDescending(r => r.StartedAt)` as default; note that this query returns `PagedResult<EnrichmentRunDto>` directly (not `Result<…>`), so no change to the return-type wrapper is needed; update `AdminEnrichmentController.ListHistory()` in `MediaHandler.API/Controllers/AdminEnrichmentController.cs` to accept and forward the new params — verify `dotnet build`
- [X] T050 [P] Add `string? SortField = null`, `string? SortOrder = "asc"`, and `string? Path = null` as the last three positional parameters to `ListLibraryRootsQuery` record in `MediaHandler.Application/Features/LibraryRoots/Queries/ListLibraryRoots/ListLibraryRootsQuery.cs`; apply `query = query.Where(r => r.Path.Contains(request.Path))` when `Path` is non-null; replace the hardcoded `OrderBy(r => r.Path)` with a `switch` covering `"path"`, `"createdat"`, with `OrderBy(r => r.Path)` as default; update `AdminLibraryRootsController.List()` in `MediaHandler.API/Controllers/AdminLibraryRootsController.cs` to accept and forward the new params — verify `dotnet build`

**Checkpoint**: All six admin list endpoints accept `sortField`/`sortOrder` and their respective text filters. Omitting the new params returns data in the same default order as before. `dotnet build` succeeds with zero warnings.

---

## Phase 11: Incremental Scan Counter Flush (Unblocks Web US-11)

**Purpose**: The `GET /api/v1/admin/scan/active` endpoint reads scan counters (`TotalDiscovered`, `Added`, `Updated`, `NeedsReview`) from the `ScanRun` DB row, which currently stays at zero until the entire scan finishes. Flushing the in-memory `ScanCounters` struct to the DB every 10 video files makes the counters visible mid-scan within the first two 4-second polling cycles.

**Goal**: Start a scan on a library with >10 files; within ~8 seconds (two polling intervals), `GET /api/v1/admin/scan/active` returns at least one counter > 0.

**Independent Test**: Run a scan on a library with 20+ files; confirm `GET /api/v1/admin/scan/active` returns non-zero `counts` before the scan completes.

### Implementation

- [X] T051 Inside the `foreach (var file in videoFiles)` loop in `ProcessRootAsync` in `MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs` (immediately after the `ClassifyAndPersistFileAsync` call, at the same position as the existing `processedInRoot % 50 == 0` progress-emit block), add an incremental counter flush every 10 files: `if (processedInRoot % 10 == 0) { scanRun.TotalDiscovered = counters.TotalDiscovered; scanRun.Added = counters.Added; scanRun.Updated = counters.Updated; scanRun.Unchanged = counters.Unchanged; scanRun.Removed = counters.Removed; scanRun.Excluded = counters.Excluded; scanRun.NeedsReview = counters.NeedsReview; await db.SaveChangesAsync(ct); }` — the existing final flush at lines 82–89 of `ExecuteAsync` is retained unchanged as the authoritative end-of-scan snapshot — verify `dotnet build` and run a test scan confirming non-zero counters mid-run

**Checkpoint**: Scanner counters are visible in `GET /api/v1/admin/scan/active` after the first 10 video files have been processed. The final counter flush at scan completion is unaffected.

---

## Phase 12: Batch Assign Review Items (Unblocks Web US-12)

**Purpose**: Introduce a `BatchAssignReviewItemsCommand` that resolves multiple `ReviewItem` rows to the same target `Media` in a single request, and expose it via `POST /api/v1/admin/review-items/batch-assign`. Unlike the existing `ResolveReviewItemCommand` (which accepts a TmdbId + Kind and performs a DB lookup), this command uses the internal `Media.Id` (Guid) directly — the frontend has already resolved the target via `GET /api/v1/media?title=…` search.

**Goal**: `POST /api/v1/admin/review-items/batch-assign` with `{ "reviewItemIds": ["…","…"], "targetMediaId": "…" }` resolves all specified review items to the target media in one call and returns per-item success/failure results.

**Independent Test**: POST with 3 valid ReviewItemIds + a valid targetMediaId — verify all 3 items are resolved (Status = Resolved) and the response contains 3 `success: true` entries. POST with one invalid ReviewItemId mixed in — verify the valid ones succeed and the invalid one produces `success: false` with a descriptive error message. POST with an unknown `targetMediaId` — verify 404.

### Implementation

- [X] T052 Create `BatchAssignReviewItemsCommand` record (`Guid[] ReviewItemIds`, `Guid TargetMediaId`) implementing `IRequest<Result<BatchAssignReviewItemsResponse>>`; add `BatchAssignReviewItemsCommandValidator` (FluentValidation: `ReviewItemIds` must not be empty, each element must not be `Guid.Empty`, `TargetMediaId` must not be `Guid.Empty`); implement handler: (1) resolve `Media` by `TargetMediaId` via `db.Medias.FindAsync` — return `Result.Fail("MEDIA_NOT_FOUND")` if null, (2) for each `ReviewItemId` in a loop: find `ReviewItem` by id — if not found record `BatchAssignItemResult(id, false, "REVIEW_ITEM_NOT_FOUND")`; otherwise set `reviewItem.ResolvedTmdbId = media.TmdbId`, `reviewItem.ResolvedKind = media.Type`, `reviewItem.Status = ReviewStatus.Resolved`, `reviewItem.ResolvedAt = DateTime.UtcNow`, record `BatchAssignItemResult(id, true, null)`; (3) `await db.SaveChangesAsync(cancellationToken)` once after the loop; (4) return `Result.Success(new BatchAssignReviewItemsResponse(results))` — all in `MediaHandler.Application/Features/Review/Commands/BatchAssignReviewItems/BatchAssignReviewItemsCommand.cs`; also add `BatchAssignReviewItemsRequest(Guid[] ReviewItemIds, Guid TargetMediaId)`, `BatchAssignItemResult(Guid ReviewItemId, bool Success, string? ErrorMessage)`, and `BatchAssignReviewItemsResponse(IReadOnlyList<BatchAssignItemResult> Results)` records to `MediaHandler.API/Contracts/Admin/ReviewRequests.cs` — verify `dotnet build`
- [X] T053 Add `POST /api/v1/admin/review-items/batch-assign` action to `AdminReviewController` in `MediaHandler.API/Controllers/AdminReviewController.cs`: accept `[FromBody] BatchAssignReviewItemsRequest request`; validate that `request.ReviewItemIds` is non-empty (return `400 Bad Request ApiResponse` if empty); dispatch `new BatchAssignReviewItemsCommand(request.ReviewItemIds, request.TargetMediaId)` via `_mediator`; map `Result.IsFailure` to `404 Not Found` when the failure code is `"MEDIA_NOT_FOUND"`; return `200 OK ApiResponse<BatchAssignReviewItemsResponse>` on success; add `[ProducesResponseType(typeof(ApiResponse<BatchAssignReviewItemsResponse>), 200)]`, `[ProducesResponseType(400)]`, `[ProducesResponseType(403)]`, `[ProducesResponseType(404)]` attributes — depends on T052; verify `dotnet build` and a test POST confirming per-item results in the response

**Checkpoint**: `POST /api/v1/admin/review-items/batch-assign` is live. Multiple review items can be resolved to a single target media in one call. Per-item failures are captured and returned without failing the entire batch.

---

## Phase 13: Collection Completeness Data (Unblocks Web US-14)

**Purpose**: Surface TV show completeness information in two existing endpoints without any schema migration. `GET /api/v1/media/stats` gains an `incompleteTvShowCount` stat; `GET /api/v1/media` list items gain an `ownedSeasonCount` field for TV shows (null for films). Both changes are purely additive DTO extensions with computed EF Core projections.

**Goal**: `GET /api/v1/media/stats` returns `"incompleteTvShowCount": 3` when 3 TV shows have fewer `TvSeason` records than `NumberOfSeasons`. `GET /api/v1/media` returns `"ownedSeasonCount": 2` for a TV show that has 2 persisted `TvSeason` rows, and `"ownedSeasonCount": null` for films.

**Independent Test**: For a TV show with `numberOfSeasons = 4` and 2 persisted `TvSeason` rows: confirm `GET /api/v1/media` returns `ownedSeasonCount: 2`; confirm `GET /api/v1/media/stats` includes that show in `incompleteTvShowCount`. For a film: confirm `ownedSeasonCount: null`. For the stats endpoint: confirm count decreases by 1 after adding a missing season record.

### Implementation

- [X] T054 Add `int IncompleteTvShowCount` as the last positional parameter to `MediaStatsDto` record in `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`; update `GetMediaStatsQueryHandler.Handle` in `MediaHandler.Application/Features/Media/Queries/GetMediaStats/GetMediaStatsQueryHandler.cs` to compute `var incompleteTvShows = await context.Medias.CountAsync(m => m.Type == MediaType.TvShow && m.NumberOfSeasons.HasValue && m.TvSeasons.Count() < m.NumberOfSeasons.Value, cancellationToken)` and pass it as the last argument in `new MediaStatsDto(totalMedia, films, tvShows, watchedByUser, totalMedia - watchedByUser, totalFiles, unlinkedFiles, incompleteTvShows)` — verify `dotnet build` and confirm `GET /api/v1/media/stats` response includes `incompleteTvShowCount`
- [X] T055 Add `int? OwnedSeasonCount` as the last positional parameter to `MediaListItemDto` record in `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`; update the `.Select(m => new MediaListItemDto(…))` projection in `GetMediaListQueryHandler.Handle` in `MediaHandler.Application/Features/Media/Queries/GetMediaList/GetMediaListQueryHandler.cs` to append `m.Type == MediaType.TvShow ? (int?)m.TvSeasons.Count() : null` as the last argument — EF Core translates this to a correlated subquery; no `Include` or additional DB calls needed — verify `dotnet build` and confirm `GET /api/v1/media` list items include `ownedSeasonCount` for TV shows and `null` for films

**Checkpoint**: `GET /api/v1/media/stats` returns `incompleteTvShowCount` and `GET /api/v1/media` returns `ownedSeasonCount`. Films receive `null`. No migration required — `TvSeasons` and `NumberOfSeasons` already exist on the `Media` entity.

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Domain + DTOs)
  ├── Phase 2 (US1 — Scan Language): depends on T005–T006 (ScanStartParameters, StartScanRequest shapes)
  ├── Phase 3 (US2 — Media Handlers): depends on T003–T004 (MediaDto, MediaListItemDto shapes)
  ├── Phase 4 (US3+US4 — Commands): depends on T001–T002 (User entity, UserDto shapes)
  │     └── Phase 5 (Migration): depends on T001 (User entity)
  │           └── Phase 6 (API Layer): depends on T013, T014, T015, T016
  └── Phase 7 (Tests): depends on Phases 2–6
Phase 8 (Polish): depends on all prior phases
Phase 9 (Enrichment Language): T038+T039 parallel → T040 → T041; T039 → T042 → T043; T044 validates all
Phase 10 (Sort/Filter): T045–T050 are all fully independent and can run in parallel (different files)
Phase 11 (Counter Flush): T051 is independent; can run in parallel with Phase 10
Phase 12 (Batch Assign): T052 → T053 (endpoint depends on command); T052 independent of Phases 10–11
Phase 13 (Completeness Data): T054 and T055 are independent (same file, different records); both independent of Phases 10–12
```

### Parallel Opportunities

**Within Phase 1** — T002, T003, T004 can all run in parallel after T001 (they are different records in different files).

**Within Phase 2** — T005 and T006 can run in parallel; T007 depends on both; T009 and T010 can be done together once T007 is merged.

**Within Phase 3** — T011 and T012 are fully independent (different handler files).

**Within Phase 4** — T013 and T014 are independent (different command files); both need T001+T002 from Phase 1.

**Within Phase 7** — T019–T031 are all independent unit tests and can be written in parallel. T032 (integration) requires Phase 6 to be complete.

**Within Phase 9** — T038 (`ScanRequests.cs`) and T039 (`IEnrichmentCoordinator.cs`) are fully independent and can run in parallel. T040 (`StartEnrichmentCommand.cs`) depends on T039; T041 (`AdminEnrichmentController.cs`) depends on T038+T040; T042 (`EnrichmentCoordinator.StartAsync`) depends on T039; T043 (private method language wiring) depends on T042; T044 (build + smoke-test) depends on all prior Phase 9 tasks.

**Within Phase 10** — T045–T050 each target a different query file and a different controller file; all six can run simultaneously.

**Phase 10 vs Phase 11 vs Phase 12 vs Phase 13** — All four new phases are independent of each other and can run in parallel. T053 depends on T052; T055 and T054 can run in parallel (same file, no dependency between the two changes).

### Suggested MVP Scope

**User Story 1 + User Story 2 only** (Phases 1–3):
- Phases 1–3 are entirely additive to existing code
- No new DB migration
- No new endpoints
- Zero risk of breaking existing functionality
- Ship scan language + media DTO fields first; profile-picture feature (Phases 4–6) can follow

**Phase 9 (Enrichment Language)** is a self-contained follow-on to Phase 2 that can be shipped independently once Phase 8 is complete — it touches only three files across two layers (Application + Infrastructure) and introduces no schema changes or new endpoints.

**Phases 10–13 (Frontend-driven enhancements)** are all additive and independent — any can be shipped in isolation once Phase 8 is complete. No new migrations. Phase 12 (Batch Assign) is the only phase that adds a new endpoint; Phases 10, 11, and 13 extend existing endpoints only.


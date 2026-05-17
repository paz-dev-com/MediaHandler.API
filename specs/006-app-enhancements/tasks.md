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
- [ ] T037 Apply migration (`dotnet ef database update -p MediaHandler.Infrastructure -s MediaHandler.API`) and run the quickstart verification checklist: (1) POST scan with `language: "fr"` → 202; (2) GET media detail → `status` + `numberOfSeasons` present; (3) POST profile-picture → 200 with path; (4) GET `/auth/me` → path in response; (5) DELETE profile-picture → 200 with null path

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
```

### Parallel Opportunities

**Within Phase 1** — T002, T003, T004 can all run in parallel after T001 (they are different records in different files).

**Within Phase 2** — T005 and T006 can run in parallel; T007 depends on both; T009 and T010 can be done together once T007 is merged.

**Within Phase 3** — T011 and T012 are fully independent (different handler files).

**Within Phase 4** — T013 and T014 are independent (different command files); both need T001+T002 from Phase 1.

**Within Phase 7** — T019–T031 are all independent unit tests and can be written in parallel. T032 (integration) requires Phase 6 to be complete.

### Suggested MVP Scope

**User Story 1 + User Story 2 only** (Phases 1–3):
- Phases 1–3 are entirely additive to existing code
- No new DB migration
- No new endpoints
- Zero risk of breaking existing functionality
- Ship scan language + media DTO fields first; profile-picture feature (Phases 4–6) can follow

# Implementation Plan: App Enhancements — Backend API Changes

**Branch**: `feature/006-app-enhancements` | **Date**: 2025-07-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-app-enhancements/spec.md`

## Summary

Extend the MediaHandler REST API with five targeted enhancements to support frontend app features: (1) add an optional `language` parameter to the scan endpoint and propagate it through `ScanStartParameters` → `ScanPipeline` → `MatchQuery` so all TMDB lookups during a scan use the requested locale; (2) expose the already-persisted `Media.Status` and `Media.NumberOfSeasons` fields in both `MediaDto` and `MediaListItemDto` so collection cards can display production-status badges without a detail fetch; (3) add `ProfilePicturePath` to the `User` entity (with EF Core migration) and expose it in `UserDto` so auth endpoints reflect custom pictures; and (4–5) introduce a new `UsersController` with upload (`POST /api/v1/users/profile-picture`), delete (`DELETE /api/v1/users/profile-picture`), and stream (`GET /api/v1/users/profile-picture/{fileName}`) endpoints backed by two new MediatR commands (`UploadProfilePictureCommand`, `DeleteProfilePictureCommand`), each following the established Clean Architecture, CQRS, Result-pattern, and FluentValidation pipeline conventions.

## Technical Context

**Language/Version**: C# / .NET 10  
**Primary Dependencies**: MediatR, FluentValidation, EF Core 10 (SQL Server), ASP.NET Core, AutoMapper  
**Storage**: SQL Server via EF Core — one new nullable column `ProfilePicturePath nvarchar(500)` on `Users`; filesystem under `wwwroot/uploads/profile-pictures/`  
**Testing**: xUnit, NSubstitute, EF Core InMemory (unit tests); Testcontainers.MsSql (integration tests)  
**Target Platform**: Linux server (Docker)  
**Project Type**: REST API (web service)  
**Performance Goals**: Profile picture upload ≤ 3 s for 2 MB files (SC-004); profile picture delete ≤ 1 s (SC-007); no measurable impact on scan or media-list latency  
**Constraints**: No new NuGet packages; no `app.UseStaticFiles()` addition; EF Core migration must apply without data loss; `wwwroot/uploads/profile-pictures/` created at startup if absent  
**Scale/Scope**: Personal NAS library; single-instance deployment; one active profile picture per user

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Clean Architecture — Dependency rule | ✅ PASS | Domain ← Application ← Infrastructure ← API. `ProfilePicturePath` in Domain entity. Commands/validators in Application. File I/O in handler (Application) with filesystem path resolved from `IWebHostEnvironment` injected via Infrastructure/API. Controllers in API. |
| I. CQRS via MediatR | ✅ PASS | Two new Command+Handler pairs: `UploadProfilePictureCommand`, `DeleteProfilePictureCommand`. Scan and media changes modify existing commands/DTOs — no new queries needed. |
| I. Result pattern | ✅ PASS | Both new handlers return `Result<UserDto>`. Delete returns `Result.Fail` (not-found) when `ProfilePicturePath` is null. No exceptions for expected failure cases. |
| I. FluentValidation pipeline | ✅ PASS | `UploadProfilePictureCommandValidator` validates file content-type (JPEG/PNG/WebP) and size (≤ 2 MB). Wired via existing `ValidationBehavior`. |
| I. Entity configuration (Fluent API) | ✅ PASS | `UserConfiguration` updated: `ProfilePicturePath` as `HasMaxLength(500)`, nullable. No data annotations. |
| I. Code style | ✅ PASS | File-scoped namespaces, `record` types for commands/DTOs, `#nullable enable`. |
| I. Domain events | ✅ PASS | Profile picture changes are self-contained within the user aggregate; no cross-cutting domain events needed. |
| II. Testing Standards | ✅ PASS | Unit tests per new handler (success + failure paths). Validator unit tests for type/size rules. Integration test for upload→me→delete flow. |
| III. User Experience | ✅ PASS | All endpoints return `ApiResponse<T>`. HTTP codes: 200 (success), 400 (validation), 401 (unauth), 404 (no picture on delete). `[ProducesResponseType]` on all actions. |
| III. Versioned routes | ✅ PASS | New endpoints under `/api/v1/users/…`. |
| III. Role-based access | ✅ PASS | Profile picture endpoints: `[Authorize]` only (acting user enforced by `ICurrentUserService.OktaId`). Scan endpoint AdminOnly unchanged. |
| IV. Performance | ✅ PASS | `AsNoTracking()` on read-only queries in handlers. File streamed via `FileStreamResult`; no full-memory load. Single user lookup by `OktaId` (indexed). |
| Architecture — Secrets | ✅ PASS | No new secrets; filesystem path is derived from `IWebHostEnvironment.WebRootPath`. |
| Workflow — Branching | ✅ PASS | Work on `feature/006-app-enhancements` branch. |

**Gate result**: ✅ ALL PASS — no violations. Proceeding to implementation.

## Project Structure

### Documentation (this feature)

```text
specs/006-app-enhancements/
├── plan.md              ← This file
├── spec.md              ← Feature specification (Status: Planned)
├── research.md          ← Phase 0 research decisions
├── data-model.md        ← Entity and DTO change reference
├── quickstart.md        ← Dev setup and verification checklist
└── contracts/
    └── api-endpoints.md ← Full endpoint contracts
```

### Source Code

```text
MediaHandler.Domain/
└── Entities/
    └── User.cs                                           ← MODIFIED: add ProfilePicturePath (string?)

MediaHandler.Application/
├── Common/
│   └── Models/Scanner/
│       └── ScanCoordinatorModels.cs                      ← MODIFIED: add Language? to ScanStartParameters
├── Common/
│   └── Mappings/
│       └── UserMappingProfile.cs                         ← VERIFY: AutoMapper convention covers ProfilePicturePath
├── Features/
│   ├── Auth/DTOs/
│   │   └── UserDto.cs                                    ← MODIFIED: add ProfilePicturePath (string?)
│   ├── Media/DTOs/
│   │   └── MediaDto.cs                                   ← MODIFIED: Status + NumberOfSeasons on MediaDto and MediaListItemDto
│   ├── Media/Queries/GetMediaById/
│   │   └── GetMediaByIdQueryHandler.cs                   ← MODIFIED: pass Status, NumberOfSeasons to new MediaDto(...)
│   ├── Media/Queries/GetMediaList/
│   │   └── GetMediaListQueryHandler.cs                   ← MODIFIED: project Status, NumberOfSeasons in .Select(...)
│   ├── Scan/Commands/StartScan/
│   │   └── StartScanCommand.cs                           ← MODIFIED: add Language?, pass to ScanStartParameters
│   └── Users/Commands/                                   ← NEW feature folder
│       ├── UploadProfilePicture/
│       │   └── UploadProfilePictureCommand.cs            ← NEW: record + AbstractValidator + handler
│       └── DeleteProfilePicture/
│           └── DeleteProfilePictureCommand.cs            ← NEW: record + handler

MediaHandler.Infrastructure/
├── Nas/Scanner/
│   └── ScanPipeline.cs                                   ← MODIFIED: accept language?, thread into MatchQuery
├── Services/
│   └── ScanRunCoordinator.cs                             ← MODIFIED: pass Language from ScanStartParameters
└── Persistence/Configurations/
    └── UserConfiguration.cs                              ← MODIFIED: add ProfilePicturePath column

MediaHandler.Infrastructure/ (or MediaHandler.API/)
└── Migrations/
    └── <timestamp>_AddProfilePicturePathToUser.cs        ← NEW: EF Core migration

MediaHandler.API/
├── Controllers/
│   └── UsersController.cs                                ← NEW: POST/DELETE/GET profile-picture
├── Contracts/Admin/
│   └── ScanRequests.cs                                   ← MODIFIED: add Language? to StartScanRequest
└── Program.cs                                            ← MODIFIED: ensure upload directory exists at startup

MediaHandler.Tests/
├── Features/Auth/
│   └── SyncUserCommandHandlerTests.cs                    ← MODIFIED: assert ProfilePicturePath in mapped dto
├── Features/Scan/
│   └── StartScanCommandHandlerTests.cs                   ← MODIFIED: assert Language forwarded to ScanStartParameters
└── Features/Users/
    ├── UploadProfilePictureCommandHandlerTests.cs        ← NEW
    ├── DeleteProfilePictureCommandHandlerTests.cs        ← NEW
    └── UploadProfilePictureValidatorTests.cs             ← NEW

MediaHandler.IntegrationTests/
└── Users/
    └── ProfilePictureEndpointTests.cs                    ← NEW: upload → GET /auth/me → delete flow
```

## Phase 0: Research Findings (Summary)

Full research details: [research.md](research.md)

| Decision | Rationale |
|----------|-----------|
| Language flows through `ScanStartParameters.Language?` → `ScanPipeline.ExecuteAsync(…, string? language)` → `MatchQuery.Language` | `MatchQuery` already has `Language` and is consumed by `TmdbMatcher` → `ITmdbService`. No `ITmdbService` interface changes required. Minimal blast radius. |
| Empty string `language` normalized to null in `StartScanCommandHandler` | Spec scenario 5: `""` → default behavior (`"en-US"` applied by `ScanPipeline`). |
| `ITmdbService` interface NOT modified | All scanner TMDB calls route through `ITmdbMatcher.ResolveAsync(MatchQuery)`. ITmdbService language params already exist and default to `"en-US"`. |
| `ScanPipeline.ExecuteAsync` receives `string? language = null` | Least-invasive addition; no `ScanRun` entity or DB schema touched. |
| Profile pictures served via dedicated `GET /api/v1/users/profile-picture/{fileName}` | No `app.UseStaticFiles()` in `Program.cs`. File access under API routing layer. |
| `ProfilePicturePath` stored as relative URL `/api/v1/users/profile-picture/{userId}.{ext}` | Frontend uses this URL directly. The streaming endpoint resolves it to `{WebRootPath}/uploads/profile-pictures/{fileName}`. Consistent with FR-011 and FR-013. |
| Old file cleanup on extension change handled in handler | Read `User.ProfilePicturePath`, extract extension, delete if different; otherwise overwrite in place. |
| File validation: `Content-Type` header + file extension | Magic-byte inspection deferred (spec assumption §Assumptions, last item). `UploadProfilePictureCommandValidator` checks both `ContentType` and `Path.GetExtension(FileName)`. |
| `wwwroot/uploads/profile-pictures/` created in `Program.cs` startup | `Directory.CreateDirectory(Path.Combine(env.WebRootPath, "uploads", "profile-pictures"))` — idempotent, no manual server setup. |
| AutoMapper `UserMappingProfile` — no change required | AutoMapper convention maps `ProfilePicturePath` by name. Verified that existing `CreateMap<User, UserDto>()` will pick it up without explicit `.ForMember`. |

## Phase 1: Design Artifacts

- [data-model.md](data-model.md) — entity schemas, DTO changes, migration details
- [contracts/api-endpoints.md](contracts/api-endpoints.md) — full endpoint contracts
- [quickstart.md](quickstart.md) — dev setup and verification checklist

## Implementation Phases

### Phase 0 — Verification & Setup

| # | Task | Notes |
|---|------|-------|
| T001 | Confirm `Media.Status` and `Media.NumberOfSeasons` exist on entity — ✅ confirmed in `Media.cs` | Read-only verification |
| T002 | Confirm `ITmdbService` methods already accept `language` (string) — ✅ all methods accept it | Read-only verification |
| T003 | Confirm `MatchQuery.Language` is carried into `TmdbMatcher.SearchCandidatesAsync` — ✅ `query with { Language = lang }` used | Read-only verification |
| T004 | Add `Directory.CreateDirectory` for upload folder to `Program.cs` startup | Infrastructure |

### Phase 1 — Domain & DTOs

| # | Task | File | Type |
|---|------|------|------|
| T005 | Add `ProfilePicturePath` to `User` entity | `User.cs` | MODIFY |
| T006 | Add `ProfilePicturePath` to `UserDto` record | `UserDto.cs` | MODIFY |
| T007 | Verify `UserMappingProfile` needs no explicit `.ForMember` for new property | `UserMappingProfile.cs` | VERIFY |
| T008 | Add `Status` (string?), `NumberOfSeasons` (int?) to `MediaDto` record | `MediaDto.cs` | MODIFY |
| T009 | Add `Status` (string?), `NumberOfSeasons` (int?) to `MediaListItemDto` record | `MediaDto.cs` | MODIFY |

### Phase 2 — Scan Language Propagation

| # | Task | File | Type |
|---|------|------|------|
| T010 | Add `string? Language = null` to `ScanStartParameters` | `ScanCoordinatorModels.cs` | MODIFY |
| T011 | Add `string? Language` to `StartScanCommand`; normalize empty→null; pass to `ScanStartParameters` | `StartScanCommand.cs` | MODIFY |
| T012 | Add `string? Language` to `StartScanRequest` | `ScanRequests.cs` | MODIFY |
| T013 | Pass `request.Language` to `new StartScanCommand(…)` in `AdminScanController.StartScan` | `AdminScanController.cs` | MODIFY |
| T014 | Add `string? language` param to `ScanPipeline.ExecuteAsync`; `ScanRunCoordinator.ExecuteScanAsync` passes `parameters.Language` | `ScanRunCoordinator.cs`, `ScanPipeline.cs` | MODIFY |
| T015 | In `ScanPipeline`, set `Language = language ?? "en-US"` on each `MatchQuery` constructed during classify/match stage | `ScanPipeline.cs` | MODIFY |

### Phase 3 — Media DTO Handler Updates

| # | Task | File | Type |
|---|------|------|------|
| T016 | Add `media.Status`, `media.NumberOfSeasons` to `new MediaDto(…)` constructor call | `GetMediaByIdQueryHandler.cs` | MODIFY |
| T017 | Add `m.Status`, `m.NumberOfSeasons` to `.Select(m => new MediaListItemDto(…))` projection | `GetMediaListQueryHandler.cs` | MODIFY |

### Phase 4 — Profile Picture Commands

| # | Task | File | Type |
|---|------|------|------|
| T018 | Create `UploadProfilePictureCommand(OktaId, FileStream, FileName, ContentType, FileSize)` + `UploadProfilePictureCommandValidator` (type + size) + handler (lookup user, cleanup old, save file, update `ProfilePicturePath`, return `UserDto`) | `UploadProfilePicture/UploadProfilePictureCommand.cs` | NEW |
| T019 | Create `DeleteProfilePictureCommand(OktaId)` + handler (lookup user, 404 if no path, delete file if exists, clear `ProfilePicturePath`, return `UserDto`) | `DeleteProfilePicture/DeleteProfilePictureCommand.cs` | NEW |

### Phase 5 — EF Core Configuration & Migration

| # | Task | File | Type |
|---|------|------|------|
| T020 | Add `ProfilePicturePath` config to `UserConfiguration`: `HasMaxLength(500)`, nullable | `UserConfiguration.cs` | MODIFY |
| T021 | Run `dotnet ef migrations add AddProfilePicturePathToUser -p MediaHandler.Infrastructure -s MediaHandler.API` | CLI | NEW |
| T022 | Review generated migration: expect `nullable nvarchar(500)` column, no `AlterColumn` on existing rows | Migration file | VERIFY |

### Phase 6 — API Layer

| # | Task | File | Type |
|---|------|------|------|
| T023 | Create `UsersController` with: `POST profile-picture` (multipart, dispatch `UploadProfilePictureCommand`), `DELETE profile-picture` (dispatch `DeleteProfilePictureCommand`), `GET profile-picture/{fileName}` (stream file from filesystem) | `UsersController.cs` | NEW |
| T024 | Inject `IWebHostEnvironment` into `UsersController` to resolve `WebRootPath` for serving | `UsersController.cs` | — |

### Phase 7 — Tests

| # | Task | File | Type |
|---|------|------|------|
| T025 | `UploadProfilePicture_WithValidJpeg_ReturnsUpdatedUserDto` | `UploadProfilePictureCommandHandlerTests.cs` | NEW |
| T026 | `UploadProfilePicture_UserNotFound_ReturnsFailure` | same | NEW |
| T027 | `UploadProfilePicture_ExtensionChanges_DeletesOldFile` | same | NEW |
| T028 | `DeleteProfilePicture_WithExistingPicture_ClearsPathAndReturnsDto` | `DeleteProfilePictureCommandHandlerTests.cs` | NEW |
| T029 | `DeleteProfilePicture_NoPicture_ReturnsNotFound` | same | NEW |
| T030 | `DeleteProfilePicture_FileAlreadyGone_StillClearsDatabasePath` | same | NEW |
| T031 | `Validate_ValidJpeg_PassesValidation` / `Validate_UnsupportedType_FailsValidation` / `Validate_OversizedFile_FailsValidation` | `UploadProfilePictureValidatorTests.cs` | NEW |
| T032 | `StartScan_WithLanguage_ForwardsLanguageToCoordinator` | `StartScanCommandHandlerTests.cs` | MODIFY |
| T033 | `GetMediaById_EnrichedTvShow_ReturnsStatusAndNumberOfSeasons` | `GetMediaByIdQueryHandlerTests.cs` | NEW |
| T034 | Integration: upload JPEG → verify `GET /auth/me` returns path → call `DELETE` → verify null | `ProfilePictureEndpointTests.cs` | NEW |

## Design Decisions

### Language Propagation Depth

The spec (FR-002, Q5) requires language propagation to all TMDB calls made during a scan. The scan pipeline routes all TMDB work through `ITmdbMatcher.ResolveAsync(MatchQuery)`. The `MatchQuery.Language` field already exists and is passed directly to `ITmdbService.SearchCandidatesAsync`. By threading `language?` down to `ScanPipeline.ExecuteAsync` and setting it on each `MatchQuery`, 100% of TMDB calls within a scan wall become language-aware — without touching `ITmdbService` or the enrichment flow.

```
AdminScanController.StartScan(StartScanRequest { Language })
  → StartScanCommand { Language }
    → ScanStartParameters { Language }
      → ScanRunCoordinator.ExecuteScanAsync → ScanPipeline.ExecuteAsync(…, language)
        → MatchQuery { Language = language ?? "en-US" }
          → TmdbMatcher → ITmdbService.SearchCandidatesAsync(query, year, kind, language)
```

### Profile Picture Filename Strategy

File name: `{user.Id}.{ext}` — using the internal `Guid` Id (not `OktaId`). This is stable, unique, safe for path construction, and avoids URL-encoding issues with OktaId strings. `ProfilePicturePath` stored as `/api/v1/users/profile-picture/{userId}.{ext}` — the frontend uses this as-is; the streaming endpoint resolves `{fileName}` from the route to `Path.Combine(WebRootPath, "uploads", "profile-pictures", fileName)`.

### Extension-Change Cleanup

```
if (user.ProfilePicturePath is not null)
{
    var oldExt = Path.GetExtension(user.ProfilePicturePath);  // e.g. ".jpg"
    var newExt = Path.GetExtension(command.FileName);          // e.g. ".png"
    if (!oldExt.Equals(newExt, StringComparison.OrdinalIgnoreCase))
        File.Delete(Path.Combine(uploadsDir, Path.GetFileName(user.ProfilePicturePath)));
}
```
Same-extension uploads simply overwrite (no explicit delete). The `DELETE` handler unconditionally clears the DB record even if `File.Exists(path)` returns false (spec edge case §edge-cases, items 3–4).

### `UsersController` — Serve Without Authentication

The `GET /api/v1/users/profile-picture/{fileName}` streaming action does not require `[Authorize]` on the action (the controller-level `[Authorize]` can be overridden with `[AllowAnonymous]`). This allows the browser to load the image `<img src="…">` tag without including a Bearer token, consistent with normal browser image loading behaviour. The spec does not mandate auth on the GET endpoint.

### MediaDto — Manual Construction (No AutoMapper)

Both `MediaDto` and `MediaListItemDto` are constructed positionally in their respective query handlers. `Status` and `NumberOfSeasons` are appended as the last two parameters on each record and each construction site. No AutoMapper profile is needed for media DTOs.

## Complexity Tracking

> No constitution violations. No entry required.

All changes follow established patterns. No new packages, no new architecture layers, no deviation from CQRS/Result/FluentValidation conventions.

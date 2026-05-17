# Research: App Enhancements — Backend API Changes

**Feature**: 006-app-enhancements  
**Date**: 2025-07-24  
**Status**: Complete — all NEEDS CLARIFICATION resolved

## Codebase Verification Findings

### R-001: Scan Language — Propagation Path

**Question**: How does `language` most cleanly flow from the HTTP request to the individual TMDB API calls made during a scan without breaking existing scanner integration tests?

**Decision**: Thread `string? Language = null` through four existing types. No interface changes required.

**Path traced**:
```
StartScanRequest            → Contracts/Admin/ScanRequests.cs (MODIFY)
StartScanCommand            → Application/Features/Scan/Commands/StartScan/ (MODIFY)
ScanStartParameters         → Application/Common/Models/Scanner/ScanCoordinatorModels.cs (MODIFY)
ScanRunCoordinator          → Infrastructure/Services/ScanRunCoordinator.cs (MODIFY)
ScanPipeline.ExecuteAsync   → Infrastructure/Nas/Scanner/ScanPipeline.cs (MODIFY)
MatchQuery.Language         → Application/Common/Models/Scanner/TmdbMatchModels.cs (already has Language field)
TmdbMatcher                 → Infrastructure/Nas/Scanner/TmdbMatcher.cs (no change — consumes MatchQuery)
ITmdbService.SearchCandidatesAsync(query, year, kind, language)  (no change — already accepts language)
```

**Key finding**: `MatchQuery` already has `Language = "en-US"` (default) and `SearchLanguages` (multi-language override list). The `TmdbMatcher` already passes `lang` to every `ITmdbService` call it makes (`SearchCandidatesAsync`, `GetMovieByIdAsync`, `GetTvShowByIdAsync`). The only missing piece is plumbing `language` from `ScanStartParameters` all the way to `MatchQuery` construction in `ScanPipeline`.

**ITmdbService status**: All methods already accept `language` (string). No interface change needed:
- `SearchMediaAsync(string query, string language, …)` — required param
- `GetMediaDetailsAsync(int tmdbId, string mediaType, string language, …)` — required param
- `GetTvShowSeasonsAsync(int tmdbId, string language, …)` — required param
- `GetMovieByIdAsync(int tmdbId, string language = "en-US", …)` — optional with default
- `GetTvShowByIdAsync(int tmdbId, string language = "en-US", …)` — optional with default
- `SearchCandidatesAsync(string query, int? year, MediaType? kindHint, string language = "en-US", …)` — optional with default

**Rationale**: Adding `Language?` to `ScanStartParameters` and threading it to `ScanPipeline.ExecuteAsync` is the minimal, non-breaking change. Existing integration tests construct `ScanStartParameters` without `Language` — the `= null` default makes this fully backward-compatible.

**Alternatives considered**:
- Modify `ITmdbService` to accept `language?` on all methods → rejected: unnecessary given `MatchQuery` already carries it
- Add `Language` to `ScanRun` entity (persisted) → rejected: scan language is a run-time concern, not historical data; spec does not require it to be persisted

---

### R-002: Media Status/NumberOfSeasons — DTO Gap

**Question**: Do `Media.Status` and `Media.NumberOfSeasons` already exist, and is the DTO construction pattern purely manual (not AutoMapper)?

**Decision**: Pure manual positional construction. Add fields as new trailing parameters on both `record` types and all call sites.

**Key findings**:
- `Media.Status` (`string?`) — exists, line 49 of `Media.cs`: `public string? Status { get; set; }`
- `Media.NumberOfSeasons` (`int?`) — exists, line 52 of `Media.cs`: `public int? NumberOfSeasons { get; set; }`
- Both are populated during the enrichment pipeline (feature 004). No migration needed.
- `MediaDto` and `MediaListItemDto` are constructed via `new MediaDto(…)` / `.Select(m => new MediaListItemDto(…))` — no AutoMapper involved.
- `GetMediaByIdQueryHandler` constructs `new MediaDto(media.Id, media.TmdbId, … media.VoteAverage, genres, files, userMedia?.IsWatched)` — 13 positional args.
- `GetMediaListQueryHandler` uses `.Select(m => new MediaListItemDto(m.Id, m.TmdbId, … m.MediaFiles.Count, …))` — inline EF Core projection (translatable to SQL).

**Impact check**: No other query handlers currently construct `MediaDto` or `MediaListItemDto`. Grep for `new MediaDto(` and `new MediaListItemDto(` confirms only the two files above.

**Rationale**: Adding `Status` and `NumberOfSeasons` as trailing nullable params to both records is a non-breaking additive change. All existing constructor call sites are in exactly two files that are both being updated.

**Alternatives considered**:
- Introduce an AutoMapper profile for media → rejected: existing pattern is manual; AutoMapper would require EF Core `ProjectTo<>` or `AsQueryable` changes to keep SQL translation
- Add fields only to `MediaDto`, not `MediaListItemDto` → rejected: spec FR-003 and Q2 explicitly require both DTOs

---

### R-003: Profile Picture — Storage and Serving Strategy

**Question**: Where are files stored, how are they served, and what path is persisted in the database?

**Decision**: Store in `{WebRootPath}/uploads/profile-pictures/{userId}.{ext}`. Serve via dedicated `GET /api/v1/users/profile-picture/{fileName}`. Persist route as `ProfilePicturePath = "/api/v1/users/profile-picture/{userId}.{ext}"`.

**Key findings**:
- `app.UseStaticFiles()` is absent from `Program.cs`. Adding it would be an infrastructure change beyond scope.
- `IWebHostEnvironment.WebRootPath` resolves to the `wwwroot/` folder at runtime. For Linux Docker deployments, this is `/app/wwwroot` (typical ASP.NET Core publish layout).
- `UsersController` does not currently exist — must be created.
- The `GET` endpoint can use `[AllowAnonymous]` to override the controller-level `[Authorize]`. This matches normal browser `<img>` tag loading patterns (no Bearer token sent for image src).
- `FileStreamResult` (returned as `PhysicalFileResult` or manual `new FileStream + return File(stream, contentType)`) streams without loading entire file into memory.

**Path conflict edge case**: `{userId}.jpg` and `{userId}.png` are different files. When user switches format, the old file must be deleted before saving the new one. Filename strategy `{user.Id}.{ext}` ensures at most one file per user (modulo rare same-second format switch).

**Rationale**: API endpoint approach keeps all access routed through the ASP.NET Core pipeline. Static files approach was rejected because it requires `app.UseStaticFiles()` (spec explicitly prohibits this for this iteration) and would bypass the authorization layer.

**Alternatives considered**:
- External object storage (S3-compatible) → rejected: spec assumption, no CDN or external storage in this iteration
- Store path as relative filesystem path instead of API route → rejected: spec FR-011 says store as API route
- Serve via static file middleware → rejected: would require `app.UseStaticFiles()` which is absent and out of scope

---

### R-004: Profile Picture — Validation Approach

**Question**: Should validation use magic-byte inspection or Content-Type + extension?

**Decision**: Content-Type header + filename extension check only. Magic-byte inspection is explicitly deferred per spec.

**Implementation**:
```csharp
var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/webp" };
var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };

RuleFor(x => x.ContentType)
    .Must(ct => allowedContentTypes.Contains(ct, StringComparer.OrdinalIgnoreCase))
    .WithMessage("File must be JPEG, PNG, or WebP.");

RuleFor(x => x.FileName)
    .Must(fn => allowedExtensions.Contains(Path.GetExtension(fn), StringComparer.OrdinalIgnoreCase))
    .WithMessage("File extension must be .jpg, .jpeg, .png, or .webp.");

RuleFor(x => x.FileSize)
    .LessThanOrEqualTo(2 * 1024 * 1024)
    .WithMessage("File must not exceed 2 MB.");
```

**Rationale**: FluentValidation runs in the MediatR pipeline before the handler executes. An invalid file is rejected before any filesystem writes occur, satisfying SC-005. The validator operates on the command record — it receives `ContentType` (from `IFormFile.ContentType`) and `FileName` (from `IFormFile.FileName`) as scalars, keeping the validator testable without touching `IFormFile`.

---

### R-005: Current User Identity — OktaId Resolution

**Question**: How does `UploadProfilePictureCommand` know which user is performing the action?

**Decision**: Controller reads `ICurrentUserService.OktaId` and passes it as a string to the command. Handler performs `FirstOrDefaultAsync(u => u.OktaId == command.OktaId)` — identical pattern to `SyncUserCommandHandler`.

**Confirmed pattern** from `SyncUserCommandHandler.cs`:
```csharp
var user = await context.Users
    .FirstOrDefaultAsync(u => u.OktaId == request.OktaId, cancellationToken);
```

**`ICurrentUserService.OktaId`** resolves from `HttpContext.User.FindFirstValue("sub")` — confirmed in `CurrentUserService.cs`.

**Rationale**: Controller stays thin (no DB access). Handler owns the user resolution. Consistent with the existing auth command pattern.

---

### R-006: EF Core Migration Scope

**Question**: What changes to the EF Core schema are required?

**Decision**: Single nullable `nvarchar(500)` column `ProfilePicturePath` on the `Users` table. No other schema changes.

**Migration name**: `AddProfilePicturePathToUser`

**Expected generated SQL** (conceptual):
```sql
ALTER TABLE [Users] ADD [ProfilePicturePath] nvarchar(500) NULL;
```

**Why nvarchar(500)**: Consistent with existing nullable string conventions in `UserConfiguration.cs` (e.g., `DisplayName` at 200, `OktaId` at 100). A full API route is at most ~80 characters (`/api/v1/users/profile-picture/{36-char-guid}.webp`), so 500 provides safe headroom.

**Existing nullable string pattern**: `DisplayName` is `HasMaxLength(200)` with no `IsRequired()` — adopted same pattern.

**Alternatives**: `nvarchar(1000)` — overkill for a path; `nvarchar(max)` — inconsistent with existing codebase string conventions.

---

## NEEDS CLARIFICATION — All Resolved

| Item | Resolution |
|------|-----------|
| Exact language propagation chain in scanner | Traced: `ScanStartParameters.Language` → `ScanPipeline.ExecuteAsync(string? language)` → `MatchQuery.Language` |
| `ITmdbService` change required? | No — all methods already accept `language` parameter |
| `MatchQuery` language field exists? | Yes — `Language = "en-US"` with `SearchLanguages` override list |
| MediaDto construction pattern (AutoMapper vs manual) | Manual positional constructors in both `GetMediaByIdQueryHandler` and `GetMediaListQueryHandler` |
| `Media.Status` / `NumberOfSeasons` already in entity? | Yes — added in feature 004, already persisted |
| Existing `UsersController`? | No — must create new |
| `app.UseStaticFiles()` presence? | Absent — profile picture serving uses dedicated streaming endpoint |
| AutoMapper needed for `ProfilePicturePath`? | No explicit `.ForMember` needed — convention mapping handles it |
| Migration column type | `nullable nvarchar(500)` — consistent with existing User string properties |


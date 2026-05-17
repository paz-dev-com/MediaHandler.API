# Data Model: App Enhancements — Backend API Changes

**Feature**: 006-app-enhancements  
**Date**: 2025-07-24

## Entity Changes

### User (MODIFIED)

**File**: `MediaHandler.Domain/Entities/User.cs`

**Change**: Add one nullable property.

```csharp
public string? ProfilePicturePath { get; set; }
```

**Full entity after change**:

| Property | Type | Nullable | Notes |
|----------|------|----------|-------|
| `Id` | `Guid` | No | Inherited from `BaseEntity` |
| `OktaId` | `string` | No | Auth0 `sub` claim, unique index |
| `Email` | `string` | No | Unique index |
| `DisplayName` | `string?` | Yes | — |
| `PreferredLanguage` | `string` | No | Default `"en"` |
| `Role` | `UserRole` | No | Enum stored as string |
| `IsActive` | `bool` | No | Default `true` |
| **`ProfilePicturePath`** | `string?` | **Yes** | **NEW** — stored as API route `/api/v1/users/profile-picture/{userId}.{ext}`; null when no custom picture |

### Media (UNCHANGED)

`Media.Status` (`string?`) and `Media.NumberOfSeasons` (`int?`) already exist (added in feature 004). No entity changes required.

---

## DTO Changes

### UserDto (MODIFIED)

**File**: `MediaHandler.Application/Features/Auth/DTOs/UserDto.cs`

**Before**:
```csharp
public record UserDto(
    Guid Id,
    string Email,
    string? DisplayName,
    string PreferredLanguage,
    string Role,
    bool IsActive);
```

**After**:
```csharp
public record UserDto(
    Guid Id,
    string Email,
    string? DisplayName,
    string PreferredLanguage,
    string Role,
    bool IsActive,
    string? ProfilePicturePath);   // NEW — null when no custom picture
```

**Mapping**: `UserMappingProfile` (`AutoMapper`) — no explicit `.ForMember` required; convention matches `User.ProfilePicturePath` → `UserDto.ProfilePicturePath` by name.

**Affected handlers** (return `UserDto` via AutoMapper):
- `SyncUserCommandHandler` — picks up via AutoMapper, no code change
- `GetCurrentUserQueryHandler` — picks up via AutoMapper, no code change
- `UpdatePreferencesCommandHandler` — picks up via AutoMapper, no code change
- `UploadProfilePictureCommandHandler` — NEW handler; uses AutoMapper
- `DeleteProfilePictureCommandHandler` — NEW handler; uses AutoMapper

---

### MediaDto (MODIFIED)

**File**: `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs`

**Before**:
```csharp
public record MediaDto(
    Guid Id,
    int TmdbId,
    string Title,
    string? OriginalTitle,
    string? Overview,
    MediaType Type,
    DateTime? ReleaseDate,
    int? Runtime,
    string? PosterPath,
    string? BackdropPath,
    decimal? VoteAverage,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MediaFileDto> Files,
    bool? IsWatched);
```

**After**:
```csharp
public record MediaDto(
    Guid Id,
    int TmdbId,
    string Title,
    string? OriginalTitle,
    string? Overview,
    MediaType Type,
    DateTime? ReleaseDate,
    int? Runtime,
    string? PosterPath,
    string? BackdropPath,
    decimal? VoteAverage,
    IReadOnlyList<string> Genres,
    IReadOnlyList<MediaFileDto> Files,
    bool? IsWatched,
    string? Status,            // NEW — e.g. "Returning Series", "Ended", "Released"; null for unenriched
    int? NumberOfSeasons);     // NEW — TV shows only; null for films or unenriched
```

**Affected handler**: `GetMediaByIdQueryHandler` — add `media.Status`, `media.NumberOfSeasons` to positional constructor call.

---

### MediaListItemDto (MODIFIED)

**File**: `MediaHandler.Application/Features/Media/DTOs/MediaDto.cs` (same file)

**Before**:
```csharp
public record MediaListItemDto(
    Guid Id,
    int TmdbId,
    string Title,
    MediaType Type,
    DateTime? ReleaseDate,
    string? PosterPath,
    decimal? VoteAverage,
    int FileCount,
    bool? IsWatched);
```

**After**:
```csharp
public record MediaListItemDto(
    Guid Id,
    int TmdbId,
    string Title,
    MediaType Type,
    DateTime? ReleaseDate,
    string? PosterPath,
    decimal? VoteAverage,
    int FileCount,
    bool? IsWatched,
    string? Status,            // NEW — same values as MediaDto.Status
    int? NumberOfSeasons);     // NEW — TV shows only
```

**Affected handler**: `GetMediaListQueryHandler` — add `m.Status`, `m.NumberOfSeasons` to the `.Select(m => new MediaListItemDto(…))` EF Core projection. Both fields are simple column projections — EF Core translates to SQL `SELECT [m].[Status], [m].[NumberOfSeasons]`.

---

### StartScanRequest (MODIFIED)

**File**: `MediaHandler.API/Contracts/Admin/ScanRequests.cs`

**Before**:
```csharp
public record StartScanRequest(
    Guid[] LibraryRootIds,
    ScanMode Mode);
```

**After**:
```csharp
public record StartScanRequest(
    Guid[] LibraryRootIds,
    ScanMode Mode,
    string? Language);    // NEW — optional BCP-47 language tag (e.g. "fr", "en"); null → default behavior
```

---

### ScanStartParameters (MODIFIED)

**File**: `MediaHandler.Application/Common/Models/Scanner/ScanCoordinatorModels.cs`

**Before**:
```csharp
public record ScanStartParameters(
    Guid ScanRunId,
    Guid[] LibraryRootIds,
    ScanMode Mode);
```

**After**:
```csharp
public record ScanStartParameters(
    Guid ScanRunId,
    Guid[] LibraryRootIds,
    ScanMode Mode,
    string? Language = null);    // NEW — passed to ScanPipeline; null uses "en-US" default
```

---

## New Commands

### UploadProfilePictureCommand (NEW)

**File**: `MediaHandler.Application/Features/Users/Commands/UploadProfilePicture/UploadProfilePictureCommand.cs`

```csharp
public record UploadProfilePictureCommand(
    string OktaId,
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSize) : IRequest<Result<UserDto>>;
```

**Handler responsibilities**:
1. Resolve `User` by `OktaId` (`FirstOrDefaultAsync`) → `Result.Fail` if not found
2. Determine `uploadsDir = Path.Combine(webRootPath, "uploads", "profile-pictures")`
3. Determine `newExt = Path.GetExtension(FileName).ToLowerInvariant()` (e.g. `.jpg`)
4. Determine `newFileName = $"{user.Id}{newExt}"` (e.g. `7f3a…guid….jpg`)
5. If `user.ProfilePicturePath is not null`: extract old extension from stored path; if it differs from `newExt`, delete old file at `uploadsDir/{oldFileName}`
6. Save file: `await using var f = File.Create(Path.Combine(uploadsDir, newFileName)); await FileStream.CopyToAsync(f)`
7. Update `user.ProfilePicturePath = $"/api/v1/users/profile-picture/{newFileName}"`
8. `await context.SaveChangesAsync()`
9. Return `Result.Success(mapper.Map<UserDto>(user))`

**Validator** (`UploadProfilePictureCommandValidator`):
- `ContentType` ∈ `{ image/jpeg, image/png, image/webp }` (case-insensitive)
- `Path.GetExtension(FileName)` ∈ `{ .jpg, .jpeg, .png, .webp }` (case-insensitive)
- `FileSize` ≤ `2 * 1024 * 1024` (2 MB)

---

### DeleteProfilePictureCommand (NEW)

**File**: `MediaHandler.Application/Features/Users/Commands/DeleteProfilePicture/DeleteProfilePictureCommand.cs`

```csharp
public record DeleteProfilePictureCommand(string OktaId) : IRequest<Result<UserDto>>;
```

**Handler responsibilities**:
1. Resolve `User` by `OktaId` → `Result.Fail` if not found
2. If `user.ProfilePicturePath is null` → return `Result.Fail("USER_HAS_NO_PROFILE_PICTURE", …)`
3. Determine filesystem path: `Path.Combine(webRootPath, "uploads", "profile-pictures", Path.GetFileName(user.ProfilePicturePath))`
4. If file exists on disk: `File.Delete(fsPath)` — if `File.Exists` returns false, proceed without error (spec edge case)
5. Set `user.ProfilePicturePath = null`
6. `await context.SaveChangesAsync()`
7. Return `Result.Success(mapper.Map<UserDto>(user))`

---

## Infrastructure: File Storage Layout

```
wwwroot/
└── uploads/
    └── profile-pictures/
        ├── 7f3a1c2d-4e5b-6f7a-8b9c-0d1e2f3a4b5c.jpg    ← user 1 (JPEG)
        ├── a1b2c3d4-e5f6-7a8b-9c0d-e1f2a3b4c5d6.png    ← user 2 (PNG)
        └── ...
```

**Directory creation**: `Program.cs` startup sequence:
```csharp
var uploadsDir = Path.Combine(builder.Environment.WebRootPath ?? 
    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), 
    "uploads", "profile-pictures");
Directory.CreateDirectory(uploadsDir);  // idempotent
```

**Served via**: `GET /api/v1/users/profile-picture/{fileName}` — `UsersController` streams from filesystem.

---

## EF Core Migration

**Migration name**: `AddProfilePicturePathToUser`

**Command**:
```bash
dotnet ef migrations add AddProfilePicturePathToUser \
  -p MediaHandler.Infrastructure \
  -s MediaHandler.API
```

**Expected migration UP**:
```csharp
migrationBuilder.AddColumn<string>(
    name: "ProfilePicturePath",
    table: "Users",
    type: "nvarchar(500)",
    maxLength: 500,
    nullable: true);
```

**Expected migration DOWN**:
```csharp
migrationBuilder.DropColumn(
    name: "ProfilePicturePath",
    table: "Users");
```

**Impact**: Non-destructive. Existing rows receive `NULL`. No data loss. Migration applies atomically.

**UserConfiguration update**:
```csharp
builder.Property(u => u.ProfilePicturePath)
    .HasMaxLength(500);
// nullable by default (no .IsRequired())
```


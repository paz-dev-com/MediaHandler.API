# API Endpoint Contracts: App Enhancements

**Feature**: 006-app-enhancements  
**Date**: 2025-07-24  
**Source**: Derived from frontend spec contracts (`MediaHandler.Web/specs/006-app-enhancements/contracts/api-contracts.md`)

---

## Modified Endpoints

### 1. POST /api/v1/admin/scan — Start Scan

**Authorization**: `AdminOnly`  
**Change**: Add optional `language` field to request body.

**Request Body**:
```json
{
  "libraryRootIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"],
  "mode": "Full | Incremental",
  "language": "en | fr | null"
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `libraryRootIds` | `Guid[]` | No | Empty array scans all enabled roots |
| `mode` | `ScanMode` | Yes | `Full` or `Incremental` |
| `language` | `string?` | No | BCP-47 tag (e.g. `"fr"`, `"en"`); null/omitted → default `"en-US"` |

**Response** (202 Accepted): Unchanged — `ApiResponse<ScanRunSummaryResponse>`.

**Validation**:
- Empty string `""` is treated as null (normalized in `StartScanCommandHandler`).
- Unrecognized locale codes are passed through to TMDB as-is (no API-level validation).

---

### 2. GET /api/v1/media/{id} — Get Media Detail

**Authorization**: Authenticated  
**Change**: Response DTO extended with `status` and `numberOfSeasons`.

**Response** (200 OK):
```json
{
  "succeeded": true,
  "data": {
    "id": "guid",
    "tmdbId": 1399,
    "title": "Game of Thrones",
    "originalTitle": "Game of Thrones",
    "overview": "...",
    "type": "TvShow",
    "releaseDate": "2011-04-17T00:00:00Z",
    "runtime": 60,
    "posterPath": "/poster.jpg",
    "backdropPath": "/backdrop.jpg",
    "voteAverage": 9.3,
    "genres": ["Action", "Drama"],
    "files": [],
    "isWatched": false,
    "status": "Ended",
    "numberOfSeasons": 8
  }
}
```

| New Field | Type | Notes |
|-----------|------|-------|
| `status` | `string?` | e.g. `"Returning Series"`, `"Ended"`, `"Released"`, `null` for unenriched |
| `numberOfSeasons` | `int?` | TV shows only; `null` for films or unenriched |

---

### 3. GET /api/v1/media — Get Media List

**Authorization**: Authenticated  
**Change**: Each `MediaListItemDto` in the paged response now includes `status` and `numberOfSeasons`.

**Response item** (extended fields only):
```json
{
  "id": "guid",
  "tmdbId": 1399,
  "title": "Game of Thrones",
  "type": "TvShow",
  "releaseDate": "2011-04-17T00:00:00Z",
  "posterPath": "/poster.jpg",
  "voteAverage": 9.3,
  "fileCount": 56,
  "isWatched": false,
  "status": "Ended",
  "numberOfSeasons": 8
}
```

---

### 4. GET /api/v1/auth/me — Get Current User

**Authorization**: Authenticated  
**Change**: Response DTO extended with `profilePicturePath`.

**Response** (200 OK):
```json
{
  "succeeded": true,
  "data": {
    "id": "guid",
    "email": "user@example.com",
    "displayName": "John Doe",
    "preferredLanguage": "en",
    "role": "User",
    "isActive": true,
    "profilePicturePath": "/api/v1/users/profile-picture/guid.jpg"
  }
}
```

`profilePicturePath` is `null` when no custom picture has been uploaded.

---

### 5. POST /api/v1/auth/sync — Sync User

**Change**: Response `UserDto` now includes `profilePicturePath` (same as above).

---

## New Endpoints

### 6. POST /api/v1/users/profile-picture — Upload Profile Picture

**Authorization**: `[Authorize]` (authenticated user; acts on current user only)  
**Content-Type**: `multipart/form-data`

**Request**:
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `file` | binary | Yes | Image file (JPEG, PNG, or WebP, ≤ 2 MB) |

**Response** (200 OK):
```json
{
  "succeeded": true,
  "data": {
    "id": "guid",
    "email": "user@example.com",
    "displayName": "John Doe",
    "preferredLanguage": "en",
    "role": "User",
    "isActive": true,
    "profilePicturePath": "/api/v1/users/profile-picture/guid.jpg"
  }
}
```

**Error Responses**:
| Code | Condition |
|------|-----------|
| `400 Bad Request` | Invalid file type (not JPEG/PNG/WebP) or file > 2 MB |
| `401 Unauthorized` | Not authenticated |
| `404 Not Found` | Authenticated user not found in database |
| `500 Internal Server Error` | Filesystem write failure |

**Behaviour**:
- File saved to `wwwroot/uploads/profile-pictures/{userId}.{ext}`.
- If user had a previous picture with a **different extension**, the old file is deleted before saving.
- Same-extension re-upload overwrites the existing file in place.
- `User.ProfilePicturePath` set to `/api/v1/users/profile-picture/{userId}.{ext}`.

---

### 7. DELETE /api/v1/users/profile-picture — Remove Profile Picture

**Authorization**: `[Authorize]` (authenticated user; acts on current user only)  
**Request Body**: None.

**Response** (200 OK):
```json
{
  "succeeded": true,
  "data": {
    "id": "guid",
    "email": "user@example.com",
    "displayName": "John Doe",
    "preferredLanguage": "en",
    "role": "User",
    "isActive": true,
    "profilePicturePath": null
  }
}
```

**Error Responses**:
| Code | Condition |
|------|-----------|
| `401 Unauthorized` | Not authenticated |
| `404 Not Found` | No custom profile picture (`ProfilePicturePath` is null) |

**Behaviour**:
- Deletes the file from the filesystem (if it still exists — missing file is not an error).
- Sets `User.ProfilePicturePath = null` and persists.
- Returns 200 OK with updated `UserDto`.

---

### 8. GET /api/v1/users/profile-picture/{fileName} — Stream Profile Picture

**Authorization**: `[AllowAnonymous]` (supports browser `<img>` tag loading)  
**Route parameter**: `fileName` — e.g. `guid.jpg`

**Response** (200 OK):
- Body: raw image binary
- `Content-Type`: `image/jpeg` | `image/png` | `image/webp` (inferred from extension)

**Error Responses**:
| Code | Condition |
|------|-----------|
| `404 Not Found` | File does not exist in `wwwroot/uploads/profile-pictures/{fileName}` |
| `400 Bad Request` | `fileName` contains path traversal characters (validation in controller) |

**Security note**: Path traversal protection required — validate `fileName` contains no directory separators or `..` sequences before building the filesystem path.

---

## Existing Endpoints (No Changes)

### GET /api/v1/files/locations — Get NAS Locations

Already exists. Returns `string[]` from `Nas.BasePaths` configuration. No changes in this feature.

---

## Controller: UsersController (NEW)

**Route**: `api/v1/users`  
**Authorization**: `[Authorize]` at controller level; `GET profile-picture/{fileName}` overrides with `[AllowAnonymous]`  
**Rate limiting**: `[EnableRateLimiting("fixed")]` (inherited from existing pattern)

```
POST   /api/v1/users/profile-picture           → UploadProfilePicture action
DELETE /api/v1/users/profile-picture           → DeleteProfilePicture action
GET    /api/v1/users/profile-picture/{fileName} → GetProfilePicture action
```


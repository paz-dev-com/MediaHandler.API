# Quickstart: App Enhancements — Backend API Changes

**Feature**: 006-app-enhancements  
**Branch**: `feature/006-app-enhancements`

## Prerequisites

- .NET 10 SDK
- Docker (for SQL Server via docker-compose)
- An Auth0 tenant (or existing dev `.env` / `user-secrets` configuration)
- EF Core CLI: `dotnet tool install --global dotnet-ef` (if not already installed)

---

## 1. Get on the Right Branch

```bash
git checkout feature/006-app-enhancements
# or create from develop
git checkout develop && git pull && git checkout -b feature/006-app-enhancements
```

---

## 2. Start SQL Server

```bash
cd /home/tpfeifer/Repos/MediaHandler/MediaHandler.API
docker-compose up -d
```

Verify: `docker ps` → `mediahandler-sql` container is healthy.

---

## 3. Apply the Migration

After implementing T020–T022 (UserConfiguration + migration file):

```bash
dotnet ef database update \
  -p MediaHandler.Infrastructure \
  -s MediaHandler.API
```

**Verify**: Connect to the database and check:
```sql
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'ProfilePicturePath';
-- Expected: nvarchar, YES, 500
```

---

## 4. Run the API

```bash
cd MediaHandler.API
dotnet run
```

The startup log should include:
```
info: ...Program[0]
      Upload directory ensured: /path/to/wwwroot/uploads/profile-pictures
```

Swagger UI: `https://localhost:5001/swagger`

---

## 5. Manual Verification Checklist

### US1 — Language-Aware Scan

1. Obtain an admin JWT token (via Auth0 / Postman).
2. POST `https://localhost:5001/api/v1/admin/scan`:
   ```json
   {
     "libraryRootIds": [],
     "mode": "Full",
     "language": "fr"
   }
   ```
3. **Expected**: `202 Accepted` with `ScanRunSummaryResponse`.
4. Confirm in application logs that TMDB search URLs include `language=fr` (enable `Debug` logging level for `MediaHandler.Infrastructure.Nas.Scanner`).
5. POST again with `"language": null` or omit the field → same 202; logs show `language=en-US` (default).
6. POST with `"language": ""` → same 202; logs show `language=en-US` (empty normalized to null).

---

### US2 — Status & Season Count in Media Responses

1. After a scan with enrichment, find a TV show `id` in the database.
2. GET `https://localhost:5001/api/v1/media/{id}`:
   ```json
   {
     "data": {
       "status": "Returning Series",
       "numberOfSeasons": 4,
       "..."
     }
   }
   ```
3. GET `https://localhost:5001/api/v1/media?type=TvShow`:
   ```json
   {
     "data": [
       { "status": "Ended", "numberOfSeasons": 6, "..." }
     ]
   }
   ```
4. For a film: `"status": "Released"`, `"numberOfSeasons": null`.
5. For unenriched media: both fields `null`.

---

### US3 — Profile Picture Upload

1. Authenticate as a regular user (non-admin JWT).
2. POST `https://localhost:5001/api/v1/users/profile-picture` as `multipart/form-data`:
   - `file`: any valid JPEG < 2 MB (e.g. a photo from your machine)
3. **Expected** `200 OK`:
   ```json
   {
     "succeeded": true,
     "data": {
       "profilePicturePath": "/api/v1/users/profile-picture/<guid>.jpg"
     }
   }
   ```
4. Verify file exists on filesystem: `ls MediaHandler.API/wwwroot/uploads/profile-pictures/`
5. GET `https://localhost:5001/api/v1/users/profile-picture/<guid>.jpg` in browser → image displays.
6. GET `https://localhost:5001/api/v1/auth/me` → response includes the same `profilePicturePath`.

**Negative tests**:
- POST with a `.gif` file → `400 Bad Request` with validation error, no file created.
- POST with a file > 2 MB → `400 Bad Request`, no file created.
- POST without JWT → `401 Unauthorized`.

---

### US4 — Profile Picture Delete

1. (Precondition: user has a profile picture from US3)
2. DELETE `https://localhost:5001/api/v1/users/profile-picture`
3. **Expected** `200 OK`:
   ```json
   {
     "succeeded": true,
     "data": {
       "profilePicturePath": null
     }
   }
   ```
4. Verify file is removed from filesystem.
5. GET `https://localhost:5001/api/v1/auth/me` → `profilePicturePath: null`.
6. DELETE again → `404 Not Found` (no picture to delete).

---

### US5 — Profile Picture Path in Auth Responses

1. Log in / sync: POST `https://localhost:5001/api/v1/auth/sync` → response includes `"profilePicturePath": null` (no picture yet).
2. Upload a picture (US3).
3. GET `https://localhost:5001/api/v1/auth/me` → `"profilePicturePath": "/api/v1/users/profile-picture/<guid>.jpg"`.
4. POST `https://localhost:5001/api/v1/auth/sync` again → same `profilePicturePath` returned.

---

## 6. Run Unit Tests

```bash
# All unit tests
dotnet test MediaHandler.Tests

# Just the new/modified tests for this feature
dotnet test MediaHandler.Tests --filter "FullyQualifiedName~Features.Users"
dotnet test MediaHandler.Tests --filter "FullyQualifiedName~Features.Scan.StartScan"
dotnet test MediaHandler.Tests --filter "FullyQualifiedName~Features.Media.GetMediaById"
```

Expected: all green, no regressions.

---

## 7. Run Integration Tests

```bash
# Requires Docker (Testcontainers will spin up SQL Server automatically)
dotnet test MediaHandler.IntegrationTests --filter "FullyQualifiedName~Users.ProfilePicture"
```

Expected: `ProfilePicture_Upload_GetMe_Delete_Flow` passes end-to-end.

---

## 8. CI Check (pre-PR)

```bash
dotnet format --verify-no-changes
dotnet build --no-incremental
dotnet test MediaHandler.Tests
dotnet test MediaHandler.IntegrationTests  # requires Docker
```

All four must pass before opening a PR.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| `wwwroot/uploads/...` directory not found | `WebRootPath` is null in test environment | Ensure `Program.cs` startup uses `env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")` |
| `415 Unsupported Media Type` on upload | Missing `[Consumes("multipart/form-data")]` attribute | Add to `UsersController.UploadProfilePicture` action |
| Migration fails with column already exists | Migration was applied twice | Run `dotnet ef database update <previous-migration-name>` to roll back first |
| TMDB calls still use `en-US` despite `language=fr` request | `Language` not threaded through `ScanStartParameters` | Verify `ScanRunCoordinator.ExecuteScanAsync` passes `parameters.Language` to `pipeline.ExecuteAsync` |
| `profilePicturePath` missing from `UserDto` responses | AutoMapper not picking up new property | Restart the API; verify `UserMappingProfile` uses `CreateMap<User, UserDto>()` (convention covers new property) |
| `numberOfSeasons` missing from media list | Handler `.Select(…)` projection not updated | Update `GetMediaListQueryHandler` positional `new MediaListItemDto(…)` call |


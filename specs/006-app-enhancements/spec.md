# Feature Specification: App Enhancements — Backend API Changes

**Feature Branch**: `feature/006-app-enhancements`  
**Created**: 2025-07-24  
**Status**: Planned  
**Input**: User description: "API-side changes to support frontend app enhancements: add language parameter to scan endpoint, extend MediaDto with TV show status and numberOfSeasons, add profilePicturePath to UserDto, and create profile picture upload/delete endpoints."

## Clarifications

### Session 2025-07-24

All decisions were established in the corresponding frontend spec session:

- **Scan language parameter**: Add optional `language?: string` to both `StartScanRequest` (request body DTO) and `StartScanCommand` (MediatR command). When provided, the language is passed to TMDB API queries during the scan. When null or omitted, the system falls back to the current default behavior (no language filter on TMDB queries). This mirrors the pattern used by the legacy `POST /api/v1/files/scan-and-import?language=` endpoint.
- **MediaDto extensions**: Extend `MediaDto` to expose `status` (string) and `numberOfSeasons` (int?) sourced from the existing `Media.Status` and `Media.NumberOfSeasons` entity fields. Both fields are already persisted from TMDB enrichment — no new DB migration or data population work is required for these two fields.
- **UserDto extension**: Add `profilePicturePath` (string?, null by default) to `UserDto`. Add the corresponding `ProfilePicturePath` nullable string property to the `User` entity. Requires a new EF Core migration.
- **Profile picture storage**: Custom profile pictures are stored on the server filesystem under `wwwroot/uploads/profile-pictures/{userId}.{ext}`. A dedicated `UsersController` is created (or extended) to house the new endpoints; they are **not** placed on `AuthController`.
- **Profile picture authorization**: The upload and delete endpoints enforce the current authenticated user only — not AdminOnly. A user may only manage their own profile picture. The current user's ID is resolved from the JWT claims.
- **Accepted file types**: JPEG, PNG, and WebP. Maximum file size: 2 MB. Validated via FluentValidation in the MediatR pipeline.

### Session 2026-05-16

Codebase-grounded clarifications after scanning actual implementation:

- **Current user ID resolution (Q1)**: Commands accept `OktaId` (string) from `ICurrentUserService.OktaId`. The handler resolves the internal `User` by `OktaId` as its first step (`FirstOrDefaultAsync(u => u.OktaId == ...)`) — identical to the pattern in `SyncUserCommandHandler`. The controller stays thin: it reads `HttpContext.User` via `ICurrentUserService` and passes `OktaId` to the command. No new interface method or DB lookup in the controller.
- **DTO scope for `status` and `numberOfSeasons` (Q2)**: These fields are added to **both** `MediaDto` (detail endpoint) **and** `MediaListItemDto` (collection/list endpoints) so the frontend can display production status badges on media cards in the collection view without a detail fetch.
- **Profile picture serving (Q3)**: Files are served through a **dedicated API endpoint** (`GET /api/v1/users/profile-picture/{fileName}`) rather than ASP.NET Core static file middleware. This avoids adding `app.UseStaticFiles()` to `Program.cs` (currently absent) and keeps all file access under the API authorization boundary. The endpoint streams the file from the filesystem with the correct `Content-Type` header.
- **Old file cleanup on format switch (Q4)**: When a user uploads a new profile picture whose extension differs from their current one (e.g., switching from JPEG to PNG), the **handler deletes the old file** before saving the new one. The previous path is read from `User.ProfilePicturePath` before overwriting.
- **Language propagation depth (Q5)**: `language` is propagated **all the way to `ITmdbClient`** — every TMDB request made during a scan (search, detail lookups, enrichment calls) includes the `language` parameter. The `ITmdbClient` interface and its implementation are updated to accept an optional `language` argument on all relevant methods.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Language-Aware Media Scan (Priority: P1)

As a user whose interface language is set to French, when the admin triggers a media scan, I want the scan to query TMDB in French so that media titles and metadata match my preferred language.

The API receives an optional `language` string in the scan request body alongside `libraryRootIds` and `mode`. The value (e.g. `"fr"`, `"en"`) is forwarded through the `StartScanCommand` to the TMDB service layer when performing lookups during the scan. When `language` is null or absent, existing TMDB query behavior is unchanged.

**Why this priority**: Scan quality is directly impacted. A wrong-language query returns wrong or no TMDB matches, corrupting the enrichment pipeline.

**Independent Test**: Submit `POST /api/v1/admin/scan` with `"language": "fr"` and verify that TMDB API calls made during the scan include the `language=fr` query parameter.

**Acceptance Scenarios**:

1. **Given** a valid scan request with `"language": "fr"` in the body, **When** the admin submits `POST /api/v1/admin/scan`, **Then** the scan begins and all TMDB queries during that scan use the `language=fr` parameter.
2. **Given** a valid scan request with `"language": "en"` in the body, **When** the admin submits the request, **Then** TMDB queries use `language=en`.
3. **Given** a valid scan request with `"language": null` or the field omitted, **When** the admin submits the request, **Then** TMDB queries use the existing default behavior (no language filter), and the response is unchanged (202 Accepted).
4. **Given** an authenticated non-admin user calls `POST /api/v1/admin/scan`, **When** the request is received, **Then** the response is 403 Forbidden.
5. **Given** a scan request where `language` is an empty string `""`, **When** the request is processed, **Then** it is treated as null/omitted (no language filter applied, no validation error).

---

### User Story 2 — TV Show Status & Season Count in Media Detail (Priority: P1)

As a user viewing a TV show detail page, I want to see whether the show is still in production and how many seasons exist according to TMDB, so I can identify gaps in my collection and know whether more content is expected.

The `GET /api/v1/media/{id}` response and all media list endpoints that return `MediaDto` now include `status` (string, e.g. `"Returning Series"`, `"Ended"`, `"Released"`) and `numberOfSeasons` (int?). Both fields are sourced from the `Media` entity where they have been stored since TMDB enrichment.

**Why this priority**: These fields are already in the database. Exposing them requires only a DTO change and is a minor non-breaking addition, yet it unlocks the TV show completeness features in the frontend.

**Independent Test**: Call `GET /api/v1/media/{id}` for a TMDB-enriched TV show and verify the response includes `status` and `numberOfSeasons` with correct values matching the stored `Media` entity.

**Acceptance Scenarios**:

1. **Given** an enriched TV show with `Media.Status = "Returning Series"` and `Media.NumberOfSeasons = 4`, **When** the client calls `GET /api/v1/media/{id}`, **Then** the response DTO includes `"status": "Returning Series"` and `"numberOfSeasons": 4`.
2. **Given** an enriched TV show with `Media.Status = "Ended"` and `Media.NumberOfSeasons = 6`, **When** the client calls the endpoint, **Then** the response includes `"status": "Ended"` and `"numberOfSeasons": 6`.
3. **Given** a Film (not a TV show), **When** the client calls `GET /api/v1/media/{id}`, **Then** `"status"` is `"Released"` (or null if not enriched) and `"numberOfSeasons"` is null.
4. **Given** a media entry that has not yet been enriched, **When** the client calls the endpoint, **Then** both `"status"` and `"numberOfSeasons"` are null in the response.
5. **Given** any media list endpoint that returns `MediaDto`, **When** the list is retrieved, **Then** all items include the `status` and `numberOfSeasons` fields (null where not applicable).

---

### User Story 3 — Profile Picture Upload (Priority: P2)

As an authenticated user, I want to upload a custom profile picture via the API so that it replaces my auth provider default picture across the application.

The `POST /api/v1/users/profile-picture` endpoint accepts a `multipart/form-data` request with a single `file` field. The server validates the file type (JPEG, PNG, or WebP) and size (≤ 2 MB), saves it to `wwwroot/uploads/profile-pictures/{userId}.{ext}`, sets `User.ProfilePicturePath` to the relative URL, and returns the updated `UserDto` including the new `profilePicturePath`.

**Why this priority**: Profile picture upload is a user-facing personalization feature. It requires both a new DB migration (to add `ProfilePicturePath` to `User`) and new endpoint logic, making it more work than the DTO extensions.

**Independent Test**: Authenticate as a user, call `POST /api/v1/users/profile-picture` with a valid JPEG file, and verify the response contains the updated `UserDto` with a non-null `profilePicturePath`. Then call `GET /api/v1/auth/me` and confirm the same path appears.

**Acceptance Scenarios**:

1. **Given** an authenticated user submits `POST /api/v1/users/profile-picture` with a valid JPEG file under 2 MB, **When** the request is processed, **Then** the file is stored at `wwwroot/uploads/profile-pictures/{userId}.jpg`, `User.ProfilePicturePath` is set to `/uploads/profile-pictures/{userId}.jpg`, and the response returns 200 OK with the updated `UserDto`.
2. **Given** an authenticated user submits a valid PNG file, **When** the request is processed, **Then** the file is stored and returned correctly with `.png` extension.
3. **Given** an authenticated user submits a valid WebP file, **When** the request is processed, **Then** the file is stored and returned correctly with `.webp` extension.
4. **Given** an authenticated user submits a file with an unsupported type (e.g., `.gif`, `.bmp`, `.pdf`), **When** the request is validated, **Then** the response is 400 Bad Request with a descriptive validation error; no file is stored.
5. **Given** an authenticated user submits a valid image file that exceeds 2 MB, **When** the request is validated, **Then** the response is 400 Bad Request with a size error; no file is stored.
6. **Given** an authenticated user already has a custom profile picture, **When** they upload a new file, **Then** the old file is overwritten (same path since the file name is `{userId}.{ext}`) and `ProfilePicturePath` is updated.
7. **Given** an unauthenticated request to `POST /api/v1/users/profile-picture`, **When** the request is received, **Then** the response is 401 Unauthorized.

---

### User Story 4 — Profile Picture Delete (Priority: P2)

As an authenticated user, I want to delete my custom profile picture via the API so that my profile reverts to the auth provider default picture.

The `DELETE /api/v1/users/profile-picture` endpoint resolves the current user's ID from claims, deletes the stored file from the filesystem, sets `User.ProfilePicturePath = null`, and returns the updated `UserDto` with `profilePicturePath: null`. If no custom picture exists, the endpoint returns 404 Not Found.

**Why this priority**: Delete complements upload as a pair. Users need the ability to revert to their auth provider picture.

**Independent Test**: Authenticate as a user who has a custom profile picture, call `DELETE /api/v1/users/profile-picture`, and verify the response returns `UserDto` with `profilePicturePath: null`. Confirm the file no longer exists on the filesystem.

**Acceptance Scenarios**:

1. **Given** an authenticated user with a custom profile picture, **When** they call `DELETE /api/v1/users/profile-picture`, **Then** the file is deleted from the filesystem, `User.ProfilePicturePath` is set to null, and the response returns 200 OK with the updated `UserDto` (`profilePicturePath: null`).
2. **Given** an authenticated user with no custom profile picture (never uploaded, or already deleted), **When** they call `DELETE /api/v1/users/profile-picture`, **Then** the response is 404 Not Found.
3. **Given** an unauthenticated request to `DELETE /api/v1/users/profile-picture`, **When** the request is received, **Then** the response is 401 Unauthorized.
4. **Given** a user whose `ProfilePicturePath` database record points to a file that no longer exists on the filesystem, **When** they call delete, **Then** the database record is cleared (set to null) and the response is 200 OK — no filesystem error is propagated.

---

### User Story 5 — Profile Picture Path in Auth Responses (Priority: P2)

As an authenticated user, I want `GET /api/v1/auth/me` and `POST /api/v1/auth/sync` to return my current `profilePicturePath` so the frontend can display the correct picture without an additional API call.

The `UserDto` returned by both auth endpoints now includes the `profilePicturePath` field. When no custom picture has been uploaded, the field is null and the frontend falls back to the auth provider picture.

**Why this priority**: This is a supporting change for the profile picture feature. Without it, the frontend cannot determine on login/sync whether a custom picture exists.

**Acceptance Scenarios**:

1. **Given** a user with a custom profile picture, **When** the client calls `GET /api/v1/auth/me`, **Then** the response includes `"profilePicturePath": "/uploads/profile-pictures/{userId}.{ext}"`.
2. **Given** a user without a custom profile picture, **When** the client calls `GET /api/v1/auth/me`, **Then** the response includes `"profilePicturePath": null`.
3. **Given** a user with a custom profile picture, **When** the client calls `POST /api/v1/auth/sync`, **Then** the response `UserDto` includes the current `profilePicturePath`.
4. **Given** a user without a custom profile picture, **When** the client calls `POST /api/v1/auth/sync`, **Then** `profilePicturePath` is null in the response.

---

### Edge Cases

- What happens if the uploaded file's MIME type is mismatched (the extension says `.jpg` but the content is a PNG binary)? The validator inspects the file's magic bytes (first few bytes of the stream) in addition to the declared content type, ensuring only valid images of the declared type are accepted.
- What happens if the filesystem is unavailable or lacks write permissions when storing a profile picture? The upload handler returns a 500 Internal Server Error (infrastructure failure, not a domain validation error) and the database record is not updated.
- What happens if `User.ProfilePicturePath` references a file that was deleted outside the API (e.g., manual deletion from the server)? The GET endpoints (`/auth/me`, `/auth/sync`) return the stored path as-is; the path may point to a missing file. The frontend handles broken image URLs gracefully. On next DELETE call, the DB record is cleared and no error is raised.
- What happens when the `language` field in the scan request contains an unrecognized locale code (e.g., `"zz"`)? The value is passed through to TMDB as-is; TMDB will either ignore it or return results in its default language. No API-level validation is applied to the language code value.
- What happens when a media list endpoint returns many items and both `status` and `numberOfSeasons` are null for films? The fields are always present in the serialized JSON as `null` — no conditional omission — ensuring a stable frontend contract.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST add an optional `language` field (nullable string) to `StartScanRequest` (request body DTO) and propagate it into `StartScanCommand` (MediatR command).
- **FR-002**: System MUST forward the `language` value from `StartScanCommand` through to `ITmdbClient` — all TMDB requests within that scan (search, detail lookup, enrichment calls) MUST include the `language` parameter. When null or omitted, the existing query behavior is unchanged.
- **FR-003**: System MUST add `status` (string?, nullable) and `numberOfSeasons` (int?, nullable) to **both** `MediaDto` and `MediaListItemDto`, mapped from the existing `Media.Status` and `Media.NumberOfSeasons` entity fields.
- **FR-004**: The `status` and `numberOfSeasons` fields MUST be present in all endpoints that return `MediaDto` or `MediaListItemDto`, including `GET /api/v1/media/{id}` and all media list/collection endpoints.
- **FR-005**: System MUST add `profilePicturePath` (string?, nullable) to `UserDto`, mapped from `User.ProfilePicturePath`.
- **FR-006**: System MUST add a nullable `ProfilePicturePath` string property to the `User` domain entity and create an EF Core migration to add the corresponding column.
- **FR-007**: System MUST expose `profilePicturePath` in the responses of `GET /api/v1/auth/me` and `POST /api/v1/auth/sync`.
- **FR-008**: System MUST provide a `POST /api/v1/users/profile-picture` endpoint that accepts `multipart/form-data` with a `file` field.
- **FR-009**: System MUST validate the uploaded file's type (JPEG, PNG, or WebP) and size (≤ 2 MB) in a FluentValidation validator. Invalid files MUST return 400 Bad Request with a structured validation error.
- **FR-010**: System MUST save the uploaded file to `wwwroot/uploads/profile-pictures/{userId}.{ext}` on the server filesystem. If the user previously had a profile picture with a **different extension**, the handler MUST delete the old file before saving the new one (read `User.ProfilePicturePath` before overwriting).
- **FR-011**: System MUST set `User.ProfilePicturePath` to the API route `/api/v1/users/profile-picture/{userId}.{ext}` after a successful upload and persist the change via EF Core.
- **FR-012**: The `POST /api/v1/users/profile-picture` endpoint MUST return 200 OK with the updated `UserDto` (including the new `profilePicturePath`) wrapped in the standard `ApiResponse<T>` envelope.
- **FR-013**: System MUST expose a `GET /api/v1/users/profile-picture/{fileName}` endpoint that streams the requested file from `wwwroot/uploads/profile-pictures/` with the correct `Content-Type` header. This endpoint replaces static file middleware for profile picture serving. **No `app.UseStaticFiles()` addition is required.**
- **FR-014**: System MUST provide a `DELETE /api/v1/users/profile-picture` endpoint that deletes the stored file and sets `User.ProfilePicturePath = null`.
- **FR-015**: The `DELETE /api/v1/users/profile-picture` endpoint MUST return 404 Not Found if the user has no custom profile picture (`ProfilePicturePath` is null).
- **FR-016**: The `DELETE /api/v1/users/profile-picture` endpoint MUST return 200 OK with the updated `UserDto` (`profilePicturePath: null`) on success.
- **FR-017**: Both profile picture endpoints MUST resolve the acting user's identity from `ICurrentUserService.OktaId` (JWT `sub` claim). The **command handler** performs the internal DB lookup (`FirstOrDefaultAsync(u => u.OktaId == ...)`) — the controller does NOT query the DB. Authorization: authenticated user only, not AdminOnly.
- **FR-018**: Both profile picture endpoints MUST return 401 Unauthorized for unauthenticated requests.
- **FR-019**: All new and modified endpoints MUST wrap responses in the standard `ApiResponse<T>` envelope.
- **FR-020**: All new commands and queries MUST follow the MediatR CQRS pattern with corresponding handlers.
- **FR-021**: All new request DTOs MUST have FluentValidation validators wired into the MediatR pipeline.
- **FR-022**: All new handlers MUST return `Result<T>` and never throw exceptions for expected failure cases.

### Key Entities *(changed or new only)*

- **StartScanRequest (modified DTO)**: The existing request body DTO for `POST /api/v1/admin/scan`. Extended with: `Language` (string?, nullable). No other fields changed.
- **StartScanCommand (modified MediatR command)**: The existing MediatR command dispatched from `POST /api/v1/admin/scan`. Extended with: `Language` (string?, nullable, passed through from the request).
- **MediaDto (modified DTO)**: The existing DTO returned by media detail endpoints. Extended with: `Status` (string?, e.g. `"Returning Series"`, `"Ended"`, `"Released"`, null) and `NumberOfSeasons` (int?, null for films or un-enriched media).
- **MediaListItemDto (modified DTO)**: The existing DTO returned by media list/collection endpoints. Extended with the same `Status` and `NumberOfSeasons` fields as `MediaDto`, so collection cards can display production status badges without a detail fetch.
- **UserDto (modified DTO)**: The existing DTO returned by auth endpoints. Extended with: `ProfilePicturePath` (string?, null when no custom picture has been uploaded).
- **User (modified entity)**: The existing domain entity for application users. Extended with: `ProfilePicturePath` (string?, nullable). Requires a new EF Core migration (`AddProfilePicturePathToUser` or similar).
- **UploadProfilePictureCommand (new MediatR command)**: Handles the profile picture upload. Parameters: `OktaId` (string), `FileStream` (Stream), `FileName` (string), `ContentType` (string), `FileSize` (long). Handler: resolves `User` by `OktaId`, deletes old file if extension changes, saves new file, updates `User.ProfilePicturePath`, returns `Result<UserDto>`.
- **DeleteProfilePictureCommand (new MediatR command)**: Handles the profile picture deletion. Parameters: `OktaId` (string). Handler: resolves `User` by `OktaId`, deletes filesystem file (if exists), sets `ProfilePicturePath = null`, returns `Result<UserDto>`. Returns `Result.NotFound` if no custom picture exists.
- **ITmdbClient (modified interface)**: All relevant methods (search, detail lookup, enrichment) updated to accept an optional `language` (string?) parameter. The HTTP Refit/typed client implementation passes `language` as a query parameter when non-null.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Media scan initiated with a `language` parameter passes that language value to 100% of TMDB API calls within that scan — verified by tracing or integration test asserting TMDB request URLs contain the correct `language=` query parameter.
- **SC-002**: `GET /api/v1/media/{id}` returns `status` and `numberOfSeasons` populated from the database for 100% of enriched media entries, with both fields null for un-enriched entries.
- **SC-003**: `GET /api/v1/auth/me` and `POST /api/v1/auth/sync` return `profilePicturePath` for 100% of requests — non-null after a successful upload, null when no custom picture exists.
- **SC-004**: Profile picture upload completes within 3 seconds for files up to 2 MB on standard server hardware.
- **SC-005**: 100% of invalid upload attempts (unsupported type or oversized file) are rejected with a 400 Bad Request response — zero files stored for invalid uploads.
- **SC-006**: Uploaded profile pictures are accessible via their returned relative URL immediately after upload — zero latency gap between upload completion and file availability via the static file endpoint.
- **SC-007**: Profile picture delete clears `User.ProfilePicturePath` and removes the file from the filesystem within 1 second.
- **SC-008**: All new and modified endpoints return responses within the `ApiResponse<T>` envelope — zero unhandled exceptions in production for expected error cases (400, 401, 404).
- **SC-009**: The EF Core migration adds the `ProfilePicturePath` column to the `Users` table without data loss to any existing row.
- **SC-010**: All changes are non-breaking — existing clients that do not read `status`, `numberOfSeasons`, or `profilePicturePath` fields continue to function without modification.

## Assumptions

- The `Media.Status` and `Media.NumberOfSeasons` properties already exist on the `Media` domain entity and are populated during the TMDB enrichment pipeline (covered by spec `004-admin-dashboard-api`). No new database migration is required for these two fields.
- The existing `MediaDto` **and** `MediaListItemDto` mappings (AutoMapper profiles or manual projections) can be extended by adding the two new scalar properties — no structural changes required.
- The existing `UserDto` and its mapping can be extended by adding `ProfilePicturePath` — no structural changes required.
- The `User` entity migration (`AddProfilePicturePathToUser`) uses a nullable `nvarchar(500)` column, consistent with path length constraints and existing nullable string conventions in the project.
- The `wwwroot/uploads/profile-pictures/` directory is created by the application at startup if it does not exist. No manual server setup is required.
- Uploaded profile pictures are accessible via `GET /api/v1/users/profile-picture/{fileName}` — a dedicated streaming endpoint in `UsersController`. `app.UseStaticFiles()` is **not** added to `Program.cs`.
- One active picture per user at a time. File name strategy: `{userId}.{ext}`. If a user switches format (e.g., JPEG → PNG), the handler reads `User.ProfilePicturePath`, deletes the old file, then saves the new one. The `DELETE` endpoint deletes the file at `User.ProfilePicturePath` if it exists on disk.
- The current user's `OktaId` is read from `ICurrentUserService.OktaId` in the controller. Both `UploadProfilePictureCommand` and `DeleteProfilePictureCommand` accept `OktaId` (string) and resolve the `User` entity internally via `FirstOrDefaultAsync(u => u.OktaId == ...)` — consistent with `SyncUserCommandHandler`.
- `ITmdbClient` is updated so all relevant methods accept an optional `language` (string?) parameter. Every TMDB HTTP call made during a scan (search, detail, enrichment) includes `language` when non-null. This is a non-breaking change — callers that don't pass `language` continue to work as before.
- The `StartScanCommand` and its handler are in the Application layer. The scan handler passes `Language` down to all `ITmdbClient` calls it makes directly or via subordinate services.
- All new endpoints are versioned under `/api/v1/` consistent with existing routes.
- The `UsersController` (new or existing) hosts the profile picture endpoints. If a `UsersController` already exists, the new endpoints are added to it; otherwise, a new one is created.
- File type validation inspects the declared `Content-Type` header and filename extension. Magic-byte inspection is a testing quality improvement, not a hard requirement for this iteration.
- No CDN or external object storage is used for profile pictures in this iteration. Server filesystem storage under `wwwroot` is the accepted approach.
- The `DELETE` command clears `ProfilePicturePath` in the database even if the filesystem file has already been deleted externally, ensuring the database state is always consistent.


# Checklist: Admin Authorization (SC-008, FR-022)

**Purpose**: Validate that **every** scanner administration endpoint enforces the AdminOnly policy, rate limiting, API versioning, and produces no DB side-effects on unauthorized requests.
**Scope**: All controllers added by this feature: `AdminLibraryRootsController`, `AdminScanController`, `AdminReviewController`. Endpoints enumerated in `contracts/library-roots.md`, `contracts/scan.md`, `contracts/review-items.md`.
**How to use**: Tick after running the authorization test suite (T111). Every row of the matrix must pass; any 200 from anonymous/non-admin is a release blocker.

## Per-Controller Conventions

- [ ] CHK001 - `AdminLibraryRootsController` is decorated with `[Authorize(Policy = "AdminOnly")]` at class level
- [ ] CHK002 - `AdminScanController` is decorated with `[Authorize(Policy = "AdminOnly")]` at class level
- [ ] CHK003 - `AdminReviewController` is decorated with `[Authorize(Policy = "AdminOnly")]` at class level
- [ ] CHK004 - All three controllers carry `[ApiVersion("1.0")]`
- [ ] CHK005 - All three controllers carry `[EnableRateLimiting("fixed")]`
- [ ] CHK006 - All three controllers route under `/api/v1/admin/` (verify `[Route]` attribute)
- [ ] CHK007 - All endpoints return `ApiResponse<T>` (success and error) — no raw `IActionResult` returning bare DTOs

## Authorization Matrix — Library Roots (`contracts/library-roots.md`)

For each row: anonymous → **401**, authenticated non-admin → **403**, admin → **2xx**.

- [ ] CHK008 - `GET    /api/v1/admin/library-roots`
- [ ] CHK009 - `POST   /api/v1/admin/library-roots`
- [ ] CHK010 - `GET    /api/v1/admin/library-roots/{id}`
- [ ] CHK011 - `PUT    /api/v1/admin/library-roots/{id}`
- [ ] CHK012 - `DELETE /api/v1/admin/library-roots/{id}`

## Authorization Matrix — Scan (`contracts/scan.md`)

- [ ] CHK013 - `POST /api/v1/admin/scans` (start scan)
- [ ] CHK014 - `GET  /api/v1/admin/scans` (list)
- [ ] CHK015 - `GET  /api/v1/admin/scans/{id}` (detail, includeReview variant covered)
- [ ] CHK016 - `POST /api/v1/admin/scans/{id}/cancel`
- [ ] CHK017 - Any SSE/streaming progress endpoint defined in contracts/scan.md
- [ ] CHK018 - Any `GET /api/v1/admin/scans/{id}/errors` or equivalent diagnostic endpoint

## Authorization Matrix — Review (`contracts/review-items.md`)

- [ ] CHK019 - `GET   /api/v1/admin/review-items` (list with filters)
- [ ] CHK020 - `GET   /api/v1/admin/review-items/{id}`
- [ ] CHK021 - `POST  /api/v1/admin/review-items/{id}/resolve` (or PUT — use the verb in the contract)
- [ ] CHK022 - `POST  /api/v1/admin/review-items/{id}/dismiss`
- [ ] CHK023 - Any bulk endpoint (`POST /api/v1/admin/review-items/bulk-resolve` etc.) defined in the contract

## No-Side-Effect Guarantee (T111)

- [ ] CHK024 - Anonymous `POST /api/v1/admin/scans` returns 401 AND creates zero `ScanRun` rows (verify with DB snapshot before/after)
- [ ] CHK025 - Non-admin `POST /api/v1/admin/library-roots` returns 403 AND creates zero `LibraryRoot` rows
- [ ] CHK026 - Non-admin `POST /api/v1/admin/review-items/{id}/resolve` returns 403 AND `ReviewItem.Status` is unchanged
- [ ] CHK027 - Non-admin `DELETE /api/v1/admin/library-roots/{id}` returns 403 AND row is not deleted (and not soft-deleted)
- [ ] CHK028 - 401/403 responses use the standard `ApiResponse<T>` error envelope, not the default ASP.NET problem-details body

## Rate Limiting

- [ ] CHK029 - Burst of N+1 requests within the window from one admin returns 429 on the (N+1)th (N from `fixed` policy config)
- [ ] CHK030 - 429 response uses `ApiResponse<T>` envelope and includes `Retry-After` header
- [ ] CHK031 - Rate limit is keyed per-user (not global), confirmed by a two-admin parallel test

## Cross-Cutting

- [ ] CHK032 - Authorization tests in T111 cover **every** endpoint listed in CHK008–CHK023 (no gaps)
- [ ] CHK033 - Swagger/OpenAPI document shows the security requirement on every admin endpoint
- [ ] CHK034 - No admin endpoint is reachable via an unversioned path (`/api/admin/...` without `v1`) — returns 404 or routes to versioned only


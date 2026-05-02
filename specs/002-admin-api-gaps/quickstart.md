# Quickstart: Admin Dashboard Backend API Gaps

**Feature**: 002-admin-api-gaps  
**Date**: 2025-07-17

## Prerequisites

- .NET 8 SDK
- Docker (for PostgreSQL via `docker-compose.yml`)
- An IDE with C# support (Rider, VS Code with C# Dev Kit)

## Setup

```bash
# Start PostgreSQL
docker compose up -d

# Restore and build
dotnet restore
dotnet build

# Run migrations
dotnet ef database update --project MediaHandler.Infrastructure --startup-project MediaHandler.API

# Run the API
dotnet run --project MediaHandler.API
```

## What This Feature Adds

### 1. Toggle Library Root Enabled (US1)

**Files to create/modify**:
- `MediaHandler.Application/Features/LibraryRoots/Commands/ToggleLibraryRootEnabled/ToggleLibraryRootEnabledCommand.cs` — Command record, FluentValidation validator, MediatR handler
- `MediaHandler.API/Contracts/Admin/LibraryRootRequests.cs` — Add `ToggleLibraryRootEnabledRequest` record
- `MediaHandler.API/Controllers/AdminLibraryRootsController.cs` — Add `ToggleEnabled` action method

**Key implementation notes**:
- Follow the `RemoveLibraryRootCommand` handler pattern for the scan-in-progress guard
- Return `LibraryRootDto` in the response (same DTO as list/create)
- The `IsEnabled` field already exists on `LibraryRoot` — no migration needed

### 2. Scan History Pagination (US2)

**Files to create/modify**:
- `MediaHandler.Application/Features/Scan/Queries/ListScanHistory/ListScanHistoryQuery.cs` — Query record, validator, handler
- `MediaHandler.API/Controllers/AdminScanController.cs` — Add `ListHistory` action method

**Key implementation notes**:
- Follow the `ListReviewItemsQuery` pattern for pagination
- Return `PagedResult<ScanRunDto>` → map to `ScanRunSummaryResponse` in the controller
- Order by `StartedAt DESC`, use `AsNoTracking()`, cap `pageSize` at 100
- Route: `GET /api/v1/admin/scan` (same base as existing scan endpoints)

### 3. Reopen Review Items (US3)

**Files to modify**:
- `MediaHandler.Domain/Enums/ReviewResolutionAction.cs` — Add `Reopen` value
- `MediaHandler.Application/Features/Review/Commands/ResolveReviewItem/ResolveReviewItemCommand.cs` — Add `Reopen` case to handler; update validator message
- `MediaHandler.API/Controllers/AdminReviewController.cs` — Add `REVIEW_ALREADY_OPEN` error mapping

**Key implementation notes**:
- Reopen requires item status to be `Resolved` or `Dismissed` (invert the existing `Open`-only guard)
- Clear: `ResolvedTmdbId`, `ResolvedKind`, `ResolvedAt`, `ResolvedBy` → all set to `null`
- Set `Status = ReviewStatus.Open`
- The existing resolve endpoint (`POST /review-items/{id}/resolve`) handles this — no new route

## Testing

```bash
# Run unit tests
dotnet test MediaHandler.Tests

# Run integration tests (requires Docker)
dotnet test MediaHandler.IntegrationTests

# Format check
dotnet format --verify-no-changes
```

**Test files to create**:
- `MediaHandler.Tests/Features/LibraryRoots/ToggleLibraryRootEnabledCommandHandlerTests.cs`
- `MediaHandler.Tests/Features/Scan/ListScanHistoryQueryHandlerTests.cs`
- `MediaHandler.Tests/Features/Review/ResolveReviewItemReopenTests.cs`

**Minimum test cases per handler**:
- Success path (happy path)
- Not-found path (404)
- Conflict path (409 — scan in progress for US1, already open for US3)
- Pagination edge cases for US2 (empty results, page beyond total)

## Verification

After implementation, verify these acceptance criteria:

1. `PUT /api/v1/admin/library-roots/{id}/enabled` with `{"isEnabled": false}` → 200 with updated root
2. `PUT /api/v1/admin/library-roots/{id}/enabled` during active scan → 409
3. `GET /api/v1/admin/scan?page=1&pageSize=20` → 200 with paginated scan runs
4. `GET /api/v1/admin/scan?pageSize=200` → pageSize capped to 100
5. `POST /api/v1/admin/review-items/{id}/resolve` with `{"action": "Reopen"}` on Resolved item → 200 with Open item
6. `POST /api/v1/admin/review-items/{id}/resolve` with `{"action": "Reopen"}` on Open item → 409


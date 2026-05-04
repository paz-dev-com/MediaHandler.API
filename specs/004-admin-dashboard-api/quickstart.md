# Quickstart: Admin Dashboard API Endpoints

**Feature**: 004-admin-dashboard-api  
**Branch**: `004-admin-dashboard-api`

## Prerequisites

- .NET 10 SDK
- Docker (for SQL Server via Testcontainers in integration tests)
- SQL Server running locally or via `docker-compose`

## Getting Started

```bash
# Clone and checkout feature branch
git checkout feature/004-admin-dashboard-api

# Restore dependencies
dotnet restore MediaHandler.slnx

# Apply database migration (after entity changes)
dotnet ef migrations add AddDashboardApiFields \
  --project MediaHandler.Infrastructure \
  --startup-project MediaHandler.API

dotnet ef database update \
  --project MediaHandler.Infrastructure \
  --startup-project MediaHandler.API

# Run the API
dotnet run --project MediaHandler.API

# Run unit tests
dotnet test MediaHandler.Tests

# Run integration tests (requires Docker)
dotnet test MediaHandler.IntegrationTests
```

## Key Implementation Order

1. **Entity changes first** (Story 8): Enhance `ScanItemDecision`, add `EnrichmentRun`, add `Media` fields → migration
2. **Scan decisions browser** (Story 1): `ListScanDecisions` query + controller endpoint
3. **TMDB reassignment** (Story 2): `ReassignTmdb` command + controller endpoint
4. **TV show groups** (Stories 3-4): `ListTvShowGroups` query + `AssignTvGroup` command
5. **Enrichment** (Story 5): `EnrichmentCoordinator` + start/status endpoints
6. **File rename** (Stories 6-7): `FileRenameService` + single/batch rename endpoints

## Architecture Patterns to Follow

| Pattern | Reference File |
|---------|---------------|
| Controller structure | `AdminScanController.cs`, `AdminReviewController.cs`, `AdminLibraryRootsController.cs` |
| MediatR query (paginated) | `ListScanHistoryQuery.cs` |
| MediatR command | `ResolveReviewItemCommand.cs` |
| Result pattern | `Result.cs`, `Result<T>` |
| ApiResponse envelope | `ApiResponse.cs` |
| FluentValidation | `ListScanHistoryQueryValidator` |
| Background coordinator | `ScanRunCoordinator.cs` |
| Entity configuration | `ScanItemDecisionConfiguration.cs` |
| TMDB service | `ITmdbService.cs`, `TmdbService.cs` |

## Verification Checklist

- [ ] New migration applies cleanly (`dotnet ef database update`)
- [ ] `GET /api/v1/admin/scan/{scanId}/decisions` returns paginated results with filters
- [ ] `PUT /api/v1/admin/scan-decisions/{id}/reassign` updates decision + linked media file
- [ ] `GET /api/v1/admin/scan-decisions/tv-groups?scanId=...` returns computed groups
- [ ] `PUT /api/v1/admin/tv-groups/{groupId}/assign?scanId=...` propagates to all episodes
- [ ] `POST /api/v1/admin/enrichment/start` starts background enrichment (409 if already running)
- [ ] `GET /api/v1/admin/enrichment/status` returns progress during and after enrichment
- [ ] `POST /api/v1/admin/files/{id}/rename?preview=true` returns proposed name
- [ ] `POST /api/v1/admin/files/{id}/rename?preview=false` renames file + updates DB
- [ ] `POST /api/v1/admin/tv-groups/{groupId}/rename?preview=true` returns all proposed names
- [ ] All endpoints return 403 for non-admin users
- [ ] All unit tests pass (`dotnet test MediaHandler.Tests`)
- [ ] All integration tests pass (`dotnet test MediaHandler.IntegrationTests`)
- [ ] `dotnet format --verify-no-changes` passes


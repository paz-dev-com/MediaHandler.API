# Research: Admin Dashboard Backend API Gaps

**Feature**: 002-admin-api-gaps  
**Date**: 2025-07-17  
**Status**: Complete — all items resolved

## R1: Toggle Library Root Enabled — Concurrency Guard Pattern

**Decision**: Reuse the existing scan-in-progress check pattern from `RemoveLibraryRootCommandHandler` — query `ScanRuns` for `Status == Running` or `Status == Pending`, then check `LibraryRootIdsJson` for the target root id.

**Rationale**: The `RemoveLibraryRootCommand` handler already implements this exact guard (lines 32–41 of `RemoveLibraryRootCommand.cs`). Using the same pattern ensures consistency and avoids introducing new abstraction for a simple check. The guard checks `ScanStatus.Running` and inspects `LibraryRootIdsJson` (which contains `"[]"` when all roots are included).

**Alternatives considered**:
- **Shared `IScanGuardService`**: Extracting the check into a reusable service was considered but rejected — two usages (Remove + Toggle) don't justify the abstraction overhead. Can be refactored later if more commands need the same guard.
- **Optimistic concurrency (EF concurrency token)**: Would detect mid-update conflicts but doesn't prevent the specific "scan is running" business rule.

## R2: Scan History Pagination — Query Pattern

**Decision**: Create a new `ListScanHistoryQuery` returning `PagedResult<ScanRunDto>`. Use `AsNoTracking()`, order by `StartedAt DESC`, apply `Skip`/`Take` server-side pagination, and cap `pageSize` at 100.

**Rationale**: This follows the exact same pattern as `ListLibraryRootsQuery` and `ListReviewItemsQuery`, both of which return `PagedResult<T>` with `AsNoTracking()` and `Skip`/`Take`. The `ScanRun` entity has a `StartedAt` column already indexed DESC (per entity comment), making the ORDER BY efficient.

**Alternatives considered**:
- **Cursor-based pagination**: More efficient for large datasets, but the existing codebase uses offset-based pagination consistently. Mixing styles would confuse consumers.
- **Reusing `GetScanRunQuery`**: That returns a single detail view with optional review items — a different DTO shape. A separate list query is cleaner.

## R3: Reopen Action — Domain Enum Extension

**Decision**: Add `Reopen = 3` to the `ReviewResolutionAction` enum. Handle the `Reopen` case in the existing `ResolveReviewItemCommandHandler.Handle()` method by adding a new case arm. The handler sets `Status = Open` and clears `ResolvedTmdbId`, `ResolvedKind`, `ResolvedAt`, `ResolvedBy`.

**Rationale**: The existing handler already uses a `switch` on `ReviewResolutionAction` with `Assign`, `Dismiss`, `Delete` cases. Adding `Reopen` as a fourth case is the most cohesive approach. The validator must be updated to accept the new enum value, and a **different** status guard is needed: Reopen requires the item to be `Resolved` or `Dismissed` (not `Open`), which is the inverse of the existing guard.

**Alternatives considered**:
- **Separate `ReopenReviewItemCommand`**: Would require a new command, handler, and validator — tripling the boilerplate for a single field-clearing operation. The existing resolve endpoint and its handler already encapsulate all review-item state transitions.
- **PATCH endpoint on ReviewItem**: Over-engineers for a single status reversion. The POST resolve endpoint already handles multiple actions.

## R4: Controller Integration — Error Handling Patterns

**Decision**: Follow existing error-to-HTTP-status patterns in each controller:
- `AdminLibraryRootsController`: Match `RemoveLibraryRoot` error handling — `NOT_FOUND` → 404, `SCAN_IN_PROGRESS` → 409.
- `AdminScanController`: Simple 400 for validation errors; no special error cases for list queries.
- `AdminReviewController`: Add `REVIEW_ALREADY_OPEN` → 409 (parallel to existing `REVIEW_ALREADY_RESOLVED` → 409).

**Rationale**: Each controller already has established error string matching patterns (e.g., `error.Contains("SCAN_IN_PROGRESS")`). Keeping the same approach avoids introducing typed error objects mid-feature.

**Alternatives considered**:
- **Typed error results (discriminated unions)**: Cleaner but a cross-cutting refactor outside this feature's scope.

## R5: Request DTO — Toggle Endpoint Body

**Decision**: Use a simple `record ToggleLibraryRootEnabledRequest(bool IsEnabled)` as the request body. The endpoint is `PUT /api/v1/admin/library-roots/{id}/enabled` — an explicit set rather than a toggle, matching the spec's idempotency requirement.

**Rationale**: The spec explicitly states "each request sets the explicit value, not toggling relative to current state" (Edge Cases section). A body with `isEnabled: true/false` makes the operation idempotent and clear.

**Alternatives considered**:
- **No body (POST toggle)**: Would be a stateful toggle requiring knowledge of current state — violates idempotency requirement.
- **PATCH on root entity**: Over-broad — exposes more fields than needed for this specific operation.

## R6: Database Schema Impact

**Decision**: No database migrations required. All three features work with existing schema:
- `LibraryRoot.IsEnabled` already exists as a column.
- `ScanRun` table already has all needed columns with `StartedAt` index.
- `ReviewItem` resolution fields already exist and are nullable.
- `ReviewResolutionAction` is a C# enum stored as string — PostgreSQL `text` column accepts any value.

**Rationale**: Confirmed by examining entity classes and the spec's Assumptions section. The `IsEnabled` property (line 30 of `LibraryRoot.cs`), all `ScanRun` counters, and all `ReviewItem` resolution fields are already in the schema.

**Alternatives considered**: None — no schema changes needed.


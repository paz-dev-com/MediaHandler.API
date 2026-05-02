# Feature Specification: Admin Dashboard Backend API Gaps

**Feature Branch**: `002-admin-api-gaps`  
**Created**: 2025-07-17  
**Status**: Draft  
**Input**: User description: "Three backend API gaps needed by the Angular admin dashboard: PUT library-roots/{id}/enabled (toggle enabled), GET scan history with pagination, and Reopen action for review items."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle Library Root Enabled Status (Priority: P1)

An administrator managing NAS library roots needs to enable or disable individual library roots without deleting them. When a root is disabled, the scanner skips it during scan runs, but all associated media files and configuration remain intact. The admin toggles the enabled status from the Library Roots management page in the dashboard.

**Why this priority**: This is the simplest of the three gaps and directly blocks the Library Root Management section of the admin dashboard (US2 in the frontend plan). Without this endpoint, admins cannot temporarily pause scanning of a root — they must delete and re-add it, which is destructive.

**Independent Test**: Can be fully tested by calling the toggle endpoint on an existing library root and verifying the `IsEnabled` field changes. Delivers immediate value by enabling non-destructive root management.

**Acceptance Scenarios**:

1. **Given** a library root exists with `IsEnabled = true`, **When** an admin sends `PUT /api/v1/admin/library-roots/{id}/enabled` with `{ "isEnabled": false }`, **Then** the root's `IsEnabled` field is updated to `false` and the response contains the updated library root with status 200.
2. **Given** a library root exists with `IsEnabled = false`, **When** an admin sends `PUT /api/v1/admin/library-roots/{id}/enabled` with `{ "isEnabled": true }`, **Then** the root's `IsEnabled` field is updated to `true` and the response contains the updated library root with status 200.
3. **Given** the provided library root id does not exist, **When** an admin sends the toggle request, **Then** the system returns 404 with error code `NOT_FOUND`.
4. **Given** a scan is currently running that includes this library root, **When** an admin attempts to disable it, **Then** the system returns 409 with error code `SCAN_IN_PROGRESS` to prevent mid-scan configuration changes.
5. **Given** a non-admin user, **When** they attempt to call this endpoint, **Then** the system returns 403 Forbidden.

---

### User Story 2 - Browse Scan History with Pagination (Priority: P1)

An administrator monitoring scanner operations needs to view a chronological list of past scan runs to assess scanning health, diagnose failures, and track media library growth over time. The scan history page in the dashboard displays a paginated table of completed (and in-progress) scans with summary statistics.

**Why this priority**: The scan history endpoint is required by the Scanner Operations section of the admin dashboard (US3 in the frontend plan). Without it, admins can only see the currently active scan but cannot review historical scan data, making it impossible to detect trends or diagnose recurring issues.

**Independent Test**: Can be fully tested by creating several scan runs (via existing start-scan endpoint), then paginating through them. Delivers immediate value by providing operational visibility into scan history.

**Acceptance Scenarios**:

1. **Given** multiple scan runs exist in the system, **When** an admin sends `GET /api/v1/admin/scan?page=1&pageSize=20`, **Then** the response contains the first 20 scan run summaries ordered by `StartedAt` descending (most recent first), with pagination metadata.
2. **Given** 50 scan runs exist, **When** an admin requests `page=3&pageSize=20`, **Then** the response contains the last 10 runs (items 41-50) with correct `totalCount=50`, `totalPages=3`.
3. **Given** no scan runs exist, **When** an admin requests the scan history, **Then** the response contains an empty array with `totalCount=0`.
4. **Given** a `pageSize` greater than 100 is requested, **When** the request is processed, **Then** the system caps `pageSize` at 100 to prevent excessive data retrieval.
5. **Given** a non-admin user, **When** they attempt to call this endpoint, **Then** the system returns 403 Forbidden.

---

### User Story 3 - Reopen Resolved or Dismissed Review Items (Priority: P2)

An administrator managing the TMDB review queue needs to reopen a previously resolved or dismissed review item when the original resolution was incorrect. For example, if a file was assigned to the wrong TMDB entry or was prematurely dismissed, the admin can revert it back to Open status for re-evaluation.

**Why this priority**: While not blocking basic dashboard functionality, the Reopen action completes the review item lifecycle and is needed for the Review Queue section (US4 in the frontend plan). Without it, incorrectly resolved items require manual database intervention to fix.

**Independent Test**: Can be fully tested by resolving a review item, then calling the resolve endpoint with `Reopen` action, and verifying the item returns to Open status with resolution fields cleared. Delivers value by enabling full self-service review queue management.

**Acceptance Scenarios**:

1. **Given** a review item exists with status `Resolved` (previously assigned), **When** an admin sends `POST /api/v1/admin/review-items/{id}/resolve` with `{ "action": "Reopen" }`, **Then** the item's status reverts to `Open`, resolution fields (`ResolvedTmdbId`, `ResolvedKind`, `ResolvedAt`, `ResolvedBy`) are cleared, and the response contains the updated item with status 200.
2. **Given** a review item exists with status `Dismissed`, **When** an admin sends the reopen request, **Then** the item's status reverts to `Open`, resolution fields are cleared, and the response contains the updated item with status 200.
3. **Given** a review item is already `Open`, **When** an admin sends the reopen request, **Then** the system returns 409 with error code `REVIEW_ALREADY_OPEN` because the item does not need reopening.
4. **Given** the provided review item id does not exist, **When** an admin sends the reopen request, **Then** the system returns 404 with error code `NOT_FOUND`.
5. **Given** a non-admin user, **When** they attempt to call this endpoint, **Then** the system returns 403 Forbidden.

---

### Edge Cases

- What happens when an admin toggles a library root's enabled status multiple times in rapid succession? The system should handle idempotent updates correctly — each request sets the explicit value, not toggling relative to current state.
- What happens when a scan is started with library roots that are subsequently disabled? The running scan continues with the roots as they were at scan start time; the disabled status only affects future scans.
- What happens when a Reopened review item's underlying file has been physically deleted from the NAS? The item returns to Open status regardless of file presence; the next scan will re-evaluate and may update or dismiss it.
- What happens when the `pageSize` or `page` query parameters contain invalid values (negative, zero, non-numeric)? The system should validate and return 400 Bad Request with clear error messages.
- What happens when a review item was resolved via `Delete` action (underlying MediaFile removed) and is then Reopened? The item returns to Open but the MediaFile remains deleted; the admin can re-assign to a different TMDB entry or dismiss again.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an endpoint to update the `IsEnabled` field of an individual library root by id.
- **FR-002**: System MUST return the updated library root representation after a successful enabled-status toggle.
- **FR-003**: System MUST prevent disabling a library root that is referenced by a currently running scan.
- **FR-004**: System MUST return 404 when the specified library root id does not exist.
- **FR-005**: System MUST provide an endpoint to list scan run summaries with server-side pagination.
- **FR-006**: System MUST return scan runs ordered by start time, most recent first.
- **FR-007**: System MUST include pagination metadata (page, pageSize, totalCount, totalPages) in the scan history response.
- **FR-008**: System MUST cap the maximum page size at 100 for scan history queries.
- **FR-009**: System MUST support a `Reopen` resolution action that reverts a Resolved or Dismissed review item back to Open status.
- **FR-010**: System MUST clear all resolution fields (resolved TMDB id, resolved kind, resolved timestamp, resolved-by user) when a review item is reopened.
- **FR-011**: System MUST reject reopen attempts on items that are already Open.
- **FR-012**: All three endpoints MUST require admin-level authorization (AdminOnly policy).
- **FR-013**: All three endpoints MUST follow the existing API response envelope pattern (`ApiResponse<T>` with `data`, `meta`, `errors`).

### Key Entities *(include if feature involves data)*

- **LibraryRoot**: A configured NAS path monitored by the scanner. Key attributes: Path, Kind (Movie/TvShow), Label, IsEnabled. The existing entity already has the `IsEnabled` field — this feature adds the API endpoint to modify it.
- **ScanRun**: A record of a single scanner execution. Key attributes: Mode (Full/Incremental), Status (Pending/Running/Completed/Cancelled/Failed), StartedAt, FinishedAt, summary counters (TotalDiscovered, Added, Updated, Unchanged, Removed, Excluded, NeedsReview). The existing entity is returned as-is — this feature adds a paginated list endpoint.
- **ReviewItem**: A file flagged for admin attention during scanning. Key attributes: FilePath, Reason, Status (Open/Resolved/Dismissed), parsed metadata, TMDB candidates, resolution fields. The existing entity's resolve flow is extended with a Reopen action.
- **ReviewResolutionAction**: Enum governing resolution actions. Currently has Assign, Dismiss, Delete — this feature adds `Reopen`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Administrators can enable or disable any library root in under 2 seconds from the dashboard without losing any existing configuration or media data.
- **SC-002**: Administrators can browse the complete scan history with pagination, viewing any historical scan run within 3 clicks from the admin dashboard.
- **SC-003**: Administrators can reopen incorrectly resolved review items without requiring direct database access, completing the action in a single operation.
- **SC-004**: All three new capabilities integrate seamlessly with the existing admin dashboard frontend with zero contract mismatches.
- **SC-005**: All operations provide clear, actionable error messages when invalid input or conflicting state is encountered.

## Assumptions

- The existing `LibraryRoot` entity already has the `IsEnabled` property — no database schema changes are needed for that field.
- The existing `ApiResponse<T>` envelope, `ApiResponseMeta` pagination model, and `ApiError` error pattern are reused for all new endpoints.
- The existing `AdminOnly` authorization policy and `fixed` rate limiter apply to all new endpoints, consistent with the other admin controllers.
- The `ReviewResolutionAction` enum requires a new `Reopen` value — this is a domain-level addition with corresponding validator and handler updates.
- The `ReviewStatus` enum does not require changes — `Open` already exists as the target state for reopened items.
- The frontend admin dashboard (Angular) is designed against contractual expectations documented in the frontend plan's `contracts/api-endpoints.md` — these backend endpoints must match those contracts.
- The existing `ScanRunSummaryResponse` DTO is reused for scan history list items to maintain consistency with other scan endpoints.
- Concurrent scan validation for library root toggle uses the same `ScanStatus.Running` / `ScanStatus.Pending` check pattern already established in the codebase.


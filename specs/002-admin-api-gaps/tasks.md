# Tasks: Admin Dashboard Backend API Gaps

**Input**: Design documents from `/specs/002-admin-api-gaps/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/api-endpoints.md, quickstart.md

**Tests**: Included — quickstart.md specifies test files and minimum test cases per handler.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Domain layer**: `MediaHandler.Domain/`
- **Application layer**: `MediaHandler.Application/`
- **API layer**: `MediaHandler.API/`
- **Tests**: `MediaHandler.Tests/`

---

## Phase 1: Setup

**Purpose**: No project initialization needed — this feature adds to an existing codebase with no schema changes.

- [x] T001 Verify existing project builds cleanly by running `dotnet build` from repository root
- [x] T002 [P] Verify existing tests pass by running `dotnet test MediaHandler.Tests`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain-level enum change required by US3 and referenced by existing validator — must land before user story phases.

**⚠️ CRITICAL**: The enum change in T003 affects the existing `ResolveReviewItemCommandValidator` via `IsInEnum()`. It must be done first so existing code continues to compile and validate correctly.

- [x] T003 Add `Reopen` value to the `ReviewResolutionAction` enum in `MediaHandler.Domain/Enums/ReviewResolutionAction.cs`. Add `Reopen` after `Delete` (value = 3). No other enum changes.

**Checkpoint**: Foundation ready — all three user stories can now proceed in parallel.

---

## Phase 3: User Story 1 — Toggle Library Root Enabled Status (Priority: P1) 🎯 MVP

**Goal**: Allow an admin to enable or disable a library root via `PUT /api/v1/admin/library-roots/{id}/enabled` without deleting it. A scan-in-progress guard prevents disabling a root mid-scan.

**Independent Test**: Call the toggle endpoint on an existing library root and verify the `IsEnabled` field changes; attempt to disable during an active scan and verify 409 response.

### Tests for User Story 1

- [x] T004 [P] [US1] Create unit tests for the toggle handler in `MediaHandler.Tests/Features/LibraryRoots/ToggleLibraryRootEnabledCommandHandlerTests.cs`. Cover: success (toggle true→false and false→true), not-found (404), scan-in-progress conflict (409). Follow existing `RemoveLibraryRootCommandHandlerTests` pattern with NSubstitute mocks for `IApplicationDbContext`.

### Implementation for User Story 1

- [x] T005 [P] [US1] Create `ToggleLibraryRootEnabledCommand` record, `ToggleLibraryRootEnabledCommandValidator`, and `ToggleLibraryRootEnabledCommandHandler` in `MediaHandler.Application/Features/LibraryRoots/Commands/ToggleLibraryRootEnabled/ToggleLibraryRootEnabledCommand.cs`. Command takes `Guid Id` and `bool IsEnabled`. Validator: `Id` must not be empty. Handler: load `LibraryRoot` by id (return `NOT_FOUND` failure if missing), check for active scan referencing this root (return `SCAN_IN_PROGRESS` failure if found — reuse pattern from `RemoveLibraryRootCommandHandler`), set `IsEnabled`, save, return `LibraryRootDto`.
- [x] T006 [P] [US1] Add `ToggleLibraryRootEnabledRequest` record (`bool IsEnabled`) in `MediaHandler.API/Contracts/Admin/LibraryRootRequests.cs`.
- [x] T007 [US1] Add `ToggleEnabled` action method to `MediaHandler.API/Controllers/AdminLibraryRootsController.cs`. Route: `[HttpPut("{id:guid}/enabled")]`. Accept `ToggleLibraryRootEnabledRequest` body, map to `ToggleLibraryRootEnabledCommand`, send via MediatR. Map result errors: `NOT_FOUND` → 404, `SCAN_IN_PROGRESS` → 409. Success returns `ApiResponse<LibraryRootDto>` with status 200. Add `[ProducesResponseType]` attributes for 200, 400, 404, 409.

**Checkpoint**: US1 is fully functional — toggle endpoint works with conflict guard and proper error responses.

---

## Phase 4: User Story 2 — Browse Scan History with Pagination (Priority: P1)

**Goal**: Allow an admin to view paginated scan run history via `GET /api/v1/admin/scan?page={page}&pageSize={pageSize}`, ordered by `StartedAt` descending.

**Independent Test**: Create scan runs (via existing start-scan endpoint or seed data), then paginate through them verifying correct ordering, counts, and page metadata.

### Tests for User Story 2

- [x] T008 [P] [US2] Create unit tests for the scan history query handler in `MediaHandler.Tests/Features/Scan/ListScanHistoryQueryHandlerTests.cs`. Cover: success with paginated results (verify ordering by `StartedAt` DESC), empty results (`totalCount=0`), page beyond total range, pageSize capped at 100. Follow existing `ListReviewItemsQueryHandlerTests` pattern.

### Implementation for User Story 2

- [x] T009 [P] [US2] Create `ListScanHistoryQuery` record, `ListScanHistoryQueryValidator`, and `ListScanHistoryQueryHandler` in `MediaHandler.Application/Features/Scan/Queries/ListScanHistory/ListScanHistoryQuery.cs`. Query takes `int Page` (default 1) and `int PageSize` (default 20). Validator: `Page` ≥ 1, `PageSize` 1–100. Handler: query `ScanRuns` with `AsNoTracking()`, order by `StartedAt` descending, count total, apply `Skip`/`Take`, map to `ScanRunDto`, return `PagedResult<ScanRunDto>`. Follow the `ListReviewItemsQuery` pagination pattern.
- [x] T010 [US2] Add `ListHistory` action method to `MediaHandler.API/Controllers/AdminScanController.cs`. Route: `[HttpGet]` on the existing controller base route (which is `/api/v1/admin/scan`). Accept `[FromQuery] int page = 1, int pageSize = 20`, map to `ListScanHistoryQuery`, send via MediatR. Return `ApiResponse<List<ScanRunDto>>` with `ApiResponseMeta` containing `page`, `pageSize`, `totalCount`, `totalPages`. Add `[ProducesResponseType]` attributes for 200, 400.

**Checkpoint**: US2 is fully functional — scan history is browsable with correct pagination metadata.

---

## Phase 5: User Story 3 — Reopen Resolved or Dismissed Review Items (Priority: P2)

**Goal**: Allow an admin to reopen a previously resolved or dismissed review item by sending `POST /api/v1/admin/review-items/{id}/resolve` with `{ "action": "Reopen" }`. The item reverts to `Open` status and all resolution fields are cleared.

**Independent Test**: Resolve a review item, then call the resolve endpoint with `Reopen` action, verify the item returns to `Open` status with all resolution fields (`ResolvedTmdbId`, `ResolvedKind`, `ResolvedAt`, `ResolvedBy`) cleared to null. Attempt to reopen an already-open item and verify 409 response.

### Tests for User Story 3

- [ ] T011 [P] [US3] Create unit tests for the Reopen case in `MediaHandler.Tests/Features/Review/ResolveReviewItemReopenTests.cs`. Cover: success reopen from `Resolved` status, success reopen from `Dismissed` status (verify all resolution fields cleared and status set to `Open`), already-open conflict (409 `REVIEW_ALREADY_OPEN`), not-found (404). Follow existing `ResolveReviewItemCommandHandlerTests` pattern.

### Implementation for User Story 3

- [ ] T012 [US3] Add `Reopen` case to the `switch` statement in the existing `ResolveReviewItemCommandHandler` in `MediaHandler.Application/Features/Review/Commands/ResolveReviewItem/ResolveReviewItemCommand.cs`. The `Reopen` case must: (1) guard that current status is `Resolved` or `Dismissed` — if `Open`, return failure with `REVIEW_ALREADY_OPEN` message; (2) set `Status = ReviewStatus.Open`; (3) clear `ResolvedTmdbId = null`, `ResolvedKind = null`, `ResolvedAt = null`, `ResolvedBy = null`; (4) save and return the updated `ReviewItemDto`.
- [ ] T013 [US3] Add `REVIEW_ALREADY_OPEN` error mapping to `MediaHandler.API/Controllers/AdminReviewController.cs`. In the resolve action's error handling, add a case for error messages containing `REVIEW_ALREADY_OPEN` → return 409 Conflict. Follow the existing `REVIEW_ALREADY_RESOLVED` → 409 pattern already in the controller.

**Checkpoint**: US3 is fully functional — review items can be reopened with proper state validation and error responses.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all three user stories.

- [ ] T014 Run full build and verify zero warnings: `dotnet build` from repository root
- [ ] T015 [P] Run all unit tests: `dotnet test MediaHandler.Tests`
- [ ] T016 [P] Run format check: `dotnet format --verify-no-changes`
- [ ] T017 Run quickstart.md verification scenarios (all 6 acceptance checks from quickstart.md § Verification)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — verify existing state
- **Foundational (Phase 2)**: Depends on Phase 1 — adds `Reopen` enum value
- **User Story 1 (Phase 3)**: Depends on Phase 2 — no dependency on other stories
- **User Story 2 (Phase 4)**: Depends on Phase 2 — no dependency on other stories
- **User Story 3 (Phase 5)**: Depends on Phase 2 (requires `Reopen` enum value) — no dependency on other stories
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (Toggle Library Root)**: Independent — touches `LibraryRoots` feature only
- **US2 (Scan History)**: Independent — touches `Scan` feature only
- **US3 (Reopen Review Items)**: Depends on T003 (enum change) — touches `Review` feature only

### Within Each User Story

- Tests written first (TDD — should fail before implementation)
- Command/Query + Validator + Handler created
- Request DTO added (if needed)
- Controller action wired up
- Story is independently testable at checkpoint

### Parallel Opportunities

- **After Phase 2**: All three user stories (US1, US2, US3) can start simultaneously
- **Within US1**: T004 (tests), T005 (command), T006 (request DTO) can run in parallel
- **Within US2**: T008 (tests) and T009 (query) can run in parallel
- **Within US3**: T011 (tests) can run in parallel with other stories
- **Phase 6**: T015 (tests) and T016 (format check) can run in parallel

---

## Parallel Example: All User Stories

```bash
# After Phase 2 completes, launch all three stories in parallel:

# Developer A — US1:
Task T004: "Unit tests for toggle handler"
Task T005: "ToggleLibraryRootEnabledCommand + Handler + Validator"
Task T006: "ToggleLibraryRootEnabledRequest DTO"
Task T007: "AdminLibraryRootsController.ToggleEnabled action"

# Developer B — US2:
Task T008: "Unit tests for scan history query handler"
Task T009: "ListScanHistoryQuery + Handler + Validator"
Task T010: "AdminScanController.ListHistory action"

# Developer C — US3:
Task T011: "Unit tests for Reopen case"
Task T012: "Reopen case in ResolveReviewItemCommandHandler"
Task T013: "REVIEW_ALREADY_OPEN error mapping in AdminReviewController"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup verification
2. Complete Phase 2: Add `Reopen` enum value (foundational)
3. Complete Phase 3: Toggle Library Root Enabled (US1)
4. **STOP and VALIDATE**: Test US1 independently with quickstart.md scenarios 1–2
5. Deploy if ready — admins can manage library root enabled state

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Toggle Enabled) → Test → Deploy (MVP!)
3. Add US2 (Scan History) → Test → Deploy
4. Add US3 (Reopen Review Items) → Test → Deploy
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Phase 2 is done:
   - Developer A: US1 (Toggle Library Root)
   - Developer B: US2 (Scan History)
   - Developer C: US3 (Reopen Review Items)
3. All stories complete and integrate independently — no cross-story file conflicts

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- No database migrations needed — all entities and columns already exist
- `ReviewResolutionAction` enum stored as string in PostgreSQL — adding `Reopen` is backwards-compatible
- All three endpoints reuse existing DTOs (`LibraryRootDto`, `ScanRunDto`, `ReviewItemDto`)
- Only one new request DTO: `ToggleLibraryRootEnabledRequest`
- Follow existing handler patterns: `RemoveLibraryRootCommand` (US1), `ListReviewItemsQuery` (US2), `ResolveReviewItemCommand` (US3)


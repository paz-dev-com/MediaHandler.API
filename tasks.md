# Tasks: Administration Dashboard

**Input**: Design documents from `/specs/004-admin-dashboard/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/api-endpoints.md ✅, quickstart.md ✅

**Tests**: Unit tests are included — the spec (plan.md Constitution Check, §II) requires `.spec.ts` for every service and complex components.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Single Angular project**: `src/app/` at repository root
- Feature files: `src/app/features/admin/`
- Shared models: `src/app/shared/models/`
- Translations: `src/assets/i18n/`

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the admin feature directory structure, shared models, enums, routing, and sidebar integration

- [x] T001 Add admin enums (`LibraryRootKind`, `ScanMode`, `ScanStatus`, `ReviewStatus`, `ReviewReason`, `ReviewResolutionAction`) to `src/app/shared/models/enums.ts`
- [x] T002 [P] Create `LibraryRoot` interface in `src/app/shared/models/library-root.model.ts`
- [x] T003 [P] Create `ScanCounts`, `ScanRunSummary`, and `ScanRunDetail` interfaces in `src/app/shared/models/admin-scan.model.ts`
- [x] T004 [P] Create `TmdbCandidate` and `ReviewItem` interfaces in `src/app/shared/models/review.model.ts`
- [x] T005 [P] Create `HealthStatus` interface in `src/app/shared/models/health.model.ts`
- [x] T006 Create admin child routes in `src/app/features/admin/admin.routes.ts` with lazy-loaded child components for dashboard, users, library-roots, scanner, and review
- [x] T007 Add `/admin` route with `authGuard` + `adminGuard` and `loadChildren` to `src/app/app.routes.ts` (insert before the wildcard `**` route)
- [x] T008 Add "Administration" nav item (`{ labelKey: 'nav.admin', icon: 'pi pi-cog', route: '/admin' }`) to the admin section of `src/app/core/layout/sidebar.component.ts`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Admin layout shell with TabMenu sub-navigation — all user story pages render inside this layout

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T009 Create `AdminLayoutComponent` with PrimeNG `TabMenu` sub-navigation (Dashboard, Users, Library Roots, Scanner, Review tabs linked to child routes) and `<router-outlet>` in `src/app/features/admin/admin-layout.component.ts`, `src/app/features/admin/admin-layout.component.html`, and `src/app/features/admin/admin-layout.component.scss`

**Checkpoint**: Admin layout shell renders at `/admin` with working tab navigation between empty child routes

---

## Phase 3: User Story 1 — User Management (Priority: P1) 🎯 MVP

**Goal**: Admins can view, search, paginate all users and change roles / toggle active status

**Independent Test**: Navigate to `/admin/users`, see paginated user table, search for a user, change their role via dropdown, toggle active status — each action shows a success toast

### Tests for User Story 1

- [x] T010 [P] [US1] Create unit tests for `AdminUserService` in `src/app/features/admin/users/admin-user.service.spec.ts` — test `getUsers()`, `setRole()`, `setActive()` HTTP calls and signal state updates

### Implementation for User Story 1

- [x] T011 [US1] Implement `AdminUserService` with signals (`users`, `loading`, `meta`) and methods `getUsers(page, pageSize, search)`, `setRole(userId, role)`, `setActive(userId, isActive)` in `src/app/features/admin/users/admin-user.service.ts`
- [x] T012 [US1] Create `AdminUsersPageComponent` with PrimeNG `Table` (server-side pagination via `onLazyLoad`), `InputText` search, `Select` for role change, `ToggleSwitch`/`Button` for active toggle, and `Tag` for status display in `src/app/features/admin/users/admin-users-page.component.ts`, `src/app/features/admin/users/admin-users-page.component.html`, and `src/app/features/admin/users/admin-users-page.component.scss`

**Checkpoint**: User Story 1 fully functional — admin can list, search, paginate users, change roles, and toggle active status

---

## Phase 4: User Story 2 — Library Root Management (Priority: P1)

**Goal**: Admins can view, filter, add, remove, and enable/disable NAS library roots

**Independent Test**: Navigate to `/admin/library-roots`, see paginated root list, filter by kind/enabled, add a new root via dialog, toggle enabled switch, remove a root with confirmation

### Tests for User Story 2

- [ ] T013 [P] [US2] Create unit tests for `AdminLibraryRootService` in `src/app/features/admin/library-roots/admin-library-root.service.spec.ts` — test `getRoots()`, `addRoot()`, `removeRoot()`, `setEnabled()` HTTP calls and signal state updates

### Implementation for User Story 2

- [ ] T014 [US2] Implement `AdminLibraryRootService` with signals (`roots`, `loading`, `meta`) and methods `getRoots(page, pageSize, kind?, enabledOnly?)`, `addRoot(path, kind, label?)`, `removeRoot(id)`, `setEnabled(id, isEnabled)` in `src/app/features/admin/library-roots/admin-library-root.service.ts`
- [ ] T015 [P] [US2] Create `AddLibraryRootDialogComponent` with PrimeNG `Dialog`, `InputText` for path, `Select` for kind, `InputText` for optional label, and submit/cancel buttons in `src/app/features/admin/library-roots/add-library-root-dialog.component.ts`
- [ ] T016 [US2] Create `AdminLibraryRootsPageComponent` with PrimeNG `Table` (pagination, `Select` filter for kind, `Select` filter for enabled status), `ToggleSwitch` for enable/disable, remove button with `ConfirmDialog`, and "Add Library Root" button opening the dialog in `src/app/features/admin/library-roots/admin-library-roots-page.component.ts`, `src/app/features/admin/library-roots/admin-library-roots-page.component.html`, and `src/app/features/admin/library-roots/admin-library-roots-page.component.scss`

**Checkpoint**: User Story 2 fully functional — admin can manage library roots with all CRUD + enable/disable operations

---

## Phase 5: User Story 3 — Scanner Operations & Monitoring (Priority: P2)

**Goal**: Admins can start/cancel scans, monitor live status via polling, and browse paginated scan history

**Independent Test**: Navigate to `/admin/scanner`, start a Full scan on all enabled roots, observe live status polling, cancel the scan, then view the scan in the history table. Verify disabled library roots do NOT appear in the root selector.

### Tests for User Story 3

- [ ] T017 [P] [US3] Create unit tests for `AdminScanService` in `src/app/features/admin/scanner/admin-scan.service.spec.ts` — test `startScan()`, `getActiveScan()`, `cancelScan()`, `getScanHistory()`, `getScanDetail()` HTTP calls, polling lifecycle (start/stop on terminal state), and signal state

### Implementation for User Story 3

- [ ] T018 [US3] Implement `AdminScanService` with signals (`activeScan`, `scanHistory`, `historyMeta`, `loading`) and methods `startScan(libraryRootIds, mode)`, `getActiveScan()`, `cancelScan(id)`, `getScanHistory(page, pageSize)`, `getScanDetail(id, includeReview)`, plus `interval(4000)` + `switchMap` polling with `takeUntilDestroyed()` in `src/app/features/admin/scanner/admin-scan.service.ts`
- [ ] T019 [P] [US3] Create `ScanLauncherComponent` with PrimeNG `MultiSelect` for enabled library root selection (populated from `AdminLibraryRootService.getRoots` with `enabledOnly=true`), `Select` for scan mode (Full/Incremental), and "Start Scan" `Button` with loading state in `src/app/features/admin/scanner/scan-launcher.component.ts`
- [ ] T020 [P] [US3] Create `ScanStatusComponent` with PrimeNG `Tag` for scan status badge, `ProgressSpinner` for running state, scan counts display, and "Cancel Scan" `Button` in `src/app/features/admin/scanner/scan-status.component.ts`
- [ ] T021 [P] [US3] Create `ScanHistoryTableComponent` with PrimeNG `Table` (server-side pagination, 20 rows per page) displaying scan mode, status `Tag`, start/finish timestamps, and summary counts per row in `src/app/features/admin/scanner/scan-history-table.component.ts`
- [ ] T022 [US3] Create `AdminScannerPageComponent` orchestrating `ScanLauncherComponent`, `ScanStatusComponent`, and `ScanHistoryTableComponent` — show launcher when no scan is active, show status when scan is running, always show history table below in `src/app/features/admin/scanner/admin-scanner-page.component.ts`, `src/app/features/admin/scanner/admin-scanner-page.component.html`, and `src/app/features/admin/scanner/admin-scanner-page.component.scss`

**Checkpoint**: User Story 3 fully functional — admin can start, monitor, cancel scans and browse history

---

## Phase 6: User Story 4 — Review Queue Management (Priority: P2)

**Goal**: Admins can view, filter, and resolve review items (assign TMDB candidate, dismiss, delete, reopen)

**Independent Test**: Navigate to `/admin/review`, see paginated open review items, filter by status/reason/scanRunId, select an item, assign a TMDB candidate via the resolve dialog, then dismiss and reopen another item

### Tests for User Story 4

- [ ] T023 [P] [US4] Create unit tests for `AdminReviewService` in `src/app/features/admin/review/admin-review.service.spec.ts` — test `getItems()`, `resolveItem()` with all action types (Assign, Dismiss, Delete, Reopen) HTTP calls and signal state

### Implementation for User Story 4

- [ ] T024 [US4] Implement `AdminReviewService` with signals (`items`, `loading`, `meta`) and methods `getItems(status?, reason?, scanRunId?, page?, pageSize?)`, `resolveItem(id, action, tmdbId?, kind?)` in `src/app/features/admin/review/admin-review.service.ts`
- [ ] T025 [P] [US4] Create `ReviewResolveDialogComponent` with PrimeNG `Dialog` displaying the selected review item's file path, parsed metadata, TMDB candidate list with poster previews, and action buttons (Assign selected candidate, Dismiss, Delete, Reopen) in `src/app/features/admin/review/review-resolve-dialog.component.ts`
- [ ] T026 [US4] Create `AdminReviewPageComponent` with PrimeNG `Table` (server-side pagination), `Select` filters for status, reason, and scan run, `Tag` for status/reason columns, and row click opening `ReviewResolveDialogComponent` in `src/app/features/admin/review/admin-review-page.component.ts`, `src/app/features/admin/review/admin-review-page.component.html`, and `src/app/features/admin/review/admin-review-page.component.scss`

**Checkpoint**: User Story 4 fully functional — admin can manage the full review item lifecycle

---

## Phase 7: User Story 5 — System Health Overview (Priority: P3)

**Goal**: Admin landing page displays API health status, server timestamp, and application version

**Independent Test**: Navigate to `/admin/dashboard`, verify health panel shows "Healthy"/"Unhealthy" status with appropriate color badge, server timestamp, and version string

### Implementation for User Story 5

- [ ] T027 [US5] Implement `AdminHealthService` with signal (`health`) and method `getHealth()` calling `GET health` in `src/app/features/admin/dashboard/admin-health.service.ts`
- [ ] T028 [P] [US5] Create `HealthPanelComponent` with PrimeNG `Tag` (severity: `success` for Healthy, `danger` for Unhealthy), `ProgressSpinner` while loading, timestamp display, and version display in `src/app/features/admin/dashboard/health-panel.component.ts`
- [ ] T029 [US5] Create `AdminDashboardPageComponent` embedding `HealthPanelComponent` and displaying quick-stats summary on the admin landing page in `src/app/features/admin/dashboard/admin-dashboard-page.component.ts`, `src/app/features/admin/dashboard/admin-dashboard-page.component.html`, and `src/app/features/admin/dashboard/admin-dashboard-page.component.scss`

**Checkpoint**: User Story 5 fully functional — admin sees health status on dashboard landing

---

## Phase 8: User Story 6 — Bilingual Support (Priority: P3)

**Goal**: All admin section labels, buttons, messages, table headers, and status indicators are translated in English and French

**Independent Test**: Switch language to French, navigate to each admin sub-section (Dashboard, Users, Library Roots, Scanner, Review), verify all visible text appears in French with no untranslated `admin.*` keys

### Implementation for User Story 6

- [ ] T030 [P] [US6] Add English translation keys (`admin.dashboard.*`, `admin.users.*`, `admin.libraryRoots.*`, `admin.scanner.*`, `admin.review.*`, `admin.health.*`, `nav.admin`) to `src/assets/i18n/en.json`
- [ ] T031 [P] [US6] Add French translation keys (matching `admin.*` structure) to `src/assets/i18n/fr.json`
- [ ] T032 [US6] Wire up `transloco` pipes in all admin component templates — replace all hardcoded strings with `{{ t('admin.*') }}` translation keys in every `.component.html` file under `src/app/features/admin/`

**Checkpoint**: All admin UI fully bilingual — no untranslated keys in either language

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, accessibility checks, and cleanup across all admin sub-sections

- [ ] T033 [P] Add loading states (`ProgressSpinner` or button `loading` property) to all async operations across admin components
- [ ] T034 [P] Add empty state messages for empty tables/lists (no users found, no library roots, no scan history, no review items) across admin components
- [ ] T035 Verify error handling — ensure backend `409`, `404`, `400`, `422` errors display meaningful toast messages via existing error interceptor across all admin services
- [ ] T036 Run `quickstart.md` validation — start app, navigate to `/admin`, exercise each sub-section per the quickstart steps

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (needs routes and models) — BLOCKS all user stories
- **User Stories (Phases 3–8)**: All depend on Phase 2 completion
  - US1 (Users) and US2 (Library Roots) can proceed in parallel
  - US3 (Scanner) depends on US2's `AdminLibraryRootService` for the scan root selector
  - US4 (Review) is independent of other stories
  - US5 (Health/Dashboard) is independent of other stories
  - US6 (i18n) should be done last — after all component templates exist
- **Polish (Phase 9)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Independent — start after Phase 2
- **US2 (P1)**: Independent — start after Phase 2 (can parallel with US1)
- **US3 (P2)**: Soft dependency on US2 (`AdminLibraryRootService` needed for scan launcher root selector)
- **US4 (P2)**: Independent — start after Phase 2 (can parallel with US1/US2)
- **US5 (P3)**: Independent — start after Phase 2
- **US6 (P3)**: Depends on all component templates being created (US1–US5)

### Within Each User Story

- Tests written FIRST; should FAIL before implementation
- Service before page component
- Child/dialog components can be parallel [P] with service
- Page component wires everything together last

### Parallel Opportunities

- T002, T003, T004, T005 (model files) — all parallel
- T010, T013, T017, T023 (service tests) — all parallel once Phase 2 is done
- T015, T019, T020, T021, T025, T028 (child components) — parallel within their story
- T030, T031 (translation files) — parallel
- US1 and US2 entire phases — parallel
- US4 and US5 entire phases — parallel

---

## Parallel Example: User Story 2

```bash
# After T014 (service), launch child component and page in parallel:
Task T015: "Create AddLibraryRootDialogComponent in src/app/features/admin/library-roots/add-library-root-dialog.component.ts"

# Then sequentially:
Task T016: "Create AdminLibraryRootsPageComponent (orchestrates table + dialog)"
```

## Parallel Example: User Story 3

```bash
# After T018 (service), launch all child components in parallel:
Task T019: "Create ScanLauncherComponent"
Task T020: "Create ScanStatusComponent"
Task T021: "Create ScanHistoryTableComponent"

# Then sequentially:
Task T022: "Create AdminScannerPageComponent (orchestrates all children)"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup (T001–T008)
2. Complete Phase 2: Foundational (T009)
3. Complete Phase 3: User Story 1 — User Management (T010–T012)
4. Complete Phase 4: User Story 2 — Library Root Management (T013–T016)
5. **STOP and VALIDATE**: Both P1 stories independently testable
6. Add minimal i18n keys for US1/US2 if needed for demo

### Incremental Delivery

1. Setup + Foundational → Admin shell renders with tab navigation
2. Add US1 (Users) → Test user management independently → Demo
3. Add US2 (Library Roots) → Test root management independently → Demo
4. Add US3 (Scanner) → Test scan operations independently → Demo
5. Add US4 (Review) → Test review queue independently → Demo
6. Add US5 (Health) → Test dashboard health panel → Demo
7. Add US6 (i18n) → Verify all text bilingual → Demo
8. Polish → Final validation

### Parallel Team Strategy

With multiple developers after Phase 2 is complete:

- **Developer A**: US1 (Users) → then US3 (Scanner)
- **Developer B**: US2 (Library Roots) → then US4 (Review)
- **Developer C**: US5 (Health/Dashboard) → then US6 (i18n) → then Polish

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- ⚠️ Three backend endpoints are PENDING (see plan.md §Backend API Gaps): `PUT library-roots/{id}/enabled`, `GET scan?page&pageSize`, `Reopen` action — frontend is coded against expected contracts

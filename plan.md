# Implementation Plan: Administration Dashboard

**Branch**: `feature/spec004-admin-dashboard` | **Date**: 2025-07-17 | **Spec**: `specs/004-admin-dashboard/spec.md`  
**Input**: Feature specification from `specs/004-admin-dashboard/spec.md`

## Summary

Build a comprehensive admin dashboard for the MediaHandler.Web Angular application, providing user management, NAS library root configuration, scanner operations with live polling, a TMDB review queue, and a system health overview. The entire UI is built with PrimeNG components (p-table, Dialog, Tag, ConfirmDialog, etc.) on top of the existing Angular 21 + Transloco + Auth0 + PrimeNG stack. The admin section is a new lazy-loaded route group at `/admin` behind the existing `adminGuard`, with child routes for each sub-section and a shared layout with `TabMenu` sub-navigation.

## Technical Context

**Language/Version**: TypeScript 5.9 / Angular 21.2  
**Primary Dependencies**: PrimeNG 21.1, PrimeFlex 4, @primeuix/themes (Aura), @jsverse/transloco 8, @auth0/auth0-angular 2.7, RxJS 7.8  
**Storage**: N/A (frontend consumes REST API; backend uses PostgreSQL)  
**Testing**: Vitest 4 with jsdom  
**Target Platform**: Web — modern evergreen browsers (Chrome, Firefox, Edge, Safari latest 2 versions)  
**Project Type**: Single-page web application (Angular standalone components)  
**Performance Goals**: LCP < 2.5s, FID < 100ms, CLS < 0.1 on 4G mobile; initial bundle < 500kB warning / 1MB error  
**Constraints**: Admin feature must be lazy-loaded (not in initial bundle); all components use OnPush change detection; signals-first state management; no new third-party libraries  
**Scale/Scope**: ~25 new files (components, services, models, specs); 5 admin sub-sections; bilingual (en/fr)

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle                    | Status  | Notes                                                                                                |
| ---------------------------- | ------- | ---------------------------------------------------------------------------------------------------- |
| **I. Single Responsibility** | ✅ PASS | Each admin sub-section split into page + child components, all under 200 lines                       |
| **I. Angular Signals-First** | ✅ PASS | All service state uses `signal()`; RxJS only for HTTP streams and polling interval                   |
| **I. Strict Typing**         | ✅ PASS | All models defined with explicit interfaces matching backend DTOs; no `any`                          |
| **I. Prettier Compliance**   | ✅ PASS | Project Prettier config (100 char, single quotes, Angular HTML parser) unchanged                     |
| **I. Standalone Components** | ✅ PASS | All new components are standalone; no NgModules introduced                                           |
| **I. Reactive Patterns**     | ✅ PASS | HTTP via `ApiService` Observables → piped → stored in signals; polling uses `interval` + `switchMap` |
| **II. Unit Tests Required**  | ✅ PASS | Every service gets a `.spec.ts`; complex components get interaction tests                            |
| **II. Test Isolation**       | ✅ PASS | Each test uses fresh setup via `beforeEach`                                                          |
| **III. Responsive Design**   | ✅ PASS | PrimeNG tables are responsive; layout uses PrimeFlex grid utilities                                  |
| **III. Loading States**      | ✅ PASS | Every async operation shows `ProgressSpinner` or button `loading` state                              |
| **III. Error Feedback**      | ✅ PASS | Error interceptor shows toasts; empty states show meaningful messages                                |
| **III. Accessibility**       | ✅ PASS | PrimeNG components are keyboard-navigable; ARIA labels provided                                      |
| **III. Consistent Styling**  | ✅ PASS | Uses PrimeNG theme (Aura) + shared SCSS variables; no hard-coded colors                              |
| **IV. Bundle Budget**        | ✅ PASS | No new dependencies; admin routes lazy-loaded                                                        |
| **IV. Lazy Loading**         | ✅ PASS | `/admin` route and all children are lazy-loaded via `loadChildren`/`loadComponent`                   |
| **IV. OnPush**               | ✅ PASS | All new components use `ChangeDetectionStrategy.OnPush`                                              |
| **IV. Memory Management**    | ✅ PASS | Subscriptions cleaned up via `takeUntilDestroyed()` / `DestroyRef`; polling auto-stops               |
| **No new dependencies**      | ✅ PASS | Zero new npm packages added                                                                          |

**Post-design re-check**: All gates remain ✅ PASS after Phase 1 design.

## Project Structure

### Documentation (this feature)

```text
specs/004-admin-dashboard/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── api-endpoints.md # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/app/
├── core/
│   ├── api/
│   │   ├── api.service.ts              # Existing — used by all admin services
│   │   ├── api-response.model.ts       # Existing — ApiResponse<T>, PaginationMeta
│   │   └── error.interceptor.ts        # Existing — global error toasts
│   ├── auth/
│   │   ├── auth.service.ts             # Existing — isAdmin signal
│   │   ├── admin.guard.ts              # Existing — guards /admin route
│   │   └── auth.interceptor.ts         # Existing — adds Bearer token
│   └── layout/
│       └── sidebar.component.ts        # Modified — add "Administration" nav item
├── features/
│   └── admin/                          # NEW — entire directory
│       ├── admin.routes.ts
│       ├── admin-layout.component.ts
│       ├── admin-layout.component.html
│       ├── admin-layout.component.scss
│       ├── dashboard/
│       │   ├── admin-dashboard-page.component.ts
│       │   ├── admin-dashboard-page.component.html
│       │   ├── admin-dashboard-page.component.scss
│       │   ├── health-panel.component.ts
│       │   └── admin-health.service.ts
│       ├── users/
│       │   ├── admin-users-page.component.ts
│       │   ├── admin-users-page.component.html
│       │   ├── admin-users-page.component.scss
│       │   ├── admin-user.service.ts
│       │   └── admin-user.service.spec.ts
│       ├── library-roots/
│       │   ├── admin-library-roots-page.component.ts
│       │   ├── admin-library-roots-page.component.html
│       │   ├── admin-library-roots-page.component.scss
│       │   ├── add-library-root-dialog.component.ts
│       │   ├── admin-library-root.service.ts
│       │   └── admin-library-root.service.spec.ts
│       ├── scanner/
│       │   ├── admin-scanner-page.component.ts
│       │   ├── admin-scanner-page.component.html
│       │   ├── admin-scanner-page.component.scss
│       │   ├── scan-launcher.component.ts
│       │   ├── scan-status.component.ts
│       │   ├── scan-history-table.component.ts
│       │   ├── admin-scan.service.ts
│       │   └── admin-scan.service.spec.ts
│       └── review/
│           ├── admin-review-page.component.ts
│           ├── admin-review-page.component.html
│           ├── admin-review-page.component.scss
│           ├── review-resolve-dialog.component.ts
│           ├── admin-review.service.ts
│           └── admin-review.service.spec.ts
├── shared/
│   └── models/
│       ├── enums.ts                    # Modified — add admin enums
│       ├── library-root.model.ts       # NEW
│       ├── admin-scan.model.ts         # NEW
│       ├── review.model.ts             # NEW
│       └── health.model.ts             # NEW
├── app.routes.ts                       # Modified — add /admin route
└── ...

src/assets/i18n/
├── en.json                             # Modified — add admin.* keys
└── fr.json                             # Modified — add admin.* keys
```

**Structure Decision**: Single Angular project with feature-based directory structure under `src/app/features/admin/`. Each admin sub-section (dashboard, users, library-roots, scanner, review) is a sub-directory with its own page component, child components, and service. This follows the existing project convention where each feature (collection, media-detail, nas-scanner, etc.) has its own directory under `features/`.

## Architecture Decisions

### Component Hierarchy

```
AppComponent
└── SidebarComponent (modified: adds "Administration" nav item for admins)
└── RouterOutlet
    └── /admin → AdminLayoutComponent (shared TabMenu sub-nav + router-outlet)
        ├── /admin/dashboard → AdminDashboardPageComponent
        │   └── HealthPanelComponent
        ├── /admin/users → AdminUsersPageComponent (inline p-table + role/active controls)
        ├── /admin/library-roots → AdminLibraryRootsPageComponent
        │   └── AddLibraryRootDialogComponent
        ├── /admin/scanner → AdminScannerPageComponent
        │   ├── ScanLauncherComponent
        │   ├── ScanStatusComponent
        │   └── ScanHistoryTableComponent
        └── /admin/review → AdminReviewPageComponent
            └── ReviewResolveDialogComponent
```

### PrimeNG Usage Map

| Component                        | PrimeNG Component                                                   | Key Features                                                                   |
| -------------------------------- | ------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `AdminLayoutComponent`           | `TabMenu`                                                           | Route-linked tabs for sub-sections                                             |
| `AdminUsersPageComponent`        | `Table`, `Tag`, `Button`, `Select`, `InputText`                     | Server-side pagination, search, role change via dropdown, active toggle        |
| `AdminLibraryRootsPageComponent` | `Table`, `Tag`, `Button`, `ToggleSwitch`, `Select`, `ConfirmDialog` | Filter by kind/enabled, toggle enabled switch, remove with confirmation        |
| `AddLibraryRootDialogComponent`  | `Dialog`, `InputText`, `Select`, `Button`                           | Modal form for path + kind + label                                             |
| `AdminScannerPageComponent`      | `Toolbar`                                                           | Action bar container                                                           |
| `ScanLauncherComponent`          | `MultiSelect`, `Select`, `Button`                                   | Root selection (enabled only), mode dropdown, start button                     |
| `ScanStatusComponent`            | `Tag`, `Badge`, `ProgressSpinner`, `Button`                         | Live status display, cancel button                                             |
| `ScanHistoryTableComponent`      | `Table`, `Tag`                                                      | Paginated history with status tags                                             |
| `AdminReviewPageComponent`       | `Table`, `Tag`, `Select`, `Button`                                  | Filterable review items with status/reason columns                             |
| `ReviewResolveDialogComponent`   | `Dialog`, `Button`, `Tag`                                           | TMDB candidate list with poster previews, assign/dismiss/delete/reopen actions |
| `HealthPanelComponent`           | `Tag`, `ProgressSpinner`                                            | Health status badge, version display                                           |
| `AdminDashboardPageComponent`    | (layout container)                                                  | Quick stats, health panel                                                      |

### Backend API Gaps

Three endpoints required by the spec are **not yet implemented** in the backend:

1. **`PUT /api/v1/admin/library-roots/{id}/enabled`** — toggle library root enabled/disabled (FR-009a)
2. **`GET /api/v1/admin/scan?page&pageSize`** — list scan history (FR-012a)
3. **`Reopen` action in `ReviewResolutionAction`** — reopen resolved/dismissed items (FR-016a)

The frontend services are designed against these expected contracts (documented in `contracts/api-endpoints.md`). The backend team must implement these before integration testing.

## Complexity Tracking

No constitution violations. No complexity exceptions needed.

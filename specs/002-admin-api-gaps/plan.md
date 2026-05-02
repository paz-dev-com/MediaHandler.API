# Implementation Plan: Admin Dashboard Backend API Gaps

**Branch**: `develop` | **Date**: 2025-07-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-admin-api-gaps/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Three backend API gaps needed by the Angular admin dashboard: (1) a PUT endpoint to toggle `LibraryRoot.IsEnabled` with scan-in-progress conflict guard, (2) a GET endpoint returning paginated scan history using the existing `ScanRunSummaryResponse` DTO, and (3) a `Reopen` action added to `ReviewResolutionAction` enum that reverts resolved/dismissed review items back to Open status. All endpoints follow existing Clean Architecture CQRS patterns with MediatR, FluentValidation, Result pattern, and `ApiResponse<T>` envelope.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: MediatR, FluentValidation, EF Core (Npgsql), ASP.NET Core  
**Storage**: PostgreSQL (via EF Core with Npgsql provider)  
**Testing**: xUnit, NSubstitute, EF Core InMemory provider (unit); Testcontainers (integration)  
**Target Platform**: Linux server (Docker)  
**Project Type**: web-service (REST API)  
**Performance Goals**: < 2s response for all operations; rate-limited at 100 req/min  
**Constraints**: AdminOnly authorization policy; `ApiResponse<T>` envelope; server-side pagination with max pageSize=100  
**Scale/Scope**: Personal media library management; single-user admin

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Code Quality — Clean Architecture & CQRS | ✅ PASS | All changes follow Domain→Application→Infrastructure→API dependency rule. Each feature uses Command/Query + Handler + Validator pattern. |
| I. Result pattern | ✅ PASS | All handlers return `Result<T>` or `Result`. No exceptions for business logic. |
| I. FluentValidation pipeline | ✅ PASS | Each new command/query gets a dedicated `AbstractValidator<T>`. |
| I. Entity configuration (Fluent API) | ✅ PASS | No new entities; no schema changes needed. Existing configurations remain. |
| I. Code style | ✅ PASS | File-scoped namespaces, primary constructors, record DTOs, `#nullable enable`. |
| II. Testing Standards | ✅ PASS | Each handler gets unit tests (success + failure). Validator tests for non-trivial rules. |
| III. UX Consistency — ApiResponse envelope | ✅ PASS | All endpoints use `ApiResponse<T>` with `data`, `meta`, `errors`. |
| III. Pagination | ✅ PASS | Scan history uses `PagedResult<T>` with `page`, `pageSize`, `totalCount`, `totalPages`. |
| III. Versioned routes | ✅ PASS | All under `/api/v1/admin/`. |
| III. Swagger docs | ✅ PASS | `[ProducesResponseType]` on all new actions. |
| III. Role-based access | ✅ PASS | All endpoints protected by `AdminOnly` policy. |
| IV. Performance — Query performance | ✅ PASS | `AsNoTracking()` for read queries; server-side `Skip`/`Take` pagination. |
| IV. Rate limiting | ✅ PASS | Existing `fixed` rate limiter on all admin controllers. |
| Architecture — Dependency rule | ✅ PASS | `Reopen` enum value in Domain; command/handler in Application; controller in API. |
| Audit trail | ✅ PASS | `BaseEntity` auto-populated by `AuditableEntitySaveChangesInterceptor`. |

**Gate Result**: ✅ ALL GATES PASS — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/002-admin-api-gaps/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-endpoints.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
MediaHandler.Domain/
├── Enums/
│   └── ReviewResolutionAction.cs        # Add Reopen value
└── Entities/                            # No changes needed

MediaHandler.Application/
├── Features/
│   ├── LibraryRoots/
│   │   └── Commands/
│   │       └── ToggleLibraryRootEnabled/
│   │           └── ToggleLibraryRootEnabledCommand.cs  # Command + Handler + Validator
│   ├── Scan/
│   │   └── Queries/
│   │       └── ListScanHistory/
│   │           └── ListScanHistoryQuery.cs             # Query + Handler + Validator
│   └── Review/
│       └── Commands/
│           └── ResolveReviewItem/
│               └── ResolveReviewItemCommand.cs         # Extend handler with Reopen case
└── Common/                              # No changes needed

MediaHandler.API/
├── Controllers/
│   ├── AdminLibraryRootsController.cs   # Add ToggleEnabled action
│   └── AdminScanController.cs           # Add ListHistory action
├── Contracts/Admin/
│   └── LibraryRootRequests.cs           # Add ToggleEnabledRequest record
└── Models/                              # No changes needed

MediaHandler.Tests/
└── Features/
    ├── LibraryRoots/
    │   └── ToggleLibraryRootEnabledCommandHandlerTests.cs
    ├── Scan/
    │   └── ListScanHistoryQueryHandlerTests.cs
    └── Review/
        └── ResolveReviewItemReopenTests.cs
```

**Structure Decision**: Existing Clean Architecture four-project layout with Domain, Application, Infrastructure, and API layers. No new projects needed — all changes fit within existing structure following established patterns.

## Complexity Tracking

> No violations to justify — all gates pass.

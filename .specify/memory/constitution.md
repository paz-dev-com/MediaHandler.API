<!--
  === Sync Impact Report ===
  Version change: (none) → 1.0.0 (initial ratification)
  Modified principles: N/A (first version)
  Added sections:
    - Core Principles (4): Code Quality, Testing Standards,
      User Experience Consistency, Performance Requirements
    - Architecture & Security Constraints
    - Development Workflow & Quality Gates
    - Governance
  Removed sections: N/A
  Templates requiring updates:
    - .specify/templates/plan-template.md — ✅ compatible
      (Constitution Check section is generic; principles apply)
    - .specify/templates/spec-template.md — ✅ compatible
      (User stories & acceptance scenarios align with testing
      principle; functional requirements align with code quality)
    - .specify/templates/tasks-template.md — ✅ compatible
      (Phase structure supports test-first and per-story delivery)
  Follow-up TODOs: none
-->

# MediaHandler.API Constitution

## Core Principles

### I. Code Quality — Clean Architecture & CQRS Discipline

Every change MUST respect the four-layer Clean Architecture
dependency rule: **Domain → Application → Infrastructure → API**.
No layer may reference a layer above it.

- **CQRS via MediatR**: Every feature MUST be implemented as a
  Command or Query with a dedicated handler. One handler per file,
  one validator per command/query in a separate file.
- **Result pattern**: Business operations MUST return `Result<T>`
  or `Result`. Exceptions MUST NOT be used for business-logic
  errors; they are reserved for unexpected infrastructure failures
  caught by `GlobalExceptionHandler`.
- **FluentValidation pipeline**: Every command/query accepting
  user input MUST have a corresponding `AbstractValidator<T>`
  registered via the `ValidationBehavior` MediatR pipeline.
  Validators MUST cover all required fields, value ranges, and
  string length constraints.
- **Entity configuration**: EF Core entities MUST use Fluent API
  via a dedicated `IEntityTypeConfiguration<T>` — one per entity.
  Data annotations MUST NOT be used for schema configuration.
- **Code style**: File-scoped namespaces, primary constructors,
  `record` types for DTOs/contracts, `#nullable enable` in every
  file. `dotnet format --verify-no-changes` MUST pass in CI.
- **Domain events**: State changes with cross-cutting effects MUST
  raise domain events via `entity.AddDomainEvent(...)`, dispatched
  by `DomainEventDispatchInterceptor`.

*Rationale*: Strict layering and pattern conformity prevent
coupling drift, keep handlers unit-testable in isolation, and
ensure new contributors can locate logic predictably.

### II. Testing Standards

All production code MUST be backed by automated tests at two
levels: **unit** and **integration**.

- **Unit tests** (`MediaHandler.Tests`): Every MediatR handler
  MUST have at least one unit test covering the success path and
  one covering a primary failure path (e.g., not-found, validation
  failure). Use **xUnit** as the test framework, **NSubstitute**
  for mocking dependencies, and **EF Core InMemory provider** for
  database fakes.
- **Integration tests** (`MediaHandler.IntegrationTests`): Every
  user-facing API workflow (multi-step sequences such as
  sync → create → query) MUST have at least one integration test
  exercising the real database via **Testcontainers.MsSql**. Tests
  MUST be self-contained: they create their own data and clean up
  or run in isolated transactions.
- **Validator tests**: FluentValidation validators with non-trivial
  rules (conditional logic, cross-field checks) MUST have dedicated
  unit tests verifying both valid and invalid input.
- **Test naming**: Tests MUST follow the pattern
  `MethodOrScenario_StateUnderTest_ExpectedBehavior`
  (e.g., `DeleteMedia_WhenMediaNotFound_ReturnsFailure`).
- **No test pollution**: Tests MUST NOT depend on execution order
  or shared mutable state across test classes.
- **CI gate**: All unit tests MUST pass before a PR can merge.
  Integration tests MUST pass in CI where Docker is available.

*Rationale*: The CQRS handler-per-file structure makes isolated
unit testing straightforward; mandating both levels catches logic
bugs early and prevents regressions in database interactions.

### III. User Experience Consistency

Every API response MUST be wrapped in the `ApiResponse<T>` envelope
to guarantee a uniform contract for consumers.

- **Success responses**: MUST include `data`, `meta`
  (with `requestId`, `timestamp`), and HTTP 200/201 as
  appropriate.
- **Error responses**: MUST include `errors` array with structured
  `ApiError` objects (`code`, `message`, `field` when applicable).
  HTTP status codes MUST be semantically correct (400 for
  validation, 401 for auth, 403 for authorization, 404 for
  not-found, 429 for rate-limit, 500 for unexpected errors).
- **Pagination**: List endpoints MUST return `PagedResult<T>` with
  `page`, `pageSize`, `totalCount`, and `totalPages`. Default page
  size MUST be defined in configuration, not hard-coded.
- **Versioned routes**: All endpoints MUST be prefixed with
  `/api/v1/`. Breaking changes MUST increment the version segment.
- **Swagger documentation**: Every controller action MUST have
  `[ProducesResponseType]` attributes for all possible status
  codes. Swagger UI MUST accurately reflect the current API
  surface.
- **Role-based access**: Admin-only operations (user management,
  NAS scan, media deletion) MUST be protected by the `AdminOnly`
  authorization policy. Not all users are equal — privilege
  escalation MUST be impossible via API manipulation.
- **Idempotency**: Import operations (e.g., `ImportFromTmdb`) MUST
  be idempotent — duplicate imports by `TmdbId` MUST be rejected
  gracefully, not cause duplicate records.

*Rationale*: A uniform response envelope and predictable error
structure let frontend consumers handle all responses with a single
parser, reducing integration friction and user-facing bugs.

### IV. Performance Requirements

The API MUST remain responsive under normal personal-use load and
degrade gracefully under pressure.

- **Rate limiting**: The fixed-window rate limiter (100 req/min)
  MUST remain active on all endpoints. Adjustments MUST be made
  via configuration, not code changes.
- **HTTP resilience**: All outbound HTTP clients (TMDB, Freebox
  NAS) MUST use `.AddStandardResilienceHandler()` (Polly retry +
  circuit-breaker). Timeout for external calls MUST NOT exceed
  30 seconds.
- **Query performance**: EF Core queries returning lists MUST use
  server-side pagination (`Skip`/`Take`) — client-side enumeration
  of full tables is prohibited. Queries MUST use `AsNoTracking()`
  for read-only paths.
- **Indexing**: Foreign keys and columns used in `WHERE`/`ORDER BY`
  clauses (e.g., `TmdbId`, `OktaId`, `MediaType`) MUST have
  database indexes defined in entity configurations.
- **No N+1 queries**: Handlers loading related data MUST use eager
  loading (`.Include()`) or explicit projection. Lazy loading MUST
  remain disabled.
- **Health checks**: The `/health` endpoint MUST verify database
  connectivity and respond within 5 seconds. A failure MUST return
  HTTP 503.
- **Structured logging**: All external-service calls and error
  paths MUST emit Serilog structured log entries with correlation
  data (`RequestId`, `UserId` where available). Logs MUST NOT
  contain secrets, tokens, or PII.

*Rationale*: Even as a personal project, sluggish responses or
silent failures erode trust in the tool. Proactive guards prevent
performance issues from compounding as the media library grows.

## Architecture & Security Constraints

- **Dependency rule**: `Domain` has zero NuGet/project references.
  `Application` references only `Domain`. `Infrastructure`
  references `Application` and `Domain`. `API` references all
  three. Violations MUST be caught in code review.
- **Secrets management**: Connection strings, API keys, and OAuth
  secrets MUST NEVER appear in source control. Development MUST
  use `dotnet user-secrets`; production MUST use environment
  variables or a secrets manager.
- **Authentication**: All non-health endpoints MUST require a valid
  Auth0 JWT. Token validation MUST verify `issuer`, `audience`,
  and `expiration`.
- **Authorization**: The `AdminOnly` policy MUST be enforced via
  the `[Authorize(Policy = "AdminOnly")]` attribute. Role
  assignment MUST only be possible through admin endpoints.
- **Audit trail**: All entities inheriting `BaseEntity` MUST have
  `CreatedAt`, `UpdatedAt`, `CreatedBy`, and `UpdatedBy`
  auto-populated by `AuditableEntitySaveChangesInterceptor`.
  Manual audit field assignment in handlers is prohibited.
- **Docker support**: The SQL Server dependency MUST be runnable
  via `docker-compose.yml` for local development. Integration
  tests MUST use Testcontainers and NOT depend on a pre-existing
  database.

## Development Workflow & Quality Gates

- **Branching**: Feature work MUST occur on dedicated branches.
  Direct commits to `main` are prohibited.
- **CI pipeline**: GitHub Actions MUST run on every PR:
  1. `dotnet format --verify-no-changes` — formatting gate.
  2. `dotnet build` — compilation gate.
  3. `dotnet test MediaHandler.Tests` — unit test gate.
  4. `dotnet test MediaHandler.IntegrationTests` — integration
     test gate (Docker required).
- **Code review checklist** (every PR):
  - [ ] Clean Architecture dependency rule respected.
  - [ ] New handler has unit tests (success + failure paths).
  - [ ] FluentValidation validator present for new commands.
  - [ ] `ApiResponse<T>` wrapper used in controller responses.
  - [ ] `[ProducesResponseType]` attributes on new/changed actions.
  - [ ] No secrets or PII in code or logs.
  - [ ] EF queries use pagination and `AsNoTracking()` for reads.
- **Commit messages**: MUST follow Conventional Commits
  (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`).
- **Documentation**: README MUST be updated when endpoints,
  configuration, or architecture decisions change.

## Governance

This constitution is the authoritative source of non-negotiable
rules for the MediaHandler.API project. It supersedes informal
conventions and ad-hoc decisions.

- **Amendments**: Any change to this constitution MUST be proposed
  via a PR with a clear rationale. The Sync Impact Report at the
  top of this file MUST be updated to reflect the change.
- **Versioning**: The constitution follows semantic versioning:
  - **MAJOR**: Removal or redefinition of a core principle.
  - **MINOR**: Addition of a new principle or material expansion
    of existing guidance.
  - **PATCH**: Clarifications, typo fixes, non-semantic rewording.
- **Compliance review**: Every feature plan (`plan.md`) MUST
  include a "Constitution Check" section verifying alignment with
  these principles before implementation begins.
- **Precedence**: If a template or workflow document conflicts with
  this constitution, the constitution prevails. The conflicting
  document MUST be updated.

**Version**: 1.0.0 | **Ratified**: 2026-04-06 | **Last Amended**: 2026-04-06

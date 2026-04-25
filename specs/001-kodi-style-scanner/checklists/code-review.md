# Checklist: Code Review — Cross-Cutting Conventions

**Purpose**: Enforce repo-wide architectural conventions on every PR in this feature.
**Scope**: All code added under `MediaHandler.Domain`, `MediaHandler.Application`, `MediaHandler.Infrastructure`, `MediaHandler.API` for feature 001-kodi-style-scanner.
**How to use**: Reviewer ticks before approving. Author self-checks before requesting review.

## API Layer

- [ ] CHK001 - **Every** new endpoint returns `ApiResponse<T>` (success and failure) — no bare DTOs, no raw `IActionResult` returns
- [ ] CHK002 - Every controller carries `[ApiVersion("1.0")]` and routes under `/api/v1/`
- [ ] CHK003 - Every admin controller carries `[Authorize(Policy = "AdminOnly")]` and `[EnableRateLimiting("fixed")]` (cross-ref `admin-authorization-sc008.md`)
- [ ] CHK004 - Endpoints map directly to MediatR commands/queries — no business logic in controllers
- [ ] CHK005 - Route names and verbs match the contracts in `contracts/scan.md`, `contracts/library-roots.md`, `contracts/review-items.md` exactly

## Application Layer (CQRS / Result / Validation)

- [ ] CHK006 - **Every** Command has a corresponding `AbstractValidator<T>` (FluentValidation) and it is registered in DI
- [ ] CHK007 - Handlers return the `Result` / `Result<T>` pattern; **no thrown exceptions for business errors** (only for truly exceptional infrastructure faults)
- [ ] CHK008 - **One handler per file** (CommandHandler, QueryHandler each in their own file)
- [ ] CHK009 - Validators run via the MediatR validation pipeline behavior (not invoked manually in handlers)
- [ ] CHK010 - Read queries use `AsNoTracking()` on the EF query
- [ ] CHK011 - Read queries project to DTOs via `Select(...)` — no entity-graph leakage to API layer

## Domain Layer

- [ ] CHK012 - New entities (`LibraryRoot`, `ScanRun`, `MediaFile`, `Movie`, `Show`, `Season`, `Episode`, `EpisodeFileLink`, `ReviewItem`, `ScanError`) inherit `BaseEntity` (or documented base)
- [ ] CHK013 - Domain entities are POCOs — no EF attributes, no `using Microsoft.EntityFrameworkCore` in `MediaHandler.Domain`
- [ ] CHK014 - Domain enums (`ScanStatus`, `ReviewItemStatus`, `MediaKind`, etc.) live in Domain, not Infrastructure
- [ ] CHK015 - Value objects / invariants enforced in constructors or factory methods, not in handlers

## Infrastructure / EF

- [ ] CHK016 - All EF `IEntityTypeConfiguration<T>` classes live under `MediaHandler.Infrastructure` (not Domain)
- [ ] CHK017 - **Single migration** for the entire feature (T048) — verify `Migrations/` adds exactly one new migration class
- [ ] CHK018 - Indexes added on every FK column AND on every column used in a WHERE/ORDER predicate by the queries in this feature (T038–T047)
- [ ] CHK019 - Filtered unique index on `ScanRun(Status) WHERE Status IN ('Pending','Running')` present (T039)
- [ ] CHK020 - JSON columns (`ReviewItem.Candidates`, T041) configured with the correct EF JSON column mapping
- [ ] CHK021 - DbContext changes only add new `DbSet<T>` properties; no destructive renames
- [ ] CHK022 - No raw SQL strings in handlers (parameterized LINQ only); any required raw SQL lives in a repository in Infrastructure with parameters

## Dependency Injection

- [ ] CHK023 - All new services registered in the appropriate `DependencyInjection.cs` (Application, Infrastructure, or API) — not ad-hoc in `Program.cs`
- [ ] CHK024 - Lifetime is correct (Scoped for DbContext-using services, Singleton only for stateless caches/clients)
- [ ] CHK025 - `IHttpClientFactory` used for the TMDB client (no `new HttpClient()`)

## Logging (T107)

- [ ] CHK026 - Serilog used everywhere — no `Console.WriteLine`, no `ILogger` string interpolation
- [ ] CHK027 - Log messages use **structured properties** (`logger.LogInformation("Scan {ScanRunId} started for root {RootId}", id, rootId)`), NOT `$"..."` interpolation
- [ ] CHK028 - Per-file log lines include `ScanRunId`, `LibraryRootId`, `FilePath` properties
- [ ] CHK029 - Log volume is bounded for 10k-file scans (cross-ref `performance.md`) — no per-file Info-level dump

## R-001 Cross-Reference

- [ ] CHK030 - `gpl-licensing-r001.md` checklist completed for any PR touching `MediaHandler.Infrastructure/Nas/Scanner/`
- [ ] CHK031 - No verbatim Kodi source in this PR — confirmed by reviewer

## Tests

- [ ] CHK032 - New code has unit tests in the matching `tests/*.UnitTests` project
- [ ] CHK033 - Test names follow the repo `MethodOrFeature_Scenario_ExpectedOutcome` convention
- [ ] CHK034 - No `Thread.Sleep` in tests — use `await Task.Delay` only when justified, prefer deterministic awaits
- [ ] CHK035 - No tests committed in `[Skip]` / `[Ignore]` state without an issue link


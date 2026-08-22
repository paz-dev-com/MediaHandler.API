# Architecture Review — MediaHandler API

Date: 2026-08-01
Scope: full solution (`MediaHandler.slnx`) — API, Application, Domain, Infrastructure, Tests, IntegrationTests.

---

## 1. Architecture Overview

Textbook 4-layer Clean Architecture, dependency rule enforced by project references:

```
Domain  ←  Application  ←  Infrastructure  ←  API
```

### Domain (`MediaHandler.Domain`)

- Zero dependencies.
- 17 entities (`Media`, `MediaFile`, `TvSeason`/`TvEpisode`, `User`, plus scanner entities: `ScanRun`, `ScanItemDecision`, `ReviewItem`, `LibraryRoot`, `ExclusionRule`, `StackGroup`, `NfoMetadata`, `EpisodeFileLink`, `EnrichmentRun`…), all inheriting `Common/BaseEntity.cs` (id, audit fields, domain-event list).
- Entities are deliberately anemic — pure data holders; all behavior lives in Application handlers. Invariants (single running scan, unique open review item) are enforced by DB filtered indexes, not domain code.

### Application (`MediaHandler.Application`)

- CQRS via MediatR 14: ~50 commands/queries across 13 feature folders, one folder per use case; record + handler in the same file.
- Every handler returns `Result<T>` (`Common/Models/Result.cs`) and never throws for expected failures.
- One MediatR pipeline behavior: `Common/Behaviors/ValidationBehavior.cs` runs FluentValidation and throws the domain `ValidationException`.
- Handlers query `IApplicationDbContext` (`Common/Interfaces/IApplicationDbContext.cs`) directly using EF Core LINQ operators — the layer pragmatically depends on EF Core (`MediaHandler.Application.csproj:12`).
- Pagination via `PagedResult<T>`; DTOs are records under `Features/<X>/DTOs/` and `Common/DTOs/`.

### Infrastructure (`MediaHandler.Infrastructure`)

- EF Core 9 / SQL Server: `MediaHandlerDbContext` (18 DbSets), 18 `IEntityTypeConfiguration<T>` classes in `Persistence/Configurations/`, 12 migrations, design-time factory.
- Two `SaveChangesInterceptor`s: `AuditableEntitySaveChangesInterceptor` (audit stamping) and `DomainEventDispatchInterceptor` (domain events via MediatR `IPublisher`).
- External integrations: Freebox NAS client (HMAC-SHA1 session auth), TMDB typed client (Bearer token). Both get `.AddStandardResilienceHandler()` (`DependencyInjection.cs:65,112`).
- `Nas/Scanner/ScanPipeline.cs` (~930 lines) — clean-room Kodi-style pipeline: enumerate → exclude → stack → parse → NFO → classify → TMDB-match → fingerprint → persist. Regexes centralized in `KodiRegexCatalog.cs` with `// SOURCE:` citations (R-001 no-GPL rule).
- Options (`NasOptions`, `TmdbOptions`, `OktaOptions`) validated with `ValidateDataAnnotations().ValidateOnStart()`.
- Singleton coordinators (`ScanRunCoordinator`, `EnrichmentCoordinator`) own background-run state and resolve scoped deps via `IServiceScopeFactory`.
- Startup recovery marks crashed `Running` scan/enrichment runs as `Failed`.

### API (`MediaHandler.API`)

- 17 controllers under `api/v1/…`, all returning the `ApiResponse<T>` envelope (`Models/ApiResponse.cs`).
- Admin controllers: class-level `[Authorize(Policy = "AdminOnly")]` + `[EnableRateLimiting("fixed")]`; the policy handler does a deliberate DB lookup so role changes apply immediately.
- Auth: JWT bearer against Auth0 (dev mode swaps in `Identity/DevAuthenticationHandler.cs`).
- Error flow has two channels:
  1. Business failures: `Result.Fail("PREFIX: message")` strings, mapped to status codes in controllers via `StartsWith` (~24 sites).
  2. Validation failures: thrown `ValidationException`, mapped to 400 by `Middleware/GlobalExceptionHandler.cs` (also maps everything else to 500).
- Composition root: `Program.cs` — Serilog, layer DI hooks, CORS, rate limiting, Swagger, health checks; middleware order is correct.

### Tests

- `MediaHandler.Tests`: xunit v3 + FluentAssertions + NSubstitute + EF InMemory via `Common/TestDbContext.cs` (fresh Guid-named DB per class). Naming: `Method_State_Expected`.
- `MediaHandler.IntegrationTests`: Testcontainers MsSQL (`mssql/server:2022-latest`) running the real migration chain; `FakeNasService` + `FixtureBuilder`/`benchmark.yaml` for deterministic scanner E2E; `WebApplicationFactory<Program>` for auth tests.

### What's done well

- Scanner clean-room discipline: 81 `// SOURCE:` citations, matching citations in test theory rows, documented source mapping in `Nas/Scanner/README.md`.
- Persistence quality: per-entity Fluent configs, filtered unique indexes enforcing invariants (e.g. `UX_EnrichmentRuns_Running`), startup recovery for crashed runs.
- Integration tests against real SQL Server with the real migration chain — high fidelity.
- Resilience handlers on both external HTTP clients; options validated at startup.
- Correct middleware ordering and a clean composition root.

---

## 2. Recommendations by Priority

### P0 — Live behavior bugs / security

#### P0-1. Verify and fix possible privilege escalation in `AuthController.Sync`

`MediaHandler.API/Controllers/AuthController.cs:57-61` accepts `body.Roles` from the client and ORs it with JWT roles to compute `isAdmin` passed to `SyncUserCommand`. If `SyncUserCommandHandler` persists that role, any client can claim Admin — and the DB-driven `AdminAuthorizationHandler` would honor it.

**Action:** check `SyncUserCommandHandler`; make the role server-authoritative (derive only from the validated JWT claims, or from the DB).

#### P0-2. Rate limiter is global, not partitioned

`MediaHandler.API/Extensions/ServiceExtensions.cs:73-79` — `AddFixedWindowLimiter("fixed", 100/min)` without a partitioner creates **one shared 100 req/min window for the entire API, all users combined**.

**Action:** use `RateLimitPartition.GetFixedWindowLimiter` partitioned by user id (fall back to client IP).

#### P0-3. Controllers dereference `result.Value` without checking `IsSuccess`

Examples: `MediaController.List` (`MediaController.cs:46-49`), `AdminScanController.GetActiveScan` (`AdminScanController.cs:118`). A handler failure becomes an NRE-driven 500 instead of a mapped 4xx.

**Action:** add the `IsSuccess` check everywhere (best solved together with P1-1 via a shared `ToActionResult()` extension).

---

### P1 — Design-level improvements

#### P1-1. Replace stringly-typed error prefixes with typed errors

`Result.Fail("NOT_FOUND: message")` strings are parsed with `StartsWith` in ~24 places across 6 controllers; `AdminScanController:50` already uses `Contains` instead of `StartsWith`; `AdminScanDecisionsController` has an `ExtractMessage` helper parsing the prefix back out. Renaming a prefix silently breaks status-code mapping.

**Action:** give `Result` an `Error(Code, Message)` record; add one `result.ToActionResult()` extension that maps codes to status codes; delete the per-controller if-chains. This also fixes P0-3 structurally.

#### P1-2. Resolve the dead domain-event scaffolding

The pipeline is fully wired (`BaseEntity.AddDomainEvent` → `DomainEventDispatchInterceptor` → `DomainEventDispatcher` → MediatR publish) but nothing implements `IDomainEvent`, nothing calls `AddDomainEvent`, and no `INotificationHandler<T>` exists. Same for `Domain/Exceptions/NotFoundException.cs` — pattern-matched in `GlobalExceptionHandler`, thrown nowhere.

**Action:** either adopt (domain events fit audit/history well) or delete. Half-built abstractions mislead new readers.

#### P1-3. Decide on AutoMapper

Registered solution-wide (`Application/DependencyInjection.cs:13`) but only 2 profiles exist and only 8 of ~50 handlers inject `IMapper`; everything else does manual `Select` projection.

**Action:** drop AutoMapper and standardize on explicit projections (already the dominant pattern), or commit to it fully. Recommendation: drop it.

#### P1-4. Close unit-test fidelity gaps

- `MediaHandler.Tests/Common/TestDbContext.cs` does not apply the production Fluent configurations (only hand-configures `MediaGenre`'s key) — unit tests are blind to real indexes/converters/constraints. A handler violating a filtered unique index passes in tests and fails in prod.
  **Action:** call `ApplyConfigurationsFromAssembly` in `TestDbContext`.
- `AGENTS.md` advertises `dotnet test --filter Category=Scanner` but no `[Trait("Category", …)]` exists anywhere — the filter matches nothing.
  **Action:** add traits to scanner tests or fix the doc.
- EF.InMemory version drift: 10.0.5 (`MediaHandler.Tests`) vs 10.0.7 (`MediaHandler.IntegrationTests`).

#### P1-5. TMDB client gaps (`Infrastructure/Tmdb/TmdbService.cs`)

- No client-side rate limiting against TMDB's request cap (the resilience handler retries but doesn't throttle).
- `GetTvShowSeasonsAsync` does an N+1 fetch per season (failures logged and skipped).

**Action:** add a simple throttle (e.g. `SemaphoreSlim`-based) and consider batching/parallelism-with-limit for season fetches.

#### P1-6. Freebox session-token cache is ineffective

`FreeboxNasService` is registered scoped and caches `_sessionToken` per instance, so the session is rebuilt on every request/scan scope instead of being reused.

**Action:** move the token cache to a singleton (with locking) or register the service accordingly, keeping the existing 403-retry logic.

---

### P2 — Hygiene / cleanup (single pass)

- `OktaOptions` is actually Auth0 configuration — rename before the misunderstanding fossilizes (`Infrastructure/Options/`, `Program.cs`, config sections).
- `ICurrentUserService` is registered inside `AddApiHealthChecks` (`Extensions/ServiceExtensions.cs:118`) — move to the auth extension.
- `AddProblemDetails()` is registered in `Program.cs` but never used (`GlobalExceptionHandler` writes `ApiResponse`) — remove.
- Two overlapping health endpoints: `MapHealthChecks("/health")` and `HealthController` at `api/v1/health` — keep one.
- Serilog console sink likely registered twice (in code and via `ReadFrom.Configuration` — `Program.cs:18-25` vs `appsettings.json`).
- Stale/misleading docs: `DatabaseInitializer.cs:12` claims it seeds a dev user (no seeding exists); `AGENTS.md` says MediatR 12 (actual: 14.1.0).
- `AuthController` returns `ApiResponse<object>` with generic `"ERROR"` codes instead of `ApiResponse<UserDto>` + prefix discrimination.
- `FilesController` is legacy (superseded by `AdminScanController`) and forces the obsolete `IMediaFileNameParser` to stay registered (`Infrastructure/DependencyInjection.cs:68-70`) — plan removal.
- Feature-folder layout is inconsistent: newer features keep record + handler + validator in one file, older ones split them — standardize on the newer single-file style.
- `MediaHandler.Infrastructure.csproj.Backup.tmp` is committed in the repo — delete.
- Stray typo in `DevAuthenticationHandler.cs:17` doc comment.

---

### Explicit non-goals (recommended to keep as-is)

- **Anemic domain model + logic in handlers**, and **`IApplicationDbContext` exposing `DbSet`s** — for an API of this size these are honest, reasonable tradeoffs. Repository wrappers would add indirection without buying testability beyond what `TestDbContext` already provides.
- **Two-channel error model** (validation via exception, business errors via `Result<T>`) — worth keeping once P1-1 makes the `Result` channel typed; both channels already converge on the same `ApiResponse` envelope.

---

## 3. Suggested Execution Order

1. P0-1 (security check — verify first, then fix if confirmed)
2. P0-2 (rate limiter partition)
3. P1-1 + P0-3 (typed errors + `ToActionResult()` — one refactor)
4. P1-4 (test fidelity)
5. P1-5, P1-6 (external-client robustness)
6. P1-2, P1-3 (dead-code decisions)
7. P2 (hygiene sweep)

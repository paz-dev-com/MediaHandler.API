# MediaHandler API — Clean Architecture Analysis

> **Date:** June 2025 (Updated: July 2025 — rev 3)
> **Branch:** `feature/phase5-6`
> **Analysed against:** Standard Clean Architecture (Domain → Application → Infrastructure → Presentation)

---

## 1. Solution Structure Overview

```
MediaHandler.API/                  ← Presentation layer (ASP.NET Core Web API)
MediaHandler.Application/          ← Application layer (Use Cases, CQRS)
MediaHandler.Infrastructure/       ← Infrastructure layer (EF Core, External APIs)
MediaHandler.Domain/               ← Domain layer (Entities, Enums, Exceptions)
MediaHandler.Tests/                ← Unit tests (xUnit, NSubstitute, FluentAssertions, EF InMemory)
MediaHandler.IntegrationTests/     ← Integration tests (xUnit, Testcontainers.MsSql, real migrations)
```

### Project References

| Project              | References                          | Correct?                                                              |
|----------------------|-------------------------------------|-----------------------------------------------------------------------|
| **Domain**           | *(none)*                            | ✅ Innermost layer, no outward dependencies                            |
| **Application**      | Domain                              | ✅ Depends only on Domain                                              |
| **Infrastructure**   | Domain, Application                 | ✅ Implements interfaces from Application/Domain                       |
| **API**              | Application, Infrastructure         | ✅ Composition root wires everything                                   |
| **Tests**            | Application, Domain                 | ✅ Tests Application layer in isolation (no Infrastructure dependency) |
| **IntegrationTests** | Application, Domain, Infrastructure | ✅ Tests Application + Infrastructure together against real SQL Server |

**Verdict:** ✅ **The dependency graph is correct.** The Dependency Rule is fully respected. Infrastructure no longer has
a `FrameworkReference` to `Microsoft.AspNetCore.App` — that concern has been correctly moved into the API layer.

---

## 2. Domain Layer Analysis

### What's present

- `Common/BaseEntity.cs` — Base entity with `Id`, `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`
- `Entities/` — `Media`, `MediaFile`, `User`, `UserMedia`, `UserEpisode`, `TvSeason`, `TvEpisode`, `WishlistItem`
- `Enums/` — `MediaType`, `UserRole`
- `Exceptions/` — `NotFoundException`, `ValidationException`

### ✅ Strengths

- **Zero NuGet dependencies** — The `.csproj` has no package references. This is textbook clean architecture.
- **File-scoped namespaces** used consistently.
- **Audit fields** present on `BaseEntity` (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`).
- **Nullable reference types** enabled.
- Navigation properties are well-modelled with collections initialised to `new List<T>()`.
- ✅ ~~`ICurrentUserService` lives in Domain~~ — **FIXED.** Moved to `MediaHandler.Application/Common/Interfaces/`.
  Domain has no infrastructure-related interfaces.
- ✅ ~~`BaseEntity.Id` is settable~~ — **FIXED.** `Id` is now `{ get; init; }`, preventing mutation after construction.
- ✅ **Domain events** supported — `BaseEntity` exposes `DomainEvents`, `AddDomainEvent`, and `ClearDomainEvents`;
  `IDomainEvent` marker lives in `Domain/Common/`.

### ⚠️ Remaining Issues & Recommendations

| #  | Severity      | Issue                                         | Detail                                                                                                                                                                                                                                                                                                                       |
|----|---------------|-----------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| D1 | ~~🟡 Medium~~ | ~~**`ICurrentUserService` lives in Domain**~~ | ✅ **FIXED.** Moved to `Application/Common/Interfaces/`; implemented by `API/Identity/CurrentUserService.cs`.                                                                                                                                                                                                                 |
| D2 | 🟡 Medium     | **Anemic domain entities**                    | All entities are pure data bags (get/set properties, no methods). Acceptable for a CRUD-heavy API but worth noting.                                                                                                                                                                                                          |
| D4 | ~~🟢 Low~~    | ~~**`Genres` stored as `string`**~~           | ✅ **FIXED.** `MediaGenre` join table with composite PK `{MediaId, Name}` replaces the comma-separated string; `IX_MediaGenres_Name` index enables DB-level genre filtering in `GetMediaListQueryHandler`.                                                                                                                    |
| D5 | ~~🟢 Low~~    | ~~**No domain events mechanism**~~            | ✅ **FIXED.** `IDomainEvent` (Domain), `IDomainEventNotification : IDomainEvent, INotification` (Application), `IDomainEventDispatcher` interface (Application), `DomainEventDispatcher` + `DomainEventDispatchInterceptor` (Infrastructure) form a complete dispatch pipeline through MediatR after each `SaveChangesAsync`. |

---

## 3. Application Layer Analysis

### What's present

- `Common/Models/` — `Result<T>`, `PagedResult<T>`
- `Common/Interfaces/` — `IApplicationDbContext`, `ITmdbService`, `INasService`, `ICurrentUserService`
- `Common/DTOs/` — `NasDtos`, `TmdbDtos`
- `Common/Behaviors/` — `ValidationBehavior<TRequest, TResponse>`
- `Common/Extensions/` — `CurrentUserExtensions` (shared OktaId→UserId resolution helper)
- `Features/` — Feature-sliced: `Media`, `Auth`, `Admin`, `Tmdb`, `Files`, `Episodes`, `WatchStatus`, `Wishlist`
- `DependencyInjection.cs` — Registers MediatR, FluentValidation, pipeline behaviors

### ✅ Strengths

- **CQRS pattern** implemented correctly — Commands modify state, Queries are read-only.
- **`Result<T>` pattern** — All expected business errors returned as `Result.Fail(...)`. Exceptions reserved for truly
  unexpected failures.
- **Feature-folder organisation** — Each feature gets its own folder with Commands/Queries/DTOs sub-folders.
- **FluentValidation pipeline** — `ValidationBehavior` intercepts requests before handlers.
- **Record types** used for DTOs and CQRS messages.
- **No direct EF dependency in handlers** — Handlers use `IApplicationDbContext` abstraction.
- ✅ ~~`ApiResponse<T>` was in Application~~ — **FIXED.** Moved to `MediaHandler.API/Models/`.
- ✅ ~~`ITmdbService` and TMDB DTOs colocated in `IApplicationDbContext.cs`~~ — **FIXED.**
- ✅ ~~`AdminHandlers.cs` bundled 3 handlers~~ — **FIXED.** Split into one-handler-per-file.
- ✅ ~~`WishlistCommandHandlers.cs` bundled 2 handlers~~ — **FIXED.** Split into separate files.
- ✅ ~~Missing `CreateMediaCommandValidator`~~ — **FIXED.**
- ✅ ~~Inconsistent error handling (`Result` vs exceptions)~~ — **FIXED.** All handlers that previously threw
  `NotFoundException` now return `Result.Fail(...)`. Controllers check the result and return `404 NotFound`.
  `UnauthorizedAccessException` is retained for truly unexpected auth failures.
- ✅ ~~Repeated OktaId→UserId pattern~~ — **FIXED.** Extracted to `CurrentUserExtensions.ResolveUserIdAsync()` in
  `Common/Extensions/`.

### ⚠️ Remaining Issues & Recommendations

| #  | Severity      | Issue                                                      | Detail                                                                                                                                                                                           |
|----|---------------|------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| A6 | ~~🟡 Medium~~ | ~~**Missing `AutoMapper`**~~                               | ✅ **FIXED.** `AutoMapper` 12.0.1 added; `UserMappingProfile` and `WishlistMappingProfile` created; `IMapper` injected into `SyncUser`, `GetCurrentUser`, `GetUsers`, and `GetWishlist` handlers. |
| A8 | 🟡 Medium     | **Application depends on `Microsoft.EntityFrameworkCore`** | Common pragmatic choice for `DbSet<T>` in `IApplicationDbContext`, but strict clean architecture would prefer `IRepository<T>`.                                                                  |

---

## 4. Infrastructure Layer Analysis

### What's present

- `Persistence/MediaHandlerDbContext.cs` — Implements `IApplicationDbContext`
- `Persistence/MediaHandlerDbContextFactory.cs` — Design-time factory for migrations
- `Persistence/AuditableEntitySaveChangesInterceptor.cs` — Populates audit fields automatically
- `Persistence/Configurations/` — 7 `IEntityTypeConfiguration<T>` classes
- `Tmdb/TmdbService.cs` — Implements `ITmdbService`
- `Nas/FreeboxNasService.cs` — Implements `INasService`
- `Options/` — `OktaOptions`, `TmdbOptions`, `NasOptions`
- `DependencyInjection.cs` — Registers all infrastructure services
- `Migrations/` — EF Core migration files

### ✅ Strengths

- **One `IEntityTypeConfiguration<T>` per entity** — Fluent API, no data annotations.
- **Proper indexing** — Composite unique indexes on join tables, unique index on `User.OktaId`/`Email`, covering index
  on `Media.TmdbId`.
- **Options pattern** — `ValidateDataAnnotations().ValidateOnStart()` with `[Required]` attributes.
- **HttpClient factory** — Typed clients for `TmdbService` and `FreeboxNasService`.
- **Resilience** — `AddStandardResilienceHandler()` (Polly v8) configured on both HTTP clients.
- ✅ ~~Audit fields never populated~~ — **FIXED.** `AuditableEntitySaveChangesInterceptor` registered.
- ✅ ~~`FrameworkReference` to `Microsoft.AspNetCore.App`~~ — **FIXED.** `CurrentUserService` moved to the API project (
  `MediaHandler.API/Identity/`). Infrastructure now has no ASP.NET Core framework dependency.
- ✅ ~~Missing Polly~~ — **FIXED.** `Microsoft.Extensions.Http.Resilience` added; `AddStandardResilienceHandler()`
  applied to both HTTP clients.
- ✅ ~~Options classes lack `[Required]` DataAnnotations~~ — **FIXED.** All `required` properties now also carry
  `[Required]` attribute.
- ✅ ~~`WishlistItemConfiguration` index is not unique~~ — **FIXED.** `.IsUnique()` added to `{ UserId, TmdbId }` index.

### ⚠️ Remaining Issues & Recommendations

| #  | Severity   | Issue                               | Detail                                                                                                                                       |
|----|------------|-------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------|
| I2 | ~~🟢 Low~~ | ~~**No structured log enrichers**~~ | ✅ **FIXED.** `Serilog.Enrichers.Environment` added; `.Enrich.WithMachineName()` and `.Enrich.WithEnvironmentName()` applied in `Program.cs`. |

---

## 5. API / Presentation Layer Analysis

### What's present

- `Program.cs` — Composition root with Serilog bootstrap logger and `app.MapHealthChecks("/health")`
- `Extensions/ServiceExtensions.cs` — Auth, Rate Limiting, Swagger, Health Checks, `CurrentUserService` DI
- `Middleware/GlobalExceptionHandler.cs` — Implements `IExceptionHandler`
- `Models/ApiResponse.cs` — `ApiResponse<T>`, `ApiResponse`, `ApiResponseMeta`, `ApiError`
- `Identity/CurrentUserService.cs` — Implements `ICurrentUserService` (moved from Infrastructure)
- `Contracts/` — Request DTOs organised by feature
- `Controllers/` — All 8 controllers with `[ProducesResponseType]` attributes

### ✅ Strengths

- **Thin controllers** — All controllers delegate to MediatR `ISender`.
- **Versioned routes** — All controllers use `api/v1/[controller]`.
- **`[Authorize]` applied consistently** — All controllers except `HealthController`.
- **Admin-only endpoints** — Correct per project guidelines.
- **Rate limiting** — Applied uniformly via `[EnableRateLimiting("fixed")]`.
- **Global exception handler** — Structured `ApiResponse` bodies for all error types.
- **CORS** — Configurable origins from `appsettings.json`.
- **Serilog** — Structured logging with `ReadFrom.Configuration` + `ReadFrom.Services` + rolling file sink.
- **Health checks** — `HealthCheckService` with `AddDbContextCheck<MediaHandlerDbContext>` backing `HealthController`
  and `/health` endpoint.
- ✅ ~~Request DTOs in controller files~~ — **FIXED.** Moved to `Contracts/`.
- ✅ ~~Swagger mixed package versions~~ — **FIXED.** All consistently at 10.1.4.
- ✅ ~~Missing `[ProducesResponseType]` attributes~~ — **FIXED.** All actions annotated with typed response types.
- ✅ ~~`HealthController` returns anonymous object~~ — **FIXED.** Returns `ApiResponse<HealthResponse>` backed by
  `HealthCheckService`.
- **`appsettings.Development.json`** — Not yet created. Only `appsettings.json` exists.

### ⚠️ Remaining Issues & Recommendations

| #  | Severity | Issue                                 | Detail                                                                                                  |
|----|----------|---------------------------------------|---------------------------------------------------------------------------------------------------------|
| P3 | 🟢 Low   | **Missing `Okta.AspNetCore` package** | Uses raw `JwtBearer` directly. This is lighter-weight and functionally correct, but deviates from spec. |

---

## 6. Cross-Cutting Concerns

| Concern                | Status        | Notes                                                                                                                                            |
|------------------------|---------------|--------------------------------------------------------------------------------------------------------------------------------------------------|
| **Authentication**     | ✅ Implemented | JWT Bearer via Okta                                                                                                                              |
| **Authorization**      | ✅ Implemented | `[Authorize]` + `"AdminOnly"` policy                                                                                                             |
| **Validation**         | ✅ Implemented | FluentValidation pipeline behavior                                                                                                               |
| **Error Handling**     | ✅ Unified     | All expected business errors use `Result.Fail`; exceptions reserved for unexpected failures                                                      |
| **Logging**            | ✅ Implemented | Serilog with Console + rolling File sinks; reads from `appsettings.json`                                                                         |
| **Rate Limiting**      | ✅ Implemented | Fixed window, 100 req/min                                                                                                                        |
| **CORS**               | ✅ Implemented | Configurable origins                                                                                                                             |
| **Swagger/OpenAPI**    | ✅ Implemented | JWT security scheme + typed `[ProducesResponseType]` on all actions                                                                              |
| **Resilience (Polly)** | ✅ Implemented | `AddStandardResilienceHandler()` on TMDB and Freebox HTTP clients                                                                                |
| **Health Checks**      | ✅ Implemented | `HealthCheckService` + DB check; exposed on `/health` and `api/v1/health`                                                                        |
| **Unit Tests**         | ✅ Implemented | `MediaHandler.Tests` project with xUnit, NSubstitute, FluentAssertions, EF InMemory                                                              |
| **Integration Tests**  | ✅ Partial     | `MediaHandler.IntegrationTests` with Testcontainers.MsSql; covers Auth sync + Wishlist round-trip. Media CRUD + genre filtering not yet covered. |
| **Domain Events**      | ✅ Implemented | `DomainEventDispatchInterceptor` dispatches `IDomainEventNotification` events via MediatR after `SaveChangesAsync`                               |
| **Audit Trail**        | ✅ Implemented | `AuditableEntitySaveChangesInterceptor` auto-populates audit fields                                                                              |

---

## 7. Summary Scorecard

| Category                       | Score | Notes                                                                                                             |
|--------------------------------|-------|-------------------------------------------------------------------------------------------------------------------|
| **Dependency Rule**            | ⭐⭐⭐⭐⭐ | Perfect — all arrows point inward; Infrastructure no longer has ASP.NET Core FrameworkReference                   |
| **Layer Responsibilities**     | ⭐⭐⭐⭐⭐ | `ApiResponse` in API; `ICurrentUserService` in Application; `CurrentUserService` in API                           |
| **Feature Organisation**       | ⭐⭐⭐⭐⭐ | One handler per file; proper feature-folder structure; shared `CurrentUserExtensions`                             |
| **Code Style & Conventions**   | ⭐⭐⭐⭐⭐ | Consistent file-scoped namespaces, records, nullable types, `init`-only `Id`                                      |
| **EF Configuration**           | ⭐⭐⭐⭐⭐ | Fluent API, unique indexes, audit interceptor, `[Required]` on options                                            |
| **Error Handling**             | ⭐⭐⭐⭐⭐ | Fully unified — `Result` for business errors, exceptions for unexpected failures                                  |
| **Resilience & Observability** | ⭐⭐⭐⭐⭐ | Serilog ✅, Polly ✅, Health checks ✅, enrichers ✅                                                                  |
| **Testing**                    | ⭐⭐⭐⭐⭐ | Unit tests (`MediaHandler.Tests`) + integration tests (`MediaHandler.IntegrationTests` with Testcontainers.MsSql) |
| **Security**                   | ⭐⭐⭐⭐  | Auth, admin roles, rate limiting all in place                                                                     |

---

## 8. Changes Since Previous Analysis

### ✅ Resolved (Must Fix)

1. ~~**Move `ApiResponse<T>`, `ApiError`, `ApiResponseMeta`** from Application → API project~~ — **Done.**
2. ~~**Split `AdminHandlers.cs`** into one-handler-per-file~~ — **Done.**
3. ~~**Split `WishlistCommandHandlers.cs`** into separate files~~ — **Done.**
4. ~~**Extract `ITmdbService` and TMDB DTOs** out of `IApplicationDbContext.cs`~~ — **Done.**

### ✅ Resolved (Should Fix)

5. ~~**Populate `CreatedBy`/`UpdatedBy` audit fields**~~ — **Done.**
6. ~~**Add `CreateMediaCommandValidator`**~~ — **Done.**
7. ~~**Fix Swashbuckle version mismatch**~~ — **Done.**
8. ~~**Unify error strategy**~~ — **Done.** All handlers return `Result.Fail(...)` for expected business errors;
   controllers check the result and return the appropriate HTTP status. `UnauthorizedAccessException` retained for auth
   failures only.
9. ~~**Add `[ProducesResponseType]` attributes**~~ — **Done.** All 8 controllers fully annotated with typed response
   types.

### ✅ Resolved (Nice to Have)

10. ~~**Request DTOs in controller files**~~ — **Done.** Moved to `Contracts/`.
11. ~~**`ICurrentUserService` in Domain**~~ — **Done.** Moved to `Application/Common/Interfaces/`. `CurrentUserService`
    implementation moved to `API/Identity/` removing the Infrastructure `FrameworkReference`.
12. ~~**`BaseEntity.Id` is settable**~~ — **Done.** Now `{ get; init; }`.
13. ~~**Repeated OktaId→UserId pattern**~~ — **Done.** `CurrentUserExtensions.ResolveUserIdAsync()` shared helper in
    `Application/Common/Extensions/`.
14. ~~**Missing Serilog**~~ — **Done.** `Serilog.AspNetCore` added; configured with `ReadFrom.Configuration`,
    `ReadFrom.Services`, Console + rolling File sinks, bootstrap logger in `Program.cs`.
15. ~~**Missing Polly**~~ — **Done.** `Microsoft.Extensions.Http.Resilience` added; `AddStandardResilienceHandler()` on
    both HTTP clients.
16. ~~**No test project**~~ — **Done.** `MediaHandler.Tests` created with xUnit, NSubstitute, FluentAssertions, EF
    InMemory; 10 unit tests covering `SyncUser`, `DeleteMedia`, `AddToWishlist`, `RemoveFromWishlist`, admin commands
    and queries.
17. ~~**`WishlistItemConfiguration` index not unique**~~ — **Done.** `.IsUnique()` added.
18. ~~**`[Required]` on Options classes**~~ — **Done.** All `required` properties carry `[Required]` DataAnnotation.
19. ~~**`HealthController` returns anonymous object**~~ — **Done.** Returns `ApiResponse<HealthResponse>` backed by
    `HealthCheckService` with DB health check.
20. ~~**`Genres` stored as `string`**~~ — **Done.** `MediaGenre` join table added with EF migration
    `AddMediaGenresTable`; `GetMediaListQueryHandler` filters via `m.Genres.Any(g => g.Name == request.Genre)` at DB
    level.
21. ~~**No domain events mechanism**~~ — **Done.** `IDomainEventNotification : IDomainEvent, INotification` in
    Application; `DomainEventDispatcher` + `DomainEventDispatchInterceptor` in Infrastructure dispatch events through
    MediatR after each save.
22. ~~**No integration tests**~~ — **Done.** `MediaHandler.IntegrationTests` project with `Testcontainers.MsSql`; covers
    `SyncUser` and wishlist add/remove/list round-trip against a real SQL Server with applied migrations.
23. ~~**`Serilog.Enrichers.Environment` missing**~~ — **Done.** `.Enrich.WithMachineName()` and
    `.Enrich.WithEnvironmentName()` in `Program.cs`.
24. ~~**AutoMapper missing**~~ — **Done.** `AutoMapper` 12.0.1; `UserMappingProfile` and `WishlistMappingProfile`
    registered via `AddAutoMapper(Assembly.GetExecutingAssembly())`.

---

## 9. Priority Recommendations (Remaining)

### ✅ Completed

1. ~~**Add `Serilog.Enrichers.Environment`**~~ — **Done.**
2. ~~**Add AutoMapper**~~ — **Done.**
3. ~~**Add integration tests**~~ — **Done.** `MediaHandler.IntegrationTests` with Testcontainers.MsSql.
4. ~~**`Genres` as a proper join table**~~ — **Done.**
5. ~~**Domain events mechanism**~~ — **Done.** Full dispatch pipeline via `DomainEventDispatchInterceptor` + MediatR.

### Remaining

1. **Accepted tradeoff — `IRepository<T>` not introduced (A8)** — `IApplicationDbContext` exposes `DbSet<T>`, keeping
   `Microsoft.EntityFrameworkCore` in the Application layer. This is a deliberate pragmatic choice: the query complexity
   of this API (filtering, pagination, multi-level includes) would produce a leaky or verbose repository abstraction
   with little practical benefit. Revisit if the Application layer grows test-isolation requirements that EF InMemory
   cannot satisfy.
2. **`ICurrentUserService` registered inside `AddApiHealthChecks()`** —
   `services.AddScoped<ICurrentUserService, CurrentUserService>()` is placed inside
   `ServiceExtensions.AddApiHealthChecks()`. Health checks and identity are unrelated concerns. Rename to
   `AddApiServices()` or extract a dedicated `AddApiIdentity()` extension.
3. **`appsettings.Development.json` not created** — Only `appsettings.json` exists. A development override file would
   allow lower log levels, relaxed rate limits, and development-specific CORS origins without touching shared config.
4. **Integration test coverage gaps** — `MediaHandler.IntegrationTests` only covers Auth + Wishlist. Key flows not yet
   covered: `DeleteMedia`, `SetWatchStatus`, `ImportFromTmdb`, `ScanNas`, genre-based filtering.
5. **Empty `Identity/` folder in Infrastructure** — `<Folder Include="Identity\" />` remains in
   `MediaHandler.Infrastructure.csproj` after `CurrentUserService` was moved to the API project. Remove to avoid
   confusion.
6. **`.csproj.Backup.tmp` artefact** — `MediaHandler.Infrastructure.csproj.Backup.tmp` should be deleted or added to
   `.gitignore`.

# Architecture & Security Review

## 🏗️ Clean Architecture Analysis

### ✅ What's Done Well

| Area | Assessment |
|---|---|
| **Layer separation** | 4 projects with correct naming: Domain → Application → Infrastructure → API |
| **Dependency direction** | Domain has **zero** dependencies. Application depends only on Domain. Infrastructure depends on Domain + Application. API depends on Application + Infrastructure. ✅ Correct. |
| **Domain purity** | Domain contains only entities, enums, exceptions, value objects, and interface contracts. No framework references. ✅ |
| **Application layer** | Uses MediatR CQRS pattern with `IRequest`/`IRequestHandler`. Uses FluentValidation. Depends on EF Core only via `IApplicationDbContext` abstraction. ✅ |
| **Infrastructure** | Implements `IApplicationDbContext`, `ICurrentUserService`, `INasService`. Keeps Okta/Freebox/EF details out of Application. ✅ |
| **API layer** | Thin controllers that only dispatch MediatR commands/queries. ✅ |
| **Exception handling** | Centralized via `GlobalExceptionHandler` using `IExceptionHandler`. Maps domain exceptions to HTTP status codes without leaking stack traces. ✅ |
| **Options pattern** | Strongly-typed options (`OktaOptions`, `TmdbOptions`, `NasOptions`) with `ValidateDataAnnotations` + `ValidateOnStart`. ✅ |
| **User Secrets** | `UserSecretsId` is configured in API csproj. ✅ |

### ⚠️ Architecture Issues

1. **`Microsoft.EntityFrameworkCore` in Application layer** — `MediaHandler.Application.csproj` has a direct `PackageReference` on `Microsoft.EntityFrameworkCore`. While `IApplicationDbContext` is an abstraction, the Application handlers use concrete EF methods (`AsNoTracking()`, `Include()`, `ToListAsync()`, `AnyAsync()`, etc.) directly. This tightly couples Application to EF Core — a clean architecture purist would use repository abstractions or at least an `IQueryable`-based read interface. **Pragmatically acceptable for small projects**, but worth flagging.

2. **DTOs (`TmdbMediaDto`, `NasFileInfo`) defined in Domain** — `ITmdbService.cs` and `INasService.cs` in `MediaHandler.Domain\Interfaces` define external service DTOs (`TmdbMediaDto`, `TmdbMediaDetailsDto`, `TmdbSeasonDto`, `NasFileInfo`). These are **infrastructure/application concerns**, not domain concepts. They should live in the Application layer (with interfaces staying in Domain or moving to Application).

3. **No `ITmdbService` registration** — Infrastructure DI registers `INasService` → `FreeboxNasService`, but there is no visible `ITmdbService` implementation or registration. The `ImportFromTmdbCommandHandler` and `SearchTmdbQueryHandler` depend on it. This will fail at runtime.

4. **Missing AutoMapper configuration** — `AutoMapper` is referenced in `Application.csproj` but `AddAutoMapper()` is never called in `DependencyInjection.AddApplication()`. There are also no visible `Profile` classes.

5. **Rate limiter defined but never applied to endpoints** — `AddApiRateLimiting()` registers a `"fixed"` named policy, and `UseRateLimiter()` is called, but no controller or endpoint uses `[EnableRateLimiting("fixed")]`. The limiter has no effect.

---

## 🔒 Security Analysis

### ✅ Security Done Well

| Area | Assessment |
|---|---|
| **Authentication** | Okta JWT Bearer with full validation (`ValidateIssuer`, `ValidateAudience`, `ValidateLifetime`, `ValidateIssuerSigningKey`, 30s clock skew). ✅ |
| **Authorization** | All controllers except `HealthController` use `[Authorize]`. Admin endpoints (`AdminController`, `DELETE Media`, `POST scan`) use `[Authorize(Policy = "AdminOnly")]`. ✅ |
| **Admin role policy** | Defined via `RequireRole("Admin")`. ✅ |
| **HTTPS** | `UseHttpsRedirection()` is called. ✅ |
| **Secrets not in code** | `appsettings.json` has empty strings for `ClientSecret`, `ApiKey`, `AppToken`. ✅ |
| **User data isolation** | Wishlist and watch status handlers filter by `_currentUser.UserId`, preventing cross-user access. ✅ |
| **Error masking** | 500 errors return a generic message, not exception details. ✅ |

### 🚨 Security Issues

1. **🔴 CORS: `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`** — The `"AllowAll"` CORS policy permits **any origin** to call the API. This opens the door to CSRF-like attacks from any malicious website when a user's browser has a valid JWT. This should be restricted to your known frontend origin(s).

2. **🔴 NAS path traversal via `basePath`** — `FilesController.Scan` takes `[FromQuery] string? basePath` and passes it directly to `INasService.ScanDirectoryAsync`. Even though it's admin-only, there's **no path validation or whitelisting**. An admin could scan any path on the NAS (path traversal). The `basePath` should be validated against `NasOptions.BasePaths`.

3. **🔴 `CurrentUserService.UserId` always null** — `UserId` tries `Guid.TryParse` on the Okta `sub` claim, but Okta `sub` values are typically opaque strings (e.g., `00u1234abcd`), **not GUIDs**. This means `UserId` will **always be null** for legitimate Okta tokens. Handlers like `SetWatchStatusCommandHandler` and `WishlistCommandHandlers` use `_currentUser.UserId`, which will throw `UnauthorizedAccessException` for every authenticated user.

4. **🟡 Deactivated users can re-sync** — Any authenticated user can call `POST /api/v1/auth/sync`, which auto-creates a user record. There is no check if the user `IsActive == false` (a deactivated user could just re-sync and keep using the system).

5. **🟡 `OktaOptions.ClientSecret` in bound options** — The client secret is bound in the options class and would be available via DI. Ensure it's not inadvertently exposed in logs or error messages.

6. **🟡 Dual role source (Okta claim vs DB)** — `CurrentUserService.IsAdmin` checks the JWT `role` claim, and the `AdminOnly` policy uses `RequireRole("Admin")` which also checks the claim. The database `User.Role` is **never consulted for authorization**. If Okta roles and DB roles diverge, the DB `Role` field becomes cosmetic. Consider either: (a) trusting Okta as the source of truth and removing `User.Role` from DB, or (b) implementing a claims transformation that checks the DB role.

7. **🟡 No `PageSize` upper bound** — Several queries accept `pageSize` with no cap. A malicious user could request `pageSize=1000000` to cause excessive database load.

8. **🟡 HMACSHA1 in `FreeboxNasService`** — SHA1 is cryptographically weak. This is imposed by the Freebox API, so it's not easily fixable, but worth documenting as a known limitation.

---

## 📋 Summary & Recommendations

| Priority | Issue | Recommendation |
|---|---|---|
| 🔴 Critical | CORS `AllowAll` | Restrict to your frontend origin(s) |
| 🔴 Critical | NAS path traversal | Validate `basePath` against `NasOptions.BasePaths` whitelist |
| 🔴 Critical | `UserId` always null (Okta `sub` is not a GUID) | Resolve user by `OktaId` lookup instead of GUID parsing on `sub` |
| 🟡 Medium | Deactivated users can re-sync | Check `IsActive` in `SyncUserCommandHandler` |
| 🟡 Medium | No `PageSize` cap | Add max page size validation (e.g., 100) |
| 🟡 Medium | Rate limiter unused | Add `[EnableRateLimiting("fixed")]` to controllers |
| 🟡 Medium | DTOs in Domain layer | Move `TmdbMediaDto`/`NasFileInfo` to Application |
| 🟡 Low | Missing `ITmdbService` implementation | Add implementation or the feature won't work at runtime |
| 🟡 Low | AutoMapper registered but unused | Remove package or add profiles |
| ℹ️ Info | Dual role source (Okta + DB) | Decide on single source of truth for roles |

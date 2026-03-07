# MediaHandler API

A personal media management API for organising, tracking, and discovering TV shows and films stored on a Freebox NAS.

## Architecture

Clean Architecture with .NET 10:

| Layer | Project | Responsibility |
|-------|---------|---------------|
| **Domain** | `MediaHandler.Domain` | Entities, enums, exceptions, domain events — zero dependencies |
| **Application** | `MediaHandler.Application` | CQRS handlers, DTOs, interfaces, validators, AutoMapper profiles |
| **Infrastructure** | `MediaHandler.Infrastructure` | EF Core, TMDB API client, Freebox NAS client, options |
| **API** | `MediaHandler.API` | ASP.NET Core controllers, auth, middleware, DI composition |
| **Tests** | `MediaHandler.Tests` | Unit tests (xUnit, NSubstitute, EF InMemory) |
| **Integration Tests** | `MediaHandler.IntegrationTests` | Integration tests (xUnit, Testcontainers.MsSql) |

## Technology Stack

- **.NET 10** — runtime
- **ASP.NET Core** — Web API framework
- **Entity Framework Core 9** — ORM with SQL Server
- **MediatR 12** — CQRS pattern
- **FluentValidation 11** — request validation
- **AutoMapper 12** — entity → DTO mapping
- **Serilog** — structured logging (console + rolling file)
- **Auth0 OAuth 2.0** — JWT authentication
- **TMDB API** — media metadata
- **Freebox API** — NAS file system access via local Freebox router
- **Microsoft.Extensions.Http.Resilience** — HTTP client resilience (standard retry + circuit-breaker)

## Implementation Status

### ✅ Phase 1 — Solution Setup
- Solution structure: 4 production projects + 2 test projects
- Project references enforcing Clean Architecture dependency rules
- `.gitignore` configured; no secrets committed
- `appsettings.json` with safe defaults only
- User Secrets initialised for development

### ✅ Phase 2 — Domain Layer
- `BaseEntity` with audit fields (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`) and domain event collection
- `IDomainEvent` marker interface
- Enums: `MediaType` (Film/TvShow), `UserRole` (User/Admin)
- Entities: `User`, `Media`, `MediaFile`, `MediaGenre`, `UserMedia`, `WishlistItem`, `TvSeason`, `TvEpisode`, `UserEpisode`
- Domain exceptions: `NotFoundException`, `ValidationException`

### ✅ Phase 3 — Application Layer
- `Result<T>` / `Result` pattern — no exceptions for business errors
- `PagedResult<T>` for paginated responses
- `ICurrentUserService` interface (implemented in API layer)
- `IDomainEventDispatcher` / `IDomainEventNotification` interfaces
- `CurrentUserExtensions.ResolveUserIdAsync()` — shared OktaId → UserId helper
- AutoMapper profiles: `UserMappingProfile`, `WishlistMappingProfile`
- MediatR, FluentValidation, AutoMapper registered via `DependencyInjection`

### ✅ Phase 4 — Infrastructure Layer
- `MediaHandlerDbContext` implementing `IApplicationDbContext`
- Fluent API entity configurations for all 9 entities
- EF Core migrations: `InitialCreate`, `AddMediaGenresTable`, `MakeMediaFileMediaIdNullable`
- `AuditableEntitySaveChangesInterceptor` — auto-populates audit fields on save
- `DomainEventDispatchInterceptor` — dispatches domain events post-save via MediatR
- Strongly-typed options with `[Required]` DataAnnotations: `OktaOptions`, `TmdbOptions`, `NasOptions`
- **`TmdbService`**: search, movie/TV details, seasons + episodes
- **`FreeboxNasService`**: full Freebox API integration
  - HMAC-SHA1 session authentication against local Freebox router
  - Automatic session token renewal on 403 expiry
  - Directory scanning via `/api/v8/fs/ls/`
  - File info retrieval via `/api/v8/fs/info/`
  - Base64 + URL-encoded path encoding
- `.AddStandardResilienceHandler()` on all HTTP clients

### ✅ Phase 5 — API Layer
- Serilog configured with bootstrap logger, rolling file + console sinks, machine/environment enrichers
- Swagger / OpenAPI with JWT Bearer security definition and `[ProducesResponseType]` on all actions
- Auth0 JWT authentication (`AddApiAuthentication`)
- `AdminOnly` policy for admin-restricted endpoints
- Fixed-window rate limiting — 100 req/min
- Global exception handler (`GlobalExceptionHandler`) — structured `ApiResponse` for all errors
- CORS configured from `appsettings.json`
- `HealthCheckService` with EF Core DB check — exposed at `/health` and `GET /api/v1/health`
- `CurrentUserService` in API layer (reads JWT claims via `IHttpContextAccessor`)

### ✅ Phase 6 — Features
- **Auth**: sync on login, get current user, update language preference
- **Media**: paginated+filtered list (search, type, genre, watched), detail with file paths, create, delete (admin), set watched
- **Media Stats**: collection overview (totals, by type, watched/unwatched, files)
- **Episodes**: seasons with per-episode watch progress, set episode watched
- **TMDB**: search, import by TMDB ID (deduplication by `TmdbId`)
- **Files**: Freebox NAS scan (admin-only) — scanned files are unlinked (`MediaId` nullable) until matched to imported media
- **Wishlist**: paginated list, add, mark as acquired, remove
- **Admin**: paginated user list, set role, enable/disable user

### ✅ Phase 7 — Tests
- **Unit tests** (`MediaHandler.Tests`): `SyncUser`, `DeleteMedia`, `AddToWishlist`, `GetUsers`, `SetUserRole`, `SetUserActive`
- **Integration tests** (`MediaHandler.IntegrationTests`): Auth sync + Wishlist round-trip against real SQL Server via Testcontainers.MsSql

---

## Project Structure

```
MediaHandler.API/
├── MediaHandler.Domain/
│   ├── Common/               BaseEntity (with domain events), IDomainEvent
│   ├── Entities/             User, Media, MediaFile, MediaGenre, UserMedia,
│   │                         WishlistItem, TvSeason, TvEpisode, UserEpisode
│   ├── Enums/                MediaType, UserRole
│   └── Exceptions/           NotFoundException, ValidationException
│
├── MediaHandler.Application/
│   ├── Common/
│   │   ├── Behaviors/        ValidationBehavior<TRequest,TResponse>
│   │   ├── DTOs/             TmdbDtos, NasDtos
│   │   ├── Extensions/       CurrentUserExtensions
│   │   ├── Interfaces/       IApplicationDbContext, ICurrentUserService,
│   │   │                     ITmdbService, INasService,
│   │   │                     IDomainEventDispatcher, IDomainEventNotification
│   │   ├── Mappings/         UserMappingProfile, WishlistMappingProfile
│   │   └── Models/           Result<T>, PagedResult<T>
│   ├── Features/
│   │   ├── Admin/            GetUsers, SetUserRole, SetUserActive
│   │   ├── Auth/             SyncUser, UpdatePreferences, GetCurrentUser
│   │   ├── Episodes/         GetSeasons, SetEpisodeWatched
│   │   ├── Files/            ScanNas
│   │   ├── Media/            GetMediaList, GetMediaById, GetMediaStats,
│   │   │                     CreateMedia, DeleteMedia
│   │   ├── Tmdb/             SearchTmdb, ImportFromTmdb
│   │   ├── WatchStatus/      SetWatchStatus
│   │   └── Wishlist/         GetWishlist, AddToWishlist,
│   │                         MarkWishlistAcquired, RemoveFromWishlist
│   └── DependencyInjection.cs
│
├── MediaHandler.Infrastructure/
│   ├── Nas/                  FreeboxNasService
│   ├── Options/              OktaOptions, TmdbOptions, NasOptions
│   ├── Persistence/
│   │   ├── Configurations/   One IEntityTypeConfiguration<T> per entity (9 files)
│   │   ├── AuditableEntitySaveChangesInterceptor.cs
│   │   ├── DomainEventDispatchInterceptor.cs
│   │   ├── DomainEventDispatcher.cs
│   │   └── MediaHandlerDbContext.cs
│   ├── Tmdb/                 TmdbService
│   └── DependencyInjection.cs
│
├── MediaHandler.API/
│   ├── Contracts/            Request DTOs by feature
│   ├── Controllers/          Health, Auth, Media, Episodes, Tmdb,
│   │                         Files, Wishlist, Admin
│   ├── Extensions/           ServiceExtensions
│   ├── Identity/             CurrentUserService
│   ├── Middleware/           GlobalExceptionHandler
│   ├── Models/               ApiResponse<T>, ApiError, ApiResponseMeta
│   ├── appsettings.json      Safe defaults only — no secrets
│   └── Program.cs
│
├── MediaHandler.Tests/            Unit tests
└── MediaHandler.IntegrationTests/ Integration tests (Testcontainers.MsSql)
```

---

## API Endpoints

| Method | Route | Description | Auth |
|--------|-------|-------------|------|
| `GET` | `/health` | Health check (DB ping) | Public |
| `GET` | `/api/v1/health` | Health check with status response | Public |
| `POST` | `/api/v1/auth/sync` | Sync Auth0 user to local DB on login | User |
| `GET` | `/api/v1/auth/me` | Current user profile | User |
| `PUT` | `/api/v1/auth/preferences` | Update language preference | User |
| `GET` | `/api/v1/media` | List media (page, search, type, genre, watched) | User |
| `GET` | `/api/v1/media/stats` | Collection statistics | User |
| `GET` | `/api/v1/media/{id}` | Media detail with file paths | User |
| `POST` | `/api/v1/media` | Add media to collection | User |
| `DELETE` | `/api/v1/media/{id}` | Remove media | Admin |
| `PUT` | `/api/v1/media/{id}/watched` | Set watch status | User |
| `GET` | `/api/v1/media/{id}/seasons` | TV seasons with per-episode watch progress | User |
| `PUT` | `/api/v1/media/{id}/seasons/{s}/episodes/{e}/watched` | Set episode watched | User |
| `GET` | `/api/v1/tmdb/search` | Search TMDB | User |
| `POST` | `/api/v1/tmdb/import/{tmdbId}` | Import media from TMDB | User |
| `GET` | `/api/v1/wishlist` | Wishlist (paginated) | User |
| `POST` | `/api/v1/wishlist` | Add to wishlist | User |
| `PUT` | `/api/v1/wishlist/{id}/acquired` | Mark wishlist item as acquired | User |
| `DELETE` | `/api/v1/wishlist/{id}` | Remove from wishlist | User |
| `POST` | `/api/v1/files/scan` | Trigger Freebox NAS scan | Admin |
| `GET` | `/api/v1/admin/users` | List all users (paginated) | Admin |
| `PUT` | `/api/v1/admin/users/{id}/role` | Set user role | Admin |
| `PUT` | `/api/v1/admin/users/{id}/active` | Enable / disable user | Admin |

---

## Configuration

`appsettings.json` holds only safe, non-secret values. All secrets are stored outside source control via **User Secrets** (dev) or **Environment Variables** (production).

### User Secrets — Development Setup

```bash
dotnet user-secrets set "Okta:Domain"        "https://dev-xxxxx.eu.auth0.com/" --project MediaHandler.API
dotnet user-secrets set "Okta:ClientId"      "your-client-id"             --project MediaHandler.API
dotnet user-secrets set "Okta:ClientSecret"  "your-client-secret"         --project MediaHandler.API
dotnet user-secrets set "Tmdb:ApiKey"        "your-tmdb-api-key"          --project MediaHandler.API
dotnet user-secrets set "Nas:AppId"          "mediahandler"               --project MediaHandler.API
dotnet user-secrets set "Nas:AppToken"       "your-freebox-app-token"     --project MediaHandler.API
dotnet user-secrets set "Nas:BasePaths:0"    "/Disk/Media/Films"          --project MediaHandler.API
dotnet user-secrets set "Nas:BasePaths:1"    "/Disk/Media/Series"         --project MediaHandler.API
```

### Environment Variables — Production

```sh
OKTA__DOMAIN=https://dev-xxxxx.eu.auth0.com/
OKTA__CLIENTSECRET=your-secret
TMDB__APIKEY=your-key
NAS__APPID=mediahandler
NAS__APPTOKEN=your-freebox-app-token
NAS__BASEPATHS__0=/Disk/Media/Films
NAS__BASEPATHS__1=/Disk/Media/Series
```

### Freebox App Token

The `AppToken` is a one-time token granted by Freebox OS. To obtain it:
1. POST to `http://mafreebox.freebox.fr/api/v8/login/authorize/` with your app info
2. Press `✓` on the Freebox front panel
3. Poll the authorization endpoint until `status` is `granted`
4. Store the returned `app_token` in User Secrets

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server / SQL Server LocalDB
- Auth0 account (with API and Application configured)
- TMDB API key
- Freebox router at `http://mafreebox.freebox.fr`

### Build & Run

```bash
dotnet restore
dotnet build
dotnet run --project MediaHandler.API
```

### Database

```bash
dotnet ef database update --project MediaHandler.Infrastructure --startup-project MediaHandler.API
```

### Run Tests

```bash
# Unit tests (no external dependencies)
dotnet test MediaHandler.Tests

# Integration tests (requires Docker for SQL Server container)
dotnet test MediaHandler.IntegrationTests
```

---

## Development Guidelines

- File-scoped namespaces, primary constructors, `record` types for DTOs
- `#nullable enable` throughout
- EF Core: Fluent API only, one `IEntityTypeConfiguration<T>` per entity
- CQRS: `Result<T>` returns for business errors, one handler per file, validators in separate files
- Domain events: raise via `entity.AddDomainEvent(new MyEvent(...))`, implement `IDomainEventNotification`
- No secrets in source code — User Secrets for dev, environment variables for production

## License

Private project for personal use.


## Implementation Progress

### ✅ Phase 1 — Setup
- [x] Solution structure: Clean Architecture with 4 projects (`Domain`, `Application`, `Infrastructure`, `API`)
- [x] Project references enforcing layer dependency rules
- [x] `.gitignore` configured to exclude secrets and build artifacts
- [x] `appsettings.json` with safe defaults only (no secrets committed)
- [x] User Secrets initialized for development

### ✅ Phase 2 — Domain Layer
- [x] `BaseEntity` with audit fields (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`)
- [x] Enums: `MediaType` (Film/TvShow), `UserRole` (User/Admin)
- [x] Entities: `User`, `Media`, `MediaFile`, `UserMedia`, `WishlistItem`, `TvSeason`, `TvEpisode`, `UserEpisode`
- [x] Domain exceptions: `NotFoundException`, `ValidationException`
- [x] Interfaces: `ITmdbService`, `INasService`, `ICurrentUserService`

### ✅ Phase 3 — Application Layer
- [x] `Result<T>` / `Result` pattern (no exceptions for business errors)
- [x] `ApiResponse<T>` envelope with `ApiResponseMeta` and `ApiError`
- [x] `PagedResult<T>` for paginated responses
- [x] MediatR and FluentValidation registered via `DependencyInjection`

### ✅ Phase 4 — Infrastructure Layer
- [x] `MediaHandlerDbContext` with all `DbSet<T>` properties
- [x] Fluent API entity configurations for all 8 entities (unique indexes, constraints, relations)
- [x] EF Core migration: `InitialCreate`
- [x] `CurrentUserService` resolving user identity from Auth0 JWT claims
- [x] Strongly-typed options: `OktaOptions`, `TmdbOptions`, `NasOptions`
- [x] **`FreeboxNasService`**: Full Freebox API integration
  - HMAC-SHA1 session authentication against local Freebox router
  - Automatic session token renewal on 403 expiry
  - Directory scanning via `/api/v8/fs/ls/`
  - File info retrieval via `/api/v8/fs/info/`
  - Base64 + URL-encoded path encoding

### ✅ Phase 5 — API Layer
- [x] `HealthController` — `GET /api/v1/health`
- [x] Swagger / OpenAPI with JWT Bearer security definition
- [x] CORS configured
- [x] Auth0 JWT authentication middleware (`AddApiAuthentication`)
- [x] Global exception handler middleware (`GlobalExceptionHandler`)
- [x] Rate limiting — fixed window, 100 req/min (`AddApiRateLimiting`)

### ✅ Phase 6 — Features
- [x] `AuthController` — `POST /sync`, `GET /me`, `PUT /preferences`
- [x] `MediaController` — list (paginated, filtered), get by id, create, delete (admin), set watched
- [x] `EpisodesController` — get seasons with episodes, set episode watched
- [x] `TmdbController` — search TMDB, import by TMDB id
- [x] `FilesController` — NAS scan (admin-only)
- [x] `WishlistController` — list, add, remove
- [x] `AdminController` — list users, set role, enable/disable user
- [x] `IApplicationDbContext` — Application-layer interface, implemented by `MediaHandlerDbContext`

---

## Project Structure

```
MediaHandler.API/
├── MediaHandler.Domain/
│   ├── Common/                  BaseEntity
│   ├── Entities/                User, Media, MediaFile, UserMedia,
│   │                            WishlistItem, TvSeason, TvEpisode, UserEpisode
│   ├── Enums/                   MediaType, UserRole
│   ├── Exceptions/              NotFoundException, ValidationException
│   └── Interfaces/              ITmdbService, INasService, ICurrentUserService
│
├── MediaHandler.Application/
│   ├── Common/
│   │   ├── Interfaces/          IApplicationDbContext
│   │   └── Models/              Result<T>, ApiResponse<T>, PagedResult<T>
│   ├── Features/
│   │   ├── Admin/               GetUsers, SetUserRole, SetUserActive
│   │   ├── Auth/                SyncUser, UpdatePreferences, GetCurrentUser
│   │   ├── Episodes/            GetSeasons, SetEpisodeWatched
│   │   ├── Files/               ScanNas
│   │   ├── Media/               GetMediaList, GetMediaById, CreateMedia, DeleteMedia
│   │   ├── Tmdb/                SearchTmdb, ImportFromTmdb
│   │   ├── WatchStatus/         SetWatchStatus
│   │   └── Wishlist/            GetWishlist, AddToWishlist, RemoveFromWishlist
│   └── DependencyInjection.cs
│
├── MediaHandler.Infrastructure/
│   ├── Identity/                CurrentUserService
│   ├── Nas/                     FreeboxNasService
│   ├── Options/                 OktaOptions, TmdbOptions, NasOptions
│   ├── Persistence/
│   │   └── Configurations/      One IEntityTypeConfiguration<T> per entity
│   ├── MediaHandlerDbContext.cs  (implements IApplicationDbContext)
│   └── DependencyInjection.cs
│
└── MediaHandler.API/
    ├── Controllers/             Health, Auth, Media, Episodes, Tmdb,
    │                            Files, Wishlist, Admin
    ├── Extensions/              ServiceExtensions (auth, rate limiting, swagger)
    ├── Middleware/              GlobalExceptionHandler
    ├── appsettings.json         Safe defaults only — no secrets
    └── Program.cs
```

---

## Configuration

`appsettings.json` holds only safe, non-secret values. All secrets are stored outside source control via **User Secrets** (dev) or **Environment Variables** (production).

### User Secrets — Development Setup

```bash
# Okta
dotnet user-secrets set "Okta:Domain"        "https://dev-xxxxx.eu.auth0.com/" --project MediaHandler.API
dotnet user-secrets set "Okta:ClientId"      "your-client-id"             --project MediaHandler.API
dotnet user-secrets set "Okta:ClientSecret"  "your-client-secret"         --project MediaHandler.API

# TMDB
dotnet user-secrets set "Tmdb:ApiKey"        "your-tmdb-api-key"          --project MediaHandler.API

# Freebox NAS
dotnet user-secrets set "Nas:AppId"          "mediahandler"               --project MediaHandler.API
dotnet user-secrets set "Nas:AppToken"       "your-freebox-app-token"     --project MediaHandler.API
dotnet user-secrets set "Nas:BasePaths:0"    "/Disk/Media/Films"          --project MediaHandler.API
dotnet user-secrets set "Nas:BasePaths:1"    "/Disk/Media/Series"         --project MediaHandler.API
```

### Environment Variables — Production

```sh
OKTA__DOMAIN=https://dev-xxxxx.eu.auth0.com/
OKTA__CLIENTSECRET=your-secret
TMDB__APIKEY=your-key
NAS__APPID=mediahandler
NAS__APPTOKEN=your-freebox-app-token
NAS__BASEPATHS__0=/Disk/Media/Films
NAS__BASEPATHS__1=/Disk/Media/Series
```

### Freebox App Token

The `AppToken` is a one-time token granted by the Freebox OS UI. To obtain it:
1. Make a POST to `http://mafreebox.freebox.fr/api/v8/login/authorize/` with your app info
2. The user must physically press the `✓` button on the Freebox front panel
3. Poll the authorization endpoint until `status` is `granted`
4. Copy the returned `app_token` into User Secrets

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server / SQL Server LocalDB
- Auth0 account (with API and Application configured)
- TMDB API key
- Freebox router accessible at `http://mafreebox.freebox.fr`

### Build & Run

```bash
dotnet restore
dotnet build
dotnet run --project MediaHandler.API
```

### Database

```bash
# Apply migration
dotnet ef database update --project MediaHandler.Infrastructure --startup-project MediaHandler.API
```

---

## API Endpoints

| Status | Method | Route | Description | Auth |
|--------|--------|-------|-------------|------|
| ✅ | `GET` | `/api/v1/health` | Health check | Public |
| ✅ | `POST` | `/api/v1/auth/sync` | Sync Auth0 user on login | User |
| ✅ | `GET` | `/api/v1/auth/me` | Current user profile | User |
| ✅ | `PUT` | `/api/v1/auth/preferences` | Update language preference | User |
| ✅ | `GET` | `/api/v1/media` | List media (page, search, type, watched filter) | User |
| ✅ | `GET` | `/api/v1/media/{id}` | Get media detail | User |
| ✅ | `POST` | `/api/v1/media` | Add media to collection | User |
| ✅ | `DELETE` | `/api/v1/media/{id}` | Remove media | Admin |
| ✅ | `PUT` | `/api/v1/media/{id}/watched` | Set watch status | User |
| ✅ | `GET` | `/api/v1/media/{id}/seasons` | Get TV seasons & episodes | User |
| ✅ | `PUT` | `/api/v1/media/{id}/seasons/{s}/episodes/{e}/watched` | Set episode watched | User |
| ✅ | `GET` | `/api/v1/tmdb/search` | Search TMDB | User |
| ✅ | `POST` | `/api/v1/tmdb/import/{tmdbId}` | Import media from TMDB | User |
| ✅ | `GET` | `/api/v1/wishlist` | List wishlist (paginated) | User |
| ✅ | `POST` | `/api/v1/wishlist` | Add to wishlist | User |
| ✅ | `DELETE` | `/api/v1/wishlist/{id}` | Remove from wishlist | User |
| ✅ | `POST` | `/api/v1/files/scan` | Trigger Freebox NAS scan | Admin |
| ✅ | `GET` | `/api/v1/admin/users` | List all users | Admin |
| ✅ | `PUT` | `/api/v1/admin/users/{id}/role` | Set user role | Admin |
| ✅ | `PUT` | `/api/v1/admin/users/{id}/active` | Enable/disable user | Admin |

---

## Development Guidelines

- File-scoped namespaces, primary constructors, `record` types for DTOs
- `#nullable enable` throughout
- EF Core: Fluent API only, one `IEntityTypeConfiguration<T>` per entity
- CQRS: `Result<T>` returns, one handler per file, validators in separate files
- No secrets in source code — User Secrets for dev, env vars for production

## License

Private project for personal use.


## Architecture

Clean Architecture with .NET 10:
- **Domain Layer**: Entities, enums, interfaces, exceptions (no dependencies)
- **Application Layer**: CQRS with MediatR, business logic, DTOs
- **Infrastructure Layer**: EF Core, external services (TMDB, NAS, Auth0)
- **API Layer**: ASP.NET Core Web API controllers

## Technology Stack

- **.NET 10** - Latest LTS runtime
- **ASP.NET Core** - Web API framework
- **Entity Framework Core 10** - ORM with SQL Server
- **MediatR** - CQRS pattern implementation
- **FluentValidation** - Request validation
- **Auth0 OAuth 2.0** - Authentication (configured)
- **TMDB API** - Media metadata (to be configured)

## Features

### Completed ✅
1. **Solution Structure**: Clean Architecture with 4 projects
2. **Domain Entities**: User, Media, MediaFile, UserMedia, WishlistItem, TvSeason, TvEpisode, UserEpisode
3. **Database Configuration**: EF Core with SQL Server
4. **Entity Configurations**: Fluent API configurations for all entities
5. **Common Infrastructure**: Result pattern, ApiResponse wrapper, PagedResult
6. **Health Check Endpoint**: `/api/v1/health`

### In Progress 🔄
- Database migrations
- Authentication setup (Okta)
- External service integrations (TMDB, NAS)

### Planned 📋
- Media management endpoints
- User authentication & preferences
- TMDB search & import
- NAS scanning & indexing
- Wishlist management
- Watch status tracking
- TV show episode tracking
- Admin user management

## Project Structure

```
MediaHandler.API/
├── MediaHandler.Domain/              # Core business entities
│   ├── Common/
│   ├── Entities/
│   ├── Enums/
│   ├── Exceptions/
│   └── Interfaces/
│
├── MediaHandler.Application/         # Business logic & CQRS
│   └── Common/
│       └── Models/
│
├── MediaHandler.Infrastructure/      # Data & external services
│   ├── Identity/
│   └── Persistence/
│       └── Configurations/
│
├── MediaHandler.API/                 # Web API layer
│   └── Controllers/
│
├── MediaHandler.Tests/               # Test projects (planned)
└── docs/                             # Documentation (planned)
```

## Database Schema

### Core Entities
- **Users** - User accounts with Okta ID, email, role (User/Admin)
- **Media** - Films and TV shows with TMDB metadata
- **MediaFiles** - Physical files on NAS storage
- **UserMedia** - Per-user watch status and ratings
- **WishlistItems** - Desired media not yet owned

### TV Show Structure
- **TvSeasons** - Season information
- **TvEpisodes** - Episode details
- **UserEpisodes** - Per-user episode watch tracking

## Configuration

### Required Settings (appsettings.json)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MediaHandler;..."
  },
  "Okta": {
    "Domain": "https://dev-xxxxx.okta.com",
    "ClientId": "your-client-id",
    "Audience": "api://mediahandler"
  },
  "Tmdb": {
    "BaseUrl": "https://api.themoviedb.org/3",
    "ImageBaseUrl": "https://image.tmdb.org/t/p"
  },
  "Nas": {
    "BasePath": "path-to-nas"
  }
}
```

### User Secrets (Development)
```bash
dotnet user-secrets set "Okta:ClientSecret" "your-secret"
dotnet user-secrets set "Tmdb:ApiKey" "your-api-key"
```

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server or SQL Server LocalDB
- Okta Developer account (for authentication)
- TMDB API key (for media metadata)

### Build & Run
```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run API (from root directory)
dotnet run --project MediaHandler.API

# Or run from API directory
cd MediaHandler.API
dotnet run
```

### Create First Migration
```bash
cd MediaHandler.Infrastructure
dotnet ef migrations add InitialCreate --startup-project ../MediaHandler.API
dotnet ef database update --startup-project ../MediaHandler.API
```

## API Endpoints

### Health
- `GET /api/v1/health` - Health check

### Planned Endpoints
- **Auth**: `/api/v1/auth/*`
- **Media**: `/api/v1/media/*`
- **Wishlist**: `/api/v1/wishlist/*`
- **TMDB**: `/api/v1/tmdb/*`
- **Files**: `/api/v1/files/*`
- **Admin**: `/api/v1/admin/*`

## Development Guidelines

### Code Style
- File-scoped namespaces
- Primary constructors where applicable
- `record` types for DTOs
- Nullable reference types enabled
- PascalCase for public members, _camelCase for private fields

### Entity Framework
- Separate `IEntityTypeConfiguration<T>` per entity
- Fluent API over data annotations
- Audit fields: CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
- Indexes on foreign keys and frequently queried fields

### CQRS with MediatR
- Commands return `Result<T>` or `Result`
- Queries are read-only
- One handler per file
- Validators in separate files

## License

Private project for personal use.

## Next Steps

1. Create and apply EF Core migrations
2. Configure Okta authentication
3. Implement TMDB service
4. Implement NAS service
5. Create media management features
6. Add user authentication endpoints
7. Implement admin features

# MediaHandler API

A personal media management API for organizing, tracking, and discovering TV shows and films stored on a Freebox NAS.

## Architecture

Clean Architecture with .NET 10:
- **Domain Layer**: Entities, enums, interfaces, exceptions (no dependencies)
- **Application Layer**: CQRS with MediatR, business logic, DTOs
- **Infrastructure Layer**: EF Core, external services (TMDB, Freebox NAS, Okta)
- **API Layer**: ASP.NET Core Web API controllers

## Technology Stack

- **.NET 10** - Latest LTS runtime
- **ASP.NET Core** - Web API framework
- **Entity Framework Core 9** - ORM with SQL Server
- **MediatR** - CQRS pattern implementation
- **FluentValidation** - Request validation
- **Okta OAuth 2.0** - Authentication
- **TMDB API** - Media metadata
- **Freebox API** - NAS file system access via local Freebox router

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
- [x] `CurrentUserService` resolving user identity from Okta JWT claims
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
- [x] Okta JWT authentication middleware (`AddApiAuthentication`)
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
dotnet user-secrets set "Okta:Domain"        "https://dev-xxxxx.okta.com" --project MediaHandler.API
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
OKTA__DOMAIN=https://dev-xxxxx.okta.com
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
- Okta Developer account
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
| ✅ | `POST` | `/api/v1/auth/sync` | Sync Okta user on login | User |
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
- **Infrastructure Layer**: EF Core, external services (TMDB, NAS, Okta)
- **API Layer**: ASP.NET Core Web API controllers

## Technology Stack

- **.NET 10** - Latest LTS runtime
- **ASP.NET Core** - Web API framework
- **Entity Framework Core 10** - ORM with SQL Server
- **MediatR** - CQRS pattern implementation
- **FluentValidation** - Request validation
- **Okta OAuth 2.0** - Authentication (to be configured)
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

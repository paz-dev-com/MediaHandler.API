# MediaHandler API

A personal media management API for organising, tracking, and discovering TV shows and films stored on a Freebox NAS.

---

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

---

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
│   │   ├── DTOs/             TmdbDtos, NasDtos, ParsedMediaInfo, AutoMatchResult
│   │   ├── Extensions/       CurrentUserExtensions
│   │   ├── Interfaces/       IApplicationDbContext, ICurrentUserService,
│   │   │                     ITmdbService, INasService,
│   │   │                     IMediaFileNameParser, IMediaImportService,
│   │   │                     IMediaAutoMatchService,
│   │   │                     IDomainEventDispatcher, IDomainEventNotification
│   │   ├── Mappings/         UserMappingProfile, WishlistMappingProfile
│   │   └── Models/           Result<T>, PagedResult<T>
│   ├── Features/
│   │   ├── Admin/            GetUsers, SetUserRole, SetUserActive
│   │   ├── Auth/             SyncUser, UpdatePreferences, GetCurrentUser
│   │   ├── Episodes/         GetSeasons, SetEpisodeWatched
│   │   ├── Files/            ScanNas, ScanAndImportNas, AutoImportMediaFiles
│   │   ├── Media/            GetMediaList, GetMediaById, GetMediaStats,
│   │   │                     CreateMedia, DeleteMedia
│   │   ├── Tmdb/             SearchTmdb, ImportFromTmdb
│   │   ├── WatchStatus/      SetWatchStatus
│   │   └── Wishlist/         GetWishlist, AddToWishlist,
│   │                         MarkWishlistAcquired, RemoveFromWishlist
│   └── DependencyInjection.cs
│
├── MediaHandler.Infrastructure/
│   ├── Nas/                  FreeboxNasService, MediaFileNameParser
│   ├── Options/              OktaOptions, TmdbOptions, NasOptions
│   ├── Persistence/
│   │   ├── Configurations/   One IEntityTypeConfiguration<T> per entity (9 files)
│   │   ├── AuditableEntitySaveChangesInterceptor.cs
│   │   ├── DomainEventDispatchInterceptor.cs
│   │   ├── DomainEventDispatcher.cs
│   │   └── MediaHandlerDbContext.cs
│   ├── Services/             MediaImportService, MediaAutoMatchService
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
| `POST` | `/api/v1/files/scan-and-import` | Scan NAS + TMDB auto-match (idempotent) | Admin |
| `POST` | `/api/v1/files/auto-import` | Retry TMDB matching for unlinked files | Admin |
| `GET` | `/api/v1/admin/users` | List all users (paginated) | Admin |
| `PUT` | `/api/v1/admin/users/{id}/role` | Set user role | Admin |
| `PUT` | `/api/v1/admin/users/{id}/active` | Enable / disable user | Admin |

---

## NAS Auto-Import Feature

### Overview

After a NAS scan discovers `MediaFile` records, the auto-import pipeline automatically matches each file against the TMDB API and creates (or retrieves) the corresponding `Media` entity, linking `MediaFile.MediaId` to it.

Three deduplication layers guarantee idempotency:

| Layer | Check | Prevents |
|-------|-------|---------|
| **Scan dedup** | `MediaFile.FilePath` unique index | Duplicate `MediaFile` rows from rescanning |
| **Import dedup** | `Media.TmdbId` check before insert | Duplicate `Media` rows for the same movie/show |
| **Link dedup** | `MediaFile.MediaId != null` filter | Re-processing already-linked files |

### `POST /api/v1/files/scan-and-import`

Combines a full NAS scan with TMDB auto-matching in a single admin request.

**Authorization**: `AdminOnly` policy required.

**Query Parameters** (all optional):

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `basePath` | `string` | `null` (all) | Restrict scan to a specific NAS path |
| `language` | `string` | `"en"` | BCP-47 language tag for TMDB metadata (max 10 chars) |

**Response** `200 OK`:

```json
{
  "success": true,
  "data": {
    "newFiles": 42,
    "existingFiles": 108,
    "totalScanned": 150,
    "foldersFound": 23,
    "matched": 40,
    "skipped": 1,
    "failed": 1,
    "errors": [
      "[/Disk/Media/Films/unknown.mkv] Unable to extract a usable title from the filename."
    ]
  }
}
```

**Response codes**: `200 OK` · `401 Unauthorized` · `403 Forbidden`

### `POST /api/v1/files/auto-import`

Processes only `MediaFile` records where `MediaId IS NULL`, without triggering a new NAS scan. Use this to retry files that were previously skipped or failed.

**Authorization**: `AdminOnly` policy required.

**Query Parameters** (all optional):

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `language` | `string` | `"en"` | BCP-47 language tag for TMDB metadata (max 10 chars) |

**Response** `200 OK`:

```json
{
  "success": true,
  "data": {
    "totalUnlinked": 3,
    "matched": 2,
    "skipped": 1,
    "failed": 0,
    "errors": []
  }
}
```

**Response codes**: `200 OK` · `401 Unauthorized` · `403 Forbidden`

### TMDB Rate Limiting

The auto-match pipeline introduces a **250 ms delay** between consecutive TMDB API calls (~4 req/s). The `HttpClient` registered for TMDB also uses `.AddStandardResilienceHandler()` which automatically retries on `HTTP 429 Too Many Requests` with exponential back-off.

### Filename Parser

The `MediaFileNameParser` supports the following patterns:

| Input path | Title | Year | Type hint |
|------------|-------|------|-----------|
| `/Movies/The.Matrix.1999.1080p.BluRay.mkv` | The Matrix | 1999 | `movie` |
| `/Series/Breaking.Bad/Season 01/S01E01.mkv` | Breaking Bad | — | `tv` |
| `/Films/Inception (2010)/Inception.mkv` | Inception | 2010 | `movie` |
| `/Disk/Media/Inception.mkv` | Inception | — | — |

Path segments like `/Movies/`, `/Films/` → `"movie"` hint; `/Series/`, `/TV/`, `/TV Shows/` → `"tv"` hint.

Files that cannot yield a usable title are counted in `failed` with a descriptive error message.

### Known Limitations (MVP Scope)

- **TV show linking**: `MediaFile` records are linked at the **series** (`Media`) level, not at the individual episode (`TvEpisode`) level. Episode-level linking is planned as a separate feature.
- **Parser coverage**: Common naming conventions are supported (dots, underscores, parenthesised year, `S01E01` patterns). Exotic or regional formats may result in `skipped` or `failed` counts — use `auto-import` to retry after manual review.
- **Language scope**: The `language` parameter applies to TMDB metadata retrieval only. The filename parser is language-agnostic.

---

## Configuration

`appsettings.json` holds only safe, non-secret values. All secrets are stored outside source control via **User Secrets** (dev) or **Environment Variables** (production).

### User Secrets — Development Setup

```bash
dotnet user-secrets set "Okta:Domain"        "https://dev-xxxxx.eu.auth0.com/" --project MediaHandler.API
dotnet user-secrets set "Okta:ClientId"      "your-client-id"                  --project MediaHandler.API
dotnet user-secrets set "Okta:ClientSecret"  "your-client-secret"              --project MediaHandler.API
dotnet user-secrets set "Tmdb:ApiKey"        "your-tmdb-api-key"               --project MediaHandler.API
dotnet user-secrets set "Nas:AppId"          "mediahandler"                    --project MediaHandler.API
dotnet user-secrets set "Nas:AppToken"       "your-freebox-app-token"          --project MediaHandler.API
dotnet user-secrets set "Nas:BasePaths:0"    "/Disk/Media/Films"               --project MediaHandler.API
dotnet user-secrets set "Nas:BasePaths:1"    "/Disk/Media/Series"              --project MediaHandler.API
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
- SQL Server (or Docker for Testcontainers in integration tests)
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
- All admin endpoints require `[Authorize(Policy = "AdminOnly")]`

---

## License

Private project for personal use.

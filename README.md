# MediaHandler API

A personal media management API for organizing, tracking, and discovering TV shows and films stored on a NAS.

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

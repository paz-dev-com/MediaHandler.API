---
agent: speckit.plan
---

# MediaHandler API - Technical Plan

## Technology Stack

### Framework & Runtime
- **Runtime**: .NET 10 (latest LTS features)
- **Framework**: ASP.NET Core Web API
- **Language**: C# 13

### Architecture
- **Pattern**: Clean Architecture (Onion Architecture)
- **Layers**:
    - `MediaHandler.Domain` - Entities, value objects, domain events, interfaces
    - `MediaHandler.Application` - Use cases, DTOs, validators, CQRS handlers
    - `MediaHandler.Infrastructure` - EF Core, external services (TMDB, NAS), Okta
    - `MediaHandler.API` - Controllers, middleware, configuration

### Database
- **DBMS**: SQL Server (LocalDB for development, SQL Server for production)
- **ORM**: Entity Framework Core (Code-First approach)
- **Migrations**: EF Core Migrations with idempotent scripts

### Authentication & Authorization
- **Provider**: Okta OAuth 2.0 / OpenID Connect (Dev account for private deployment)
- **Token Type**: JWT Bearer tokens
- **Authorization**: Policy-based authorization with claims
- **Security Headers**: HSTS, CSP, X-Content-Type-Options, X-Frame-Options

### Libraries (Maintained & Secured Only)

#### Core
| Library | Purpose |
|---------|---------|
| `Microsoft.EntityFrameworkCore.SqlServer` | SQL Server EF Core provider |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT authentication |
| `Okta.AspNetCore` | Okta integration |

#### Application Layer
| Library | Purpose |
|---------|---------|
| `MediatR` | CQRS and mediator pattern |
| `FluentValidation` | Request validation |
| `AutoMapper` | Object mapping |

#### Infrastructure
| Library | Purpose |
|---------|---------|
| `Microsoft.Extensions.Http.Polly` | HTTP resilience policies |
| `Serilog` + `Serilog.Sinks.Console` + `Serilog.Sinks.File` | Structured logging |

#### Testing
| Library | Purpose |
|---------|---------|
| `xUnit` | Test framework |
| `FluentAssertions` | Assertion library |
| `Moq` | Mocking framework |
| `Microsoft.AspNetCore.Mvc.Testing` | Integration testing |
| `Testcontainers` | SQL Server container for integration tests |

#### Security & Quality
| Library | Purpose |
|---------|---------|
| `Microsoft.AspNetCore.RateLimiting` | Rate limiting |
| `Swashbuckle.AspNetCore` | OpenAPI/Swagger documentation |

## Project Structure

```
MediaHandler.API/
├── src/
│   ├── MediaHandler.Domain/
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   ├── Enums/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   └── Interfaces/
│   │
│   ├── MediaHandler.Application/
│   │   ├── Common/
│   │   │   ├── Behaviors/
│   │   │   ├── Interfaces/
│   │   │   └── Mappings/
│   │   ├── Features/
│   │   │   ├── Media/
│   │   │   ├── Users/
│   │   │   ├── Wishlist/
│   │   │   └── Tmdb/
│   │   └── DTOs/
│   │
│   ├── MediaHandler.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── Configurations/
│   │   │   ├── Migrations/
│   │   │   └── Repositories/
│   │   ├── Services/
│   │   │   ├── TmdbService/
│   │   │   └── NasService/
│   │   └── Identity/
│   │
│   └── MediaHandler.API/
│       ├── Controllers/
│       ├── Middleware/
│       ├── Filters/
│       └── Extensions/
│
├── tests/
│   ├── MediaHandler.Domain.Tests/
│   ├── MediaHandler.Application.Tests/
│   ├── MediaHandler.Infrastructure.Tests/
│   └── MediaHandler.API.Tests/
│
└── docs/
```

## Security Requirements (Private Deployment)

### Authentication Flow
1. User authenticates via Okta hosted login
2. Okta issues JWT access token + ID token
3. API validates JWT signature and claims
4. User context extracted from token for authorization

### API Security Checklist
- [ ] All endpoints require authentication (except health check)
- [ ] HTTPS enforced (even locally via dev certificates)
- [ ] Rate limiting configured per user
- [ ] Input validation on all endpoints
- [ ] SQL injection prevention via parameterized queries (EF Core)
- [ ] Secrets stored in User Secrets (dev) / Environment Variables (prod)
- [ ] CORS configured for known origins only
- [ ] Audit logging for sensitive operations
- [ ] Dependency vulnerability scanning in CI

### Okta Configuration
- **Grant Type**: Authorization Code with PKCE
- **Scopes**: `openid`, `profile`, `email`, custom scopes as needed
- **Token Lifetime**: Access token 1 hour, Refresh token 7 days
- **Audience**: API identifier

## Database Design Principles

### Code-First Conventions
- Entity configurations in separate `IEntityTypeConfiguration<T>` classes
- Soft deletes where appropriate (IsDeleted flag)
- Audit fields on all entities (CreatedAt, UpdatedAt, CreatedBy, UpdatedBy)
- Indexes defined in configuration for query optimization

### Naming Conventions
- Tables: PascalCase plural (e.g., `Media`, `Users`, `MediaFiles`)
- Columns: PascalCase (e.g., `TmdbId`, `WatchedAt`)
- Foreign Keys: `{Entity}Id` (e.g., `UserId`, `MediaId`)

## API Design Standards

### Endpoint Conventions
- RESTful resource naming: `/api/v1/media`, `/api/v1/users/{id}/wishlist`
- Versioning via URL path: `/api/v1/`, `/api/v2/`
- Pagination: `?page=1&pageSize=20` with cursor-based option for large sets

### Response Format
```json
{
  "data": { },
  "meta": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 150,
    "totalPages": 8
  },
  "errors": []
}
```

### Error Response Format
```json
{
  "data": null,
  "meta": null,
  "errors": [
    {
      "code": "VALIDATION_ERROR",
      "message": "Title is required",
      "field": "title"
    }
  ]
}
```

## Development Workflow

### Local Development
1. SQL Server LocalDB for database
2. Okta Developer account for authentication
3. User Secrets for sensitive configuration
4. Hot reload enabled

### Configuration Hierarchy
1. `appsettings.json` - Base configuration
2. `appsettings.Development.json` - Dev overrides
3. User Secrets - Sensitive values (Okta secrets, TMDB API key)
4. Environment Variables - Production overrides

## Deployment (Private/Local)

- **Hosting**: Self-hosted on local machine or home server
- **Database**: SQL Server Express or full SQL Server
- **Reverse Proxy**: Optional (Nginx/Caddy for HTTPS termination)
- **Container**: Optional Docker support for easier deployment


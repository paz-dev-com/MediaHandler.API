---
agent: speckit.tasks
---

# MediaHandler API - Task Generation Context

## Project-Specific Task Patterns

When generating tasks for MediaHandler API features, follow these patterns based on the Clean Architecture structure.

### Layer-Based Task Organization

Tasks should be organized respecting the dependency flow:

```
Domain → Application → Infrastructure → API
```

**Domain Layer Tasks** (No dependencies):
- Entity definitions in `src/MediaHandler.Domain/Entities/`
- Value objects in `src/MediaHandler.Domain/ValueObjects/`
- Domain interfaces in `src/MediaHandler.Domain/Interfaces/`
- Domain exceptions in `src/MediaHandler.Domain/Exceptions/`
- Domain events in `src/MediaHandler.Domain/Events/`

**Application Layer Tasks** (Depends on Domain):
- DTOs in `src/MediaHandler.Application/DTOs/`
- CQRS Commands/Queries in `src/MediaHandler.Application/Features/{Feature}/Commands/` or `Queries/`
- Validators in `src/MediaHandler.Application/Features/{Feature}/Validators/`
- Handlers in `src/MediaHandler.Application/Features/{Feature}/Handlers/`
- Mapping profiles in `src/MediaHandler.Application/Common/Mappings/`

**Infrastructure Layer Tasks** (Depends on Domain & Application):
- EF Core configurations in `src/MediaHandler.Infrastructure/Persistence/Configurations/`
- Repository implementations in `src/MediaHandler.Infrastructure/Persistence/Repositories/`
- External service clients in `src/MediaHandler.Infrastructure/Services/`
- Migrations in `src/MediaHandler.Infrastructure/Persistence/Migrations/`

**API Layer Tasks** (Depends on all layers):
- Controllers in `src/MediaHandler.API/Controllers/`
- Middleware in `src/MediaHandler.API/Middleware/`
- Filters in `src/MediaHandler.API/Filters/`
- DI configuration in `src/MediaHandler.API/Extensions/`

### Feature-Specific Patterns

#### Media Management Tasks
```
T0XX [USx] Create Media entity in src/MediaHandler.Domain/Entities/Media.cs
T0XX [USx] Create MediaType enum in src/MediaHandler.Domain/Enums/MediaType.cs
T0XX [USx] Create GetMediaQuery in src/MediaHandler.Application/Features/Media/Queries/GetMedia/
T0XX [USx] Create MediaConfiguration in src/MediaHandler.Infrastructure/Persistence/Configurations/MediaConfiguration.cs
T0XX [USx] Create MediaController in src/MediaHandler.API/Controllers/MediaController.cs
```

#### TMDB Integration Tasks
```
T0XX [USx] Create ITmdbService interface in src/MediaHandler.Domain/Interfaces/ITmdbService.cs
T0XX [USx] Create TmdbMediaDto in src/MediaHandler.Application/DTOs/Tmdb/TmdbMediaDto.cs
T0XX [USx] Create TmdbService in src/MediaHandler.Infrastructure/Services/TmdbService/TmdbService.cs
T0XX [USx] Configure TmdbService with Polly resilience in src/MediaHandler.Infrastructure/Services/TmdbService/
```

#### User & Authentication Tasks
```
T0XX [USx] Create User entity in src/MediaHandler.Domain/Entities/User.cs
T0XX [USx] Configure Okta authentication in src/MediaHandler.API/Extensions/AuthenticationExtensions.cs
T0XX [USx] Create CurrentUserService in src/MediaHandler.Infrastructure/Identity/CurrentUserService.cs
T0XX [USx] Create authorization policies in src/MediaHandler.API/Extensions/AuthorizationExtensions.cs
```

#### NAS Integration Tasks
```
T0XX [USx] Create INasService interface in src/MediaHandler.Domain/Interfaces/INasService.cs
T0XX [USx] Create MediaFile entity in src/MediaHandler.Domain/Entities/MediaFile.cs
T0XX [USx] Create NasService in src/MediaHandler.Infrastructure/Services/NasService/NasService.cs
T0XX [USx] Create ScanNasCommand in src/MediaHandler.Application/Features/MediaFiles/Commands/ScanNas/
```

### Test Task Patterns (When Requested)

#### Unit Tests
```
T0XX [P] [USx] Create Media entity tests in tests/MediaHandler.Domain.Tests/Entities/MediaTests.cs
T0XX [P] [USx] Create GetMediaQueryHandler tests in tests/MediaHandler.Application.Tests/Features/Media/
```

#### Integration Tests
```
T0XX [USx] Create MediaController integration tests in tests/MediaHandler.API.Tests/Controllers/MediaControllerTests.cs
T0XX [USx] Create TmdbService integration tests in tests/MediaHandler.Infrastructure.Tests/Services/TmdbServiceTests.cs
```

### Setup Phase Must Include

1. Solution structure creation with all 4 projects
2. NuGet package installation (EF Core, MediatR, FluentValidation, etc.)
3. DbContext setup with SQL Server configuration
4. Okta authentication configuration
5. Swagger/OpenAPI configuration
6. Serilog logging configuration
7. Global exception handling middleware
8. Base response envelope classes

### Foundational Phase Must Include

1. Base entity class with audit fields (CreatedAt, UpdatedAt, etc.)
2. Common interfaces (IRepository<T>, IUnitOfWork)
3. MediatR pipeline behaviors (validation, logging)
4. AutoMapper base configuration
5. Rate limiting configuration
6. CORS configuration

### Parallelization Rules for MediaHandler

**CAN be parallel [P]**:
- Different entity definitions (User, Media, MediaFile)
- Different DTOs that don't reference each other
- Unit tests for different components
- EF configurations for unrelated entities

**CANNOT be parallel**:
- Entity that depends on another entity (MediaFile depends on Media)
- Handler that uses a service not yet created
- Controller that uses a handler not yet implemented
- Integration tests (may share database state)

### Standard Labels

- `[US1]` - User Authentication & Preferences
- `[US2]` - Media Collection Browsing
- `[US3]` - Watch Status Management
- `[US4]` - TMDB Search & Import
- `[US5]` - Wishlist Management
- `[US6]` - NAS Scanning & Indexing
- `[US7]` - TV Show Episode Tracking

(Actual labels will come from spec.md user stories)

### MVP Recommendation

For MediaHandler, a typical MVP scope would be:
- Phase 1: Setup
- Phase 2: Foundational
- Phase 3: US1 (Authentication) - Required for all other features
- Phase 4: US2 (Media Browsing) - Core value proposition

This provides a working, testable application with the essential functionality.


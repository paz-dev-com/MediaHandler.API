---
agent: speckit.implement
---

# MediaHandler API - Implementation Context

## Project Overview

MediaHandler is a media management API built with:
- **.NET 10** / ASP.NET Core Web API
- **Clean Architecture** (Domain → Application → Infrastructure → API)
- **Entity Framework Core** Code-First with SQL Server
- **Okta OAuth 2.0** for authentication
- **TMDB API** for media metadata

## Implementation Standards

### Code Style Requirements

**C# Conventions:**
- Use file-scoped namespaces
- Use primary constructors where appropriate
- Prefer `record` types for DTOs and value objects
- Use nullable reference types (`#nullable enable`)
- Follow Microsoft naming conventions (PascalCase for public, _camelCase for private fields)

**Entity Framework:**
- Separate `IEntityTypeConfiguration<T>` per entity
- Include audit fields: `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`
- Use Fluent API over data annotations
- Configure indexes in entity configurations

**MediatR/CQRS:**
- One handler per file
- Commands return `Result<T>` or `Result` (no exceptions for business errors)
- Queries are read-only, Commands modify state
- Validators in separate files using FluentValidation

### File Structure Patterns

**Feature Organization (Application Layer):**
```
Features/
├── Media/
│   ├── Commands/
│   │   ├── CreateMedia/
│   │   │   ├── CreateMediaCommand.cs
│   │   │   ├── CreateMediaCommandHandler.cs
│   │   │   └── CreateMediaCommandValidator.cs
│   │   └── UpdateMedia/
│   └── Queries/
│       ├── GetMedia/
│       └── GetMediaList/
```

**Controller Pattern:**
```csharp
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class MediaController : ControllerBase
{
    private readonly ISender _sender;
    
    public MediaController(ISender sender) => _sender = sender;
    
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<MediaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var result = await _sender.Send(new GetMediaQuery(id), ct);
        return result.IsSuccess ? Ok(ApiResponse.Success(result.Value)) : NotFound();
    }
}
```

### Dependency Injection Setup

**Domain Layer:** No DI (pure entities and interfaces)

**Application Layer:**
```csharp
// In ApplicationServiceExtensions.cs
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationAssemblyMarker).Assembly));
services.AddValidatorsFromAssembly(typeof(ApplicationAssemblyMarker).Assembly);
services.AddAutoMapper(typeof(ApplicationAssemblyMarker).Assembly);
```

**Infrastructure Layer:**
```csharp
// In InfrastructureServiceExtensions.cs
services.AddDbContext<MediaHandlerDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
services.AddScoped<ITmdbService, TmdbService>();
services.AddScoped<INasService, NasService>();
services.AddScoped<ICurrentUserService, CurrentUserService>();
```

### Required NuGet Packages

**Domain:** (minimal dependencies)
- None required (pure C#)

**Application:**
- `MediatR`
- `FluentValidation`
- `FluentValidation.DependencyInjectionExtensions`
- `AutoMapper`

**Infrastructure:**
- `Microsoft.EntityFrameworkCore.SqlServer`
- `Microsoft.EntityFrameworkCore.Tools`
- `Microsoft.Extensions.Http.Polly`
- `Serilog.AspNetCore`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`

**API:**
- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `Okta.AspNetCore`
- `Swashbuckle.AspNetCore`
- `Microsoft.AspNetCore.RateLimiting`

### Configuration Patterns

**appsettings.json structure:**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MediaHandler;Trusted_Connection=True;"
  },
  "Okta": {
    "Domain": "https://dev-xxxxx.okta.com",
    "ClientId": "",
    "Audience": "api://mediahandler"
  },
  "Tmdb": {
    "BaseUrl": "https://api.themoviedb.org/3",
    "ImageBaseUrl": "https://image.tmdb.org/t/p"
  },
  "Nas": {
    "BasePath": ""
  }
}
```

**User Secrets (Development):**
- `Okta:ClientSecret`
- `Tmdb:ApiKey`

### Error Handling

**Global Exception Handler:**
```csharp
public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var response = exception switch
        {
            ValidationException ve => ApiResponse.Fail(ve.Errors.Select(e => new ApiError("VALIDATION_ERROR", e.ErrorMessage, e.PropertyName))),
            NotFoundException nf => ApiResponse.Fail(new ApiError("NOT_FOUND", nf.Message)),
            _ => ApiResponse.Fail(new ApiError("INTERNAL_ERROR", "An unexpected error occurred"))
        };
        
        context.Response.StatusCode = exception switch
        {
            ValidationException => 400,
            NotFoundException => 404,
            UnauthorizedAccessException => 401,
            _ => 500
        };
        
        await context.Response.WriteAsJsonAsync(response, ct);
        return true;
    }
}
```

### Testing Standards (When Required)

**Unit Test Pattern:**
```csharp
public class GetMediaQueryHandlerTests
{
    private readonly Mock<IMediaRepository> _repositoryMock = new();
    private readonly GetMediaQueryHandler _handler;
    
    public GetMediaQueryHandlerTests()
    {
        _handler = new GetMediaQueryHandler(_repositoryMock.Object, Mock.Of<IMapper>());
    }
    
    [Fact]
    public async Task Handle_WhenMediaExists_ReturnsMedia()
    {
        // Arrange
        var mediaId = Guid.NewGuid();
        _repositoryMock.Setup(x => x.GetByIdAsync(mediaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Media { Id = mediaId });
        
        // Act
        var result = await _handler.Handle(new GetMediaQuery(mediaId), CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }
}
```

### Phase Execution Notes

**Setup Phase:**
1. Create solution with `dotnet new sln`
2. Create projects with proper references
3. Add NuGet packages per layer
4. Configure DbContext with connection string
5. Setup Serilog in Program.cs
6. Configure Okta authentication
7. Add Swagger with JWT support

**Foundational Phase:**
1. Create `BaseEntity` with audit fields
2. Create `IRepository<T>` interface
3. Create `Result<T>` pattern classes
4. Setup MediatR pipeline behaviors
5. Create `ApiResponse<T>` wrapper
6. Configure CORS and rate limiting

**Feature Phases:**
- Follow the task list order strictly
- Implement tests first if TDD is specified
- Validate compilation after each significant change
- Run `dotnet build` between phases

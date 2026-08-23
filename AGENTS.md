# AGENTS.md — MediaHandler API

Personal media management API (Freebox NAS + TMDB + Auth0). C# / .NET 10 · ASP.NET Core · EF Core 9 · MediatR 12 · FluentValidation · Serilog.

---

## Architecture

Strict 4-layer Clean Architecture — dependency rule enforced at project reference level:

```
Domain  ←  Application  ←  Infrastructure  ←  API
```

No upward references. No domain logic in controllers. No lazy loading anywhere.

| Layer | Project | Key contents |
|-------|---------|--------------|
| Domain | `MediaHandler.Domain` | Entities, enums, exceptions, domain events — zero dependencies |
| Application | `MediaHandler.Application` | MediatR handlers, DTOs, `IApplicationDbContext`, validators, `Result<T>` |
| Infrastructure | `MediaHandler.Infrastructure` | EF Core, Freebox NAS client, TMDB client, migrations |
| API | `MediaHandler.API` | Controllers, `ApiResponse<T>`, auth, middleware |

---

## Essential Commands

```bash
dotnet build
dotnet run --project MediaHandler.API

# Unit tests — no external dependencies
dotnet test MediaHandler.Tests

# Integration tests — requires Docker (Testcontainers.MsSql)
dotnet test MediaHandler.IntegrationTests

# Run only scanner tests
dotnet test --filter Category=Scanner

# Add EF Core migration
dotnet ef migrations add <MigrationName> --project MediaHandler.Infrastructure --startup-project MediaHandler.API

# Apply migrations
dotnet ef database update --project MediaHandler.Infrastructure --startup-project MediaHandler.API
```

Dev mode auto-provides admin JWT via `DevAuthenticationHandler` — no real Auth0 setup needed for local runs.

---

## CQRS Pattern (MediatR)

Every feature = one handler file + one validator file in a dedicated subfolder. The command/query record lives in the same file as the handler.

```
Features/Media/Commands/LinkMediaFile/
├── LinkMediaFileCommandHandler.cs   ← record + handler
└── LinkMediaFileCommandValidator.cs ← AbstractValidator<T>
```

**Handler return type**: always `Result<T>` (never throw for expected failures).

```csharp
public record LinkMediaFileCommand(Guid MediaId, Guid FileId) : IRequest<Result<Unit>>;

public class LinkMediaFileCommandHandler(IApplicationDbContext context)
    : IRequestHandler<LinkMediaFileCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(...)
    {
        if (mediaFile is null)
            return Result.Fail<Unit>("NOT_FOUND: MediaFile not found.");
        ...
        return Result.Success(Unit.Value);
    }
}
```

**Error discrimination in controllers**: string-prefix convention — `"NOT_FOUND"`, `"FILE_ALREADY_LINKED"`, `"MEDIA_NOT_TV_SHOW"`, etc.

```csharp
var error = result.Errors.FirstOrDefault() ?? string.Empty;
if (error.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
    return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));
```

---

## API Response Envelope

All endpoints return `ApiResponse<T>` or `ApiResponse`. Paginated responses include `ApiResponseMeta`.

```csharp
return Ok(ApiResponse<IReadOnlyList<UnlinkedFileDto>>.Success(result.Value.Items, meta));
return NotFound(ApiResponse.Fail(new ApiError("NOT_FOUND", error)));
```

---

## Controller Conventions

- Route prefix: `api/v1/...`
- Admin endpoints: class-level `[Authorize(Policy = "AdminOnly")]` + `[EnableRateLimiting("fixed")]`
- All actions need `[ProducesResponseType<ApiResponse<T>>(StatusCodes.StatusXXX)]` for every status code
- Primary constructor injection of `ISender sender`

```csharp
[ApiController]
[Route("api/v1/admin/media")]
[Authorize(Policy = "AdminOnly")]
[EnableRateLimiting("fixed")]
public class AdminMediaFilesController(ISender sender) : ControllerBase
```

---

## EF Core Rules

- Fluent API only — no data annotations on entities
- One `IEntityTypeConfiguration<T>` per entity in `MediaHandler.Infrastructure/Persistence/Configurations/`
- All reads: `AsNoTracking()`
- No N+1: use `.Include().ThenInclude()` in a single query
- `IApplicationDbContext` (not `MediaHandlerDbContext`) is injected into handlers

---

## Unit Test Pattern

Unit tests use `TestDbContext` (EF InMemory), **not** NSubstitute for DB operations. Test naming: `Method_State_Expected`.

```csharp
public class FileLinkCommandHandlerTests
{
    private readonly TestDbContext _context = TestDbContext.Create(); // isolated in-memory DB per test class

    [Fact]
    public async Task LinkFile_WhenFileIsUnlinked_SetsMediaIdAndReturnsSuccess()
    {
        // arrange: add entities directly to _context
        // act: instantiate handler directly (new LinkMediaFileCommandHandler(_context))
        // assert: FluentAssertions on result + re-query _context to verify side effects
    }
}
```

Each test class gets a fresh `Guid`-named in-memory DB via `TestDbContext.Create()`.

---

## Code Style

- File-scoped namespaces
- Primary constructors for services and handlers
- `record` types for DTOs and commands/queries
- `#nullable enable` throughout
- No secrets in source — User Secrets (dev), env vars (prod)

---

## Scanner (No-GPL Rule — R-001)

`MediaHandler.Infrastructure/Nas/Scanner/` contains the Kodi-style classification pipeline. **Every regex or heuristic constant must have a `// SOURCE:` comment** citing a public, non-GPL source (Kodi wiki, advancedsettings.xml docs, observed behavior). Never copy from Kodi `.cpp`/`.h` files.

```csharp
// SOURCE: Kodi wiki – Video file naming, "File Naming" section
private static readonly Regex YearPattern = new(@"\((\d{4})\)", RegexOptions.Compiled);
```

When a filename is parsed incorrectly, add a `[Theory]` row in `MediaHandler.Tests/Scanner/` (TDD red first), then fix the parser in `MediaHandler.Infrastructure/Nas/Scanner/`.

---

## Feature Delivery Pipeline (Unified Agents — Backend Context)

Unified agents automatically detect whether the request concerns backend, frontend, or both.

### Backend pipeline

1. `analyst` — backend functional spec
2. `architect` — CQRS handlers, DTOs, migrations, endpoints
3. `developer` — implement backend design; run `dotnet build` + `dotnet test MediaHandler.Tests`
4. `code-reviewer` — verify backend correctness; APPROVED or CHANGES_REQUESTED

Unified agents load this repo’s conventions automatically when backend context is detected.

---

## Active Feature Spec

Current active plan: `specs/008-kodi-db-import/plan.md` — adds Kodi video database upload import (path-mapping translation, file linking, idempotent re-import, preview mode, run reports).


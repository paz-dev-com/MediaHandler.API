# Contributing to MediaHandler

## Quick Start

```bash
# Prerequisites
# - .NET 10 SDK
# - Docker (for integration tests via Testcontainers)
# - TMDB API key (user secrets)

dotnet restore
dotnet build
dotnet test MediaHandler.Tests               # unit tests (no Docker needed)
dotnet test MediaHandler.IntegrationTests    # integration tests (Docker required)
dotnet run --project MediaHandler.API        # start the API
```

## Development Guidelines

- File-scoped namespaces, primary constructors, `record` types for DTOs
- `#nullable enable` throughout
- EF Core: Fluent API only, one `IEntityTypeConfiguration<T>` per entity
- CQRS: `Result<T>` returns for business errors, one handler per file
- All admin endpoints require `[Authorize(Policy = "AdminOnly")]`
- All API responses wrapped in `ApiResponse<T>`
- Conventional Commits for commit messages

## Running a NAS Scan Locally

See the [README — Kodi-Style Scanner](README.md#kodi-style-nas-library-scanner) section for
step-by-step instructions.

## Adding a Failing Parser Case

When the scanner misclassifies a file:

1. Identify the parser component responsible:
   - Title/year wrong → `KodiNameParserTests.cs`
   - Should be excluded but isn't (or vice versa) → `ExclusionEvaluatorTests.cs`
   - Stacking not grouped correctly → `StackingDetectorTests.cs`
   - Episode number extraction wrong → `TvEpisodeMatcherTests.cs`

2. Add a `[Theory]` / `[InlineData]` row with the failing input and expected result.

3. Run `dotnet test MediaHandler.Tests` — verify the test fails (red).

4. Implement the fix in `MediaHandler.Infrastructure/Nas/Scanner/`.

5. Run tests again — verify green.

6. **Every new regex must have a `// SOURCE:` comment** (see below).

## No-GPL-Paste Rule (R-001)

All scanner heuristics in `MediaHandler.Infrastructure/Nas/Scanner/` are derived
**clean-room** from publicly documented Kodi behavior.

**Permitted sources:**
- [Kodi wiki — Naming video files](https://kodi.wiki/view/Naming_video_files)
- [Kodi wiki — advancedsettings.xml](https://kodi.wiki/view/Advancedsettings.xml)
- Observed black-box behavior of a local Kodi installation
- Published community naming conventions

**Forbidden:**
- Copying any string, regex, or algorithm verbatim from Kodi GPL-2.0 source code
- Referencing internal `.cpp`/`.h` source files in `// SOURCE:` comments

Every PR touching `Scanner/` must include a source mapping table in the PR description.
See [`MediaHandler.Infrastructure/Nas/Scanner/README.md`](MediaHandler.Infrastructure/Nas/Scanner/README.md)
for the complete checklist.

## Testing

```bash
# Unit tests only
dotnet test MediaHandler.Tests

# Integration tests (requires Docker for SQL Server container)
dotnet test MediaHandler.IntegrationTests

# Run a specific test class
dotnet test MediaHandler.Tests --filter "FullyQualifiedName~KodiNameParserTests"
```

## Project Structure

| Layer | Project | Responsibility |
|-------|---------|---------------|
| Domain | `MediaHandler.Domain` | Entities, enums — zero dependencies |
| Application | `MediaHandler.Application` | CQRS handlers, DTOs, interfaces, validators |
| Infrastructure | `MediaHandler.Infrastructure` | EF Core, TMDB, NAS scanner, Freebox |
| API | `MediaHandler.API` | ASP.NET Core controllers, auth, middleware |
| Tests | `MediaHandler.Tests` | Unit tests (xUnit, NSubstitute) |
| IntegrationTests | `MediaHandler.IntegrationTests` | Integration tests (Testcontainers.MsSql) |


---
agent: speckit.analyze
---

# MediaHandler API - Analysis Context

## Project-Specific Analysis Rules

### Constitution Alignment Checks

When analyzing MediaHandler artifacts, verify alignment with these core principles:

**Code Quality:**
- All public APIs must have XML documentation
- Methods should not exceed 50 lines
- Cyclomatic complexity ≤10 per method

**Testing Standards:**
- Unit test coverage ≥80% for new code
- Integration tests required for: API endpoints, database operations, TMDB service
- Test naming: `MethodName_StateUnderTest_ExpectedBehavior`

**Security (NON-NEGOTIABLE):**
- All endpoints must require authentication (except health check)
- Input validation on all entry points
- No secrets in source code
- Audit logging for sensitive operations

**Performance:**
- API response targets: P50 <100ms, P95 <300ms
- No N+1 queries
- Async/await for all I/O operations

### Domain-Specific Terminology

Watch for terminology consistency across artifacts:

| Canonical Term | Common Variants (Flag as Drift) |
|----------------|--------------------------------|
| `Media` | Movie, Film, Show, Content, Item |
| `MediaFile` | File, MediaItem, StoredMedia |
| `WishlistItem` | Wishlist, WantedMedia, ToWatch |
| `UserMedia` | UserCollection, WatchStatus |
| `TmdbId` | ExternalId, MovieDbId, TmdbRef |
| `WatchStatus` | Watched, Seen, Completed |

### Entity Coverage Checklist

Ensure tasks.md covers all required entities:

**Core Entities:**
- [ ] `User` - Account, preferences, language
- [ ] `Media` - Film/TV show with TMDB reference
- [ ] `MediaFile` - Physical file on NAS
- [ ] `UserMedia` - Per-user watch status, rating
- [ ] `WishlistItem` - Desired media not yet owned

**TV-Specific Entities:**
- [ ] `TvSeason` - Season info for TV shows
- [ ] `TvEpisode` - Episode info with watch tracking

### Integration Coverage Checklist

Ensure tasks.md covers all external integrations:

- [ ] **Okta Authentication** - JWT validation, user claims
- [ ] **TMDB API** - Search, details, images, multi-language
- [ ] **NAS File System** - Scanning, path resolution, folder access
- [ ] **SQL Server** - DbContext, migrations, connection pooling

### API Endpoint Coverage

Verify these endpoint categories are addressed:

| Category | Expected Endpoints |
|----------|-------------------|
| Auth | `/api/v1/auth/me`, `/api/v1/auth/preferences` |
| Media | CRUD + search, filter, sort, paginate |
| TMDB | `/api/v1/tmdb/search`, `/api/v1/tmdb/import/{id}` |
| Wishlist | CRUD for user's wishlist |
| Files | `/api/v1/files/scan`, `/api/v1/files/{id}/path` |
| Episodes | TV show episode tracking |

### Clean Architecture Compliance

**Layer Dependency Violations (CRITICAL):**
- Domain referencing Application, Infrastructure, or API
- Application referencing Infrastructure or API
- Infrastructure referencing API
- Circular dependencies between features

**Expected Project References:**
```
MediaHandler.API → MediaHandler.Application, MediaHandler.Infrastructure
MediaHandler.Infrastructure → MediaHandler.Application, MediaHandler.Domain
MediaHandler.Application → MediaHandler.Domain
MediaHandler.Domain → (none)
```

### Non-Functional Requirement Mapping

Ensure NFRs have corresponding tasks:

| NFR Category | Expected Task Types |
|--------------|--------------------|
| Security | Auth middleware, input validation, rate limiting, CORS |
| Performance | Caching setup, index configuration, async patterns |
| Observability | Serilog configuration, structured logging, health checks |
| Resilience | Polly policies for TMDB, retry logic, circuit breaker |

### Common Analysis Findings for MediaHandler

**Likely Ambiguities:**
- "Fast response times" → Must specify P50/P95 targets
- "Secure authentication" → Must specify Okta + JWT details
- "Complete media info" → Must specify which TMDB fields

**Likely Coverage Gaps:**
- Missing pagination for media list endpoints
- Missing error handling for TMDB API failures
- Missing NAS path validation
- Missing TV episode bulk operations

**Likely Inconsistencies:**
- Film vs Movie terminology
- TvShow vs Series vs Show naming
- Path vs FilePath vs Location for NAS


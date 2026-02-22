---
agent: speckit.taskstoissues
---

# MediaHandler API - Tasks to Issues Context

## Issue Creation Guidelines

When converting MediaHandler tasks to GitHub issues, follow these conventions for consistency and traceability.

## Issue Title Conventions

Format task IDs and descriptions into clear issue titles:

| Task Type | Title Format | Example |
|-----------|--------------|---------|
| Setup | `[Setup] {description}` | `[Setup] Create solution structure with Clean Architecture projects` |
| Domain | `[Domain] {entity/feature}` | `[Domain] Create Media entity with TMDB reference` |
| Application | `[App] {feature} - {operation}` | `[App] Media - Create GetMediaQuery handler` |
| Infrastructure | `[Infra] {service/component}` | `[Infra] Configure TmdbService with Polly resilience` |
| API | `[API] {endpoint/controller}` | `[API] Create MediaController with CRUD endpoints` |
| Test | `[Test] {scope} - {target}` | `[Test] Unit - GetMediaQueryHandler` |

## Issue Labels

Apply these labels based on task characteristics:

### Layer Labels
- `layer:domain` - Domain entities, value objects, interfaces
- `layer:application` - Commands, queries, handlers, validators
- `layer:infrastructure` - EF Core, external services, persistence
- `layer:api` - Controllers, middleware, configuration

### Feature Labels
- `feature:media` - Media collection management
- `feature:auth` - Authentication and authorization
- `feature:tmdb` - TMDB integration
- `feature:nas` - NAS file system integration
- `feature:wishlist` - Wishlist functionality
- `feature:admin` - Admin features

### Type Labels
- `type:setup` - Project setup and configuration
- `type:entity` - Domain entity creation
- `type:endpoint` - API endpoint implementation
- `type:integration` - External service integration
- `type:test` - Test implementation

### Priority Labels
- `priority:critical` - Blocking other tasks
- `priority:high` - Core functionality
- `priority:medium` - Important but not blocking
- `priority:low` - Nice to have

## Issue Body Template

Structure issue bodies consistently:

```markdown
## Description
{Task description from tasks.md}

## Acceptance Criteria
- [ ] {Specific, testable criterion 1}
- [ ] {Specific, testable criterion 2}
- [ ] Code compiles without warnings
- [ ] Follows project coding standards

## Technical Notes
- **File(s)**: `{file path from task}`
- **Layer**: {Domain|Application|Infrastructure|API}
- **Dependencies**: #{issue_number} (if applicable)

## Related
- Task ID: {T0XX}
- User Story: {US-X} (if applicable)
- Spec Section: {§FR-X or §NFR-X} (if applicable)
```

## Dependency Mapping

When tasks have dependencies, reference them in issues:

### MediaHandler Common Dependency Chains

| Downstream Task | Depends On |
|-----------------|------------|
| Application handlers | Domain entities |
| Infrastructure repositories | Domain interfaces |
| API controllers | Application handlers |
| Integration tests | All layers implemented |
| TMDB service | Domain ITmdbService interface |
| NAS service | Domain INasService interface |

### Issue Linking
- Use `Depends on #X` in issue body for hard dependencies
- Use `Related to #X` for soft relationships
- Group related issues under milestones (e.g., "US1: Authentication", "US2: Media Browsing")

## User Story Grouping

Map tasks to user stories for milestone organization:

| User Story | Milestone Name | Scope |
|------------|---------------|-------|
| US1 | Authentication & Preferences | Okta setup, user entity, preferences |
| US2 | Media Collection Browsing | Media entity, queries, controllers |
| US3 | Watch Status Management | UserMedia, status tracking |
| US4 | TMDB Search & Import | TmdbService, search, import |
| US5 | Wishlist Management | WishlistItem, CRUD |
| US6 | NAS Scanning & Indexing | NasService, MediaFile |
| US7 | TV Show Episode Tracking | TvSeason, TvEpisode |
| Admin | Admin Features | User management, system config |

## Issue Sizing Guidelines

Estimate issue size based on task complexity:

| Size Label | Criteria | Example |
|------------|----------|---------|
| `size:xs` | < 1 hour, single file | Create enum, simple DTO |
| `size:s` | 1-2 hours, 1-2 files | Entity + configuration |
| `size:m` | 2-4 hours, 3-5 files | Command + handler + validator |
| `size:l` | 4-8 hours, 5+ files | Full feature slice |
| `size:xl` | > 8 hours | Complex integration, refactor |

## Phase-Based Milestones

Organize issues into phase milestones:

1. **Phase 1: Setup** - Solution structure, packages, configuration
2. **Phase 2: Foundational** - Base classes, common infrastructure
3. **Phase 3+: User Stories** - Feature implementation by story
4. **Final Phase: Polish** - Cross-cutting concerns, documentation

## Notes for MediaHandler

- All issues should reference the Clean Architecture layer
- Security-related issues (`feature:auth`, `feature:admin`) should be marked `priority:high`
- TMDB integration issues should note rate limiting considerations
- NAS-related issues should note path format assumptions (Windows)


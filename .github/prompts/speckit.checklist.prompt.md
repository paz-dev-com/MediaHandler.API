---
agent: speckit.checklist
---

# MediaHandler API - Checklist Context

## Checklist Purpose Reminder

Checklists are **unit tests for requirements** - they validate that requirements are complete, clear, consistent, and measurable. They do NOT test implementation.

## MediaHandler-Specific Checklist Domains

When generating checklists for MediaHandler features, consider these domain-specific focus areas:

### 1. API Requirements (`api.md`)
Focus on API contract completeness and clarity:
- Are endpoint paths, methods, and response codes defined?
- Are request/response schemas specified with all required fields?
- Is pagination defined for list endpoints?
- Are error response formats documented?
- Is API versioning strategy specified?

**Example Items:**
- "Are all Media endpoints defined with HTTP methods and paths? [Completeness]"
- "Is the pagination format (cursor vs offset) specified for list endpoints? [Clarity]"
- "Are 4xx vs 5xx error scenarios distinguished in requirements? [Coverage]"

### 2. Security Requirements (`security.md`)
Focus on security specification completeness:
- Are authentication requirements specified for all endpoints?
- Are authorization rules (who can access what) defined?
- Are admin-specific permissions documented?
- Is input validation scope documented?
- Are audit logging requirements specified?
- Is data privacy (user isolation) addressed?

**Example Items:**
- "Are authorization rules specified for each endpoint? [Completeness, Gap]"
- "Are admin role capabilities and restrictions defined? [Clarity]"
- "Is the JWT validation process documented? [Clarity]"
- "Are audit logging requirements defined for sensitive operations? [Coverage]"

### 3. Data Model Requirements (`data.md`)
Focus on entity and relationship specification:
- Are all entities and their attributes defined?
- Are relationships (1:1, 1:N, N:N) specified?
- Are constraints (unique, required, max length) documented?
- Are audit fields consistently required?
- Is soft delete vs hard delete specified?

**Example Items:**
- "Are all Media entity attributes defined with types? [Completeness, Spec §Data]"
- "Is the relationship between Media and MediaFile cardinality specified? [Clarity]"
- "Are cascade delete behaviors defined for related entities? [Coverage, Gap]"

### 4. Integration Requirements (`integration.md`)
Focus on external service specifications:
- Are TMDB API interactions fully specified?
- Are failure/fallback scenarios documented?
- Is caching strategy defined?
- Are rate limiting considerations addressed?
- Is NAS path handling specified?

**Example Items:**
- "Is TMDB API failure fallback behavior defined? [Coverage, Exception Flow]"
- "Is the caching duration for TMDB metadata specified? [Clarity, Gap]"
- "Are NAS disconnection scenarios addressed? [Coverage, Gap]"

### 5. User Experience Requirements (`ux.md`)
Focus on user-facing behavior specifications:
- Are response format requirements consistent?
- Is multi-language support specified?
- Are loading/error states defined?
- Is pagination UX specified?
- Are empty state scenarios addressed?

**Example Items:**
- "Is the API response envelope structure consistently defined? [Consistency]"
- "Are language fallback rules specified when translation unavailable? [Clarity]"
- "Is zero-results response format defined for search? [Coverage, Edge Case]"

### 6. Performance Requirements (`performance.md`)
Focus on NFR specification quality:
- Are response time targets quantified?
- Are throughput expectations defined?
- Is database query performance addressed?
- Are caching requirements specified?
- Is concurrent user load estimated?

**Example Items:**
- "Are P50/P95/P99 latency targets specified? [Clarity, Spec §NFR]"
- "Is the expected concurrent user count defined? [Measurability, Gap]"
- "Are database indexing requirements specified? [Coverage]"

### 7. Admin & Authorization (`admin.md`)
Focus on admin role and permission specifications:
- Are admin capabilities explicitly defined?
- Are user management operations specified?
- Is role assignment/revocation documented?
- Are admin-only endpoints identified?
- Is admin audit trail specified?

**Example Items:**
- "Are admin role permissions enumerated? [Completeness, Gap]"
- "Is the user management workflow (create/disable/delete) specified? [Coverage]"
- "Are admin actions audit logged per requirements? [Clarity]"

## MediaHandler-Specific Requirement Patterns

### Watch for These Ambiguities
| Vague Requirement | Clarification Question |
|-------------------|----------------------|
| "Fast search" | "Is search latency target quantified? [Ambiguity]" |
| "Complete metadata" | "Are required TMDB fields enumerated? [Completeness]" |
| "Secure access" | "Are authentication requirements fully specified? [Clarity]" |
| "Graceful degradation" | "Is fallback behavior defined for each failure mode? [Coverage]" |
| "Admin privileges" | "Are admin capabilities explicitly listed? [Completeness]" |

### Domain-Specific Gaps to Check
| Gap Area | Checklist Item |
|----------|---------------|
| TV Show structure | "Are season/episode hierarchy requirements complete? [Gap]" |
| Watch status | "Is watch status granularity (binary vs progress) specified? [Clarity]" |
| Multi-user | "Are user data isolation requirements defined? [Coverage]" |
| Admin roles | "Are admin-only operations identified and documented? [Gap]" |
| TMDB sync | "Is metadata refresh frequency specified? [Gap]" |
| NAS paths | "Is path format (absolute/relative) specified? [Clarity]" |

## Checklist Naming Convention

Use these domain-based names for MediaHandler checklists:

| Checklist | Focus Area |
|-----------|------------|
| `api.md` | API contract, endpoints, response formats |
| `security.md` | Auth, authorization, data protection |
| `admin.md` | Admin roles, user management, permissions |
| `data.md` | Entities, relationships, constraints |
| `integration.md` | TMDB, NAS, external services |
| `ux.md` | User-facing behavior, localization |
| `performance.md` | Latency, throughput, caching |
| `code-quality.md` | Architecture compliance, patterns |

## Traceability References

When referencing MediaHandler specs, use these section markers:
- `[Spec §FR-X]` - Functional Requirement
- `[Spec §NFR-X]` - Non-Functional Requirement
- `[Spec §US-X]` - User Story
- `[Spec §Data]` - Data Model section
- `[Plan §Arch]` - Architecture decisions
- `[Plan §Stack]` - Technology stack
- `[Gap]` - Missing requirement
- `[Ambiguity]` - Unclear requirement
- `[Conflict]` - Contradicting requirements


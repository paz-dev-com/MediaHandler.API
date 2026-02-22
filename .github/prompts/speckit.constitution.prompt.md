---
agent: speckit.constitution
---

# Constitution Principles

Create or update the project constitution with the following five core principles:

## I. Code Quality

**Principle Name**: Code Quality First

**Description**:
- All code MUST follow established coding standards and style guides for the project's language/framework
- Functions and methods MUST have single responsibility - no method exceeds 50 lines without justification
- Code MUST be self-documenting with meaningful names; comments explain "why", not "what"
- All public APIs MUST have XML documentation or equivalent
- Cyclomatic complexity MUST not exceed 10 per method without explicit approval
- Dead code, unused imports, and TODO comments MUST be resolved before merge
- Code reviews are MANDATORY - no direct commits to main/protected branches
- Static analysis tools MUST pass with zero warnings on new code

## II. Testing Standards

**Principle Name**: Test-Driven Quality (NON-NEGOTIABLE)

**Description**:
- Unit test coverage MUST be ≥80% for new code, ≥70% for modified code
- TDD approach: Write failing tests → Implement → Refactor (Red-Green-Refactor)
- Integration tests REQUIRED for: API endpoints, database operations, external service integrations
- All tests MUST be deterministic - no flaky tests allowed in CI pipeline
- Test naming convention: `MethodName_StateUnderTest_ExpectedBehavior`
- Mocking MUST be used for external dependencies; no tests should require network access
- Performance regression tests REQUIRED for critical paths
- Tests MUST run in isolation with no shared state between test cases

## III. User Experience Consistency

**Principle Name**: Consistent User Experience

**Description**:
- API responses MUST follow consistent JSON structure with standard envelope (data, errors, metadata)
- Error messages MUST be user-friendly, actionable, and include error codes
- HTTP status codes MUST be used correctly and consistently across all endpoints
- API versioning MUST be maintained; breaking changes require major version bump
- Response times for user-facing operations SHOULD not exceed 200ms (P95)
- Pagination MUST be implemented consistently using cursor-based or offset patterns
- All validation errors MUST return field-level details in standardized format
- Localization support MUST be considered for user-facing strings

## IV. Application Security

**Principle Name**: Security by Design (NON-NEGOTIABLE)

**Description**:
- Input validation MUST occur at all entry points - never trust user input
- Authentication and authorization MUST be implemented on all protected endpoints
- Secrets MUST NEVER be committed to source control; use environment variables or secret managers
- SQL injection, XSS, CSRF protections MUST be verified for all applicable endpoints
- HTTPS MUST be enforced for all production traffic
- Security headers (CORS, CSP, HSTS) MUST be properly configured
- Dependency vulnerabilities MUST be scanned; critical/high severity blocks deployment
- Audit logging REQUIRED for authentication events and sensitive data access
- Principle of least privilege MUST be applied to all service accounts and API permissions

## V. Performance Requirements

**Principle Name**: Performance Excellence

**Description**:
- API endpoint response time targets: P50 <100ms, P95 <300ms, P99 <1s
- Database queries MUST be optimized; no N+1 queries, indexes required for filtered columns
- Async/await patterns MUST be used for I/O-bound operations
- Connection pooling REQUIRED for database and HTTP client connections
- Caching strategy MUST be defined for frequently accessed, rarely changed data
- Memory allocation patterns MUST avoid unnecessary allocations in hot paths
- Bulk operations MUST be used for batch processing (no loops with individual DB calls)
- Performance baselines MUST be established and monitored; regressions block release
- Resource limits (CPU, memory, connections) MUST be defined for all services

## Governance

- This constitution supersedes all other development practices
- Amendments require: documented justification, team review, migration plan for existing code
- All PRs MUST verify compliance with these principles
- Exceptions require explicit approval and documentation with expiration date
- Quarterly reviews to assess principle effectiveness and update as needed


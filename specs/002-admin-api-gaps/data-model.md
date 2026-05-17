# Data Model: Admin Dashboard Backend API Gaps

**Feature**: 002-admin-api-gaps  
**Date**: 2025-07-17

## Overview

No new entities or schema changes are required. This feature adds API surface to existing entities and extends one domain enum. All three capabilities operate on entities that already have the necessary fields.

## Existing Entities (referenced, not modified)

### LibraryRoot

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | `Guid` | PK, auto-generated | From `BaseEntity` |
| Path | `string` | Required, unique index | Canonical NAS path |
| Kind | `LibraryRootKind` | Required, enum (Movie, TvShow) | Content type |
| Label | `string?` | Optional | Admin-friendly name |
| **IsEnabled** | `bool` | Default `true` | **Target of US1 toggle endpoint** |
| CreatedAt | `DateTime` | Auto-set | From `BaseEntity` |
| UpdatedAt | `DateTime?` | Auto-set on update | From `BaseEntity` |
| CreatedBy | `string?` | Auto-set | From `BaseEntity` |
| UpdatedBy | `string?` | Auto-set on update | From `BaseEntity` |

**Relationships**: `MediaFiles` (one-to-many) — not affected by this feature.

**Validation rules for toggle**:
- `Id` must be non-empty GUID
- `IsEnabled` must be a valid boolean
- Reject if scan with `Status == Running` references the root (409 Conflict)

---

### ScanRun

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | `Guid` | PK, auto-generated | From `BaseEntity` |
| Mode | `ScanMode` | Required, enum | Full or Incremental |
| Status | `ScanStatus` | Required, enum | Pending/Running/Completed/Failed/Cancelled |
| **StartedAt** | `DateTime` | Indexed DESC | **ORDER BY column for US2 pagination** |
| FinishedAt | `DateTime?` | Nullable | Set on terminal state |
| FailureReason | `string?` | Nullable | Populated on Failed |
| LibraryRootIdsJson | `string` | Default `"[]"` | Denormalized root ids |
| TotalDiscovered | `int` | Default 0 | Summary counter |
| Added | `int` | Default 0 | Summary counter |
| Updated | `int` | Default 0 | Summary counter |
| Unchanged | `int` | Default 0 | Summary counter |
| Removed | `int` | Default 0 | Summary counter |
| Excluded | `int` | Default 0 | Summary counter |
| NeedsReview | `int` | Default 0 | Summary counter |

**Validation rules for list query**:
- `page` must be ≥ 1
- `pageSize` must be 1–100 (capped at 100)

---

### ReviewItem

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | `Guid` | PK | From `BaseEntity` |
| FilePath | `string` | Required | Unique partial index on Open status |
| Reason | `ReviewReason` | Required, enum | Why flagged |
| Status | `ReviewStatus` | Required, enum | Open/Resolved/Dismissed |
| ParsedTitle | `string?` | Nullable | From filename/NFO |
| ParsedYear | `int?` | Nullable | From filename/NFO |
| ParsedSeason | `int?` | Nullable | TV only |
| ParsedEpisode | `int?` | Nullable | TV only |
| CandidatesJson | `string` | Default `"[]"` | TMDB candidate array |
| **ResolvedTmdbId** | `int?` | Nullable | **Cleared on Reopen** |
| **ResolvedKind** | `MediaType?` | Nullable | **Cleared on Reopen** |
| **ResolvedAt** | `DateTime?` | Nullable | **Cleared on Reopen** |
| **ResolvedBy** | `string?` | Nullable | **Cleared on Reopen** |
| FirstSeenScanRunId | `Guid?` | Nullable | Diagnostic link |

**State transitions for Reopen (US3)**:
```
Resolved ──[Reopen]──→ Open  (clear resolution fields)
Dismissed ─[Reopen]──→ Open  (clear resolution fields)
Open ──────[Reopen]──→ ERROR  (409 REVIEW_ALREADY_OPEN)
```

---

## Enum Modification

### ReviewResolutionAction (Domain)

**Current values**: `Assign`, `Dismiss`, `Delete`  
**New value**: `Reopen`

```csharp
public enum ReviewResolutionAction
{
    Assign,   // Map to TMDB id → status = Resolved
    Dismiss,  // Acknowledge without mapping → status = Dismissed
    Delete,   // Remove MediaFile → status = Dismissed
    Reopen    // Revert to Open, clear resolution fields
}
```

**Impact**: The `ResolveReviewItemCommandValidator` must update its `IsInEnum()` message to include `Reopen`. The handler's `switch` statement must add a `Reopen` case.

## DTOs (existing, reused as-is)

| DTO | Used By | Notes |
|-----|---------|-------|
| `LibraryRootDto` | US1 response | Already has `IsEnabled` field |
| `ScanRunDto` | US2 list items | Maps directly from `ScanRun` |
| `ScanCountsDto` | US2 (nested in `ScanRunDto`) | Summary counters |
| `ReviewItemDto` | US3 response | Already has resolution fields |

## New Request DTOs

| DTO | Endpoint | Fields |
|-----|----------|--------|
| `ToggleLibraryRootEnabledRequest` | US1 PUT | `bool IsEnabled` |

No new response DTOs needed — all responses use existing DTOs.


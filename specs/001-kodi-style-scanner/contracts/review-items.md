# Admin Review Items API Contracts

**Feature**: Kodi-Style NAS Library Scanner
**All endpoints**: `[Authorize(Policy = "AdminOnly")]`, `[EnableRateLimiting("fixed")]`, `/api/v1/admin/review-items*`.
**All responses**: wrapped in `ApiResponse<T>` per constitution III.

---

## GET /api/v1/admin/review-items

List items in the review queue. Default behavior: only `Open` items.

### Query parameters

- `status` (`ReviewStatus?`, default `Open`) — `Open` | `Resolved` | `Dismissed`.
- `page` (int, default 1)
- `pageSize` (int, default from configuration, max 100)
- `reason` (`ReviewReason?`) — optional filter.
- `scanRunId` (Guid?) — if provided, only items first surfaced by that run.

### Response — 200 — `ApiResponse<IReadOnlyList<ReviewItemDto>>` with `ApiResponseMeta` for pagination

```csharp
public record ReviewItemDto(
    Guid Id,
    string FilePath,
    ReviewReason Reason,
    ReviewStatus Status,
    string? ParsedTitle,
    int? ParsedYear,
    int? ParsedSeason,
    int? ParsedEpisode,
    IReadOnlyList<TmdbCandidateDto> Candidates,
    int? ResolvedTmdbId,
    MediaType? ResolvedKind,
    DateTime? ResolvedAt,
    DateTime CreatedAt);

public record TmdbCandidateDto(
    int TmdbId,
    MediaType Kind,
    string Title,
    int? Year,
    decimal? Score,           // popularity-derived
    string? PosterPath);
```

| Code | When |
|---|---|
| 200 | Always. |
| 401 / 403 | Auth / role. |

---

## POST /api/v1/admin/review-items/{id:guid}/resolve

Manually resolve a review item by attaching a TMDB id, OR by dismissing.

### Request body — `ResolveReviewRequest`

```csharp
public record ResolveReviewRequest(
    ReviewResolutionAction Action,   // Assign | Dismiss | Delete
    int? TmdbId,                     // required when Action = Assign
    MediaType? Kind);                // required when Action = Assign
```

`ReviewResolutionAction` is a new enum (`Assign | Dismiss | Delete`).

**Validator** (`ResolveReviewItemCommandValidator`):

- ReviewItem exists AND `Status = Open`.
- When `Action = Assign`: `TmdbId > 0`, `Kind` ∈ enum.
- When `Action = Delete`: physically removes the underlying `MediaFile`(s)
  AND any orphaned parent `Media`/`TvSeason`/`TvEpisode`. The
  ReviewItem transitions to `Resolved` with `ResolvedTmdbId = null`.

### Behavior

- **Assign**: pipeline re-runs only the TMDB-resolution stage for the
  given `FilePath`, using the supplied id. On success, persists the
  `Media` (or maps the episode) and sets `Status = Resolved`,
  `ResolvedTmdbId = tmdbId`, `ResolvedKind = kind`,
  `ResolvedBy = currentUserId`, `ResolvedAt = UtcNow`. Future scans
  honor the resolution: a `MediaFile` whose path matches a previously
  `Resolved` ReviewItem is mapped via the saved id without re-querying
  TMDB by title.
- **Dismiss**: marks `Status = Dismissed`. The file remains unmapped
  but is suppressed from the open review queue. Future scans MAY
  re-surface a new ReviewItem if the file's fingerprint changes
  (i.e., dismiss is a "this fingerprint is uninteresting" signal).
- **Delete**: see above.

### Response — 200 — `ApiResponse<ReviewItemDto>`

| Code | When |
|---|---|
| 200 | Resolved / dismissed / deleted. |
| 400 | Validation failure (e.g., Assign without `TmdbId`). |
| 401 / 403 | Auth / role. |
| 404 | No ReviewItem with that id. |
| 409 | ReviewItem is not `Open`. Body: `ApiError("REVIEW_ALREADY_RESOLVED", ...)`. |
| 422 | TMDB lookup of supplied id failed (no such id). Body: `ApiError("TMDB_ID_NOT_FOUND", ...)`. |


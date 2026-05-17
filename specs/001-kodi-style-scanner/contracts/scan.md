# Admin Scan API Contracts

**Feature**: Kodi-Style NAS Library Scanner
**All endpoints**: `[Authorize(Policy = "AdminOnly")]`, `[EnableRateLimiting("fixed")]`, `/api/v1/admin/scan*`.
**All responses**: wrapped in `ApiResponse<T>` per constitution III.

---

## POST /api/v1/admin/scan

Start a new scan run. Returns `202 Accepted` with the freshly created
`ScanRun` summary. The scan executes in the background; clients poll
`GET /api/v1/admin/scan/{id}` for progress.

### Request body — `StartScanRequest`

```csharp
public record StartScanRequest(
    Guid[] LibraryRootIds,   // empty array → scan ALL enabled roots
    ScanMode Mode);          // Full | Incremental
```

**Validator** (`StartScanCommandValidator`):

- `LibraryRootIds` MUST be distinct.
- Every id in `LibraryRootIds` MUST reference an existing, enabled `LibraryRoot`.
- `Mode` ∈ enum.

### Response — 202 — `ApiResponse<ScanRunSummaryResponse>`

```csharp
public record ScanRunSummaryResponse(
    Guid Id,
    ScanMode Mode,
    ScanStatus Status,             // typically Pending right after start
    DateTime StartedAt,
    DateTime? FinishedAt,
    Guid[] LibraryRootIds,
    ScanCountsDto Counts);

public record ScanCountsDto(
    int TotalDiscovered,
    int Added,
    int Updated,
    int Unchanged,
    int Removed,
    int Excluded,
    int NeedsReview);
```

### Status codes

| Code | When |
|---|---|
| 202 | Scan accepted and queued. |
| 400 | Validation failure (`ApiError[]` with `field`). |
| 401 | No / invalid JWT. |
| 403 | JWT but not in `Admin` role. |
| 409 | Another scan is already `Running`. Body: `ApiError("SCAN_IN_PROGRESS", "...", null)`. |

---

## GET /api/v1/admin/scan/{id:guid}

Fetch a single scan run with summary counters and (optionally) the
needs-review list.

### Query parameters

- `includeReview` (bool, default `false`) — if true, include up to 100
  most recent open `ReviewItem`s for this scan run.

### Response — 200 — `ApiResponse<ScanRunDetailResponse>`

```csharp
public record ScanRunDetailResponse(
    Guid Id,
    ScanMode Mode,
    ScanStatus Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? FailureReason,
    Guid[] LibraryRootIds,
    ScanCountsDto Counts,
    IReadOnlyList<ReviewItemDto>? ReviewItems); // null when includeReview = false
```

### Status codes

| Code | When |
|---|---|
| 200 | Found. |
| 401 / 403 | Auth / role. |
| 404 | No scan run with that id. |

---

## GET /api/v1/admin/scan/active

Convenience endpoint: returns the currently `Running` scan, if any.

### Response — 200 — `ApiResponse<ScanRunSummaryResponse?>`

`Data = null` when no scan is running.

| Code | When |
|---|---|
| 200 | Always (data may be null). |
| 401 / 403 | Auth / role. |

---

## POST /api/v1/admin/scan/{id:guid}/cancel

Request cancellation of a running scan. Idempotent: cancelling an
already-finished scan returns 200 with the current state.

### Response — 200 — `ApiResponse<ScanRunSummaryResponse>`

The scan transitions through `Cancelled` status; the response reflects
post-cancel state (may be `Cancelled` or `Completed` if the scan finished
in the same instant).

### Status codes

| Code | When |
|---|---|
| 200 | Cancellation requested or already terminal. |
| 401 / 403 | Auth / role. |
| 404 | No scan run with that id. |

---

## Pagination & list endpoints

Scan-run list endpoint (`GET /api/v1/admin/scan?page=&pageSize=&status=`)
is **out of scope** for this feature; `GET /api/v1/admin/scan/active` and
`GET /api/v1/admin/scan/{id}` cover the workflow. List view can be added
without contract changes to existing endpoints.


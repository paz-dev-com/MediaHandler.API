# API Endpoint Contracts: Admin Dashboard Backend API Gaps

**Feature**: 002-admin-api-gaps  
**Date**: 2025-07-17  
**Base URL**: `/api/v1/admin`  
**Auth**: All endpoints require `AdminOnly` policy (Auth0 JWT with admin role)  
**Rate Limit**: `fixed` (100 req/min)

---

## 1. Toggle Library Root Enabled

### `PUT /api/v1/admin/library-roots/{id}/enabled`

Toggle the `IsEnabled` status of a library root. Sets an explicit value (idempotent).

**Path Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Guid` | Yes | Library root ID |

**Request Body**:

```json
{
  "isEnabled": false
}
```

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `isEnabled` | `boolean` | Yes | — |

**Response 200 OK** — Updated library root:

```json
{
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "path": "/nas/Movies",
    "kind": "Movie",
    "label": "Main Movies",
    "isEnabled": false,
    "createdAt": "2025-07-01T10:00:00Z",
    "updatedAt": "2025-07-17T14:30:00Z"
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:

| Status | Code | Condition |
|--------|------|-----------|
| 400 | `VALIDATION_ERROR` | Invalid request body |
| 401 | — | Missing or invalid JWT |
| 403 | — | Non-admin user |
| 404 | `NOT_FOUND` | Library root ID does not exist |
| 409 | `SCAN_IN_PROGRESS` | A running scan references this root |

**Error 404 Example**:
```json
{
  "data": null,
  "meta": null,
  "errors": [
    {
      "code": "NOT_FOUND",
      "message": "Library root '3fa85f64-...' was not found."
    }
  ]
}
```

**Error 409 Example**:
```json
{
  "data": null,
  "meta": null,
  "errors": [
    {
      "code": "SCAN_IN_PROGRESS",
      "message": "Cannot modify a library root while a scan targeting it is running."
    }
  ]
}
```

---

## 2. List Scan History (Paginated)

### `GET /api/v1/admin/scan?page={page}&pageSize={pageSize}`

Retrieve a paginated list of scan run summaries, ordered by `StartedAt` descending.

**Query Parameters**:

| Parameter | Type | Required | Default | Constraints |
|-----------|------|----------|---------|-------------|
| `page` | `int` | No | `1` | ≥ 1 |
| `pageSize` | `int` | No | `20` | 1–100 (capped) |

**Response 200 OK** — Paginated scan history:

```json
{
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "mode": "Full",
      "status": "Completed",
      "startedAt": "2025-07-17T12:00:00Z",
      "finishedAt": "2025-07-17T12:15:00Z",
      "libraryRootIds": ["a1b2c3d4-..."],
      "counts": {
        "totalDiscovered": 1500,
        "added": 25,
        "updated": 10,
        "unchanged": 1450,
        "removed": 5,
        "excluded": 8,
        "needsReview": 2
      }
    }
  ],
  "meta": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 50,
    "totalPages": 3
  },
  "errors": null
}
```

**Empty result**:
```json
{
  "data": [],
  "meta": {
    "page": 1,
    "pageSize": 20,
    "totalCount": 0,
    "totalPages": 0
  },
  "errors": null
}
```

**Error Responses**:

| Status | Code | Condition |
|--------|------|-----------|
| 400 | `BAD_REQUEST` | Invalid page/pageSize (negative, zero, non-numeric) |
| 401 | — | Missing or invalid JWT |
| 403 | — | Non-admin user |

---

## 3. Reopen Review Item

### `POST /api/v1/admin/review-items/{id}/resolve`

Reopen a previously resolved or dismissed review item. Uses the existing resolve endpoint with a new `Reopen` action.

**Path Parameters**:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `id` | `Guid` | Yes | Review item ID |

**Request Body**:

```json
{
  "action": "Reopen",
  "tmdbId": null,
  "kind": null
}
```

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `action` | `string` | Yes | Must be `"Reopen"` |
| `tmdbId` | `int?` | No | Ignored for Reopen |
| `kind` | `string?` | No | Ignored for Reopen |

**Response 200 OK** — Reopened review item:

```json
{
  "data": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "filePath": "/nas/Movies/Unknown Movie (2024)/movie.mkv",
    "reason": "NoTmdbMatch",
    "status": "Open",
    "parsedTitle": "Unknown Movie",
    "parsedYear": 2024,
    "parsedSeason": null,
    "parsedEpisode": null,
    "candidates": [],
    "resolvedTmdbId": null,
    "resolvedKind": null,
    "resolvedAt": null,
    "createdAt": "2025-07-15T08:00:00Z"
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:

| Status | Code | Condition |
|--------|------|-----------|
| 400 | `VALIDATION_ERROR` | Invalid request body |
| 401 | — | Missing or invalid JWT |
| 403 | — | Non-admin user |
| 404 | `NOT_FOUND` | Review item ID does not exist |
| 409 | `REVIEW_ALREADY_OPEN` | Item is already in Open status |

**Error 409 Example**:
```json
{
  "data": null,
  "meta": null,
  "errors": [
    {
      "code": "REVIEW_ALREADY_OPEN",
      "message": "This review item is already open."
    }
  ]
}
```

---

## Response Envelope

All responses use the `ApiResponse<T>` envelope:

```json
{
  "data": "<T or T[] or null>",
  "meta": {
    "page": "<int?>",
    "pageSize": "<int?>",
    "totalCount": "<int?>",
    "totalPages": "<int?>"
  },
  "errors": [
    {
      "code": "<string>",
      "message": "<string>",
      "field": "<string?>"
    }
  ]
}
```

- Success: `data` populated, `errors` null
- Error: `data` null, `errors` populated
- `meta` is populated only for paginated endpoints


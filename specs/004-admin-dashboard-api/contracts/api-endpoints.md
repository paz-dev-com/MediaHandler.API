# API Endpoint Contracts: Admin Dashboard API

**Feature**: 004-admin-dashboard-api  
**Base Path**: `/api/v1`  
**Auth**: All endpoints require `AdminOnly` policy (403 Forbidden if unauthorized)  
**Envelope**: All responses wrapped in `ApiResponse<T>`

---

## 1. Scan Decisions Browser

### GET `/api/v1/admin/scan/{scanId}/decisions`

Browse all scan item decisions for a given scan run.

**Query Parameters**:
| Param | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `decisionType` | `string` | No | — | Filter by `ScanDecisionKind` (Added, Updated, Unchanged, Removed, Excluded, NeedsReview) |
| `mediaType` | `string` | No | — | Filter by `MediaType` (Film, TvShow) |
| `libraryRootId` | `Guid` | No | — | Filter by library root |
| `page` | `int` | No | 1 | Page number (≥1) |
| `pageSize` | `int` | No | 25 | Items per page (1–100) |

**Success Response** (200):
```json
{
  "data": [
    {
      "id": "guid",
      "scanRunId": "guid",
      "filePath": "/nas/Movies/Fight Club (1999)/Fight Club.mkv",
      "kind": "Added",
      "reason": null,
      "assignedTmdbId": 550,
      "assignedTmdbKind": "Film",
      "assignedTitle": "Fight Club",
      "assignedYear": 1999,
      "assignedPosterPath": "/a26cz...",
      "candidatesJson": "[{\"tmdbId\":550,\"kind\":\"Film\",\"title\":\"Fight Club\",\"year\":1999,\"posterPath\":\"/a26cz...\",\"overview\":\"...\",\"score\":0.95}]",
      "parsedTitle": "Fight Club",
      "parsedYear": 1999,
      "parsedSeason": null,
      "parsedEpisode": null,
      "parsedMediaType": "Film",
      "libraryRootId": "guid",
      "libraryRootPath": "/nas/Movies",
      "mediaFileId": "guid"
    }
  ],
  "meta": {
    "page": 1,
    "pageSize": 25,
    "totalCount": 150,
    "totalPages": 6
  },
  "errors": null
}
```

**Error Responses**:
- 400: Invalid query parameters (validation error)
- 404: Scan run not found

---

## 2. TMDB Reassignment

### PUT `/api/v1/admin/scan-decisions/{id}/reassign`

Reassign the TMDB source for a scan item decision.

**Request Body**:
```json
{
  "tmdbId": 550,
  "mediaType": "Film"
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `tmdbId` | `int` | Yes | TMDB ID to assign (>0) |
| `mediaType` | `string` | Yes | "Film" or "TvShow" |

**Success Response** (200):
```json
{
  "data": {
    "id": "guid",
    "assignedTmdbId": 550,
    "assignedTmdbKind": "Film",
    "assignedTitle": "Fight Club",
    "assignedYear": 1999,
    "mediaFileId": "guid",
    "mediaId": "guid"
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:
- 400: Validation error (missing/invalid tmdbId or mediaType)
- 404: Scan item decision not found

---

## 3. TV Show Groups

### GET `/api/v1/admin/scan-decisions/tv-groups`

Compute and return TV show episode groupings for a scan run.

**Query Parameters**:
| Param | Type | Required | Notes |
|-------|------|----------|-------|
| `scanId` | `Guid` | Yes | Scan run to group |

**Success Response** (200):
```json
{
  "data": [
    {
      "groupId": "guid",
      "parsedShowName": "Breaking Bad",
      "episodeCount": 62,
      "assignedTmdbId": 1396,
      "assignedTmdbKind": "TvShow",
      "assignedTitle": "Breaking Bad",
      "assignedYear": 2008,
      "assignedPosterPath": "/ggFHV..."
    }
  ],
  "meta": null,
  "errors": null
}
```

**Error Responses**:
- 400: Missing scanId
- 404: Scan run not found

---

## 4. TV Show Group Assignment

### PUT `/api/v1/admin/tv-groups/{groupId}/assign`

Assign a TMDB TV show entry to an entire group.

**Query Parameters**:
| Param | Type | Required | Notes |
|-------|------|----------|-------|
| `scanId` | `Guid` | Yes | Scan run context (needed to resolve group members) |

**Request Body**:
```json
{
  "tmdbId": 1396
}
```

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `tmdbId` | `int` | Yes | TMDB TV show ID (>0) |

**Success Response** (200):
```json
{
  "data": {
    "groupId": "guid",
    "parsedShowName": "Breaking Bad",
    "episodeCount": 62,
    "assignedTmdbId": 1396,
    "assignedTmdbKind": "TvShow",
    "assignedTitle": "Breaking Bad",
    "assignedYear": 2008,
    "assignedPosterPath": "/ggFHV..."
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:
- 400: Validation error (missing tmdbId)
- 404: Group not found (no matching decisions for groupId in the given scanId)

---

## 5. Batch TMDB Enrichment — Start

### POST `/api/v1/admin/enrichment/start`

Start a batch TMDB enrichment process.

**Request Body**: None (empty body or `{}`)

**Success Response** (202 Accepted):
```json
{
  "data": {
    "enrichmentRunId": "guid",
    "status": "Pending",
    "totalItems": 150,
    "message": "Enrichment started. Poll GET /api/v1/admin/enrichment/status for progress."
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:
- 409: Enrichment already in progress (`ENRICHMENT_IN_PROGRESS`)
- 200: No entries to enrich (success with `totalItems: 0`)

---

## 6. Batch TMDB Enrichment — Status

### GET `/api/v1/admin/enrichment/status`

Poll enrichment progress.

**Success Response** (200):
```json
{
  "data": {
    "enrichmentRunId": "guid",
    "status": "Running",
    "startedAt": "2025-07-18T10:00:00Z",
    "finishedAt": null,
    "totalItems": 150,
    "enrichedCount": 45,
    "failedCount": 2,
    "skippedCount": 10,
    "currentItem": "Breaking Bad (2008)",
    "errorDetails": [
      {
        "mediaId": "guid",
        "tmdbId": 99999,
        "title": "Unknown Movie",
        "error": "TMDB API returned 404"
      }
    ]
  },
  "meta": null,
  "errors": null
}
```

**When no enrichment has run**:
```json
{
  "data": null,
  "meta": null,
  "errors": null
}
```

---

## 7. File Rename

### POST `/api/v1/admin/files/{id}/rename`

Rename a media file on the NAS.

**Query Parameters**:
| Param | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `preview` | `bool` | No | `true` | Preview mode (no rename executed) |

**Success Response** (200):
```json
{
  "data": {
    "mediaFileId": "guid",
    "currentFileName": "fight.club.1999.bluray.mkv",
    "proposedFileName": "Fight Club (1999).mkv",
    "currentPath": "/nas/Movies/fight.club.1999.bluray.mkv",
    "proposedPath": "/nas/Movies/Fight Club (1999).mkv",
    "executed": false
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:
- 400: Validation error (no TMDB assignment: `TMDB_ASSIGNMENT_REQUIRED`)
- 404: Media file not found / source file not found on NAS (`FILE_NOT_FOUND`)
- 409: Target filename already exists (`FILE_CONFLICT`)
- 500: Filesystem error (permissions, disk full)

---

## 8. TV Show Group Batch Rename

### POST `/api/v1/admin/tv-groups/{groupId}/rename`

Batch rename all episode files in a TV show group.

**Query Parameters**:
| Param | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `scanId` | `Guid` | Yes | — | Scan run context |
| `preview` | `bool` | No | `true` | Preview mode |

**Success Response** (200):
```json
{
  "data": {
    "groupId": "guid",
    "parsedShowName": "Breaking Bad",
    "episodes": [
      {
        "mediaFileId": "guid",
        "currentFileName": "breaking.bad.s01e01.mkv",
        "proposedFileName": "Breaking Bad - S01E01 - Pilot.mkv",
        "executed": false
      }
    ],
    "totalEpisodes": 62,
    "executedCount": 0
  },
  "meta": null,
  "errors": null
}
```

**Error Responses**:
- 400: No TMDB assignment on group (`TMDB_ASSIGNMENT_REQUIRED`)
- 404: Group not found
- 409: One or more target filenames conflict (`BATCH_FILE_CONFLICT` — entire batch rejected)

---

## Common Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `NOT_FOUND` | 404 | Resource does not exist |
| `VALIDATION_ERROR` | 400 | Request validation failed |
| `ENRICHMENT_IN_PROGRESS` | 409 | Enrichment already running |
| `TMDB_ASSIGNMENT_REQUIRED` | 400 | TMDB assignment needed before operation |
| `FILE_NOT_FOUND` | 404 | Physical file missing from NAS |
| `FILE_CONFLICT` | 409 | Target filename already exists (case-insensitive) |
| `BATCH_FILE_CONFLICT` | 409 | One or more files in batch would conflict |
| `SCAN_NOT_FOUND` | 404 | Scan run does not exist |


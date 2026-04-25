# Admin Library Roots API Contracts

**Feature**: Kodi-Style NAS Library Scanner
**All endpoints**: `[Authorize(Policy = "AdminOnly")]`, `[EnableRateLimiting("fixed")]`, `/api/v1/admin/library-roots*`.
**All responses**: wrapped in `ApiResponse<T>` per constitution III.

---

## GET /api/v1/admin/library-roots

List every configured library root.

### Query parameters

- `page` (int, default 1)
- `pageSize` (int, default from configuration, e.g. 20, max 100)
- `kind` (`LibraryRootKind?`) — optional filter
- `enabledOnly` (bool, default `false`)

### Response — 200 — `ApiResponse<IReadOnlyList<LibraryRootDto>>` with `ApiResponseMeta` for pagination

```csharp
public record LibraryRootDto(
    Guid Id,
    string Path,
    LibraryRootKind Kind,
    string? Label,
    bool IsEnabled,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
```

| Code | When |
|---|---|
| 200 | Always. |
| 401 / 403 | Auth / role. |

---

## POST /api/v1/admin/library-roots

Register a new library root.

### Request body — `AddLibraryRootRequest`

```csharp
public record AddLibraryRootRequest(
    string Path,
    LibraryRootKind Kind,
    string? Label);
```

**Validator** (`AddLibraryRootCommandValidator`):

- `Path` non-empty, ≤ 1024 chars.
- `Path` MUST start with one of the configured NAS base paths
  (`INasService.GetConfiguredPathsAsync`).
- `Path` MUST be unique among existing `LibraryRoot.Path`.
- `Kind` ∈ enum.
- `Label` (if provided) ≤ 200 chars.

### Response — 201 — `ApiResponse<LibraryRootDto>`

`Location` header points at `GET /api/v1/admin/library-roots/{id}` (read
endpoint can be added later; not required for this feature).

| Code | When |
|---|---|
| 201 | Created. |
| 400 | Validation failure. |
| 401 / 403 | Auth / role. |
| 409 | Duplicate `Path`. Body: `ApiError("LIBRARY_ROOT_DUPLICATE", ...)`. |

---

## DELETE /api/v1/admin/library-roots/{id:guid}

Remove a library root. **Soft semantics**: the root is removed from the
table; existing `MediaFile` rows that reference it are left in place but
their `LibraryRootId` is nulled out and they are flagged
`MissingSince = UtcNow` (R-007), so the admin can confirm cleanup via
the review flow.

### Response — 204 No Content

| Code | When |
|---|---|
| 204 | Deleted. |
| 401 / 403 | Auth / role. |
| 404 | No root with that id. |
| 409 | A scan is currently `Running` and references this root. Body: `ApiError("SCAN_IN_PROGRESS", ...)`. |


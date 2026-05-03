# Data Model: Fix Scanner Title Parsing & TMDB Matching

## Modified Entities

### MatchQuery (Application layer — record)

**File**: `MediaHandler.Application/Common/Models/Scanner/TmdbMatchModels.cs`

```csharp
public record MatchQuery(
    string Title,
    int? Year,
    MediaType? KindHint,
    int? NfoTmdbId = null,
    int? ExplicitTokenId = null,
    string Language = "en-US",
    string? FallbackTitle = null,              // NEW: folder-hierarchy derived title
    IReadOnlyList<string>? SearchLanguages = null  // NEW: ordered language list from config
);
```

| Field | Type | Description |
|-------|------|-------------|
| `Title` | `string` | Primary title extracted from filename (text before SxxExx, cleaned) |
| `FallbackTitle` | `string?` | Secondary title from folder hierarchy; null if unavailable or same as Title |
| `SearchLanguages` | `IReadOnlyList<string>?` | Ordered language codes for TMDB search (e.g., `["fr-FR", "en-US"]`); null = use default `["en-US"]` |

**Validation rules**:
- `Title` must not be null or whitespace (enforced by caller in `ScanPipeline.BuildMatchQuery`)
- `FallbackTitle` must differ from `Title` when set (no point retrying the same string — enforced in `ScanPipeline.BuildMatchQuery` with: `FallbackTitle = folderTitle != parsedTitle ? folderTitle : null`)
- `SearchLanguages` must contain valid BCP-47 language tags when provided

**Precedence rule — `Language` vs `SearchLanguages`** (M4):
- When `SearchLanguages` is **non-null and non-empty**, it takes **full precedence**; the legacy `Language` field is ignored by `TmdbMatcher.ResolveAsync`.
- When `SearchLanguages` is **null**, `TmdbMatcher` internally uses `["en-US"]` as the effective language list. In this case the legacy `Language` field is consulted by zero new code paths; it exists solely so existing call sites that explicit-set `Language` compile without modification.

---

### LibraryRoot (Domain entity — extended)

**File**: `MediaHandler.Domain/Entities/LibraryRoot.cs`

```csharp
public class LibraryRoot : BaseEntity
{
    // ...existing properties...

    /// <summary>
    ///     Ordered list of language codes for TMDB searches on files under this root.
    ///     When null or empty, the global default ("en-US") is used.
    ///     Stored as JSON array in the database column.
    /// </summary>
    public IReadOnlyList<string>? SearchLanguages { get; set; }
}
```

| Field | Type | Storage | Description |
|-------|------|---------|-------------|
| `SearchLanguages` | `IReadOnlyList<string>?` | `jsonb` column | Ordered language codes for TMDB multi-language search |

**EF Configuration** (in `LibraryRootConfiguration.cs`):
```csharp
builder.Property(e => e.SearchLanguages)
    .HasColumnType("jsonb")
    .HasConversion(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
```

---

### EpisodeNameParseResult (Application layer — record, extended)

**File**: `MediaHandler.Application/Common/Models/Scanner/EpisodeNameParseResult.cs`

Current `Title` field is repurposed — it now carries the **show title** (text before SxxExx), not the episode title.

```csharp
public record EpisodeNameParseResult(
    bool IsSuccess,
    string? Title,                    // Show title (from filename or folder)
    IReadOnlyList<EpisodeNumber> EpisodeNumbers,  // ← matches actual source code (not "Episodes")
    string? Warning = null,
    string? EpisodeTitle = null,      // NEW: episode title (text after SxxExx)
    string? FolderTitle = null        // NEW: show title derived from folder hierarchy
);
```

| Field | Type | Description |
|-------|------|-------------|
| `Title` | `string?` | Show title extracted from filename (before SxxExx, cleaned) |
| `EpisodeTitle` | `string?` | Episode title (text after SxxExx); informational only |
| `FolderTitle` | `string?` | Alternative show title from folder hierarchy; used as FallbackTitle in MatchQuery |

> ⚠️ **M3 note**: The record parameter is named `EpisodeNumbers` in source (not `Episodes`). Use the correct name to avoid compilation errors.

---

## New Value Objects / Configuration Records

### ReleaseTagOptions (Application layer — options record)

**File**: `MediaHandler.Application/Common/Models/Scanner/ReleaseTagOptions.cs`

```csharp
/// <summary>
///     Configuration-driven release tag patterns for title cleaning.
///     Bound from appsettings.json section "Scanner:ReleaseTags".
///     Reloaded on change via IOptionsMonitor<ReleaseTagOptions>.
/// </summary>
public sealed class ReleaseTagOptions
{
    public const string SectionName = "Scanner:ReleaseTags";

    /// <summary>Additional release-group tag patterns (regex) to strip from titles.</summary>
    public List<string> AdditionalPatterns { get; set; } = [];

    /// <summary>When true, the default built-in patterns are NOT applied (custom-only mode).</summary>
    public bool DisableDefaults { get; set; } = false;
}
```

---

## Unchanged Entities (confirmed no modification needed)

| Entity | Reason |
|--------|--------|
| `ReviewItem` | `ParsedTitle` field already exists; will now contain correct title instead of garbage |
| `MediaFile` | No schema change |
| `ScanRun` | No schema change |
| `ScanItemDecision` | No schema change |
| `NfoMetadata` | No schema change |

---

## State Transitions

### TMDB Resolution Chain (updated flow)

```
ParseEpisode(fullPath)
  → Extract show title from text before SxxExx
  → Clean: dots→spaces, strip release tags
  → Extract folder title via hierarchy walk
  → Return EpisodeNameParseResult { Title, FolderTitle, EpisodeTitle }

BuildMatchQuery(file, root)
  → MatchQuery { Title, FallbackTitle=FolderTitle, SearchLanguages=root.SearchLanguages }

TmdbMatcher.ResolveAsync(query)
  → For each language in query.SearchLanguages ?? ["en-US"]:
      → Search TMDB with query.Title + language
      → If match found → return
  → For each language in query.SearchLanguages ?? ["en-US"]:
      → Search TMDB with query.FallbackTitle + language (if different from Title)
      → If match found → return
  → Return NeedsReview
```

---

## Database Migration

A single EF Core migration is needed to add the `SearchLanguages` column to `LibraryRoots`:

```sql
ALTER TABLE "LibraryRoots" ADD COLUMN "SearchLanguages" jsonb NULL;
```

This is a nullable, non-breaking addition (existing rows default to null = use global default language).


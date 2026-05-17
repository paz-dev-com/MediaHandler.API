# Internal Contracts: Scanner Title Parsing & TMDB Matching

> This feature does not expose new public API endpoints. All changes are internal to the
> scanner pipeline. This document defines the **internal interface contracts** between
> modified components to ensure consistent integration.

## IKodiNameParser.ParseEpisode — Updated Contract

```csharp
/// <summary>
///     Parses a TV episode file path into show title, episode numbers, and folder-based fallback title.
/// </summary>
/// <param name="fullPath">Absolute path to the episode file.</param>
/// <param name="hint">Season context from folder structure.</param>
/// <returns>
///     EpisodeNameParseResult with:
///     - Title: show name extracted from filename (text before SxxExx, cleaned of release tags)
///     - FolderTitle: show name from folder hierarchy (may differ from Title; null if unavailable)
///     - Episodes: list of season+episode numbers found
///     - EpisodeTitle: text after SxxExx (episode name or null)
/// </returns>
EpisodeNameParseResult ParseEpisode(string fullPath, EpisodeNumberingHint hint);
```

### Behavioral Contract

| Input | Expected Title | Expected FolderTitle |
|-------|---------------|---------------------|
| `/Séries/Slow Horses/S03/Slow.Horses.S03E05.MULTi.1080p.WEBRip.x264.AC3-MULTiViSiON.mkv` | "Slow Horses" | "Slow Horses" |
| `/Séries/Law and Order/SVU/S19/Law.and.Order.SUV.S19E23.FRENCH.DVDRip.XviD-Wawacity.tv.avi` | "Law and Order SUV" | "Law and Order SVU" |
| `/Séries/The Nanny/Une.Nounou.Denfer.S04.MULTi.DVDRIP.x264-ETAY/Une.Nounou.Denfer.S04E10.MULTi.DVDRIP.x264-ETAY.mkv` | "Une Nounou Denfer" | "The Nanny" |
| `/Séries/The Wire/The Wire/Sur écoute S04E01 - La fin de l'été.mkv` | "Sur écoute" | "The Wire" |
| `/Séries/The Killing US/S03/The.Killing.US.2011.S03E10.1080p.MULTi.WEB-DL.AvALoN.mkv` | "The Killing US 2011" | "The Killing US" |

> ℹ️ **L1 note**: The Expected Title `"Law and Order SUV"` is intentional — it preserves the typo exactly as it appears in the filename (`SUV` instead of `SVU`). Title reflects filename content; FolderTitle (`"Law and Order SVU"`) holds the correct form from the folder hierarchy. The TMDB matcher will try both.

---

## ITmdbMatcher.ResolveAsync — Updated Resolution Chain

```csharp
/// <summary>
///     Resolves a MatchQuery to a TMDB entry using the precedence chain:
///     NfoTmdbId → ExplicitTokenId → Multi-language title search → FallbackTitle search → NeedsReview
/// </summary>
/// <remarks>
///     Multi-language search: iterates through query.SearchLanguages (or default ["en-US"]),
///     trying query.Title in each language. If no match, retries with query.FallbackTitle
///     (if non-null and different from Title) in the same language sequence.
///     
    ///     Deduplication: per-scan ConcurrentDictionary keyed by (title, language, year?, kind?) prevents
    ///     duplicate TMDB API calls within the same scan run. The full 4-tuple key avoids cache collisions
    ///     between movies and TV shows with the same title, or between shows with disambiguation year.
/// </remarks>
Task<TmdbMatchResult> ResolveAsync(MatchQuery query, CancellationToken ct = default);
```

### Resolution Sequence Diagram

```
ResolveAsync(query)
│
├─ [1] NfoTmdbId set? → LookupById → return if found
├─ [2] ExplicitTokenId set? → LookupById → return if found
├─ [3] For lang in (query.SearchLanguages ?? ["en-US"]):
│      ├─ Check cache[(query.Title, lang, query.Year, query.KindHint)]
│      ├─ SearchCandidatesAsync(query.Title, query.Year, kind, lang)
│      └─ If match → ApplyPolicy → cache → return
├─ [4] If query.FallbackTitle != null && != query.Title:
│      For lang in (query.SearchLanguages ?? ["en-US"]):
│          ├─ Check cache[(query.FallbackTitle, lang, null, kind)]
│          ├─ SearchCandidatesAsync(query.FallbackTitle, null, kind, lang)
│          └─ If match → ApplyPolicy → cache → return
└─ [5] NeedsReview(NoTmdbResult)
```

---

## MatchQuery Record — Updated Schema

```csharp
public record MatchQuery(
    string Title,
    int? Year,
    MediaType? KindHint,
    int? NfoTmdbId = null,
    int? ExplicitTokenId = null,
    string Language = "en-US",             // Kept for backward compat — IGNORED when SearchLanguages is set
    string? FallbackTitle = null,          // NEW — must differ from Title when set
    IReadOnlyList<string>? SearchLanguages = null  // NEW — takes full precedence over Language when non-null
);
```

**Precedence rule**: When `SearchLanguages` is non-null and non-empty, `TmdbMatcher.ResolveAsync` iterates through it and ignores `Language`. When `SearchLanguages` is null, `["en-US"]` is used internally; `Language` is not read by new code paths.

---

## Configuration Contract: appsettings.json

```json
{
  "Scanner": {
    "ReleaseTags": {
      "AdditionalPatterns": [],
      "DisableDefaults": false
    },
    "DefaultSearchLanguages": ["en-US"]
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Scanner:ReleaseTags:AdditionalPatterns` | `string[]` | `[]` | Extra regex patterns to strip from titles |
| `Scanner:ReleaseTags:DisableDefaults` | `bool` | `false` | If true, only custom patterns are applied |
| `Scanner:DefaultSearchLanguages` | `string[]` | `["en-US"]` | Global fallback when `LibraryRoot.SearchLanguages` is null |


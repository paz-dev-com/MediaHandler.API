# MediaHandler.Infrastructure/Nas/Scanner — Clean-Room Policy (R-001)

## Purpose

This directory contains the **Kodi-style NAS classification pipeline**: regex tables, name
parsers, stacking detector, exclusion evaluator, episode matcher, NFO parser, TMDB matcher,
and the orchestrating scan pipeline.

All heuristics in this folder are a **clean-room re-derivation** of Kodi's scanning
behavior. They are implemented entirely from:

1. The [Kodi wiki — Video file naming](https://kodi.wiki/view/Naming_video_files) (public documentation).
2. The [Kodi wiki — advancedsettings.xml](https://kodi.wiki/view/Advancedsettings.xml) (documented defaults).
3. Black-box behavioral observation of a local Kodi installation against a known media tree.
4. Published community file-naming conventions (e.g., [The Movie DB naming guide](https://www.themoviedb.org/documentation/naming-guidelines)).

> **No string, regex, or algorithm in this folder may be a verbatim copy of any file from
> the Kodi source tree** (`/home/tpfeifer/Repos/xbmc-master/` or any other GPL-2.0 source).
> Doing so would bind this project to GPL-2.0 terms, which is incompatible with its licence.

**Kodi reference version**: The behavioral observations were made against the *xbmc-master*
checkout at `/home/tpfeifer/Repos/xbmc-master/` (Kodi v21 "Omega" development branch).

---

## R-001 Attribution Requirements

Every regex pattern or heuristic constant added to this directory **must** include an inline
`// SOURCE:` comment specifying one of the permissible sources listed above. Examples:

```csharp
// SOURCE: Kodi wiki – Video file naming, "File Naming" section
private static readonly Regex YearPattern = new(@"\((\d{4})\)", RegexOptions.Compiled);

// SOURCE: advancedsettings.xml default — cleanstrings tokenlist
private static readonly string[] CleanTokens = [ "1080p", "720p", "BluRay", ... ];
```

Reviewers **must reject** any PR where a `// SOURCE:` comment is absent or cites an
internal Kodi `.cpp`/`.h` file.

---

## Regex & Heuristic Source Citations (KodiRegexCatalog.cs)

The following table lists every regex/heuristic in `KodiRegexCatalog.cs` with its
documented source:

| Pattern / Constant | Source |
|---|---|
| `VideoExtensions` | Kodi wiki advancedsettings `<videoextensions>` — observed defaults |
| `ExplicitTmdbIdToken` | Kodi wiki — `{tmdb=NNN}` / `{tmdbid=NNN}` tokens in filenames |
| `YearInParens` | Kodi wiki — `(YEAR)` in folder/filename for release year |
| `YearDotSeparated` | advancedsettings `moviecleanDatestamp` — dot-separated year pattern |
| `YearAtEnd` | Kodi wiki — year at end of title after last dot or space |
| `MovieCleanupTokens[]` | advancedsettings `moviecleanString` — quality/codec/source tokens removed from titles |
| `SxxExx` | Kodi wiki — canonical `S01E01` TV episode naming |
| `SxxExxToEyy` | Kodi wiki — multi-episode `S01E01-E02` / `S01E01E02` ranges |
| `SeasonXEpisode` | Kodi wiki — alternate `1x05` episode numbering |
| `DateBased` | Kodi wiki — `YYYY.MM.DD` date-based episode naming for daily shows |
| `AbsoluteEpisode` | Kodi wiki — `E042` without season prefix (anime) |
| `AbsoluteNumber` | Kodi wiki — 3-digit zero-padded absolute episode number |
| `SeasonFolderName` | Observed Kodi behaviour — `Season XX`, `Serie`, `Saison`, `Staffel` patterns |
| `SpecialsFolderName` | Observed Kodi behaviour — `Specials` / `Season 00` / `S00` folder names |
| `StackSuffixCd` | advancedsettings `stackingregex` — `cd1`/`cd2` stacking suffix |
| `StackSuffixDisc` | advancedsettings `stackingregex` — `disc1`/`disk1` stacking suffix |
| `StackSuffixPart` | advancedsettings `stackingregex` — `part1`/`part2` stacking suffix |
| `StackSuffixPt` | advancedsettings `stackingregex` — `pt1`/`pt2` stacking suffix |
| `StackSuffixLetter` | advancedsettings `stackingregex` — `(a)`/`(b)` bracketed letter |
| `SampleFilenamePattern` | Kodi wiki — files with `-sample` suffix are excluded |
| `TrailerFilenamePattern` | Kodi advancedsettings `<trailerextensions>` — `-trailer` suffix |
| `ExcludedFolderNames` | Kodi wiki — `Sample`, `Extras`, `Featurettes`, `Trailers` folders excluded |
| `HiddenFolderPattern` | Observed Kodi behaviour — folder names starting with `.` are hidden/excluded |
| `DefaultExclusionRules` | Composite: all of the above exclusion patterns, plus `.nomedia` marker (advancedsettings) |

---

## PR Checklist — Scanner/ Changes

Every pull request that modifies files in this directory must satisfy all items before merge:

- [ ] Every new regex or heuristic constant has a `// SOURCE:` comment citing a public,
      non-GPL source (wiki page URL, advancedsettings key name, or "observed black-box behavior").
- [ ] No string from `/home/tpfeifer/Repos/xbmc-master/` or any GPL source has been
      copy-pasted — verified by the author and a second reviewer.
- [ ] A corresponding `[Theory]` row has been added to the relevant test file
      (`KodiNameParserTests`, `ExclusionEvaluatorTests`, `StackingDetectorTests`, or
      `TvEpisodeMatcherTests`) documenting the new case.
- [ ] `dotnet test --filter Category=Scanner` passes locally.
- [ ] The PR description includes a "Source mapping" table listing each new pattern and its
      permissible source.

---

## Architecture Note

| Component | File | Responsibility |
|---|---|---|
| Regex catalog | `KodiRegexCatalog.cs` | All clean-room-derived patterns in one auditable place |
| Name parser | `KodiNameParser.cs` | Movie / TV-show name extraction from path segments |
| Exclusion evaluator | `ExclusionEvaluator.cs` | Extension allow-list, sample/trailer/extras patterns, `.nomedia` |
| Stacking detector | `StackingDetector.cs` | cd1/cd2, part1/part2, disc1/disc2, (a)/(b) grouping |
| Episode matcher | `TvEpisodeMatcher.cs` | SxxExx, 1x05, date-based, absolute-number patterns |
| NFO parser | `NfoParser.cs` | XDocument-based tolerant NFO sidecar reader |
| TMDB matcher | `TmdbMatcher.cs` | LRU-cached wrapper; ambiguity → ReviewItem |
| Scan pipeline | `ScanPipeline.cs` | Orchestrates enumerate → exclude → group → parse → NFO → TMDB → persist |

The `INasFileEnumerator` implementation lives one level up in `MediaHandler.Infrastructure/Nas/`
(not in this sub-folder) because it wraps the existing `INasService` and is not a
scanner-specific heuristic.

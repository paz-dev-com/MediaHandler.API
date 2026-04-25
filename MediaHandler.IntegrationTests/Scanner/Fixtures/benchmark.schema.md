# Benchmark Fixture YAML Schema

**File**: `benchmark.yaml` (lives alongside this schema in `Scanner/Fixtures/`)  
**Consumed by**: `FixtureBuilder.cs` (builds the fake `INasService` in-memory tree)  
**Purpose**: Declarative specification of the integration-test NAS tree, plus per-path
ground-truth annotations used by SC-001..SC-007 assertions.

---

## Top-level structure

```yaml
# benchmark.yaml
version: "1"           # schema version (bump on breaking changes)
base_path: "/nas"      # logical root used by the fake INasService

movies: [...]          # list of MovieEntry
tv_shows: [...]        # list of TvShowEntry
exclusion_baits: [...]  # list of ExclusionBaitEntry
review_baits: [...]    # list of ReviewBaitEntry
```

---

## `MovieEntry`

Represents a single movie (or stacked multi-part movie) in the fixture tree.

```yaml
movies:
  - folder: "The Matrix (1999)"          # relative folder under base_path/Movies/
    files:                               # one or more physical files
      - name: "The Matrix (1999).mkv"
    expected_tmdb_id: 603                # ground-truth TMDB movie id
    expected_classification: movie       # "movie" | "stacked_movie" | "review"
    nfo: |                               # optional inline NFO content (written as movie.nfo)
      <?xml version="1.0" encoding="utf-8"?>
      <movie>
        <tmdbid>603</tmdbid>
        <title>The Matrix</title>
        <year>1999</year>
      </movie>

  # Stacked example: cd1 / cd2 layout
  - folder: "Kill Bill (2003)"
    files:
      - name: "Kill.Bill.2003.cd1.mkv"
        stack_part: 1
      - name: "Kill.Bill.2003.cd2.mkv"
        stack_part: 2
    expected_tmdb_id: 24
    expected_classification: stacked_movie

  # Flat layout (no sub-folder)
  - flat: true
    files:
      - name: "Inception.2010.1080p.BluRay.x264-GROUP.mkv"
    expected_tmdb_id: 27205
    expected_classification: movie
```

### `MovieEntry` fields

| Field | Type | Required | Description |
|---|---|---|---|
| `folder` | string | No* | Relative folder path e.g. `"The Matrix (1999)"`. Omit when `flat: true`. |
| `flat` | bool | No | When `true`, file is placed directly in the root movies folder. |
| `files` | list of `FileEntry` | Yes | Physical files to create in the folder. |
| `expected_tmdb_id` | int | Yes | Ground-truth TMDB id for SC-001/SC-002 assertion. |
| `expected_classification` | string | Yes | `movie` \| `stacked_movie` \| `review` |
| `nfo` | string (inline XML) | No | If present, written as `movie.nfo` in the folder. |

---

## `TvShowEntry`

Represents a complete TV show with one or more seasons.

```yaml
tv_shows:
  - show: "Breaking Bad"
    expected_tmdb_id: 1396
    nfo: |
      <?xml version="1.0" encoding="utf-8"?>
      <tvshow>
        <tmdbid>1396</tmdbid>
        <title>Breaking Bad</title>
      </tvshow>
    seasons:
      - season: 1
        episodes:
          - episode: 1
            file: "Breaking.Bad.S01E01.mkv"
            expected_classification: episode
          - episode: 2
            file: "Breaking.Bad.S01E02.mkv"
            expected_classification: episode

      # Multi-episode file: S02E05 + S02E06 share one physical file
      - season: 2
        episodes:
          - episode: 5
            episode_end: 6             # produces two EpisodeFileLink rows
            file: "Breaking.Bad.S02E05-E06.mkv"
            expected_classification: episode

      # Specials / Season 00
      - season: 0
        folder_name: "Specials"        # overrides the default "Season 00" folder name
        episodes:
          - episode: 1
            file: "Breaking.Bad.Special.mkv"
            expected_classification: episode

  # 1x05-style numbering
  - show: "Seinfeld"
    expected_tmdb_id: 1400
    seasons:
      - season: 1
        episodes:
          - episode: 5
            file: "Seinfeld.1x05.mkv"       # alternate numbering format
            expected_classification: episode

  # Date-based numbering
  - show: "The Daily Show"
    expected_tmdb_id: 2224
    seasons:
      - season: 2024
        episodes:
          - episode: 1
            file: "The.Daily.Show.2024.03.19.mkv"
            expected_classification: episode
```

### `TvShowEntry` fields

| Field | Type | Required | Description |
|---|---|---|---|
| `show` | string | Yes | Show name (used as folder name under `TV Shows/`). |
| `expected_tmdb_id` | int | Yes | Ground-truth TMDB series id. |
| `nfo` | string | No | Written as `tvshow.nfo` in the show root folder. |
| `seasons` | list of `SeasonEntry` | Yes | Season definitions. |

### `SeasonEntry` fields

| Field | Type | Required | Description |
|---|---|---|---|
| `season` | int | Yes | Season number (0 = Specials). |
| `folder_name` | string | No | Overrides automatic `Season XX` naming. |
| `episodes` | list of `EpisodeEntry` | Yes | Episode definitions. |

### `EpisodeEntry` fields

| Field | Type | Required | Description |
|---|---|---|---|
| `episode` | int | Yes | Episode number (start of range for multi-episode). |
| `episode_end` | int | No | Inclusive end of multi-episode range. |
| `file` | string | Yes | Physical filename. |
| `expected_classification` | string | Yes | `episode` \| `review` |
| `nfo` | string | No | Per-episode NFO content (written as `<basename>.nfo`). |

---

## `ExclusionBaitEntry`

Files and folders that **must not** appear as `MediaFile` rows (i.e., `ScanItemDecision.Kind = Excluded`).
Used by SC-003.

```yaml
exclusion_baits:
  - path: "Movies/Extras/deleted-scenes.mkv"
    expected_exclusion_reason: "extras-folder"

  - path: "Movies/The Matrix (1999)/The Matrix (1999)-sample.mkv"
    expected_exclusion_reason: "sample-filename"

  - path: "Movies/Trailers/matrix-trailer.mkv"
    expected_exclusion_reason: "trailer-folder"

  - path: "Movies/.recycle/oldfile.mkv"
    expected_exclusion_reason: "hidden-folder"

  - path: "TV Shows/Breaking Bad/.nomedia"
    is_nomedia_marker: true
    expected_exclusion_reason: "nomedia-marker"

  - path: "Movies/poster.jpg"
    expected_exclusion_reason: "non-video-extension"
```

### `ExclusionBaitEntry` fields

| Field | Type | Required | Description |
|---|---|---|---|
| `path` | string | Yes | Path relative to `base_path`. |
| `expected_exclusion_reason` | string | Yes | Human-readable label tying the row to an `ExclusionRule.Name`. |
| `is_nomedia_marker` | bool | No | When `true`, `FixtureBuilder` writes a zero-byte `.nomedia` file here. |

---

## `ReviewBaitEntry`

Files that **should** land in the `ReviewItem` table (ambiguous / unresolvable).
Used by SC-001/SC-002/SC-006.

```yaml
review_baits:
  - path: "Movies/the.movie.mkv"
    expected_review_reason: "NoTmdbResult"    # matches ReviewReason enum value
    parsed_title: "the movie"
    parsed_year: null

  - path: "TV Shows/SomeShow/episode_no_pattern.mkv"
    expected_review_reason: "UnparseableEpisode"

  - path: "Movies/Conflict Folder/another title.mkv"
    expected_review_reason: "MultipleCandidates"
    parsed_title: "another title"
```

### `ReviewBaitEntry` fields

| Field | Type | Required | Description |
|---|---|---|---|
| `path` | string | Yes | Path relative to `base_path`. |
| `expected_review_reason` | string | Yes | Must match a `ReviewReason` enum value name. |
| `parsed_title` | string | No | Expected `ReviewItem.ParsedTitle` for assertion. |
| `parsed_year` | int/null | No | Expected `ReviewItem.ParsedYear` for assertion. |

---

## `FileEntry` (inline)

Used inside `movies[].files` and `tv_shows[].seasons[].episodes`.

| Field | Type | Required | Description |
|---|---|---|---|
| `name` | string | Yes | Filename (no path). |
| `stack_part` | int | No | Stack part number (`1`, `2`, …) — sets `MediaFileRole = StackedPart`. |
| `size_bytes` | long | No | Simulated file size (default: 1 073 741 824 = 1 GiB). |
| `mtime_utc` | string (ISO 8601) | No | Simulated last-modified time (default: `2024-01-01T00:00:00Z`). |

---

## Validation rules enforced by `FixtureBuilder`

1. `expected_tmdb_id` must be positive.
2. `expected_classification` must be one of `movie`, `stacked_movie`, `episode`, `review`.
3. `expected_review_reason` (in `review_baits`) must match a `ReviewReason` enum member name.
4. `expected_exclusion_reason` must match an `ExclusionRule.Name` registered in `KodiRegexCatalog`.
5. Each `path` in `exclusion_baits` and `review_baits` must be unique (no duplicate registrations).
6. `stack_part` values within the same `folder` must be contiguous starting at 1.

---

## Minimum fixture size (SC-001 requirements)

The `FixtureBuilder` asserts at construction time that `benchmark.yaml` meets the following
minimums (per `quickstart.md §1.1`):

| Requirement | Minimum |
|---|---|
| Movies total | 200 |
| Movies in per-folder layout | 160 (≥ 80 %) |
| Movies in flat layout | 20 (≥ 10 %) |
| Stacked movies (cd/part/disc) | 5 |
| TV shows | 50 |
| Shows with Specials folder | 5 |
| Shows with multi-episode file | 5 |
| Shows with 1x05-style numbering | 2 |
| Shows with date-based numbering | 1 |
| NFO sidecars — movies | 5 |
| NFO sidecars — TV shows | 3 |
| Exclusion bait entries | 1 per exclusion rule (≥ 6) |
| Review bait entries | 3 |


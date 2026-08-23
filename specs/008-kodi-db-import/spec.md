# Feature Specification: Kodi Video Database Import

**Feature Branch**: `008-kodi-db-import`
**Created**: 2026-03-19
**Status**: Draft
**Input**: User description: "I use Kodi to read all my media files and it would be nice to use the Kodi database in order to add new medias in the app." — the admin uploads Kodi's local SQLite video database (`MyVideos*.db`) through an admin endpoint; the system creates Media entries from Kodi library items and links them to already-scanned media files where paths match; the operation is on-demand, repeatable, and idempotent.

## Clarifications

Scope decisions established with the user (do not re-open):

- **Database type**: Kodi's local SQLite **video** database file (`MyVideos<version>.db` from Kodi's `userdata/Database` folder). The music database (`MyMusic*.db`) is not concerned.
- **Access mode**: the admin uploads the `.db` file through an admin endpoint each time they want to import. The system never connects to a running Kodi instance.
- **Behavior**: the import **creates Media entries** from Kodi library items (movies, TV shows, episodes) **and links** them to media files already discovered by the NAS scanner, wherever the Kodi file location can be mapped to a scanned file path.
- **Frequency**: on-demand and repeatable. The admin triggers a sync whenever wanted; re-runs MUST be idempotent.

Public schema facts relied upon (reference only — per the repository's no-GPL rule, no Kodi source code is ever copied; these are documented, publicly available schema concepts):

- The video database holds one row per **movie**, per **TV show**, and per **episode**; episode rows carry their season number, episode number, and owning show.
- Every library item references a **file entry** composed of a directory path (`path.strPath`, a URI *as seen by the Kodi box*, e.g. `smb://server/share/Movies/`) plus a filename (`files.strFilename`). Kodi paths therefore do **not** textually match the app's canonical NAS paths (e.g. `/nas/Movies/…`) — translation is required.
- External identifiers (TMDB, IMDB, TVDB) are available per item via the `uniqueid` concept.
- Stacked (multi-part) movies are stored as a single file entry whose name is a `stack://` URI listing every part.
- Multi-episode files appear as several episode rows sharing the same file entry.
- Watched status (play count, last-played date) and resume bookmarks exist in the database but are **not imported** by default (see Open Questions).
- A music-video concept exists in the Kodi video database; the app has no such media type, so those rows are ignored (counted as skipped).
- Released schema versions relevant to users: 119 (Kodi 19 "Matrix"), 121 (Kodi 20 "Nexus"), 131 (Kodi 21 "Omega"). The file name suffix carries the version (e.g. `MyVideos121.db`).

Consistency anchors with existing features:

- Linking semantics follow `specs/007-media-file-linking`: a `MediaFile` is linked to at most one `Media` at a time; episodes are linked via episode-to-file links; stacked parts belong to a stack group under one Media entry.
- Scanner behavior (`specs/001-kodi-style-scanner`) remains the only way physical files are discovered: the import **never creates file records** for paths the scanner has not seen.
- Season 0 ("specials") episodes are preserved, consistent with the scanner; completeness reporting already excludes them (007).
- Items whose identity cannot be confidently resolved are surfaced through the existing admin **review workflow** rather than silently mis-created, consistent with scanner behavior.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Import a Kodi Library from an Uploaded Database (Priority: P1)

An administrator exports their Kodi video database file and uploads it through an admin endpoint. The system validates the file, reads every movie, TV show, and episode in the Kodi library, and creates the corresponding Media entries — films, TV shows, and their season/episode structure — using the identity information Kodi already curated (title, year, external TMDB/IMDB identifiers). Entries that already exist (same media kind and TMDB identity, e.g. created earlier by the scanner) are reused, never duplicated. Items whose identity cannot be confidently established are routed to the review queue instead of creating a wrong entry.

**Why this priority**: This is the core value of the feature: Kodi has already done the curation work (titles, years, identifiers), so importing its database is the fastest, lowest-error path to populate the app. Everything else (linking, reporting, preview) builds on it.

**Independent Test**: Upload a fixture `MyVideos121.db` containing a known set of movies and one TV show with several episodes. Verify each item appears exactly once as a Media entry with the correct kind, title, year, and TMDB identity, and that the TV show carries the expected season/episode structure.

**Acceptance Scenarios**:

1. **Given** a valid `MyVideos121.db` containing 3 movies and 1 TV show with 5 episodes, **When** the admin uploads it, **Then** the import completes and exactly 3 Film entries and 1 TvShow entry exist, and the show has the 5 episodes under their correct season numbers.
2. **Given** a Kodi movie carrying a TMDB identifier, **When** imported, **Then** the Media entry is created with that TMDB identity directly, without a provider title search.
3. **Given** a Kodi item carrying only a non-TMDB external identifier (e.g. IMDB), **When** imported, **Then** the system resolves the corresponding TMDB identity through the metadata provider and creates the entry with it.
4. **Given** a Kodi item with no external identifier whose title+year yields exactly one confident provider match, **When** imported, **Then** the entry is created with that identity.
5. **Given** a Kodi item whose identity is ambiguous (no identifier, several comparable provider candidates), **When** imported, **Then** no Media entry is created for it and the item appears in the review queue with its candidates and a human-readable reason.
6. **Given** a Media entry already exists with the same kind and TMDB identity as a Kodi item, **When** imported, **Then** no duplicate is created; the Kodi item is associated with the existing entry.
7. **Given** a Kodi TV show with zero episodes, **When** imported, **Then** the TvShow entry is still created (an empty show), with no seasons or episodes.
8. **Given** a non-admin authenticated user, **When** they call the import endpoint, **Then** the response is 403 Forbidden. **Given** an unauthenticated caller, **Then** the response is 401 Unauthorized.
9. **Given** an uploaded file that is not a valid Kodi video database (wrong file, corrupt, music database, unsupported version), **When** the request is processed, **Then** it is rejected with a clear, specific error and nothing is imported.

---

### User Story 2 — Translate Kodi Paths and Link Scanned Files (Priority: P1)

An administrator configures how Kodi file locations translate to the app's NAS paths (ordered prefix mappings, e.g. `smb://FREEBOX/Films/` → `/nas/Movies/`). During import, every Kodi file reference is normalized and translated through these mappings; when the translated path matches a file the scanner already knows, the imported media item is linked to that file (movie file, stack parts, or episode links). Items that cannot be translated or matched are still imported, but left unlinked and clearly reported, so the admin can fix mappings or run a scan and re-import.

**Why this priority**: Without linking, the import produces a catalog disconnected from the physical collection — no completeness, no "owned" flags. Path translation is the make-or-break rule of the feature because Kodi URIs never match NAS paths textually.

**Independent Test**: Configure one prefix mapping, import a fixture DB where some Kodi paths fall under the mapping and some do not, and verify that matched items are linked to the correct scanned files while unmatched items are imported unlinked and reported with their Kodi path prefix.

**Acceptance Scenarios**:

1. **Given** a mapping `smb://FREEBOX/Films/` → `/nas/Movies/` and a Kodi movie located at `smb://FREEBOX/Films/The Matrix (1999)/The Matrix (1999).mkv` for which the scanner recorded `/nas/Movies/The Matrix (1999)/The Matrix (1999).mkv`, **When** imported, **Then** that scanned file is linked to the imported Media entry.
2. **Given** a Kodi episode whose translated path matches a scanned episode file, **When** imported, **Then** an episode-to-file link is created for the correct season/episode and the file is associated with the show's Media entry.
3. **Given** a Kodi path under a prefix covered by no mapping, **When** imported, **Then** the Media entry is still created, no file is linked, the item is reported as "unmatched path", and the report surfaces the distinct uncovered Kodi prefixes so the admin can extend the mappings.
4. **Given** a Kodi path that translates through a mapping but matches no scanned file, **When** imported, **Then** the entry is created unlinked and reported as "no scanned file"; after the admin scans that location and re-imports, the link is created.
5. **Given** a stacked Kodi movie (a `stack://` reference listing two parts) where both parts were scanned, **When** imported, **Then** both part files are linked as stack parts of the single Media entry; **Given** only one part was scanned, **Then** that part is linked and the missing part is reported.
6. **Given** a Kodi multi-episode file (several episode rows sharing one file entry), **When** imported, **Then** each episode is linked to the same file with its position in the file preserved.
7. **Given** a scanned file already linked to a **different** Media entry than the one the Kodi item resolves to, **When** imported, **Then** the existing link is preserved and the discrepancy is reported as a conflict — links are never silently stolen.
8. **Given** Kodi paths containing percent-encoded characters, mixed separators, or letter-case differences relative to the scanned paths, **When** translated, **Then** they are normalized (decoding, separator and case handling consistent with the scanner's path comparison) and still match.

---

### User Story 3 — Repeatable, Idempotent Re-Import (Priority: P1)

An administrator re-uploads a fresh export of their Kodi database whenever they want to synchronize. Each run creates only what is new, links only what is not yet linked, leaves existing entries and their enriched metadata untouched, and never deletes or unlinks app data for items that disappeared from Kodi — those are counted and reported instead.

**Why this priority**: The user explicitly requires on-demand, repeatable sync. Without strict idempotency, every re-import would risk duplicates, clobbered metadata, or lost links — making the feature unusable as a sync mechanism.

**Independent Test**: Import the same fixture DB twice and verify the second run creates and changes nothing (all items reported unchanged). Then import an updated DB with one added movie, one removed movie, and one re-identified item, and verify only the addition is created, the removal is only reported, and the re-identification is surfaced.

**Acceptance Scenarios**:

1. **Given** a database already imported once, **When** the identical file is uploaded again, **Then** the second run creates no Media entries, no seasons/episodes, and no links, and reports every item as unchanged.
2. **Given** a re-upload containing new Kodi items, **When** imported, **Then** only the new items are created and linked; previously imported items are untouched.
3. **Given** a re-upload where a previously imported item's file is already linked to an entry with a **different** TMDB identity than the one Kodi now carries, **When** imported, **Then** no duplicate entry is created and the identity discrepancy is reported as a conflict for the admin to resolve.
4. **Given** a re-upload in which previously imported items no longer exist in the Kodi database, **When** imported, **Then** the corresponding Media entries, file links, and watch data are left untouched, and the items are counted and listed in the run report as "no longer in Kodi".
5. **Given** an earlier run left items unlinked (unmapped path or missing scan), **When** the admin fixes the cause and re-imports, **Then** linking is re-attempted and the now-matching files are linked.
6. **Given** an import run is already in progress, **When** a second import is triggered, **Then** it is rejected with 409 Conflict and the running import is unaffected.
7. **Given** a run fails partway through, **When** the admin re-uploads the same file, **Then** the new run converges to the same end state as a single successful run (no partial duplicates).

---

### User Story 4 — Import Run Report and History (Priority: P2)

An administrator inspects the outcome of any import run: summary counters (movies/shows/episodes created or reused, files linked, unmatched paths, conflicts, items no longer in Kodi, items sent to review, skipped music videos) and a paged, per-item detail list giving each Kodi item's outcome and reason. Past runs remain browsable, newest first.

**Why this priority**: Trust in a sync mechanism comes from verifiable outcomes. The report is how the admin discovers unmapped prefixes, conflicts, and items needing review — it turns "sync" from a leap of faith into an auditable operation.

**Independent Test**: Run an import over a fixture containing one of each outcome category, then verify the run detail shows correct counts per category and that every item appears in the detail list with the expected outcome and reason.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** the admin views it, **Then** counters are shown for at least: created, reused, files linked, unmatched path, no scanned file, conflicts, no longer in Kodi, needs review, skipped (music videos).
2. **Given** a completed run, **When** the admin pages through the item detail list, **Then** each entry shows the Kodi title, media kind, outcome, and a human-readable reason for non-success outcomes.
3. **Given** several past runs, **When** the admin lists run history, **Then** runs appear newest-first with status, start/finish times, and summary counters.
4. **Given** a non-admin authenticated user, **When** they call any import report or history endpoint, **Then** the response is 403 Forbidden.
5. **Given** a requested run id that does not exist, **When** the report is requested, **Then** the response is 404 Not Found.

---

### User Story 5 — Preview an Import Before Committing (Priority: P2)

An administrator uploads a database in "preview" mode to validate it and see what *would* happen — how many entries would be created, how many files linked, which prefixes are unmapped, which conflicts would arise — without changing any application data. This lets them tune path mappings before running the real import.

**Why this priority**: Path mappings are guesswork the first time; a dry run converts trial-and-error on live data into a safe check. It is P2 because the real import already never destroys data, so preview is a convenience, not a safeguard.

**Independent Test**: Submit a fixture DB in preview mode, verify the projected counters and per-item outcomes, verify nothing was persisted, then run the real import on the same file and verify the outcomes match the preview.

**Acceptance Scenarios**:

1. **Given** a valid upload in preview mode, **When** processed, **Then** projected counters and per-item outcomes are returned and **no** Media entries, seasons, episodes, links, or review items are persisted.
2. **Given** an invalid or unsupported file, **When** submitted in preview mode, **Then** the same validation errors are returned as for a real import.
3. **Given** a preview followed by a real import of the same file with no intervening changes, **When** both complete, **Then** the real run's outcomes match the preview.
4. **Given** Kodi items lacking direct TMDB identifiers, **When** previewed, **Then** they are reported as "requires identity lookup" rather than being resolved against the provider during preview (preview performs no provider traffic).

---

### Edge Cases

- **Empty library**: a valid video database containing zero movies, shows, and episodes imports successfully with all counters at zero.
- **Wrong database**: a Kodi *music* database, or any SQLite file lacking the video-library structure, is rejected as "not a Kodi video database" — never partially parsed.
- **Corrupt or truncated file**: rejected with a clear error; nothing is imported; no run is left in a running state.
- **Unsupported or unrecognized version**: rejected with the detected version named in the error (version is read from the `MyVideos<version>.db` file name; a renamed file with no recognizable suffix is rejected with guidance to keep the original name).
- **File copied while Kodi is running**: if the upload is structurally unreadable or inconsistent, validation rejects it with guidance to close Kodi before copying the file.
- **Duplicate identities within Kodi**: two Kodi movies sharing one TMDB identity (e.g. different editions/versions) result in a single Media entry with both files linked, flagged as informational in the report.
- **Re-identified item with unlinked file**: if an item's TMDB identity changed in Kodi *and* no file link connects it to the previously created entry, the import cannot recognize it as the same item and creates a separate entry; the old entry remains for manual cleanup (see Open Questions).
- **Non-file protocols**: Kodi items referencing `pvr://`, `http://`, `upnp://`, or other non-filesystem locations can never map to scanned NAS files; they are imported unlinked and reported as "unsupported location".
- **Kodi-internal duplicates**: the same file path referenced by multiple Kodi items (e.g. a file appearing as both a movie and an episode) is reported as a conflict; the first consistent link wins, subsequent ones are not applied.
- **Missing-but-recorded files**: a scanned file currently marked missing by the scanner can still be linked (linking expresses identity; presence is the scanner's concern).
- **Oversized upload**: a file beyond the configured size limit is rejected before processing with a clear limit message.
- **Provider outage mid-import**: items needing an identity lookup are skipped and reported as "identity lookup failed — retry"; items carrying direct TMDB identifiers still import normally; the run completes with a partial-failure indication rather than failing wholesale.
- **Non-ASCII titles and paths**: accented or non-Latin characters in titles and paths are decoded and matched without mojibake or silent drops.
- **Specials**: season-0 episodes are imported and linked like any other season, consistent with the scanner; they remain excluded from completeness reporting per the existing rule.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Upload and validation

- **FR-001**: System MUST expose an admin endpoint accepting a Kodi video database file upload, restricted to the administrator role (`AdminOnly` policy), returning 401 for unauthenticated and 403 for non-admin callers.
- **FR-002**: System MUST reject, before any import processing: missing/empty files, files that are not valid SQLite databases, files that do not contain the Kodi video-library structure (movies/shows/episodes/files/paths), and files exceeding a configurable size limit. Each rejection carries a specific, human-readable reason and nothing is persisted.
- **FR-003**: System MUST determine the database schema version from the file name suffix (`MyVideos<version>.db`) and MUST reject versions outside the supported set, naming the detected version in the error. The initial supported set is 119, 121, and 131 (Kodi 19/20/21) — see Open Questions.
- **FR-004**: Validation failures MUST leave no persisted side effects (no entries, no links, no review items, no lingering run in a non-terminal state).

#### Identity resolution and Media creation

- **FR-005**: System MUST create Media entries for Kodi **movies** (kind: Film) and **TV shows** (kind: TvShow), and MUST materialize the season/episode structure for every Kodi episode, keyed by show + season number + episode number, including season 0 (specials).
- **FR-006**: Identity for a new entry MUST be resolved with the precedence: Kodi TMDB identifier → non-TMDB external identifier resolved through the metadata provider → title+year provider search. Items with no confident identity are sent to the review queue with candidates and reason; no Media entry is created for them.
- **FR-007**: System MUST de-duplicate against existing entries by media kind + TMDB identity: a Kodi item matching an existing entry is associated with it and never creates a duplicate, regardless of whether the existing entry was created by the scanner, a previous import, or manually.
- **FR-008**: Newly created entries are populated from Kodi with: media kind, title, year, and original title when available. All other display metadata (overview, poster/backdrop, runtime, genres, release date, series/season/episode details) is owned by the existing TMDB enrichment and is left for it to fill in.
- **FR-009**: The import MUST NOT modify metadata of pre-existing entries. On re-import, only missing entries and missing links are created; metadata refresh remains the job of enrichment.
- **FR-010**: Episode and season display names sourced from Kodi are placeholders only; TMDB enrichment remains the authority for season/episode metadata and expected-episode counts, and merges onto the same season/episode keys without duplicating them.
- **FR-011**: Music-video rows in the Kodi database MUST be ignored and counted as skipped; no music-video media type is introduced.

#### Path mapping and linking

- **FR-012**: System MUST support an ordered set of admin-managed path prefix mappings (Kodi URI prefix → app NAS path prefix), persisted and reusable across runs (see Open Questions on per-upload overrides).
- **FR-013**: System MUST translate each Kodi file reference (directory path + filename) through the mappings after normalization: percent-decoding, separator normalization, trailing-slash handling, and case-insensitive comparison consistent with the scanner's path semantics.
- **FR-014**: A Kodi item whose translated path matches a scanner-known file MUST be linked to it: movie files (including every part of a `stack://` reference, expanded into its parts) via the media-file link; episode files via an episode-to-file link preserving the episode's position within multi-episode files.
- **FR-015**: The import MUST NEVER create file records: only files already known to the scanner can be linked.
- **FR-016**: A Kodi item whose path cannot be mapped or matches no scanned file MUST still be imported (entry created) and left unlinked, with the outcome and the involved Kodi path prefix recorded in the run report.
- **FR-017**: When the matched file is already linked to a different Media entry than the Kodi item resolves to, the existing link MUST be preserved and the situation reported as a conflict; links are never silently reassigned.
- **FR-018**: Linking MUST respect the existing linking invariants: one file linked to at most one Media entry; stack parts grouped under their single movie entry; multi-episode files linked to each episode with position preserved.

#### Idempotency and re-import

- **FR-019**: Re-importing an unchanged database MUST produce zero creations, zero link changes, and zero metadata changes, with all items reported as unchanged.
- **FR-020**: Each run MUST re-attempt linking for items that were previously left unlinked, so that fixed mappings or newly scanned files are picked up without manual re-entry.
- **FR-021**: Items present in a previous import but absent from the current database MUST be left fully untouched (entries, links, watch data) and MUST be counted and listed as "no longer in Kodi" in the run report.
- **FR-022**: Identity discrepancies discovered through existing file links (Kodi identity now differs from the linked entry's identity) MUST be reported as conflicts, not applied automatically.

#### Run lifecycle, reporting, and preview

- **FR-023**: An import executes as a recorded run with a lifecycle (pending → running → completed/failed), start/finish timestamps, summary counters, and a per-item outcome list, consistent with how scan and enrichment runs are recorded and browsed.
- **FR-024**: Only one import run may be active at a time; concurrent triggers are rejected with 409 Conflict.
- **FR-025**: Because large libraries may take minutes, triggering an import returns immediately with the run identifier; the admin polls the run for progress and the final report.
- **FR-026**: The run report MUST expose counters for at least: movies/shows/episodes created, existing entries reused, files linked, unmatched paths, mapped-but-not-scanned, conflicts, no longer in Kodi, needs review, skipped music videos; counters MUST reconcile with the per-item detail list.
- **FR-027**: The per-item detail list MUST be paged and MUST include Kodi title, media kind, outcome, and reason for non-success outcomes; run history MUST be browsable newest-first.
- **FR-028**: System MUST support a preview mode that performs full validation and outcome projection without persisting anything and without provider traffic (items requiring identity lookup are reported as such).
- **FR-029**: All import endpoints MUST wrap responses in the standard response envelope and expose pagination metadata consistent with existing admin list endpoints.

#### Security and compliance

- **FR-030**: All import, preview, report, and path-mapping management endpoints MUST require the administrator role.
- **FR-031**: The uploaded database MUST be handled as untrusted input: validated before parsing, processed read-only, and never executed against the application's own database. (No GPL-licensed Kodi code is copied; only documented schema facts are used, consistent with the repository's scanner policy.)

### Key Entities *(new concepts and reused ones — functional view)*

- **Import Run (new)**: one execution of the import (or preview), with mode, status, timestamps, the source file name/version, summary counters, and per-item outcomes.
- **Import Item Outcome (new)**: per Kodi library item result — kind, title, outcome (created / reused / linked / unmatched / conflict / skipped / needs review / no longer in Kodi), and reason.
- **Path Mapping (new)**: an ordered Kodi-prefix → NAS-prefix translation rule, admin-managed and reusable across runs.
- **Reused**: Media (Film/TvShow entries), Media File (scanner-discovered physical files), Season / Episode / episode-to-file link, Stack Group (multi-part movies), Review workflow (unresolved identities).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Kodi file references whose translated path exactly matches a scanner-known file are linked during import — zero missed matches on the reference fixture (movies, stack parts, single- and multi-episode files).
- **SC-002**: Importing the same database twice in a row yields zero creations, zero link changes, and zero metadata changes on the second run (verified by automated test on a fixture containing movies, shows, stacked and multi-episode items).
- **SC-003**: Zero duplicate Media entries by kind + TMDB identity across any combination of scanner-created, manually created, and import-created entries (verified by mixed-origin fixture).
- **SC-004**: 100% of link conflicts and identity discrepancies are reported and leave existing links untouched — zero silent reassignments.
- **SC-005**: 100% of invalid uploads (non-SQLite, music DB, unsupported version, corrupt, oversized, empty library file structure) are rejected with a specific reason and zero persisted side effects.
- **SC-006**: On a reference database of ~5,000 library items, a full import completes in under 10 minutes when at most 500 items require provider identity lookups; a preview of the same database completes in under 2 minutes (no provider traffic).
- **SC-007**: Zero non-administrator callers can trigger, preview, or inspect imports (authorization tests on every new endpoint).
- **SC-008**: For every completed run, the summary counters reconcile exactly with the per-item detail list (no item unaccounted for).

## Assumptions

- The uploaded database originates from a single Kodi instance and reflects its library at copy time; the admin is responsible for copying the file in a consistent state (ideally with Kodi closed).
- A Media entry fundamentally requires a TMDB identity in the current product; Kodi items without a resolvable identity go to the review queue rather than creating placeholder entries (consistent with the scanner's needs-review behavior).
- Contacting the metadata provider during import (to resolve IMDB/TVDB identifiers or title+year searches) is acceptable, consistent with scanner behavior; items carrying a direct TMDB identifier require no lookup.
- The scanner remains the sole discovery mechanism for physical files; import consumes scanner output and never probes the NAS itself.
- Path comparison semantics (case-insensitivity, separator normalization) follow the scanner's existing conventions.
- The import is strictly one-way (Kodi → app); nothing is ever written back to the uploaded file or to Kodi.
- Default conflict policy is "preserve existing link and report" (FR-017/FR-022); making conflicts admin-resolvable with a "Kodi wins" action is a candidate follow-up, not required here.
- Episodes are imported for the seasons present in Kodi, including season 0; completeness reporting continues to ignore specials per the existing rule.
- Importing watched status, play counts, resume points, and personal ratings is **excluded by default** because Kodi is single-user while the app tracks watch state per user (see Open Questions).

## Out of Scope

- Kodi **music** database (`MyMusic*.db`) and **music videos** present in the video database.
- Kodi watched status / play counts / last-played dates / resume bookmarks / user ratings (pending decision — see Open Questions).
- Kodi artwork (poster/fanart URLs) and trailers — TMDB enrichment owns imagery.
- Kodi movie sets/collections (no collection concept in the app today).
- PVR recordings, live-TV entries, and any non-filesystem Kodi content.
- Connecting to a running Kodi instance, Kodi JSON-RPC, or any continuous/scheduled synchronization — import is strictly on-demand file upload.
- Any write-back to Kodi or modification of the uploaded file.
- A resolution workflow for import conflicts beyond reporting them (manual resolution via the existing link/unlink endpoints is the interim path).
- Frontend/UI work; scanner or enrichment redesign.

## Open Questions

1. **Supported schema versions** — Recommended: 119, 121, 131 (Kodi 19/20/21); the user cited `MyVideos121.db`, so 121 is mandatory. Options: (a) this set, rejecting others with the detected version named; (b) 121+ only; (c) attempt best-effort parsing of any version ≥ 119 with a warning. Which set should be supported?
2. **Watched status import** — Kodi stores play counts and last-played dates per file; the app tracks watch state per user. Options: (a) out of scope entirely (default retained above); (b) optionally map Kodi "watched" to a designated app user during import; (c) defer to a follow-up feature. Which do you want?
3. **Items removed from Kodi** — Default retained: leave app data untouched and report. Alternatives: (b) flag them for admin review; (c) automatically unlink their files. Confirm the default or pick an alternative.
4. **Conflict policy** — Default retained: existing links always win; conflicts are only reported. Alternatives: (b) "Kodi wins" option per run; (c) route conflicts into the review queue for one-click resolution. Confirm or change.
5. **Upload size limit** — Recommended: configurable, default 100 MB (typical video databases are 1–50 MB). Is a different default needed?
6. **Path-mapping management** — Recommended: persisted admin-managed mappings reused across runs, with optional per-upload overrides. Alternative: per-upload only (nothing persisted). Preference?
7. **Uploaded file retention** — Recommended: the file is processed transiently and discarded when the run reaches a terminal state; only the run report persists. Alternative: retain the file for audit with an expiry. Preference?
8. **Re-identified items** — When an item's TMDB identity changes in Kodi and no file link connects it to the old entry, the import creates a separate entry (old one left for manual cleanup). Options: (a) accept this limitation (default retained); (b) remember Kodi item identities between runs to detect and report re-identification precisely. Is the simple behavior acceptable?

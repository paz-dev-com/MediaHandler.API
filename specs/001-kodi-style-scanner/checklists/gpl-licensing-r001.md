# Checklist: GPL-2.0 / R-001 Clean-Room Compliance

**Purpose**: Validate that no GPL-2.0 source from Kodi (xbmc-master) is copied into MediaHandler, and that the clean-room re-derivation policy (R-001) is provably enforced on every PR touching the scanner.
**Scope**: Any PR that touches `MediaHandler.Infrastructure/Nas/Scanner/**`, `KodiRegexCatalog.cs`, parser/stacking/exclusion logic, or NFO mapping.
**How to use**: Reviewer ticks every item before approving the PR. If any box cannot be ticked, request changes. Author must self-check before requesting review.

## Source Isolation (R-001)

- [ ] CHK001 - PR diff contains **zero** files with extensions `.cpp`, `.h`, `.hpp`, `.cc`, `.cxx` originating from `/home/tpfeifer/Repos/xbmc-master/` (verify via `git diff --name-only` and path inspection)
- [ ] CHK002 - PR diff contains **zero** verbatim string blocks ≥ 5 consecutive lines copy-pasted from any file under `/home/tpfeifer/Repos/xbmc-master/xbmc/` (spot-check with `grep -F` against `VideoInfoScanner.cpp`, `RegExp.h`, `AdvancedSettings.cpp`)
- [ ] CHK003 - No build, project, or csproj reference points at `xbmc-master/` paths (search `*.csproj`, `*.sln`, `Directory.Build.*`)
- [ ] CHK004 - No runtime dependency on the Kodi binary, Kodi DLL, or any Kodi-linked NuGet package is introduced (review `<PackageReference>` additions)
- [ ] CHK005 - No copied media files, sample fixtures, or test data carry Kodi license headers (`grep -r "GPL-2.0\|GNU General Public" tests/`)

## Per-Regex Provenance (T002, T053–T056)

- [ ] CHK006 - Every regex pattern added/modified in `MediaHandler.Infrastructure/Nas/Scanner/KodiRegexCatalog.cs` carries a `// SOURCE:` comment naming (a) the Kodi behavior it re-implements, (b) the human author who re-derived it, (c) the date
- [ ] CHK007 - Each `// SOURCE:` comment explicitly states the pattern was re-authored from observed behavior / documentation, NOT transcribed from Kodi source (FR-024, R-001)
- [ ] CHK008 - Every regex has at least one corresponding parity unit test asserting input → expected output (cross-ref T053–T056, T062–T066)
- [ ] CHK009 - No regex contains identifier names, capture-group names, or comment fragments lifted from Kodi source files (e.g., `m_tvshowMatcher`, `g_advancedSettings`)

## Policy Documentation (T002, T112)

- [ ] CHK010 - `MediaHandler.Infrastructure/Nas/Scanner/README.md` exists and states the R-001 clean-room policy verbatim (no Kodi source copy, re-derivation only, per-regex `// SOURCE:` requirement)
- [ ] CHK011 - Scanner/README.md lists the reviewer's R-001 responsibilities (this checklist) and links to it
- [ ] CHK012 - Top-level repository LICENSE / NOTICE is unchanged or — if changed — does NOT add GPL-2.0 attribution implying derivative status
- [ ] CHK013 - No file in the PR adds a GPL-2.0 SPDX header (`SPDX-License-Identifier: GPL-2.0`) to MediaHandler code

## Reviewer Sign-Off

- [ ] CHK014 - Reviewer has opened `KodiRegexCatalog.cs` side-by-side with the PR diff and confirmed each new/changed pattern personally
- [ ] CHK015 - Reviewer has run `git log --diff-filter=A --name-only` on new files in `Scanner/` to confirm none originated outside this repo
- [ ] CHK016 - If the author consulted Kodi source for behavioral observation, the PR description names which Kodi feature was observed and confirms only behavior (not code) was used
- [ ] CHK017 - Final reviewer sign-off comment explicitly states: "R-001 clean-room verified" before merge


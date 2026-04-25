#!/usr/bin/env bash
set -euo pipefail

# GitHub Issues creation script for 001-kodi-style-scanner tasks
# Generated from tasks.md

REPO="paz-dev-com/MediaHandler.API"
COMMON_LABELS="spec:001"
CREATED_FILE="/tmp/gh-issues-created.log"
> "$CREATED_FILE"

create_issue() {
  local title="$1"
  local body="$2"
  local labels="$3"
  
  echo "Creating: $title"
  local url
  url=$(gh issue create --repo "$REPO" --title "$title" --body "$body" --label "$labels" 2>&1)
  echo "$title -> $url" >> "$CREATED_FILE"
  echo "  ✓ $url"
  sleep 0.5
}

echo "=== Phase 1: Setup (Shared Infrastructure) ==="

create_issue "T001: Create directory skeleton for scanner feature" \
"## Task
Create directory skeleton for the Kodi-style scanner feature.

### Directories to create
- \`MediaHandler.Infrastructure/Nas/Scanner/\`
- \`MediaHandler.Application/Features/Scan/{Commands,Queries}\`
- \`MediaHandler.Application/Features/LibraryRoots/{Commands,Queries}\`
- \`MediaHandler.Application/Features/Review/{Commands,Queries}\`
- \`MediaHandler.API/Contracts/Admin/\`
- \`MediaHandler.Tests/Scanner/\`
- \`MediaHandler.Tests/Features/{Scan,Review,LibraryRoots}/\`
- \`MediaHandler.IntegrationTests/Scanner/Fixtures/\`

Add a \`.gitkeep\` where the folder will not yet contain a file.

### Acceptance Criteria
- [ ] All directories exist
- [ ] \`.gitkeep\` files added where no source file exists yet
- [ ] Solution still builds cleanly

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T001
- [plan.md](../specs/001-kodi-style-scanner/plan.md) — Project Structure" \
"$COMMON_LABELS,phase:1-setup,type:setup,size:xs"

create_issue "T002: Author Scanner README with R-001 clean-room policy" \
"## Task
Author \`MediaHandler.Infrastructure/Nas/Scanner/README.md\` restating the **R-001 clean-room policy**.

### Requirements
- No verbatim copy of GPL Kodi source
- Derivation only from documented behavior + observed black-box behavior of the Kodi reference
- Include an in-file checklist every PR touching \`Scanner/\` must satisfy

### Acceptance Criteria
- [ ] README.md exists in Scanner directory
- [ ] Clean-room policy clearly stated
- [ ] PR checklist included

### References
- [research.md](../specs/001-kodi-style-scanner/research.md) — R-001
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T002" \
"$COMMON_LABELS,phase:1-setup,documentation,size:xs,parallelizable"

create_issue "T003: Add benchmark fixture schema for integration tests" \
"## Task
Add \`MediaHandler.IntegrationTests/Scanner/Fixtures/benchmark.schema.md\` describing the YAML manifest format consumed by \`FixtureBuilder\`.

### Schema must describe
- Paths
- Expected classification
- Expected TMDB id
- Expected exclusion reason

### References
- [quickstart.md](../specs/001-kodi-style-scanner/quickstart.md) §1.2
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T003" \
"$COMMON_LABELS,phase:1-setup,documentation,type:test,size:xs,parallelizable"

create_issue "T004: Extend integration-test factory with WithFakeNasService hook" \
"## Task
Extend the integration-test web-app factory (\`MediaHandler.IntegrationTests/Common/\`) with a \`WithFakeNasService(...)\` hook so scanner tests can substitute \`INasService\` with an in-memory tree without touching Freebox code.

### Acceptance Criteria
- [ ] \`WithFakeNasService\` method available on test factory
- [ ] Scanner tests can inject fake NAS file trees
- [ ] Existing tests still pass

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T004" \
"$COMMON_LABELS,phase:1-setup,type:test,layer:infrastructure,size:s,parallelizable"

echo ""
echo "=== Phase 2: Foundational — Domain Enums ==="

create_issue "T005: Create LibraryRootKind enum" \
"## Task
Create \`MediaHandler.Domain/Enums/LibraryRootKind.cs\` with values: \`Movies | TvShows | Mixed\`.

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T005" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T006: Create ScanMode enum" \
"## Task
Create \`MediaHandler.Domain/Enums/ScanMode.cs\` with values: \`Full | Incremental\`.

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T006" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T007: Create ScanStatus enum" \
"## Task
Create \`MediaHandler.Domain/Enums/ScanStatus.cs\` with values: \`Pending | Running | Completed | Failed | Cancelled\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T007" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T008: Create ScanDecisionKind enum" \
"## Task
Create \`MediaHandler.Domain/Enums/ScanDecisionKind.cs\` with values: \`Added | Updated | Unchanged | Removed | Excluded | NeedsReview\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T008" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T009: Create ReviewStatus enum" \
"## Task
Create \`MediaHandler.Domain/Enums/ReviewStatus.cs\` with values: \`Open | Resolved | Dismissed\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T009" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T010: Create ReviewReason enum" \
"## Task
Create \`MediaHandler.Domain/Enums/ReviewReason.cs\` with values: \`NoTmdbResult | MultipleCandidates | YearMismatch | UnparseableEpisode | NfoMalformed | UnknownFormat\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T010" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T011: Create MediaFileRole enum" \
"## Task
Create \`MediaHandler.Domain/Enums/MediaFileRole.cs\` with values: \`Main | StackedPart | Episode\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T011" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T012: Create ReviewResolutionAction enum" \
"## Task
Create \`MediaHandler.Domain/Enums/ReviewResolutionAction.cs\` with values: \`Assign | Dismiss | Delete\`.

Referenced by the review-items contract.

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T012" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

echo ""
echo "=== Phase 2: Foundational — Domain Entities ==="

create_issue "T013: Create LibraryRoot entity" \
"## Task
Create \`MediaHandler.Domain/Entities/LibraryRoot.cs\` per data-model.md, inheriting \`BaseEntity\`.

### Properties
- Path
- Kind (LibraryRootKind)
- Label
- IsEnabled

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T013" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T014: Create ScanRun entity" \
"## Task
Create \`MediaHandler.Domain/Entities/ScanRun.cs\` inheriting \`BaseEntity\`.

### Properties
- Mode (ScanMode)
- Status (ScanStatus)
- StartedAt, FinishedAt
- FailureReason
- LibraryRootIds (JSON)
- Denormalized count columns (TotalDiscovered, Added, Updated, Unchanged, Removed, Excluded, NeedsReview)

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T014" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T015: Create ScanItemDecision entity" \
"## Task
Create \`MediaHandler.Domain/Entities/ScanItemDecision.cs\` inheriting \`BaseEntity\`.

### Properties
- ScanRunId FK
- FilePath
- Kind (ScanDecisionKind)
- Reason
- RuleId
- MediaFileId?

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T015" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T016: Create ReviewItem entity" \
"## Task
Create \`MediaHandler.Domain/Entities/ReviewItem.cs\` inheriting \`BaseEntity\`.

### Properties
- FilePath, Reason (ReviewReason), Status (ReviewStatus)
- ParsedTitle, ParsedYear, ParsedSeason, ParsedEpisode
- Candidates (JSON)
- ResolvedTmdbId, ResolvedKind, ResolvedBy, ResolvedAt
- FirstSeenScanRunId

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T016" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:s,parallelizable"

create_issue "T017: Create ExclusionRule entity" \
"## Task
Create \`MediaHandler.Domain/Entities/ExclusionRule.cs\` inheriting \`BaseEntity\`.

### Properties
- Pattern, Kind, RuleId, Origin, IsEnabled

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T017" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T018: Create StackGroup entity" \
"## Task
Create \`MediaHandler.Domain/Entities/StackGroup.cs\` inheriting \`BaseEntity\`.

### Properties
- MediaId, FolderPath, Discriminator, PartCount

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T018" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T019: Create NfoMetadata entity" \
"## Task
Create \`MediaHandler.Domain/Entities/NfoMetadata.cs\` inheriting \`BaseEntity\`.

### Properties
- SourcePath, RawXml hash, ParsedTitle, ParsedYear, TmdbId, Kind, ParsedAt

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T019" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

create_issue "T020: Create EpisodeFileLink entity" \
"## Task
Create \`MediaHandler.Domain/Entities/EpisodeFileLink.cs\` inheriting \`BaseEntity\`.

### Properties
- TvEpisodeId, MediaFileId, OrdinalInFile

Many-to-many join for multi-episode files.

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T020" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs,parallelizable"

echo ""
echo "=== Phase 2: Foundational — Modified Domain Entities ==="

create_issue "T021: Modify Media entity — add Year, NfoMetadataId, ReviewState" \
"## Task
Modify \`MediaHandler.Domain/Entities/Media.cs\`:
- Add \`Year?\` (nullable int)
- Add \`NfoMetadataId?\` (nullable Guid FK)
- Add \`ReviewState\` (nullable ReviewStatus enum reference)

No behavior beyond data shape.

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T021" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:xs"

create_issue "T022: Modify MediaFile entity — add Fingerprint, MtimeUtc, StackGroupId, Role, LibraryRootId, scan tracking, MissingSince" \
"## Task
Modify \`MediaHandler.Domain/Entities/MediaFile.cs\`:
- Add \`Fingerprint\` (SHA-256 hex of size+mtime+absolute path normalized)
- Add \`MtimeUtc\`
- Add \`StackGroupId?\`
- Add \`Role\` (MediaFileRole)
- Add \`LibraryRootId?\`
- Add \`FirstSeenScanRunId\`, \`LastSeenScanRunId\`
- Add \`MissingSince?\`

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T022" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:s"

create_issue "T023: Modify TvEpisode entity — expose EpisodeFileLinks navigation" \
"## Task
Modify \`MediaHandler.Domain/Entities/TvEpisode.cs\`:
- Expose navigation collection \`EpisodeFileLinks\` (replaces single \`MediaFileId\`)
- Keep convenience \`PrimaryFile\` resolver

### References
- [data-model.md](../specs/001-kodi-style-scanner/data-model.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T023" \
"$COMMON_LABELS,phase:2-foundational,layer:domain,size:s"

echo ""
echo "=== Phase 2: Foundational — Application Interfaces ==="

create_issue "T024: Create IKodiNameParser interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/IKodiNameParser.cs\`.

### Methods
- \`MovieNameParseResult ParseMovie(string fullPath)\`
- \`EpisodeNameParseResult ParseEpisode(string fullPath, LibraryRootKind hint)\`

### References
- [plan.md](../specs/001-kodi-style-scanner/plan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T024" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T025: Create INfoParser interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/INfoParser.cs\`.

### Methods
- \`Task<NfoParseResult> ParseAsync(string nfoPath, CancellationToken ct)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T025" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T026: Create IStackingDetector interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/IStackingDetector.cs\`.

### Methods
- \`IReadOnlyList<StackGroupCandidate> Group(IEnumerable<NasFileEntry> filesInFolder)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T026" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T027: Create IExclusionEvaluator interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/IExclusionEvaluator.cs\`.

### Methods
- \`ExclusionVerdict Evaluate(NasFileEntry entry, ExclusionContext ctx)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T027" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T028: Create ITvEpisodeMatcher interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/ITvEpisodeMatcher.cs\`.

### Methods
- \`IReadOnlyList<EpisodeNumber> Match(string filename, EpisodeNumberingHint hint)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T028" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T029: Create INasFileEnumerator interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/INasFileEnumerator.cs\`.

### Methods
- \`IAsyncEnumerable<NasFileEntry> EnumerateAsync(LibraryRoot root, CancellationToken ct)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T029" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable,feature:nas"

create_issue "T030: Create IScanRunCoordinator interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/IScanRunCoordinator.cs\`.

### Methods
- \`Task<ScanRunHandle> StartAsync(StartScanRequest req, CancellationToken ct)\`
- \`Task RequestCancellationAsync(Guid id)\`
- \`ChannelReader<ScanProgressDto> Subscribe(Guid id)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T030" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T031: Create ITmdbMatcher interface" \
"## Task
Create \`MediaHandler.Application/Common/Interfaces/ITmdbMatcher.cs\`.

### Methods
- \`Task<TmdbMatchResult> ResolveAsync(MatchQuery q, CancellationToken ct)\`

Honouring R-001 precedence: NfoTmdbId → ExplicitTokenId → Title+Year → Title.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T031
- [research.md](../specs/001-kodi-style-scanner/research.md) — R-001" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable,feature:tmdb"

create_issue "T032: Modify IApplicationDbContext — add DbSets for new entities" \
"## Task
Modify \`MediaHandler.Application/Common/Interfaces/IApplicationDbContext.cs\` — add \`DbSet<>\` for all eight new entities.

### Dependencies
- Depends on T013–T020 (entity definitions)

### DbSets to add
- LibraryRoot, ScanRun, ScanItemDecision, ReviewItem
- ExclusionRule, StackGroup, NfoMetadata, EpisodeFileLink

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T032" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs"

echo ""
echo "=== Phase 2: Foundational — Shared DTOs ==="

create_issue "T033: Create LibraryRootDto" \
"## Task
Create \`MediaHandler.Application/Common/DTOs/LibraryRootDto.cs\`.

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T033" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T034: Create ScanRunDto and ScanCountsDto" \
"## Task
Create \`MediaHandler.Application/Common/DTOs/ScanRunDto.cs\` + \`ScanCountsDto\`.

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T034" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T035: Create ScanProgressDto" \
"## Task
Create \`MediaHandler.Application/Common/DTOs/ScanProgressDto.cs\` — channel payload with phase, processed, total, last decision.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T035" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

create_issue "T036: Create ReviewItemDto and TmdbCandidateDto" \
"## Task
Create \`MediaHandler.Application/Common/DTOs/ReviewItemDto.cs\` + \`TmdbCandidateDto\`.

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T036" \
"$COMMON_LABELS,phase:2-foundational,layer:application,size:xs,parallelizable"

echo ""
echo "=== Phase 2: Foundational — Infrastructure Persistence ==="

create_issue "T037: Modify MediaHandlerDbContext — register new DbSets and configurations" \
"## Task
Modify \`MediaHandler.Infrastructure/Persistence/MediaHandlerDbContext.cs\` — register the eight new \`DbSet<>\`s and apply configurations from assembly.

### Dependencies
- Depends on T032

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T037" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:s"

create_issue "T038: Create LibraryRootConfiguration (EF)" \
"## Task
Create \`Persistence/Configurations/LibraryRootConfiguration.cs\`:
- Unique index on \`Path\`
- Max-length 1024

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T038" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs,parallelizable"

create_issue "T039: Create ScanRunConfiguration (EF) with filtered unique index" \
"## Task
Create \`Persistence/Configurations/ScanRunConfiguration.cs\`:
- Index on \`StartedAt\`
- **Filtered unique index** \`WHERE Status = 'Running'\` enforcing single active scan

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T039" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:s,parallelizable"

create_issue "T040: Create ScanItemDecisionConfiguration (EF)" \
"## Task
Create \`Persistence/Configurations/ScanItemDecisionConfiguration.cs\`:
- Index on \`ScanRunId\`, \`FilePath\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T040" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs,parallelizable"

create_issue "T041: Create ReviewItemConfiguration (EF)" \
"## Task
Create \`Persistence/Configurations/ReviewItemConfiguration.cs\`:
- Index on \`Status\`, \`FilePath\`
- JSON column for \`Candidates\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T041" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:s,parallelizable"

create_issue "T042: Create ExclusionRuleConfiguration (EF)" \
"## Task
Create \`Persistence/Configurations/ExclusionRuleConfiguration.cs\`:
- Index on \`RuleId\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T042" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs,parallelizable"

create_issue "T043: Create StackGroupConfiguration (EF)" \
"## Task
Create \`Persistence/Configurations/StackGroupConfiguration.cs\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T043" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs,parallelizable"

create_issue "T044: Create NfoMetadataConfiguration (EF)" \
"## Task
Create \`Persistence/Configurations/NfoMetadataConfiguration.cs\`.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T044" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs,parallelizable"

create_issue "T045: Create EpisodeFileLinkConfiguration (EF) with composite key" \
"## Task
Create \`Persistence/Configurations/EpisodeFileLinkConfiguration.cs\`:
- Composite key \`(TvEpisodeId, MediaFileId, OrdinalInFile)\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T045" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs,parallelizable"

create_issue "T046: Modify MediaConfiguration (EF) — new columns and indexes for T021" \
"## Task
Modify \`Persistence/Configurations/MediaConfiguration.cs\`:
- Column mapping + index for new fields from T021 (\`Year?\`, \`NfoMetadataId?\`, \`ReviewState\`)

### Dependencies
- Depends on T021

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T046" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:xs"

create_issue "T047: Modify MediaFileConfiguration (EF) — Fingerprint, LibraryRootId, MissingSince indexes" \
"## Task
Modify \`Persistence/Configurations/MediaFileConfiguration.cs\`:
- Index on \`Fingerprint\`, \`LibraryRootId\`, \`MissingSince\`
- Drop direct \`TvEpisodeId\` FK and replace with \`EpisodeFileLink\` join

### Dependencies
- Depends on T022

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T047" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:s"

create_issue "T048: Generate KodiScannerSchema EF migration" \
"## Task
Generate single migration \`MediaHandler.Infrastructure/Migrations/20260320000000_KodiScannerSchema.cs\` covering ALL schema deltas.

### Steps
\`\`\`bash
dotnet ef migrations add KodiScannerSchema --project MediaHandler.Infrastructure --startup-project MediaHandler.API
\`\`\`

Inspect generated SQL for the filtered unique index from T039 and amend with raw SQL if EF emits a non-filtered version.

### Dependencies
- Depends on T037–T047

### Acceptance Criteria
- [ ] Migration applies cleanly to Testcontainers SQL Server
- [ ] Filtered unique index on ScanRun.Status works correctly
- [ ] \`dotnet ef database update\` succeeds

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T048" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,priority:critical,size:m"

echo ""
echo "=== Phase 2: Foundational — Skeleton Implementations ==="

create_issue "T049: Scaffold NasFileEnumerator over INasService" \
"## Task
Scaffold \`MediaHandler.Infrastructure/Nas/NasFileEnumerator.cs\` implementing \`INasFileEnumerator\` over the existing \`INasService\`.

Returns the async stream, no exclusion logic yet.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T049" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,feature:nas,size:s,parallelizable"

create_issue "T050: Scaffold ScanRunCoordinator singleton" \
"## Task
Scaffold \`MediaHandler.Infrastructure/Services/ScanRunCoordinator.cs\`:
- Singleton
- Owns \`Dictionary<Guid, (CancellationTokenSource, Channel<ScanProgressDto>)>\`
- Methods throw \`NotImplementedException\` until US1 fills them

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T050" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:s,parallelizable"

create_issue "T051: Wire DI for all new interfaces and implementations" \
"## Task
Wire DI in \`MediaHandler.Infrastructure/DependencyInjection.cs\`:
- Register all interfaces from T024–T031
- Coordinator as singleton
- Enumerator as scoped
- Verify \`dotnet build\` passes solution-wide

### Dependencies
- Depends on T024–T031, T049, T050

### Acceptance Criteria
- [ ] All interfaces resolvable from DI
- [ ] \`dotnet build\` passes solution-wide

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T051" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,priority:critical,size:s"

create_issue "T052: Add startup recovery hook for orphaned Running scans" \
"## Task
Add startup recovery hook that, on application start, transitions any \`ScanRun.Status = Running\` rows to \`Failed\` with \`FailureReason = \"Process restarted before scan finished\"\`.

Per quickstart §6 last row.

### References
- [quickstart.md](../specs/001-kodi-style-scanner/quickstart.md) §6
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T052" \
"$COMMON_LABELS,phase:2-foundational,layer:infrastructure,size:s"

echo ""
echo "=== Phase 3: US1 — Tests (write FIRST, must FAIL) ==="

create_issue "T053: Author KodiNameParserTests — ≥60 movie + ≥40 TV patterns" \
"## Task
Author \`MediaHandler.Tests/Scanner/KodiNameParserTests.cs\` as a \`[Theory]\` table covering:
- ≥ 60 movie filename patterns
- ≥ 40 TV episode filename patterns

Derived clean-room per **R-001** (no strings from Kodi \`.cpp/.h\`). Document each row's source as \"Kodi wiki: file naming\" or \"observed default behaviour\" in XML doc comments.

### TDD
Tests MUST be written first and MUST FAIL before implementation lands.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T053
- [research.md](../specs/001-kodi-style-scanner/research.md) — R-001" \
"$COMMON_LABELS,phase:3-us1,type:test,size:l,parallelizable"

create_issue "T054: Author ExclusionEvaluatorTests" \
"## Task
Author \`MediaHandler.Tests/Scanner/ExclusionEvaluatorTests.cs\` covering:
- Video-extension allow-list
- Sample/trailer/extras/featurettes patterns
- \`Sample/\`/\`Extras/\`/\`Trailers/\`/\`Featurettes/\` subfolders
- Hidden files
- \`.nomedia\` subtree skip

Every row tied to a \`RuleId\`.

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T054" \
"$COMMON_LABELS,phase:3-us1,type:test,size:m,parallelizable"

create_issue "T055: Author StackingDetectorTests" \
"## Task
Author \`MediaHandler.Tests/Scanner/StackingDetectorTests.cs\` for:
- \`cd1/cd2\`, \`part1/part2\`, \`disc1/disc2\`, \`(a)/(b)\`, \`pt1/pt2\`

Expect a single \`StackGroupCandidate\` per pair.

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T055" \
"$COMMON_LABELS,phase:3-us1,type:test,size:s,parallelizable"

create_issue "T056: Author TvEpisodeMatcherTests" \
"## Task
Author \`MediaHandler.Tests/Scanner/TvEpisodeMatcherTests.cs\` for:
- \`SxxExx\`, \`SxxExx-Eyy\`, \`xXy\`, \`1x05\`
- Date-based \`YYYY.MM.DD\`
- Absolute-numbering fallback
- Multi-episode rows yield ≥ 2 \`EpisodeNumber\` outputs

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T056" \
"$COMMON_LABELS,phase:3-us1,type:test,size:m,parallelizable"

create_issue "T057: Author StartScanCommandHandlerTests" \
"## Task
Author \`MediaHandler.Tests/Features/Scan/StartScanCommandHandlerTests.cs\`:
- Happy path returns \`ScanRunHandle\`
- Second concurrent call returns \`Result.Conflict(\"SCAN_IN_PROGRESS\")\`

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T057" \
"$COMMON_LABELS,phase:3-us1,type:test,layer:application,size:s,parallelizable"

create_issue "T058: Author GetScanRunQueryHandlerTests" \
"## Task
Author \`MediaHandler.Tests/Features/Scan/GetScanRunQueryHandlerTests.cs\`:
- Returns mapped DTO
- Not-found returns \`Result.NotFound\`

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T058" \
"$COMMON_LABELS,phase:3-us1,type:test,layer:application,size:s,parallelizable"

create_issue "T059: Author AddLibraryRootCommandHandlerTests" \
"## Task
Author \`MediaHandler.Tests/Features/LibraryRoots/AddLibraryRootCommandHandlerTests.cs\`:
- Duplicate path → \`Conflict(\"LIBRARY_ROOT_DUPLICATE\")\`
- Path outside configured base paths → validation failure

### TDD
Tests MUST FAIL before implementation.

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T059" \
"$COMMON_LABELS,phase:3-us1,type:test,layer:application,size:s,parallelizable"

create_issue "T060: Author FullScanEndToEndTests — SC-001 classification accuracy ≥98%" \
"## Task
Author \`MediaHandler.IntegrationTests/Scanner/FullScanEndToEndTests.cs::Sc001_ClassificationAccuracy_AtLeast98Percent\`.

Uses Phase-1 fixture seed (filled by T064–T065), Testcontainers SQL Server + fake \`INasService\`.

### Success Criteria
SC-001: ≥ 98% auto-classification accuracy on benchmark library.

### TDD
Tests MUST FAIL before implementation.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-001
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T060" \
"$COMMON_LABELS,phase:3-us1,type:test,type:integration,size:m,parallelizable"

create_issue "T061: Author IncrementalScanIdempotencyTests — SC-005" \
"## Task
Author \`MediaHandler.IntegrationTests/Scanner/IncrementalScanIdempotencyTests.cs::Sc005_IncrementalScan_UnchangedAndFast\`:
- Second scan in \`Incremental\` mode against unchanged tree
- Must report Added=Updated=Removed=0
- Wall-clock < 25% of first scan

### Success Criteria
SC-005: Incremental re-scan < 25% of full scan time.

### TDD
Tests MUST FAIL before implementation.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-005
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T061" \
"$COMMON_LABELS,phase:3-us1,type:test,type:integration,size:m,parallelizable"

echo ""
echo "=== Phase 3: US1 — Implementation ==="

create_issue "T062: Implement KodiRegexCatalog — clean-room regex tables" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/KodiRegexCatalog.cs\`:
- Clean-room re-derived regex tables (movie cleanup tokens, year extractors, episode patterns, stacking suffixes)
- Each pattern carries an inline \`// SOURCE:\` comment naming the public Kodi behaviour it reproduces

**⚠️ R-001**: No string in this file may be copied verbatim from Kodi GPL files.

Makes T053 / T056 begin to pass.

### References
- [research.md](../specs/001-kodi-style-scanner/research.md) — R-001
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T062" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,feature:nas,priority:high,size:l"

create_issue "T063: Implement KodiNameParser consuming KodiRegexCatalog" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/KodiNameParser.cs\` consuming \`KodiRegexCatalog\`.
- Folder-name takes precedence over filename per spec edge case
- Makes all of T053 pass

### Dependencies
- Depends on T062

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — Edge Cases
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T063" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,feature:nas,priority:high,size:m"

create_issue "T064: Implement ExclusionEvaluator" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/ExclusionEvaluator.cs\`:
- Extension allow-list
- Regex sample/trailer matchers
- Folder-name exclusion set
- Hidden + \`.nomedia\` subtree handling
- Pull initial rule rows into a static \`ExclusionRule\` seed

Makes T054 pass.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-009, FR-010, FR-011
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T064" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,feature:nas,size:m,parallelizable"

create_issue "T065: Implement StackingDetector" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/StackingDetector.cs\`.

Makes T055 pass.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-007
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T065" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,feature:nas,size:s,parallelizable"

create_issue "T066: Implement TvEpisodeMatcher" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/TvEpisodeMatcher.cs\`.

Makes T056 pass.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-005, FR-006, FR-008
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T066" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,feature:nas,size:m,parallelizable"

create_issue "T067: Implement ScanPipeline orchestrator (TMDB stub)" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/ScanPipeline.cs\`:
- Orchestrate: enumerate → exclude → group(stacks) → parse(folder+file) → classify(movie/episode) → fingerprint → persist → emit ScanItemDecision
- **TMDB stage is a stub** recording every item as \`Added\` with \`TmdbId=null\` (US2 fills it)
- Fingerprint = \`SHA256(absPath|size|mtimeUnix)\` for idempotency

### References
- [plan.md](../specs/001-kodi-style-scanner/plan.md) — Project Structure
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T067" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,feature:nas,priority:high,size:l"

create_issue "T068: Flesh out ScanRunCoordinator — replace T050 stubs" \
"## Task
Flesh out \`MediaHandler.Infrastructure/Services/ScanRunCoordinator.cs\`:
- Replace T050 stubs
- Own \`Channel<ScanProgressDto>\`
- Run \`ScanPipeline\` on \`Task.Run\`
- Enforce single-active-scan via DB filtered index probe + in-memory mutex
- Support \`RequestCancellationAsync\`

### Dependencies
- Depends on T050, T067

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T068" \
"$COMMON_LABELS,phase:3-us1,layer:infrastructure,priority:high,size:m"

create_issue "T069: Implement AddLibraryRoot Command + Handler + Validator" \
"## Task
Implement \`MediaHandler.Application/Features/LibraryRoots/Commands/AddLibraryRoot/AddLibraryRootCommand.cs\` + Handler + Validator per contracts/library-roots.md.

### Validation
- Path non-empty, ≤ 1024 chars
- Path starts with configured NAS base paths
- Path unique among existing LibraryRoots
- Kind ∈ enum
- Label ≤ 200 chars

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T069" \
"$COMMON_LABELS,phase:3-us1,layer:application,feature:nas,size:m"

create_issue "T070: Implement RemoveLibraryRoot Command + Handler + Validator" \
"## Task
Implement \`Features/LibraryRoots/Commands/RemoveLibraryRoot/\` (Command + Handler + Validator):
- Soft-delete + \`MissingSince\` cascade
- \`Conflict(\"SCAN_IN_PROGRESS\")\` guard

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T070" \
"$COMMON_LABELS,phase:3-us1,layer:application,feature:nas,size:s,parallelizable"

create_issue "T071: Implement ListLibraryRoots Query (paginated)" \
"## Task
Implement \`Features/LibraryRoots/Queries/ListLibraryRoots/\`:
- Paginated
- Filters: \`kind\`, \`enabledOnly\`

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T071" \
"$COMMON_LABELS,phase:3-us1,layer:application,feature:nas,size:s,parallelizable"

create_issue "T072: Implement StartScan Command + Handler + Validator" \
"## Task
Implement \`Features/Scan/Commands/StartScan/StartScanCommand.cs\` + Handler + Validator:
- Distinct ids
- Existing + enabled roots
- Single-active enforcement → \`Conflict(\"SCAN_IN_PROGRESS\")\`

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T072" \
"$COMMON_LABELS,phase:3-us1,layer:application,priority:high,size:m"

create_issue "T073: Implement CancelScan Command" \
"## Task
Implement \`Features/Scan/Commands/CancelScan/\`:
- Looks up coordinator
- Calls \`RequestCancellationAsync\`
- Returns post-cancel summary

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T073" \
"$COMMON_LABELS,phase:3-us1,layer:application,size:s,parallelizable"

create_issue "T074: Implement GetScanRun Query with includeReview plumbing" \
"## Task
Implement \`Features/Scan/Queries/GetScanRun/\` with \`includeReview\` flag plumbing.

Review list returns null in this story; populated in US4.

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T074" \
"$COMMON_LABELS,phase:3-us1,layer:application,size:s,parallelizable"

create_issue "T075: Implement GetActiveScan Query" \
"## Task
Implement \`Features/Scan/Queries/GetActiveScan/\`.

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T075" \
"$COMMON_LABELS,phase:3-us1,layer:application,size:xs,parallelizable"

create_issue "T076: Create LibraryRootRequests API contract" \
"## Task
Create \`MediaHandler.API/Contracts/Admin/LibraryRootRequests.cs\` (\`AddLibraryRootRequest\`).

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T076" \
"$COMMON_LABELS,phase:3-us1,layer:api,size:xs,parallelizable"

create_issue "T077: Create ScanRequests and ScanResponses API contracts" \
"## Task
Create \`MediaHandler.API/Contracts/Admin/ScanRequests.cs\` (\`StartScanRequest\`) and \`ScanResponses.cs\` (\`ScanRunSummaryResponse\`, \`ScanRunDetailResponse\`).

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T077" \
"$COMMON_LABELS,phase:3-us1,layer:api,size:xs,parallelizable"

create_issue "T078: Implement AdminLibraryRootsController" \
"## Task
Implement \`MediaHandler.API/Controllers/AdminLibraryRootsController.cs\`:
- \`GET\`, \`POST\`, \`DELETE\` per contracts/library-roots.md
- \`[Authorize(Policy = \"AdminOnly\")]\`
- \`[EnableRateLimiting(\"fixed\")]\`
- \`[ApiVersion(\"1.0\")]\`
- Full \`[ProducesResponseType]\` set
- \`ApiResponse<T>\` envelope

### References
- [contracts/library-roots.md](../specs/001-kodi-style-scanner/contracts/library-roots.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T078" \
"$COMMON_LABELS,phase:3-us1,layer:api,type:endpoint,feature:nas,size:m"

create_issue "T079: Implement AdminScanController" \
"## Task
Implement \`MediaHandler.API/Controllers/AdminScanController.cs\`:
- \`POST /scan\` (returns 202 + Location)
- \`GET /scan/{id}\`
- \`GET /scan/active\`
- \`POST /scan/{id}/cancel\`
- Per contracts/scan.md
- \`[Authorize(Policy = \"AdminOnly\")]\`, \`ApiResponse<T>\` envelope

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T079" \
"$COMMON_LABELS,phase:3-us1,layer:api,type:endpoint,size:m"

create_issue "T080: Implement FixtureBuilder + benchmark.yaml (≥200 movies, ≥50 TV shows)" \
"## Task
Implement \`MediaHandler.IntegrationTests/Scanner/Fixtures/FixtureBuilder.cs\` + \`benchmark.yaml\` containing:
- ≥ 200 movies + ≥ 50 TV shows
- Layouts: per-folder + flat + stacked + multi-disc movies
- Season folders + Specials + multi-episode + 1x05 + date-based TV shows
- Exclusion bait, NFO sidecars, misnamed review-bait

Wire the fake \`INasService\` to read from this manifest.

### References
- [quickstart.md](../specs/001-kodi-style-scanner/quickstart.md) §1.1
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T080" \
"$COMMON_LABELS,phase:3-us1,type:test,type:integration,feature:nas,size:l"

create_issue "T081: Run T060+T061 to green — iterate on KodiRegexCatalog + ScanPipeline" \
"## Task
Run T060 + T061 integration tests to green:
- Iterate on \`KodiRegexCatalog\` + \`ScanPipeline\` to satisfy:
  - **SC-001**: ≥ 98% auto-classification
  - **SC-005**: Incremental < 25% of full scan

### Dependencies
- Depends on T060, T061, T062–T068, T080

### Acceptance Criteria
- [ ] SC-001 classification accuracy ≥ 98%
- [ ] SC-005 incremental scan time < 25% of full scan
- [ ] All US1 tests pass

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-001, SC-005
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T081" \
"$COMMON_LABELS,phase:3-us1,priority:high,size:l"

echo ""
echo "=== Phase 4: US2 — Tests ==="

create_issue "T082: Author TmdbMatcherTests" \
"## Task
Author \`MediaHandler.Tests/Scanner/TmdbMatcherTests.cs\` — table covering:
- Id-token wins over title+year
- Title+year wins over title
- Multi-candidate same-score → \`Reason=MultipleCandidates\`
- Year mismatch beyond ±1 → \`Reason=YearMismatch\`
- No result → \`Reason=NoTmdbResult\`
- Transient HTTP failure → matcher surfaces \`Result.Error\` without throwing

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T082" \
"$COMMON_LABELS,phase:4-us2,type:test,feature:tmdb,size:m,parallelizable"

create_issue "T083: Author ResolveReviewItemCommandHandlerTests" \
"## Task
Author \`MediaHandler.Tests/Features/Review/ResolveReviewItemCommandHandlerTests.cs\`:
- \`Assign\` with valid TMDB id → status \`Resolved\`, persists fields
- \`Assign\` against TMDB miss → \`Result.UnprocessableEntity(\"TMDB_ID_NOT_FOUND\")\`
- \`Dismiss\` → status \`Dismissed\`
- \`Delete\` → underlying \`MediaFile\` gone, orphans cleaned
- Non-Open item → \`Conflict(\"REVIEW_ALREADY_RESOLVED\")\`

### TDD
Tests MUST FAIL before implementation.

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T083" \
"$COMMON_LABELS,phase:4-us2,type:test,layer:application,size:m,parallelizable"

create_issue "T084: Extend FullScanEndToEndTests — SC-002 silent misclassification ≤0.5%" \
"## Task
Extend \`FullScanEndToEndTests.cs\` with \`Sc002_SilentMisclassRate_AtMost0p5Percent\`:
- Every divergence from fixture's expected \`(tmdbId, kind, season, episode)\` MUST also yield a \`ReviewItem\` for the same path
- Otherwise it counts as silent misclassification

### Success Criteria
SC-002: ≤ 0.5% silent misclassification rate.

### TDD
Tests MUST FAIL before implementation.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-002
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T084" \
"$COMMON_LABELS,phase:4-us2,type:test,type:integration,feature:tmdb,size:m,parallelizable"

create_issue "T085: Author ReviewQueueResolutionTests — round-trip scan→review→resolve→re-scan" \
"## Task
Author \`MediaHandler.IntegrationTests/Scanner/ReviewQueueResolutionTests.cs\`:
- Scan → review item created
- \`POST /api/v1/admin/review-items/{id}/resolve\` Assign
- Next scan respects the resolution (no re-flag, mapped via saved id without TMDB title query)

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T085" \
"$COMMON_LABELS,phase:4-us2,type:test,type:integration,feature:tmdb,size:m,parallelizable"

echo ""
echo "=== Phase 4: US2 — Implementation ==="

create_issue "T086: Implement TmdbMatcher with LRU cache and ambiguity policy" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/TmdbMatcher.cs\`:
- Wraps existing \`ITmdbService\`
- In-process LRU cache keyed by \`(query, year, kind)\`
- Per-scan dedup
- Precedence per **R-001**: NfoTmdbId → ExplicitTokenId → Title+Year → Title
- Ambiguity policy: ≥ 2 candidates within 5% popularity score → \`MultipleCandidates\`
- Year mismatch > ±1 → \`YearMismatch\`
- Tolerates transient failures (FR-017)

### References
- [research.md](../specs/001-kodi-style-scanner/research.md) — R-001
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-017
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T086" \
"$COMMON_LABELS,phase:4-us2,layer:infrastructure,feature:tmdb,priority:high,size:l"

create_issue "T087: Extend TmdbService with id-based and multi-candidate lookups" \
"## Task
Extend \`MediaHandler.Infrastructure/Tmdb/TmdbService.cs\` (and \`ITmdbService\`) with:
- Id-based movie/show lookup
- Episode lookup \`(showId, season, episode)\`
- Multi-candidate search variant returning popularity score + poster path for \`TmdbCandidateDto\`

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T087" \
"$COMMON_LABELS,phase:4-us2,layer:infrastructure,feature:tmdb,size:m"

create_issue "T088: Replace TMDB stub in ScanPipeline with real ITmdbMatcher" \
"## Task
Replace the TMDB stub from T067 inside \`ScanPipeline.cs\` with a real \`ITmdbMatcher\` call:
- On \`MultipleCandidates\`/\`NoTmdbResult\`/\`YearMismatch\`/\`UnparseableEpisode\`/\`UnknownFormat\`: create \`ReviewItem\` (status \`Open\`) with parsed fields + candidates
- On success: persist \`Media\`/\`TvShow\`/\`TvEpisode\` link via \`EpisodeFileLink\` for multi-episode files

### Dependencies
- Depends on T067, T086

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T088" \
"$COMMON_LABELS,phase:4-us2,layer:infrastructure,feature:tmdb,priority:high,size:l"

create_issue "T089: Implement ResolveReviewItem Command + Handler + Validator" \
"## Task
Implement \`Features/Review/Commands/ResolveReviewItem/\` (Command + Handler + Validator) per contracts/review-items.md:
- Supports \`Assign | Dismiss | Delete\`
- \`Assign\` re-runs only the TMDB-resolution stage using supplied id
- Persists resolution for future scan honoring
- Writes \"resolution memory\" so subsequent scans don't re-flag

### Dependencies
- Depends on T032/T037/T048 (Foundational DB)

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T089" \
"$COMMON_LABELS,phase:4-us2,layer:application,feature:tmdb,size:m"

create_issue "T090: Implement ListReviewItems Query (paginated)" \
"## Task
Implement \`Features/Review/Queries/ListReviewItems/\`:
- Paginated
- Filters: \`status\` (default \`Open\`), \`reason\`, \`scanRunId\`

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T090" \
"$COMMON_LABELS,phase:4-us2,layer:application,size:s,parallelizable"

create_issue "T091: Create ReviewRequests API contract" \
"## Task
Create \`MediaHandler.API/Contracts/Admin/ReviewRequests.cs\` (\`ResolveReviewRequest\`).

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T091" \
"$COMMON_LABELS,phase:4-us2,layer:api,size:xs,parallelizable"

create_issue "T092: Implement AdminReviewController" \
"## Task
Implement \`MediaHandler.API/Controllers/AdminReviewController.cs\`:
- \`GET /review-items\`
- \`POST /review-items/{id}/resolve\`
- Per contracts/review-items.md
- \`[Authorize(Policy = \"AdminOnly\")]\`, \`ApiResponse<T>\` envelope
- \`[ProducesResponseType]\` for 200/400/401/403/404/409/422

### References
- [contracts/review-items.md](../specs/001-kodi-style-scanner/contracts/review-items.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T092" \
"$COMMON_LABELS,phase:4-us2,layer:api,type:endpoint,size:m"

create_issue "T093: Implement scan-pipeline read-back of resolved ReviewItems" \
"## Task
Implement scan-pipeline read-back of resolved \`ReviewItem\`s so a subsequent scan matching the same \`FilePath\` (or fingerprint) reuses the saved \`(TmdbId, Kind)\` without re-querying TMDB title search.

Closes the loop tested by T085.

### Dependencies
- Depends on T088, T089

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T093" \
"$COMMON_LABELS,phase:4-us2,layer:infrastructure,feature:tmdb,size:m"

create_issue "T094: Run T084 to green — tune TmdbMatcher thresholds for SC-002" \
"## Task
Run T084 to green. Tune \`TmdbMatcher\` thresholds (popularity gap, year tolerance) until SC-002 (≤ 0.5% silent misclassification) is satisfied on the benchmark fixture.

### Dependencies
- Depends on T084, T086–T088

### Acceptance Criteria
- [ ] SC-002 ≤ 0.5% silent misclassification rate
- [ ] All US2 tests pass

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-002
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T094" \
"$COMMON_LABELS,phase:4-us2,priority:high,feature:tmdb,size:m"

echo ""
echo "=== Phase 5: US3 — Tests ==="

create_issue "T095: Author NfoParserTests" \
"## Task
Author \`MediaHandler.Tests/Scanner/NfoParserTests.cs\`:
- Well-formed \`movie.nfo\` with \`<tmdbid>\` parsed correctly
- Malformed XML returns \`NfoParseResult.Malformed\` (not throws)
- Missing optional fields return null without failing
- \`tvshow.nfo\` and per-episode \`.nfo\` shapes covered

### TDD
Tests MUST FAIL before implementation.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-012, FR-013
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T095" \
"$COMMON_LABELS,phase:5-us3,type:test,size:m,parallelizable"

create_issue "T096: Add NFO override integration scenario to FullScanEndToEndTests" \
"## Task
Add \`Sc_Nfo_OverridesFilenameGuess\` integration scenario to \`FullScanEndToEndTests.cs\` covering acceptance scenarios 1–3 of US3.

### TDD
Tests MUST FAIL before implementation.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — US3 acceptance scenarios
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T096" \
"$COMMON_LABELS,phase:5-us3,type:test,type:integration,size:s,parallelizable"

echo ""
echo "=== Phase 5: US3 — Implementation ==="

create_issue "T097: Implement NfoParser using XDocument" \
"## Task
Implement \`MediaHandler.Infrastructure/Nas/Scanner/NfoParser.cs\` using \`XDocument\`:
- Tolerant of unknown elements
- Returns \`NfoParseResult { Title, Year, TmdbId, Kind, ParsedSuccessfully, Warning? }\`

Makes T095 pass.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-012, FR-013
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T097" \
"$COMMON_LABELS,phase:5-us3,layer:infrastructure,size:m"

create_issue "T098: Wire NFO discovery into ScanPipeline" \
"## Task
Wire NFO discovery into \`ScanPipeline\`:
- Per-folder: discover \`movie.nfo\`/\`tvshow.nfo\`
- Per-file: discover \`<basename>.nfo\`
- On parse success: persist \`NfoMetadata\` row, attach to \`Media\`, surface to \`ITmdbMatcher\` precedence chain
- On \`Warning\`: write \`ScanItemDecision\` (\`Kind=NeedsReview, Reason=NfoMalformed\`) ONLY if filename fallback also fails; otherwise emit Serilog warning + proceed

### Dependencies
- Depends on T067, T086

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T098" \
"$COMMON_LABELS,phase:5-us3,layer:infrastructure,feature:nas,size:m"

create_issue "T099: Add NFO override precedence tests to KodiNameParserTests + TmdbMatcherTests" \
"## Task
Add override-precedence rows to \`KodiNameParserTests\` / \`TmdbMatcherTests\` ensuring NFO id always wins (per plan US3 mapping note).

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T099" \
"$COMMON_LABELS,phase:5-us3,type:test,feature:tmdb,size:s"

create_issue "T100: Run T096 to green — NFO integration tests pass" \
"## Task
Run T096 to green. NFO escape-hatch works; libraries with curated NFOs map deterministically.

### Dependencies
- Depends on T096, T097, T098

### Acceptance Criteria
- [ ] NFO override integration tests pass
- [ ] Malformed NFO falls back gracefully

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T100" \
"$COMMON_LABELS,phase:5-us3,size:s"

echo ""
echo "=== Phase 6: US4 — Tests ==="

create_issue "T101: Add SC-006 diagnosability test — any file diagnosable under 30s" \
"## Task
Add \`Sc006_AnyFileDiagnosable_Under30Seconds\` to \`FullScanEndToEndTests.cs\`:
- For every fixture file path, GET-detail response (with \`includeReview=true\`) MUST contain either a matching \`MediaFile\` reference OR a \`ScanItemDecision\` row OR a \`ReviewItem\` row
- Each must carry \`Reason\` / \`RuleId\`
- Assert presence in O(1) lookup per path

### Success Criteria
SC-006: Admin can diagnose any file in < 30 seconds.

### TDD
Tests MUST FAIL before implementation.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-006
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T101" \
"$COMMON_LABELS,phase:6-us4,type:test,type:integration,size:m,parallelizable"

create_issue "T102: Unit test GetScanRunQueryHandler — IncludeReview returns open items" \
"## Task
Unit test \`MediaHandler.Tests/Features/Scan/GetScanRunQueryHandlerTests.cs::IncludeReview_ReturnsOpenItemsForRun\` (extension of T058).

### TDD
Tests MUST FAIL before implementation.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T102" \
"$COMMON_LABELS,phase:6-us4,type:test,layer:application,size:s,parallelizable"

echo ""
echo "=== Phase 6: US4 — Implementation ==="

create_issue "T103: Extend GetScanRun handler — includeReview returns open ReviewItems" \
"## Task
Extend \`GetScanRun\` query handler + DTO mapping so \`includeReview=true\` returns up to 100 most recent open \`ReviewItem\`s scoped to the run (per contracts/scan.md).

### Dependencies
- Depends on T074

### References
- [contracts/scan.md](../specs/001-kodi-style-scanner/contracts/scan.md)
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T103" \
"$COMMON_LABELS,phase:6-us4,layer:application,size:s"

create_issue "T104: Ensure ScanPipeline writes ScanItemDecision for every processed path" \
"## Task
Ensure \`ScanPipeline\` writes a \`ScanItemDecision\` row for **every** processed path:
- Added/Updated/Unchanged/Removed/Excluded/NeedsReview
- Include \`RuleId\` for exclusions
- Include \`Reason\` for review items (FR-023 data-side)

### Dependencies
- Depends on T067, T088

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-023
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T104" \
"$COMMON_LABELS,phase:6-us4,layer:infrastructure,feature:nas,size:m"

create_issue "T105: Implement removed-file detection with NAS-unreachable safeguard" \
"## Task
Implement removed-file detection:
- At end of scan, every \`MediaFile\` belonging to a scanned \`LibraryRoot\` whose \`LastSeenScanRunId != currentRunId\` gets \`MissingSince = UtcNow\` + \`ScanItemDecision { Kind=Removed }\` (FR-019)
- **Safeguard**: Failing root reads (NAS unreachable) MUST suppress this step for that root and write a single \`Reason=\"NAS unreachable\"\` decision instead — **no mass-removal on transient failure**

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-019
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T105" \
"$COMMON_LABELS,phase:6-us4,layer:infrastructure,feature:nas,priority:high,size:m"

create_issue "T106: Run T101, T102 to green — US4 tests pass" \
"## Task
Run T101, T102 to green.

### Dependencies
- Depends on T101, T102, T103, T104, T105

### Acceptance Criteria
- [ ] SC-006 diagnosability test passes
- [ ] All US4 tests pass
- [ ] All four user stories independently demonstrable

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T106" \
"$COMMON_LABELS,phase:6-us4,size:s"

echo ""
echo "=== Phase 7: Polish ==="

create_issue "T107: Add structured Serilog enrichers for scan pipeline (FR-023)" \
"## Task
Add structured Serilog enrichers in \`ScanPipeline\`:
- Every per-file decision logs at \`Information\` with properties \`{ScanRunId, FilePath, Kind, Reason?, RuleId?, TmdbId?}\`
- Stage transitions log at \`Debug\`
- Confirm log volume is bounded for 10,000-file scan (no per-file \`Warning+\` unless actual problem)

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — FR-023
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T107" \
"$COMMON_LABELS,phase:7-polish,layer:infrastructure,size:m"

create_issue "T108: Implement SC-003 exclusion fidelity test — no false positives or negatives" \
"## Task
Implement \`MediaHandler.IntegrationTests/Scanner/Sc003_ExclusionFidelity_NoFalsePositivesOrFalseNegatives\`:
- Every \`expected: excluded\` path → \`ScanItemDecision.Kind=Excluded\` + zero \`MediaFile\`
- Every \`expected: included\` → \`MediaFile\` exists

### Success Criteria
SC-003: 100% exclusion accuracy.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-003
- [quickstart.md](../specs/001-kodi-style-scanner/quickstart.md) — §SC-003
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T108" \
"$COMMON_LABELS,phase:7-polish,type:test,type:integration,size:m,parallelizable"

create_issue "T109: Implement SC-004 Kodi behavioral parity test (≥99%)" \
"## Task
Implement \`MediaHandler.IntegrationTests/Scanner/Sc004_KodiBehavioralParity\`:
- Run scanner over curated parity-fixture subset
- Paths annotated with observed Kodi classification recorded out-of-band
- Assert ≥ 99% matching outcomes

### Success Criteria
SC-004: ≥ 99% parity with Kodi behavior.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-004
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T109" \
"$COMMON_LABELS,phase:7-polish,type:test,type:integration,size:m,parallelizable"

create_issue "T110: Implement SC-007 manual correction reduction test (≥80%)" \
"## Task
Implement \`MediaHandler.IntegrationTests/Scanner/Sc007_ManualCorrectionReduction_AtLeast80Percent\`:
- Operate against synthetic benchmark + injected baseline number representing previous implementation's review count

### Success Criteria
SC-007: ≥ 80% reduction in manual corrections.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-007
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T110" \
"$COMMON_LABELS,phase:7-polish,type:test,type:integration,size:m,parallelizable"

create_issue "T111: Implement AdminAuthorizationTests (SC-008)" \
"## Task
Implement \`MediaHandler.IntegrationTests/Scanner/AdminAuthorizationTests.cs\` covering, for **every** endpoint in contracts:
- Anonymous → 401
- Authenticated non-admin (\`User\` role) JWT → 403 (explicit SC-008 case)
- Admin JWT → 2xx
- Anonymous \`POST /api/v1/admin/scan\` does **not** create any \`ScanRun\` row in the DB

### Endpoints covered
- All from \`contracts/scan.md\`
- All from \`contracts/library-roots.md\`
- All from \`contracts/review-items.md\`

### Dependencies
- Depends on T078, T079, T092 (controllers exist)

### Success Criteria
SC-008: Zero non-admin users can initiate scan or modify review items.

### References
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-008
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T111" \
"$COMMON_LABELS,phase:7-polish,type:test,type:integration,priority:high,size:m"

create_issue "T112: Update Scanner README — final regex source citations" \
"## Task
Update \`MediaHandler.Infrastructure/Nas/Scanner/README.md\` (final pass):
- List every regex/heuristic source citation accumulated in \`KodiRegexCatalog.cs\`
- Include the Kodi-version reference (commit hash from reference repo)

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T112" \
"$COMMON_LABELS,phase:7-polish,documentation,size:s,parallelizable"

create_issue "T113: Update README/CONTRIBUTING with scanner docs" \
"## Task
Update top-level \`README.md\` / \`CONTRIBUTING.md\` with:
- How to run a scan locally
- Where to add a failing parser case
- The no-GPL-paste rule

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T113" \
"$COMMON_LABELS,phase:7-polish,documentation,size:s,parallelizable"

create_issue "T114: Add scanner endpoints to Postman/Bruno collection" \
"## Task
If a Postman / Bruno collection exists in the repo, add the four new endpoint groups:
- scan, scan/{id}, scan/{id}/cancel, scan/active
- library-roots CRUD
- review-items list+resolve

With example admin JWT auth. If no such collection exists, skip and note in PR.

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T114" \
"$COMMON_LABELS,phase:7-polish,documentation,size:s,parallelizable"

create_issue "T115: Retire MediaFileNameParser — delete and clean up" \
"## Task
Retire \`MediaHandler.Infrastructure/Nas/MediaFileNameParser.cs\`:
- Confirm no callers remain in the \`main\` solution graph (only test fixtures may keep references during transition)
- Delete the file and remove DI registration
- Run \`dotnet build\` and full test suite

### Acceptance Criteria
- [ ] No callers of \`MediaFileNameParser\` remain
- [ ] File deleted
- [ ] DI registration removed
- [ ] \`dotnet build\` passes
- [ ] Full test suite passes

### References
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T115" \
"$COMMON_LABELS,phase:7-polish,type:refactor,layer:infrastructure,size:s"

create_issue "T116: Final benchmark pass — capture SC-001..SC-008 report" \
"## Task
Execute \`quickstart.md\` end-to-end against the Phase-3 fixture. Capture SC-001..SC-008 numbers in a markdown report under \`specs/001-kodi-style-scanner/benchmark-report.md\`.

### Acceptance Criteria
- [ ] \`benchmark-report.md\` created with all SC metrics
- [ ] SC-001: ≥ 98% classification accuracy
- [ ] SC-002: ≤ 0.5% silent misclassification
- [ ] SC-003: 100% exclusion accuracy
- [ ] SC-004: ≥ 99% Kodi parity
- [ ] SC-005: Incremental < 25% of full scan time
- [ ] SC-006: Any file diagnosable < 30s
- [ ] SC-007: ≥ 80% reduction in manual corrections
- [ ] SC-008: Zero non-admin access

### References
- [quickstart.md](../specs/001-kodi-style-scanner/quickstart.md)
- [spec.md](../specs/001-kodi-style-scanner/spec.md) — SC-001..SC-008
- [tasks.md](../specs/001-kodi-style-scanner/tasks.md) — T116" \
"$COMMON_LABELS,phase:7-polish,priority:high,size:m"

echo ""
echo "=== DONE ==="
echo "Total issues created: $(wc -l < "$CREATED_FILE")"
cat "$CREATED_FILE"


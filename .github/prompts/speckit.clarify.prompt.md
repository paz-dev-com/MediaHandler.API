---
agent: speckit.clarify
---

# MediaHandler API - Clarification Context

## Project Domain Knowledge

Use this domain context when analyzing specifications and formulating clarification questions for MediaHandler features.

### Core Domain Concepts

| Concept | Definition | Key Considerations |
|---------|------------|-------------------|
| Media | A film or TV show with metadata from TMDB | Has TmdbId, title, type (Film/TvShow), release date |
| MediaFile | Physical file on NAS storage | Path, size, format, linked to Media entity |
| UserMedia | Per-user relationship to Media | Watch status, personal rating, notes |
| WishlistItem | Desired media not yet owned | User wants to acquire, no physical file |
| TvSeason/TvEpisode | TV show structure | Season number, episode number, individual watch tracking |

### Known Integrations

| Integration | Purpose | Failure Considerations |
|-------------|---------|----------------------|
| TMDB API | Media metadata, images, multi-language info | Rate limits, API downtime, missing translations |
| Okta OAuth | User authentication | Token validation, session management |
| NAS File System | Media file storage | Path access, permission issues, disconnection |
| SQL Server | Persistent storage | Connection pooling, query performance |

### Default Assumptions (Unless Spec States Otherwise)

When clarifying specifications, assume these defaults unless the spec explicitly states different requirements:

**User & Authentication:**
- Single Okta tenant for all users
- Users can only see/modify their own data
- Admin roles exist for user management and system configuration

**Media Management:**
- TMDB is the authoritative source for media metadata
- Media can exist without files (wishlist scenario)
- One MediaFile can only belong to one Media
- Watch status is binary (watched/not watched) unless spec mentions progress tracking

**NAS Integration:**
- NAS is local/mounted (not remote API)
- Path format is OS-dependent (Windows paths for now)
- Manual scan trigger (not automatic file watching)

**Performance:**
- Expected collection size: 1-10,000 media items per user
- Expected concurrent users: 1-10 (private deployment)
- Expected TMDB calls: < 100/day (caching expected)

### High-Impact Clarification Areas

When analyzing MediaHandler specs, prioritize these areas for clarification:

1. **Watch Progress Granularity**
   - Binary (watched/unwatched) vs percentage progress?
   - Per-episode tracking for TV shows?
   - Resume position storage?

2. **TMDB Data Caching Strategy**
   - Cache duration for metadata?
   - Refresh strategy for ongoing TV shows?
   - Fallback when TMDB unavailable?

3. **Multi-User Scenarios**
   - Shared media library vs isolated collections?
   - Can users see what others have watched?
   - Family/household sharing model?

4. **NAS Path Handling**
   - Relative vs absolute paths?
   - Path format normalization?
   - Handling moved/renamed files?

5. **Search & Discovery**
   - Local collection search only?
   - TMDB search for new media?
   - Combined search across owned + wishlist?

6. **Offline/Disconnected Mode**
   - Behavior when TMDB unavailable?
   - Behavior when NAS disconnected?
   - Cached data availability?

### Common Ambiguities to Detect

Watch for these vague terms and seek quantification:

| Vague Term | Clarification Needed |
|------------|---------------------|
| "Fast search" | P95 latency target? Index strategy? |
| "Complete media info" | Which TMDB fields exactly? |
| "Multiple formats" | Specific format list? Fallback? |
| "Periodic refresh" | Interval? Trigger conditions? |
| "Large collection" | Number threshold? Performance implications? |
| "Secure access" | Auth method? Token lifetime? |

### Recommended Defaults for Common Questions

When making recommendations, consider these established patterns:

| Question Area | Recommended Default | Rationale |
|--------------|--------------------| ----------|
| Watch status model | Binary (watched/not) | Simpler, covers 90% use case |
| TMDB cache duration | 24 hours for released, 1 hour for airing | Balance freshness vs API limits |
| Search scope | Local collection with TMDB discovery option | Clear separation of concerns |
| Path storage | Absolute paths | Avoids ambiguity, simpler implementation |
| Multi-user model | Isolated collections | Privacy by default |

### Out-of-Scope Clarifications

Do NOT ask clarification questions about:
- Streaming/playback implementation (explicitly out of scope)
- Download/torrent management (explicitly out of scope)
- Social features between users (explicitly out of scope)
- Mobile app specifics (API-first approach)
- Specific NAS vendor support (file system abstraction)

### Question Formulation Guidelines

When formulating questions for MediaHandler:

1. **Prefer concrete options over open-ended questions**
   - ✅ "Watch status: A) Binary B) Percentage C) Episode-level"
   - ❌ "How should watch status work?"

2. **Include default recommendation with rationale**
   - ✅ "**Recommended:** Option A - simpler and matches common media apps"

3. **Focus on decisions that impact data model or API contracts**
   - Data model changes are expensive later
   - API contract changes affect potential consumers

4. **Defer implementation details to planning phase**
   - Don't ask about specific libraries
   - Don't ask about exact SQL schema design
   - Don't ask about deployment specifics


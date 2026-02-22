---
agent: speckit.specify
---

# MediaHandler API - Project Context

## Application Overview

MediaHandler is a personal media management API that allows users to organize, track, and discover TV shows and films stored on a NAS (Network Attached Storage). The application supports multiple users, each with their own collection and preferences.

## Core Features

### 1. Media Collection Management
- **Browse Collection**: View all TV shows and films in the user's NAS storage
- **Storage Location**: Display where each media file is physically stored (path/folder)
- **Direct Folder Access**: Provide direct access links to the media folder on the NAS
- **Watch Status Tracking**: Mark media as watched/unwatched per user
- **Collection Statistics**: Overview of total media, watched vs unwatched, by type (TV/Film)

### 2. Wishlist / Watchlist
- **Add Desired Media**: Add TV shows or films the user wants to watch but doesn't own yet
- **Import from TMDB**: Search and import media information from The Movie Database (TMDB)
- **Unified View**: Single list combining owned media and wishlist items with clear distinction
- **Acquisition Status**: Track whether wishlist items have been acquired

### 3. Media Information (TMDB Integration)
- **Complete Metadata**: Title, synopsis, release date, runtime, genres, cast, crew, ratings
- **Artwork**: Posters, backdrops, and thumbnails from TMDB
- **Multi-language Support**: Fetch and display media information in the user's preferred language
- **TV Show Details**: Season/episode information, air dates, episode summaries
- **Auto-refresh**: Periodic updates for ongoing TV series

### 4. Multi-User Support
- **User Accounts**: Individual user registration and authentication
- **Personal Collections**: Each user has their own watched status and wishlist
- **User Preferences**: Language preference, display settings per user
- **Privacy**: Users can only see and manage their own data

## Technical Requirements

### External Integrations
- **TMDB API**: Primary source for media metadata (requires API key)
- **NAS Access**: File system access to scan and index media files
- **Supported NAS Protocols**: Consider SMB/CIFS, NFS, or direct path access

### Data Model Entities
- `User`: Account information, preferences, language setting
- `Media`: Core media entity (film or TV show) with TMDB reference
- `MediaFile`: Physical file location on NAS, linked to Media
- `UserMedia`: Junction table for user-specific data (watched status, rating, notes)
- `WishlistItem`: Media the user wants but doesn't own
- `TvSeason` / `TvEpisode`: TV show structure with watch progress

### API Capabilities
- User authentication (register, login, token management)
- Media CRUD operations with filtering/sorting/pagination
- TMDB search and import endpoints
- NAS scanning and file indexing
- Watch status management
- Wishlist management
- User preference management

### Language/Localization
- Store user's preferred language (ISO 639-1 code, e.g., "en", "fr", "de")
- Fetch TMDB data in user's language
- Fallback to English if translation unavailable

## Business Rules

1. Media can exist without a physical file (wishlist items)
2. Physical files MUST be linked to a Media entity
3. Watch status is per-user, not global
4. TMDB ID is the primary external reference for deduplication
5. Users cannot access other users' collections or watch status
6. Media files are indexed by path; duplicate paths are not allowed
7. TV show watch status can be tracked at series, season, or episode level

## Out of Scope (Initial Release)
- Streaming/playback within the application
- Automatic media file organization/renaming
- Download management or torrent integration
- Social features (sharing, recommendations between users)
- Mobile applications (API-first approach)


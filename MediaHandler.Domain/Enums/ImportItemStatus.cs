namespace MediaHandler.Domain.Enums;

/// <summary>
///     Per-item outcome assigned by the Kodi import pipeline to a single Kodi library item.
/// </summary>
public enum ImportItemStatus
{
    /// <summary>A new <c>Media</c> (or <c>TvSeason</c>/<c>TvEpisode</c>) row was created.</summary>
    Created,

    /// <summary>An existing <c>(Type, TmdbId)</c> Media entry was associated (first time this Kodi item is seen).</summary>
    Reused,

    /// <summary>The item was present in the baseline run; nothing changed.</summary>
    Unchanged,

    /// <summary>Identity unresolved → a <c>ReviewItem</c> was created or reused.</summary>
    NeedsReview,

    /// <summary>Preview only: resolving this item would require provider traffic.</summary>
    RequiresIdentityLookup,

    /// <summary>Transient provider failure — the item is retried on the next run.</summary>
    IdentityLookupFailed,

    /// <summary>Identity discrepancy discovered via an existing file link; nothing was applied.</summary>
    Conflict,

    /// <summary>Music-video row ignored (the app has no music-video media type).</summary>
    SkippedMusicVideo,

    /// <summary>Synthesized row: present in the baseline run but absent from this upload.</summary>
    NoLongerInKodi
}

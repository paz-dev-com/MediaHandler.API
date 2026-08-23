namespace MediaHandler.Domain.Enums;

/// <summary>
///     Origin of a <c>ReviewItem</c> — which subsystem surfaced the unresolved item.
/// </summary>
public enum ReviewItemSource
{
    /// <summary>Surfaced by the NAS scanner pipeline (default for pre-existing rows).</summary>
    Scan,

    /// <summary>Surfaced by a Kodi database import run.</summary>
    KodiImport
}

namespace MediaHandler.Domain.Enums;

/// <summary>
/// Determines whether a <c>ScanRun</c> should walk every file or only changed ones.
/// </summary>
public enum ScanMode
{
    /// <summary>Re-visits the entire library tree regardless of fingerprint state.</summary>
    Full,

    /// <summary>
    /// Skips files whose <c>MediaFile.Fingerprint</c> matches the stored value,
    /// reducing scan time for large, predominantly unchanged libraries (SC-005).
    /// </summary>
    Incremental
}


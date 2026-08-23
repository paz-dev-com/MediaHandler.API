namespace MediaHandler.API.Contracts.Admin;

/// <summary>Create/update body for a persisted Kodi path mapping.</summary>
public record PathMappingUpsertRequest(string KodiPrefix, string NasPrefix, int? SortOrder);

/// <summary>A single per-upload path-mapping override (multipart <c>overrides</c> JSON array item).</summary>
public record PathMappingOverrideRequest(string KodiPrefix, string NasPrefix);

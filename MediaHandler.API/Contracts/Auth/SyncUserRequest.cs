namespace MediaHandler.API.Contracts.Auth;

/// <summary>
/// Optional body for POST auth/sync.
/// The frontend sends user profile data sourced from the Auth0 ID token (auth0.user$),
/// used as fallback when the access token does not contain those claims.
///
/// This handles two cases:
///  - Auth0 access token without audience → opaque token (no JWT, no extractable claims).
///  - Auth0 Action not configured → access token lacks email/name custom claims.
///
/// The Sub field is only trusted in development (DevAuthenticationHandler).
/// In production the real JWT bearer validation always provides the authoritative sub.
/// </summary>
public record SyncUserRequest(
    string? Sub,
    string? Email,
    string? Name);

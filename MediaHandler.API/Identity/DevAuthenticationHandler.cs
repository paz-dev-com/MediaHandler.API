using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MediaHandler.API.Identity;

/// <summary>
/// Development-only authentication handler.
///
/// Priority order:
///   1. Real Bearer JWT — if a valid JWT is present in the Authorization header, its
///      claims (sub, email, name, roles) are extracted WITHOUT signature validation.
///      This allows real Auth0 users to authenticate in development.
///
///   2. X-Dev-* headers — useful for Swagger, integration tests, or manual overrides
///      when no real JWT is available.
///
///   3. Hardcoded defaults — "auth0|devuser1" / "dev@local.com" / Admin role.
///      These are intentionally kept for unit tests that don't provide a token.
///
/// Headers (all optional, only used when no Bearer JWT is present):
///   X-Dev-OktaId    — sub claim    (default: "auth0|devuser1")
///   X-Dev-Email     — email claim  (default: "dev@local.com")
///   X-Dev-IsAdmin   — Pass "false" to remove the Admin role (default: admin = true)
/// </summary>
public class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    /// <summary>
    /// Namespaced roles claim — must match the API's RoleClaimType and the Auth0 Action.
    /// </summary>
    private const string RolesClaimName = "https://mediahandler.com/roles";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Try to extract claims from a real Bearer JWT (no signature validation in dev).
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        Logger.LogDebug("[DevAuth] Authorization header: {Header}",
            authHeader is null ? "<absent>" : authHeader[..Math.Min(authHeader.Length, 40)] + "…");

        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var token = authHeader["Bearer ".Length..].Trim();
            var parts = token.Split('.');
            Logger.LogDebug("[DevAuth] Token parts count: {Count} (3 = JWT, other = opaque)", parts.Length);

            var jwtClaims = TryExtractJwtClaims(token);
            if (jwtClaims is not null)
            {
                var jwtSub = jwtClaims.FirstOrDefault(c => c.Type == "sub")?.Value;
                var jwtEmail = jwtClaims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;
                Logger.LogInformation("[DevAuth] Real JWT detected → sub={Sub}, email={Email}", jwtSub, jwtEmail ?? "<absent>");

                var jwtIdentity = new ClaimsIdentity(jwtClaims, SchemeName);
                var jwtTicket = new AuthenticationTicket(new ClaimsPrincipal(jwtIdentity), SchemeName);
                return Task.FromResult(AuthenticateResult.Success(jwtTicket));
            }

            Logger.LogWarning("[DevAuth] Token present but not a decodable JWT (likely opaque — check Auth0 API audience). Falling back to dev defaults.");
        }
        else
        {
            Logger.LogDebug("[DevAuth] No Bearer token → using dev defaults (Swagger / unit tests).");
        }

        // 2. Fall back to X-Dev-* headers, then hardcoded defaults (unit tests / Swagger).
        var oktaId = Request.Headers["X-Dev-OktaId"].FirstOrDefault() ?? "auth0|devuser1";
        var email = Request.Headers["X-Dev-Email"].FirstOrDefault() ?? "dev@local.com";
        var isAdmin = !string.Equals(
            Request.Headers["X-Dev-IsAdmin"].FirstOrDefault(),
            "false",
            StringComparison.OrdinalIgnoreCase);

        var claims = new List<Claim>
        {
            new("sub", oktaId),
            new(ClaimTypes.Email, email),
        };

        if (isAdmin)
            claims.Add(new(ClaimTypes.Role, "Admin"));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Decodes the JWT payload without validating the signature (dev-only).
    /// Returns null if the token is not a well-formed JWT or has no subject claim.
    /// </summary>
    private static List<Claim>? TryExtractJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            // Base64Url → Base64 padding
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty,
            };

            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            var root = doc.RootElement;

            // sub is mandatory — it is the user's unique identity
            if (!root.TryGetProperty("sub", out var subProp) ||
                string.IsNullOrEmpty(subProp.GetString()))
                return null;

            var claims = new List<Claim>
            {
                new("sub", subProp.GetString()!),
            };

            // email
            if (root.TryGetProperty("email", out var emailProp) &&
                emailProp.GetString() is { Length: > 0 } email)
                claims.Add(new(ClaimTypes.Email, email));

            // name / display name
            if (root.TryGetProperty("name", out var nameProp) &&
                nameProp.GetString() is { Length: > 0 } name)
                claims.Add(new("name", name));

            // Roles from the namespaced custom claim (set by Auth0 Action)
            if (root.TryGetProperty(RolesClaimName, out var rolesProp) &&
                rolesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var role in rolesProp.EnumerateArray())
                {
                    if (role.GetString() is { Length: > 0 } roleName)
                        claims.Add(new(ClaimTypes.Role, roleName));
                }
            }

            return claims;
        }
        catch
        {
            // Malformed token — fall back to dev defaults
            return null;
        }
    }
}

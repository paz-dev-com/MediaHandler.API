using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MediaHandler.API.Identity;

/// <summary>
/// Development-only authentication handler. Authenticates every request automatically
/// using headers so Swagger and tools work without a real Okta token.
///
/// Headers (all optional — defaults apply when omitted):
///   X-Dev-OktaId    — OktaId claim (default: "okta|devuser1")
///   X-Dev-Email     — Email claim  (default: "dev@local.com")
///   X-Dev-IsAdmin   — Pass "false" to remove the Admin role (default: admin = true)
/// </summary>
public class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
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
}

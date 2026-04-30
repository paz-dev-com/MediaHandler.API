#nullable enable
// SC-008: Zero unauthorized scan starts — every admin endpoint enforces AdminOnly policy.
// Tests: Anonymous → 401, User role → 403, Admin → 2xx.
// Also verifies that an anonymous POST /api/v1/admin/scan does NOT create a ScanRun row.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace MediaHandler.IntegrationTests.Scanner;

/// <summary>
/// SC-008: authorization coverage for every scanner-related admin endpoint.
/// Uses <see cref="WebApplicationFactory{TEntryPoint}"/> with the DevAuthenticationHandler
/// and Testcontainers SQL Server for a realistic integration test.
/// </summary>
public sealed class AdminAuthorizationTests : IAsyncLifetime
{
    private MsSqlContainer _dbContainer = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _dbContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await _dbContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("ConnectionStrings:DefaultConnection", _dbContainer.GetConnectionString());
                // Disable HTTPS redirection in tests
                builder.UseSetting("ASPNETCORE_URLS", "http://+:80");
            });

        // Run migrations against the test container
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // All endpoints to test, from contracts/scan.md, library-roots.md, review-items.md
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (Method, Path, ExpectedAdminStatus) — for each endpoint defined in the contracts.
    /// The Admin status code may vary (200, 201, 202, 204, 400, 404) depending on
    /// whether the request body/path params are valid, BUT it must not be 401 or 403.
    /// </summary>
    public static IEnumerable<TheoryDataRow<string, string>> AllEndpoints()
    {
        // scan.md
        yield return new("POST", "/api/v1/admin/scan");
        yield return new("GET", "/api/v1/admin/scan/" + Guid.NewGuid());
        yield return new("GET", "/api/v1/admin/scan/active");
        yield return new("POST", "/api/v1/admin/scan/" + Guid.NewGuid() + "/cancel");

        // library-roots.md
        yield return new("GET", "/api/v1/admin/library-roots");
        yield return new("POST", "/api/v1/admin/library-roots");
        yield return new("DELETE", "/api/v1/admin/library-roots/" + Guid.NewGuid());

        // review-items.md
        yield return new("GET", "/api/v1/admin/review-items");
        yield return new("POST", "/api/v1/admin/review-items/" + Guid.NewGuid() + "/resolve");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Anonymous → 401 for every endpoint
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Anonymous_ReturnsUnauthorized(string method, string path)
    {
        // DevAuthenticationHandler requires an explicit opt-out for anonymous simulation.
        // When NO Authorization header AND NO X-Dev-* headers are sent, the dev handler
        // defaults to Admin. To simulate anonymous, we need to remove the default.
        // Instead, we'll create a separate factory that uses a real JWT scheme.
        var client = CreateAnonymousClient();

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"Anonymous {method} {path} must return 401");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authenticated User (non-admin) → 403 for every endpoint
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task AuthenticatedUser_ReturnsForbidden(string method, string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        // DevAuthenticationHandler: X-Dev-IsAdmin=false → User role only (no Admin)
        request.Headers.Add("X-Dev-IsAdmin", "false");
        request.Headers.Add("X-Dev-OktaId", "auth0|testuser1");
        request.Headers.Add("X-Dev-Email", "user@test.com");
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: $"User-role {method} {path} must return 403");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Admin → 2xx (or valid error like 400/404/409 — NOT 401/403)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task AdminUser_ReturnsSuccessOrValidError(string method, string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        // DevAuthenticationHandler defaults to Admin — no special headers needed
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Admin must never get 401 or 403. Other errors (400, 404, 409, 422) are acceptable
        // since the request body or path params may be invalid for this generic test.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: $"Admin {method} {path} must not return 401");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            because: $"Admin {method} {path} must not return 403");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Anonymous POST /api/v1/admin/scan must NOT create a ScanRun row
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Anonymous_PostScan_DoesNotCreateScanRunRow()
    {
        var client = CreateAnonymousClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/scan");
        request.Content = JsonContent.Create(new { libraryRootIds = Array.Empty<Guid>(), mode = "Full" });

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // Should be 401
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify no ScanRun was created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var scanRunCount = await db.ScanRuns.CountAsync(TestContext.Current.CancellationToken);
        scanRunCount.Should().Be(0,
            because: "An anonymous POST to /api/v1/admin/scan must not create any ScanRun row");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a client that simulates an anonymous (unauthenticated) caller.
    /// The DevAuthenticationHandler defaults to Admin when no headers are present,
    /// so we use a custom factory with production-like JWT auth that properly rejects
    /// requests without a valid token.
    /// </summary>
    private HttpClient CreateAnonymousClient()
    {
        // Create a factory with production-like auth that rejects anonymous requests.
        // Override the environment to "Production" so DevAuthenticationHandler is NOT used.
        var anonFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _dbContainer.GetConnectionString());
            builder.UseSetting("Okta:Domain", "https://fake-auth0-domain.us.auth0.com");
            builder.UseSetting("Okta:Audience", "https://fake-api-audience");
        });

        return anonFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    /// <summary>
    /// Adds a minimal JSON body for POST/PUT requests to prevent 415 Unsupported Media Type.
    /// </summary>
    private static void AddMinimalBody(HttpRequestMessage request, string method, string path)
    {
        if (method is "POST" or "PUT")
        {
            if (path.Contains("/scan") && !path.Contains("/cancel") && !path.Contains("/resolve"))
            {
                request.Content = JsonContent.Create(new { libraryRootIds = Array.Empty<Guid>(), mode = "Full" });
            }
            else if (path.Contains("/library-roots"))
            {
                request.Content = JsonContent.Create(new { path = "/nas/test", kind = "Movies", label = "Test" });
            }
            else if (path.Contains("/resolve"))
            {
                request.Content = JsonContent.Create(new { action = "Dismiss", tmdbId = (int?)null, kind = (string?)null });
            }
            else
            {
                request.Content = JsonContent.Create(new { });
            }
        }
    }
}


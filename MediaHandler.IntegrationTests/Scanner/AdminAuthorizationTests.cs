// SC-008: Zero unauthorized scan starts — every admin endpoint enforces AdminOnly policy.
// Tests: Anonymous → 401, User role → 403, Admin → 2xx.
// Also verifies that an anonymous POST /api/v1/admin/scan does NOT create a ScanRun row.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaHandler.IntegrationTests.Scanner;

// ─── Shared factory fixture ───────────────────────────────────────────────────

/// <summary>
///     Class-level fixture for <see cref="AdminAuthorizationTests" />.
///     Created ONCE per test class so all 28 theory cases share the same
///     <see cref="WebApplicationFactory{T}" /> — avoiding the 128-inotify-instance limit.
/// </summary>
public sealed class AdminAuthorizationFixture : IAsyncLifetime
{
    /// <summary>The shared factory, backed by an EF Core InMemory database.</summary>
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("ASPNETCORE_URLS", "http://+:80");

                // Satisfy ValidateOnStart() for all required options so the factory
                // can start in both local and CI environments without real credentials.
                builder.UseSetting("Okta:Domain", "https://fake-auth0-domain.us.auth0.com");
                builder.UseSetting("Okta:ClientId", "fake-client-id");
                builder.UseSetting("Okta:ClientSecret", "fake-client-secret");
                builder.UseSetting("Okta:Audience", "https://fake-api-audience");
                builder.UseSetting("Tmdb:ReadAccessToken", "fake-tmdb-token");
                builder.UseSetting("Nas:AppToken", "fake-nas-token");

                builder.ConfigureServices(services =>
                {
                    // ── Replace SQL Server DbContext with EF Core InMemory ─────────────
                    // Authorization middleware fires before any controller/DB code, so
                    // InMemory is sufficient for 401/403 checks AND for the ScanRun-row
                    // assertion test.
                    //
                    // Remove ALL EF Core service descriptors that embed the SQL Server
                    // factory (including IDbContextOptionsConfiguration<T> which carries
                    // the factory lambda that resolves the interceptors).
                    var toRemove = services
                        .Where(d =>
                            d.ServiceType == typeof(DbContextOptions<MediaHandlerDbContext>) ||
                            d.ServiceType == typeof(MediaHandlerDbContext) ||
                            d.ServiceType.FullName?.Contains("AuditableEntitySaveChangesInterceptor") == true ||
                            d.ServiceType.FullName?.Contains("DomainEventDispatchInterceptor") == true ||
                            d.ServiceType.FullName?.Contains("IDomainEventDispatcher") == true ||
                            // EF Core 8+ stores the factory lambda in IDbContextOptionsConfiguration<T>
                            (d.ServiceType.IsGenericType &&
                             d.ServiceType.GetGenericTypeDefinition().FullName
                                 ?.Contains("IDbContextOptionsConfiguration") == true))
                        .ToList();
                    foreach (var d in toRemove)
                        services.Remove(d);

                    // Register a fresh DbContext using InMemory via a direct factory so
                    // no interceptors are required.
                    var inMemoryOptions = new DbContextOptionsBuilder<MediaHandlerDbContext>()
                        .UseInMemoryDatabase("AdminAuthTest")
                        .Options;

                    services.AddScoped<MediaHandlerDbContext>(_ => new MediaHandlerDbContext(inMemoryOptions));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MediaHandlerDbContext>());
                });
            });

        // Ensure the EF Core model is created in the InMemory store.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        db.Database.EnsureCreated();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}

// ─── Test class ───────────────────────────────────────────────────────────────

/// <summary>
///     SC-008: authorization coverage for every scanner-related admin endpoint.
///     Uses <see cref="WebApplicationFactory{TEntryPoint}" /> with the DevAuthenticationHandler
///     and an EF Core InMemory database — no SQL Server container required because
///     authorization middleware runs before any database code.
/// </summary>
public sealed class AdminAuthorizationTests : IClassFixture<AdminAuthorizationFixture>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdminAuthorizationTests(AdminAuthorizationFixture fixture)
    {
        _factory = fixture.Factory;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // All endpoints to test
    // ═══════════════════════════════════════════════════════════════════════════

    public static IEnumerable<TheoryDataRow<string, string>> AllEndpoints()
    {
        // scan.md
        yield return new TheoryDataRow<string, string>("POST", "/api/v1/admin/scan");
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/scan/" + Guid.NewGuid());
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/scan/active");
        yield return new TheoryDataRow<string, string>("POST", "/api/v1/admin/scan/" + Guid.NewGuid() + "/cancel");

        // library-roots.md
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/library-roots");
        yield return new TheoryDataRow<string, string>("POST", "/api/v1/admin/library-roots");
        yield return new TheoryDataRow<string, string>("DELETE", "/api/v1/admin/library-roots/" + Guid.NewGuid());

        // review-items.md
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/review-items");
        yield return new TheoryDataRow<string, string>("POST",
            "/api/v1/admin/review-items/" + Guid.NewGuid() + "/resolve");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Anonymous → 401 for every endpoint
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Anonymous_ReturnsUnauthorized(string method, string path)
    {
        var client = CreateAnonymousClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"Anonymous {method} {path} must return 401");
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
        request.Headers.Add("X-Dev-IsAdmin", "false");
        request.Headers.Add("X-Dev-OktaId", "auth0|testuser1");
        request.Headers.Add("X-Dev-Email", "user@test.com");
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"User-role {method} {path} must return 403");
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
        // DevAuthenticationHandler defaults to Admin when no headers are present
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            $"Admin {method} {path} must not return 401");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"Admin {method} {path} must not return 403");
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

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify no ScanRun was created in the InMemory database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var scanRunCount = await db.ScanRuns.CountAsync(TestContext.Current.CancellationToken);
        scanRunCount.Should().Be(0,
            "An anonymous POST to /api/v1/admin/scan must not create any ScanRun row");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    ///     Creates a client that simulates an anonymous caller by switching to "Production"
    ///     so that the real JWT bearer handler is used (which rejects unauthenticated requests).
    /// </summary>
    private HttpClient CreateAnonymousClient()
    {
        var anonFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // Okta (required for JWT bearer auth in Production)
            builder.UseSetting("Okta:Domain", "https://fake-auth0-domain.us.auth0.com");
            builder.UseSetting("Okta:ClientId", "fake-client-id");
            builder.UseSetting("Okta:ClientSecret", "fake-client-secret");
            builder.UseSetting("Okta:Audience", "https://fake-api-audience");
            // Satisfy ValidateOnStart() for other required options
            builder.UseSetting("Tmdb:ReadAccessToken", "fake-tmdb-token");
            builder.UseSetting("Nas:AppToken", "fake-nas-token");
        });

        return anonFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private static void AddMinimalBody(HttpRequestMessage request, string method, string path)
    {
        if (method is "POST" or "PUT")
        {
            if (path.Contains("/scan") && !path.Contains("/cancel") && !path.Contains("/resolve"))
                request.Content = JsonContent.Create(new { libraryRootIds = Array.Empty<Guid>(), mode = "Full" });
            else if (path.Contains("/library-roots"))
                request.Content = JsonContent.Create(new { path = "/nas/test", kind = "Movies", label = "Test" });
            else if (path.Contains("/resolve"))
                request.Content = JsonContent.Create(new
                { action = "Dismiss", tmdbId = (int?)null, kind = (string?)null });
            else
                request.Content = JsonContent.Create(new { });
        }
    }
}
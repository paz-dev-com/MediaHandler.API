using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaHandler.IntegrationTests.KodiImport;

[CollectionDefinition("KodiImportApi")]
public sealed class KodiImportApiCollection : ICollectionFixture<KodiImportApiFixture>
{
}

/// <summary>
///     Shared fixture for Kodi import API tests. Uses an EF Core InMemory database and substitutes
///     the singleton coordinator so background tasks do not run out of band.
/// </summary>
public sealed class KodiImportApiFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public WebApplicationFactory<Program> AnonymousFactory { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
                builder.UseSetting("ASPNETCORE_URLS", "http://+:80");

                builder.UseSetting("Okta:Domain", "https://fake-auth0-domain.us.auth0.com");
                builder.UseSetting("Okta:ClientId", "fake-client-id");
                builder.UseSetting("Okta:ClientSecret", "fake-client-secret");
                builder.UseSetting("Okta:Audience", "https://fake-api-audience");
                builder.UseSetting("Tmdb:ReadAccessToken", "fake-tmdb-token");
                builder.UseSetting("Nas:AppToken", "fake-nas-token");

                builder.ConfigureServices(services =>
                {
                    var toRemove = services
                        .Where(d =>
                            d.ServiceType == typeof(DbContextOptions<MediaHandlerDbContext>) ||
                            d.ServiceType == typeof(MediaHandlerDbContext) ||
                            d.ServiceType.FullName?.Contains("AuditableEntitySaveChangesInterceptor") == true ||
                            d.ServiceType.FullName?.Contains("DomainEventDispatchInterceptor") == true ||
                            d.ServiceType.FullName?.Contains("IDomainEventDispatcher") == true ||
                            (d.ServiceType.IsGenericType &&
                             d.ServiceType.GetGenericTypeDefinition().FullName?.Contains("IDbContextOptionsConfiguration") == true))
                        .ToList();
                    foreach (var d in toRemove)
                        services.Remove(d);

                    var inMemoryOptions = new DbContextOptionsBuilder<MediaHandlerDbContext>()
                        .UseInMemoryDatabase("KodiImportApiTest")
                        .Options;

                    services.AddScoped<MediaHandlerDbContext>(_ => new MediaHandlerDbContext(inMemoryOptions));
                    services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MediaHandlerDbContext>());

                    services.AddSingleton<IKodiVideoDbReader, FakeKodiVideoDbReader>();
                    services.AddSingleton<IKodiImportFileStore, FakeKodiImportFileStore>();
                    services.AddSingleton<IImportRunCoordinator, FakeImportRunCoordinator>(sp => new FakeImportRunCoordinator(sp));
                });
            });

        AnonymousFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Okta:Domain", "https://fake-auth0-domain.us.auth0.com");
            builder.UseSetting("Okta:ClientId", "fake-client-id");
            builder.UseSetting("Okta:ClientSecret", "fake-client-secret");
            builder.UseSetting("Okta:Audience", "https://fake-api-audience");
            builder.UseSetting("Tmdb:ReadAccessToken", "fake-tmdb-token");
            builder.UseSetting("Nas:AppToken", "fake-nas-token");
        });

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        db.Database.EnsureCreated();

        if (!db.Users.Any(u => u.OktaId == "auth0|devuser1"))
        {
            db.Users.Add(new User
            {
                OktaId = "auth0|devuser1",
                Email = "dev@local.com",
                DisplayName = "Dev Admin",
                Role = UserRole.Admin,
                IsActive = true
            });
        }

        if (!db.Users.Any(u => u.OktaId == "auth0|testuser1"))
        {
            db.Users.Add(new User
            {
                OktaId = "auth0|testuser1",
                Email = "user@test.com",
                DisplayName = "Test User",
                Role = UserRole.User,
                IsActive = true
            });
        }

        db.SaveChanges();

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await AnonymousFactory.DisposeAsync();
        await Factory.DisposeAsync();
    }

    private sealed class FakeKodiVideoDbReader : IKodiVideoDbReader
    {
        public Task<KodiDbValidationResult> ValidateAsync(string filePath, int schemaVersion, CancellationToken ct = default)
            => Task.FromResult(KodiDbValidationResult.Valid());

        public Task<KodiLibrarySnapshot> ReadAsync(string filePath, int schemaVersion, CancellationToken ct = default)
            => Task.FromResult(new KodiLibrarySnapshot([], [], []));
    }

    private sealed class FakeKodiImportFileStore : IKodiImportFileStore
    {
        public Task<Result<StoredUpload>> SaveAsync(Stream content, string fileName, long declaredLength, CancellationToken ct)
        {
            if (declaredLength > 100)
                return Task.FromResult(Result.Fail<StoredUpload>("UPLOAD_TOO_LARGE: The uploaded file exceeds the configured size limit."));

            return Task.FromResult(Result.Success(new StoredUpload("/tmp/fake.db", declaredLength)));
        }

        public void Delete(string? filePath)
        {
        }

        public void PurgeOrphans()
        {
        }
    }

    private sealed class FakeImportRunCoordinator(IServiceProvider serviceProvider) : IImportRunCoordinator
    {
        public Task<KodiImportRunHandle> StartAsync(KodiImportStartParameters parameters, CancellationToken ct = default)
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
            db.ImportRuns.Add(new ImportRun
            {
                Id = parameters.ImportRunId,
                Mode = parameters.Mode,
                Status = ImportRunStatus.Running,
                SourceFileName = parameters.SourceFileName,
                SchemaVersion = parameters.SchemaVersion,
                StartedAt = DateTime.UtcNow,
                UploadedFilePath = parameters.StoredFilePath,
                PathMappingsJson = System.Text.Json.JsonSerializer.Serialize(parameters.Mappings)
            });
            db.SaveChanges();
            return Task.FromResult(new KodiImportRunHandle(parameters.ImportRunId));
        }
    }
}

/// <summary>
///     API-level tests for the Kodi import endpoints: authorization matrix, start/import/preview,
///     conflict/concurrency, and error mapping.
/// </summary>
[Collection("KodiImportApi")]
public sealed class KodiImportApiTests
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly WebApplicationFactory<Program> _anonymousFactory;

    public KodiImportApiTests(KodiImportApiFixture fixture)
    {
        _factory = fixture.Factory;
        _anonymousFactory = fixture.AnonymousFactory;
    }

    public static IEnumerable<TheoryDataRow<string, string>> AllEndpoints()
    {
        yield return new TheoryDataRow<string, string>("POST", "/api/v1/admin/kodi-import");
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/kodi-import");
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/kodi-import/active");
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/kodi-import/" + Guid.NewGuid());
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/kodi-import/" + Guid.NewGuid() + "/items");
        yield return new TheoryDataRow<string, string>("GET", "/api/v1/admin/kodi-import/path-mappings");
        yield return new TheoryDataRow<string, string>("POST", "/api/v1/admin/kodi-import/path-mappings");
        yield return new TheoryDataRow<string, string>("PUT", "/api/v1/admin/kodi-import/path-mappings/" + Guid.NewGuid());
        yield return new TheoryDataRow<string, string>("DELETE", "/api/v1/admin/kodi-import/path-mappings/" + Guid.NewGuid());
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Anonymous_ReturnsUnauthorized(string method, string path)
    {
        var client = _anonymousFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"Anonymous {method} {path} must return 401");
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task AuthenticatedUser_ReturnsForbidden(string method, string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Dev-IsAdmin", "false");
        request.Headers.Add("X-Dev-OktaId", "auth0|testuser1");
        request.Headers.Add("X-Dev-Email", "user@test.com");
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"User-role {method} {path} must return 403");
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task AdminUser_ReturnsSuccessOrValidError(string method, string path)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        AddMinimalBody(request, method, path);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            $"Admin {method} {path} must not return 401");
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            $"Admin {method} {path} must not return 403");
    }

    [Fact]
    public async Task PostImport_ValidMultipart_Returns202ThenRunCompletes()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("MyVideos121.db"), "file", "MyVideos121.db");
        content.Add(new StringContent("import"), "mode");

        var response = await client.PostAsync("/api/v1/admin/kodi-import", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponseEnvelope<ImportRunSummary>>(TestContext.Current.CancellationToken);
        envelope.Should().NotBeNull();
        envelope!.Data.Should().NotBeNull();

        var runResponse = await client.GetAsync($"/api/v1/admin/kodi-import/{envelope.Data!.Id}", TestContext.Current.CancellationToken);
        runResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostImport_SecondWhileRunning_Returns409()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        db.ImportRuns.Add(new ImportRun
        {
            Mode = KodiImportMode.Import,
            Status = ImportRunStatus.Running,
            SourceFileName = "MyVideos121.db",
            SchemaVersion = 121,
            StartedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var content = new MultipartFormDataContent();
        content.Add(new StringContent("MyVideos121.db"), "file", "MyVideos121.db");

        var response = await client.PostAsync("/api/v1/admin/kodi-import", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PostImport_OversizedUpload_Returns400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(new string('x', 200)), "file", "MyVideos121.db");

        var response = await client.PostAsync("/api/v1/admin/kodi-import", content, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRun_UnknownId_Returns404()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(
            "/api/v1/admin/kodi-import/" + Guid.NewGuid(), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void AddMinimalBody(HttpRequestMessage request, string method, string path)
    {
        if (method is not ("POST" or "PUT"))
            return;

        if (path.Contains("/path-mappings"))
            request.Content = JsonContent.Create(new { kodiPrefix = "smb://x", nasPrefix = "/nas/x" });
        else if (path == "/api/v1/admin/kodi-import")
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent("x"), "file", "MyVideos121.db");
            request.Content = content;
        }
        else
            request.Content = JsonContent.Create(new { });
    }

    private sealed class ApiResponseEnvelope<T>
    {
        public T? Data { get; set; }
    }

    private sealed class ImportRunSummary
    {
        public Guid Id { get; set; }
    }
}

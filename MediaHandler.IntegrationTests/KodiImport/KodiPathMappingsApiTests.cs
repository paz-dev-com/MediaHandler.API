using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MediaHandler.API.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediaHandler.IntegrationTests.KodiImport;

/// <summary>
///     End-to-end CRUD tests for the Kodi path-mapping admin endpoints via WebApplicationFactory.
/// </summary>
[Collection("KodiImportApi")]
public sealed class KodiPathMappingsApiTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public KodiPathMappingsApiTests(KodiImportApiFixture fixture)
    {
        _factory = fixture.Factory;
    }

    [Fact]
    public async Task CreateListUpdateDelete_RoundTrip_Works()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Create
        var createResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/kodi-import/path-mappings",
            new { kodiPrefix = $"smb://FREEBOX/{suffix}/", nasPrefix = $"/nas/{suffix}/", sortOrder = (int?)0 },
            TestContext.Current.CancellationToken);
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var createEnvelope = await createResponse.Content.ReadFromJsonAsync<ApiResponse<KodiPathMappingDto>>(TestContext.Current.CancellationToken);
        createEnvelope.Should().NotBeNull();
        createEnvelope!.Data!.KodiPrefix.Should().Be($"smb://FREEBOX/{suffix}");
        createEnvelope.Data.NasPrefix.Should().Be($"/nas/{suffix}");

        // List
        var listResponse = await client.GetAsync(
            "/api/v1/admin/kodi-import/path-mappings", TestContext.Current.CancellationToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listEnvelope = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<KodiPathMappingDto>>>(TestContext.Current.CancellationToken);
        listEnvelope!.Data.Should().ContainSingle(m => m.KodiPrefix == $"smb://FREEBOX/{suffix}");

        // Update
        var id = createEnvelope.Data.Id;
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/admin/kodi-import/path-mappings/{id}",
            new { kodiPrefix = $"smb://FREEBOX/{suffix}2/", nasPrefix = $"/nas/{suffix}2/", sortOrder = 1 },
            TestContext.Current.CancellationToken);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateEnvelope = await updateResponse.Content.ReadFromJsonAsync<ApiResponse<KodiPathMappingDto>>(TestContext.Current.CancellationToken);
        updateEnvelope!.Data!.KodiPrefix.Should().Be($"smb://FREEBOX/{suffix}2");
        updateEnvelope.Data.NasPrefix.Should().Be($"/nas/{suffix}2");

        // Delete
        var deleteResponse = await client.DeleteAsync(
            $"/api/v1/admin/kodi-import/path-mappings/{id}", TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // List again — the specific mapping is gone; other test data may remain in the shared DB.
        listResponse = await client.GetAsync(
            "/api/v1/admin/kodi-import/path-mappings", TestContext.Current.CancellationToken);
        listEnvelope = await listResponse.Content.ReadFromJsonAsync<ApiResponse<List<KodiPathMappingDto>>>(TestContext.Current.CancellationToken);
        listEnvelope!.Data.Should().NotContain(m => m.Id == id);
    }

    [Fact]
    public async Task Create_DuplicatePrefix_Returns422()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await client.PostAsJsonAsync(
            "/api/v1/admin/kodi-import/path-mappings",
            new { kodiPrefix = $"smb://FREEBOX/{suffix}/", nasPrefix = $"/nas/{suffix}/" },
            TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/kodi-import/path-mappings",
            new { kodiPrefix = $"smb://FREEBOX/{suffix}/", nasPrefix = $"/nas/Other/" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private sealed class ApiResponse<T>
    {
        public T Data { get; set; } = default!;
    }

    private sealed class KodiPathMappingDto
    {
        public Guid Id { get; set; }
        public string KodiPrefix { get; set; } = string.Empty;
        public string NasPrefix { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }
}

using MediaHandler.Application.Common.DTOs;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaHandler.IntegrationTests.Common;

/// <summary>
///     Integration-test base class for scanner tests. Extends <see cref="IntegrationTestBase" />
///     with a <see cref="WithFakeNasService" /> hook that registers a <see cref="FakeNasService" />
///     in place of <c>FreeboxNasService</c>, so scanner tests can run against a deterministic
///     in-memory NAS tree without any Freebox infrastructure.
///     Usage:
///     <code>
/// public class MyScannerTest : ScannerIntegrationTestBase
/// {
///     public override async ValueTask InitializeAsync()
///     {
///         await base.InitializeAsync();
/// 
///         // Build an in-memory tree (e.g., from FixtureBuilder or inline)
///         var entries = new[]
///         {
///             new NasFileInfo("/nas/Movies/Inception (2010)/Inception (2010).mkv",
///                 "Inception (2010).mkv", 1_073_741_824, "MKV",
///                 DateTime.UtcNow, DateTime.UtcNow),
///         };
/// 
///         WithFakeNasService(entries, configuredPaths: ["/nas"]);
///     }
/// }
/// </code>
/// </summary>
public abstract class ScannerIntegrationTestBase : IntegrationTestBase
{
    private ServiceProvider? _scannerServiceProvider;

    /// <summary>The <see cref="FakeNasService" /> registered for this test, if configured.</summary>
    protected FakeNasService? FakeNas { get; private set; }

    /// <summary>
    ///     A <see cref="IServiceProvider" /> that exposes <see cref="INasService" /> resolved to the
    ///     <see cref="FakeNasService" /> registered via <see cref="WithFakeNasService" />. Available
    ///     after <see cref="WithFakeNasService" /> has been called.
    /// </summary>
    protected IServiceProvider? ScannerServices => _scannerServiceProvider;

    /// <summary>
    ///     Configures an in-memory <see cref="FakeNasService" /> as the <see cref="INasService" />
    ///     for this test. Also builds a <see cref="ScannerServices" /> provider that exposes
    ///     <see cref="INasService" /> alongside the shared <see cref="IntegrationTestBase.DbContext" />.
    ///     Call this method during <see cref="InitializeAsync" /> after <c>base.InitializeAsync()</c>,
    ///     or from inside individual test methods to compose different trees per test.
    /// </summary>
    /// <param name="entries">
    ///     The flat in-memory file/directory entries to serve. Typically produced by
    ///     <c>FixtureBuilder</c> or assembled inline for focused tests.
    /// </param>
    /// <param name="configuredPaths">
    ///     Base paths reported by <see cref="INasService.GetConfiguredPathsAsync" />. Defaults to
    ///     <c>["/nas"]</c> when <see langword="null" />.
    /// </param>
    /// <param name="additionalServices">
    ///     Optional callback to register further services into the scanner service collection
    ///     (e.g., substitute implementations for other interfaces).
    /// </param>
    /// <returns>The configured <see cref="FakeNasService" /> instance.</returns>
    protected FakeNasService WithFakeNasService(
        IEnumerable<NasFileInfo> entries,
        IEnumerable<string>? configuredPaths = null,
        Action<IServiceCollection>? additionalServices = null)
    {
        FakeNas = new FakeNasService(entries, configuredPaths);

        // Dispose any previously built provider before replacing
        _scannerServiceProvider?.Dispose();

        var services = new ServiceCollection();

        // Register the fake NAS service
        services.AddSingleton<INasService>(FakeNas);

        // Expose the shared DbContext so handlers resolved from ScannerServices use the same
        // in-memory transaction scope as assertions in the test body.
        services.AddSingleton<MediaHandlerDbContext>(_ => DbContext);
        services.AddSingleton<IApplicationDbContext>(_ => DbContext);

        services.AddLogging();

        additionalServices?.Invoke(services);

        _scannerServiceProvider = services.BuildServiceProvider();

        return FakeNas;
    }

    /// <summary>
    ///     Builds a <see cref="ScanRunCoordinator" /> from explicit <paramref name="pipeline" /> and
    ///     <paramref name="coordinatorDb" /> instances by wiring them into a minimal
    ///     <see cref="IServiceScopeFactory" />. This avoids the captive-dependency restriction that
    ///     prevents injecting scoped services directly into the singleton coordinator.
    /// </summary>
    protected static ScanRunCoordinator CreateScanRunCoordinator(
        ScanPipeline pipeline,
        MediaHandlerDbContext coordinatorDb)
    {
        // Build a minimal service provider that returns the pre-created instances when the
        // coordinator resolves services from its internally-created scopes.
        var sp = new ServiceCollection()
            .AddSingleton(coordinatorDb)
            .AddSingleton<IApplicationDbContext>(coordinatorDb)
            .AddSingleton(pipeline)
            .BuildServiceProvider();

        return new ScanRunCoordinator(
            NullLogger<ScanRunCoordinator>.Instance,
            sp.GetRequiredService<IServiceScopeFactory>());
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (_scannerServiceProvider is not null)
            await _scannerServiceProvider.DisposeAsync();

        await base.DisposeAsync();
    }
}
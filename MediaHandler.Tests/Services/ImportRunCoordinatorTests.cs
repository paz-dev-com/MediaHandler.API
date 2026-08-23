using FluentAssertions;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Application.Common.Models;
using MediaHandler.Application.Common.Models.Kodi;
using MediaHandler.Application.Features.Dashboard.Commands.StartEnrichment;
using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Kodi;
using MediaHandler.Infrastructure.Nas.Scanner;
using MediaHandler.Infrastructure.Persistence;
using MediaHandler.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaHandler.Tests.Services;

public class ImportRunCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WhenPendingToRunningFails_MarksRunAsFailed()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddScoped<MediaHandlerDbContext>(_ =>
        {
            var options = new DbContextOptionsBuilder<MediaHandlerDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            return new ThrowingOnSecondSaveDbContext(options);
        });
        var provider = services.BuildServiceProvider();

        var coordinator = new ImportRunCoordinator(
            NullLogger<ImportRunCoordinator>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>());

        var parameters = new KodiImportStartParameters(
            Guid.NewGuid(),
            "/tmp/test.db",
            "MyVideos121.db",
            121,
            KodiImportMode.Import,
            []);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(parameters, TestContext.Current.CancellationToken));

        ex.Message.Should().Be("IMPORT_IN_PROGRESS");

        using var verifyScope = provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var run = await db.ImportRuns.SingleAsync(TestContext.Current.CancellationToken);
        run.Status.Should().Be(ImportRunStatus.Failed);
        run.FinishedAt.Should().NotBeNull();
        run.FailureReason.Should().Be("IMPORT_IN_PROGRESS");
    }

    [Fact]
    public async Task StartAsync_SecondCallWhileFirstRunning_MarksRejectedAsFailedAndReturnsImportInProgress()
    {
        ThrowingOnSecondRunningTransitionDbContext.ResetCounter();

        var services = CreateConcurrentServices("concurrent-import-test");
        var provider = services.BuildServiceProvider();

        var coordinator = new ImportRunCoordinator(
            NullLogger<ImportRunCoordinator>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>());

        var firstParameters = new KodiImportStartParameters(
            Guid.NewGuid(), "/tmp/test1.db", "MyVideos121.db", 121, KodiImportMode.Import, []);
        var secondParameters = new KodiImportStartParameters(
            Guid.NewGuid(), "/tmp/test2.db", "MyVideos121.db", 121, KodiImportMode.Import, []);

        var firstHandle = await coordinator.StartAsync(firstParameters, TestContext.Current.CancellationToken);
        firstHandle.Should().NotBeNull();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(secondParameters, TestContext.Current.CancellationToken));
        ex.Message.Should().Be("IMPORT_IN_PROGRESS");

        using var verifyScope = provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();
        var runs = await db.ImportRuns.ToListAsync(TestContext.Current.CancellationToken);
        runs.Should().HaveCount(2);
        runs.Should().ContainSingle(r => r.Id == secondParameters.ImportRunId && r.Status == ImportRunStatus.Failed);
    }

    private static IServiceCollection CreateConcurrentServices(string databaseName)
    {
        var services = new ServiceCollection();

        services.AddScoped<MediaHandlerDbContext>(_ =>
        {
            var options = new DbContextOptionsBuilder<MediaHandlerDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            return new ThrowingOnSecondRunningTransitionDbContext(options);
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MediaHandlerDbContext>());

        services.AddScoped<IKodiVideoDbReader>(_ =>
        {
            var reader = Substitute.For<IKodiVideoDbReader>();
            reader.ReadAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new KodiLibrarySnapshot([], [], []));
            return reader;
        });
        services.AddScoped<ITmdbService>(_ => Substitute.For<ITmdbService>());
        services.AddScoped<ITmdbMatcher, TmdbMatcher>();
        services.AddScoped<KodiImportPipeline>();
        services.AddScoped<IKodiImportFileStore>(_ =>
        {
            var store = Substitute.For<IKodiImportFileStore>();
            store.Delete(Arg.Any<string?>());
            return store;
        });
        services.AddLogging();

        return services;
    }

    private class ThrowingOnSecondSaveDbContext(DbContextOptions<MediaHandlerDbContext> options) : MediaHandlerDbContext(options)
    {
        private int _saveCount;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == 2)
                throw new DbUpdateException("Simulated concurrent Running import");

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    ///     Simulates the unique-index race for the Pending→Running transition:
    ///     the first transition succeeds, the second one fails because another import
    ///     is already running.
    /// </summary>
    private class ThrowingOnSecondRunningTransitionDbContext(DbContextOptions<MediaHandlerDbContext> options) : MediaHandlerDbContext(options)
    {
        private static int _pendingToRunningCount;

        public static void ResetCounter() => Interlocked.Exchange(ref _pendingToRunningCount, 0);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var isPendingToRunning = ChangeTracker.Entries<ImportRun>()
                .Any(e => e.State == EntityState.Modified
                          && e.OriginalValues.GetValue<ImportRunStatus>(nameof(ImportRun.Status)) == ImportRunStatus.Pending
                          && e.CurrentValues.GetValue<ImportRunStatus>(nameof(ImportRun.Status)) == ImportRunStatus.Running);

            if (isPendingToRunning)
            {
                var count = Interlocked.Increment(ref _pendingToRunningCount);
                if (count == 2)
                    throw new DbUpdateException("Simulated concurrent Running import");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    // =====================================================================
    // Post-import enrichment trigger tests
    // =====================================================================

    [Fact]
    public async Task StartAsync_ImportMode_CompletesAndTriggersEnrichment()
    {
        var sender = new CapturingSender(Result.Success(new StartEnrichmentResult(
            WasStarted: true,
            EnrichmentRunId: Guid.NewGuid(),
            Status: EnrichmentStatus.Pending,
            TotalItems: 3)));

        var (coordinator, provider) = CreateCoordinatorWithSender(sender);

        var parameters = new KodiImportStartParameters(
            Guid.NewGuid(), "/tmp/test.db", "MyVideos121.db", 121,
            KodiImportMode.Import, []);

        await coordinator.StartAsync(parameters, TestContext.Current.CancellationToken);

        var run = await WaitForTerminalRunAsync(provider, parameters.ImportRunId);
        run.Status.Should().Be(ImportRunStatus.Completed);
        sender.LastCommand.Should().BeOfType<StartEnrichmentCommand>();
    }

    [Fact]
    public async Task StartAsync_PreviewMode_CompletesWithoutTriggeringEnrichment()
    {
        var sender = new CapturingSender(Result.Success(new StartEnrichmentResult(
            WasStarted: false,
            EnrichmentRunId: null,
            Status: EnrichmentStatus.Completed,
            TotalItems: 0)));

        var (coordinator, provider) = CreateCoordinatorWithSender(sender);

        var parameters = new KodiImportStartParameters(
            Guid.NewGuid(), "/tmp/test.db", "MyVideos121.db", 121,
            KodiImportMode.Preview, []);

        await coordinator.StartAsync(parameters, TestContext.Current.CancellationToken);

        var run = await WaitForTerminalRunAsync(provider, parameters.ImportRunId);
        run.Status.Should().Be(ImportRunStatus.Completed);
        sender.LastCommand.Should().BeNull();
    }

    [Fact]
    public async Task StartAsync_ImportMode_WhenEnrichmentAlreadyRunning_RunStillCompletes()
    {
        var sender = new CapturingSender(Result.Fail<StartEnrichmentResult>(
            "ENRICHMENT_ALREADY_RUNNING: An enrichment run is already in progress."));

        var (coordinator, provider) = CreateCoordinatorWithSender(sender);

        var parameters = new KodiImportStartParameters(
            Guid.NewGuid(), "/tmp/test.db", "MyVideos121.db", 121,
            KodiImportMode.Import, []);

        await coordinator.StartAsync(parameters, TestContext.Current.CancellationToken);

        var run = await WaitForTerminalRunAsync(provider, parameters.ImportRunId);
        run.Status.Should().Be(ImportRunStatus.Completed);
        sender.LastCommand.Should().BeOfType<StartEnrichmentCommand>();
    }

    private static (ImportRunCoordinator Coordinator, ServiceProvider Provider) CreateCoordinatorWithSender(ISender sender)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();

        services.AddScoped<MediaHandlerDbContext>(_ =>
        {
            var options = new DbContextOptionsBuilder<MediaHandlerDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            return new MediaHandlerDbContext(options);
        });
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<MediaHandlerDbContext>());

        services.AddScoped<IKodiVideoDbReader>(_ =>
        {
            var reader = Substitute.For<IKodiVideoDbReader>();
            reader.ReadAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new KodiLibrarySnapshot([], [], []));
            return reader;
        });
        services.AddScoped<ITmdbService>(_ => Substitute.For<ITmdbService>());
        services.AddScoped<ITmdbMatcher, TmdbMatcher>();
        services.AddScoped<KodiImportPipeline>();
        services.AddScoped<IKodiImportFileStore>(_ =>
        {
            var store = Substitute.For<IKodiImportFileStore>();
            store.Delete(Arg.Any<string?>());
            return store;
        });
        services.AddSingleton(sender);
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var coordinator = new ImportRunCoordinator(
            NullLogger<ImportRunCoordinator>.Instance,
            provider.GetRequiredService<IServiceScopeFactory>());

        return (coordinator, provider);
    }

    private static async Task<ImportRun> WaitForTerminalRunAsync(ServiceProvider provider, Guid runId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();

        for (var i = 0; i < 50; i++)
        {
            var run = await db.ImportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId);
            if (run is { Status: ImportRunStatus.Completed or ImportRunStatus.Failed })
                return run;

            await Task.Delay(100);
        }

        throw new TimeoutException("Import run did not reach a terminal state within the expected time.");
    }

    private sealed class CapturingSender(Result<StartEnrichmentResult> response) : ISender
    {
        public object? LastCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastCommand = request;
            return ResolveResponse<TResponse>();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastCommand = request;
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
        {
            LastCommand = request;
            return ResolveResponse<TResponse>();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish(INotification notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private Task<TResponse> ResolveResponse<TResponse>()
        {
            if (response is Result<StartEnrichmentResult> r && typeof(TResponse) == typeof(Result<StartEnrichmentResult>))
            {
                return Task.FromResult((TResponse)(object)r);
            }

            throw new InvalidOperationException($"Unexpected response type: {typeof(TResponse)}");
        }
    }
}

using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.API.Extensions;

public static class DatabaseInitializer
{
    /// <summary>
    ///     Applies any pending EF Core migrations and seeds the default dev user.
    ///     Call this only in Development.
    ///     For non-relational providers (e.g., EF Core InMemory used in integration tests),
    ///     <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreatedAsync" /> is used instead.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();

        // InMemory (and other non-relational) providers don't support migrations.
        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync();
            return;
        }

        if (!await db.Database.CanConnectAsync())
        {
            // Database does not exist yet — create it by applying all migrations.
            await db.Database.MigrateAsync();
        }
        else
        {
            var pending = await db.Database.GetPendingMigrationsAsync();
            if (pending.Any())
                await db.Database.MigrateAsync();
        }

        await CleanUpStaleEnrichmentRunsAsync(db);
    }

    /// <summary>
    ///     Transitions any <c>EnrichmentRun</c> rows stuck in the <c>Running</c> state to
    ///     <c>Failed</c> with a crash-recovery reason.  This guards against runs that were
    ///     interrupted by a process restart and would otherwise block future enrichment jobs
    ///     (the filtered unique index permits at most one <c>Running</c> row at a time).
    /// </summary>
    private static async Task CleanUpStaleEnrichmentRunsAsync(MediaHandlerDbContext db)
    {
        var staleRuns = await db.EnrichmentRuns
            .Where(r => r.Status == EnrichmentStatus.Running)
            .ToListAsync();

        if (staleRuns.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var run in staleRuns)
        {
            run.Status = EnrichmentStatus.Failed;
            run.FailureReason = "Process restarted unexpectedly";
            run.FinishedAt = now;
        }

        await db.SaveChangesAsync();
    }
}
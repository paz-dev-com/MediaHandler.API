using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

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

        try
        {
            // Attempt to migrate the database.
            // This will create the database and apply all migrations if it doesn't exist,
            // or just apply pending migrations if it already exists.
            await db.Database.MigrateAsync();
        }
        catch (SqlException ex) when (ex.Number == 1801)
        {
            // Error 1801: "Database already exists"
            // This happens when the container restarts and the persisted database in the volume
            // still exists. In this case, just apply any pending migrations.
            var logger = app.Services.GetRequiredService<ILogger<object>>();
            logger.LogInformation("Database already exists. Checking for pending migrations...");

            var pending = await db.Database.GetPendingMigrationsAsync();
            if (pending.Any())
            {
                logger.LogInformation("Found {PendingCount} pending migrations. Applying migrations...", pending.Count());
                await db.Database.MigrateAsync();
            }
            else
            {
                logger.LogInformation("No pending migrations found. Database is up to date.");
            }
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
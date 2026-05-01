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
    }
}
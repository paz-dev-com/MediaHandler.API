using MediaHandler.Domain.Entities;
using MediaHandler.Domain.Enums;
using MediaHandler.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaHandler.API.Extensions;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies any pending EF Core migrations and seeds the default dev user.
    /// Call this only in Development.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaHandlerDbContext>();

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

        await SeedDevUserAsync(db);
    }

    private static async Task SeedDevUserAsync(MediaHandlerDbContext db)
    {
        const string devOktaId = "okta|devuser1";

        if (!await db.Users.AnyAsync(u => u.OktaId == devOktaId))
        {
            db.Users.Add(new User
            {
                OktaId = devOktaId,
                Email = "dev@local.com",
                DisplayName = "Dev User",
                Role = UserRole.Admin
            });

            await db.SaveChangesAsync();
        }
    }
}

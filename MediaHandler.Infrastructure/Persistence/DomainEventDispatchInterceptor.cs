using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaHandler.Infrastructure.Persistence;

public class DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    private List<IDomainEvent> _pendingEvents = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _pendingEvents = CollectAndClear(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var savedResult = await base.SavedChangesAsync(eventData, result, cancellationToken);

        var events = _pendingEvents;
        _pendingEvents = [];

        if (events.Count > 0)
            await dispatcher.DispatchAsync(events, cancellationToken);

        return savedResult;
    }

    private static List<IDomainEvent> CollectAndClear(DbContext? context)
    {
        if (context is null) return [];

        var events = context.ChangeTracker
            .Entries<BaseEntity>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
            entry.Entity.ClearDomainEvents();

        return events;
    }
}

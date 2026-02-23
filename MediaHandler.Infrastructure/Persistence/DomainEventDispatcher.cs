using MediatR;
using MediaHandler.Application.Common.Interfaces;
using MediaHandler.Domain.Common;

namespace MediaHandler.Infrastructure.Persistence;

public class DomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var ev in events)
        {
            if (ev is INotification notification)
                await publisher.Publish(notification, cancellationToken);
        }
    }
}

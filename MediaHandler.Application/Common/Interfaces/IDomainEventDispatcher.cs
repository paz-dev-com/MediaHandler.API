using MediaHandler.Domain.Common;

namespace MediaHandler.Application.Common.Interfaces;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
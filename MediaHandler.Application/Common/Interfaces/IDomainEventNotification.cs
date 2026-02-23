using MediatR;
using MediaHandler.Domain.Common;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Marker interface for domain events that are dispatched via MediatR.
/// Implement this instead of <see cref="IDomainEvent"/> directly when you need
/// an <see cref="INotificationHandler{TNotification}"/> to handle the event.
/// </summary>
public interface IDomainEventNotification : IDomainEvent, INotification { }

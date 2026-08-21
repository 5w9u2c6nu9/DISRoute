using DISRoute.Domain.Events;
namespace DISRoute.Application.Events;
public interface IEventBus { ValueTask PublishAsync(DomainEvent evt,CancellationToken cancellationToken=default); }

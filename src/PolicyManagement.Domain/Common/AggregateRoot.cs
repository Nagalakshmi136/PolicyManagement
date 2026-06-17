namespace PolicyManagement.Domain.Common;

/// <summary>
/// Base class for all aggregate roots. Holds a collection of domain events
/// raised during the lifetime of the aggregate that can be dispatched after
/// the unit of work is committed.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents()
        => _domainEvents.Clear();
}

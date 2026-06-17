namespace PolicyManagement.Domain.Common;

/// <summary>
/// Marker interface for all domain events raised by aggregate roots.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

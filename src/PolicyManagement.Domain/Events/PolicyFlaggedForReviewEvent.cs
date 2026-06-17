using PolicyManagement.Domain.Common;

namespace PolicyManagement.Domain.Events;

/// <summary>
/// Raised by <see cref="Entities.Policy.FlagForReview"/> when a policy
/// is successfully flagged for underwriter review for the first time.
/// </summary>
public sealed record PolicyFlaggedForReviewEvent(
    string PolicyId,
    DateTime FlaggedAt) : IDomainEvent
{
    public DateTime OccurredAt => FlaggedAt;
}

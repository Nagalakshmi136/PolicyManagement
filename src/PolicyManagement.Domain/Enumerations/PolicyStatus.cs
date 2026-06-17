namespace PolicyManagement.Domain.Enumerations;

/// <summary>
/// Represents the lifecycle state of an insurance policy.
/// Stored in the database as the string name (e.g. "Active", "Expired").
/// </summary>
public enum PolicyStatus
{
    Active,
    Expired,
    Pending,
    Cancelled
}

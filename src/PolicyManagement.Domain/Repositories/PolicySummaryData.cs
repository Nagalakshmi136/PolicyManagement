using PolicyManagement.Domain.Enumerations;

namespace PolicyManagement.Domain.Repositories;

/// <summary>
/// Aggregate DTO returned by <see cref="IPolicyRepository.GetSummaryAsync"/>.
/// All computation is performed server-side in SQL so the application layer
/// never loads the full policy table into memory.
/// </summary>
public sealed record PolicySummaryData(
    /// <summary>Total number of policies in the system.</summary>
    int TotalCount,

    /// <summary>Number of Active policies.</summary>
    int ActiveCount,

    /// <summary>Number of Expired policies.</summary>
    int ExpiredCount,

    /// <summary>Number of Pending policies.</summary>
    int PendingCount,

    /// <summary>Number of Cancelled policies.</summary>
    int CancelledCount,

    /// <summary>
    /// Premium totals grouped by LineOfBusiness and Currency.
    /// Key: (LineOfBusiness, Currency)  Value: sum of PremiumAmount.
    /// </summary>
    IReadOnlyDictionary<(LineOfBusiness LineOfBusiness, string Currency), decimal> PremiumByLobAndCurrency,

    /// <summary>
    /// Number of policies whose ExpiryDate falls within the window
    /// [today, expiringSoonCutoff] (inclusive).
    /// </summary>
    int ExpiringSoonCount);

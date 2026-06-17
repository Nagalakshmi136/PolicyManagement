using PolicyManagement.Domain.Common;
using PolicyManagement.Domain.Entities;

namespace PolicyManagement.Domain.Repositories;

/// <summary>
/// Read/query interface for the <see cref="Policy"/> aggregate. Mutations are
/// persisted through <c>IUnitOfWork</c> in the Application layer — there is
/// no explicit Save on this interface.
/// </summary>
public interface IPolicyRepository
{
    /// <summary>Returns a single policy by its surrogate UUID id, or null.</summary>
    Task<Policy?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all policies whose ids are in <paramref name="ids"/>.
    /// Policies whose ids are not found are simply omitted from the result.
    /// </summary>
    Task<IReadOnlyList<Policy>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies filtering, full-text search, sorting, and pagination as
    /// specified by <paramref name="criteria"/> and returns a page of results.
    /// </summary>
    Task<PagedResult<Policy>> SearchAsync(
        PolicySearchCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes summary statistics server-side in SQL and returns a lightweight
    /// aggregate DTO. The <paramref name="expiringSoonCutoff"/> is the upper
    /// boundary (inclusive) for the expiring-soon count — typically
    /// <c>DateOnly.FromDateTime(DateTime.UtcNow).AddDays(expiringSoonDays)</c>.
    /// </summary>
    Task<PolicySummaryData> GetSummaryAsync(
        DateOnly expiringSoonCutoff,
        CancellationToken cancellationToken = default);
}

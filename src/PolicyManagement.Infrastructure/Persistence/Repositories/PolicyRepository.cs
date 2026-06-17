using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Common;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPolicyRepository"/>.
/// All methods are <c>internal sealed</c>; never calls <c>SaveChangesAsync</c>
/// directly — that is the responsibility of <see cref="UnitOfWork"/>.
/// </summary>
internal sealed class PolicyRepository(AppDbContext db) : IPolicyRepository
{
    // ------------------------------------------------------------------
    // Single-record lookups
    // ------------------------------------------------------------------

    public async Task<Policy?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default) =>
        await db.Policies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Policy>> GetByIdsAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await db.Policies
            .AsNoTracking()
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    // ------------------------------------------------------------------
    // Paginated search
    // ------------------------------------------------------------------

    public async Task<PagedResult<Policy>> SearchAsync(
        PolicySearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = db.Policies.AsNoTracking().AsQueryable();

        // Filters
        if (criteria.Status.HasValue)
            query = query.Where(p => p.Status == criteria.Status.Value);

        if (criteria.LineOfBusiness.HasValue)
            query = query.Where(p => p.LineOfBusiness == criteria.LineOfBusiness.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Region))
            query = query.Where(p => p.Region == criteria.Region);

        if (criteria.EffectiveDateFrom.HasValue)
            query = query.Where(p => p.EffectiveDate >= criteria.EffectiveDateFrom.Value);

        if (criteria.EffectiveDateTo.HasValue)
            query = query.Where(p => p.EffectiveDate <= criteria.EffectiveDateTo.Value);

        if (!string.IsNullOrWhiteSpace(criteria.SearchTerm))
        {
            var term = criteria.SearchTerm.Trim();
            query = query.Where(p =>
                p.PolicyNumber.Contains(term) ||
                p.PolicyholderName.Contains(term) ||
                p.Underwriter.Contains(term));
        }

        // Sort
        query = ApplySort(query, criteria.SortBy, criteria.SortDescending);

        // Paginate
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Policy>(items, totalCount, criteria.Page, criteria.PageSize);
    }

    // ------------------------------------------------------------------
    // Summary statistics — server-side aggregation, never loads full table
    // ------------------------------------------------------------------

    public async Task<PolicySummaryData> GetSummaryAsync(
        DateOnly expiringSoonCutoff,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Status counts
        var statusGroups = await db.Policies
            .AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Premium totals grouped by LineOfBusiness AND Currency
        var premiumGroups = await db.Policies
            .AsNoTracking()
            .GroupBy(p => new { p.LineOfBusiness, p.Currency })
            .Select(g => new
            {
                g.Key.LineOfBusiness,
                g.Key.Currency,
                Total = g.Sum(p => p.PremiumAmount)
            })
            .ToListAsync(cancellationToken);

        // Expiring soon: expiryDate in [today, expiringSoonCutoff]
        var expiringSoonCount = await db.Policies
            .AsNoTracking()
            .CountAsync(
                p => p.ExpiryDate >= today && p.ExpiryDate <= expiringSoonCutoff,
                cancellationToken);

        int CountByStatus(PolicyStatus s) =>
            statusGroups.FirstOrDefault(g => g.Status == s)?.Count ?? 0;

        var activeCount    = CountByStatus(PolicyStatus.Active);
        var expiredCount   = CountByStatus(PolicyStatus.Expired);
        var pendingCount   = CountByStatus(PolicyStatus.Pending);
        var cancelledCount = CountByStatus(PolicyStatus.Cancelled);

        return new PolicySummaryData(
            TotalCount: activeCount + expiredCount + pendingCount + cancelledCount,
            ActiveCount: activeCount,
            ExpiredCount: expiredCount,
            PendingCount: pendingCount,
            CancelledCount: cancelledCount,
            PremiumByLobAndCurrency: premiumGroups.ToDictionary(
                g => (g.LineOfBusiness, g.Currency),
                g => g.Total),
            ExpiringSoonCount: expiringSoonCount);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IQueryable<Policy> ApplySort(
        IQueryable<Policy> query,
        string? sortBy,
        bool descending) =>
        sortBy?.ToLowerInvariant() switch
        {
            "policynumber"      => descending ? query.OrderByDescending(p => p.PolicyNumber)      : query.OrderBy(p => p.PolicyNumber),
            "policyholdername"  => descending ? query.OrderByDescending(p => p.PolicyholderName)  : query.OrderBy(p => p.PolicyholderName),
            "premiumamount"     => descending ? query.OrderByDescending(p => p.PremiumAmount)     : query.OrderBy(p => p.PremiumAmount),
            "effectivedate"     => descending ? query.OrderByDescending(p => p.EffectiveDate)     : query.OrderBy(p => p.EffectiveDate),
            "expirydate"        => descending ? query.OrderByDescending(p => p.ExpiryDate)        : query.OrderBy(p => p.ExpiryDate),
            "status"            => descending ? query.OrderByDescending(p => p.Status)            : query.OrderBy(p => p.Status),
            "lineofbusiness"    => descending ? query.OrderByDescending(p => p.LineOfBusiness)    : query.OrderBy(p => p.LineOfBusiness),
            "region"            => descending ? query.OrderByDescending(p => p.Region)            : query.OrderBy(p => p.Region),
            "underwriter"       => descending ? query.OrderByDescending(p => p.Underwriter)       : query.OrderBy(p => p.Underwriter),
            _                   => descending ? query.OrderByDescending(p => p.CreatedAt)         : query.OrderBy(p => p.CreatedAt),
        };
}

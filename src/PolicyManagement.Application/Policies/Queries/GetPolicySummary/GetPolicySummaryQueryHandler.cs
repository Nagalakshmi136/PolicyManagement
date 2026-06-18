using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolicyManagement.Application.Common.Options;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Policies.Queries.GetPolicySummary;

/// <summary>
/// Handles <see cref="GetPolicySummaryQuery"/>: computes the cutoff date
/// from <see cref="CacheOptions.ExpiringSoonDays"/>, delegates all SQL
/// aggregation to <see cref="IPolicyRepository.GetSummaryAsync"/>, and maps
/// the domain aggregate to <see cref="PolicySummaryStatsDto"/>.
///
/// Per ADR-009, premium totals are grouped by BOTH <c>lineOfBusiness</c> AND
/// <c>currency</c> — never summed across currencies.
/// </summary>
internal sealed class GetPolicySummaryQueryHandler(
    IPolicyRepository policyRepository,
    IOptions<CacheOptions> cacheOptions,
    ILogger<GetPolicySummaryQueryHandler> logger)
    : IRequestHandler<GetPolicySummaryQuery, PolicySummaryStatsDto>
{
    public async Task<PolicySummaryStatsDto> Handle(
        GetPolicySummaryQuery query,
        CancellationToken cancellationToken)
    {
        var expiringSoonDays = cacheOptions.Value.ExpiringSoonDays;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(expiringSoonDays);

        var summary = await policyRepository.GetSummaryAsync(cutoff, cancellationToken);

        logger.LogInformation(
            "PolicySummary: total={Total}, expiringSoon={ExpiringSoon}, cutoff={Cutoff}",
            summary.TotalCount,
            summary.ExpiringSoonCount,
            cutoff);

        var premiumItems = summary.PremiumByLobAndCurrency
            .Select(kvp => new PremiumByLobAndCurrencyDto(
                LineOfBusiness: kvp.Key.LineOfBusiness == LineOfBusiness.AccidentAndHealth
                    ? "A&H"
                    : kvp.Key.LineOfBusiness.ToString(),
                Currency: kvp.Key.Currency,
                TotalPremium: kvp.Value))
            .OrderBy(x => x.LineOfBusiness)
            .ThenBy(x => x.Currency)
            .ToList()
            .AsReadOnly();

        return new PolicySummaryStatsDto(
            CountsByStatus: new CountsByStatusDto(
                Active: summary.ActiveCount,
                Expired: summary.ExpiredCount,
                Pending: summary.PendingCount,
                Cancelled: summary.CancelledCount),
            TotalPremiumByLineOfBusiness: premiumItems,
            ExpiringSoonCount: summary.ExpiringSoonCount);
    }
}

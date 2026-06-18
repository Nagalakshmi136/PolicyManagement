namespace PolicyManagement.Application.Policies.DTOs;

/// <summary>
/// Aggregated portfolio-level statistics returned by
/// <c>GetPolicySummaryQueryHandler</c>.
/// Matches the <c>PolicySummaryStats</c> schema in the OpenAPI spec.
/// </summary>
public sealed record PolicySummaryStatsDto(
    CountsByStatusDto CountsByStatus,
    IReadOnlyList<PremiumByLobAndCurrencyDto> TotalPremiumByLineOfBusiness,
    int ExpiringSoonCount);

namespace PolicyManagement.Application.Policies.DTOs;

/// <summary>
/// Total premium for a single (lineOfBusiness, currency) group.
/// Matches the <c>PremiumByLineOfBusiness</c> schema in the OpenAPI spec.
/// Grouped by BOTH lineOfBusiness AND currency per ADR-009 to avoid
/// misleading cross-currency aggregation.
/// </summary>
public sealed record PremiumByLobAndCurrencyDto(
    string LineOfBusiness,
    string Currency,
    decimal TotalPremium);

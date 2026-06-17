namespace PolicyManagement.Application.Policies.DTOs;

/// <summary>
/// Lean list-item DTO returned by <c>ListPoliciesQueryHandler</c>.
/// Matches the <c>PolicySummaryItem</c> schema in the OpenAPI spec.
/// <c>LineOfBusiness</c> is serialised as a string so that the
/// <c>AccidentAndHealth</c> enum value maps to the OpenAPI value "A&amp;H".
/// </summary>
public sealed record PolicySummaryDto(
    string Id,
    string PolicyNumber,
    string PolicyholderName,
    string LineOfBusiness,
    string Status,
    decimal PremiumAmount,
    string Currency,
    DateOnly EffectiveDate,
    DateOnly ExpiryDate,
    string Region,
    string Underwriter,
    bool FlaggedForReview,
    DateTime CreatedAt,
    DateTime UpdatedAt);

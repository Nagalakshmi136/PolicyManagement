namespace PolicyManagement.Application.Policies.DTOs;

/// <summary>
/// Full policy record returned by <c>GetPolicyByIdQueryHandler</c>.
/// Matches the <c>PolicyResponse</c> schema in the OpenAPI spec.
/// <c>LineOfBusiness</c> is a string so that the <c>AccidentAndHealth</c>
/// enum value maps to the OpenAPI value "A&amp;H".
/// </summary>
public sealed record PolicyDto(
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

namespace PolicyManagement.Api.Contracts;

/// <summary>
/// Query parameters for <c>GET /api/v1/policies</c>.
/// All parameters are optional. Validated by <c>ListPoliciesQueryValidator</c>
/// in the Application layer after mapping to <c>ListPoliciesQuery</c>.
/// </summary>
public sealed record ListPoliciesRequest(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? LineOfBusiness = null,
    string? Region = null,
    DateOnly? EffectiveDateFrom = null,
    DateOnly? EffectiveDateTo = null,
    string? Search = null,
    string? SortBy = null,
    string? SortDirection = null);

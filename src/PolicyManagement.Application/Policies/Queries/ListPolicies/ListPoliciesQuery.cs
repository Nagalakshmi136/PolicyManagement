using MediatR;
using PolicyManagement.Application.Common.Models;
using PolicyManagement.Application.Policies.DTOs;

namespace PolicyManagement.Application.Policies.Queries.ListPolicies;

/// <summary>
/// Query for the paginated list of policies.
/// Corresponds to <c>GET /api/v1/policies</c> in the OpenAPI spec.
/// <para>
/// <c>Status</c> and <c>LineOfBusiness</c> are accepted as strings so that
/// <c>ListPoliciesQueryValidator</c> can produce structured validation errors
/// before any enum parsing occurs.  The handler converts them to domain enum
/// types when building <c>PolicySearchCriteria</c>.
/// </para>
/// </summary>
public sealed record ListPoliciesQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    string? LineOfBusiness = null,
    string? Region = null,
    DateOnly? EffectiveDateFrom = null,
    DateOnly? EffectiveDateTo = null,
    string? Search = null,
    string? SortBy = null,
    string? SortDirection = null) : IRequest<PagedResponse<PolicySummaryDto>>;

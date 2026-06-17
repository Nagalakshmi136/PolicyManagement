using MediatR;
using Microsoft.Extensions.Logging;
using PolicyManagement.Application.Common.Models;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Policies.Queries.ListPolicies;

/// <summary>
/// Handles <see cref="ListPoliciesQuery"/>: applies filter/sort/pagination via
/// the repository and maps each <see cref="Policy"/> aggregate to a
/// <see cref="PolicySummaryDto"/> for the HTTP response.
/// </summary>
internal sealed class ListPoliciesQueryHandler(
    IPolicyRepository policyRepository,
    ILogger<ListPoliciesQueryHandler> logger)
    : IRequestHandler<ListPoliciesQuery, PagedResponse<PolicySummaryDto>>
{
    public async Task<PagedResponse<PolicySummaryDto>> Handle(
        ListPoliciesQuery query,
        CancellationToken cancellationToken)
    {
        var criteria = new PolicySearchCriteria(
            Page: query.Page,
            PageSize: query.PageSize,
            Status: query.Status is not null
                ? Enum.Parse<PolicyStatus>(query.Status)
                : null,
            LineOfBusiness: query.LineOfBusiness is not null
                ? ParseLineOfBusiness(query.LineOfBusiness)
                : null,
            Region: query.Region,
            SearchTerm: query.Search,
            SortBy: query.SortBy ?? "createdAt",
            SortDescending: string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase),
            EffectiveDateFrom: query.EffectiveDateFrom,
            EffectiveDateTo: query.EffectiveDateTo);

        var pagedResult = await policyRepository.SearchAsync(criteria, cancellationToken);

        logger.LogInformation(
            "ListPolicies returned {TotalCount} total records (page {Page} of {TotalPages})",
            pagedResult.TotalCount,
            pagedResult.Page,
            pagedResult.TotalPages);

        return PagedResponse<PolicySummaryDto>.From(pagedResult, ToDto);
    }

    private static PolicySummaryDto ToDto(Policy policy) => new(
        Id: policy.Id,
        PolicyNumber: policy.PolicyNumber,
        PolicyholderName: policy.PolicyholderName,
        LineOfBusiness: policy.LineOfBusiness == Domain.Enumerations.LineOfBusiness.AccidentAndHealth
            ? "A&H"
            : policy.LineOfBusiness.ToString(),
        Status: policy.Status.ToString(),
        PremiumAmount: policy.PremiumAmount,
        Currency: policy.Currency,
        EffectiveDate: policy.EffectiveDate,
        ExpiryDate: policy.ExpiryDate,
        Region: policy.Region,
        Underwriter: policy.Underwriter,
        FlaggedForReview: policy.FlaggedForReview,
        CreatedAt: policy.CreatedAt,
        UpdatedAt: policy.UpdatedAt);

    private static LineOfBusiness ParseLineOfBusiness(string value) =>
        value == "A&H"
            ? LineOfBusiness.AccidentAndHealth
            : Enum.Parse<LineOfBusiness>(value);
}

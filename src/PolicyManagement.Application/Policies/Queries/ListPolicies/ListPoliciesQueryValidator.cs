using FluentValidation;

namespace PolicyManagement.Application.Policies.Queries.ListPolicies;

/// <summary>
/// Validates all parameters of <see cref="ListPoliciesQuery"/> before the
/// handler runs.  Invalid queries are rejected with HTTP 400 by
/// <c>ValidationBehaviour</c> and <c>ExceptionHandlingMiddleware</c>.
/// </summary>
public sealed class ListPoliciesQueryValidator : AbstractValidator<ListPoliciesQuery>
{
    private static readonly IReadOnlySet<string> AllowedSortByFields =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "policyNumber", "policyholderName", "lineOfBusiness", "status",
            "premiumAmount", "currency", "effectiveDate", "expiryDate",
            "region", "underwriter", "flaggedForReview", "createdAt", "updatedAt"
        };

    private static readonly IReadOnlySet<string> AllowedStatuses =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Active", "Expired", "Pending", "Cancelled"
        };

    private static readonly IReadOnlySet<string> AllowedLinesOfBusiness =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Property", "Casualty", "A&H", "Marine"
        };

    private static readonly IReadOnlySet<string> AllowedRegions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Singapore", "Hong Kong", "Australia", "Japan",
            "Thailand", "Indonesia", "Malaysia", "Philippines"
        };

    public ListPoliciesQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("page must be at least 1.");

        RuleFor(x => x.PageSize)
            .GreaterThanOrEqualTo(1)
            .WithMessage("pageSize must be at least 1.")
            .LessThanOrEqualTo(100)
            .WithMessage("pageSize must not exceed 100.");

        RuleFor(x => x.SortBy)
            .Must(s => AllowedSortByFields.Contains(s!))
            .When(x => x.SortBy is not null)
            .WithMessage("sortBy must be one of the allowed field names.");

        RuleFor(x => x.SortDirection)
            .Must(s => s == "asc" || s == "desc")
            .When(x => x.SortDirection is not null)
            .WithMessage("sortDirection must be 'asc' or 'desc'.");

        RuleFor(x => x.Status)
            .Must(s => AllowedStatuses.Contains(s!))
            .When(x => x.Status is not null)
            .WithMessage("status must be one of: Active, Expired, Pending, Cancelled.");

        RuleFor(x => x.LineOfBusiness)
            .Must(s => AllowedLinesOfBusiness.Contains(s!))
            .When(x => x.LineOfBusiness is not null)
            .WithMessage("lineOfBusiness must be one of: Property, Casualty, A&H, Marine.");

        RuleFor(x => x.Region)
            .Must(s => AllowedRegions.Contains(s!))
            .When(x => x.Region is not null)
            .WithMessage("region must be one of the supported APAC values.");

        RuleFor(x => x.Search)
            .MaximumLength(200)
            .When(x => x.Search is not null)
            .WithMessage("search must not exceed 200 characters.");

        RuleFor(x => x)
            .Must(x => x.EffectiveDateFrom is null || x.EffectiveDateTo is null
                       || x.EffectiveDateFrom <= x.EffectiveDateTo)
            .WithMessage("effectiveDateFrom must not be after effectiveDateTo.")
            .WithName("effectiveDateFrom");
    }
}

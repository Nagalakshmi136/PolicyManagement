using FluentValidation;

namespace PolicyManagement.Application.Policies.Commands.FlagPolicies;

/// <summary>
/// Validates <see cref="FlagPoliciesCommand"/> before the handler runs.
/// Per ADR-007 and the OpenAPI spec:
/// <list type="bullet">
///   <item>At least one ID is required (empty array → 400).</item>
///   <item>Maximum 100 IDs per request.</item>
///   <item>Each element must be a non-empty, well-formed UUID.</item>
/// </list>
/// </summary>
public sealed class FlagPoliciesCommandValidator : AbstractValidator<FlagPoliciesCommand>
{
    public FlagPoliciesCommandValidator()
    {
        RuleFor(x => x.PolicyIds)
            .NotEmpty()
            .WithMessage("policyIds must not be empty.")
            .Must(ids => ids.Count >= 1)
            .WithMessage("policyIds must contain at least one ID.")
            .Must(ids => ids.Count <= 100)
            .WithMessage("policyIds must not exceed 100 IDs per request.");

        RuleForEach(x => x.PolicyIds)
            .NotEmpty()
            .WithMessage("Each policy ID must not be empty.")
            .Must(id => Guid.TryParse(id, out _))
            .WithMessage("Each policy ID must be a valid UUID.");
    }
}

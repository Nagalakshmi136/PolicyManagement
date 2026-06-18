using FluentValidation;

namespace PolicyManagement.Application.Policies.Queries.GetPolicyById;

/// <summary>
/// Validates <see cref="GetPolicyByIdQuery"/> before the handler runs.
/// The <c>id</c> path parameter must be a non-empty UUID string.
/// </summary>
public sealed class GetPolicyByIdQueryValidator : AbstractValidator<GetPolicyByIdQuery>
{
    public GetPolicyByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("id must not be empty.")
            .Must(BeAValidGuid)
            .WithMessage("id must be a valid UUID.");
    }

    private static bool BeAValidGuid(string? id)
        => Guid.TryParse(id, out _);
}

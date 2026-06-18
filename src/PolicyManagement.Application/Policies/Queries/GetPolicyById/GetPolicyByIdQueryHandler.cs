using MediatR;
using Microsoft.Extensions.Logging;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enumerations;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Policies.Queries.GetPolicyById;

/// <summary>
/// Handles <see cref="GetPolicyByIdQuery"/>: looks up the <see cref="Policy"/>
/// aggregate by its surrogate UUID and maps it to a <see cref="PolicyDto"/>.
/// Throws <see cref="NotFoundException"/> (→ HTTP 404) when the policy does
/// not exist.
/// </summary>
internal sealed class GetPolicyByIdQueryHandler(
    IPolicyRepository policyRepository,
    ILogger<GetPolicyByIdQueryHandler> logger)
    : IRequestHandler<GetPolicyByIdQuery, PolicyDto>
{
    public async Task<PolicyDto> Handle(
        GetPolicyByIdQuery query,
        CancellationToken cancellationToken)
    {
        var policy = await policyRepository.GetByIdAsync(query.Id, cancellationToken);

        if (policy is null)
        {
            logger.LogWarning("Policy with id {PolicyId} was not found", query.Id);
            throw new NotFoundException(nameof(Policy), query.Id);
        }

        logger.LogInformation("Retrieved policy {PolicyId}", policy.Id);

        return ToDto(policy);
    }

    private static PolicyDto ToDto(Policy policy) => new(
        Id: policy.Id,
        PolicyNumber: policy.PolicyNumber,
        PolicyholderName: policy.PolicyholderName,
        LineOfBusiness: policy.LineOfBusiness == LineOfBusiness.AccidentAndHealth
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
}

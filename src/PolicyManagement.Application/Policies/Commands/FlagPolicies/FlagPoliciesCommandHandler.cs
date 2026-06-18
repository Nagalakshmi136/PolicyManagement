using MediatR;
using Microsoft.Extensions.Logging;
using PolicyManagement.Application.Policies.DTOs;
using PolicyManagement.Domain.Repositories;

namespace PolicyManagement.Application.Policies.Commands.FlagPolicies;

/// <summary>
/// Handles <see cref="FlagPoliciesCommand"/> with partial-success semantics
/// per ADR-007:
/// <list type="bullet">
///   <item>Fetches all policies whose IDs appear in the request in a single
///         repository call via <see cref="IPolicyRepository.GetByIdsAsync"/>.</item>
///   <item>Calls <c>Policy.FlagForReview()</c> on each found policy (idempotent
///         — no-op if already flagged; domain event raised only on first flag).</item>
///   <item>Persists all mutations in one <see cref="IUnitOfWork.SaveChangesAsync"/>
///         call.</item>
///   <item>Returns <see cref="FlagPoliciesResponseDto"/> with <c>flaggedIds</c>
///         and <c>notFoundIds</c> computed from the set difference between the
///         requested IDs and the found IDs.</item>
/// </list>
/// Always returns HTTP 200 — even when zero IDs matched (all go to
/// <c>notFoundIds</c>). The operation is idempotent.
/// </summary>
internal sealed class FlagPoliciesCommandHandler(
    IPolicyRepository policyRepository,
    IUnitOfWork unitOfWork,
    ILogger<FlagPoliciesCommandHandler> logger)
    : IRequestHandler<FlagPoliciesCommand, FlagPoliciesResponseDto>
{
    public async Task<FlagPoliciesResponseDto> Handle(
        FlagPoliciesCommand command,
        CancellationToken cancellationToken)
    {
        var requestedIds = command.PolicyIds.ToHashSet(StringComparer.Ordinal);

        var foundPolicies = await policyRepository.GetByIdsAsync(requestedIds, cancellationToken);

        foreach (var policy in foundPolicies)
            policy.FlagForReview();

        if (foundPolicies.Count > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        var flaggedIds = foundPolicies.Select(p => p.Id).ToList().AsReadOnly();
        var foundIdSet = new HashSet<string>(flaggedIds, StringComparer.Ordinal);
        var notFoundIds = requestedIds
            .Where(id => !foundIdSet.Contains(id))
            .OrderBy(id => id)
            .ToList()
            .AsReadOnly();

        logger.LogInformation(
            "FlagPolicies: requested={Requested}, flagged={Flagged}, notFound={NotFound}",
            requestedIds.Count,
            flaggedIds.Count,
            notFoundIds.Count);

        return new FlagPoliciesResponseDto(
            FlaggedCount: flaggedIds.Count,
            FlaggedIds: flaggedIds,
            NotFoundIds: notFoundIds);
    }
}

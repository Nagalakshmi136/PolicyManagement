using MediatR;
using PolicyManagement.Application.Policies.DTOs;

namespace PolicyManagement.Application.Policies.Commands.FlagPolicies;

/// <summary>
/// Command to bulk-flag policies for underwriter review.
/// Corresponds to <c>PATCH /api/v1/policies/flag</c> in the OpenAPI spec.
/// Per ADR-007, partial-success semantics apply: policies that resolve to
/// existing records are flagged; IDs not found are reported in
/// <see cref="FlagPoliciesResponseDto.NotFoundIds"/>.
/// </summary>
public sealed record FlagPoliciesCommand(
    IReadOnlyList<string> PolicyIds) : IRequest<FlagPoliciesResponseDto>;

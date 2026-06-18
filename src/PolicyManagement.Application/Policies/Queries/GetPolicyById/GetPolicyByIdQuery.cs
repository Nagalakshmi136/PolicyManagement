using MediatR;
using PolicyManagement.Application.Policies.DTOs;

namespace PolicyManagement.Application.Policies.Queries.GetPolicyById;

/// <summary>
/// Query for a single policy by its surrogate UUID.
/// Corresponds to <c>GET /api/v1/policies/{id}</c> in the OpenAPI spec.
/// </summary>
public sealed record GetPolicyByIdQuery(string Id) : IRequest<PolicyDto>;

using MediatR;
using PolicyManagement.Application.Policies.DTOs;

namespace PolicyManagement.Application.Policies.Queries.GetPolicySummary;

/// <summary>
/// Query for aggregated portfolio-level statistics.
/// Corresponds to <c>GET /api/v1/policies/summary</c> in the OpenAPI spec.
/// No input parameters — the <c>expiringSoonDays</c> window is read from
/// <c>CacheOptions</c> inside the handler.
/// </summary>
public sealed record GetPolicySummaryQuery : IRequest<PolicySummaryStatsDto>;

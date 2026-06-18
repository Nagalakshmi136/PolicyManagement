namespace PolicyManagement.Application.Policies.DTOs;

/// <summary>
/// Policy counts broken down by lifecycle status.
/// Matches the <c>countsByStatus</c> property of <c>PolicySummaryStats</c>
/// in the OpenAPI spec.
/// </summary>
public sealed record CountsByStatusDto(
    int Active,
    int Expired,
    int Pending,
    int Cancelled);

namespace PolicyManagement.Application.Policies.DTOs;

/// <summary>
/// Partial-success response from the bulk flag operation.
/// Matches the <c>FlagPoliciesResponse</c> schema in the OpenAPI spec.
/// Per ADR-007, <c>NotFoundIds</c> is empty when all supplied IDs resolved.
/// The response is always HTTP 200 — the caller must inspect the body to
/// determine whether all IDs were found.
/// </summary>
public sealed record FlagPoliciesResponseDto(
    int FlaggedCount,
    IReadOnlyList<string> FlaggedIds,
    IReadOnlyList<string> NotFoundIds);

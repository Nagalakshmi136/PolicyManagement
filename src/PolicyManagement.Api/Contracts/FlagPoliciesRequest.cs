namespace PolicyManagement.Api.Contracts;

/// <summary>
/// Request body for <c>PATCH /api/v1/policies/flag</c>.
/// Per ADR-007, partial-success semantics apply: all resolvable IDs are flagged
/// and IDs not found are reported in the response body rather than returning an error.
/// </summary>
public sealed record FlagPoliciesRequest(
    IReadOnlyList<string> PolicyIds);

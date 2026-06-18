using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PolicyManagement.Api.Contracts;
using PolicyManagement.Application.Policies.Commands.FlagPolicies;
using PolicyManagement.Application.Policies.Queries.GetPolicyById;
using PolicyManagement.Application.Policies.Queries.GetPolicySummary;
using PolicyManagement.Application.Policies.Queries.ListPolicies;

namespace PolicyManagement.Api.Controllers;

/// <summary>
/// BFF controller for insurance policy lifecycle operations.
/// All actions delegate to MediatR handlers — no business logic lives here.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/policies")]
public sealed class PoliciesController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Returns a paginated, filterable, sortable list of policies.
    /// <c>GET /api/v1/policies</c>
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "PolicyRead")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] ListPoliciesRequest request, CancellationToken ct)
    {
        var query = new ListPoliciesQuery(
            Page:               request.Page,
            PageSize:           request.PageSize,
            Status:             request.Status,
            LineOfBusiness:     request.LineOfBusiness,
            Region:             request.Region,
            EffectiveDateFrom:  request.EffectiveDateFrom,
            EffectiveDateTo:    request.EffectiveDateTo,
            Search:             request.Search,
            SortBy:             request.SortBy,
            SortDirection:      request.SortDirection);

        var result = await sender.Send(query, ct);

        return Ok(new
        {
            data       = result.Items,
            pagination = result.Pagination
        });
    }

    /// <summary>
    /// Returns aggregated portfolio-level statistics.
    /// <c>GET /api/v1/policies/summary</c>
    /// </summary>
    /// <remarks>
    /// This action uses a literal route segment <c>summary</c> which ASP.NET Core
    /// resolves before the parameterised <c>{id}</c> route.
    /// </remarks>
    [HttpGet("summary")]
    [Authorize(Policy = "PolicyRead")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var result = await sender.Send(new GetPolicySummaryQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns a single policy by its surrogate UUID.
    /// <c>GET /api/v1/policies/{id}</c>
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "PolicyRead")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await sender.Send(new GetPolicyByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Bulk-flags policies for underwriter review.
    /// <c>PATCH /api/v1/policies/flag</c>
    /// </summary>
    /// <remarks>
    /// Partial-success semantics (ADR-007): all resolvable IDs are flagged.
    /// IDs not found are reported in <c>notFoundIds</c> rather than returning an error.
    /// Always returns HTTP 200 — inspect the body to determine completeness.
    /// </remarks>
    [HttpPatch("flag")]
    [Authorize(Policy = "PolicyWrite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BulkFlag([FromBody] FlagPoliciesRequest request, CancellationToken ct)
    {
        var command = new FlagPoliciesCommand(request.PolicyIds);
        var result  = await sender.Send(command, ct);
        return Ok(result);
    }
}

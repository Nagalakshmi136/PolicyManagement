namespace PolicyManagement.Application.Common.Models;

/// <summary>
/// Pagination metadata included alongside a page of results.
/// Returned by all list query handlers as part of <see cref="PagedResponse{T}"/>.
/// </summary>
public sealed record PaginationMeta(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage);

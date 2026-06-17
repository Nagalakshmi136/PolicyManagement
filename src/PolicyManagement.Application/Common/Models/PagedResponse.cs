using PolicyManagement.Domain.Common;

namespace PolicyManagement.Application.Common.Models;

/// <summary>
/// Application-layer response envelope for paginated query results.
/// Handlers return this type; the API layer maps it to the HTTP response body.
/// </summary>
/// <typeparam name="T">The DTO item type.</typeparam>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    PaginationMeta Pagination)
{
    /// <summary>
    /// Creates a <see cref="PagedResponse{T}"/> from a domain
    /// <see cref="PagedResult{T}"/> and a pre-mapped item list.
    /// </summary>
    public static PagedResponse<T> From(PagedResult<T> pagedResult)
        => new(
            pagedResult.Items,
            new PaginationMeta(
                pagedResult.Page,
                pagedResult.PageSize,
                pagedResult.TotalCount,
                pagedResult.TotalPages,
                pagedResult.HasNextPage,
                pagedResult.HasPreviousPage));

    /// <summary>
    /// Creates a <see cref="PagedResponse{TDest}"/> by mapping each item in a
    /// domain <see cref="PagedResult{TSource}"/> using the provided selector.
    /// </summary>
    public static PagedResponse<TDest> From<TSource, TDest>(
        PagedResult<TSource> pagedResult,
        Func<TSource, TDest> selector)
        => new(
            pagedResult.Items.Select(selector).ToList().AsReadOnly(),
            new PaginationMeta(
                pagedResult.Page,
                pagedResult.PageSize,
                pagedResult.TotalCount,
                pagedResult.TotalPages,
                pagedResult.HasNextPage,
                pagedResult.HasPreviousPage));
}

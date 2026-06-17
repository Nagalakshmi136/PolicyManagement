namespace PolicyManagement.Domain.Common;

/// <summary>
/// Represents a single page of results from a paginated repository query.
/// </summary>
/// <typeparam name="T">The item type in the page.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

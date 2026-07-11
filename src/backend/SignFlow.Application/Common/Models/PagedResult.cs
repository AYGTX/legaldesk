namespace SignFlow.Application.Common.Models;

public class PagedResult<T>
{
    public List<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => PageNumber < TotalPages;
    public bool HasPreviousPage => PageNumber > 1;

    public static PagedResult<T> Create(IEnumerable<T> items, int totalCount, int pageNumber, int pageSize)
        => new()
        {
            Items = items.ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

    public static PagedResult<T> Empty(int pageNumber, int pageSize)
        => new() { PageNumber = pageNumber, PageSize = pageSize };
}

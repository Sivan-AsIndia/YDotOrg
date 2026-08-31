namespace YDots.DON.Application.Common.Models;

/// <summary>
/// The list envelope from the DTO catalogue: Items, Page, PageSize, TotalCount, TotalPages.
/// Note that the member is Page, not PageIndex — that is what the Donors contract names, and
/// it differs from the IAM envelope on purpose.
/// </summary>
public sealed class PagedResponse<T>
{
    public PagedResponse()
    {
        Items = [];
    }

    public PagedResponse(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
        TotalPages = pageSize <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    public IReadOnlyList<T> Items { get; init; }

    public int TotalCount { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalPages { get; init; }

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;
}

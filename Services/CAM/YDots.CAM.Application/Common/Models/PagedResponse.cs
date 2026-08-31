namespace YDots.CAM.Application.Common.Models;

/// <summary>
/// The list envelope every grid endpoint returns: Items, Page, PageSize, TotalCount,
/// TotalPages. Identical in shape to the IAM and DON envelopes, so one Angular pager component
/// works against all three.
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

    public static PagedResponse<T> Empty(int page = 1, int pageSize = 20) => new([], 0, page, pageSize);
}

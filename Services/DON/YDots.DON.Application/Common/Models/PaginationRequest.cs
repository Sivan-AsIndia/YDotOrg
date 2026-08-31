namespace YDots.DON.Application.Common.Models;

/// <summary>
/// Standard paging input used by every list endpoint. The setter clamps the page size so a
/// caller cannot ask for 100,000 rows and take the database with them.
/// </summary>
public class PaginationRequest
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            <= 0 => 20,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Sort expression, for example "updatedAtUtc desc".</summary>
    public string? Sort { get; set; }

    /// <summary>Page numbers below 1 would produce a negative Skip, so normalise before use.</summary>
    public int Skip => (Page < 1 ? 0 : Page - 1) * PageSize;
}

namespace YDots.CAM.Application.Common.Models;

/// <summary>
/// Standard paging input used by every list endpoint.
///
/// THE SETTER CLAMPS THE PAGE SIZE, which is the whole point of the class. Without it a caller
/// can ask for a hundred thousand rows and take the database with them, and the endpoint that
/// forgets to guard is the one nobody reviews.
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

    /// <summary>Free-text search term applied by the read service.</summary>
    public string? Search { get; set; }

    /// <summary>Page numbers below 1 would produce a negative Skip, so normalise before use.</summary>
    public int Skip => (Page < 1 ? 0 : Page - 1) * PageSize;
}

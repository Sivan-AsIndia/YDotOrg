using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Donors.DTOs;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read side of the Donor resource, exactly as section 6 defines it. Every method takes an
/// <see cref="AccessScope"/> so the scope restriction is part of the signature rather than
/// something a caller might forget.
/// </summary>
public interface IDonorReadService
{
    /// <summary>Scoped projection for grids.</summary>
    Task<PagedResponse<DonorListItemResponse>> SearchAsync(
        DonorSearchFilter query,
        AccessScope scope,
        CancellationToken cancellationToken);

    /// <summary>Purpose-built detail read.</summary>
    Task<DonorDetailResponse?> GetDetailAsync(
        Guid id,
        AccessScope scope,
        CancellationToken cancellationToken);

    /// <summary>Rows for a dropdown or autocomplete, restricted by the same scope.</summary>
    Task<IReadOnlyList<DonorLookupResponse>> LookupAsync(
        string? search,
        int maximumRows,
        AccessScope scope,
        CancellationToken cancellationToken);

    /// <summary>Unpaged rows for the controlled export, capped by the caller.</summary>
    Task<IReadOnlyList<DonorListItemResponse>> ExportRowsAsync(
        DonorSearchFilter query,
        int maximumRows,
        AccessScope scope,
        CancellationToken cancellationToken);
}

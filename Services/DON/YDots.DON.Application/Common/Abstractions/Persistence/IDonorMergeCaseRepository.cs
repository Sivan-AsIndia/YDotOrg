using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>Duplicate review cases behind SCR-DON-004.</summary>
public interface IDonorMergeCaseRepository
{
    Task<PagedResponse<DonorMergeCase>> SearchAsync(
        DuplicateReviewSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default);

    Task<DonorMergeCase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Loads the case together with both candidate donors, for the comparison panel.</summary>
    Task<DonorMergeCase?> GetWithCandidatesAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Open cases that mention a donor. Feeds the Duplicate links panel on Donor 360.</summary>
    Task<IReadOnlyList<DonorMergeCase>> GetForDonorAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<bool> PairExistsAsync(Guid candidateAId, Guid candidateBId, CancellationToken cancellationToken = default);

    Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default);

    void Add(DonorMergeCase mergeCase);
}

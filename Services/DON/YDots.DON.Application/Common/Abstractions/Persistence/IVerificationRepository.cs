using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>Identity verification attempts behind DON-UI-07.</summary>
public interface IVerificationRepository
{
    Task<PagedResponse<DonorIdentityVerification>> SearchAsync(
        VerificationSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default);

    Task<DonorIdentityVerification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The verification a donor is currently going through, if any.</summary>
    Task<DonorIdentityVerification?> GetOpenForDonorAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DonorIdentityVerification>> GetHistoryForDonorAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default);

    void Add(DonorIdentityVerification verification);
}

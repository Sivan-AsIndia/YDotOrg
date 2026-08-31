using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>Follow-up tasks behind DON-UI-08 and the Follow-ups panel on Donor 360.</summary>
public interface IFollowUpRepository
{
    Task<PagedResponse<FollowUpTask>> SearchAsync(
        FollowUpSearchFilter filter,
        AccessScope scope,
        CancellationToken cancellationToken = default);

    Task<FollowUpTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowUpTask>> GetOpenForDonorAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FollowUpTask>> GetOpenForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default);

    void Add(FollowUpTask task);
}

using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>Campaign reference data behind the scope-aware campaign selectors.</summary>
public interface ICampaignRepository
{
    Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> GetActiveAsync(Guid organisationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> SearchAsync(Guid organisationId, string? search, int maximumRows, CancellationToken cancellationToken = default);

    void Add(Campaign campaign);
}

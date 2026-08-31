using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// Consent rows behind SCR-DON-005 and the consent summary on Donor 360. There is no Update
/// method on purpose: consent is append only, so a correction inserts a new row and supersedes
/// the old one.
/// </summary>
public interface IConsentRepository
{
    Task<PagedResponse<Consent>> SearchAsync(ConsentSearchFilter filter, AccessScope scope, CancellationToken cancellationToken = default);

    Task<Consent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The current Active row for one donor and channel, if there is one.</summary>
    Task<Consent?> GetCurrentAsync(Guid donorId, ConsentChannel channel, CancellationToken cancellationToken = default);

    /// <summary>All Active rows for a donor. Feeds the consent status badge and the follow-up channel check.</summary>
    Task<IReadOnlyList<Consent>> GetCurrentForDonorAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Consent>> GetHistoryAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Consent>> GetForLeadAsync(Guid leadId, CancellationToken cancellationToken = default);

    void Add(Consent consent);
}

using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// The three panels on Donor 360 that are read-only projections: donation totals by stage,
/// promises and documents. Grouped in one interface because they are always loaded together
/// for the same screen.
/// </summary>
public interface IDonor360Repository
{
    Task<IReadOnlyList<DonorDonationSummary>> GetDonationSummariesAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DonorPromise>> GetPromisesAsync(Guid donorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents visible to the caller. Confidential rows are filtered out in the query rather
    /// than after it, so an unpermitted caller never receives the row at all.
    /// </summary>
    Task<IReadOnlyList<DonorDocument>> GetDocumentsAsync(Guid donorId, bool includeConfidential, CancellationToken cancellationToken = default);

    /// <summary>Campaigns this donor has been touched by, through the leads that converted to it.</summary>
    Task<IReadOnlyList<(Campaign Campaign, string LeadReference, DateTimeOffset? ConvertedAtUtc)>> GetCampaignHistoryAsync(
        Guid donorId,
        CancellationToken cancellationToken = default);

    void AddPromise(DonorPromise promise);

    void AddDocument(DonorDocument document);

    void AddDonationSummary(DonorDonationSummary summary);
}

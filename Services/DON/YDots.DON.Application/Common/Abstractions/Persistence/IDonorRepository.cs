using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write side of the Donor aggregate. The first three methods are the section 6 contract, word
/// for word; the rest is what the lifecycle commands and the Donor 360 screen need.
/// </summary>
public interface IDonorRepository
{
    // ---- Section 6 contract ------------------------------------------------------------------

    /// <summary>Load the tracked aggregate by identifier.</summary>
    Task<Donor?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stage a new aggregate.</summary>
    Task AddAsync(Donor aggregate, CancellationToken cancellationToken);

    /// <summary>Duplicate and uniqueness check on the normalised natural key.</summary>
    Task<bool> ExistsByBusinessKeyAsync(string normalizedKey, Guid? excludingId, CancellationToken cancellationToken);

    // ---- Supporting operations -----------------------------------------------------------------

    /// <summary>Load the aggregate together with its contacts, consents, interactions and tags.</summary>
    Task<Donor?> GetWithChildrenAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> DonorNumberExistsAsync(string donorNumber, CancellationToken cancellationToken = default);

    /// <summary>Highest sequence used for a year, so the next donor number continues the run.</summary>
    Task<int> GetMaxNumberSequenceAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Candidate matches on e-mail, phone or name. Used by the duplicate check.</summary>
    Task<IReadOnlyList<Donor>> FindDuplicateCandidatesAsync(
        Guid organisationId,
        string? email,
        string? phone,
        string? displayName,
        Guid? excludingId,
        CancellationToken cancellationToken = default);

    void Remove(Donor donor);

    void AddContact(DonorContact contact);

    void RemoveContact(DonorContact contact);

    void AddTag(DonorTag tag);

    void RemoveTag(DonorTag tag);

    void AddInteraction(DonorInteraction interaction);

    Task<IReadOnlyList<DonorContact>> GetContactsAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DonorTag>> GetTagsAsync(Guid donorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DonorInteraction>> GetInteractionsAsync(Guid donorId, int maximumRows, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DonorAuditEvent>> GetActivityHistoryAsync(Guid donorId, int maximumRows, CancellationToken cancellationToken = default);
}

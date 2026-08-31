using YDots.DON.Application.Common.Models;
using YDots.DON.Application.DTOs;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Application.Common.Abstractions.Persistence;

/// <summary>Leads behind SCR-DON-001, SCR-DON-002 and SCR-DON-006.</summary>
public interface ILeadRepository
{
    Task<PagedResponse<Lead>> SearchAsync(LeadSearchFilter filter, AccessScope scope, CancellationToken cancellationToken = default);

    Task<Lead?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Lead?> GetWithAssignmentsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Lead>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<int> GetMaxReferenceSequenceAsync(int year, CancellationToken cancellationToken = default);

    /// <summary>Safe duplicate candidates for the lead capture screen: same e-mail, phone or name.</summary>
    Task<IReadOnlyList<Lead>> FindDuplicateCandidatesAsync(
        Guid organisationId,
        string? email,
        string? mobileNumber,
        string? firstName,
        string? lastName,
        Guid? excludingId,
        CancellationToken cancellationToken = default);

    /// <summary>Open work per owner, used to build the workload band on the assignment board.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetOpenWorkCountsByOwnerAsync(Guid organisationId, CancellationToken cancellationToken = default);

    /// <summary>Owners who already appear on a lead, so the board can offer them without calling IAM.</summary>
    Task<IReadOnlyList<(Guid UserId, string Name, string? TeamCode)>> GetKnownOwnersAsync(Guid organisationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync(Guid organisationId, AccessScope scope, CancellationToken cancellationToken = default);

    void Add(Lead lead);

    void Remove(Lead lead);

    void AddAssignment(LeadAssignment assignment);

    Task<IReadOnlyList<LeadAssignment>> GetAssignmentHistoryAsync(Guid leadId, CancellationToken cancellationToken = default);
}

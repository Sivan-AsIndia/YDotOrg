using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to <see cref="Tenant"/> and its satellites.
///
/// TENANT IS NOT ITSELF TENANT-OWNED, so nothing here is filtered by the ambient Organisation
/// — it IS the Organisation. Reaching these methods is therefore gated by the platform
/// permissions rather than by a query filter, which is why every one of them sits behind a
/// <c>platform.organisations.*</c> permission on the controller.
/// </summary>
public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Tenant?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Load with domains, documents and status history, for the detail screen.</summary>
    Task<Tenant?> GetWithDetailAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// THE ANONYMOUS ENTRY POINT. Resolves a host name to its Organisation before anybody has
    /// a token, by matching <see cref="TenantDomain.HostName"/> exactly.
    ///
    /// Exact match only — no prefix matching, no "closest" match, no fallback to the first
    /// Organisation. A near-miss here would authenticate somebody against the wrong
    /// Organisation, so an unrecognised host resolves to null and the caller gets the
    /// platform sign-in page.
    /// </summary>
    Task<Tenant?> ResolveByHostAsync(string hostName, CancellationToken cancellationToken);

    /// <summary>Is this subdomain free inside the BusinessUnit?</summary>
    Task<bool> SubdomainExistsAsync(string subdomain, Guid businessUnitId, Guid? excludingTenantId, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, Guid businessUnitId, Guid? excludingTenantId, CancellationToken cancellationToken);

    Task<bool> HostNameExistsAsync(string hostName, Guid? excludingTenantId, CancellationToken cancellationToken);

    /// <summary>Next sequential Organisation code, for example TEN004.</summary>
    Task<string> NextTenantCodeAsync(Guid businessUnitId, CancellationToken cancellationToken);

    /// <summary>How many Organisations exist under a BusinessUnit, for the ceiling check.</summary>
    Task<int> CountAsync(Guid businessUnitId, CancellationToken cancellationToken);

    /// <summary>Every Organisation SuperAdmin may select, for the switcher.</summary>
    Task<IReadOnlyList<Tenant>> GetSelectableAsync(Guid businessUnitId, CancellationToken cancellationToken);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    Task AddDomainAsync(TenantDomain domain, CancellationToken cancellationToken);

    Task<TenantDomain?> GetPrimaryDomainAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantDomain>> GetDomainsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<TenantDomain?> GetDomainAsync(Guid domainId, CancellationToken cancellationToken);

    Task AddDocumentAsync(TenantDocument document, CancellationToken cancellationToken);

    Task<TenantDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantDocument>> GetDocumentsAsync(Guid tenantId, CancellationToken cancellationToken);

    // ---- Grouped document submissions -----------------------------------------------------
    //
    // Every one of these ignores the tenant query filter, because SuperAdmin reviews from the
    // platform host with no Organisation selected and would otherwise see an empty list. The
    // Organisation always comes from the route or from the request context, never from a body,
    // so there is nothing for a caller to point somewhere else.

    Task AddSubmissionAsync(TenantDocumentSubmission submission, CancellationToken cancellationToken);

    /// <summary>One submission with its files. Null when it does not exist.</summary>
    Task<TenantDocumentSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken);

    /// <summary>Every submission for one Organisation, newest first, files included.</summary>
    Task<IReadOnlyList<TenantDocumentSubmission>> GetSubmissionsAsync(
        Guid tenantId, CancellationToken cancellationToken);

    /// <summary>How many submissions are waiting on a reviewer, per Organisation.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPendingSubmissionCountsAsync(CancellationToken cancellationToken);

    void RemoveDocument(TenantDocument document);

    /// <summary>Deletes a submission outright. Only ever an unsent draft - see the command.</summary>
    void RemoveSubmission(TenantDocumentSubmission submission);

    /// <summary>
    /// Appends a lifecycle row. Called by every status transition, so the Organisation
    /// timeline is complete by construction rather than by each handler remembering.
    /// </summary>
    Task AddStatusHistoryAsync(TenantStatusHistory history, CancellationToken cancellationToken);

    Task<IReadOnlyList<TenantStatusHistory>> GetStatusHistoryAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Counts by status, for the SuperAdmin dashboard tiles.</summary>
    Task<IReadOnlyDictionary<TenantStatus, int>> GetStatusCountsAsync(Guid businessUnitId, CancellationToken cancellationToken);
}

/// <summary>Write-side access to the root platform entity.</summary>
public interface IBusinessUnitRepository
{
    Task<BusinessUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<BusinessUnit?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// The single BusinessUnit the platform currently runs as. The model supports several,
    /// but everything today assumes one, and this is the accessor that assumption goes
    /// through so introducing a second is a change in one place.
    /// </summary>
    Task<BusinessUnit?> GetDefaultAsync(CancellationToken cancellationToken);

    /// <summary>Resolve by a platform host such as www.ngoplanet.com.</summary>
    Task<BusinessUnit?> ResolveByRootDomainAsync(string hostName, CancellationToken cancellationToken);

    Task<IReadOnlyList<BusinessUnit>> GetAllAsync(CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken);

    Task<bool> RootDomainExistsAsync(string rootDomain, Guid? excludingId, CancellationToken cancellationToken);

    Task AddAsync(BusinessUnit businessUnit, CancellationToken cancellationToken);
}

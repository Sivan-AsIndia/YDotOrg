using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Domain.Entities;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to <see cref="Tenant"/> and its satellites.
///
/// NONE OF THESE READS IS FILTERED BY THE AMBIENT ORGANISATION, and that is correct rather
/// than an oversight: <c>Tenant</c> is the isolation boundary, not something inside it, so
/// filtering it by the current Organisation would leave an Organisation unable to load itself
/// and SuperAdmin unable to see the list they are meant to administer.
///
/// What protects these methods is the platform permission on every endpoint that reaches
/// them — <c>platform.organisations.*</c> — which only SuperAdmin can hold.
/// </summary>
public sealed class TenantRepository(IamDbContext context) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);

    public Task<Tenant?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Tenants.FirstOrDefaultAsync(
            tenant => tenant.Code == code.ToUpperInvariant(), cancellationToken);

    public Task<Tenant?> GetWithDetailAsync(Guid id, CancellationToken cancellationToken) =>
        context.Tenants
            .Include(tenant => tenant.Domains)
            .Include(tenant => tenant.Documents)
            .Include(tenant => tenant.StatusHistory.OrderByDescending(history => history.OccurredAtUtc))
            .Include(tenant => tenant.BusinessUnit)
            .FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);

    /// <summary>
    /// THE ANONYMOUS ENTRY POINT: host name to Organisation.
    ///
    /// Exact match only, and only against a domain row that is both active and verified. No
    /// prefix matching, no closest match, no fallback to "the first Organisation" — a
    /// near-miss here would authenticate somebody against an Organisation they never named,
    /// so an unrecognised host resolves to null and the caller gets the platform sign-in page.
    /// </summary>
    public Task<Tenant?> ResolveByHostAsync(string hostName, CancellationToken cancellationToken)
    {
        var normalised = hostName.Trim().ToLowerInvariant();

        return context.TenantDomains
            .Where(domain => domain.HostName == normalised && domain.IsActive && domain.IsVerified)
            .Select(domain => domain.Tenant)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> SubdomainExistsAsync(
        string subdomain, Guid businessUnitId, Guid? excludingTenantId, CancellationToken cancellationToken) =>
        context.Tenants
            .Where(tenant => tenant.BusinessUnitId == businessUnitId)
            .Where(tenant => excludingTenantId == null || tenant.Id != excludingTenantId)
            .AnyAsync(tenant => tenant.Subdomain == subdomain.ToLowerInvariant(), cancellationToken);

    public Task<bool> CodeExistsAsync(
        string code, Guid businessUnitId, Guid? excludingTenantId, CancellationToken cancellationToken) =>
        context.Tenants
            .Where(tenant => tenant.BusinessUnitId == businessUnitId)
            .Where(tenant => excludingTenantId == null || tenant.Id != excludingTenantId)
            .AnyAsync(tenant => tenant.Code == code.ToUpperInvariant(), cancellationToken);

    public Task<bool> HostNameExistsAsync(
        string hostName, Guid? excludingTenantId, CancellationToken cancellationToken) =>
        context.TenantDomains
            .Where(domain => excludingTenantId == null || domain.TenantId != excludingTenantId)
            .AnyAsync(domain => domain.HostName == hostName.ToLowerInvariant(), cancellationToken);

    /// <summary>The next sequential Organisation code, TEN004.</summary>
    public async Task<string> NextTenantCodeAsync(Guid businessUnitId, CancellationToken cancellationToken)
    {
        var existing = await context.Tenants
            .CountAsync(tenant => tenant.BusinessUnitId == businessUnitId, cancellationToken);

        for (var attempt = 1; attempt <= 50; attempt++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"TEN{existing + attempt:D3}");

            if (!await CodeExistsAsync(candidate, businessUnitId, null, cancellationToken))
            {
                return candidate;
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture, $"TEN-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}");
    }

    public Task<int> CountAsync(Guid businessUnitId, CancellationToken cancellationToken) =>
        context.Tenants.CountAsync(tenant => tenant.BusinessUnitId == businessUnitId, cancellationToken);

    /// <summary>
    /// The Organisations SuperAdmin may enter.
    ///
    /// Archived ones are excluded: there is nothing to operate on inside one, and offering it
    /// in the switcher would only produce an error on selection. Everything else is offered,
    /// INCLUDING those still onboarding — reviewing a submission is precisely why a root user
    /// would enter one that is not yet Active.
    /// </summary>
    public async Task<IReadOnlyList<Tenant>> GetSelectableAsync(
        Guid businessUnitId, CancellationToken cancellationToken) =>
        await context.Tenants
            .Where(tenant => tenant.BusinessUnitId == businessUnitId)
            .Where(tenant => tenant.Status != TenantStatus.Archived)
            .OrderBy(tenant => tenant.Name)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken) =>
        await context.Tenants.AddAsync(tenant, cancellationToken);

    public async Task AddDomainAsync(TenantDomain domain, CancellationToken cancellationToken) =>
        await context.TenantDomains.AddAsync(domain, cancellationToken);

    public Task<TenantDomain?> GetPrimaryDomainAsync(Guid tenantId, CancellationToken cancellationToken) =>
        context.TenantDomains.FirstOrDefaultAsync(
            domain => domain.TenantId == tenantId && domain.IsPrimary, cancellationToken);

    public async Task<IReadOnlyList<TenantDomain>> GetDomainsAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.TenantDomains
            .Where(domain => domain.TenantId == tenantId)
            .OrderByDescending(domain => domain.IsPrimary)
            .ThenBy(domain => domain.HostName)
            .ToListAsync(cancellationToken);

    public Task<TenantDomain?> GetDomainAsync(Guid domainId, CancellationToken cancellationToken) =>
        context.TenantDomains.FirstOrDefaultAsync(domain => domain.Id == domainId, cancellationToken);

    public async Task AddDocumentAsync(TenantDocument document, CancellationToken cancellationToken) =>
        await context.TenantDocuments.AddAsync(document, cancellationToken);

    /// <summary>
    /// One document.
    ///
    /// Filters bypassed because SuperAdmin reviews documents belonging to an Organisation they
    /// have NOT selected into — that is the whole review queue. Authorisation is the platform
    /// permission on the endpoint.
    /// </summary>
    public Task<TenantDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        context.TenantDocuments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(document => document.Id == documentId, cancellationToken);

    public async Task<IReadOnlyList<TenantDocument>> GetDocumentsAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.TenantDocuments
            .IgnoreQueryFilters()
            .Where(document => document.TenantId == tenantId)
            .OrderBy(document => document.DocumentType)
            .ThenByDescending(document => document.UploadedAtUtc)
            .ToListAsync(cancellationToken);

    // ---- Grouped document submissions -----------------------------------------------------

    public async Task AddSubmissionAsync(
        TenantDocumentSubmission submission, CancellationToken cancellationToken) =>
        await context.TenantDocumentSubmissions.AddAsync(submission, cancellationToken);

    /// <summary>
    /// One submission with its files.
    ///
    /// IgnoreQueryFilters for the reason set out on the interface: a reviewer works from the
    /// platform host with no Organisation selected, so the automatic tenant filter would match
    /// nothing and every submission would look empty. The id comes from the route, which is
    /// reachable only with the platform review permission.
    /// </summary>
    public async Task<TenantDocumentSubmission?> GetSubmissionAsync(
        Guid submissionId, CancellationToken cancellationToken) =>
        await context.TenantDocumentSubmissions
            .IgnoreQueryFilters()
            .Include(submission => submission.Documents)
            .FirstOrDefaultAsync(submission => submission.Id == submissionId, cancellationToken);

    public async Task<IReadOnlyList<TenantDocumentSubmission>> GetSubmissionsAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.TenantDocumentSubmissions
            .IgnoreQueryFilters()
            .Include(submission => submission.Documents)
            .Where(submission => submission.TenantId == tenantId)
            .OrderByDescending(submission => submission.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Pending counts per Organisation, for the badge on the review queue.
    ///
    /// Grouped in the database rather than by loading every submission and counting in memory:
    /// the queue screen needs one number per Organisation, not the rows behind it.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetPendingSubmissionCountsAsync(
        CancellationToken cancellationToken) =>
        await context.TenantDocumentSubmissions
            .IgnoreQueryFilters()
            .Where(submission => submission.Status == TenantDocumentSubmissionStatus.Submitted
                                 || submission.Status == TenantDocumentSubmissionStatus.UnderReview)
            .GroupBy(submission => submission.TenantId)
            .Select(group => new { TenantId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.TenantId, row => row.Count, cancellationToken);

    public void RemoveDocument(TenantDocument document) => context.TenantDocuments.Remove(document);

    public void RemoveSubmission(TenantDocumentSubmission submission) =>
        context.TenantDocumentSubmissions.Remove(submission);

    public async Task AddStatusHistoryAsync(TenantStatusHistory history, CancellationToken cancellationToken) =>
        await context.TenantStatusHistory.AddAsync(history, cancellationToken);

    public async Task<IReadOnlyList<TenantStatusHistory>> GetStatusHistoryAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.TenantStatusHistory
            .Where(history => history.TenantId == tenantId)
            .OrderByDescending(history => history.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<TenantStatus, int>> GetStatusCountsAsync(
        Guid businessUnitId, CancellationToken cancellationToken)
    {
        var counts = await context.Tenants
            .Where(tenant => tenant.BusinessUnitId == businessUnitId)
            .GroupBy(tenant => tenant.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(item => item.Status, item => item.Count);
    }
}

/// <summary>Write-side access to the root platform entity.</summary>
public sealed class BusinessUnitRepository(IamDbContext context) : IBusinessUnitRepository
{
    public Task<BusinessUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.BusinessUnits.FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    public Task<BusinessUnit?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.BusinessUnits.FirstOrDefaultAsync(
            unit => unit.Code == code.ToUpperInvariant(), cancellationToken);

    /// <summary>
    /// The BusinessUnit the platform runs as.
    ///
    /// Ordered by creation so the answer is stable rather than whatever the database returns
    /// first. The model supports several BusinessUnits; everything today assumes one, and this
    /// accessor is where that assumption lives so introducing a second is a change in one place.
    /// </summary>
    public Task<BusinessUnit?> GetDefaultAsync(CancellationToken cancellationToken) =>
        context.BusinessUnits
            .OrderBy(unit => unit.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<BusinessUnit?> ResolveByRootDomainAsync(string hostName, CancellationToken cancellationToken)
    {
        var normalised = hostName.Trim().ToLowerInvariant();

        return context.BusinessUnits.FirstOrDefaultAsync(
            unit => unit.RootDomain == normalised, cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessUnit>> GetAllAsync(CancellationToken cancellationToken) =>
        await context.BusinessUnits.OrderBy(unit => unit.Name).ToListAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(string code, Guid? excludingId, CancellationToken cancellationToken) =>
        context.BusinessUnits
            .Where(unit => excludingId == null || unit.Id != excludingId)
            .AnyAsync(unit => unit.Code == code.ToUpperInvariant(), cancellationToken);

    public Task<bool> RootDomainExistsAsync(
        string rootDomain, Guid? excludingId, CancellationToken cancellationToken) =>
        context.BusinessUnits
            .Where(unit => excludingId == null || unit.Id != excludingId)
            .AnyAsync(unit => unit.RootDomain == rootDomain.ToLowerInvariant(), cancellationToken);

    public async Task AddAsync(BusinessUnit businessUnit, CancellationToken cancellationToken) =>
        await context.BusinessUnits.AddAsync(businessUnit, cancellationToken);
}

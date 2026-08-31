using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Organisations.DTOs;
using YDot.IAM.Application.Features.Organisations.Mappings;
using YDot.IAM.Domain.Enums;
using YDot.IAM.Infrastructure.Persistence;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// Read side for the Organisation directory and detail screens.
///
/// NONE OF THIS IS ORGANISATION-FILTERED, and that is the point: <c>Tenant</c> is the
/// isolation boundary rather than something inside it, and the directory exists precisely to
/// list every Organisation. What guards these methods is the platform permission on the
/// endpoints that reach them, which only SuperAdmin can hold.
///
/// <see cref="GetCurrentAsync"/> is the exception. It resolves the Organisation from the
/// REQUEST CONTEXT rather than from an id, so a TenantAdmin can read their own profile and
/// has no way to name a different one.
/// </summary>
public sealed class OrganisationReadService(
    IamDbContext context,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IOrganisationReadService
{
    public async Task<PagedResponse<OrganisationListItemResponse>> SearchAsync(
        TenantSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = context.Tenants.AsNoTracking();

        if (filter.BusinessUnitId.HasValue)
        {
            query = query.Where(tenant => tenant.BusinessUnitId == filter.BusinessUnitId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(tenant =>
                tenant.Name.ToLower().Contains(term)
                || tenant.Code.ToLower().Contains(term)
                || tenant.Subdomain.ToLower().Contains(term)
                || (tenant.LegalName != null && tenant.LegalName.ToLower().Contains(term))
                || (tenant.RegistrationNumber != null && tenant.RegistrationNumber.ToLower().Contains(term)));
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(tenant => tenant.Status == filter.Status.Value);
        }

        // The review queue: everything sitting on SuperAdmin desk.
        if (filter.AwaitingReview == true)
        {
            query = query.Where(tenant =>
                tenant.Status == TenantStatus.Submitted
                || tenant.Status == TenantStatus.Resubmitted
                || tenant.Status == TenantStatus.UnderReview);
        }

        if (!string.IsNullOrWhiteSpace(filter.Country))
        {
            query = query.Where(tenant => tenant.Country == filter.Country);
        }

        if (!string.IsNullOrWhiteSpace(filter.OrganisationType))
        {
            query = query.Where(tenant => tenant.OrganisationType == filter.OrganisationType);
        }

        if (filter.CreatedFromUtc.HasValue)
        {
            query = query.Where(tenant => tenant.CreatedAtUtc >= filter.CreatedFromUtc.Value);
        }

        if (filter.CreatedToUtc.HasValue)
        {
            query = query.Where(tenant => tenant.CreatedAtUtc <= filter.CreatedToUtc.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .Select(tenant => new
            {
                tenant.Id,
                tenant.Code,
                tenant.Name,
                tenant.Subdomain,
                RootDomain = tenant.BusinessUnit!.RootDomain,
                tenant.Status,
                tenant.LogoUrl,
                tenant.Country,
                tenant.CreatedAtUtc,
                tenant.UpdatedAtUtc,
                tenant.Version,

                // Counted in the projection rather than with a second round trip per row,
                // which is what a directory of two hundred Organisations would otherwise cost.
                UserCount = context.Users.IgnoreQueryFilters()
                    .Count(user => user.TenantId == tenant.Id),

                AdminEmail = context.Users.IgnoreQueryFilters()
                    .Where(user => user.TenantId == tenant.Id && user.IsTenantAdmin)
                    .Select(user => user.Email)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new OrganisationListItemResponse(
                row.Id,
                row.Code,
                row.Name,
                row.Subdomain,
                $"{row.Subdomain}.{row.RootDomain}",
                row.Status,
                OrganisationMappingConfig.DescribeStatus(row.Status),
                row.LogoUrl,
                row.Country,
                row.UserCount,
                row.AdminEmail,
                row.CreatedAtUtc,
                row.UpdatedAtUtc,
                row.Status is TenantStatus.Submitted or TenantStatus.Resubmitted or TenantStatus.UnderReview,
                row.Version))
            .ToList();

        return new PagedResponse<OrganisationListItemResponse>(items, total, filter.Page, filter.PageSize);
    }

    /// <summary>
    /// One Organisation, read from the platform side.
    ///
    /// THE REVIEWER'S INTERNAL NOTES ARE INCLUDED, because this route is reachable only with the
    /// platform TenantsView permission - a reviewer reading their own working notes about a
    /// submission they are deciding.
    /// </summary>
    public Task<OrganisationDetailResponse?> GetDetailAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        BuildDetailAsync(tenantId, includeInternalNotes: true, cancellationToken);

    /// <summary>
    /// The caller own Organisation.
    ///
    /// Resolved from the request context, so a Tenant user cannot ask for a different one by
    /// changing an id in the URL — there is no id to change.
    /// </summary>
    /// <remarks>
    /// THE REVIEWER'S INTERNAL NOTES ARE WITHHELD on this path. The two reads returned the same
    /// response type and therefore the same fields, so the split between them controlled only
    /// WHICH Organisation you could see and not which of its fields came back - and the note a
    /// reviewer wrote about an organisation was delivered to that organisation.
    /// </remarks>
    public Task<OrganisationDetailResponse?> GetCurrentAsync(CancellationToken cancellationToken) =>
        tenantContext.TenantId.HasValue
            ? BuildDetailAsync(tenantContext.TenantId.Value, includeInternalNotes: false, cancellationToken)
            : Task.FromResult<OrganisationDetailResponse?>(null);

    public async Task<OrganisationStatisticsResponse> GetStatisticsAsync(
        Guid businessUnitId, CancellationToken cancellationToken)
    {
        var byStatus = await context.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.BusinessUnitId == businessUnitId)
            .GroupBy(tenant => tenant.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(params TenantStatus[] statuses) =>
            byStatus.Where(item => statuses.Contains(item.Status)).Sum(item => item.Count);

        return new OrganisationStatisticsResponse(
            byStatus.Sum(item => item.Count),
            CountOf(TenantStatus.Active),
            CountOf(TenantStatus.Submitted, TenantStatus.Resubmitted, TenantStatus.UnderReview),
            CountOf(TenantStatus.Invited, TenantStatus.InvitationAccepted, TenantStatus.ProfileIncomplete),
            CountOf(TenantStatus.Suspended),
            CountOf(TenantStatus.Archived),
            CountOf(TenantStatus.Rejected),
            byStatus.ToDictionary(
                item => item.Status.ToString(), item => item.Count, StringComparer.Ordinal));
    }

    public async Task<IReadOnlyList<OrganisationListItemResponse>> GetAwaitingReviewAsync(
        Guid businessUnitId, CancellationToken cancellationToken)
    {
        var filter = new TenantSearchFilter
        {
            BusinessUnitId = businessUnitId,
            AwaitingReview = true,
            PageSize = 100,
            // Oldest first: the one that has been waiting longest is the one to look at.
            Sort = "submittedatutc asc"
        };

        var page = await SearchAsync(filter, cancellationToken);

        return page.Items;
    }

    private async Task<OrganisationDetailResponse?> BuildDetailAsync(
        Guid tenantId, bool includeInternalNotes, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // ---- IgnoreQueryFilters is REQUIRED here, and the reason is worth understanding ----
        //
        // Most tables in this database carry an automatic "WHERE tenant_id = the current one"
        // that EF adds to every query without being asked. That is the isolation boundary, and
        // it is why one Organisation's records simply cannot appear in another's screens.
        //
        // WHEN NO ORGANISATION IS RESOLVED, THAT FILTER MATCHES NOTHING - not everything. A
        // SuperAdmin reviewing a submission from the platform host has not entered an
        // Organisation, so their current Organisation is null, so every filtered row is
        // excluded.
        //
        // Documents were the casualty. TenantDocument is tenant-owned and therefore filtered;
        // Tenant, TenantDomain and TenantStatusHistory are BusinessUnit-owned and are not. So
        // the Registration Review screen rendered the profile, the web addresses and the full
        // history perfectly, and then said "Nothing has been uploaded" beneath them - which is
        // the one thing a reviewer is actually there to look at.
        //
        // IS IGNORING THE FILTER SAFE HERE? Yes, because of where tenantId comes from in both
        // callers, and that is the only thing that makes it safe:
        //
        //   GetDetailAsync   an id from the URL, reachable only with the platform
        //                    TenantsView permission - which is SuperAdmin's alone
        //   GetCurrentAsync  no id at all; it is taken from the request context, so a Tenant
        //                    user has nothing to tamper with
        //
        // TenantRepository.GetDocumentsAsync already does exactly this, for exactly this
        // reason. Never add IgnoreQueryFilters to a query whose id a Tenant user can choose.
        var tenant = await context.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(item => item.BusinessUnit)
            .Include(item => item.Domains)
            .Include(item => item.Documents)
            .Include(item => item.StatusHistory)
            .FirstOrDefaultAsync(item => item.Id == tenantId, cancellationToken);

        if (tenant?.BusinessUnit is null)
        {
            return null;
        }

        var userCount = await context.Users
            .IgnoreQueryFilters()
            .CountAsync(user => user.TenantId == tenantId, cancellationToken);

        var admin = await context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenantId && user.IsTenantAdmin)
            .OrderBy(user => user.CreatedAtUtc)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.Status,
                user.LastLoginAtUtc,
                HasPassword = user.PasswordHash != null,
                InvitationExpiresAtUtc = context.UserInvitations.IgnoreQueryFilters()
                    .Where(invitation => invitation.UserId == user.Id)
                    .Where(invitation => invitation.Status == InvitationStatus.Pending
                                         || invitation.Status == InvitationStatus.Resent)
                    .Select(invitation => (DateTimeOffset?)invitation.ExpiresAtUtc)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        var primaryAdmin = admin is null
            ? null
            : new OrganisationAdminResponse(
                admin.Id,
                admin.DisplayName,
                admin.Email ?? string.Empty,
                admin.Status,
                // "Activated" means they actually set a password, not merely that the row exists.
                admin.HasPassword,
                admin.LastLoginAtUtc,
                admin.InvitationExpiresAtUtc,
                admin.InvitationExpiresAtUtc.HasValue);

        return tenant.ToDetailResponse(
            tenant.BusinessUnit,
            userCount,
            primaryAdmin,
            [.. tenant.Domains.OrderByDescending(domain => domain.IsPrimary).ThenBy(domain => domain.HostName)],
            [.. tenant.Documents.OrderBy(document => document.DocumentType)
                .ThenByDescending(document => document.UploadedAtUtc)],
            [.. tenant.StatusHistory.OrderByDescending(history => history.OccurredAtUtc)],
            now,
            includeInternalNotes);
    }

    /// <summary>Sorting from a closed set, for the same reason as the user directory.</summary>
    private static IQueryable<Domain.Entities.Tenant> ApplySort(
        IQueryable<Domain.Entities.Tenant> query, string? sort) =>
        (sort?.Trim().ToLowerInvariant()) switch
        {
            "name" or "name asc" => query.OrderBy(tenant => tenant.Name),
            "name desc" => query.OrderByDescending(tenant => tenant.Name),
            "code" or "code asc" => query.OrderBy(tenant => tenant.Code),
            "code desc" => query.OrderByDescending(tenant => tenant.Code),
            "status" => query.OrderBy(tenant => tenant.Status).ThenBy(tenant => tenant.Name),
            "submittedatutc asc" => query.OrderBy(tenant => tenant.SubmittedAtUtc ?? tenant.CreatedAtUtc),
            "submittedatutc" => query.OrderByDescending(tenant => tenant.SubmittedAtUtc ?? tenant.CreatedAtUtc),
            "createdatutc asc" => query.OrderBy(tenant => tenant.CreatedAtUtc),
            "createdatutc" => query.OrderByDescending(tenant => tenant.CreatedAtUtc),
            _ => query.OrderByDescending(tenant => tenant.UpdatedAtUtc ?? tenant.CreatedAtUtc)
        };
}

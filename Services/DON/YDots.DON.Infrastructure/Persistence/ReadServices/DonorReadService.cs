using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Application.Features.Donors.Mappings;
using YDots.DON.Domain.Entities;

namespace YDots.DON.Infrastructure.Persistence.ReadServices;

/// <summary>
/// EF Core implementation of the Donor read side.
///
/// Every query here starts from <see cref="ApplyScope"/>. That is deliberate: it is a single
/// place where the organisation boundary and the own-records restriction are applied, and it
/// runs before any filter the caller supplied, so no combination of query-string values can
/// widen what they see.
/// </summary>
public sealed class DonorReadService(DonDbContext context, ICurrentUser currentUser) : IDonorReadService
{
    public async Task<PagedResponse<DonorListItemResponse>> SearchAsync(
        DonorSearchFilter query,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        var donors = BuildQuery(query, scope);

        var total = await donors.CountAsync(cancellationToken);

        var items = await donors
            .OrderByDescending(donor => donor.UpdatedAtUtc ?? donor.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var rows = items.Select(donor => donor.ToListItemResponse()).ToList();

        return new PagedResponse<DonorListItemResponse>(rows, total, query.Page, query.PageSize);
    }

    public async Task<DonorDetailResponse?> GetDetailAsync(
        Guid id,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        var donor = await ApplyScope(context.Donors.AsNoTracking(), scope)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return donor?.ToDetailResponse(
            currentUser.CanSeeContact(),
            DonorMappingConfig.PermittedActionsFor(donor));
    }

    public async Task<IReadOnlyList<DonorLookupResponse>> LookupAsync(
        string? search,
        int maximumRows,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        var donors = ApplyScope(context.Donors.AsNoTracking(), scope);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            donors = donors.Where(donor =>
                donor.DonorNumber.ToLower().Contains(term)
                || (donor.FirstName != null && donor.FirstName.ToLower().Contains(term))
                || (donor.LastName != null && donor.LastName.ToLower().Contains(term))
                || (donor.OrganisationName != null && donor.OrganisationName.ToLower().Contains(term)));
        }

        var items = await donors
            .OrderBy(donor => donor.DonorNumber)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);

        return [.. items.Select(donor => donor.ToLookupResponse())];
    }

    public async Task<IReadOnlyList<DonorListItemResponse>> ExportRowsAsync(
        DonorSearchFilter query,
        int maximumRows,
        AccessScope scope,
        CancellationToken cancellationToken)
    {
        var items = await BuildQuery(query, scope)
            .OrderByDescending(donor => donor.UpdatedAtUtc ?? donor.CreatedAtUtc)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);

        return [.. items.Select(donor => donor.ToListItemResponse())];
    }

    private IQueryable<Donor> BuildQuery(DonorSearchFilter query, AccessScope scope)
    {
        var donors = ApplyScope(context.Donors.AsNoTracking(), scope);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();

            donors = donors.Where(donor =>
                donor.DonorNumber.ToLower().Contains(term)
                || (donor.FirstName != null && donor.FirstName.ToLower().Contains(term))
                || (donor.LastName != null && donor.LastName.ToLower().Contains(term))
                || (donor.OrganisationName != null && donor.OrganisationName.ToLower().Contains(term))
                || (donor.PrimaryEmail != null && donor.PrimaryEmail.ToLower().Contains(term))
                || (donor.PrimaryPhone != null && donor.PrimaryPhone.Contains(term)));
        }

        if (query.DonorType is not null)
        {
            donors = donors.Where(donor => donor.DonorType == query.DonorType);
        }

        if (query.Status is not null)
        {
            donors = donors.Where(donor => donor.Status == query.Status);
        }

        if (query.ApprovalState is not null)
        {
            donors = donors.Where(donor => donor.ApprovalState == query.ApprovalState);
        }

        if (!string.IsNullOrWhiteSpace(query.PreferredLanguage))
        {
            donors = donors.Where(donor => donor.PreferredLanguage == query.PreferredLanguage);
        }

        if (query.DoNotContact is not null)
        {
            donors = donors.Where(donor => donor.DoNotContact == query.DoNotContact);
        }

        if (query.RelationshipOwnerUserId is not null)
        {
            donors = donors.Where(donor => donor.RelationshipOwnerUserId == query.RelationshipOwnerUserId);
        }

        if (!string.IsNullOrWhiteSpace(query.TagCode))
        {
            var tagCode = query.TagCode.Trim().ToUpperInvariant();
            donors = donors.Where(donor => donor.Tags.Any(tag => tag.Code == tagCode));
        }

        if (query.UpdatedAfterUtc is not null)
        {
            donors = donors.Where(donor => (donor.UpdatedAtUtc ?? donor.CreatedAtUtc) >= query.UpdatedAfterUtc);
        }

        if (query.UpdatedBeforeUtc is not null)
        {
            donors = donors.Where(donor => (donor.UpdatedAtUtc ?? donor.CreatedAtUtc) <= query.UpdatedBeforeUtc);
        }

        return donors;
    }

    /// <summary>
    /// The scope gate. Organisation always; then, for a caller who carries only narrowing
    /// scopes, the records they own or the exact records their token named. Nothing in this
    /// class queries Donors without going through here.
    /// </summary>
    private static IQueryable<Donor> ApplyScope(IQueryable<Donor> donors, AccessScope scope)
    {
        donors = donors.Where(donor => donor.OrganisationId == scope.OrganisationId);

        if (scope.IsOrganisationWide)
        {
            return donors;
        }

        // An explicit-record scope names the exact rows, so it replaces the ownership test.
        var explicitRecordIds = scope.ExplicitRecordIds;

        return explicitRecordIds.Count > 0
            ? donors.Where(donor => explicitRecordIds.Contains(donor.Id))
            : donors.Where(donor => donor.RelationshipOwnerUserId == scope.UserId);
    }
}

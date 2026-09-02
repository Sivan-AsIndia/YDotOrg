using Microsoft.EntityFrameworkCore;
using YDots.DON.Application.Common.Abstractions.Persistence;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Services;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Application.Features.Donors.Mappings;
using YDots.DON.Domain.Entities;
using YDots.DON.Domain.Enums;

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

        var rows = await BuildRowsAsync(items, cancellationToken);

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

        return await BuildRowsAsync(items, cancellationToken);
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

    /// <summary>
    /// Fills a page of grid rows with the facts that live in other tables.
    ///
    /// FOUR QUERIES FOR THE WHOLE PAGE, NOT FOUR PER ROW. Giving totals, the next follow-up, the
    /// consent state and the identity verification each live in their own table; fetching them
    /// row by row would be forty queries for a ten-row page. Each is fetched once for every donor
    /// on the page and then matched in memory.
    /// </summary>
    private async Task<List<DonorListItemResponse>> BuildRowsAsync(
        List<Donor> donors,
        CancellationToken cancellationToken)
    {
        var canSeeContact = currentUser.CanSeeContact();

        if (donors.Count == 0)
        {
            return [];
        }

        var donorIds = donors.Select(donor => donor.Id).ToList();

        // RECEIVED ONLY. Pledged money has not arrived, and a "lifetime giving" figure that
        // included it would overstate what the charity actually has.
        var summaries = await context.DonorDonationSummaries
            .AsNoTracking()
            .Where(summary => donorIds.Contains(summary.DonorId)
                && summary.Stage == DonationStage.Received)
            .ToListAsync(cancellationToken);

        var followUps = await context.FollowUpTasks
            .AsNoTracking()
            .Where(task => task.DonorId != null
                && donorIds.Contains(task.DonorId.Value)
                && (task.Status == FollowUpStatus.Planned
                    || task.Status == FollowUpStatus.Assigned
                    || task.Status == FollowUpStatus.Rescheduled))
            .ToListAsync(cancellationToken);

        var consents = await context.Consents
            .AsNoTracking()
            .Where(consent => consent.DonorId != null && donorIds.Contains(consent.DonorId.Value))
            .ToListAsync(cancellationToken);

        var verifications = await context.DonorIdentityVerifications
            .AsNoTracking()
            .Where(verification => donorIds.Contains(verification.DonorId))
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var today = now.Date;

        return donors
            .Select(donor =>
            {
                var received = summaries.Where(summary => summary.DonorId == donor.Id).ToList();
                var lifetime = received.Sum(summary => summary.TotalAmount);
                var currency = received.FirstOrDefault()?.Currency ?? "INR";
                var lastReceived = received
                    .OrderByDescending(summary => summary.AsAtUtc)
                    .FirstOrDefault();

                var nextFollowUp = followUps
                    .Where(task => task.DonorId == donor.Id && task.DueAtUtc != null)
                    .OrderBy(task => task.DueAtUtc)
                    .FirstOrDefault();

                var verification = verifications
                    .Where(candidate => candidate.DonorId == donor.Id)
                    .OrderByDescending(candidate => candidate.CreatedAtUtc)
                    .FirstOrDefault();

                var donorConsents = consents.Where(consent => consent.DonorId == donor.Id).ToList();

                return donor.ToListItemResponse(
                    canSeeContact,
                    campaignName: null,
                    lastDonationAmount: lastReceived?.TotalAmount,
                    lastDonationAtUtc: lastReceived?.AsAtUtc,
                    lifetimeGiving: lifetime,
                    currency: currency,
                    followUpStatus: DescribeFollowUp(nextFollowUp?.DueAtUtc, today),
                    verificationStatus: verification?.Status.ToString() ?? "Pending",
                    consentStatus: DescribeConsent(donorConsents),

                    // SOMETHING FOR A PERSON TO LOOK AT: a consent that has expired, or one that
                    // was withdrawn. Both mean the permitted channels have changed.
                    consentReviewRequired: donorConsents.Any(consent =>
                        consent.ConsentState == ConsentState.Withdrawn
                        || (consent.ExpiryAtUtc != null && consent.ExpiryAtUtc <= now)));
            })
            .ToList();
    }

    /// <summary>
    /// Overdue / Due Today / Tomorrow / None, recomputed on read.
    ///
    /// NEVER STORED, because overdue happens as time passes rather than because somebody saved
    /// the record - a stored value would be wrong for most of any given day.
    /// </summary>
    private static string DescribeFollowUp(DateTimeOffset? dueAtUtc, DateTime today)
    {
        if (dueAtUtc is null)
        {
            return "None";
        }

        var due = dueAtUtc.Value.Date;

        if (due < today) return "Overdue";
        if (due == today) return "Due Today";
        if (due == today.AddDays(1)) return "Tomorrow";
        return "None";
    }

    /// <summary>
    /// The donor's overall consent position.
    ///
    /// PARTIAL IS THE INTERESTING ONE. A donor who has granted e-mail and withdrawn SMS is
    /// neither fully contactable nor fully off-limits, and collapsing that to "Granted" is how
    /// somebody ends up texting a person who asked them not to.
    /// </summary>
    private static string DescribeConsent(IReadOnlyCollection<Consent> consents)
    {
        if (consents.Count == 0)
        {
            return "Not provided";
        }

        var granted = consents.Count(consent => consent.ConsentState == ConsentState.Granted);

        if (granted == 0) return "Withdrawn";
        return granted == consents.Count ? "Granted" : "Partial";
    }
}

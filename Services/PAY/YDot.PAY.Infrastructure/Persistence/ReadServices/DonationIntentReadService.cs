using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Mappings;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.ReadServices;

/// <summary>
/// The read side of donation intents.
///
/// IT MATERIALISES ENTITIES RATHER THAN PROJECTING STRAIGHT TO DTOs, which is worth explaining
/// because the usual advice is the opposite. The response shapes depend on computed members -
/// <c>IsPayable</c>, <c>IsTerminal</c>, <c>NeedsVerification</c>, <c>IsLinkExpiredAt</c> - that
/// live on the entity and encode rules the module is not free to restate differently here. A
/// hand-written projection would have to duplicate all four in LINQ, and the day one of them
/// changed the grid and the detail screen would start disagreeing about whether a donation could
/// still be paid.
///
/// The pages are small and capped, so the cost of that is a few extra columns per row.
/// </summary>
public sealed class DonationIntentReadService(
    PaymentDbContext context,
    ICurrentUser currentUser,
    ICampaignDirectory campaigns,
    IDateTimeProvider clock,
    IOptions<PaymentSettings> paymentSettings)
    : IDonationIntentReadService
{
    private readonly PaymentSettings _settings = paymentSettings.Value;

    public async Task<PagedResponse<DonationIntentListItemResponse>> SearchAsync(
        DonationIntentSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(scope);

        var query = ApplyFilter(context.DonationIntents.AsNoTracking(), filter, scope);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var campaignNames = await ResolveCampaignNamesAsync(
            scope.TenantId, rows.Select(intent => intent.CampaignId), cancellationToken);

        var items = rows
            .Select(intent => intent.ToListItemResponse(
                LookupCampaign(campaignNames, intent.CampaignId), canSeeSensitiveDonor))
            .ToList();

        return new PagedResponse<DonationIntentListItemResponse>(
            items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<DonationIntentDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var intent = await DetailQuery()
            .Where(candidate => candidate.Id == id)
            .Where(candidate => !scope.IsOwnRecordsOnly || candidate.CreatedByUserId == scope.UserId)

            // A DONOR READS THEIR OWN DONATION AND NO OTHER. Fetching by id would otherwise let
            // anybody holding pay.intents.view open any intent in the Organisation by walking
            // identifiers - the one hole a list filter alone would leave open.
            .Where(candidate => !scope.IsDonorSelfService
                                || (scope.HasDonorIdentity
                                    && candidate.NormalisedEmail == scope.DonorEmail))
            .FirstOrDefaultAsync(cancellationToken);

        if (intent is null)
        {
            return null;
        }

        var campaignName = intent.CampaignId.HasValue
            ? await campaigns.GetCampaignNameAsync(intent.TenantId, intent.CampaignId.Value, cancellationToken)
            : null;

        return BuildDetail(
            intent,
            campaignName,
            canSeeSensitiveDonor,
            DonationMappingConfig.PermittedActionsFor(
                intent, currentUser.HasPermission, clock.UtcNow, _settings.MaximumAttemptsBeforeSupport));
    }

    /// <summary>
    /// One intent by its public reference, for the DONOR-FACING result page.
    ///
    /// THREE THINGS DIFFER FROM THE STAFF READ, all for the same reason - the caller has no
    /// session:
    ///
    ///   * The Organisation filter is bypassed. There is no Organisation to filter by; the
    ///     reference is what resolves one, and it is unguessable and unique platform-wide.
    ///   * The donor details are ALWAYS masked. Not "masked unless permitted" - there is nobody
    ///     to hold a permission, so the safe branch is the only branch.
    ///   * The permitted actions are computed with a permission probe that answers false to
    ///     everything, so a donor is offered Pay and Retry from the intent's own state and never
    ///     a staff action.
    /// </summary>
    public async Task<DonationIntentDetailResponse?> GetDetailByReferenceAsync(
        string intentReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(intentReference))
        {
            return null;
        }

        var intent = await DetailQuery()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.IntentReference == intentReference, cancellationToken);

        if (intent is null)
        {
            return null;
        }

        var campaignName = intent.CampaignId.HasValue
            ? await campaigns.GetCampaignNameAsync(intent.TenantId, intent.CampaignId.Value, cancellationToken)
            : null;

        return BuildDetail(
            intent,
            campaignName,
            canSeeSensitiveDonor: false,
            DonationMappingConfig.PermittedActionsFor(
                intent,
                _ => false,
                clock.UtcNow,
                _settings.MaximumAttemptsBeforeSupport));
    }

    /// <summary>
    /// Section 23: the queue Payment Support works from.
    ///
    /// "NEEDS A PERSON" IS NARROWER THAN "FAILED". An intent that failed once and was then paid
    /// needs nobody. What lands here is an intent that is still unpaid AND has either exhausted
    /// the retry allowance or has an attempt whose outcome is unknown - the second being the more
    /// urgent, because unknown means the donor may already have been charged.
    ///
    /// ORDERED BY WHAT NEEDS VERIFYING FIRST, then oldest, because an unverified attempt is money
    /// that may have moved without the books knowing.
    /// </summary>
    public async Task<PagedResponse<PaymentSupportCaseResponse>> GetSupportQueueAsync(
        PaginationRequest pagination,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pagination);
        ArgumentNullException.ThrowIfNull(scope);

        var query = context.DonationIntents
            .AsNoTracking()
            .Include(intent => intent.Attempts)
            .Where(intent => intent.Status == DonationIntentStatus.Failed
                             || intent.Status == DonationIntentStatus.PaymentInProgress)
            .Where(intent => intent.Attempts.Count >= _settings.MaximumAttemptsBeforeSupport
                             || intent.Attempts.Any(attempt =>
                                 attempt.Status == PaymentAttemptStatus.TimedOut
                                 || attempt.Status == PaymentAttemptStatus.Pending));

        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(intent => intent.CreatedByUserId == scope.UserId);
        }

        query = ApplyDonorScope(query, scope);

        if (!string.IsNullOrWhiteSpace(pagination.Search))
        {
            var term = pagination.Search.Trim().ToLowerInvariant();

            query = query.Where(intent =>
                intent.IntentReference.ToLower().Contains(term)
                || intent.DonorName.ToLower().Contains(term)
                || intent.NormalisedEmail.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(intent => intent.Attempts.Any(attempt =>
                attempt.Status == PaymentAttemptStatus.TimedOut
                || attempt.Status == PaymentAttemptStatus.Pending))
            .ThenBy(intent => intent.CreatedAtUtc)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        var campaignNames = await ResolveCampaignNamesAsync(
            scope.TenantId, rows.Select(intent => intent.CampaignId), cancellationToken);

        var items = rows.Select(intent =>
        {
            var lastAttempt = intent.Attempts
                .OrderByDescending(attempt => attempt.AttemptNumber)
                .FirstOrDefault();

            return new PaymentSupportCaseResponse(
                intent.Id,
                intent.IntentReference,
                intent.DonorName,
                PaymentMappingConfig.MaskEmail(intent.Email, canSeeSensitiveDonor),
                intent.Amount.ToResponse(),
                intent.Status,
                intent.AttemptCount,
                intent.LastAttemptAtUtc,

                // The gateway's own message, not the donor-facing one: support needs the detail
                // the donor was deliberately spared.
                lastAttempt?.GatewayMessage ?? intent.FailureReason,

                lastAttempt?.GatewayResultCode,
                intent.Attempts.Any(attempt => attempt.NeedsVerification),
                intent.CampaignId,
                LookupCampaign(campaignNames, intent.CampaignId),
                intent.CreatedAtUtc);
        }).ToList();

        return new PagedResponse<PaymentSupportCaseResponse>(
            items, totalCount, pagination.Page, pagination.PageSize);
    }

    // =====================================================================================
    // Shared shaping
    // =====================================================================================

    /// <summary>
    /// Everything the detail screen needs, in one round trip.
    ///
    /// The donation's receipts are included two levels down because the inline donation summary
    /// reports whether a receipt was issued - and an absent collection would read as "no receipt"
    /// rather than "not loaded", which is the kind of wrong answer nobody questions.
    /// </summary>
    private IQueryable<DonationIntent> DetailQuery() =>
        context.DonationIntents
            .AsNoTracking()
            .Include(intent => intent.Attempts)
            .Include(intent => intent.Donation)
                .ThenInclude(donation => donation!.Receipts);

    private static DonationIntentDetailResponse BuildDetail(
        DonationIntent intent,
        string? campaignName,
        bool canSeeSensitiveDonor,
        IReadOnlyList<string> permittedActions)
    {
        var receiptNumber = intent.Donation?.Receipts
            .Where(receipt => receipt.Status == ReceiptStatus.Issued)
            .OrderByDescending(receipt => receipt.VersionNumber)
            .Select(receipt => receipt.ReceiptNumber)
            .FirstOrDefault();

        return new DonationIntentDetailResponse(
            intent.Id,
            intent.TenantId,
            intent.IntentReference,
            intent.Status,
            PaymentMappingConfig.Describe(intent.Status),
            intent.Amount.ToResponse(),
            intent.DonorName,
            PaymentMappingConfig.MaskEmail(intent.Email, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskMobile(intent.Mobile, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskTaxIdentifier(intent.TaxIdentifier, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskAddress(intent.AddressLine1, canSeeSensitiveDonor),
            PaymentMappingConfig.MaskAddress(intent.AddressLine2, canSeeSensitiveDonor),
            intent.CountryId,
            intent.StateId,
            intent.CityId,
            intent.PostalCode,
            intent.CampaignId,
            campaignName,
            intent.SourceType,
            PaymentMappingConfig.Describe(intent.SourceType),
            intent.TrackingReference,
            intent.TrackingAssetId,
            intent.LeadId,
            intent.DonorId,
            intent.ConsentGiven,
            intent.ConsentVersion,
            intent.ConsentGivenAtUtc,
            intent.AllowPublicRecognition,
            intent.PublicRecognitionName,
            intent.PaymentLinkUrl,
            intent.PaymentLinkExpiresAtUtc,
            intent.ExistingDonorMatched,
            intent.ExistingDonorCheckedAtUtc,
            intent.AttemptCount,
            intent.LastAttemptAtUtc,
            intent.FailureReason,
            intent.CancellationReason,

            // Section 24: the whole lifecycle, newest first - which is the order somebody
            // investigating a failed payment reads it in.
            [.. intent.Attempts
                .OrderByDescending(attempt => attempt.AttemptNumber)
                .Select(attempt => attempt.ToResponse())],

            intent.Donation?.ToSummaryResponse(receiptNumber),
            intent.CreatedAtUtc,
            intent.CreatedByUserId,
            intent.UpdatedAtUtc,
            intent.UpdatedByUserId,
            intent.Version,
            permittedActions);
    }

    /// <summary>
    /// Narrows a query to the signed-in donor's own donations.
    ///
    /// IT MATCHES ON <c>NormalisedEmail</c>, which is written lower-cased when the intent is
    /// created, so this is an index-friendly equality rather than a function over every row.
    ///
    /// NO IDENTITY MEANS NO ROWS. A donor whose token carries no e-mail cannot have a filter
    /// built for them, and the safe resolution of "I cannot tell whose these are" is to return
    /// none - never to fall through and return everybody's.
    /// </summary>
    private static IQueryable<DonationIntent> ApplyDonorScope(
        IQueryable<DonationIntent> query, AccessScope scope)
    {
        if (!scope.IsDonorSelfService)
        {
            return query;
        }

        return scope.HasDonorIdentity
            ? query.Where(intent => intent.NormalisedEmail == scope.DonorEmail)
            : query.Where(_ => false);
    }

    private static IQueryable<DonationIntent> ApplyFilter(
        IQueryable<DonationIntent> query, DonationIntentSearchFilter filter, AccessScope scope)
    {
        if (scope.IsOwnRecordsOnly)
        {
            query = query.Where(intent => intent.CreatedByUserId == scope.UserId);
        }

        query = ApplyDonorScope(query, scope);

        if (filter.Status.HasValue)
        {
            query = query.Where(intent => intent.Status == filter.Status.Value);
        }

        if (filter.SourceType.HasValue)
        {
            query = query.Where(intent => intent.SourceType == filter.SourceType.Value);
        }

        if (filter.CampaignId.HasValue)
        {
            query = query.Where(intent => intent.CampaignId == filter.CampaignId.Value);
        }

        if (filter.LeadId.HasValue)
        {
            query = query.Where(intent => intent.LeadId == filter.LeadId.Value);
        }

        if (filter.CreatedFromUtc.HasValue)
        {
            query = query.Where(intent => intent.CreatedAtUtc >= filter.CreatedFromUtc.Value);
        }

        if (filter.CreatedToUtc.HasValue)
        {
            query = query.Where(intent => intent.CreatedAtUtc <= filter.CreatedToUtc.Value);
        }

        // "Needs attention" is not the same as "failed": an intent that failed and was then paid
        // needs nobody, and an intent still in progress with a timed-out attempt needs somebody
        // urgently.
        if (filter.NeedsAttention == true)
        {
            query = query.Where(intent =>
                (intent.Status == DonationIntentStatus.Failed
                 || intent.Status == DonationIntentStatus.PaymentInProgress)
                && intent.Donation == null);
        }
        else if (filter.NeedsAttention == false)
        {
            query = query.Where(intent =>
                intent.Status != DonationIntentStatus.Failed
                && intent.Status != DonationIntentStatus.PaymentInProgress);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            // The e-mail is matched on the NORMALISED column, which is already lower-cased and
            // indexed - matching the display column would be both slower and case-sensitive.
            query = query.Where(intent =>
                intent.IntentReference.ToLower().Contains(term)
                || intent.DonorName.ToLower().Contains(term)
                || intent.NormalisedEmail.Contains(term)
                || (intent.Mobile != null && intent.Mobile.Contains(term)));
        }

        return query;
    }

    /// <summary>
    /// Sorting, from a whitelist.
    ///
    /// A WHITELIST RATHER THAN A DYNAMIC EXPRESSION, because the sort key arrives in a query
    /// string. Anything unrecognised falls back to newest-first instead of failing, so a stale
    /// bookmark still returns a sensible page.
    /// </summary>
    private static IQueryable<DonationIntent> ApplySort(IQueryable<DonationIntent> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "reference" => query.OrderBy(intent => intent.IntentReference),
            "reference_desc" => query.OrderByDescending(intent => intent.IntentReference),
            "donor" => query.OrderBy(intent => intent.DonorName),
            "donor_desc" => query.OrderByDescending(intent => intent.DonorName),
            "amount" => query.OrderBy(intent => intent.Amount.Amount),
            "amount_desc" => query.OrderByDescending(intent => intent.Amount.Amount),
            "status" => query.OrderBy(intent => intent.Status).ThenByDescending(intent => intent.CreatedAtUtc),
            "created" => query.OrderBy(intent => intent.CreatedAtUtc),
            _ => query.OrderByDescending(intent => intent.CreatedAtUtc)
        };

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveCampaignNamesAsync(
        Guid tenantId, IEnumerable<Guid?> campaignIds, CancellationToken cancellationToken)
    {
        var ids = campaignIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await campaigns.GetCampaignNamesAsync(tenantId, ids, cancellationToken);
    }

    private static string? LookupCampaign(IReadOnlyDictionary<Guid, string> names, Guid? campaignId) =>
        campaignId.HasValue && names.TryGetValue(campaignId.Value, out var name) ? name : null;
}

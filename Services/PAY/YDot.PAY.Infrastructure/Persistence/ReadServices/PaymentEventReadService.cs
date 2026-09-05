using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Abstractions.Services;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Application.Features.Shared.Mappings;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.ReadServices;

/// <summary>
/// The read side of the gateway event queue - SCR-PAY-003.
///
/// THE RAW PAYLOAD IS THE INTERESTING PROBLEM HERE. It is the verbatim body a gateway posted,
/// and it routinely contains the donor's e-mail, name and sometimes a masked instrument - all
/// things the rest of this module masks carefully. Returning it to anybody who can open the
/// queue would undo that masking through the back door, so the detail read strips it unless the
/// caller holds the events permission.
///
/// UNLIKE THE REPOSITORY, THIS SCOPES BY ORGANISATION. The repository is unfiltered because a
/// webhook has no session; these reads feed a screen, and a screen has a signed-in operator who
/// must see only their own charity's events.
/// </summary>
public sealed class PaymentEventReadService(
    PaymentDbContext context,
    ICurrentUser currentUser,
    ICampaignDirectory campaigns,
    ITenantContext tenantContext)
    : IPaymentEventReadService
{
    public async Task<PagedResponse<PaymentEventListItemResponse>> SearchAsync(
        PaymentEventSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyDonorScope(
            ApplyFilter(
                context.PaymentEvents
                    .AsNoTracking()
                    .Include(paymentEvent => paymentEvent.PaymentAttempt)
                        .ThenInclude(attempt => attempt!.DonationIntent),
                filter),
            currentUser.Scope);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        // ONE LOOKUP FOR THE WHOLE PAGE, not one per row. Campaign names live in CAM, so naming
        // them row by row would be a request per row on every page of the queue.
        var campaignIds = rows
            .Select(row => row.PaymentAttempt?.DonationIntent?.CampaignId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        // THE TENANT COMES FROM THE REQUEST'S OWN CONTEXT, not from a row. Reading it off the
        // first result would mean an empty page had no tenant to ask about, and a mixed page -
        // which the row filter already prevents - would silently name the wrong charity's
        // campaigns.
        var tenantId = tenantContext.TenantId;

        var campaignNames = campaignIds.Count == 0 || tenantId is null
            ? new Dictionary<Guid, string>()
            : await campaigns.GetCampaignNamesAsync(tenantId.Value, campaignIds, cancellationToken);

        var canSeeDonor = currentUser.HasPermission(PermissionCodes.DonationsViewSensitiveDonor);

        var items = rows.Select(row => ToListItem(row, campaignNames, canSeeDonor)).ToList();

        return new PagedResponse<PaymentEventListItemResponse>(
            items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<PaymentEventDetailResponse?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var paymentEvent = await ApplyDonorScope(
                context.PaymentEvents
                    .AsNoTracking()
                    .Include(candidate => candidate.PaymentAttempt)
                        .ThenInclude(attempt => attempt!.DonationIntent),
                currentUser.Scope)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (paymentEvent is null)
        {
            return null;
        }

        var canSeeRawPayload = currentUser.HasPermission(PermissionCodes.PaymentsViewEvents);

        return new PaymentEventDetailResponse(
            paymentEvent.Id,
            paymentEvent.EventType,
            PaymentMappingConfig.Describe(paymentEvent.EventType),
            paymentEvent.Status,
            PaymentMappingConfig.Describe(paymentEvent.Status),
            paymentEvent.GatewayName,
            paymentEvent.GatewayEventId,
            paymentEvent.GatewayReference,
            paymentEvent.Amount.ToResponseOrNull(),
            paymentEvent.OccurredAtUtc,
            paymentEvent.ReceivedAtUtc,
            paymentEvent.ProcessedAtUtc,
            paymentEvent.SignatureVerified,
            paymentEvent.ProcessingError,
            paymentEvent.ProcessingAttempts,
            paymentEvent.DonationIntentId ?? paymentEvent.PaymentAttempt?.DonationIntentId,
            paymentEvent.PaymentAttempt?.DonationIntent?.IntentReference,
            paymentEvent.PaymentAttemptId,

            // Withheld rather than masked. A gateway payload has no fixed shape, so there is no
            // reliable way to mask the donor details inside it - the only safe answer for a
            // caller without the permission is not to return it at all.
            canSeeRawPayload ? paymentEvent.RawPayload : null,

            paymentEvent.DismissedByUserId,
            paymentEvent.DismissalReason,
            paymentEvent.Version,
            PermittedActionsFor(paymentEvent));
    }

    // =====================================================================================
    // Shaping
    // =====================================================================================

    private static PaymentEventListItemResponse ToListItem(
        PaymentEvent paymentEvent,
        IReadOnlyDictionary<Guid, string> campaignNames,
        bool canSeeDonor)
    {
        var intent = paymentEvent.PaymentAttempt?.DonationIntent;

        var campaignName = intent?.CampaignId is Guid campaignId
            && campaignNames.TryGetValue(campaignId, out var name)
                ? name
                : null;

        return new PaymentEventListItemResponse(
            paymentEvent.Id,
            paymentEvent.EventType,
            PaymentMappingConfig.Describe(paymentEvent.EventType),
            paymentEvent.Status,
            PaymentMappingConfig.Describe(paymentEvent.Status),
            paymentEvent.GatewayName,
            paymentEvent.GatewayEventId,
            paymentEvent.GatewayReference,

            // THE INTENT'S AMOUNT WHERE THE EVENT CARRIES NONE. A payment.failed webhook often
            // omits it, and a queue row reading "-" for the amount is the one column an operator
            // most needs to see.
            paymentEvent.Amount.ToResponseOrNull() ?? intent?.Amount.ToResponse(),

            paymentEvent.OccurredAtUtc,
            paymentEvent.ReceivedAtUtc,
            paymentEvent.ProcessedAtUtc,
            paymentEvent.SignatureVerified,
            paymentEvent.ProcessingError,
            paymentEvent.ProcessingAttempts,
            paymentEvent.DonationIntentId ?? paymentEvent.PaymentAttempt?.DonationIntentId,
            intent?.IntentReference,
            paymentEvent.Version,
            intent?.DonorName,
            intent is null ? null : PaymentMappingConfig.MaskEmail(intent.Email, canSeeDonor),
            campaignName,
            DescribeOutcome(intent?.Status));
    }

    /// <summary>
    /// The donation's outcome in the document's own three words.
    ///
    /// EXPIRED AND CANCELLED ARE PENDING, NOT FAIL. The document says a donor who cancels part-way
    /// appears in the queue as Pending, and an expired link is the same situation - nothing was
    /// charged and nothing was refused, so the recovery is to send them a fresh link rather than
    /// to retry a payment that never happened.
    /// </summary>
    private static string DescribeOutcome(DonationIntentStatus? status) =>
        status switch
        {
            DonationIntentStatus.Paid => "Success",
            DonationIntentStatus.Failed => "Fail",
            null => "Pending",
            _ => "Pending",
        };

    /// <summary>
    /// What may be done to a queued event next.
    ///
    /// A PROCESSED EVENT OFFERS NOTHING. Reprocessing one that already applied would record the
    /// donation a second time, which is the exact failure the queue exists to prevent - so the
    /// button is not drawn rather than drawn and refused.
    /// </summary>
    private IReadOnlyList<string> PermittedActionsFor(PaymentEvent paymentEvent)
    {
        var actions = new List<string>();

        if (currentUser.HasPermission(PermissionCodes.PaymentsViewEvents))
        {
            actions.Add("View");
        }

        if (paymentEvent.Status is PaymentEventStatus.Processed or PaymentEventStatus.Dismissed)
        {
            return actions;
        }

        if (currentUser.HasPermission(PermissionCodes.PaymentsReprocessEvent))
        {
            actions.Add("Reprocess");
        }

        if (currentUser.HasPermission(PermissionCodes.PaymentsDismissEvent))
        {
            actions.Add("Dismiss");
        }

        return actions;
    }

    /// <summary>
    /// Narrows the queue to the signed-in donor's own events.
    ///
    /// THIS READ SERVICE TOOK NO <see cref="AccessScope"/> AT ALL, and that was defensible while
    /// every caller was a member of staff: the Organisation filter on the DbContext was the only
    /// boundary the queue needed. The donor role changes that. Payments and Receipts is on the
    /// donor's own menu, so without this a donor opening it would page through every donation
    /// every other donor in the Organisation has ever attempted, with names and masked contact
    /// details attached.
    ///
    /// IT READS THE SCOPE FROM <c>ICurrentUser</c> RATHER THAN TAKING A PARAMETER, which keeps
    /// the interface and its three call sites unchanged. The service already injects the current
    /// user for the two permission checks below, so the scope is not a new dependency - and a
    /// narrowing that cannot be forgotten at a call site is the one that holds.
    ///
    /// THE E-MAIL IS ON THE INTENT, reached through the attempt. An event that never matched an
    /// attempt - an unrecognised webhook - therefore belongs to nobody and is correctly invisible
    /// to a donor; those are exactly the rows that are staff's to investigate.
    ///
    /// NO IDENTITY MEANS NO ROWS, never all rows.
    /// </summary>
    private static IQueryable<PaymentEvent> ApplyDonorScope(
        IQueryable<PaymentEvent> query, AccessScope scope)
    {
        if (!scope.IsDonorSelfService)
        {
            return query;
        }

        return scope.HasDonorIdentity
            ? query.Where(paymentEvent =>
                paymentEvent.PaymentAttempt != null
                && paymentEvent.PaymentAttempt.DonationIntent != null
                && paymentEvent.PaymentAttempt.DonationIntent.NormalisedEmail == scope.DonorEmail)
            : query.Where(_ => false);
    }

    private static IQueryable<PaymentEvent> ApplyFilter(
        IQueryable<PaymentEvent> query, PaymentEventSearchFilter filter)
    {
        if (filter.Status.HasValue)
        {
            query = query.Where(paymentEvent => paymentEvent.Status == filter.Status.Value);
        }

        if (filter.EventType.HasValue)
        {
            query = query.Where(paymentEvent => paymentEvent.EventType == filter.EventType.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.GatewayName))
        {
            var gateway = filter.GatewayName.Trim();

            query = query.Where(paymentEvent => paymentEvent.GatewayName == gateway);
        }

        if (filter.ReceivedFromUtc.HasValue)
        {
            query = query.Where(paymentEvent => paymentEvent.ReceivedAtUtc >= filter.ReceivedFromUtc.Value);
        }

        if (filter.ReceivedToUtc.HasValue)
        {
            query = query.Where(paymentEvent => paymentEvent.ReceivedAtUtc <= filter.ReceivedToUtc.Value);
        }

        // The first thing to look at when something is wrong: a failed signature is either a
        // misconfiguration or somebody trying to fabricate a payment.
        if (filter.SignatureFailedOnly == true)
        {
            query = query.Where(paymentEvent => !paymentEvent.SignatureVerified);
        }

        if (filter.OutstandingOnly == true)
        {
            query = query.Where(paymentEvent =>
                paymentEvent.Status == PaymentEventStatus.Pending
                || paymentEvent.Status == PaymentEventStatus.Failed);
        }

        // THE QUEUE IS FAIL AND PENDING, AND NOTHING ELSE. The document is explicit that a
        // successful payment never appears here - it goes straight to the receipt - so an unset
        // filter still excludes Success rather than returning everything.
        query = filter.PaymentOutcome switch
        {
            PaymentOutcomeFilter.Fail => query.Where(paymentEvent =>
                paymentEvent.PaymentAttempt != null
                && paymentEvent.PaymentAttempt.DonationIntent != null
                && paymentEvent.PaymentAttempt.DonationIntent.Status == DonationIntentStatus.Failed),

            PaymentOutcomeFilter.Pending => query.Where(paymentEvent =>
                paymentEvent.PaymentAttempt == null
                || paymentEvent.PaymentAttempt.DonationIntent == null
                || (paymentEvent.PaymentAttempt.DonationIntent.Status != DonationIntentStatus.Failed
                    && paymentEvent.PaymentAttempt.DonationIntent.Status != DonationIntentStatus.Paid)),

            PaymentOutcomeFilter.Success => query.Where(paymentEvent =>
                paymentEvent.PaymentAttempt != null
                && paymentEvent.PaymentAttempt.DonationIntent != null
                && paymentEvent.PaymentAttempt.DonationIntent.Status == DonationIntentStatus.Paid),

            _ => query.Where(paymentEvent =>
                paymentEvent.PaymentAttempt == null
                || paymentEvent.PaymentAttempt.DonationIntent == null
                || paymentEvent.PaymentAttempt.DonationIntent.Status != DonationIntentStatus.Paid),
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            // THE DONOR'S NAME IS THE SEARCH TERM PEOPLE ACTUALLY HAVE. Somebody triaging this
            // queue is usually acting on a phone call, and the caller quotes their own name and
            // e-mail - never a gateway event id.
            query = filter.SearchIncludesDonor
                ? query.Where(paymentEvent =>
                    paymentEvent.GatewayEventId.ToLower().Contains(term)
                    || (paymentEvent.GatewayReference != null
                        && paymentEvent.GatewayReference.ToLower().Contains(term))
                    || (paymentEvent.PaymentAttempt != null
                        && paymentEvent.PaymentAttempt.DonationIntent != null
                        && (paymentEvent.PaymentAttempt.DonationIntent.DonorName.ToLower().Contains(term)
                            || paymentEvent.PaymentAttempt.DonationIntent.Email.ToLower().Contains(term)
                            || paymentEvent.PaymentAttempt.DonationIntent.IntentReference.ToLower().Contains(term))))
                : query.Where(paymentEvent =>
                    paymentEvent.GatewayEventId.ToLower().Contains(term)
                    || (paymentEvent.GatewayReference != null
                        && paymentEvent.GatewayReference.ToLower().Contains(term)));
        }

        return query;
    }

    private static IQueryable<PaymentEvent> ApplySort(IQueryable<PaymentEvent> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "received" => query.OrderBy(paymentEvent => paymentEvent.ReceivedAtUtc),
            "occurred" => query.OrderBy(paymentEvent => paymentEvent.OccurredAtUtc),
            "occurred_desc" => query.OrderByDescending(paymentEvent => paymentEvent.OccurredAtUtc),
            "status" => query.OrderBy(paymentEvent => paymentEvent.Status)
                .ThenByDescending(paymentEvent => paymentEvent.ReceivedAtUtc),

            // Newest first by default. An operator opening this queue is nearly always looking at
            // what just happened, not at the backlog.
            _ => query.OrderByDescending(paymentEvent => paymentEvent.ReceivedAtUtc)
        };
}

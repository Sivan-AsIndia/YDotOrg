using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Abstractions.Security;
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
public sealed class PaymentEventReadService(PaymentDbContext context, ICurrentUser currentUser)
    : IPaymentEventReadService
{
    public async Task<PagedResponse<PaymentEventListItemResponse>> SearchAsync(
        PaymentEventSearchFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = ApplyFilter(
            context.PaymentEvents
                .AsNoTracking()
                .Include(paymentEvent => paymentEvent.PaymentAttempt)
                    .ThenInclude(attempt => attempt!.DonationIntent),
            filter);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await ApplySort(query, filter.Sort)
            .Skip(filter.Skip)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToListItem).ToList();

        return new PagedResponse<PaymentEventListItemResponse>(
            items, totalCount, filter.Page, filter.PageSize);
    }

    public async Task<PaymentEventDetailResponse?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var paymentEvent = await context.PaymentEvents
            .AsNoTracking()
            .Include(candidate => candidate.PaymentAttempt)
                .ThenInclude(attempt => attempt!.DonationIntent)
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

    private static PaymentEventListItemResponse ToListItem(PaymentEvent paymentEvent) =>
        new(paymentEvent.Id,
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
            paymentEvent.Version);

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

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(paymentEvent =>
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

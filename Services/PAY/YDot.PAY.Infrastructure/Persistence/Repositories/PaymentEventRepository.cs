using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.Repositories;

/// <summary>
/// The gateway event queue.
///
/// EVERY READ HERE IGNORES THE ORGANISATION FILTER, which is unusual enough to state plainly:
/// a webhook arrives with no session, no header and no user, so at the moment these queries run
/// there is no Organisation to filter by. The Organisation is what the lookups RESOLVE, working
/// from a gateway reference that is unique platform-wide.
///
/// The staff-facing reads of this same data go through <c>PaymentEventReadService</c>, which
/// scopes explicitly - so nothing here ends up on a screen unscoped.
/// </summary>
public sealed class PaymentEventRepository(PaymentDbContext context) : IPaymentEventRepository
{
    public async Task AddAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken) =>
        await context.PaymentEvents.AddAsync(paymentEvent, cancellationToken);

    public Task<PaymentEvent?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.PaymentEvents
            .IgnoreQueryFilters()
            .Include(paymentEvent => paymentEvent.PaymentAttempt)
                .ThenInclude(attempt => attempt!.DonationIntent)
            .FirstOrDefaultAsync(paymentEvent => paymentEvent.Id == id, cancellationToken);

    /// <summary>
    /// THE DUPLICATE-DELIVERY GUARD.
    ///
    /// Gateways redeliver webhooks - sometimes for days, sometimes because our own 500 made them
    /// - and without this check a redelivered capture would record the donation a second time.
    /// Matched on (gateway, event id) rather than event id alone, because two providers may
    /// legitimately issue the same identifier.
    ///
    /// Returning the EXISTING ROW rather than a bool is deliberate: the caller answers the
    /// webhook with what it decided the first time, so the gateway sees a consistent result
    /// instead of a second, different one.
    /// </summary>
    public Task<PaymentEvent?> FindByGatewayEventIdAsync(
        string gatewayName, string gatewayEventId, CancellationToken cancellationToken) =>
        context.PaymentEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                paymentEvent => paymentEvent.GatewayName == gatewayName
                                && paymentEvent.GatewayEventId == gatewayEventId,
                cancellationToken);

    /// <summary>
    /// Events still needing processing or a person, oldest first.
    ///
    /// OLDEST FIRST because an unprocessed capture is money the books do not yet know about, and
    /// the longer it sits the more likely somebody has already asked about it. Capped so a
    /// backlog cannot be loaded whole.
    /// </summary>
    public async Task<IReadOnlyList<PaymentEvent>> GetOutstandingAsync(
        int maximumRows, CancellationToken cancellationToken) =>
        await context.PaymentEvents
            .IgnoreQueryFilters()
            .Where(paymentEvent => paymentEvent.Status == PaymentEventStatus.Pending
                                   || paymentEvent.Status == PaymentEventStatus.Failed)
            .OrderBy(paymentEvent => paymentEvent.ReceivedAtUtc)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);
}

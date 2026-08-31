using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>Write-side access to the gateway event queue.</summary>
public interface IPaymentEventRepository
{
    Task AddAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken);

    Task<PaymentEvent?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether this exact gateway event has already been stored.
    ///
    /// THE DUPLICATE-DELIVERY GUARD. Gateways retry webhooks, sometimes for days, and without
    /// this a redelivered capture event would record the donation twice. Checked ACROSS
    /// Organisations because a webhook arrives with no session to scope it.
    /// </summary>
    Task<PaymentEvent?> FindByGatewayEventIdAsync(
        string gatewayName, string gatewayEventId, CancellationToken cancellationToken);

    /// <summary>Events still needing processing or a person, oldest first.</summary>
    Task<IReadOnlyList<PaymentEvent>> GetOutstandingAsync(
        int maximumRows, CancellationToken cancellationToken);
}

using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>
/// The per-Organisation gateway configuration.
///
/// THIS IS WHAT MAKES THE MODULE GENUINELY TENANT-SPECIFIC. Each charity's donations go to its
/// OWN merchant account; a shared account would pool every organisation's income into one
/// settlement, which is a legal problem rather than a data one.
/// </summary>
public interface IGatewayAccountRepository
{
    Task AddAsync(PaymentGatewayAccount account, CancellationToken cancellationToken);

    Task<PaymentGatewayAccount?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The active account for the caller's Organisation.
    ///
    /// Returns null when payments have not been set up, which the handlers report as
    /// PAYMENT_GATEWAY_NOT_CONFIGURED rather than as a generic failure - a donor should be told
    /// to contact the charity, not that something broke.
    /// </summary>
    Task<PaymentGatewayAccount?> GetActiveForTenantAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The active account for a NAMED Organisation, bypassing the filter.
    ///
    /// A DELIBERATE BYPASS FOR THE PUBLIC AND WEBHOOK PATHS. A donor following a payment link and
    /// a gateway posting a callback both arrive with no session; the Organisation has already
    /// been resolved from the intent or the attempt, and this loads the account that belongs to
    /// it. It takes a TenantId rather than reading ambient state precisely so the caller has to
    /// have resolved one.
    /// </summary>
    Task<PaymentGatewayAccount?> GetActiveForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PaymentGatewayAccount>> GetAllForTenantAsync(CancellationToken cancellationToken);
}

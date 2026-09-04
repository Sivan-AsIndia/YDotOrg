using YDot.IAM.Domain.Entities.Configuration;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// The payment gateway configurations, and the change log beside them.
///
/// EVERY READ HERE IS ORGANISATION-SCOPED BY THE QUERY FILTER, with two named exceptions that
/// take a TenantId as a parameter rather than reading ambient state. Both exist for SuperAdmin,
/// who is entitled to look across Organisations and who has to say which one they mean; making
/// the id a parameter is what stops a global read happening by accident on a path that meant to
/// be scoped.
/// </summary>
public interface IPaymentGatewayConfigurationRepository
{
    Task AddAsync(PaymentGatewayConfiguration configuration, CancellationToken cancellationToken);

    void Remove(PaymentGatewayConfiguration configuration);

    /// <summary>One configuration inside the caller's scope, or null.</summary>
    Task<PaymentGatewayConfiguration?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One configuration ACROSS Organisations. SuperAdmin only - the handler checks before
    /// calling, and the caller has to have an id in hand to get here at all.
    /// </summary>
    Task<PaymentGatewayConfiguration?> GetAcrossTenantsAsync(
        Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The row for a provider and environment, which together with the Organisation form the
    /// natural key. Used to refuse a duplicate before one reaches the unique index.
    /// </summary>
    Task<PaymentGatewayConfiguration?> GetByProviderAsync(
        Guid tenantId,
        PaymentGatewayProvider provider,
        PaymentGatewayEnvironment environment,
        CancellationToken cancellationToken);

    /// <summary>Every configuration the caller's Organisation holds, live and sandbox alike.</summary>
    Task<IReadOnlyList<PaymentGatewayConfiguration>> GetForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// The other active rows in the same environment, which activating one has to stand down.
    ///
    /// AT MOST ONE ACTIVE ROW PER ENVIRONMENT is the rule the payment flow depends on: PAY asks
    /// for "the active configuration" and a second one would make the answer arbitrary.
    /// </summary>
    Task<IReadOnlyList<PaymentGatewayConfiguration>> GetOtherActiveAsync(
        Guid tenantId,
        PaymentGatewayEnvironment environment,
        Guid excludingId,
        CancellationToken cancellationToken);

    // ---- Change log ---------------------------------------------------------------------------

    /// <summary>
    /// Adds change-log rows.
    ///
    /// TAKES A COLLECTION because an update writes one row per changed field, and adding twelve
    /// rows one call at a time would let a partial failure leave a half-written history of a
    /// change that fully happened.
    /// </summary>
    Task AddAuditAsync(
        IEnumerable<PaymentGatewayConfigurationAudit> entries, CancellationToken cancellationToken);
}

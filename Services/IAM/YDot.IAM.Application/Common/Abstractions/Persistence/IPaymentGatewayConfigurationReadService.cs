using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;

namespace YDot.IAM.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read side for the payment gateway configuration screen and its change log.
///
/// SEPARATE FROM THE REPOSITORY FOR THE USUAL REASON - a grid wants columns from two tables for
/// twenty-five rows, not twenty-five tracked aggregates - and for one that is specific to this
/// feature: the projections here are where the credential columns are DROPPED. A repository
/// hands back the entity, ciphertext and all; these methods return DTOs that have nowhere to
/// put it.
///
/// <c>tenantId</c> IS A PARAMETER RATHER THAN AMBIENT STATE on every method. SuperAdmin reads
/// across Organisations, so the query cannot rely on the DbContext filter alone, and making the
/// scope an argument means each call site has had to decide what it is asking for.
/// </summary>
public interface IPaymentGatewayConfigurationReadService
{
    /// <summary>
    /// The configurations, filtered and paged.
    ///
    /// <paramref name="tenantId"/> null means EVERY Organisation, which is reachable only for a
    /// root user - the handler resolves it and never passes null for anybody else.
    /// </summary>
    Task<PagedResponse<PaymentGatewayConfigurationResponse>> SearchAsync(
        PaymentGatewayConfigurationFilter filter,
        Guid? tenantId,
        Func<bool, IReadOnlyList<string>> permittedActions,
        CancellationToken cancellationToken);

    /// <summary>One configuration, with its Organisation's name resolved.</summary>
    Task<PaymentGatewayConfigurationResponse?> GetAsync(
        Guid id,
        Guid? tenantId,
        Func<bool, IReadOnlyList<string>> permittedActions,
        CancellationToken cancellationToken);

    /// <summary>
    /// The change log, newest first.
    ///
    /// NEWEST FIRST IS NOT A PREFERENCE. Somebody opens this panel because something stopped
    /// working today, and the row that explains it is the most recent one.
    /// </summary>
    Task<PagedResponse<PaymentGatewayConfigurationAuditResponse>> SearchAuditAsync(
        PaymentGatewayAuditFilter filter, Guid? tenantId, CancellationToken cancellationToken);
}

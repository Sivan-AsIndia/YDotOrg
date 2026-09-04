using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Domain.Entities.Configuration;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.Repositories;

/// <summary>
/// Loads and stores gateway configurations.
///
/// TWO METHODS HERE CALL <c>IgnoreQueryFilters</c> AND BOTH ARE NAMED FOR IT. The DbContext
/// filters every read to the caller's Organisation, which is right for a TenantAdmin and wrong
/// for a root user supporting one - so the two paths that a root user reaches take the
/// Organisation as an argument, and the handler has already decided whether the caller is
/// entitled to use them.
///
/// EVERYTHING ELSE GOES THROUGH THE FILTER, including the natural-key lookup. That one takes a
/// TenantId as well, but as a NARROWING inside the filter rather than around it: it is called on
/// a path where the Organisation has just been resolved, and passing it explicitly means the
/// query says which Organisation it is about rather than relying on ambient state to be right.
/// </summary>
public sealed class PaymentGatewayConfigurationRepository(IamDbContext context)
    : IPaymentGatewayConfigurationRepository
{
    public async Task AddAsync(
        PaymentGatewayConfiguration configuration, CancellationToken cancellationToken) =>
        await context.PaymentGatewayConfigurations.AddAsync(configuration, cancellationToken);

    public void Remove(PaymentGatewayConfiguration configuration) =>
        context.PaymentGatewayConfigurations.Remove(configuration);

    public Task<PaymentGatewayConfiguration?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.PaymentGatewayConfigurations
            .FirstOrDefaultAsync(configuration => configuration.Id == id, cancellationToken);

    /// <summary>
    /// One configuration whatever Organisation owns it.
    ///
    /// A DELIBERATE FILTER BYPASS FOR SUPERADMIN, and the only thing standing between it and a
    /// cross-Organisation read is the handler's scope check - which is why the name says what it
    /// does rather than hiding it behind an overload of <see cref="GetAsync"/>.
    /// </summary>
    public Task<PaymentGatewayConfiguration?> GetAcrossTenantsAsync(
        Guid id, CancellationToken cancellationToken) =>
        context.PaymentGatewayConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(configuration => configuration.Id == id, cancellationToken);

    public Task<PaymentGatewayConfiguration?> GetByProviderAsync(
        Guid tenantId,
        PaymentGatewayProvider provider,
        PaymentGatewayEnvironment environment,
        CancellationToken cancellationToken) =>
        context.PaymentGatewayConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                configuration => configuration.TenantId == tenantId
                                 && configuration.Provider == provider
                                 && configuration.Environment == environment,
                cancellationToken);

    public async Task<IReadOnlyList<PaymentGatewayConfiguration>> GetForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        await context.PaymentGatewayConfigurations
            .IgnoreQueryFilters()
            .Where(configuration => configuration.TenantId == tenantId)
            .OrderBy(configuration => configuration.Environment)
            .ThenBy(configuration => configuration.Provider)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The other active rows in one environment.
    ///
    /// TRACKED, NOT PROJECTED, because the caller is about to set IsActive false on every one of
    /// them inside the same unit of work.
    /// </summary>
    public async Task<IReadOnlyList<PaymentGatewayConfiguration>> GetOtherActiveAsync(
        Guid tenantId,
        PaymentGatewayEnvironment environment,
        Guid excludingId,
        CancellationToken cancellationToken) =>
        await context.PaymentGatewayConfigurations
            .IgnoreQueryFilters()
            .Where(configuration => configuration.TenantId == tenantId
                                    && configuration.Environment == environment
                                    && configuration.IsActive
                                    && configuration.Id != excludingId)
            .ToListAsync(cancellationToken);

    public async Task AddAuditAsync(
        IEnumerable<PaymentGatewayConfigurationAudit> entries, CancellationToken cancellationToken) =>
        await context.PaymentGatewayConfigurationAudits.AddRangeAsync(entries, cancellationToken);
}

using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Persistence.Repositories;

/// <summary>
/// Each Organisation's own gateway configuration.
///
/// THIS IS WHERE TENANT ISOLATION STOPS BEING A DATA QUESTION AND BECOMES A LEGAL ONE. Every
/// charity collects into its OWN merchant account; a shared account would pool several
/// organisations' income into one settlement, which no amount of correct reporting afterwards
/// would fix.
/// </summary>
public sealed class GatewayAccountRepository(PaymentDbContext context) : IGatewayAccountRepository
{
    public async Task AddAsync(PaymentGatewayAccount account, CancellationToken cancellationToken) =>
        await context.GatewayAccounts.AddAsync(account, cancellationToken);

    public Task<PaymentGatewayAccount?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        context.GatewayAccounts.FirstOrDefaultAsync(account => account.Id == id, cancellationToken);

    /// <summary>
    /// The active account for the caller's own Organisation, through the query filter.
    ///
    /// AN ORGANISATION MAY HOLD SEVERAL ROWS - a live one and a test one, or a retired provider -
    /// so this narrows to the single ACTIVE, NON-TEST account. Returning a test account to the
    /// live donation path would send a real donor to a sandbox and take no money at all.
    /// </summary>
    public Task<PaymentGatewayAccount?> GetActiveForTenantAsync(CancellationToken cancellationToken) =>
        context.GatewayAccounts
            .Where(account => account.IsActive && !account.IsTestMode)
            .OrderBy(account => account.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The active account for a NAMED Organisation.
    ///
    /// A DELIBERATE FILTER BYPASS FOR THE PUBLIC AND WEBHOOK PATHS. A donor following a payment
    /// link and a gateway posting a callback both arrive with no session; the Organisation has
    /// already been resolved from the intent or the attempt, and this loads the account
    /// belonging to it.
    ///
    /// IT TAKES THE ID AS A PARAMETER RATHER THAN READING AMBIENT STATE precisely so that the
    /// caller must have resolved one. A version that fell back to the ambient context would
    /// silently return nothing on exactly the paths this exists for.
    /// </summary>
    public Task<PaymentGatewayAccount?> GetActiveForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        context.GatewayAccounts
            .IgnoreQueryFilters()
            .Where(account => account.TenantId == tenantId)
            .Where(account => account.IsActive && !account.IsTestMode)
            .OrderBy(account => account.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Every account the caller's Organisation holds, live and test alike.
    ///
    /// The configuration screen needs both, which is why this one does NOT narrow by mode.
    /// Ordered so the live account leads - it is the one an operator is nearly always looking
    /// for, and putting a sandbox row at the top of that list invites the wrong edit.
    /// </summary>
    public async Task<IReadOnlyList<PaymentGatewayAccount>> GetAllForTenantAsync(
        CancellationToken cancellationToken) =>
        await context.GatewayAccounts
            .OrderBy(account => account.IsTestMode)
            .ThenBy(account => account.GatewayName)
            .ToListAsync(cancellationToken);
}

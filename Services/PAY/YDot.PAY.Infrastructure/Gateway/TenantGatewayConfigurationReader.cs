using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YDot.PAY.Infrastructure.Persistence;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Reads the Organisation's active gateway configuration from the IAM table.
///
/// SCOPED, WITH A PER-REQUEST MEMO. Taking one payment asks for the configuration two or three
/// times - once to pick the account, once to resolve the credential, sometimes again to verify a
/// signature - and each would otherwise be its own round trip on the donor's critical path. The
/// memo lives for one request, so a configuration changed mid-flight is picked up by the next
/// donation rather than being cached until a process restarts. That is the right trade for a
/// merchant credential: a stale one takes money into the wrong account.
///
/// PRODUCTION WINS OVER SANDBOX where an Organisation has both active, and the reasoning is the
/// same one the deployment guide already states: which environment a payment is really in is
/// decided by the KEY, not by the row's label. An Organisation with only a sandbox row
/// configured - which is every organisation during setup, and every organisation on a developer's
/// machine - still takes payments through it, with whatever key is stored. Refusing to use a row
/// labelled Sandbox would mean the configuration screen appeared to work and no donation ever
/// did.
/// </summary>
internal sealed class TenantGatewayConfigurationReader(
    PaymentDbContext context, ILogger<TenantGatewayConfigurationReader> logger)
{
    private readonly Dictionary<Guid, TenantGatewayConfiguration?> _memo = [];

    /// <summary>
    /// The Organisation's active configuration, or null when it has not set one up.
    ///
    /// NULL IS AN ORDINARY ANSWER, not an error. An Organisation that has never opened the
    /// configuration screen has no row, and everything downstream falls back to the way payments
    /// worked before this feature existed - the seeded gateway account and the credentials in
    /// the deployment's own configuration.
    /// </summary>
    public async Task<TenantGatewayConfiguration?> GetActiveAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        if (_memo.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var configuration = await context.TenantGatewayConfigurations
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.IsActive)

            // Production first. `Environment` is stored as text, so the ordering is on the
            // comparison rather than on the column - "Production" sorts after "Sandbox"
            // alphabetically, which would put the wrong one first.
            .OrderByDescending(row => row.Environment == "Production")
            .ThenBy(row => row.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        _memo[tenantId] = configuration;

        if (configuration is not null)
        {
            logger.LogDebug(
                "Organisation {TenantId} has an active {Provider} ({Environment}) gateway "
                + "configuration, so donations will use it rather than the deployment defaults.",
                tenantId,
                configuration.Provider,
                configuration.Environment);
        }

        return configuration;
    }

    /// <summary>
    /// The same answer, synchronously, for <see cref="IGatewayCredentialResolver"/>.
    ///
    /// WHY A SYNCHRONOUS PATH EXISTS AT ALL, since one is normally a mistake.
    /// <c>IGatewayCredentialResolver.Resolve</c> is synchronous and is called from inside the
    /// gateway adapters, which are handed an account rather than a chance to await anything.
    /// Changing that interface would touch every adapter and every call site for a lookup that
    /// is, in practice, already answered.
    ///
    /// AND IN PRACTICE IT IS ALREADY ANSWERED. Every path that reaches a credential resolver got
    /// its account from <see cref="ConfiguredGatewayAccountRepository"/>, which awaited this same
    /// reader a moment earlier and left the answer in the memo. The blocking query below is the
    /// cold path - a resolver reached some other way - and it is one indexed row from a table
    /// with a handful of rows per Organisation.
    /// </summary>
    public TenantGatewayConfiguration? GetActive(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        if (_memo.TryGetValue(tenantId, out var cached))
        {
            return cached;
        }

        var configuration = context.TenantGatewayConfigurations
            .AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.IsActive)
            .OrderByDescending(row => row.Environment == "Production")
            .ThenBy(row => row.CreatedAtUtc)
            .FirstOrDefault();

        _memo[tenantId] = configuration;

        return configuration;
    }
}

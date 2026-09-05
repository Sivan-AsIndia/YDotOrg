using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Application.Common.Settings;
using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Makes the donation flow honour what an Organisation entered on IAM's payment gateway
/// configuration screen.
///
/// WHY A DECORATOR RATHER THAN A CHANGE TO THE REPOSITORY. Six call sites across four command
/// handlers ask <see cref="IGatewayAccountRepository"/> for "this Organisation's account", and
/// every one of them is on a path that takes or refunds money. Threading a second lookup through
/// all six is six chances to miss one - and the one missed is a donation that quietly uses the
/// old merchant account after an administrator has changed it. Wrapping the interface means the
/// configuration is honoured on every path that exists today and on every path added later,
/// without any of them knowing.
///
/// IT DOES TWO THINGS, AND THE SECOND IS THE ONE THAT MATTERS MOST:
///
/// 1. MERGES. Where a <c>pay_gateway_accounts</c> row exists AND the Organisation has an active
///    configuration, a COPY of the row is returned with the configuration's provider, merchant
///    id, currency and URLs written over it. The GatewayName is what
///    <see cref="PaymentGatewayRouter"/> dispatches on, so an Organisation that switches from
///    Razorpay to another provider on the screen starts speaking that provider's API with no
///    deployment.
///
/// 2. SYNTHESISES. Where there is NO row - which is every Organisation on a stack that has never
///    run the gateway seeder - an account is built from the configuration alone. Without this,
///    an administrator could fill the screen in completely, see it marked active, and still have
///    every donation refused with PAYMENT_GATEWAY_NOT_CONFIGURED, because the flow was looking in
///    a table the screen does not write to.
///
/// NEITHER ONE EVER REACHES THE DATABASE. Both return a new, untracked entity rather than a
/// mutated one; see <c>Merge</c> for exactly what a mutated tracked entity would have written
/// into <c>pay_gateway_accounts</c> as a side effect of taking a donation, and why that would
/// eventually stop payments altogether.
///
/// A SYNTHESISED ACCOUNT'S ID IS THE CONFIGURATION'S ID, not a fresh Guid. Nothing has a foreign
/// key to <c>pay_gateway_accounts</c> - checked - and a stable id means a log line naming an
/// account can be traced to the configuration that produced it, where a per-request Guid would
/// name nothing.
/// </summary>
internal sealed class ConfiguredGatewayAccountRepository(
    IGatewayAccountRepository inner,
    TenantGatewayConfigurationReader configurations,
    IOptions<GatewayConfigurationSettings> settings,
    ILogger<ConfiguredGatewayAccountRepository> logger) : IGatewayAccountRepository
{
    private readonly GatewayConfigurationSettings _settings = settings.Value;

    public Task AddAsync(PaymentGatewayAccount account, CancellationToken cancellationToken) =>
        inner.AddAsync(account, cancellationToken);

    /// <summary>
    /// One account by id, untouched.
    ///
    /// NOT OVERLAID, and that is not an oversight. This is the administrative lookup - a screen
    /// showing what is stored in <c>pay_gateway_accounts</c> - and overlaying it would show
    /// somebody a row that does not match what they would find in the table.
    /// </summary>
    public Task<PaymentGatewayAccount?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        inner.GetAsync(id, cancellationToken);

    /// <summary>
    /// The active account for the caller's own Organisation.
    ///
    /// UNTOUCHED, because this overload has no TenantId to look a configuration up by: it relies
    /// on the ambient query filter, and reaching for the ambient Organisation here would give a
    /// different answer on the anonymous donation path than the filter gave. The paths that take
    /// money all use the overload below, which names the Organisation.
    /// </summary>
    public Task<PaymentGatewayAccount?> GetActiveForTenantAsync(CancellationToken cancellationToken) =>
        inner.GetActiveForTenantAsync(cancellationToken);

    /// <summary>
    /// The account a donation, refund or webhook for a NAMED Organisation should use.
    ///
    /// THIS IS THE ONE THE MONEY GOES THROUGH. Every donation, every verification, every refund
    /// resolves its account here.
    /// </summary>
    public async Task<PaymentGatewayAccount?> GetActiveForTenantAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var account = await inner.GetActiveForTenantAsync(tenantId, cancellationToken);

        if (!_settings.UseTenantConfiguration)
        {
            // The escape hatch is set. Behave exactly as this module did before the
            // configuration screen existed.
            return account;
        }

        var configuration = await configurations.GetActiveAsync(tenantId, cancellationToken);

        if (configuration is null)
        {
            return account;
        }

        if (account is null)
        {
            logger.LogInformation(
                "Organisation {TenantId} has no pay_gateway_accounts row, so its {Provider} "
                + "({Environment}) configuration is being used directly.",
                tenantId,
                configuration.Provider,
                configuration.Environment);

            return Synthesise(configuration);
        }

        return Merge(account, configuration);
    }

    public Task<IReadOnlyList<PaymentGatewayAccount>> GetAllForTenantAsync(
        CancellationToken cancellationToken) =>
        inner.GetAllForTenantAsync(cancellationToken);

    /// <summary>
    /// A COPY of the stored account with the configuration's answers written over it.
    ///
    /// A COPY, AND THAT IS THE WHOLE POINT OF THIS METHOD. The account the inner repository
    /// returns is TRACKED by the payment DbContext, and the donation flow calls SaveChanges a few
    /// lines later to write the payment attempt. Mutating the tracked entity would therefore
    /// PERSIST every overlaid value into <c>pay_gateway_accounts</c> as a side effect of taking a
    /// donation - including <c>ApiKeyReference</c>, which would be permanently replaced by a
    /// marker pointing at an IAM row. Delete that row afterwards and PAY is left with a dangling
    /// reference that resolves to nothing, so every subsequent donation is refused with
    /// PAYMENT_GATEWAY_NOT_CONFIGURED and the stored credentials it used to fall back on are
    /// unreachable. An untracked copy cannot do any of that.
    ///
    /// THE CONFIGURATION ONLY WINS WHERE IT ACTUALLY SAYS SOMETHING. A blank merchant id on the
    /// screen means "not filled in", not "clear the one PAY already has": the screen is the source
    /// of truth for what it holds, not for what it leaves empty.
    /// </summary>
    private static PaymentGatewayAccount Merge(
        PaymentGatewayAccount stored, TenantGatewayConfiguration configuration) =>
        new()
        {
            Id = stored.Id,
            TenantId = stored.TenantId,
            BusinessUnitId = stored.BusinessUnitId,

            // THE LINE THAT REDIRECTS THE MONEY. PaymentGatewayRouter dispatches on this, so an
            // organisation that switches provider on the configuration screen starts speaking
            // that provider's API with no deployment.
            GatewayName = Prefer(configuration.Provider, stored.GatewayName) ?? stored.GatewayName,
            MerchantId = Prefer(configuration.MerchantId, stored.MerchantId) ?? stored.MerchantId,

            // THE REFERENCE BECOMES A MARKER, which is what ties the two halves together:
            // TenantConfiguredCredentialResolver recognises it and unseals the configuration's own
            // credentials rather than looking the reference up in the deployment's configuration.
            ApiKeyReference = TenantConfiguredCredentialResolver.ReferenceFor(configuration),
            WebhookSecretReference = null,

            // The stored row's own mode is kept. See Synthesise for why the environment label is
            // not translated into test mode.
            IsTestMode = stored.IsTestMode,
            IsActive = stored.IsActive,
            SettlementCurrencyCode = Prefer(
                configuration.SettlementCurrencyCode, stored.SettlementCurrencyCode)
                ?? stored.SettlementCurrencyCode,
            ReturnUrl = Prefer(configuration.ReturnUrl, stored.ReturnUrl),
            WebhookUrl = Prefer(configuration.WebhookUrl, stored.WebhookUrl),
            PaymentLinkValidityMinutes = configuration.PaymentLinkValidityMinutes > 0
                ? configuration.PaymentLinkValidityMinutes
                : stored.PaymentLinkValidityMinutes,
            EnabledMethods = Prefer(configuration.EnabledMethods, stored.EnabledMethods),
            Notes = stored.Notes,
            CreatedAtUtc = stored.CreatedAtUtc,
            CreatedByUserId = stored.CreatedByUserId,
            UpdatedAtUtc = stored.UpdatedAtUtc,
            UpdatedByUserId = stored.UpdatedByUserId,
            Version = stored.Version
        };

    /// <summary>
    /// The configured value where there is one, and the stored one where there is not.
    ///
    /// ONE METHOD RATHER THAN A NULLABLE AND A NON-NULLABLE OVERLOAD: nullability annotations do
    /// not distinguish overloads, so the two would be the same signature. The three non-nullable
    /// properties coalesce at their own call sites instead.
    /// </summary>
    private static string? Prefer(string? configured, string? stored) =>
        string.IsNullOrWhiteSpace(configured) ? stored : configured;

    /// <summary>
    /// An account built from the configuration alone, for an Organisation with no stored row.
    ///
    /// NOT TRACKED AND NEVER SAVED. It exists for the length of one request, to give the gateway
    /// adapters the shape they expect.
    /// </summary>
    private static PaymentGatewayAccount Synthesise(TenantGatewayConfiguration configuration) =>
        new()
        {
            Id = configuration.Id,
            TenantId = configuration.TenantId,
            BusinessUnitId = configuration.BusinessUnitId,
            GatewayName = configuration.Provider,
            MerchantId = configuration.MerchantId ?? string.Empty,
            ApiKeyReference = TenantConfiguredCredentialResolver.ReferenceFor(configuration),
            WebhookSecretReference = null,

            // NOT DERIVED FROM THE ENVIRONMENT LABEL, deliberately. PAY's own repository filters
            // test-mode rows OUT of the live path, so marking a Sandbox configuration as test
            // mode here would make it unusable - and an Organisation whose only configuration is
            // a sandbox one, which is every Organisation during setup, would take no payments at
            // all. Test versus live is decided by the KEY, as the deployment guide already says.
            IsTestMode = false,
            IsActive = true,
            SettlementCurrencyCode = string.IsNullOrWhiteSpace(configuration.SettlementCurrencyCode)
                ? "INR"
                : configuration.SettlementCurrencyCode,
            ReturnUrl = configuration.ReturnUrl,
            WebhookUrl = configuration.WebhookUrl,
            PaymentLinkValidityMinutes = configuration.PaymentLinkValidityMinutes > 0
                ? configuration.PaymentLinkValidityMinutes
                : 60,
            EnabledMethods = configuration.EnabledMethods,
            Notes = $"From the {configuration.Environment} gateway configuration "
                    + $"({configuration.DisplayName ?? configuration.Provider}).",
            CreatedAtUtc = configuration.CreatedAtUtc
        };
}

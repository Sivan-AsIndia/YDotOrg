using Microsoft.EntityFrameworkCore;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Mappings;
using YDot.IAM.Domain.Entities.Configuration;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Persistence.ReadServices;

/// <summary>
/// The grids behind the payment gateway configuration screen.
///
/// <c>IgnoreQueryFilters</c> IS USED THROUGHOUT AND THE SCOPE IS APPLIED BY HAND. That looks
/// backwards for a read service and is not: the screen serves a root user who reads across
/// Organisations as well as a TenantAdmin who reads one, so the filter cannot express the
/// question. What replaces it is a <c>tenantId</c> that the QUERY HANDLER resolved from the
/// caller's scope - never from the request - and that this class applies before any other
/// predicate. A null reaches here only for a root user.
///
/// THE ORGANISATION NAME IS JOINED IN because a root user's list is unusable without it: five
/// rows all saying "Razorpay / Production" and differing only in a GUID is not a screen anybody
/// can work from.
///
/// NO CREDENTIAL COLUMN IS SELECTED. The projections build the response DTO, which has nowhere
/// to put one - see the mapping.
/// </summary>
public sealed class PaymentGatewayConfigurationReadService(IamDbContext context)
    : IPaymentGatewayConfigurationReadService
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResponse<PaymentGatewayConfigurationResponse>> SearchAsync(
        PaymentGatewayConfigurationFilter filter,
        Guid? tenantId,
        Func<bool, IReadOnlyList<string>> permittedActions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(permittedActions);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);

        var organisations = await OrganisationNamesAsync(tenantId, cancellationToken);

        var configured = await ConfiguredRowsAsync(
            filter, tenantId, permittedActions, organisations, cancellationToken);

        var deployment = await DeploymentRowsAsync(
            filter, tenantId, configured, organisations, cancellationToken);

        // MERGED AND PAGED IN MEMORY, which is worth justifying because it is normally the wrong
        // shape. Two things make it right here:
        //
        //   The two sets live in different services' tables, with different columns and different
        //   meanings, so there is no single query that returns them - and a UNION over columns
        //   that only half line up would be worse than either.
        //
        //   The volume is bounded by (organisations x providers x environments): a handful of rows
        //   per organisation, a few dozen platform-wide. Both queries are indexed and neither
        //   returns a credential.
        //
        // If a deployment ever grows to thousands of organisations this becomes a real query.
        // Nothing about the response shape would have to change.
        var all = configured
            .Concat(deployment)

            // WHAT IS TAKING MONEY TODAY, FIRST. Then production before sandbox, then the rows
            // entered on this screen before the ones inherited from the deployment - because a
            // superseded row at the top of the list is an invitation to edit the wrong one.
            .OrderByDescending(row => row.IsActive && !row.IsSuperseded)
            .ThenByDescending(row => row.Environment == nameof(PaymentGatewayEnvironment.Production))
            .ThenBy(row => row.Source == ConfigurationSources.Deployment)
            .ThenBy(row => row.OrganisationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Provider, StringComparer.Ordinal)
            .ToList();

        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResponse<PaymentGatewayConfigurationResponse>(
            items, all.Count, page, pageSize);
    }

    /// <summary>The rows entered on this screen.</summary>
    private async Task<List<PaymentGatewayConfigurationResponse>> ConfiguredRowsAsync(
        PaymentGatewayConfigurationFilter filter,
        Guid? tenantId,
        Func<bool, IReadOnlyList<string>> permittedActions,
        IReadOnlyDictionary<Guid, OrganisationLabel> organisations,
        CancellationToken cancellationToken)
    {
        var query = Scoped(tenantId);

        if (filter.Provider is { } provider)
        {
            query = query.Where(configuration => configuration.Provider == provider);
        }

        if (filter.Environment is { } environment)
        {
            query = query.Where(configuration => configuration.Environment == environment);
        }

        if (filter.IsActive is { } isActive)
        {
            query = query.Where(configuration => configuration.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Lower-cased Contains, matching every other read service in this assembly. ILike
            // would read better and would be the only Npgsql-specific expression in the layer -
            // one idiom for "search" is worth more than one tidier query.
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(configuration =>
                (configuration.DisplayName != null && configuration.DisplayName.ToLower().Contains(term))
                || (configuration.MerchantId != null && configuration.MerchantId.ToLower().Contains(term)));
        }

        var rows = await query.ToListAsync(cancellationToken);

        return [.. rows.Select(row => row.ToResponse(
            NameOf(organisations, row.TenantId),
            CodeOf(organisations, row.TenantId),
            permittedActions(row.IsActive)))];
    }

    /// <summary>
    /// The gateway rows PAY built from the deployment's own environment.
    ///
    /// WITHOUT THESE THE SCREEN LIES BY OMISSION. Every organisation on this platform already had
    /// a gateway before the configuration screen existed - seeded from the keys in the
    /// environment - and those rows are what takes donations today. A list showing only its own
    /// rows opens on "No payment gateway is configured" for an organisation whose donations are
    /// working perfectly, which invites somebody to set up a second gateway to fix nothing.
    ///
    /// SUPERSEDED RATHER THAN HIDDEN once an organisation configures its own active gateway. PAY
    /// lays the configuration over the deployment account at the moment of payment, so the
    /// deployment row genuinely stops deciding where the money goes - but it still exists, and an
    /// operator asking why the merchant account changed needs to be able to see that.
    /// </summary>
    private async Task<List<PaymentGatewayConfigurationResponse>> DeploymentRowsAsync(
        PaymentGatewayConfigurationFilter filter,
        Guid? tenantId,
        IReadOnlyCollection<PaymentGatewayConfigurationResponse> configured,
        IReadOnlyDictionary<Guid, OrganisationLabel> organisations,
        CancellationToken cancellationToken)
    {
        // "Inactive" cannot match one of these. An inactive pay_gateway_accounts row takes no
        // donations and is not something this screen can act on either, so there is nothing
        // useful to show.
        if (filter.IsActive == false)
        {
            return [];
        }

        var query = context.DeploymentGatewayAccounts
            .AsNoTracking()
            .Where(account => account.IsActive);

        if (tenantId is { } scoped)
        {
            query = query.Where(account => account.TenantId == scoped);
        }

        if (filter.Provider is { } provider)
        {
            var providerName = provider.ToString();
            query = query.Where(account => account.GatewayName == providerName);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();

            query = query.Where(account =>
                account.MerchantId.ToLower().Contains(term)
                || account.GatewayName.ToLower().Contains(term));
        }

        var accounts = await query.ToListAsync(cancellationToken);

        // Which organisations have already taken their gateway over. Computed once, not per row.
        var supersededTenants = configured
            .Where(row => row.IsActive)
            .Select(row => row.TenantId)
            .ToHashSet();

        var results = new List<PaymentGatewayConfigurationResponse>(accounts.Count);

        foreach (var account in accounts)
        {
            var environment = account.IsTestMode
                ? PaymentGatewayEnvironment.Sandbox
                : PaymentGatewayEnvironment.Production;

            // The environment filter is applied here rather than in SQL, because what PAY stores
            // is a test-mode flag and what this screen speaks is Sandbox/Production.
            if (filter.Environment is { } wanted && wanted != environment)
            {
                continue;
            }

            results.Add(ToDeploymentResponse(
                account,
                environment,
                supersededTenants.Contains(account.TenantId),
                NameOf(organisations, account.TenantId),
                CodeOf(organisations, account.TenantId)));
        }

        return results;
    }

    /// <summary>
    /// A deployment account in the shape the list renders.
    ///
    /// <c>PermittedActions</c> IS EMPTY, ALWAYS. Its credentials live in the deployment's
    /// environment: this service cannot read them, cannot test with them and cannot change them.
    /// The way to take one over is to configure your own, which supersedes it - and the screen
    /// says exactly that rather than offering buttons that would fail.
    /// </summary>
    private static PaymentGatewayConfigurationResponse ToDeploymentResponse(
        Configuration.DeploymentGatewayAccount account,
        PaymentGatewayEnvironment environment,
        bool isSuperseded,
        string? organisationName,
        string? organisationCode)
    {
        // AN UNRECOGNISED NAME FALLS BACK rather than failing. PAY's own router does the same
        // thing with the same column, and a list that threw on a provider name somebody spelled
        // differently would take the whole screen down over one row.
        var provider = Enum.TryParse<PaymentGatewayProvider>(account.GatewayName, true, out var parsed)
            ? parsed
            : PaymentGatewayProvider.HostedCheckout;

        var descriptor = PaymentGatewayCatalogue.Find(provider);

        return new PaymentGatewayConfigurationResponse(
            account.Id,
            account.TenantId,
            organisationName,
            organisationCode,
            provider.ToString(),
            descriptor?.Name ?? account.GatewayName,
            environment.ToString(),
            DisplayName: null,
            account.MerchantId,

            // NOT A MASKED KEY - there is no key here to mask. What identifies these credentials
            // is the NAME of the configuration section they are deployed under, and that travels
            // in DeploymentKeyReference at the end.
            ApiKeyHint: null,
            HasApiKey: !string.IsNullOrWhiteSpace(account.ApiKeyReference),
            HasSecretKey: !string.IsNullOrWhiteSpace(account.ApiKeyReference),
            account.WebhookUrl,
            HasWebhookSecret: !string.IsNullOrWhiteSpace(account.WebhookSecretReference),
            SubscribedEvents: [],
            account.SettlementCurrencyCode.Trim(),
            account.ReturnUrl,
            account.PaymentLinkValidityMinutes,
            PaymentGatewayMappingConfig.Split(account.EnabledMethods),
            account.IsActive,
            descriptor?.HasAdapter ?? false,
            LastTestedAtUtc: null,
            LastTestSucceeded: null,
            LastTestMessage: null,
            account.Notes,
            account.CreatedAtUtc,
            account.UpdatedAtUtc,
            account.Version,
            PermittedActions: [],
            ConfigurationSources.Deployment,
            isSuperseded,
            account.ApiKeyReference);
    }

    /// <summary>An organisation's display name and code, for the rows that name one.</summary>
    private readonly record struct OrganisationLabel(string Name, string Code);

    /// <summary>
    /// Organisation names for every row the page might show, in ONE query.
    ///
    /// A CORRELATED SUBQUERY PER ROW WAS WHAT THIS REPLACED, and it was wrong twice over: the
    /// merged list is built from two tables, so the subquery would have had to be written twice;
    /// and a root user's page issued two extra reads per row for a lookup that resolves the same
    /// handful of organisations every time.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, OrganisationLabel>> OrganisationNamesAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        var query = context.Tenants.IgnoreQueryFilters().AsNoTracking();

        if (tenantId is { } scoped)
        {
            query = query.Where(tenant => tenant.Id == scoped);
        }

        var rows = await query
            .Select(tenant => new { tenant.Id, tenant.Name, tenant.Code })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => new OrganisationLabel(row.Name, row.Code));
    }

    private static string? NameOf(
        IReadOnlyDictionary<Guid, OrganisationLabel> organisations, Guid tenantId) =>
        organisations.TryGetValue(tenantId, out var found) ? found.Name : null;

    private static string? CodeOf(
        IReadOnlyDictionary<Guid, OrganisationLabel> organisations, Guid tenantId) =>
        organisations.TryGetValue(tenantId, out var found) ? found.Code : null;

    public async Task<PaymentGatewayConfigurationResponse?> GetAsync(
        Guid id,
        Guid? tenantId,
        Func<bool, IReadOnlyList<string>> permittedActions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permittedActions);

        var configuration = await Scoped(tenantId)
            .FirstOrDefaultAsync(row => row.Id == id, cancellationToken);

        if (configuration is null)
        {
            return null;
        }

        var organisations = await OrganisationNamesAsync(configuration.TenantId, cancellationToken);

        return configuration.ToResponse(
            NameOf(organisations, configuration.TenantId),
            CodeOf(organisations, configuration.TenantId),
            permittedActions(configuration.IsActive));
    }

    public async Task<PagedResponse<PaymentGatewayConfigurationAuditResponse>> SearchAuditAsync(
        PaymentGatewayAuditFilter filter, Guid? tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);

        var query = context.PaymentGatewayConfigurationAudits.IgnoreQueryFilters().AsNoTracking();

        if (tenantId is { } scopedTenant)
        {
            query = query.Where(entry => entry.TenantId == scopedTenant);
        }

        if (filter.ConfigurationId is { } configurationId && configurationId != Guid.Empty)
        {
            query = query.Where(entry => entry.ConfigurationId == configurationId);
        }

        if (filter.Action is { } action)
        {
            query = query.Where(entry => entry.Action == action);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(entry => entry.OccurredAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(entry => entry.OccurredAtUtc <= to);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // NEWEST FIRST. Somebody opens this panel because something stopped working today.
        var rows = await query
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var organisations = await OrganisationNamesAsync(tenantId, cancellationToken);

        var items = rows
            .Select(entry => entry.ToResponse(NameOf(organisations, entry.TenantId)))
            .ToList();

        return new PagedResponse<PaymentGatewayConfigurationAuditResponse>(
            items, totalCount, page, pageSize);
    }

    /// <summary>
    /// The base query with the caller's scope already applied.
    ///
    /// ONE PLACE, so a filter added to a method below cannot accidentally become the FIRST
    /// predicate and leave the scope off. Null is a root user reading every Organisation, which
    /// the query handler is the only thing that can produce.
    /// </summary>
    private IQueryable<PaymentGatewayConfiguration> Scoped(Guid? tenantId)
    {
        var query = context.PaymentGatewayConfigurations.IgnoreQueryFilters().AsNoTracking();

        return tenantId is { } scoped
            ? query.Where(configuration => configuration.TenantId == scoped)
            : query;
    }
}

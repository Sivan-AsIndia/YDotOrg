using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Persistence;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.Common.Settings;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Mappings;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways.Queries;

/// <summary>What the form needs before a provider has been chosen.</summary>
public sealed record GetPaymentGatewayCatalogueQuery;

/// <summary>The configurations in scope, filtered and paged.</summary>
public sealed record SearchPaymentGatewayConfigurationsQuery(PaymentGatewayConfigurationFilter Filter);

/// <summary>One configuration.</summary>
public sealed record GetPaymentGatewayConfigurationQuery(Guid ConfigurationId);

/// <summary>The change log, filtered and paged.</summary>
public sealed record SearchPaymentGatewayAuditQuery(PaymentGatewayAuditFilter Filter);

/// <summary>
/// The read side of the payment gateway configuration screen.
///
/// EVERY QUERY IS SCOPED THROUGH <see cref="PaymentGatewayScope"/> AND NOT THROUGH THE FILTER
/// THE CALLER SENT. A TenantAdmin who edits <c>?tenantId=</c> in the query string gets their own
/// Organisation's rows, because the resolved scope replaces the value rather than validating it.
/// Validating it would mean answering "no" to a question that reveals the row exists.
///
/// NOTHING HERE RETURNS A CREDENTIAL, and there is no permission that makes it. The projections
/// in the read service have no field to put one in.
/// </summary>
public sealed class PaymentGatewayConfigurationQueryHandler(
    IPaymentGatewayConfigurationReadService reader,
    PaymentGatewayScope scope,
    IOptions<ClientAppSettings> clientSettings)
{
    /// <summary>
    /// The providers, events and methods the form offers, plus the webhook URL to paste into the
    /// provider's dashboard.
    ///
    /// THE SUGGESTED URL IS BUILT, NOT GUESSED. It comes from the deployment's configured public
    /// address, so an operator copying it gets one that actually resolves - as opposed to the
    /// usual outcome, which is somebody typing localhost into Razorpay's dashboard and wondering
    /// for a day why no webhook ever arrives.
    /// </summary>
    public Task<Result<PaymentGatewayCatalogueResponse>> HandleAsync(
        GetPaymentGatewayCatalogueQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        _ = cancellationToken;

        return Task.FromResult<Result<PaymentGatewayCatalogueResponse>>(
            PaymentGatewayMappingConfig.ToCatalogueResponse(SuggestedWebhookUrl()));
    }

    public async Task<Result<PagedResponse<PaymentGatewayConfigurationResponse>>> HandleAsync(
        SearchPaymentGatewayConfigurationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenantId = scope.ResolveReadTenant(query.Filter.TenantId);

        // A non-root caller with no resolved Organisation has nothing to show, and returning an
        // empty page says so without an error - there is no fault here, only no context yet.
        if (tenantId is null && !scope.CanReadAllOrganisations)
        {
            return PagedResponse<PaymentGatewayConfigurationResponse>.Empty(
                query.Filter.Page, query.Filter.PageSize);
        }

        return await reader.SearchAsync(
            query.Filter, tenantId, scope.PermittedActions, cancellationToken);
    }

    public async Task<Result<PaymentGatewayConfigurationResponse>> HandleAsync(
        GetPaymentGatewayConfigurationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // NULL MEANS "EVERY ORGANISATION" TO THE READ SERVICE, so it may only ever be passed for
        // a root user. A TenantAdmin whose Organisation has not resolved would otherwise fetch
        // ACROSS Organisations by id - the one place in this feature where an unresolved context
        // could turn into a cross-tenant read rather than an empty list.
        Guid? tenantId;

        if (scope.CanReadAllOrganisations)
        {
            tenantId = null;
        }
        else
        {
            tenantId = scope.ResolveReadTenant(null);

            if (tenantId is null)
            {
                return Result.Failure<PaymentGatewayConfigurationResponse>(
                    Error.NotFound("That gateway configuration was not found."));
            }
        }

        var configuration = await reader.GetAsync(
            query.ConfigurationId, tenantId, scope.PermittedActions, cancellationToken);

        return configuration is null
            ? Result.Failure<PaymentGatewayConfigurationResponse>(
                Error.NotFound("That gateway configuration was not found."))
            : configuration;
    }

    public async Task<Result<PagedResponse<PaymentGatewayConfigurationAuditResponse>>> HandleAsync(
        SearchPaymentGatewayAuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tenantId = scope.ResolveReadTenant(query.Filter.TenantId);

        if (tenantId is null && !scope.CanReadAllOrganisations)
        {
            return PagedResponse<PaymentGatewayConfigurationAuditResponse>.Empty(
                query.Filter.Page, query.Filter.PageSize);
        }

        return await reader.SearchAuditAsync(query.Filter, tenantId, cancellationToken);
    }

    /// <summary>
    /// Where this deployment expects a provider to post its webhooks.
    ///
    /// PAY OWNS THE ENDPOINT, NOT IAM, which is why this is built from the public base address
    /// rather than from the current request: the address an operator has to paste points at the
    /// payments service, and the request that asked for it arrived at identity.
    ///
    /// THE PROVIDER IS IN THE PATH, and that is not decoration. PAY reads it from the ROUTE to
    /// decide which signing secret to check the signature against - taking the provider name out
    /// of an unverified body instead would let a forger choose which secret we verify with.
    /// </summary>
    private string? SuggestedWebhookUrl()
    {
        var baseUrl = clientSettings.Value.BaseUrl;

        return string.IsNullOrWhiteSpace(baseUrl)
            ? null
            : $"{baseUrl.TrimEnd('/')}/pay-api/webhooks/{{provider}}";
    }
}

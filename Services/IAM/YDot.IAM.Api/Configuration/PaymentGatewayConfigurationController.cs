using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.IAM.Api.Controllers;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Application.Common.Results;
using YDot.IAM.Application.DTOs;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Commands.ManagePaymentGatewayConfiguration;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.DTOs;
using YDot.IAM.Application.Features.Configuration.PaymentGateways.Queries;
using YDot.IAM.Infrastructure.Authorization;

namespace YDot.IAM.Api.Configuration;

/// <summary>
/// Where an Organisation says which payment gateway takes its donations, and with which
/// credentials.
///
/// WHO REACHES IT. SUPERADMIN and TENANTADMIN, and nobody else. The four permission codes behind
/// these endpoints are in <c>RoleAccessProfiles.AdministratorOnlyCodes</c>, so INITIATOR and
/// APPROVER never carry one however an Organisation configures its roles. A root user may read
/// and write any Organisation's configuration but must name which; a TenantAdmin's Organisation
/// comes from their token and a TenantId in a request body is ignored.
///
/// NO SECRET LEAVES THESE ENDPOINTS. In: the plaintext credential, once, over TLS, sealed before
/// it reaches a column. Out: a four-character hint and a has-a-secret flag, never the value and
/// never the ciphertext. A merchant secret in a response ends up in the browser's memory and its
/// dev tools; in a request log it ends up in a proxy buffer and an exception message, which is
/// why the credential fields never appear in a query string or a route.
///
/// EVERY WRITE IS AUDITED TWICE - once to the configuration's own change log, which is the
/// per-field before-and-after this screen renders, and once to the platform audit trail. Both
/// are written in the same transaction as the change.
/// </summary>
[Route("api/v1/configuration/payment-gateways")]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Produces("application/json")]
public sealed class PaymentGatewayConfigurationController(
    PaymentGatewayConfigurationCommandHandler commands,
    PaymentGatewayConfigurationQueryHandler queries) : ApiControllerBase
{
    /// <summary>
    /// The providers, webhook events and payment methods the form offers.
    ///
    /// SERVED FROM HERE RATHER THAN COMPILED INTO THE ANGULAR BUNDLE because the half that
    /// matters - whether PAY has an adapter that speaks a provider's own API - is a fact about
    /// the deployed back end. A copy in the client goes stale the first time an adapter ships,
    /// and then either hides a provider that works or promises one that does not.
    /// </summary>
    [HttpGet("catalogue")]
    [HasPermission(PermissionCodes.PaymentGatewaysView)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentGatewayCatalogueResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalogueAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetPaymentGatewayCatalogueQuery(), cancellationToken));

    /// <summary>
    /// The configurations this caller may see.
    ///
    /// A TenantAdmin gets their own Organisation's, whatever <c>tenantId</c> says - the scope
    /// REPLACES the filter value rather than validating it, because validating would mean
    /// answering "no" to a question that reveals the row exists.
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.PaymentGatewaysView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<PaymentGatewayConfigurationResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] PaymentGatewayConfigurationFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new SearchPaymentGatewayConfigurationsQuery(filter), cancellationToken));

    [HttpGet("{id:guid}", Name = "GetPaymentGatewayConfiguration")]
    [HasPermission(PermissionCodes.PaymentGatewaysView)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentGatewayConfigurationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new GetPaymentGatewayConfigurationQuery(id), cancellationToken));

    /// <summary>
    /// Creates or updates a configuration.
    ///
    /// AN UPSERT ON ONE VERB, because the natural key is (Organisation, provider, environment)
    /// rather than an id the browser holds. A screen that had to know whether a row already
    /// existed would have to ask first, and the answer could change between the two calls.
    ///
    /// <c>expectedVersion</c> IS WHAT SEPARATES THE TWO. Absent means create; present means
    /// "update this version of it", and a stale one answers 409 rather than overwriting somebody
    /// else's change - which on this screen would mean silently re-pointing an Organisation's
    /// settlement account.
    /// </summary>
    [HttpPut]
    [HasPermission(PermissionCodes.PaymentGatewaysManage)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentGatewayConfigurationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertAsync(
        [FromBody] UpsertPaymentGatewayConfigurationRequest request,
        CancellationToken cancellationToken) =>
        FromResult(
            await commands.HandleAsync(
                new UpsertPaymentGatewayConfigurationCommand(request), cancellationToken),
            "Payment gateway configuration saved.");

    /// <summary>
    /// Turns a configuration on or off.
    ///
    /// ACTIVATING ONE STANDS THE OTHERS IN THE SAME ENVIRONMENT DOWN, in the same transaction.
    /// Two active configurations would make "the active gateway" an arbitrary choice, and for a
    /// settlement account that means donations landing in whichever merchant account the
    /// database happened to order first.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [HasPermission(PermissionCodes.PaymentGatewaysManage)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeStatusAsync(
        Guid id,
        [FromBody] ChangePaymentGatewayStatusRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new ChangePaymentGatewayStatusCommand(id, request), cancellationToken));

    /// <summary>
    /// Reaches the provider with the stored credentials.
    ///
    /// IT DOES NOT MOVE MONEY. For Razorpay it creates a one-rupee ORDER - the same first call
    /// the donation path makes, which reserves nothing and charges nobody - and fails in exactly
    /// the ways a misconfigured merchant account fails. The result is stored on the row whether
    /// it passed or failed, because "last tested: failed, three weeks ago" is what somebody needs
    /// to see when donations stop working.
    ///
    /// A SEPARATE PERMISSION FROM MANAGE, so an administrator can be given the ability to
    /// diagnose a gateway without the ability to re-point it.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    [HasPermission(PermissionCodes.PaymentGatewaysTest)]
    [ProducesResponseType(
        typeof(ApiResponse<PaymentGatewayTestResultResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new TestPaymentGatewayConfigurationCommand(id), cancellationToken));

    /// <summary>
    /// Deletes a configuration.
    ///
    /// REFUSED WHILE IT IS THE ACTIVE ONE. Deleting the row donations are currently flowing
    /// through stops every payment for that Organisation the moment it commits, so it is a
    /// two-step: stand it down, which is visible and reversible and has its own audit row, then
    /// delete it. A reason is required, and the change log outlives the row.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(PermissionCodes.PaymentGatewaysDelete)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        [FromBody] DeletePaymentGatewayConfigurationRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await commands.HandleAsync(
            new DeletePaymentGatewayConfigurationCommand(id, request), cancellationToken));

    /// <summary>
    /// The change log: who changed what, when, from what to what.
    ///
    /// READ-ONLY, like the platform trail, and for the same reason - a log that can be corrected
    /// is not evidence of anything. Credentials are masked ON WRITE, so no permission can reveal
    /// one here: what a credential row records is "set", "changed" or "cleared" and the
    /// four-character hint.
    /// </summary>
    [HttpGet("audit")]
    [HasPermission(PermissionCodes.PaymentGatewaysView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<PaymentGatewayConfigurationAuditResponse>>),
        StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAuditAsync(
        [FromQuery] PaymentGatewayAuditFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(
            new SearchPaymentGatewayAuditQuery(filter), cancellationToken));
}

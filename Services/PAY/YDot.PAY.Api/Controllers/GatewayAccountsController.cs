using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Gateway.Commands.ManageGatewayAccount;
using YDot.PAY.Application.Features.Gateway.DTOs;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// Each organisation's payment gateway configuration.
///
/// THIS IS WHERE THE MODULE'S TENANCY STOPS BEING A DATA QUESTION AND BECOMES A LEGAL ONE. Every
/// charity collects into its OWN merchant account; a shared one would pool several organisations'
/// income into a single settlement, which no amount of correct reporting afterwards would fix.
///
/// NO SECRET PASSES THROUGH THESE ENDPOINTS, in either direction. The request carries a
/// REFERENCE to a key already placed in the secret store, and the response says only whether a
/// key is configured. A merchant secret in a request body ends up in a request log, a proxy
/// buffer and an exception message; in a response it ends up in the browser's memory and its dev
/// tools.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/gateway-accounts")]
[Produces("application/json")]
public sealed class GatewayAccountsController(GatewayAccountCommandHandler accounts)
    : ApiControllerBase
{
    /// <summary>
    /// Every account this organisation holds, live and test alike.
    ///
    /// BOTH ARE SHOWN, with the test flag prominent on each. Hiding test accounts would leave an
    /// operator unable to see why donations are not reaching their bank; showing them
    /// indistinguishably is how an organisation ends up reporting income it never received.
    /// </summary>
    [HttpGet]
    [HasPermission(PermissionCodes.GatewayView)]
    [ProducesResponseType(
        typeof(ApiResponse<IReadOnlyList<GatewayAccountResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken) =>
        FromResult(await accounts.HandleAsync(new GetGatewayAccountsQuery(), cancellationToken));

    /// <summary>
    /// Creates or updates the account for a gateway and mode.
    ///
    /// AN UPSERT RATHER THAN SEPARATE POST AND PUT, because the natural key is (organisation,
    /// gateway, test mode) rather than an id the caller holds. A configuration screen that had to
    /// know whether a row already existed would have to ask first, and the answer could change
    /// between the two calls.
    ///
    /// <c>ExpectedVersion</c> is optional precisely because of that: absent means "create", and
    /// present means "update this version of it".
    /// </summary>
    [HttpPut]
    [HasPermission(PermissionCodes.GatewayManage)]
    [ProducesResponseType(typeof(ApiResponse<GatewayAccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertAsync(
        [FromBody] UpsertGatewayAccountRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await accounts.HandleAsync(new UpsertGatewayAccountCommand(request), cancellationToken),
            "Gateway account saved.");
}

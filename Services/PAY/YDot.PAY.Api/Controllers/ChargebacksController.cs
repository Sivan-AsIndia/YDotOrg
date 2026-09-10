using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Refunds.Commands.ManageChargeback;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Application.Features.Refunds.Queries;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// The chargeback register - SCR-PAY-008.
///
/// UNLIKE A REFUND, NONE OF THIS IS OUR DECISION. The donor's bank has already taken the money
/// back; the case exists so somebody can CONTEST it before the evidence deadline, and after that
/// deadline nothing anybody does here changes the outcome.
///
/// THAT IS WHY THE DEFAULT ORDERING IS BY URGENCY rather than by date, and why the list carries a
/// server-computed <c>DaysUntilEvidenceDue</c>. A case with two days left matters more than one
/// opened this morning with thirty, and leaving each client to work that out means they
/// eventually disagree about which is which.
///
/// THERE IS NO ENDPOINT TO OPEN A CASE BY HAND. Chargebacks arrive from the gateway, through the
/// webhook and the event queue. A hand-created case would be an assertion that a bank did
/// something, with nothing to corroborate it.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/chargebacks")]
[Produces("application/json")]
public sealed class ChargebacksController(
    RefundQueryHandler queries,
    ChargebackCommandHandler chargebacks,
    ILogger<ChargebacksController> logger) : ApiControllerBase
{
    /// <summary>The chargeback register, most urgent first.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.ChargebacksView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<ChargebackCaseListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] ChargebackSearchFilter filter, CancellationToken cancellationToken)
    {
        logger.LogInformation("Searching chargeback cases.");

        var result = await queries.HandleAsync(new SearchChargebacksQuery(filter), cancellationToken);

        logger.LogInformation("Chargeback case search completed successfully.");

        return FromResult(result);
    }

    /// <summary>One chargeback case in full, with its evidence.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetChargebackAsync))]
    [HasPermission(PermissionCodes.ChargebacksView)]
    [ProducesResponseType(typeof(ApiResponse<ChargebackCaseDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChargebackAsync(
        Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting chargeback case details.");

        var result = await queries.HandleAsync(new GetChargebackQuery(id), cancellationToken);

        logger.LogInformation("Chargeback case detail request completed.");

        return FromResult(result);
    }

    /// <summary>
    /// Assigns the case to somebody.
    ///
    /// A CASE WITH NO OWNER IS ONE NOBODY WORKS, and this is the module where that has a deadline
    /// attached to it.
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    [HasPermission(PermissionCodes.ChargebacksAssign)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignAsync(
        Guid id, [FromBody] AssignChargebackRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Assign chargeback action started.");

        var result = await chargebacks.HandleAsync(
            new AssignChargebackCommand(id, request), cancellationToken);

        logger.LogInformation("Assign chargeback action completed.");

        return FromResult(result, "Chargeback assigned.");
    }

    /// <summary>
    /// Submits evidence contesting the chargeback.
    ///
    /// REFUSED ONCE THE DEADLINE HAS PASSED, rather than accepted and quietly ignored. Letting
    /// somebody file evidence the bank will never look at leaves them believing the case is
    /// contested when it is already lost.
    /// </summary>
    [HttpPost("{id:guid}/evidence")]
    [HasPermission(PermissionCodes.ChargebacksSubmitEvidence)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitEvidenceAsync(
        Guid id,
        [FromBody] SubmitChargebackEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Submit chargeback evidence action started.");

        var result = await chargebacks.HandleAsync(
            new SubmitChargebackEvidenceCommand(id, request), cancellationToken);

        logger.LogInformation("Submit chargeback evidence action completed.");

        return FromResult(result, "Evidence submitted.");
    }

    /// <summary>
    /// Records the bank's decision, or concedes without contesting.
    ///
    /// A LOST CHARGEBACK MOVES THE DONATION TO ChargedBack and takes it out of the receiptable
    /// set - the money is gone, and a tax receipt for it would let the donor claim relief on a
    /// gift the charity never kept.
    /// </summary>
    [HttpPost("{id:guid}/resolve")]
    [HasPermission(PermissionCodes.ChargebacksResolve)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveAsync(
        Guid id, [FromBody] ResolveChargebackRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Resolve chargeback action started.");

        var result = await chargebacks.HandleAsync(
            new ResolveChargebackCommand(id, request), cancellationToken);

        logger.LogInformation("Resolve chargeback action completed.");

        return FromResult(result, "Chargeback resolved.");
    }
}
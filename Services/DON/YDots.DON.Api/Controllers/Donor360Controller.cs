using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Donor360.Commands.CreateIntent;
using YDots.DON.Application.Features.Donor360.DTOs;
using YDots.DON.Application.Features.Donor360.Queries.GetDonor360;
using YDots.DON.Application.Features.Donors.Commands.ManageDonor;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// SCR-DON-003 Donor 360. Unified identity, donations, communications, consent, tasks and history.
/// Route from the developer contract: /api/v1/donors/donor-360.
/// </summary>
[Route("api/v1/donors/donor-360")]
[Authorize]
public sealed class Donor360Controller : ApiControllerBase
{
    private readonly ILogger<Donor360Controller> _logger;

    public Donor360Controller(ILogger<Donor360Controller> logger)
    {
        _logger = logger;
    }

    /// <summary>GET the whole 360 view for one donor: thirteen panels in one call.</summary>
    [HttpGet("{donorId:guid}", Name = "GetDonor360")]
    [HasPermission(PermissionCodes.Donor360View)]
    [ProducesResponseType(typeof(ApiResponse<Donor360Response>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid donorId,
        [FromServices] Donor360QueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Donor 360 retrieval started. DonorId={DonorId}", donorId);

        var result = await handler.HandleAsync(new GetDonor360Query(donorId), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Donor 360 retrieval completed successfully. DonorId={DonorId}", donorId);
        }
        else
        {
            _logger.LogWarning("Donor 360 retrieval failed. DonorId={DonorId}", donorId);
        }

        return FromResult(result);
    }

    /// <summary>POST correct. Records a change to the donor with a mandatory reason.</summary>
    [HttpPost("{donorId:guid}/correct")]
    [HasPermission(PermissionCodes.Donor360Correct)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Correct(
        Guid donorId,
        [FromBody] CorrectDonorRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Donor correction started. DonorId={DonorId}", donorId);

        var result = await handler.HandleAsync(new CorrectDonorCommand(donorId, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Donor correction completed successfully. DonorId={DonorId}", donorId);
        }
        else
        {
            _logger.LogWarning("Donor correction failed. DonorId={DonorId}", donorId);
        }

        return FromResult(result, "The correction was recorded.");
    }

    /// <summary>
    /// POST create intent. Records a stated giving intention as an open promise.
    ///
    /// THIS IS A PLEDGE, NOT A PAYMENT REQUEST, and the name invites the opposite reading. It
    /// writes a DonorPromise (reference PRM-…) inside DON and calls nothing in PAY, so no
    /// DonationIntent is created and nothing appears on the Donation Intents screen. That is
    /// deliberate: somebody saying they intend to give is not the same event as asking them to
    /// pay, and a pledge that is never honoured must not sit in the payments ledger.
    ///
    /// A payment is started separately, by the donor, through the public donation endpoint. QA
    /// scenario DON-04 describes this endpoint as the DON-to-PAY hand-off; that description is
    /// wrong and the response message below now says plainly what was created.
    /// </summary>
    [HttpPost("{donorId:guid}/create-intent")]
    [HasPermission(PermissionCodes.Donor360CreateIntent)]
    [ProducesResponseType(typeof(ApiResponse<PromiseResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateIntent(
        Guid donorId,
        [FromBody] CreateIntentRequest request,
        [FromServices] CreateIntentCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Donor pledge creation started. DonorId={DonorId}", donorId);

        var result = await handler.HandleAsync(new CreateIntentCommand(donorId, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Donor pledge creation completed successfully. DonorId={DonorId}", donorId);
        }
        else
        {
            _logger.LogWarning("Donor pledge creation failed. DonorId={DonorId}", donorId);
        }

        return FromResult(result,
            "The pledge was recorded. It is not a payment request - the donor pays through their own donation link.");
    }

    /// <summary>DELETE an unused draft donor. Only for an unsubmitted Prospect with no history.</summary>
    [HttpDelete("{donorId:guid}")]
    [HasPermission(PermissionCodes.Donor360DeleteDraft)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDraft(
        Guid donorId,
        [FromBody] ReasonRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Draft donor deletion started. DonorId={DonorId}", donorId);

        var result = await handler.HandleAsync(new DeleteDonorDraftCommand(donorId, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Draft donor deletion completed successfully. DonorId={DonorId}", donorId);
        }
        else
        {
            _logger.LogWarning("Draft donor deletion failed. DonorId={DonorId}", donorId);
        }

        return FromResult(result);
    }
}
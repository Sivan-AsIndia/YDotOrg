using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.IdentityVerification.Commands.VerifyIdentity;
using YDots.DON.Application.Features.IdentityVerification.DTOs;
using YDots.DON.Application.Features.IdentityVerification.Queries.GetVerifications;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// DON-UI-07 Donor identity verification. Verify contact ownership and identity confidence
/// before a sensitive correction, a merge or portal access.
///
/// Nothing on this controller ever returns the challenge code or the unmasked destination. The
/// code goes to the donor; the screen only ever sees "+91******3210".
/// </summary>
[Route("api/v1/donors/donor-identity-verification")]
[Authorize]
public sealed class DonorIdentityVerificationController : ApiControllerBase
{
    private readonly ILogger<DonorIdentityVerificationController> _logger;

    public DonorIdentityVerificationController(ILogger<DonorIdentityVerificationController> logger)
    {
        _logger = logger;
    }

    /// <summary>GET the verification attempts plus every filter option.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.VerificationView)]
    [ProducesResponseType(typeof(ApiResponse<IdentityVerificationListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetList(
        [FromQuery] VerificationSearchFilter filter,
        [FromServices] IdentityVerificationQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Identity verification list retrieval started.");

        var result = await handler.HandleAsync(new GetVerificationListQuery(filter), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Identity verification list retrieval completed successfully.");
        }
        else
        {
            _logger.LogWarning("Identity verification list retrieval failed.");
        }

        return FromResult(result);
    }

    /// <summary>GET one verification attempt.</summary>
    [HttpGet("{id:guid}", Name = "GetVerificationById")]
    [HasPermission(PermissionCodes.VerificationView)]
    [ProducesResponseType(typeof(ApiResponse<IdentityVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] IdentityVerificationQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Identity verification detail retrieval started. VerificationId={VerificationId}", id);

        var result = await handler.HandleAsync(new GetVerificationDetailQuery(id), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Identity verification detail retrieval completed successfully. VerificationId={VerificationId}", id);
        }
        else
        {
            _logger.LogWarning("Identity verification detail retrieval failed. VerificationId={VerificationId}", id);
        }

        return FromResult(result);
    }

    /// <summary>
    /// POST send challenge. The primary action. Pressing it twice resends on the same attempt
    /// rather than opening a second competing challenge for the same donor.
    /// </summary>
    [HttpPost("send-challenge")]
    [HasPermission(PermissionCodes.VerificationSendChallenge)]
    [ProducesResponseType(typeof(ApiResponse<ChallengeSentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendChallenge(
        [FromBody] SendChallengeRequest request,
        [FromServices] IdentityVerificationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Identity verification challenge send started.");

        var result = await handler.HandleAsync(new SendChallengeCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Identity verification challenge sent successfully.");
        }
        else
        {
            _logger.LogWarning("Identity verification challenge send failed.");
        }

        return FromResult(result);
    }

    /// <summary>POST verify code. A wrong code costs an attempt; running out fails the verification.</summary>
    [HttpPost("{id:guid}/verify-code")]
    [HasPermission(PermissionCodes.VerificationVerifyCode)]
    [ProducesResponseType(typeof(ApiResponse<IdentityVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> VerifyCode(
        Guid id,
        [FromBody] VerifyCodeRequest request,
        [FromServices] IdentityVerificationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Identity verification code validation started. VerificationId={VerificationId}", id);

        var result = await handler.HandleAsync(new VerifyCodeCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Identity verification code validation completed successfully. VerificationId={VerificationId}", id);
        }
        else
        {
            _logger.LogWarning("Identity verification code validation failed. VerificationId={VerificationId}", id);
        }

        return FromResult(result, "The identity was verified.");
    }

    /// <summary>POST escalate review. Hands the attempt to a named reviewer with supporting evidence.</summary>
    [HttpPost("{id:guid}/escalate-review")]
    [HasPermission(PermissionCodes.VerificationEscalateReview)]
    [ProducesResponseType(typeof(ApiResponse<IdentityVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EscalateReview(
        Guid id,
        [FromBody] EscalateVerificationRequest request,
        [FromServices] IdentityVerificationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Identity verification review escalation started. VerificationId={VerificationId}", id);

        var result = await handler.HandleAsync(new EscalateVerificationCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Identity verification review escalation completed successfully. VerificationId={VerificationId}", id);
        }
        else
        {
            _logger.LogWarning("Identity verification review escalation failed. VerificationId={VerificationId}", id);
        }

        return FromResult(result, "The verification was escalated for review.");
    }

    /// <summary>POST cancel verification. Danger action: named reason required.</summary>
    [HttpPost("{id:guid}/cancel-verification")]
    [HasPermission(PermissionCodes.VerificationCancel)]
    [ProducesResponseType(typeof(ApiResponse<IdentityVerificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelVerification(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] IdentityVerificationCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Identity verification cancellation started. VerificationId={VerificationId}", id);

        var result = await handler.HandleAsync(new CancelVerificationCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Identity verification cancellation completed successfully. VerificationId={VerificationId}", id);
        }
        else
        {
            _logger.LogWarning("Identity verification cancellation failed. VerificationId={VerificationId}", id);
        }

        return FromResult(result, "The verification was cancelled.");
    }
}
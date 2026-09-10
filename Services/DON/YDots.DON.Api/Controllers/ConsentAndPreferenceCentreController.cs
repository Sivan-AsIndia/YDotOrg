using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.ConsentCentre.Commands.ManageConsent;
using YDots.DON.Application.Features.ConsentCentre.DTOs;
using YDots.DON.Application.Features.ConsentCentre.Queries.GetConsentCentre;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// SCR-DON-005 Consent and preference centre. Record notices, permissions, opt-outs and
/// public-recognition preference. Route: /api/v1/donors/consent-and-preference-centre.
///
/// There is no PUT here on purpose. Consent is append only: Grant supersedes, Withdraw closes
/// and Correct inserts a corrected copy, so the history can always be read back in order.
/// </summary>
[Route("api/v1/donors/consent-and-preference-centre")]
[Authorize]
public sealed class ConsentAndPreferenceCentreController : ApiControllerBase
{
    private readonly ILogger<ConsentAndPreferenceCentreController> _logger;

    public ConsentAndPreferenceCentreController(ILogger<ConsentAndPreferenceCentreController> logger)
    {
        _logger = logger;
    }

    /// <summary>GET the consent rows, the history for one donor and every catalogue.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.ConsentCentreView)]
    [ProducesResponseType(typeof(ApiResponse<ConsentCentreResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(
        [FromQuery] ConsentSearchFilter filter,
        [FromServices] ConsentCentreQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consent centre retrieval started.");

        var result = await handler.HandleAsync(new GetConsentCentreQuery(filter), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Consent centre retrieval completed successfully.");
        }
        else
        {
            _logger.LogWarning("Consent centre retrieval failed.");
        }

        return FromResult(result);
    }

    /// <summary>GET one consent row. Reading a confidential evidence reference is audited.</summary>
    [HttpGet("{id:guid}", Name = "GetConsentById")]
    [HasPermission(PermissionCodes.ConsentCentreView)]
    [ProducesResponseType(typeof(ApiResponse<ConsentListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] ConsentCentreQueryHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consent evidence retrieval started. ConsentId={ConsentId}", id);

        var result = await handler.HandleAsync(new GetConsentEvidenceQuery(id), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Consent evidence retrieval completed successfully. ConsentId={ConsentId}", id);
        }
        else
        {
            _logger.LogWarning("Consent evidence retrieval failed. ConsentId={ConsentId}", id);
        }

        return FromResult(result);
    }

    /// <summary>POST grant. Records a new permission and supersedes the previous row for that channel.</summary>
    [HttpPost("grant")]
    [HasPermission(PermissionCodes.ConsentCentreGrant)]
    [ProducesResponseType(typeof(ApiResponse<ConsentListItemResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Grant(
        [FromBody] GrantConsentRequest request,
        [FromServices] ConsentCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consent grant operation started.");

        var result = await handler.HandleAsync(new GrantConsentCommand(request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Consent grant operation completed successfully.");
        }
        else
        {
            _logger.LogWarning("Consent grant operation failed.");
        }

        return CreatedFromResult(result, "GetConsentById", new { id = result.Value?.Id ?? Guid.Empty },
            "The consent was recorded.");
    }

    /// <summary>POST withdraw. Closes the permission and, if it was the last one, sets Do not contact.</summary>
    [HttpPost("{id:guid}/withdraw")]
    [HasPermission(PermissionCodes.ConsentCentreWithdraw)]
    [ProducesResponseType(typeof(ApiResponse<ConsentListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(
        Guid id,
        [FromBody] WithdrawConsentRequest request,
        [FromServices] ConsentCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consent withdrawal operation started. ConsentId={ConsentId}", id);

        var result = await handler.HandleAsync(new WithdrawConsentCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Consent withdrawal operation completed successfully. ConsentId={ConsentId}", id);
        }
        else
        {
            _logger.LogWarning("Consent withdrawal operation failed. ConsentId={ConsentId}", id);
        }

        return FromResult(result, "The consent was withdrawn.");
    }

    /// <summary>POST correct. Supersedes the row with a corrected copy; the original is never edited.</summary>
    [HttpPost("{id:guid}/correct")]
    [HasPermission(PermissionCodes.ConsentCentreCorrect)]
    [ProducesResponseType(typeof(ApiResponse<ConsentListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Correct(
        Guid id,
        [FromBody] CorrectConsentRequest request,
        [FromServices] ConsentCommandHandler handler,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Consent correction operation started. ConsentId={ConsentId}", id);

        var result = await handler.HandleAsync(new CorrectConsentCommand(id, request), cancellationToken);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Consent correction operation completed successfully. ConsentId={ConsentId}", id);
        }
        else
        {
            _logger.LogWarning("Consent correction operation failed. ConsentId={ConsentId}", id);
        }

        return FromResult(result, "The correction was recorded and the previous row was superseded.");
    }
}
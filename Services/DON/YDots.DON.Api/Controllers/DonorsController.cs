using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;
using YDots.DON.Application.Common.Results;
using YDots.DON.Application.DTOs;
using YDots.DON.Application.Features.Donors.Commands.ManageDonor;
using YDots.DON.Application.Features.Donors.DTOs;
using YDots.DON.Application.Features.Donors.Queries.SearchDonors;
using YDots.DON.Infrastructure.Authorization;

namespace YDots.DON.Api.Controllers;

/// <summary>
/// The Donor resource: the eight endpoints in section 8 of the developer contract, plus the
/// lookup and the controlled export the screens need.
///
/// Route order matters here. The literal routes (/lookup, /export) are declared before
/// /{id:guid}, and the guid constraint is what keeps a word like "lookup" from being parsed as
/// an identifier.
/// </summary>
[Route("api/v1/donors")]
[Authorize]
public sealed class DonorsController : ApiControllerBase
{
    /// <summary>GET the donor list. View permission plus data scope.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.DonorsView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<DonorListItemResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] DonorSearchFilter filter,
        [FromServices] DonorQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new SearchDonorsQuery(filter), cancellationToken));

    /// <summary>GET the dropdown rows for the donor selectors on the other screens.</summary>
    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.DonorsView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DonorLookupResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Lookup(
        [FromQuery] string? search,
        [FromQuery] int maximumRows,
        [FromServices] DonorQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new LookupDonorsQuery(search, maximumRows), cancellationToken));

    /// <summary>GET a controlled CSV of the rows the caller can already see.</summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.DonorsExport)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] DonorSearchFilter filter,
        [FromServices] DonorQueryHandler handler,
        CancellationToken cancellationToken) =>
        FileFromResult(await handler.HandleAsync(new ExportDonorsQuery(filter), cancellationToken));

    /// <summary>GET one donor. View permission plus record scope.</summary>
    [HttpGet("{id:guid}", Name = "GetDonorById")]
    [HasPermission(PermissionCodes.DonorsView)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] DonorQueryHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new GetDonorDetailQuery(id), cancellationToken));

    /// <summary>POST a new donor. Create permission plus the duplicate check.</summary>
    [HttpPost]
    [HasPermission(PermissionCodes.DonorsCreate)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDonorRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateDonorCommand(request), cancellationToken);

        return CreatedFromResult(result, "GetDonorById", new { id = result.Value?.Id ?? Guid.Empty },
            "The donor record was created.");
    }

    /// <summary>PUT an edit. Edit permission plus expectedVersion.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.DonorsEdit)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDonorRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new UpdateDonorCommand(id, request), cancellationToken),
            "The donor record was updated.");

    /// <summary>POST submit. Moves the record to PendingApproval.</summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(PermissionCodes.DonorsSubmit)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(
        Guid id,
        [FromBody] TransitionRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new SubmitDonorCommand(id, request), cancellationToken),
            "The donor record was submitted for approval.");

    /// <summary>
    /// POST approve or reject. The creator is refused even when they hold the permission —
    /// the maker/checker rule is enforced inside the handler, not by the attribute.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.DonorsApprove)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] DecisionRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new ApproveDonorCommand(id, request), cancellationToken),
            request.Approved ? "The donor record was approved." : "The donor record was rejected.");

    /// <summary>POST cancel. The reason is mandatory; the record moves to Restricted, never away.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(PermissionCodes.DonorsCancel)]
    [ProducesResponseType(typeof(ApiResponse<DonorDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken) =>
        FromResult(await handler.HandleAsync(new CancelDonorCommand(id, request), cancellationToken),
            "The donor record was cancelled.");

    /// <summary>POST archive. Terminal state, so the contract returns 204 with no body.</summary>
    [HttpPost("{id:guid}/archive")]
    [HasPermission(PermissionCodes.DonorsArchive)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(
        Guid id,
        [FromBody] ReasonRequest request,
        [FromServices] DonorCommandHandler handler,
        CancellationToken cancellationToken) =>
        NoContentFromResult(await handler.HandleAsync(new ArchiveDonorCommand(id, request), cancellationToken));
}

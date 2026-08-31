using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Common.Results;
using YDot.PAY.Application.Features.Donations.Commands.ManageDonation;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Donations.Queries;
using YDot.PAY.Application.Features.Receipts.Commands.ManageReceipt;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Refunds.Commands.ManageRefund;
using YDot.PAY.Application.Features.Refunds.DTOs;
using YDot.PAY.Infrastructure.Authorization;

namespace YDot.PAY.Api.Controllers;

/// <summary>
/// The donation register - money that actually arrived.
///
/// THE ACTIONS THAT HANG OFF A DONATION LIVE HERE rather than on the receipt and refund
/// controllers, because they are things done TO a donation: issue its receipt, raise a refund
/// against it. The resulting record is then managed on its own controller. That split follows how
/// the screens work - an operator opens a donation and issues its receipt; they open the receipt
/// register to correct one.
/// </summary>
[ApiController]
[Authorize(Policy = PolicyNames.ActiveUserOnly)]
[Route("api/v1/donations")]
[Produces("application/json")]
public sealed class DonationsController(
    DonationQueryHandler queries,
    DonationCommandHandler donations,
    ReceiptCommandHandler receipts,
    RefundCommandHandler refunds) : ApiControllerBase
{
    /// <summary>The donation register.</summary>
    [HttpGet]
    [HasPermission(PermissionCodes.DonationsView)]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<DonationListItemResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] DonationSearchFilter filter, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new SearchDonationsQuery(filter), cancellationToken));

    /// <summary>
    /// Counts and totals for the register's summary tiles.
    ///
    /// The totals are reported in the Organisation's PREDOMINANT currency rather than summed
    /// across currencies - adding a rupee to a dollar is the one arithmetic a money total must
    /// never do, and a tile made of three currencies looks authoritative and means nothing.
    /// </summary>
    [HttpGet("statistics")]
    [HasPermission(PermissionCodes.DonationsView)]
    [ProducesResponseType(typeof(ApiResponse<DonationStatisticsResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatisticsAsync(CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetDonationStatisticsQuery(), cancellationToken));

    /// <summary>
    /// The CSV export.
    ///
    /// THE DONOR COLUMNS ARE MASKED IN THE FILE unless the caller holds the sensitive-donor
    /// permission - if anything a CSV needs the masking more than a screen does, because it
    /// outlives the session that produced it and travels by e-mail.
    ///
    /// The response carries an X-Export-Reference header tying the file back to the audit row
    /// recording who exported it.
    /// </summary>
    [HttpGet("export")]
    [HasPermission(PermissionCodes.DonationsExport)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] DonationSearchFilter filter, CancellationToken cancellationToken) =>
        FileFromResult(await queries.HandleAsync(new ExportDonationsQuery(filter), cancellationToken));

    /// <summary>One donation in full, with its receipts, refunds and chargebacks.</summary>
    [HttpGet("{id:guid}", Name = nameof(GetDonationAsync))]
    [HasPermission(PermissionCodes.DonationsView)]
    [ProducesResponseType(typeof(ApiResponse<DonationDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDonationAsync(Guid id, CancellationToken cancellationToken) =>
        FromResult(await queries.HandleAsync(new GetDonationQuery(id), cancellationToken));

    /// <summary>
    /// Records a gift taken outside the gateway: a cheque, a bank transfer, cash at an event.
    ///
    /// IT STILL GOES THROUGH AN INTENT, so an offline gift carries the same attribution and
    /// consent record as a card payment and appears in exactly the same reports.
    /// </summary>
    [HttpPost("offline")]
    [HasPermission(PermissionCodes.DonationsRecordOffline)]
    [ProducesResponseType(typeof(ApiResponse<DonationDetailResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordOfflineAsync(
        [FromBody] RecordOfflineDonationRequest request, CancellationToken cancellationToken)
    {
        var result = await donations.HandleAsync(
            new RecordOfflineDonationCommand(request), cancellationToken);

        return result.IsFailure
            ? FromResult(result)
            : CreatedFromResult(
                result, nameof(GetDonationAsync), new { id = result.Value!.Id }, "Donation recorded.");
    }

    /// <summary>
    /// Marks a donation reconciled against a bank statement.
    ///
    /// AN ASSERTION BY A PERSON, audited with their identity: "matched" means somebody looked at
    /// a statement line and this donation and said they are the same money.
    /// </summary>
    [HttpPost("{id:guid}/reconcile")]
    [HasPermission(PermissionCodes.DonationsReconcile)]
    [ProducesResponseType(typeof(ApiResponse<OutcomeResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReconcileAsync(
        Guid id, [FromBody] ReconcileDonationRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await donations.HandleAsync(new ReconcileDonationCommand(id, request), cancellationToken),
            "Donation reconciled.");

    /// <summary>
    /// Issues the tax receipt for a donation - section 24.
    ///
    /// THE AMOUNT IS NOT A PARAMETER. A receipt is for what was actually given, less anything
    /// refunded, and letting a caller choose the figure on a tax document is exactly the hole a
    /// receipt exists to close.
    /// </summary>
    [HttpPost("{id:guid}/receipt")]
    [HasPermission(PermissionCodes.ReceiptsIssue)]
    [ProducesResponseType(typeof(ApiResponse<ReceiptDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> IssueReceiptAsync(
        Guid id, [FromBody] IssueReceiptRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await receipts.HandleAsync(new IssueReceiptCommand(id, request), cancellationToken),
            "Receipt issued.");

    /// <summary>
    /// Raises a refund against a donation.
    ///
    /// RAISING IS NOT APPROVING. This opens a case; a DIFFERENT person has to approve it before
    /// any money moves, which the handler enforces per record rather than by permission.
    /// </summary>
    [HttpPost("{id:guid}/refunds")]
    [HasPermission(PermissionCodes.RefundsRequest)]
    [ProducesResponseType(typeof(ApiResponse<RefundCaseDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestRefundAsync(
        Guid id, [FromBody] RequestRefundRequest request, CancellationToken cancellationToken) =>
        FromResult(
            await refunds.HandleAsync(new RequestRefundCommand(id, request), cancellationToken),
            "Refund requested.");
}

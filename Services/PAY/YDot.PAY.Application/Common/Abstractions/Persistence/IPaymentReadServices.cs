using YDot.PAY.Application.Common.Models;
using YDot.PAY.Application.Features.Donations.DTOs;
using YDot.PAY.Application.Features.Payments.DTOs;
using YDot.PAY.Application.Features.Receipts.DTOs;
using YDot.PAY.Application.Features.Refunds.DTOs;

namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>
/// Read-side projections for the donation intent register and the intent detail screen.
///
/// SEPARATE FROM THE REPOSITORIES for the usual reason: a repository loads a tracked aggregate
/// so it can be changed, while a grid wants a dozen columns for twenty rows.
///
/// <paramref name="canSeeSensitiveDonor"/> APPEARS ON EVERY METHOD, which is unusual and
/// deliberate. Masking a donor's e-mail has to happen in the PROJECTION, not in the controller:
/// a read service that returned the real address and trusted the caller to mask it would leak
/// the moment one endpoint forgot.
/// </summary>
public interface IDonationIntentReadService
{
    Task<PagedResponse<DonationIntentListItemResponse>> SearchAsync(
        DonationIntentSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    Task<DonationIntentDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken);

    /// <summary>
    /// One intent by its public reference, for the DONOR-FACING result page.
    ///
    /// IT TAKES NO <see cref="AccessScope"/> AND ALWAYS MASKS. The caller has no session - they
    /// are a donor holding a payment link - so there is no scope to narrow by, and the reference
    /// itself is the authorisation: it is unguessable and resolves to exactly one intent.
    ///
    /// The masking is not optional here, unlike on the staff read. A donor sees their own
    /// donation, and nothing this returns reveals more than the payment link already did.
    /// </summary>
    Task<DonationIntentDetailResponse?> GetDetailByReferenceAsync(
        string intentReference, CancellationToken cancellationToken);

    /// <summary>The support queue: intents that failed and need a person. Section 23.</summary>
    Task<PagedResponse<PaymentSupportCaseResponse>> GetSupportQueueAsync(
        PaginationRequest pagination,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);
}

/// <summary>Read side for the donation register and its statistics.</summary>
public interface IDonationReadService
{
    Task<PagedResponse<DonationListItemResponse>> SearchAsync(
        DonationSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    Task<DonationDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken);

    /// <summary>
    /// Counts and totals for the register's summary tiles.
    ///
    /// The totals are grouped BY CURRENCY internally and returned in the Organisation's primary
    /// currency, because adding a rupee to a dollar is the one thing a money total must never do.
    /// </summary>
    Task<DonationStatisticsResponse> GetStatisticsAsync(
        AccessScope scope, CancellationToken cancellationToken);

    Task<IReadOnlyList<DonationExportRow>> GetExportRowsAsync(
        DonationSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);
}

/// <summary>Read side for the payment event queue.</summary>
public interface IPaymentEventReadService
{
    Task<PagedResponse<PaymentEventListItemResponse>> SearchAsync(
        PaymentEventSearchFilter filter, CancellationToken cancellationToken);

    Task<PaymentEventDetailResponse?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Read side for the receipt register.</summary>
public interface IReceiptReadService
{
    Task<PagedResponse<ReceiptListItemResponse>> SearchAsync(
        ReceiptSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    Task<ReceiptDetailResponse?> GetDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReceiptExportRow>> GetExportRowsAsync(
        ReceiptSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    /// <summary>
    /// The Receipt Register - issued receipts and failed payments together, with the totals.
    ///
    /// SEPARATE FROM <c>SearchAsync</c> ON PURPOSE. That one answers "which receipts exist", which
    /// is the right question for correcting or voiding one. This answers "what happened to every
    /// payment", which is what the document's register shows, and the two sets are deliberately
    /// different sizes.
    /// </summary>
    Task<ReceiptRegisterResponse> GetRegisterAsync(
        ReceiptRegisterFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);
}

/// <summary>Read side for the refund and chargeback registers.</summary>
public interface IRefundReadService
{
    Task<PagedResponse<RefundCaseListItemResponse>> SearchRefundsAsync(
        RefundSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    Task<RefundCaseDetailResponse?> GetRefundDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundExportRow>> GetRefundExportRowsAsync(
        RefundSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    Task<PagedResponse<ChargebackCaseListItemResponse>> SearchChargebacksAsync(
        ChargebackSearchFilter filter,
        AccessScope scope,
        bool canSeeSensitiveDonor,
        CancellationToken cancellationToken);

    Task<ChargebackCaseDetailResponse?> GetChargebackDetailAsync(
        Guid id, AccessScope scope, bool canSeeSensitiveDonor, CancellationToken cancellationToken);
}

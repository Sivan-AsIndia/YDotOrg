using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>Write-side access to refund and chargeback cases.</summary>
public interface IRefundRepository
{
    // ---- Refunds ----------------------------------------------------------------------

    Task AddRefundAsync(RefundCase refundCase, CancellationToken cancellationToken);

    Task<RefundCase?> GetRefundAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a refund is already being worked on this donation.
    ///
    /// Guards the double request: two refunds approved in parallel could between them exceed the
    /// donation, and the gateway would refuse the second in a way nobody sees until reconciliation.
    /// </summary>
    Task<bool> HasOpenRefundAsync(Guid donationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefundCase>> GetRefundsForDonationAsync(
        Guid donationId, CancellationToken cancellationToken);

    Task<bool> CaseReferenceExistsAsync(string caseReference, CancellationToken cancellationToken);

    // ---- Chargebacks -------------------------------------------------------------------------

    Task AddChargebackAsync(ChargebackCase chargebackCase, CancellationToken cancellationToken);

    Task<ChargebackCase?> GetChargebackAsync(Guid id, CancellationToken cancellationToken);

    Task<ChargebackCase?> GetChargebackByDisputeReferenceAsync(
        string disputeReference, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChargebackCase>> GetChargebacksForDonationAsync(
        Guid donationId, CancellationToken cancellationToken);
}

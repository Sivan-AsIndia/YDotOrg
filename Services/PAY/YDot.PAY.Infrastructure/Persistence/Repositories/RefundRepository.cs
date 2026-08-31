using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.Repositories;

/// <summary>Refund and chargeback cases.</summary>
public sealed class RefundRepository(PaymentDbContext context) : IRefundRepository
{
    // ---- Refunds ----------------------------------------------------------------------

    public async Task AddRefundAsync(RefundCase refundCase, CancellationToken cancellationToken) =>
        await context.RefundCases.AddAsync(refundCase, cancellationToken);

    /// <summary>
    /// One refund case with its donation.
    ///
    /// THE DONATION IS INCLUDED WITH ITS OTHER CASES, because approving a refund has to check
    /// the refundable balance against everything else already committed against that donation -
    /// and a lazily-absent collection would read as "nothing else outstanding".
    /// </summary>
    public Task<RefundCase?> GetRefundAsync(Guid id, CancellationToken cancellationToken) =>
        context.RefundCases
            .Include(refundCase => refundCase.Donation)
                .ThenInclude(donation => donation.Receipts)
            .Include(refundCase => refundCase.Donation)
                .ThenInclude(donation => donation.RefundCases)
            .Include(refundCase => refundCase.Donation)
                .ThenInclude(donation => donation.ChargebackCases)
            .FirstOrDefaultAsync(refundCase => refundCase.Id == id, cancellationToken);

    /// <summary>
    /// Whether a refund is already being worked on this donation.
    ///
    /// "OPEN" MEANS UNDECIDED OR IN FLIGHT - requested, approved or processing. A rejected or
    /// completed case is finished and must not block a later legitimate request; an in-flight one
    /// must, or two refunds could between them exceed the donation and the gateway would refuse
    /// the second in a way nobody notices until reconciliation.
    /// </summary>
    public Task<bool> HasOpenRefundAsync(Guid donationId, CancellationToken cancellationToken) =>
        context.RefundCases
            .AnyAsync(
                refundCase => refundCase.DonationId == donationId
                              && (refundCase.Status == RefundStatus.Requested
                                  || refundCase.Status == RefundStatus.Approved
                                  || refundCase.Status == RefundStatus.Processing),
                cancellationToken);

    public async Task<IReadOnlyList<RefundCase>> GetRefundsForDonationAsync(
        Guid donationId, CancellationToken cancellationToken) =>
        await context.RefundCases
            .Where(refundCase => refundCase.DonationId == donationId)
            .OrderByDescending(refundCase => refundCase.RequestedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Whether a case reference is taken, by a refund OR a chargeback.
    ///
    /// BOTH TABLES ARE CHECKED because the two share one reference space: an operator reading
    /// "CASE-7K2M9QRT" out to a donor should not have to know which kind of case it is, and two
    /// different cases answering to one reference would make that conversation impossible.
    ///
    /// UNFILTERED, because the generator's collision check has to see references held by other
    /// Organisations too - a filtered check would report one free and the insert would then fail
    /// on the unique index instead.
    /// </summary>
    public async Task<bool> CaseReferenceExistsAsync(
        string caseReference, CancellationToken cancellationToken)
    {
        var refundExists = await context.RefundCases
            .IgnoreQueryFilters()
            .AnyAsync(refundCase => refundCase.CaseReference == caseReference, cancellationToken);

        if (refundExists)
        {
            return true;
        }

        return await context.ChargebackCases
            .IgnoreQueryFilters()
            .AnyAsync(chargeback => chargeback.CaseReference == caseReference, cancellationToken);
    }

    // ---- Chargebacks -------------------------------------------------------------------------

    public async Task AddChargebackAsync(ChargebackCase chargebackCase, CancellationToken cancellationToken) =>
        await context.ChargebackCases.AddAsync(chargebackCase, cancellationToken);

    public Task<ChargebackCase?> GetChargebackAsync(Guid id, CancellationToken cancellationToken) =>
        context.ChargebackCases
            .Include(chargeback => chargeback.Donation)
                .ThenInclude(donation => donation.Receipts)
            .FirstOrDefaultAsync(chargeback => chargeback.Id == id, cancellationToken);

    /// <summary>
    /// A chargeback by the bank's own dispute reference.
    ///
    /// UNFILTERED: a dispute notification arrives through the webhook path with no session, and
    /// the reference is unique platform-wide by a filtered unique index.
    /// </summary>
    public Task<ChargebackCase?> GetChargebackByDisputeReferenceAsync(
        string disputeReference, CancellationToken cancellationToken) =>
        context.ChargebackCases
            .IgnoreQueryFilters()
            .Include(chargeback => chargeback.Donation)
            .FirstOrDefaultAsync(
                chargeback => chargeback.GatewayDisputeReference == disputeReference, cancellationToken);

    public async Task<IReadOnlyList<ChargebackCase>> GetChargebacksForDonationAsync(
        Guid donationId, CancellationToken cancellationToken) =>
        await context.ChargebackCases
            .Where(chargeback => chargeback.DonationId == donationId)
            .OrderByDescending(chargeback => chargeback.OpenedAtUtc)
            .ToListAsync(cancellationToken);
}

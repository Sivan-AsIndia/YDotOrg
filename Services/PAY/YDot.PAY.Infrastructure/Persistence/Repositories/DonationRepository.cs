using Microsoft.EntityFrameworkCore;
using YDot.PAY.Application.Common.Abstractions.Persistence;
using YDot.PAY.Domain.Entities;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Infrastructure.Persistence.Repositories;

/// <summary>
/// Write-side access to intents, attempts and donations.
///
/// TWO METHODS CALL <c>IgnoreQueryFilters</c> AND BOTH SAY SO IN THEIR NAME. Every other read
/// here goes through the Organisation filter. The two exceptions serve callers who arrive with
/// no session at all - a donor following a payment link and a gateway posting a webhook - and
/// in both cases the reference they present is what RESOLVES the Organisation rather than
/// something that could be used to choose one.
/// </summary>
public sealed class DonationRepository(PaymentDbContext context) : IDonationRepository
{
    // ---- Donation intents -------------------------------------------------------------

    public async Task AddIntentAsync(DonationIntent intent, CancellationToken cancellationToken) =>
        await context.DonationIntents.AddAsync(intent, cancellationToken);

    public Task<DonationIntent?> GetIntentAsync(Guid id, CancellationToken cancellationToken) =>
        context.DonationIntents
            .Include(intent => intent.Attempts)
            .Include(intent => intent.Donation)
            .FirstOrDefaultAsync(intent => intent.Id == id, cancellationToken);

    /// <summary>
    /// THE FIRST DELIBERATE FILTER BYPASS.
    ///
    /// A donor holding a payment link has no session, so there is no Organisation to filter by
    /// until this lookup has found one. The reference is twelve unguessable characters, is unique
    /// platform-wide by index, and resolves to exactly one row - the caller is naming a record
    /// that already belongs to an Organisation, not choosing which Organisation to act in.
    /// </summary>
    public Task<DonationIntent?> GetIntentByReferenceAsync(
        string intentReference, CancellationToken cancellationToken) =>
        context.DonationIntents
            .IgnoreQueryFilters()
            .Include(intent => intent.Attempts)
            .Include(intent => intent.Donation)
            .FirstOrDefaultAsync(intent => intent.IntentReference == intentReference, cancellationToken);

    /// <summary>
    /// Section 26: an unpaid intent for the same donor and amount inside this Organisation.
    ///
    /// SCOPED EXPLICITLY BY TenantId rather than relying on the ambient filter, because the
    /// public path calls it with no session - and a match found in the wrong Organisation would
    /// hand one charity's donor a link belonging to another.
    ///
    /// The amount is compared through the owned type's column, and the window is deliberately
    /// narrow: only intents that can still be paid.
    /// </summary>
    public Task<DonationIntent?> FindOpenIntentAsync(
        Guid tenantId, string normalisedEmail, decimal amount, CancellationToken cancellationToken) =>
        context.DonationIntents
            .IgnoreQueryFilters()
            .Where(intent => intent.TenantId == tenantId)
            .Where(intent => intent.NormalisedEmail == normalisedEmail)
            .Where(intent => intent.Amount.Amount == amount)
            .Where(intent => intent.Status == DonationIntentStatus.Draft
                             || intent.Status == DonationIntentStatus.AwaitingPayment)
            .OrderByDescending(intent => intent.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Intents whose payment link has lapsed.
    ///
    /// FILTER-FREE AND CAPPED. The expiry sweep is a background job with no Organisation of its
    /// own - it has to see the whole platform - and the row cap stops one very stale run from
    /// loading a year of abandoned intents into memory at once.
    /// </summary>
    public async Task<IReadOnlyList<DonationIntent>> GetExpiredIntentsAsync(
        DateTimeOffset asOf, int maximumRows, CancellationToken cancellationToken) =>
        await context.DonationIntents
            .IgnoreQueryFilters()
            .Where(intent => intent.PaymentLinkExpiresAtUtc != null
                             && intent.PaymentLinkExpiresAtUtc <= asOf)
            .Where(intent => intent.Status == DonationIntentStatus.Draft
                             || intent.Status == DonationIntentStatus.AwaitingPayment)
            .OrderBy(intent => intent.PaymentLinkExpiresAtUtc)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);

    // ---- Payment attempts ----------------------------------------------------------------

    public async Task AddAttemptAsync(PaymentAttempt attempt, CancellationToken cancellationToken) =>
        await context.PaymentAttempts.AddAsync(attempt, cancellationToken);

    public Task<PaymentAttempt?> GetAttemptAsync(Guid id, CancellationToken cancellationToken) =>
        context.PaymentAttempts
            .Include(attempt => attempt.DonationIntent)
            .FirstOrDefaultAsync(attempt => attempt.Id == id, cancellationToken);

    /// <summary>
    /// THE SECOND DELIBERATE FILTER BYPASS, and the one every webhook depends on.
    ///
    /// A gateway posts a callback carrying its own reference and nothing else. There is no
    /// session, no header and no Organisation until this lookup resolves one. The reference is
    /// unique platform-wide by a filtered unique index, so this returns one row or none.
    ///
    /// The intent is included because the caller invariably needs it next, and loading it
    /// separately would be a second unfiltered query rather than one.
    /// </summary>
    public Task<PaymentAttempt?> GetAttemptByGatewayReferenceAsync(
        string gatewayReference, CancellationToken cancellationToken) =>
        context.PaymentAttempts
            .IgnoreQueryFilters()
            .Include(attempt => attempt.DonationIntent)
                .ThenInclude(intent => intent.Donation)
            .FirstOrDefaultAsync(
                attempt => attempt.GatewayReference == gatewayReference, cancellationToken);

    /// <summary>
    /// The most recent attempt on an intent.
    ///
    /// UNFILTERED, because safe retry and verification both run on the public path where the
    /// intent was itself resolved by reference. Scoped to one intent id, so it can return only
    /// rows belonging to the Organisation that intent already resolved to.
    /// </summary>
    public Task<PaymentAttempt?> GetLatestAttemptAsync(Guid intentId, CancellationToken cancellationToken) =>
        context.PaymentAttempts
            .IgnoreQueryFilters()
            .Where(attempt => attempt.DonationIntentId == intentId)
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);

    // ---- Donations ---------------------------------------------------------------------------

    public async Task AddDonationAsync(Donation donation, CancellationToken cancellationToken) =>
        await context.Donations.AddAsync(donation, cancellationToken);

    /// <summary>
    /// One donation with everything the detail screen and the action rules need.
    ///
    /// THE THREE COLLECTIONS ARE NOT OPTIONAL. <c>HasIssuedReceipt</c>, <c>HasOpenCase</c> and
    /// the permitted-action list are all computed FROM them, so loading the donation without
    /// them would silently report a receipted donation as unreceipted and offer a second
    /// receipt.
    /// </summary>
    public Task<Donation?> GetDonationAsync(Guid id, CancellationToken cancellationToken) =>
        context.Donations
            .Include(donation => donation.Receipts)
            .Include(donation => donation.RefundCases)
            .Include(donation => donation.ChargebackCases)
            .Include(donation => donation.DonationIntent)
            .FirstOrDefaultAsync(donation => donation.Id == id, cancellationToken);

    /// <summary>
    /// The donation recorded against an intent, if any.
    ///
    /// UNFILTERED, because the webhook path asks this question before an Organisation is in
    /// scope - and the answer is what stops a redelivered capture recording a second donation.
    /// The intent id already belongs to exactly one Organisation.
    /// </summary>
    public Task<Donation?> GetDonationByIntentAsync(Guid intentId, CancellationToken cancellationToken) =>
        context.Donations
            .IgnoreQueryFilters()
            .Include(donation => donation.Receipts)
            .FirstOrDefaultAsync(donation => donation.DonationIntentId == intentId, cancellationToken);

    public Task<Donation?> GetDonationByReferenceAsync(
        string donationReference, CancellationToken cancellationToken) =>
        context.Donations
            .Include(donation => donation.Receipts)
            .Include(donation => donation.RefundCases)
            .Include(donation => donation.ChargebackCases)
            .FirstOrDefaultAsync(
                donation => donation.DonationReference == donationReference, cancellationToken);

    /// <summary>
    /// Whether a reference is taken.
    ///
    /// UNFILTERED ON PURPOSE. References are unique PLATFORM-WIDE, so a filtered check would
    /// report a reference free that another Organisation already holds, and the insert would
    /// then fail on the unique index instead - turning a retryable collision into an error the
    /// donor sees.
    /// </summary>
    public Task<bool> DonationReferenceExistsAsync(string reference, CancellationToken cancellationToken) =>
        context.Donations
            .IgnoreQueryFilters()
            .AnyAsync(donation => donation.DonationReference == reference, cancellationToken);

    public Task<bool> IntentReferenceExistsAsync(string reference, CancellationToken cancellationToken) =>
        context.DonationIntents
            .IgnoreQueryFilters()
            .AnyAsync(intent => intent.IntentReference == reference, cancellationToken);
}

using YDot.PAY.Domain.Entities;

namespace YDot.PAY.Application.Common.Abstractions.Persistence;

/// <summary>
/// Write-side access to the donation intent, its attempts and the donation itself.
///
/// EVERY READ HERE PASSES THROUGH THE ORGANISATION QUERY FILTER, with two documented exceptions
/// that both take references arriving from OUTSIDE any session - a donor following a payment
/// link, and a gateway posting a webhook. Both are marked and both explain themselves.
/// </summary>
public interface IDonationRepository
{
    // ---- Donation intents -------------------------------------------------------------

    Task AddIntentAsync(DonationIntent intent, CancellationToken cancellationToken);

    Task<DonationIntent?> GetIntentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an intent from its public reference, ACROSS Organisations.
    ///
    /// THE FIRST DELIBERATE FILTER BYPASS. A donor following a payment link has no session and
    /// no Organisation, so the reference has to resolve globally - and the reference is what
    /// CARRIES the Organisation, which the caller then operates within.
    ///
    /// It is safe because the reference is twelve unguessable characters and resolves to exactly
    /// one row: the caller is naming a record that already belongs to an Organisation, not
    /// choosing which Organisation to act in.
    /// </summary>
    Task<DonationIntent?> GetIntentByReferenceAsync(string intentReference, CancellationToken cancellationToken);

    /// <summary>
    /// Section 26: is this e-mail already an intent-level match inside this Organisation?
    ///
    /// Used to spot a donor starting a second intent for the same gift - a double submit - so
    /// the existing one can be reused rather than a second payment link issued.
    /// </summary>
    Task<DonationIntent?> FindOpenIntentAsync(
        Guid tenantId, string normalisedEmail, decimal amount, CancellationToken cancellationToken);

    /// <summary>Intents whose payment link has lapsed, for the expiry sweep.</summary>
    Task<IReadOnlyList<DonationIntent>> GetExpiredIntentsAsync(
        DateTimeOffset asOf, int maximumRows, CancellationToken cancellationToken);

    // ---- Payment attempts ----------------------------------------------------------------

    Task AddAttemptAsync(PaymentAttempt attempt, CancellationToken cancellationToken);

    Task<PaymentAttempt?> GetAttemptAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an attempt from the GATEWAY's reference, ACROSS Organisations.
    ///
    /// THE SECOND DELIBERATE FILTER BYPASS, and the one a webhook depends on: a gateway posts a
    /// callback with its own reference and nothing else, so there is no Organisation to filter
    /// by until this lookup has resolved one.
    ///
    /// The reference is unique platform-wide - enforced by a unique index - so this returns one
    /// row or none.
    /// </summary>
    Task<PaymentAttempt?> GetAttemptByGatewayReferenceAsync(
        string gatewayReference, CancellationToken cancellationToken);

    /// <summary>The most recent attempt on an intent, which is the one safe retry reasons about.</summary>
    Task<PaymentAttempt?> GetLatestAttemptAsync(Guid intentId, CancellationToken cancellationToken);

    // ---- Donations ---------------------------------------------------------------------------

    Task AddDonationAsync(Donation donation, CancellationToken cancellationToken);

    Task<Donation?> GetDonationAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The donation recorded against an intent, if any.
    ///
    /// AT MOST ONE. That invariant is what stops a double capture becoming double income: two
    /// successful captures on one intent produce one donation and a refund case, never two
    /// donations.
    /// </summary>
    Task<Donation?> GetDonationByIntentAsync(Guid intentId, CancellationToken cancellationToken);

    Task<Donation?> GetDonationByReferenceAsync(
        string donationReference, CancellationToken cancellationToken);

    /// <summary>Whether a donation reference is already taken. Guards the generator's collision.</summary>
    Task<bool> DonationReferenceExistsAsync(string reference, CancellationToken cancellationToken);

    Task<bool> IntentReferenceExistsAsync(string reference, CancellationToken cancellationToken);
}

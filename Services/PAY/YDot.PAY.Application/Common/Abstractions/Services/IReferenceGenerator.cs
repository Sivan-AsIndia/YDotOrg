namespace YDot.PAY.Application.Common.Abstractions.Services;

/// <summary>
/// Produces the public references this module hands out.
///
/// EVERY ONE OF THEM IS UNGUESSABLE, and that is not decoration. An intent reference appears in
/// a payment link that is e-mailed and printed; a donation reference is quoted in support
/// conversations. A sequential reference would let anybody holding one enumerate every other
/// donation on the platform - who gave, how much, to which campaign.
///
/// THE RECEIPT NUMBER IS THE DELIBERATE EXCEPTION and is NOT generated here. Tax authorities
/// expect receipt numbers to run in an unbroken per-organisation sequence, so it is allocated
/// from the database inside the issuing transaction where a gap cannot open up.
/// </summary>
public interface IReferenceGenerator
{
    /// <summary>A donation intent reference, for example INT-7K3M9QXA2P4R.</summary>
    string NewIntentReference();

    /// <summary>A donation reference, for example DON-4Q8T2NVX6H1Y.</summary>
    string NewDonationReference();

    /// <summary>A refund or chargeback case reference.</summary>
    string NewCaseReference(string prefix);

    /// <summary>
    /// An idempotency key for a gateway call.
    ///
    /// THE KEY TO SAFE RETRY. Reusing it means the gateway recognises a repeat of the same
    /// attempt and returns the original outcome rather than charging again.
    /// </summary>
    string NewIdempotencyKey();
}

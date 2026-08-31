using System.Security.Cryptography;
using YDot.PAY.Application.Common.Abstractions.Services;

namespace YDot.PAY.Infrastructure.Services;

/// <summary>
/// Produces the public references this module hands out.
///
/// EVERY ONE OF THEM IS CRYPTOGRAPHICALLY RANDOM, and that is not decoration. An intent
/// reference appears in a payment link that is e-mailed, printed on a poster and encoded in a QR
/// code; a donation reference is quoted in support conversations and pasted into e-mails. A
/// sequential reference would let anybody holding one enumerate every other donation on the
/// platform - who gave, how much, to which campaign - and the tenant isolation everywhere else
/// would be worth nothing.
///
/// THE ALPHABET EXCLUDES 0, O, 1, I AND L. These references get read aloud down a telephone by
/// a donor to a support agent, and those five characters are the ones people get wrong. Twelve
/// characters from an alphabet of 31 is about 59 bits - far beyond guessing, while still short
/// enough to fit on a printed receipt.
///
/// THE RECEIPT NUMBER IS DELIBERATELY NOT HERE. Tax authorities expect receipt numbers to run in
/// an unbroken per-organisation sequence, so it is allocated from the database inside the issuing
/// transaction where a gap cannot open up. It is the one identifier in this module that must be
/// guessable, and the one that must never collide.
/// </summary>
public sealed class ReferenceGenerator : IReferenceGenerator
{
    /// <summary>Unambiguous when spoken and when read from a printed receipt.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private const int ReferenceLength = 12;

    public string NewIntentReference() => $"INT-{Random(ReferenceLength)}";

    public string NewDonationReference() => $"DON-{Random(ReferenceLength)}";

    /// <summary>
    /// A refund or chargeback case reference.
    ///
    /// The prefix is the caller's, so a refund reads RFD- and a chargeback CBK- - which means an
    /// operator can tell what kind of case they are looking at from the reference alone, without
    /// opening it.
    /// </summary>
    public string NewCaseReference(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        return $"{prefix.Trim().ToUpperInvariant()}-{Random(ReferenceLength)}";
    }

    /// <summary>
    /// An idempotency key for a gateway call.
    ///
    /// THE KEY TO SAFE RETRY. Reusing it means the gateway recognises a repeat of the same
    /// attempt and returns the original outcome rather than charging again - which is the
    /// difference between helping a donor whose card failed and charging one who already paid.
    ///
    /// It is a full GUID rather than the shorter alphabet above because nobody ever reads it
    /// aloud, and the wider space is free.
    /// </summary>
    public string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// A random string from the alphabet.
    ///
    /// <c>RandomNumberGenerator</c> RATHER THAN <c>Random</c>. A pseudo-random reference seeded
    /// from the clock is predictable to anybody who knows roughly when a donation was made, which
    /// defeats the entire purpose - the reference is the only thing standing between a stranger
    /// and somebody else's donation record on the public result page.
    /// </summary>
    private static string Random(int length)
    {
        var characters = new char[length];

        for (var index = 0; index < length; index++)
        {
            characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(characters);
    }
}

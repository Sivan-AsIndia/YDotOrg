using System.Security.Cryptography;
using System.Text;
using YDot.IAM.Application.Common.Abstractions.Security;

namespace YDot.IAM.Infrastructure.Security;

/// <summary>
/// Generates the secrets that travel in links and cookies, and hashes them for storage.
///
/// TWO DECISIONS RUN THROUGH ALL OF IT.
///
/// FIRST, EVERY RANDOM VALUE COMES FROM <see cref="RandomNumberGenerator"/>, never from
/// <c>System.Random</c>. System.Random is seeded predictably and is not a cryptographic
/// generator, so an invitation token produced with it can be guessed by anybody who knows
/// roughly when it was issued — which is a way into somebody account.
///
/// SECOND, THE HASH IS PLAIN SHA-256 RATHER THAN A PASSWORD HASH, and that is deliberate.
/// These are 256-bit random values, not passwords: there is no dictionary to attack and no
/// low-entropy guess to make, so the slow salted hashing that protects a password buys
/// nothing here and would make every token lookup a table scan instead of an index seek.
/// A password is a completely different problem and goes through
/// <see cref="IPasswordHasher"/>, which uses the framework algorithm.
/// </summary>
public sealed class TokenHasher : ITokenHasher
{
    /// <summary>
    /// The alphabet for a human-readable reference. Deliberately excludes the characters
    /// people misread over the phone: 0/O, 1/I/L. Support staff read these aloud.
    /// </summary>
    private const string ReferenceAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string GenerateToken(int byteLength = 32)
    {
        var length = Math.Clamp(byteLength, 16, 64);
        var bytes = RandomNumberGenerator.GetBytes(length);

        // URL-safe, so it survives being pasted into a query string without escaping.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    /// <summary>
    /// Constant-time comparison.
    ///
    /// A naive <c>==</c> returns as soon as two bytes differ, so the time it takes leaks how
    /// many leading characters matched. Given enough attempts that is enough to reconstruct a
    /// token one byte at a time, which is why this uses the fixed-time comparison even though
    /// the values being compared are already hashes.
    /// </summary>
    public bool Verify(string token, string expectedHash)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actual = Encoding.UTF8.GetBytes(Hash(token));
        var expected = Encoding.UTF8.GetBytes(expectedHash.ToLowerInvariant());

        return actual.Length == expected.Length
               && CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// A short, NON-SECRET handle such as INV-7K2M9X.
    ///
    /// It exists so support can talk about an invitation on the phone without either party
    /// reading out the actual token. Because it is not a secret it is deliberately short —
    /// but it is still drawn from the cryptographic generator, because a predictable
    /// reference would let somebody enumerate which invitations exist.
    /// </summary>
    public string GenerateReference(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var characters = new char[6];

        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
        }

        return $"{prefix.Trim().ToUpperInvariant()}-{new string(characters)}";
    }

    /// <summary>
    /// A numeric one-time code.
    ///
    /// <see cref="RandomNumberGenerator.GetInt32(int)"/> per digit rather than one modulo of a
    /// larger number, because the modulo approach skews the distribution towards the low
    /// digits and a skewed one-time code is easier to guess than it looks.
    /// </summary>
    public string GenerateNumericCode(int digits = 6)
    {
        var length = Math.Clamp(digits, 4, 10);
        var characters = new char[length];

        for (var index = 0; index < length; index++)
        {
            characters[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        return new string(characters);
    }
}

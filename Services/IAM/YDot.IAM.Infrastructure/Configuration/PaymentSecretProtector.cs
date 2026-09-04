using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Infrastructure.Configuration;

/// <summary>
/// Seals a merchant credential with AES-256-GCM.
///
/// WHY GCM AND NOT CBC. GCM authenticates as well as encrypts, so a ciphertext that has been
/// altered in the database fails to open rather than decrypting to plausible rubbish that is
/// then sent to a payment provider as a key. With CBC an attacker who can write to the column
/// can flip bits in the plaintext without the application noticing; here the tag check fails and
/// <see cref="Unprotect"/> returns null, which the callers already treat as "not configured".
///
/// THE STORED FORM is <c>v1.&lt;nonce&gt;.&lt;tag&gt;.&lt;ciphertext&gt;</c>, each part base64.
/// The version prefix is what makes a future change of algorithm or key possible without a
/// migration that has to decrypt every row first: a reader that meets a prefix it does not
/// recognise returns null instead of guessing.
///
/// A FRESH NONCE PER CALL, FROM THE SYSTEM CSPRNG. Reusing a nonce under one key is the single
/// catastrophic mistake available in GCM - it leaks the XOR of two plaintexts and, worse, the
/// authentication subkey - so it is generated here and never derived from anything about the
/// record.
///
/// WHAT THIS DOES NOT DEFEND AGAINST, stated plainly because a security control described
/// vaguely gets trusted for more than it does: an attacker holding both the database AND the
/// application's configuration can read every credential. That is inherent - the service has to
/// be able to decrypt these to take a payment. What it defends against is the far more common
/// case of the two being separated: a stolen backup, a dumped table, a production restore into a
/// test environment.
/// </summary>
public sealed class PaymentSecretProtector : IPaymentSecretProtector
{
    /// <summary>The format marker. A stored value that does not start with this is not ours.</summary>
    private const string Version = "v1";

    /// <summary>96 bits, the size GCM is specified around and the only one worth using.</summary>
    private const int NonceBytes = 12;

    /// <summary>128 bits, the full tag. Truncating it weakens the authentication for nothing.</summary>
    private const int TagBytes = 16;

    /// <summary>
    /// The label mixed into the derived key.
    ///
    /// It is what keeps a key derived from the JWT signing key from being the same as anything
    /// else that might one day be derived from it. Deriving two purposes to the same key is how
    /// a weakness in one becomes a weakness in the other.
    /// </summary>
    private static readonly byte[] DerivationInfo =
        Encoding.UTF8.GetBytes("YDot.PaymentGatewayCredential.v1");

    private readonly byte[] _key;

    public PaymentSecretProtector(
        IOptions<PaymentGatewaySettings> settings,
        IOptions<JwtSettings> jwtSettings,
        ILogger<PaymentSecretProtector> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jwtSettings);

        _key = ResolveKey(settings.Value, jwtSettings.Value, logger);
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var cipher = new byte[bytes.Length];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, bytes, cipher, tag);

        // The plaintext copy this method made is cleared. The caller's string is beyond reach -
        // .NET strings are immutable and interned - but there is no reason to leave this one
        // sitting in a pooled buffer either.
        CryptographicOperations.ZeroMemory(bytes);

        return string.Join(
            '.',
            Version,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(cipher));
    }

    public string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return null;
        }

        var parts = cipherText.Split('.');

        if (parts.Length != 4 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            // Not something this protector sealed: a value from an earlier scheme, or a column
            // somebody edited by hand. Null rather than an exception - see the interface.
            return null;
        }

        try
        {
            var nonce = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            var cipher = Convert.FromBase64String(parts[3]);

            if (nonce.Length != NonceBytes || tag.Length != TagBytes)
            {
                return null;
            }

            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // THE TAG DID NOT VERIFY: a different key, or an altered ciphertext. Both mean the
            // same thing to every caller - there is no usable credential here - and neither is
            // worth an unhandled exception on a donation path. Nothing is logged with it,
            // because the only distinguishing detail is the ciphertext itself.
            return null;
        }
        catch (FormatException)
        {
            // Not base64. Same answer.
            return null;
        }
    }

    /// <summary>
    /// The masked form: the provider's prefix, then a fixed run of dots, then the last four
    /// characters.
    ///
    /// THE PREFIX IS KEPT DELIBERATELY. <c>rzp_live_</c> against <c>rzp_test_</c> is the single
    /// most useful thing an operator can see on this screen, and it is not secret - Razorpay
    /// prints it in its own dashboard. The middle is replaced by a FIXED number of dots rather
    /// than one per character, so the mask does not leak the key's length.
    /// </summary>
    public string? Hint(string? plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        var value = plaintext.Trim();

        // Too short to mask usefully. Four characters of an eight-character secret is half of it,
        // so nothing but dots is shown.
        if (value.Length <= 8)
        {
            return "........";
        }

        // Everything up to and including the last underscore is a provider prefix, not a
        // credential: rzp_test_, pk_live_, sk_test_. A key with no underscore has no prefix.
        var lastUnderscore = value.LastIndexOf('_');

        var prefix = lastUnderscore > 0 && lastUnderscore < value.Length - 4
            ? value[..(lastUnderscore + 1)]
            : string.Empty;

        return $"{prefix}........{value[^4..]}";
    }

    /// <summary>
    /// The 32-byte key, from configuration where there is one and derived from the JWT signing
    /// key where there is not.
    ///
    /// HKDF RATHER THAN A PLAIN HASH for the derivation. The signing key is a high-entropy secret
    /// but it is not uniformly distributed key material, and HKDF's extract step is exactly the
    /// operation for turning one into the other. Hashing it directly would work in practice and
    /// would be the wrong habit to leave in the codebase.
    /// </summary>
    private static byte[] ResolveKey(
        PaymentGatewaySettings settings, JwtSettings jwt, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(settings.EncryptionKey))
        {
            byte[] configured;

            try
            {
                configured = Convert.FromBase64String(settings.EncryptionKey.Trim());
            }
            catch (FormatException)
            {
                throw new InvalidOperationException(
                    "PaymentGatewaySettings:EncryptionKey is not valid base64. It must be 32 "
                    + "random bytes, base64-encoded - generate one with `openssl rand -base64 32`.");
            }

            if (configured.Length != 32)
            {
                throw new InvalidOperationException(
                    $"PaymentGatewaySettings:EncryptionKey decodes to {configured.Length} bytes. "
                    + "AES-256 needs exactly 32 - generate one with `openssl rand -base64 32`.");
            }

            return configured;
        }

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            // NEITHER KEY IS SET. Failing at startup is the right answer: the alternative is a
            // service that accepts a merchant credential, stores it under a key nobody chose,
            // and cannot be moved to a real key later without every stored credential becoming
            // unreadable.
            throw new InvalidOperationException(
                "No key is available to encrypt payment gateway credentials. Set "
                + "PaymentGatewaySettings:EncryptionKey (32 random bytes, base64), or set "
                + "JwtSettings:SigningKey, which it falls back to deriving from.");
        }

        logger.LogWarning(
            "PaymentGatewaySettings:EncryptionKey is not configured, so payment gateway "
            + "credentials are sealed with a key derived from the JWT signing key. This works, "
            + "but it ties the two together: ROTATING THE JWT SIGNING KEY WOULD MAKE EVERY "
            + "STORED MERCHANT CREDENTIAL UNREADABLE. Set an explicit key before going live.");

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(jwt.SigningKey),
            outputLength: 32,
            salt: null,
            info: DerivationInfo);
    }
}

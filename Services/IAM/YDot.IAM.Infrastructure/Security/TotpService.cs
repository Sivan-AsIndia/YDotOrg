using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Infrastructure.Security;

/// <summary>
/// Time-based one-time passwords, RFC 6238. This is what an authenticator app produces.
///
/// HOW IT WORKS, IN ONE PARAGRAPH. The server and the phone share a secret. Both take the
/// current Unix time, divide it by 30 to get a counter, and HMAC-SHA1 that counter with the
/// secret. A fixed truncation of the result gives six digits. Because both sides compute the
/// same thing from the same clock, no code ever travels between them — which is exactly why
/// an authenticator app is stronger than an SMS.
///
/// THE DRIFT WINDOW IS THE PRACTICAL PART. Phone clocks are not perfectly synchronised, so a
/// code generated a few seconds either side of a boundary would otherwise be rejected and the
/// person would be told, wrongly, that they typed it incorrectly. One step each way accepts
/// codes from a ninety-second window in total, which is the usual compromise between
/// forgiving and loose.
///
/// SHA-1 IS CORRECT HERE, despite being unacceptable for most things. RFC 6238 specifies it,
/// and every authenticator app implements it; the security of TOTP rests on the secret and
/// the short window, not on the hash resisting collisions. Using SHA-256 would be "stronger"
/// and would simply not work with Google Authenticator.
/// </summary>
public sealed class TotpService(IOptions<SecuritySettings> securityOptions) : ITotpService
{
    private readonly SecuritySettings _security = securityOptions.Value;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// A new shared secret: 160 bits, Base32-encoded because that is what authenticator apps
    /// and QR codes expect.
    /// </summary>
    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);

        return ToBase32(bytes);
    }

    /// <summary>
    /// The otpauth:// URI a QR code encodes.
    ///
    /// The issuer and the account label are what the person sees in their authenticator, so
    /// both carry the Organisation name — somebody who administers three Organisations would
    /// otherwise end up with three identical entries called "YDot" and no way to tell which
    /// code belongs to which.
    /// </summary>
    public string BuildProvisioningUri(string secret, string accountName, string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);

        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(accountName);

        return string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{encodedIssuer}:{encodedAccount}"
            + $"?secret={secret}"
            + $"&issuer={encodedIssuer}"
            + $"&algorithm=SHA1"
            + $"&digits={_security.TotpDigits}"
            + $"&period={_security.TotpPeriodSeconds}");
    }

    /// <summary>
    /// Verifies a code, tolerating a small clock drift either way.
    ///
    /// The comparison is constant-time. A naive string equality would leak, through its
    /// timing, how many leading digits were right — which turns a million-guess space into a
    /// far smaller one.
    /// </summary>
    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var normalised = code.Trim();
        if (normalised.Length != _security.TotpDigits || !normalised.All(char.IsDigit))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = FromBase32(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _security.TotpPeriodSeconds;
        var drift = Math.Max(0, _security.TotpAllowedDriftSteps);

        for (var step = -drift; step <= drift; step++)
        {
            var candidate = Compute(key, counter + step);

            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(normalised)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The code valid right now. Used only to render a development helper on the enrolment
    /// screen, so somebody without a phone to hand can still complete the flow.
    /// </summary>
    public string GetCurrentCode(string secret)
    {
        var key = FromBase32(secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / _security.TotpPeriodSeconds;

        return Compute(key, counter);
    }

    /// <summary>The RFC 6238 computation: HMAC the counter, then dynamically truncate.</summary>
    private string Compute(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);

        // The counter is big-endian on the wire; BitConverter is little-endian on x86.
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        // RFC 6238 specifies HMAC-SHA1, and every authenticator app implements it. The
        // security of TOTP rests on the shared secret and the 30-second window, not on the
        // hash resisting collisions - and switching to SHA-256 here would simply stop
        // Google Authenticator working. Suppressed at the call site rather than globally so
        // a genuinely weak hash elsewhere still fails the build.
#pragma warning disable CA5350 // Do not use weak cryptographic algorithms
        var hash = HMACSHA1.HashData(key, counterBytes);
#pragma warning restore CA5350

        // Dynamic truncation: the low nibble of the last byte picks where to read from.
        var offset = hash[^1] & 0x0F;

        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var modulo = (int)Math.Pow(10, _security.TotpDigits);

        return (binary % modulo).ToString(CultureInfo.InvariantCulture)
            .PadLeft(_security.TotpDigits, '0');
    }

    private static string ToBase32(byte[] data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsRemaining = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsRemaining += 8;

            while (bitsRemaining >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bitsRemaining - 5)) & 31]);
                bitsRemaining -= 5;
            }
        }

        if (bitsRemaining > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bitsRemaining)) & 31]);
        }

        return builder.ToString();
    }

    private static byte[] FromBase32(string encoded)
    {
        // Padding and spacing are stripped: people paste secrets with both.
        var cleaned = encoded.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        var output = new byte[cleaned.Length * 5 / 8];
        var buffer = 0;
        var bitsRemaining = 0;
        var index = 0;

        foreach (var character in cleaned)
        {
            var value = Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"'{character}' is not a Base32 character.");
            }

            buffer = (buffer << 5) | value;
            bitsRemaining += 5;

            if (bitsRemaining >= 8)
            {
                output[index++] = (byte)(buffer >> (bitsRemaining - 8));
                bitsRemaining -= 8;
            }
        }

        return output;
    }
}

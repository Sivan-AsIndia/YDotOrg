using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using YDots.DON.Application.Common.Abstractions.Services;
using YDots.DON.Application.Common.Settings;
using YDots.DON.Domain.ValueObjects;

namespace YDots.DON.Infrastructure.Services;

/// <summary>
/// Creates and checks the identity verification codes used by DON-UI-07.
///
/// Three deliberate choices. The code comes from RandomNumberGenerator, not Random, because a
/// predictable code defeats the whole exercise. Only the SHA-256 hash is stored, so a database
/// reader cannot pass somebody else's challenge. And the comparison is fixed-time, so the
/// number of matching leading characters cannot be measured by timing repeated attempts.
/// </summary>
public sealed class ChallengeCodeService(IOptions<DonorSettings> donorSettings) : IChallengeCodeService
{
    private readonly DonorSettings _settings = donorSettings.Value;

    public (string Code, string CodeHash) Create()
    {
        var digits = _settings.VerificationCodeDigits is >= 4 and <= 10 ? _settings.VerificationCodeDigits : 6;
        var upperBound = (int)Math.Pow(10, digits);

        var value = RandomNumberGenerator.GetInt32(0, upperBound);
        var code = value.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');

        return (code, Hash(code));
    }

    public bool Verify(string code, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var supplied = Encoding.UTF8.GetBytes(Hash(code.Trim()));
        var stored = Encoding.UTF8.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(supplied, stored);
    }

    public string MaskDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }

        return destination.Contains('@', StringComparison.Ordinal)
            ? EmailValue.Mask(destination)
            : PrimaryPhoneValue.Mask(destination);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

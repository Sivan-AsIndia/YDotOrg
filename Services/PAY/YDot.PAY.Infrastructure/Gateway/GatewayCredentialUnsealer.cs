using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.PAY.Application.Common.Settings;

namespace YDot.PAY.Infrastructure.Gateway;

/// <summary>
/// Opens a credential that IAM sealed.
///
/// THIS IS THE READ HALF OF IAM'S <c>PaymentSecretProtector</c> AND NOTHING ELSE. There is no
/// seal operation here, on purpose: PAY never writes a merchant credential, so a Protect method
/// would be a capability with no caller and an invitation to grow one.
///
/// IT MUST AGREE WITH IAM ON THREE THINGS or it opens nothing: the format
/// (<c>v1.nonce.tag.ciphertext</c>, each base64), the algorithm (AES-256-GCM with a 96-bit nonce
/// and a 128-bit tag), and the key. The first two are fixed here and there; the third comes from
/// configuration both services read - see <see cref="GatewayConfigurationSettings"/> for what
/// happens when they disagree.
///
/// A FAILURE TO OPEN IS NOT AN EXCEPTION. Null comes back, the caller falls through to the
/// deployment's configured credentials, and the donation either works that way or is refused
/// with PAYMENT_GATEWAY_NOT_CONFIGURED - which tells a donor to contact the charity. Throwing
/// would turn a configuration problem into a 500 on a donation page.
/// </summary>
internal sealed class GatewayCredentialUnsealer
{
    private const string Version = "v1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>
    /// The HKDF label. IT MUST BE BYTE-FOR-BYTE WHAT IAM USES - a different label derives a
    /// different key, and the symptom is every credential failing to open with nothing in the
    /// log to say why.
    /// </summary>
    private static readonly byte[] DerivationInfo =
        Encoding.UTF8.GetBytes("YDot.PaymentGatewayCredential.v1");

    private readonly byte[]? _key;
    private readonly ILogger<GatewayCredentialUnsealer> _logger;

    public GatewayCredentialUnsealer(
        IOptions<GatewayConfigurationSettings> settings,
        IOptions<JwtSettings> jwtSettings,
        ILogger<GatewayCredentialUnsealer> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(jwtSettings);

        _logger = logger;
        _key = ResolveKey(settings.Value, jwtSettings.Value, logger);
    }

    /// <summary>True when a key is available at all. False means every unseal returns null.</summary>
    public bool IsAvailable => _key is not null;

    public string? Unseal(string? cipherText)
    {
        if (_key is null || string.IsNullOrWhiteSpace(cipherText))
        {
            return null;
        }

        var parts = cipherText.Split('.');

        if (parts.Length != 4 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
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
            // THE TAG DID NOT VERIFY. Either the two services derived different keys, or the
            // column was altered. Logged WITHOUT the ciphertext, because the only distinguishing
            // detail is the value itself.
            _logger.LogError(
                "A stored merchant credential could not be opened. The usual cause is that PAY "
                + "and IAM derive different encryption keys - check that "
                + "PaymentGatewaySettings__EncryptionKey (or, failing that, "
                + "JwtSettings__SigningKey) is identical for both services. Donations for the "
                + "organisation concerned will fall back to the deployment's own credentials.");

            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// The key, or null when neither source is configured.
    ///
    /// NULL RATHER THAN A STARTUP FAILURE, which is the opposite of IAM's behaviour and is
    /// deliberate. IAM refuses to start because a service that accepts credentials it cannot
    /// seal properly is worse than one that will not start. PAY has a working fallback - the
    /// deployment's own configured credentials, which is how every donation was taken before
    /// this feature existed - so refusing to start would take payments down over a feature no
    /// Organisation might yet be using.
    /// </summary>
    private static byte[]? ResolveKey(
        GatewayConfigurationSettings settings, JwtSettings jwt, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(settings.EncryptionKey))
        {
            try
            {
                var configured = Convert.FromBase64String(settings.EncryptionKey.Trim());

                if (configured.Length == 32)
                {
                    return configured;
                }

                logger.LogError(
                    "PaymentGatewaySettings:EncryptionKey decodes to {Length} bytes; AES-256 "
                    + "needs 32. Tenant-configured gateway credentials cannot be opened until "
                    + "this is corrected.",
                    configured.Length);

                return null;
            }
            catch (FormatException)
            {
                logger.LogError(
                    "PaymentGatewaySettings:EncryptionKey is not valid base64. "
                    + "Tenant-configured gateway credentials cannot be opened until this is "
                    + "corrected.");

                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            logger.LogWarning(
                "Neither PaymentGatewaySettings:EncryptionKey nor JwtSettings:SigningKey is set, "
                + "so gateway credentials entered on the configuration screen cannot be read. "
                + "Donations will use the credentials in this deployment's own configuration.");

            return null;
        }

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(jwt.SigningKey),
            outputLength: 32,
            salt: null,
            info: DerivationInfo);
    }
}

namespace YDot.IAM.Application.Common.Abstractions.Security;

/// <summary>
/// Time-based one-time passwords for the authenticator-app factor (RFC 6238).
/// </summary>
public interface ITotpService
{
    /// <summary>A new Base32 shared secret for an enrolment.</summary>
    string GenerateSecret();

    /// <summary>
    /// The otpauth:// URI the QR code encodes. The issuer and account label are what the
    /// person sees in their authenticator, so both carry the Organisation name — somebody
    /// who administers three Organisations would otherwise get three identical entries.
    /// </summary>
    string BuildProvisioningUri(string secret, string accountName, string issuer);

    /// <summary>
    /// Verifies a code, tolerating a small clock drift either way. The window comes from
    /// SecuritySettings rather than being hard-coded, so it can be tightened.
    /// </summary>
    bool VerifyCode(string secret, string code);

    /// <summary>The code valid right now. Used only to render a development helper.</summary>
    string GetCurrentCode(string secret);
}

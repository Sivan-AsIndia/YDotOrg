namespace YDots.CAM.Application.Common.Settings;

/// <summary>
/// How CAM validates the tokens IAM signs.
///
/// CAM NEVER ISSUES A TOKEN, and that asymmetry is the reason this class is so much smaller
/// than the one in IAM: there is no lifetime, no refresh window and no rotation policy here,
/// because none of those are CAM decisions. It only needs to know the key, the issuer and the
/// audience well enough to check a signature.
///
/// THE SIGNING KEY MUST BE THE SAME STRING IAM SIGNS WITH. Supply it through
/// <c>JwtSettings__SigningKey</c> from the environment in both services; a mismatch presents as
/// every request answering 401 with nothing in the log to explain why.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "YDot.IAM";

    public string Audience { get; set; } = "YDot.Client";

    /// <summary>
    /// How much clock drift between IAM and CAM is tolerated on the expiry check.
    ///
    /// The framework default is five MINUTES, which quietly extends every token past its stated
    /// life. Thirty seconds is enough for real drift between two containers and nothing like
    /// enough to matter.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;
}

namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// How PAY validates the tokens IAM signs. PAY never issues one.
///
/// THE SIGNING KEY MUST BE THE SAME STRING IAM SIGNS WITH. A mismatch presents as every staff
/// request answering 401 with nothing in the log to explain why - while the anonymous donor
/// endpoints carry on working, which makes it look like a permissions problem rather than a
/// configuration one.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "YDot.IAM";

    public string Audience { get; set; } = "YDot.Client";

    /// <summary>
    /// Tolerated clock drift on the expiry check.
    ///
    /// The framework default is five MINUTES, which quietly extends every token past its stated
    /// life. Thirty seconds covers real drift between two containers.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;
}

namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// Where the Angular client lives, and where the donor is sent back to after paying.
///
/// A WILDCARD CORS ORIGIN IS NOT AN OPTION. The staff client sends its bearer token, and a
/// browser refuses to send credentials to a wildcard origin - so the list must be explicit.
/// </summary>
public sealed class ClientAppSettings
{
    public const string SectionName = "ClientAppSettings";

    public IList<string> AllowedOrigins { get; set; } = [];

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Where the gateway returns the donor to after payment.
    ///
    /// The intent reference is appended, so the result page can show the right donation without
    /// the donor needing an account.
    /// </summary>
    public string PaymentResultPath { get; set; } = "/give/result";

    /// <summary>Where the donor lands to activate the account created for them. Section 17.</summary>
    public string DonorActivationPath { get; set; } = "/auth/activate";
}

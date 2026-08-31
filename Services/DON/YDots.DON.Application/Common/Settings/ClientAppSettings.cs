namespace YDots.DON.Application.Common.Settings;

/// <summary>
/// Bound from the ClientAppSettings section of appsettings.json.
///
/// The refresh token IAM issues travels in an HttpOnly cookie, and a browser only sends a
/// cookie cross-origin when the response says AllowCredentials. The CORS specification forbids
/// combining AllowCredentials with "*", so the browser origins are listed explicitly here.
/// </summary>
public sealed class ClientAppSettings
{
    public const string SectionName = "ClientAppSettings";

    /// <summary>Root address of the Angular application, with no trailing slash.</summary>
    public string BaseUrl { get; set; } = "http://localhost:6700";

    public string ApplicationName { get; set; } = "YDot";

    /// <summary>Origins allowed to call this API with credentials.</summary>
    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:6700",
        "http://localhost:6701",
        "https://localhost:6701"
    ];
}

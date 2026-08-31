namespace YDots.CAM.Application.Common.Settings;

/// <summary>
/// Where the Angular client lives, which CAM needs for exactly one reason: to decide which
/// origins CORS permits.
///
/// A WILDCARD ORIGIN IS NOT AN OPTION HERE. The client sends its bearer token and its
/// credentials, and a browser refuses to send credentials to a wildcard origin - so the list
/// has to be explicit, and being explicit is also what stops another site calling this API with
/// a token it managed to obtain.
/// </summary>
public sealed class ClientAppSettings
{
    public const string SectionName = "ClientAppSettings";

    public IList<string> AllowedOrigins { get; set; } = [];

    /// <summary>The client base URL, used when a notification has to link back to a screen.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

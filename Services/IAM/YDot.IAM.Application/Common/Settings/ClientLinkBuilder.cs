namespace YDot.IAM.Application.Common.Settings;

/// <summary>
/// Builds the links that go out in e-mails.
///
/// THIS EXISTS BECAUSE SEVEN HANDLERS EACH BUILT THEIR OWN, and they did not agree. Every one
/// of them took the scheme from <see cref="ClientAppSettings.BaseUrl"/> and then pasted an
/// Organisation host on the front of a path — silently dropping the PORT. That is invisible in
/// production, where the client sits on 443 and there is no port to lose, and fatal anywhere the
/// client is not on the default port: a perfectly good reset link arrived as
/// "http://asd.localhost/auth/reset-password?token=…", the browser filled in port 80, and
/// whatever happened to be listening there answered instead of this application.
///
/// So the rule is one line, in one place: an Organisation link keeps the client's whole
/// authority — scheme AND port — and changes only the host.
/// </summary>
public static class ClientLinkBuilder
{
    /// <summary>
    /// A link to the platform host itself.
    ///
    /// For work that belongs to no single Organisation: a platform administrator reviewing a
    /// registration, or anyone following a link before an Organisation has been resolved.
    /// </summary>
    public static string PlatformUrl(this ClientAppSettings client, string path, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        return Compose(client.BaseUrl.TrimEnd('/'), path, token);
    }

    /// <summary>
    /// A link to one Organisation's own host, carrying the client's scheme and port across.
    ///
    /// The host must be the Organisation's, not the platform's: the Organisation is resolved
    /// FROM THE HOST, so a platform link resolves the wrong one — or none — and the token is
    /// then refused for a mismatch the recipient can neither see nor fix. Falls back to the
    /// platform host only when there is genuinely no Organisation host to use.
    /// </summary>
    public static string TenantUrl(
        this ClientAppSettings client, string? hostName, string path, string? token = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (string.IsNullOrWhiteSpace(hostName))
        {
            return client.PlatformUrl(path, token);
        }

        return Compose(client.OriginFor(hostName), path, token);
    }

    /// <summary>
    /// The origin for an Organisation host: the client's scheme and port, that host in the middle.
    /// </summary>
    public static string OriginFor(this ClientAppSettings client, string hostName)
    {
        ArgumentNullException.ThrowIfNull(client);

        var host = hostName.Trim().TrimEnd('/');

        // Anything already absolute is taken as given rather than rebuilt — a recorded host name
        // is allowed to carry its own origin.
        if (host.Contains("://", StringComparison.Ordinal))
        {
            return host;
        }

        if (!Uri.TryCreate(client.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            // Unparseable configuration: keep the scheme, and take the port if one is there.
            var scheme = client.BaseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
                ? "https"
                : "http";

            return $"{scheme}://{host}";
        }

        // IsDefaultPort is the whole point: :443 on https and :80 on http are left off, so
        // production links stay clean, while 6700 survives.
        var port = baseUri.IsDefaultPort ? string.Empty : $":{baseUri.Port}";

        return $"{baseUri.Scheme}://{host}{port}";
    }

    private static string Compose(string origin, string path, string? token)
    {
        var route = string.IsNullOrWhiteSpace(path) ? string.Empty
            : path.StartsWith('/') ? path
            : "/" + path;

        return string.IsNullOrEmpty(token)
            ? $"{origin}{route}"
            : $"{origin}{route}?token={Uri.EscapeDataString(token)}";
    }
}

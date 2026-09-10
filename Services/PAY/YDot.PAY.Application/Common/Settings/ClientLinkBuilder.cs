namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// Builds the links that go out on documents and in e-mails.
///
/// THE SAME RULE IAM SETTLED ON, for the same reason. An Organisation link keeps the client's
/// whole authority - scheme AND port - and changes only the host. Pasting an Organisation host
/// on the front of a path drops the port, which is invisible in production where the client sits
/// on 443 and fatal anywhere it does not: "http://ten1.localhost/app/..." sends the browser to
/// port 80 and whatever happens to be listening there answers instead of this application.
///
/// THE HOST MUST BE THE ORGANISATION'S. The Organisation is resolved FROM THE HOST, so a link
/// built on the platform host resolves the wrong one - or none - and the reader lands on a page
/// that cannot show them their own donation.
/// </summary>
public static class ClientLinkBuilder
{
    /// <summary>A link to the platform host itself, for anything owned by no one Organisation.</summary>
    public static string PlatformUrl(this ClientAppSettings client, string path)
    {
        ArgumentNullException.ThrowIfNull(client);

        return Compose(client.BaseUrl.TrimEnd('/'), path);
    }

    /// <summary>
    /// A link to one Organisation's own host, carrying the client's scheme and port across.
    ///
    /// Falls back to the platform host only when there is genuinely no Organisation host to
    /// use - a link to the right screen on the wrong host still beats no link at all.
    /// </summary>
    public static string TenantUrl(this ClientAppSettings client, string? hostName, string path)
    {
        ArgumentNullException.ThrowIfNull(client);

        return string.IsNullOrWhiteSpace(hostName)
            ? client.PlatformUrl(path)
            : Compose(client.OriginFor(hostName), path);
    }

    /// <summary>
    /// The origin for an Organisation host: the client's scheme and port, that host in the middle.
    /// </summary>
    public static string OriginFor(this ClientAppSettings client, string hostName)
    {
        ArgumentNullException.ThrowIfNull(client);

        var host = hostName.Trim().TrimEnd('/');

        // Anything already absolute is taken as given rather than rebuilt - a recorded host name
        // is allowed to carry its own origin.
        if (host.Contains("://", StringComparison.Ordinal))
        {
            return host.TrimEnd('/');
        }

        if (!Uri.TryCreate(client.BaseUrl, UriKind.Absolute, out var baseUri))
        {
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

    private static string Compose(string origin, string path)
    {
        var route = string.IsNullOrWhiteSpace(path) ? string.Empty
            : path.StartsWith('/') ? path
            : "/" + path;

        return $"{origin}{route}";
    }
}

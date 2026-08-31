using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Services;

/// <summary>
/// Turns a raw User-Agent string into the browser, operating system and client type the
/// sessions screen displays.
///
/// DELIBERATELY APPROXIMATE, and never used for an authorisation decision. User-Agent strings
/// are famously unreliable - every browser lies about being several others for historical
/// reasons, and the modern trend is to freeze them entirely. This produces something a person
/// recognises on their "your devices" list, which is all it is for.
///
/// ORDER MATTERS IN THE BROWSER CHECKS. Edge contains "Chrome", Chrome contains "Safari", and
/// Safari contains neither of the others - so the most specific has to be tested first or
/// every Edge session is reported as Chrome.
/// </summary>
public sealed class UserAgentParser : IUserAgentParser
{
    public ClientInfo Parse(string? userAgent, string? clientTypeHeader = null)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            // The header is still honoured, because a mobile client often sends a bare or
            // custom agent but does declare what it is.
            return Enum.TryParse<ClientType>(clientTypeHeader, ignoreCase: true, out var declared)
                ? new ClientInfo(declared, null, null, declared.ToString())
                : ClientInfo.Unknown;
        }

        var agent = userAgent;
        var browser = DetectBrowser(agent);
        var operatingSystem = DetectOperatingSystem(agent);
        var clientType = DetectClientType(agent, clientTypeHeader);

        var deviceName = browser is null && operatingSystem is null
            ? clientType.ToString()
            : string.Join(" on ", new[] { browser, operatingSystem }.Where(part => part is not null));

        return new ClientInfo(clientType, browser, operatingSystem, deviceName);
    }

    private static string? DetectBrowser(string agent)
    {
        // Most specific first. Edge and Opera both masquerade as Chrome; Chrome masquerades
        // as Safari.
        if (Contains(agent, "Edg/") || Contains(agent, "Edge/")) return "Edge";
        if (Contains(agent, "OPR/") || Contains(agent, "Opera")) return "Opera";
        if (Contains(agent, "SamsungBrowser")) return "Samsung Internet";
        if (Contains(agent, "Firefox") || Contains(agent, "FxiOS")) return "Firefox";
        if (Contains(agent, "CriOS")) return "Chrome";
        if (Contains(agent, "Chrome") || Contains(agent, "Chromium")) return "Chrome";
        if (Contains(agent, "Safari")) return "Safari";
        if (Contains(agent, "MSIE") || Contains(agent, "Trident")) return "Internet Explorer";
        if (Contains(agent, "PostmanRuntime")) return "Postman";
        if (Contains(agent, "curl")) return "curl";

        return null;
    }

    private static string? DetectOperatingSystem(string agent)
    {
        // Android before Linux, because every Android agent also says Linux.
        if (Contains(agent, "Android")) return "Android";
        if (Contains(agent, "iPhone")) return "iOS";
        if (Contains(agent, "iPad")) return "iPadOS";
        if (Contains(agent, "Windows NT 10")) return "Windows";
        if (Contains(agent, "Windows")) return "Windows";
        if (Contains(agent, "Mac OS X") || Contains(agent, "Macintosh")) return "macOS";
        if (Contains(agent, "CrOS")) return "ChromeOS";
        if (Contains(agent, "Linux")) return "Linux";

        return null;
    }

    private static ClientType DetectClientType(string agent, string? clientTypeHeader)
    {
        // An explicit declaration from the client wins: a native app knows what it is far
        // better than we can infer from its agent string.
        if (Enum.TryParse<ClientType>(clientTypeHeader, ignoreCase: true, out var declared)
            && declared != ClientType.Unknown)
        {
            return declared;
        }

        if (Contains(agent, "Mobile") || Contains(agent, "Android")
            || Contains(agent, "iPhone") || Contains(agent, "iPad"))
        {
            return ClientType.Mobile;
        }

        if (Contains(agent, "Electron")) return ClientType.Desktop;
        if (Contains(agent, "PostmanRuntime") || Contains(agent, "curl")) return ClientType.Api;

        return ClientType.Web;
    }

    private static bool Contains(string value, string token) =>
        value.Contains(token, StringComparison.OrdinalIgnoreCase);
}

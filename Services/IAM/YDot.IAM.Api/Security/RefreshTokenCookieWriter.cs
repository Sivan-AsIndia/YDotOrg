using Microsoft.Extensions.Options;
using YDot.IAM.Application.Common.Settings;

namespace YDot.IAM.Api.Security;

/// <summary>
/// Puts the refresh token into an HttpOnly cookie, and takes it back out again.
///
/// WHY A COOKIE AND NOT THE RESPONSE BODY. A refresh token is a long-lived credential. In a
/// response body it is readable by any JavaScript on the page, which means one cross-site
/// scripting flaw anywhere in the Angular app hands an attacker a persistent session. In an
/// HttpOnly cookie the browser attaches it to requests but no script can read it.
///
/// THE THREE FLAGS, AND WHY EACH ONE MATTERS:
///
/// <code>
/// HttpOnly   script cannot read it - the whole point
/// Secure     it never travels over plain HTTP
/// SameSite   None is REQUIRED here, because ten1.ngoplanet.com calling api.ngoplanet.com is
///            a cross-site request; None also forces Secure, which is correct
/// </code>
///
/// THE DOMAIN IS THE SUBTLE ONE. Setting it to ".ngoplanet.com" lets one cookie work across
/// every Organisation subdomain, which is what SuperAdmin switching needs. Leaving it unset
/// scopes the cookie to the exact host, which is tighter and is the right default in
/// development.
/// </summary>
public sealed class RefreshTokenCookieWriter(IOptions<JwtSettings> jwtOptions)
{
    private readonly JwtSettings _jwt = jwtOptions.Value;

    public void Write(HttpResponse response, string refreshToken, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        response.Cookies.Append(_jwt.RefreshTokenCookieName, refreshToken, BuildOptions(expiresAtUtc));
    }

    /// <summary>
    /// Reads the token, preferring the cookie.
    ///
    /// The body is the fallback for the mobile client, which has no cookie jar. The cookie
    /// wins when both are present, because it is the one that cannot have been supplied by
    /// script on the page.
    /// </summary>
    public string? Read(HttpRequest request, string? fromBody)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fromCookie = request.Cookies[_jwt.RefreshTokenCookieName];

        return string.IsNullOrWhiteSpace(fromCookie) ? fromBody : fromCookie;
    }

    /// <summary>
    /// Clears the cookie on sign-out.
    ///
    /// The options MUST match those used to write it — path, domain, secure and same-site — or
    /// the browser treats it as a different cookie and the original quietly survives.
    /// </summary>
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(
            _jwt.RefreshTokenCookieName, BuildOptions(DateTimeOffset.UtcNow.AddDays(-1)));
    }

    /// <summary>
    /// Writes the trusted-device token, which is the same kind of secret and gets the same
    /// treatment. Its lifetime is longer, because remembering a device is the point of it.
    /// </summary>
    public void WriteTrustedDevice(HttpResponse response, string deviceToken, int days)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            return;
        }

        response.Cookies.Append(
            TrustedDeviceCookieName,
            deviceToken,
            BuildOptions(DateTimeOffset.UtcNow.AddDays(days)));
    }

    public string? ReadTrustedDevice(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Cookies[TrustedDeviceCookieName];
    }

    private string TrustedDeviceCookieName => _jwt.RefreshTokenCookieName + "_device";

    private CookieOptions BuildOptions(DateTimeOffset expiresAtUtc) => new()
    {
        // Script cannot read it. This is the reason the cookie exists at all.
        HttpOnly = true,

        Secure = _jwt.RefreshTokenCookieSecure,

        SameSite = Enum.TryParse<SameSiteMode>(_jwt.RefreshTokenCookieSameSite, out var mode)
            ? mode
            : SameSiteMode.None,

        // Null means "this exact host", which is the tighter default.
        Domain = string.IsNullOrWhiteSpace(_jwt.RefreshTokenCookieDomain)
            ? null
            : _jwt.RefreshTokenCookieDomain,

        Path = "/",
        Expires = expiresAtUtc,
        IsEssential = true
    };
}

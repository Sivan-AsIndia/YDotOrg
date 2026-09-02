using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Abstractions.Services;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Security;

/// <summary>
/// Reads the visitor badge.
///
/// Every value here comes from a claim the JWT bearer middleware has already validated
/// against the signing key, issuer, audience and expiry. No handler ever parses a token
/// itself, and nothing in this class trusts a header, a body or a query string.
/// </summary>
public sealed class CurrentUser(
    IHttpContextAccessor httpContextAccessor,
    ITenantContext tenantContext,
    IUserAgentParser userAgentParser) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        ParseGuid(FindFirst(ClaimTypes.NameIdentifier) ?? FindFirst(ClaimTypeNames.UserId)) ?? Guid.Empty;

    public string? UserCode => FindFirst(ClaimTypeNames.UserCode);

    public string? DisplayName => FindFirst(ClaimTypeNames.DisplayName);

    public string? Username => FindFirst(ClaimTypeNames.Username);

    public string? Email => FindFirst(ClaimTypeNames.Email) ?? FindFirst(ClaimTypes.Email);

    public Guid? SessionId => ParseGuid(FindFirst(ClaimTypeNames.SessionId));

    public IReadOnlySet<string> Permissions =>
        Principal is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : Principal.FindAll(ClaimTypeNames.Permission)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<string> Roles =>
        Principal is null ? [] : [.. Principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)];

    public IReadOnlyList<string> DataScopes =>
        Principal is null ? [] : [.. Principal.FindAll(ClaimTypeNames.DataScope).Select(claim => claim.Value)];

    public PrivilegeLevel PrivilegeLevel =>
        Enum.TryParse<PrivilegeLevel>(FindFirst(ClaimTypeNames.PrivilegeLevel), out var parsed)
            ? parsed
            : PrivilegeLevel.Standard;

    public bool IsSuperAdmin =>
        string.Equals(FindFirst(ClaimTypeNames.IsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

    public bool IsTenantAdmin =>
        string.Equals(FindFirst(ClaimTypeNames.IsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

    public bool MfaCompleted =>
        string.Equals(FindFirst(ClaimTypeNames.MfaCompleted), "true", StringComparison.OrdinalIgnoreCase);

    public TokenType TokenType =>
        Enum.TryParse<TokenType>(FindFirst(ClaimTypeNames.TokenType), out var parsed)
            ? parsed
            : TokenType.Access;

    public string? SecurityStamp => FindFirst(ClaimTypeNames.SecurityStamp);

    public Guid? DepartmentId => ParseGuid(FindFirst(ClaimTypeNames.DepartmentId));

    public Guid? OrganisationUnitId => ParseGuid(FindFirst(ClaimTypeNames.OrganisationUnitId));

    /// <summary>One identifier tying the request, the log line and the audit row together.</summary>
    public string CorrelationId =>
        httpContextAccessor.HttpContext?.Items["CorrelationId"] as string
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString();

    /// <summary>
    /// The caller address.
    ///
    /// Read from the connection, NOT from X-Forwarded-For. That header is trivially forged, so
    /// trusting it directly would let anybody write whatever address they liked into the audit
    /// trail and slip past the per-IP rate limit. Behind a real proxy, ForwardedHeaders
    /// middleware rewrites the connection address from a configured list of trusted proxies,
    /// which is the only safe way to honour it.
    ///
    /// AN IPv4 ADDRESS IS WRITTEN IN IPv4 FORM. Kestrel listens on a dual-stack socket, so an
    /// ordinary IPv4 caller arrives as the mapped form <c>::ffff:172.20.0.1</c> and every
    /// session row, audit row and security screen showed that. It is the same address written
    /// in a way almost nobody reads, and it also means the SAME caller can be stored two ways
    /// depending on which socket the request landed on — so "how many sessions from this
    /// address" quietly gets the wrong answer. Unmapping here fixes every reader at once,
    /// because every one of them goes through this property.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;

            if (address is null)
            {
                return null;
            }

            // A genuine IPv6 caller is left exactly as it is; only the mapped IPv4 form is
            // unwrapped.
            return address.IsIPv4MappedToIPv6
                ? address.MapToIPv4().ToString()
                : address.ToString();
        }
    }

    public string? UserAgent
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Length > 400 ? value[..400] : value;
        }
    }

    public ClientType ClientType
    {
        get
        {
            // The token wins when there is one: it was signed at sign-in and records what the
            // client actually was. The header is only consulted before authentication.
            var fromToken = FindFirst(ClaimTypeNames.ClientType);
            if (Enum.TryParse<ClientType>(fromToken, out var parsed))
            {
                return parsed;
            }

            var header = httpContextAccessor.HttpContext?.Request.Headers["X-Client-Type"].ToString();
            return Enum.TryParse<ClientType>(header, ignoreCase: true, out var fromHeader)
                ? fromHeader
                : ClientType.Unknown;
        }
    }

    /// <summary>
    /// The browser and operating system, read off the User-Agent.
    ///
    /// BOTH OF THESE USED TO RETURN A LITERAL <c>null</c>, and the effect was visible on the
    /// security screen. <c>SessionTokenService</c> stamps a new session from this interface, so
    /// every session row was written with an empty browser and an empty operating system, and
    /// "Active sessions" showed a dash where the device should be — while the sign-in activity
    /// feed immediately below it said "Chrome on Windows", because the sign-in handler parses
    /// the agent itself. Two lists on one page, disagreeing about the same request.
    ///
    /// Parsing here rather than at each call site means the answer is the same everywhere it is
    /// read, and the parse is a handful of string comparisons over a header that is already in
    /// memory — see <see cref="IUserAgentParser"/> for why it is deliberately approximate and
    /// never used for an authorisation decision.
    ///
    /// Computed on demand and not cached: <c>ICurrentUser</c> is scoped to one request, so the
    /// agent cannot change underneath it, and a session is stamped once.
    /// </summary>
    public string? Browser => ParsedClient.Browser;

    public string? OperatingSystem => ParsedClient.OperatingSystem;

    private ClientInfo ParsedClient =>
        userAgentParser.Parse(UserAgent, httpContextAccessor.HttpContext?.Request.Headers["X-Client-Type"]);

    /// <summary>
    /// Device identifier from a mobile client.
    ///
    /// Taken from the token when present, so it reflects what was actually presented at
    /// sign-in rather than whatever a later request claims. Never used as an authentication
    /// factor on its own — it is capture, not proof.
    /// </summary>
    public string? DeviceIdentifier
    {
        get
        {
            var fromToken = FindFirst(ClaimTypeNames.DeviceIdentifier);
            if (!string.IsNullOrWhiteSpace(fromToken))
            {
                return fromToken;
            }

            var header = httpContextAccessor.HttpContext?.Request.Headers["X-Device-Id"].ToString();
            return string.IsNullOrWhiteSpace(header) ? null : header.Length > 200 ? header[..200] : header;
        }
    }

    public string? IdempotencyKey
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers["Idempotency-Key"].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Length > 200 ? value[..200] : value;
        }
    }

    /// <summary>
    /// The permission check.
    ///
    /// SUPERADMIN ALWAYS PASSES. Section 4.1 of the brief requires a root user to reach every
    /// Tenant module "without needing to be individually assigned every Tenant permission",
    /// which is also why their token carries no permission claims at all — writing a hundred
    /// and thirty of them would bloat every request for no benefit.
    ///
    /// This is NOT a hole in the Tenant boundary. WHICH Organisation a root user is inside is
    /// decided by <see cref="ITenantContext"/> and the query filters, and this method has no
    /// say in it whatsoever.
    /// </summary>
    public bool HasPermission(string permissionCode) =>
        IsSuperAdmin || Permissions.Contains(permissionCode);

    public bool HasAllPermissions(params string[] permissionCodes)
    {
        if (IsSuperAdmin)
        {
            return true;
        }

        var held = Permissions;
        return permissionCodes.All(held.Contains);
    }

    public bool HasAnyPermission(params string[] permissionCodes)
    {
        if (IsSuperAdmin)
        {
            return true;
        }

        var held = Permissions;
        return permissionCodes.Any(held.Contains);
    }

    /// <summary>
    /// The effective data scope handed to every read service.
    ///
    /// The Organisation comes from <see cref="ITenantContext"/> rather than straight from the
    /// claim, so a SuperAdmin who has switched Organisation mid-session is scoped to the one
    /// they switched to.
    /// </summary>
    public AccessScope Scope => new(
        tenantContext.BusinessUnitId,
        tenantContext.TenantId,
        UserId,
        tenantContext.Scope,
        DataScopes);

    private string? FindFirst(string claimType) => Principal?.FindFirst(claimType)?.Value;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YDot.IAM.Application.Common.Abstractions.Security;
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
    ITenantContext tenantContext) : ICurrentUser
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
    /// </summary>
    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

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

    public string? Browser => null;

    public string? OperatingSystem => null;

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

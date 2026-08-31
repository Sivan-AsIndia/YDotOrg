using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YDots.CAM.Application.Common.Abstractions.Security;
using YDots.CAM.Application.Common.Constants;
using YDots.CAM.Application.Common.Models;

namespace YDots.CAM.Infrastructure.Security;

/// <summary>
/// Reads the visitor badge.
///
/// WHAT THIS REPLACES, AND WHY IT MATTERED. The previous implementation returned two hard-coded
/// Guids with a comment saying "Default value for development purpose", <c>IsAuthenticated =>
/// false</c> and <c>HasPermission => false</c>. Three consequences followed and all three were
/// live:
///
///   - EVERY REQUEST OPERATED AS THE SAME FIXED ORGANISATION, whoever called it. There was no
///     Organisation isolation in the Campaign module at all.
///   - Every audit row recorded the same fake actor, so the trail could not answer who did
///     anything.
///   - <c>HasPermission</c> returning false meant no permission check could ever pass, so
///     nothing could rely on one - and nothing did.
///
/// Everything now comes from claims the JWT bearer middleware has already validated against the
/// IAM signing key, issuer and audience. CAM never parses a token itself and never calls IAM to
/// ask who somebody is: the signed token already says so, and the signature is what makes that
/// trustworthy.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        ParseGuid(FindFirst(ClaimTypes.NameIdentifier) ?? FindFirst(ClaimTypeNames.UserId)) ?? Guid.Empty;

    public IReadOnlySet<string> Permissions =>
        Principal is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : Principal.FindAll(ClaimTypeNames.Permission)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal);

    public string? DisplayName => FindFirst(ClaimTypeNames.DisplayName);

    public string? Username => FindFirst(ClaimTypeNames.Username);

    public string? Email => FindFirst(ClaimTypeNames.Email) ?? FindFirst(ClaimTypes.Email);

    public Guid? SessionId => ParseGuid(FindFirst(ClaimTypeNames.SessionId));

    public IReadOnlyList<string> Roles =>
        Principal is null ? [] : [.. Principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)];

    public IReadOnlyList<string> DataScopes =>
        Principal is null ? [] : [.. Principal.FindAll(ClaimTypeNames.DataScope).Select(claim => claim.Value)];

    public bool IsSuperAdmin =>
        string.Equals(FindFirst(ClaimTypeNames.IsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

    public bool IsTenantAdmin =>
        string.Equals(FindFirst(ClaimTypeNames.IsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>One identifier that ties the request, the log line and the audit row together.</summary>
    public string CorrelationId =>
        httpContextAccessor.HttpContext?.Items["CorrelationId"] as string
        ?? httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString();

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();

            // Truncated because the column is bounded and a crafted 8KB user-agent would
            // otherwise fail the insert rather than the request.
            return string.IsNullOrWhiteSpace(value) ? null : value.Length > 400 ? value[..400] : value;
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
    /// Permission check.
    ///
    /// SuperAdmin passes unconditionally, matching how IAM answers the same question: a root
    /// user reaches every Tenant module without being individually assigned every Tenant
    /// permission. It is not a hole in the Organisation boundary - which Organisation they are
    /// inside is decided by ITenantContext and the query filters, and this method has no say
    /// in it.
    /// </summary>
    public bool HasPermission(string permissionCode) =>
        IsSuperAdmin || Permissions.Contains(permissionCode);

    public bool HasAnyPermission(params string[] permissionCodes)
    {
        ArgumentNullException.ThrowIfNull(permissionCodes);

        return IsSuperAdmin || permissionCodes.Any(code => Permissions.Contains(code));
    }

    public AccessScope Scope => new(
        ParseGuid(FindFirst(ClaimTypeNames.TenantId) ?? FindFirst(ClaimTypeNames.OrganisationId))
        ?? Guid.Empty,
        UserId,
        DataScopes);

    private string? FindFirst(string claimType) => Principal?.FindFirst(claimType)?.Value;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}

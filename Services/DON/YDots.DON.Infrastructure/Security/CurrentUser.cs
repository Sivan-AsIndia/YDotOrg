using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Constants;
using YDots.DON.Application.Common.Models;

namespace YDots.DON.Infrastructure.Security;

/// <summary>
/// Reads the visitor badge.
///
/// Everything here comes from claims that the JWT bearer middleware has already validated
/// against the IAM signing key, issuer and audience. DON never parses a token itself and never
/// calls IAM to ask who somebody is: the signed token already says so, and the signature is
/// what makes that trustworthy.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        ParseGuid(FindFirst(ClaimTypes.NameIdentifier) ?? FindFirst(ClaimTypeNames.UserId)) ?? Guid.Empty;

    public Guid OrganisationId => ParseGuid(FindFirst(ClaimTypeNames.OrganisationId)) ?? Guid.Empty;

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
            return string.IsNullOrWhiteSpace(value) ? null : value.Length > 400 ? value[..400] : value;
        }
    }

    /// <summary>Section 10 idempotency: read from the Idempotency-Key request header.</summary>
    public string? IdempotencyKey
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers["Idempotency-Key"].ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Length > 200 ? value[..200] : value;
        }
    }

    public bool IsSuperAdmin =>
        string.Equals(FindFirst(ClaimTypeNames.IsSuperAdmin), "true", StringComparison.OrdinalIgnoreCase);

    public bool IsTenantAdmin =>
        string.Equals(FindFirst(ClaimTypeNames.IsTenantAdmin), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Permission check.
    ///
    /// THE SuperAdmin BYPASS WAS MISSING HERE, and DON was the only service without it. This was a
    /// plain `Permissions.Contains(...)`, so the platform root user was refused every donor and
    /// lead endpoint unless somebody had separately assigned them all fifty-odd `don.*` codes -
    /// while the same user passed straight through IAM, CAM and PAY.
    ///
    /// It is not a hole in the Organisation boundary. WHICH Organisation the request operates in
    /// comes from the resolved organisation id and the query filters; this method has no say in it.
    /// </summary>
    public bool HasPermission(string permissionCode) =>
        IsSuperAdmin || Permissions.Contains(permissionCode);

    public bool HasAnyPermission(params string[] permissionCodes)
    {
        if (IsSuperAdmin)
        {
            return true;
        }

        return permissionCodes is not null
            && permissionCodes.Any(code => Permissions.Contains(code));
    }

    public AccessScope Scope => new(OrganisationId, UserId, DataScopes);

    private string? FindFirst(string claimType) => Principal?.FindFirst(claimType)?.Value;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;
}

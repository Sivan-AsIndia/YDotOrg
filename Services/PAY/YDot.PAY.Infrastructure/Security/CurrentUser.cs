using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YDot.PAY.Application.Common.Abstractions.Security;
using YDot.PAY.Application.Common.Constants;
using YDot.PAY.Application.Common.Models;

namespace YDot.PAY.Infrastructure.Security;

/// <summary>
/// The authenticated actor, read from claims the JWT middleware has already validated against
/// the IAM signing key.
///
/// THIS SERVICE IS DIFFERENT FROM THE OTHER THREE IN ONE IMPORTANT WAY: many of its endpoints
/// have no authenticated caller at all. The public donation flow - sections 19 to 22 - is a
/// stranger with a QR code and no account. Every member here therefore returns a benign empty
/// value rather than throwing, and the donor-facing paths are written to expect that.
///
/// <see cref="UserId"/> RETURNING <c>Guid.Empty</c> IS A REAL VALUE HERE, not a failure. A
/// donation made by an anonymous donor genuinely has no creating user, and the audit row carries
/// the Organisation and the correlation id instead. Treating empty as an error would mean
/// refusing every public gift.
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

    /// <summary>
    /// One identifier tying the request, the log line and the audit row together.
    ///
    /// IT IS QUOTED BACK TO THE DONOR on the payment verification screen, so it is the string a
    /// support conversation starts from - which is why it must exist even for an anonymous
    /// request that has no user and no session.
    /// </summary>
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

    /// <summary>
    /// The Idempotency-Key header.
    ///
    /// IT CARRIES MORE WEIGHT IN THIS SERVICE THAN ANYWHERE ELSE. A repeated POST to a donation
    /// endpoint is a repeated charge, so the key is what lets the second call return the first
    /// call's answer rather than taking the money again.
    /// </summary>
    public string? IdempotencyKey
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers["Idempotency-Key"].ToString();

            return string.IsNullOrWhiteSpace(value) ? null : value.Length > 100 ? value[..100] : value;
        }
    }

    /// <summary>
    /// Permission check.
    ///
    /// SuperAdmin passes unconditionally, matching how IAM, CAM and DON answer the same question.
    /// It is not a hole in the Organisation boundary - which Organisation the request is inside
    /// is decided by <c>ITenantContext</c> and the query filters, and this method has no say in
    /// that.
    ///
    /// AN UNAUTHENTICATED CALLER FAILS EVERY CHECK, which is what makes it safe to compute a
    /// donor's permitted actions with this same method: they get Pay and Retry from the intent's
    /// own state, and never a staff action.
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

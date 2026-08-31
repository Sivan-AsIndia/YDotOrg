using YDots.CAM.Application.Common.Models;

namespace YDots.CAM.Application.Common.Abstractions.Security;

/// <summary>
/// The authenticated actor and their scope.
///
/// Think of it as reading the visitor badge the guard already checked at the door. Every value
/// comes from a claim the JWT bearer middleware has validated against the IAM signing key, so
/// no handler ever parses a token itself and CAM never calls IAM to ask who somebody is - the
/// signed token already says, and the signature is what makes that trustworthy.
///
/// TENANCY LIVES NEXT DOOR in <see cref="ITenantContext"/> rather than here. The two answer
/// different questions - who is calling, and which Organisation the request is operating in -
/// and they have different lifetimes: an anonymous public donation link resolves an
/// Organisation and has no user at all.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    bool IsAuthenticated { get; }

    string? DisplayName { get; }

    string? Username { get; }

    string? Email { get; }

    Guid? SessionId { get; }

    /// <summary>Permission codes carried by the token.</summary>
    IReadOnlySet<string> Permissions { get; }

    IReadOnlyList<string> Roles { get; }

    IReadOnlyList<string> DataScopes { get; }

    /// <summary>True when this caller is the platform root user.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>True when this caller administers the Organisation they are operating in.</summary>
    bool IsTenantAdmin { get; }

    /// <summary>One identifier tying the request, the log line and the audit row together.</summary>
    string CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    /// <summary>Value of the Idempotency-Key request header, when the caller sent one.</summary>
    string? IdempotencyKey { get; }

    /// <summary>
    /// Permission check.
    ///
    /// Returns true unconditionally for SuperAdmin, matching how IAM answers the same question:
    /// a root user reaches every Tenant module without being individually assigned every Tenant
    /// permission. It is not a hole in the Organisation boundary - WHICH Organisation they are
    /// inside is decided by <see cref="ITenantContext"/>, and this method has no say in it.
    /// </summary>
    bool HasPermission(string permissionCode);

    bool HasAnyPermission(params string[] permissionCodes);

    /// <summary>The effective data scope handed to every read service.</summary>
    AccessScope Scope { get; }
}

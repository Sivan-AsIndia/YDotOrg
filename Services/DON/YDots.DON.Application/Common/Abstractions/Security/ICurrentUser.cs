using YDots.DON.Application.Common.Models;

namespace YDots.DON.Application.Common.Abstractions.Security;

/// <summary>
/// The authenticated actor and their scope, exactly as the application interface table in
/// section 6 requires: UserId, OrganisationId and Permissions. The remaining members are what
/// the audit trail and the screen payloads need.
///
/// Think of it as reading the visitor badge that the guard already checked at the door. Every
/// value comes from a JWT claim that the authentication middleware has already validated, so
/// no handler ever parses a token itself.
/// </summary>
public interface ICurrentUser
{
    // ---- Section 6 contract -----------------------------------------------------------------

    Guid UserId { get; }

    Guid OrganisationId { get; }

    IReadOnlySet<string> Permissions { get; }

    // ---- Supporting members ------------------------------------------------------------------

    bool IsAuthenticated { get; }

    string? DisplayName { get; }

    string? Username { get; }

    string? Email { get; }

    Guid? SessionId { get; }

    IReadOnlyList<string> Roles { get; }

    IReadOnlyList<string> DataScopes { get; }

    /// <summary>True when this caller is the platform root user.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>True when this caller administers the Organisation they are operating in.</summary>
    bool IsTenantAdmin { get; }

    /// <summary>One identifier that ties the request, the log line and the audit row together.</summary>
    string CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    /// <summary>Value of the Idempotency-Key request header, when the caller sent one.</summary>
    string? IdempotencyKey { get; }

    /// <summary>
    /// Permission check.
    ///
    /// SuperAdmin passes unconditionally, matching how IAM, CAM and PAY answer the same question:
    /// a root user reaches every module without being individually assigned every permission in
    /// it. It is NOT a hole in the Organisation boundary - which Organisation a request operates
    /// in is decided by the query filters and the resolved organisation id, and this method has no
    /// say in that.
    /// </summary>
    bool HasPermission(string permissionCode);

    /// <summary>True when the caller holds any one of the codes. Same SuperAdmin rule.</summary>
    bool HasAnyPermission(params string[] permissionCodes);

    /// <summary>The effective data scope handed to every read service.</summary>
    AccessScope Scope { get; }
}

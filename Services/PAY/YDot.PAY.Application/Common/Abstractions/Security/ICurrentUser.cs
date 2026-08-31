using YDot.PAY.Application.Common.Models;

namespace YDot.PAY.Application.Common.Abstractions.Security;

/// <summary>
/// The authenticated actor and their scope, read from claims the JWT middleware has already
/// validated against the IAM signing key.
///
/// MANY PAY ENDPOINTS HAVE NO AUTHENTICATED CALLER AT ALL, which makes this service different
/// from the others. The public donation flow - sections 19 to 22 - is a stranger with a QR code
/// and no account. Every member here returns an empty value for those requests, and the
/// donor-facing endpoints are written to expect that rather than to assume a user.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    bool IsAuthenticated { get; }

    string? DisplayName { get; }

    string? Username { get; }

    string? Email { get; }

    Guid? SessionId { get; }

    IReadOnlySet<string> Permissions { get; }

    IReadOnlyList<string> Roles { get; }

    IReadOnlyList<string> DataScopes { get; }

    bool IsSuperAdmin { get; }

    bool IsTenantAdmin { get; }

    /// <summary>One identifier tying the request, the log line and the audit row together.</summary>
    string CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    /// <summary>
    /// The Idempotency-Key header.
    ///
    /// IT CARRIES MORE WEIGHT HERE THAN IN THE OTHER SERVICES. A repeated POST to a donation
    /// endpoint is a repeated charge, so the key is what lets the second call return the first
    /// call's answer rather than taking the money again.
    /// </summary>
    string? IdempotencyKey { get; }

    bool HasPermission(string permissionCode);

    bool HasAnyPermission(params string[] permissionCodes);

    AccessScope Scope { get; }
}

using YDot.IAM.Application.Common.Models;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Security;

/// <summary>
/// The authenticated actor and their scope.
///
/// Think of it as reading the visitor badge the guard already checked at the door. Every
/// value comes from a claim the authentication middleware has validated against the signing
/// key, so no handler ever parses a token itself.
///
/// Tenancy lives next door in <see cref="ITenantContext"/> rather than here, because the
/// Organisation context is resolved for anonymous requests too — sign-in has a Tenant but no
/// user.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    bool IsAuthenticated { get; }

    string? UserCode { get; }

    string? DisplayName { get; }

    string? Username { get; }

    string? Email { get; }

    Guid? SessionId { get; }

    /// <summary>Permission codes carried by the token.</summary>
    IReadOnlySet<string> Permissions { get; }

    IReadOnlyList<string> Roles { get; }

    IReadOnlyList<string> DataScopes { get; }

    PrivilegeLevel PrivilegeLevel { get; }

    bool IsSuperAdmin { get; }

    bool IsTenantAdmin { get; }

    /// <summary>True once the second factor has been satisfied for this session.</summary>
    bool MfaCompleted { get; }

    /// <summary>Access, MfaPending, StepUp or TenantSelectionPending.</summary>
    TokenType TokenType { get; }

    /// <summary>The security stamp the token was signed with, compared against the stored one.</summary>
    string? SecurityStamp { get; }

    Guid? DepartmentId { get; }

    Guid? OrganisationUnitId { get; }

    // ---- Request context -----------------------------------------------------------------

    /// <summary>One identifier tying the request, the log line and the audit row together.</summary>
    string CorrelationId { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    ClientType ClientType { get; }

    string? Browser { get; }

    string? OperatingSystem { get; }

    /// <summary>Device identifier reported by a mobile client, where one was supplied.</summary>
    string? DeviceIdentifier { get; }

    /// <summary>Value of the Idempotency-Key request header, when the caller sent one.</summary>
    string? IdempotencyKey { get; }

    /// <summary>
    /// Permission check.
    ///
    /// Returns true unconditionally for SuperAdmin. Section 4.1 of the brief requires that a
    /// root user reach every Tenant module "without needing to be individually assigned every
    /// Tenant permission", and this is where that is honoured. It is not a hole in the Tenant
    /// boundary: which Organisation they are inside is decided by
    /// <see cref="ITenantContext"/>, and this method has no say in it.
    /// </summary>
    bool HasPermission(string permissionCode);

    /// <summary>True when the caller holds every one of the given permissions.</summary>
    bool HasAllPermissions(params string[] permissionCodes);

    /// <summary>True when the caller holds at least one of the given permissions.</summary>
    bool HasAnyPermission(params string[] permissionCodes);

    /// <summary>The effective data scope handed to every read service.</summary>
    AccessScope Scope { get; }
}

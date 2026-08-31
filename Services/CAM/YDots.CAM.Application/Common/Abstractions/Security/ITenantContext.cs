namespace YDots.CAM.Application.Common.Abstractions.Security;

/// <summary>
/// Which Organisation the current request is operating in.
///
/// SEPARATE FROM <see cref="ICurrentUser"/> ON PURPOSE. The two answer different questions and
/// do not always both have an answer: a public donation link resolves an Organisation with no
/// authenticated user, and a SuperAdmin who has not yet selected an Organisation is
/// authenticated with no Tenant.
///
/// EVERYTHING TRUSTS THIS OBJECT COMPLETELY - the query filters, the write stamping, the audit
/// rows - so it is resolved once, from the validated token, by middleware that runs before any
/// handler. Nothing in the application layer can change it.
/// </summary>
public interface ITenantContext
{
    /// <summary>The resolved Organisation, or null when the request has none.</summary>
    Guid? TenantId { get; }

    /// <summary>The root boundary above Tenant.</summary>
    Guid BusinessUnitId { get; }

    string? TenantCode { get; }

    string? TenantName { get; }

    /// <summary>True once an Organisation has been resolved for this request.</summary>
    bool HasTenant { get; }

    /// <summary>True when the caller is operating with platform-wide scope.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// The Organisation, or an exception if there is none.
    ///
    /// A TENANT-OWNED WRITE MUST NOT PROCEED WITHOUT ONE. Returning Guid.Empty instead would
    /// write a row owned by nobody, which no query filter would ever return again - a silent
    /// data loss that looks like a successful save.
    /// </summary>
    Guid RequireTenantId();
}

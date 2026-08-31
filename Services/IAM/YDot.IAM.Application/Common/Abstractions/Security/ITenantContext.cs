using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Abstractions.Security;

/// <summary>
/// The Organisation the current request is operating in.
///
/// THIS IS THE SINGLE SOURCE OF TRUTH FOR TENANCY, and everything else keys off it:
///
/// <code>
/// request
///    |
///    v
/// TenantResolutionMiddleware      host name  -&gt; Tenant   (anonymous)
///    |                            token claim -&gt; Tenant   (authenticated, and it wins)
///    v
/// ITenantContext.TenantId
///    |
///    +--&gt; IamDbContext global query filters      reads cannot cross
///    +--&gt; IamDbContext.SaveChangesAsync stamping writes cannot cross
///    +--&gt; handlers, for the checks a filter cannot express
/// </code>
///
/// WHERE THE VALUE MAY COME FROM, IN PRIORITY ORDER:
///
///   1. The <c>tenant_id</c> claim of a validated JWT. Signed by IAM, so it cannot be edited
///      by the caller. This always wins.
///   2. The host name, for anonymous requests such as sign-in, which have no token yet.
///   3. An <c>X-Tenant</c> header, on loopback only, purely so the Angular dev server can
///      work without subdomains.
///
/// WHERE IT MUST NEVER COME FROM: a request body, a query string, or anything the browser
/// stored. Section 47 of the brief is explicit about this, and the reason is simple — those
/// are all caller-controlled, so trusting one turns the isolation boundary into a suggestion.
///
/// SUPERADMIN. A global caller has <see cref="Scope"/> = Global and a
/// <see cref="TenantId"/> that reflects whichever Organisation they selected. Their
/// persistent <c>User.TenantId</c> stays null throughout: selecting an Organisation changes
/// the request context and nothing else.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The Organisation being operated in, or null when there is none — a platform host, an
    /// unresolved host, or a SuperAdmin who has not chosen yet.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>The root boundary. Present even when no Organisation is resolved.</summary>
    Guid BusinessUnitId { get; }

    /// <summary>Organisation code, for logs and messages. Null when unresolved.</summary>
    string? TenantCode { get; }

    /// <summary>Organisation display name, for the header bar and e-mail subjects.</summary>
    string? TenantName { get; }

    /// <summary>Lifecycle status of the resolved Organisation, used to refuse sign-in when not Active.</summary>
    TenantStatus? TenantStatus { get; }

    /// <summary>Global for SuperAdmin, Tenant for everybody else.</summary>
    AccessScopeType Scope { get; }

    /// <summary>True when the caller is the platform root user.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// True when a Global-scope caller has selected an Organisation and is working inside it.
    /// Lets an endpoint tell "SuperAdmin acting as TEN001" from "SuperAdmin doing platform
    /// work", which the audit trail records differently.
    /// </summary>
    bool IsTenantMode { get; }

    /// <summary>True when an Organisation was resolved at all.</summary>
    bool HasTenant { get; }

    /// <summary>The host the request arrived on, normalised.</summary>
    string? HostName { get; }

    /// <summary>True when the host is a platform host rather than an Organisation subdomain.</summary>
    bool IsPlatformHost { get; }

    /// <summary>
    /// True when the query filters should be lifted for this scope.
    ///
    /// Reserved for the genuinely global reads: SuperAdmin listing every Organisation,
    /// platform-wide reporting. It is NOT set merely because the caller is SuperAdmin — a
    /// root user operating inside TEN001 is filtered to TEN001 like anybody else, which is
    /// exactly what section 48 asks for.
    /// </summary>
    bool IsGlobalQueryScope { get; }

    /// <summary>
    /// The Organisation a write should be stamped with. Throws when there is none, because a
    /// Tenant-owned row with no Organisation is a bug that must not reach the database.
    /// </summary>
    Guid RequireTenantId();
}

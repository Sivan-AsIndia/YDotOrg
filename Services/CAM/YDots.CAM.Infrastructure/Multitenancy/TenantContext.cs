using YDots.CAM.Application.Common.Abstractions.Security;

namespace YDots.CAM.Infrastructure.Multitenancy;

/// <summary>
/// The mutable, request-scoped implementation of <see cref="ITenantContext"/>.
///
/// WHY IT IS MUTABLE, AND WHY THAT IS SAFE. The Organisation is not known when the DI container
/// builds the graph - it is discovered part-way through the pipeline, by
/// <c>TenantResolutionMiddleware</c>, from the validated token. So this object is created empty
/// and filled in once.
///
/// The safety comes from three things:
///
///   1. It is registered SCOPED, so one instance serves exactly one request and cannot leak
///      into another.
///   2. <see cref="Set"/> is internal, so only the middleware in this assembly can call it - an
///      application handler has no way to change the Organisation it is operating in.
///   3. <see cref="_isResolved"/> makes it single-assignment. A second call is ignored rather
///      than honoured, so nothing downstream can quietly re-point the request at a different
///      Organisation half-way through.
///
/// That third point is the important one. Everything else in the module - the query filters,
/// the write stamping, the audit rows - trusts this object completely, so it must not be
/// possible to change it after the first read.
///
/// CAM DOES NOT RESOLVE AN ORGANISATION FROM A HOST NAME, unlike IAM. IAM has to, because it
/// serves anonymous sign-in requests that have no token yet. Every CAM endpoint requires an
/// authenticated caller, so the token is always available and is the only source consulted -
/// which removes a whole class of spoofing that a header or host-based path would open up.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private bool _isResolved;

    public Guid? TenantId { get; private set; }

    public Guid BusinessUnitId { get; private set; }

    public string? TenantCode { get; private set; }

    public string? TenantName { get; private set; }

    public bool IsSuperAdmin { get; private set; }

    public bool HasTenant => TenantId.HasValue;

    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException(
            "No organisation is resolved for this request. "
            + "An organisation-owned write cannot proceed without one.");

    /// <summary>
    /// Called once by the tenant-resolution middleware. Internal, so nothing in the application
    /// layer can reach it.
    ///
    /// Subsequent calls are ignored rather than throwing: the middleware may legitimately run
    /// after an earlier component has already resolved the context, and an exception there
    /// would turn a harmless duplicate into a 500.
    /// </summary>
    internal void Set(
        Guid? tenantId,
        Guid businessUnitId,
        string? tenantCode,
        string? tenantName,
        bool isSuperAdmin)
    {
        if (_isResolved)
        {
            return;
        }

        TenantId = tenantId;
        BusinessUnitId = businessUnitId;
        TenantCode = tenantCode;
        TenantName = tenantName;
        IsSuperAdmin = isSuperAdmin;

        _isResolved = true;
    }
}

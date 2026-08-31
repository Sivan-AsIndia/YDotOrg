using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Infrastructure.Multitenancy;

/// <summary>
/// The mutable, request-scoped implementation of <see cref="ITenantContext"/>.
///
/// WHY IT IS MUTABLE, AND WHY THAT IS SAFE. The Organisation is not known when the DI
/// container builds the graph — it is discovered part-way through the pipeline, by
/// <c>TenantResolutionMiddleware</c>, from either the validated token or the host name. So
/// this object is created empty and filled in once.
///
/// The safety comes from three things:
///
///   1. It is registered SCOPED, so one instance serves exactly one request and cannot leak
///      into another.
///   2. <see cref="Set"/> is internal, so only the middleware in this assembly can call it —
///      an application handler has no way to change the Organisation it is operating in.
///   3. <see cref="_isResolved"/> makes it single-assignment. A second call is ignored rather
///      than honoured, so nothing downstream can quietly re-point the request at a different
///      Organisation half-way through.
///
/// That third point is the important one. Everything else in the system — the query filters,
/// the write stamping, the audit rows — trusts this object completely, so it must not be
/// possible to change it after the first read.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private bool _isResolved;

    public Guid? TenantId { get; private set; }

    public Guid BusinessUnitId { get; private set; }

    public string? TenantCode { get; private set; }

    public string? TenantName { get; private set; }

    public TenantStatus? TenantStatus { get; private set; }

    public AccessScopeType Scope { get; private set; } = AccessScopeType.Tenant;

    public bool IsSuperAdmin { get; private set; }

    public bool IsTenantMode => Scope == AccessScopeType.Global && TenantId.HasValue;

    public bool HasTenant => TenantId.HasValue;

    public string? HostName { get; private set; }

    public bool IsPlatformHost { get; private set; }

    /// <summary>
    /// Lifts the query filters for a genuinely platform-wide read.
    ///
    /// DELIBERATELY NARROW. It is NOT set merely because the caller is SuperAdmin — a root
    /// user operating inside TEN001 is filtered to TEN001 exactly like anybody else, which is
    /// what section 48 of the brief requires. It is turned on only for the handful of reads
    /// that are about the platform itself, such as listing every Organisation, and only
    /// through <see cref="GlobalQueryScope"/>, which is a disposable that always puts it back.
    /// </summary>
    public bool IsGlobalQueryScope { get; private set; }

    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException(
            "No organisation is resolved for this request. A tenant-owned write cannot proceed without one.");

    /// <summary>
    /// Called once by the tenant-resolution middleware. Internal, so nothing in the
    /// application layer can reach it.
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
        TenantStatus? tenantStatus,
        AccessScopeType scope,
        bool isSuperAdmin,
        string? hostName,
        bool isPlatformHost)
    {
        if (_isResolved)
        {
            return;
        }

        TenantId = tenantId;
        BusinessUnitId = businessUnitId;
        TenantCode = tenantCode;
        TenantName = tenantName;
        TenantStatus = tenantStatus;
        Scope = scope;
        IsSuperAdmin = isSuperAdmin;
        HostName = hostName;
        IsPlatformHost = isPlatformHost;

        _isResolved = true;
    }

    /// <summary>
    /// Re-points the context at a different Organisation, for the SuperAdmin switch.
    ///
    /// Separate from <see cref="Set"/> because it deliberately bypasses the single-assignment
    /// guard, and only one caller ever needs that: the select-tenant endpoint, which has
    /// already proved the caller is Global scope. Refuses outright for anybody else, so the
    /// bypass cannot be borrowed.
    /// </summary>
    internal void SwitchTenant(Guid tenantId, string? tenantCode, string? tenantName, TenantStatus status)
    {
        if (Scope != AccessScopeType.Global)
        {
            throw new InvalidOperationException(
                "Only a global-scope caller can switch organisation.");
        }

        TenantId = tenantId;
        TenantCode = tenantCode;
        TenantName = tenantName;
        TenantStatus = status;
    }

    /// <summary>
    /// Opens a platform-wide read window. Always use it with <c>using</c>, so the filters go
    /// back on however the block exits.
    /// </summary>
    internal IDisposable EnterGlobalQueryScope() => new GlobalQueryScope(this);

    /// <summary>
    /// Turns the query filters off for the life of the block, and back on afterwards.
    ///
    /// This exists so the legitimate global reads have ONE obvious, greppable entry point,
    /// rather than <c>IgnoreQueryFilters()</c> appearing ad hoc across the repositories where
    /// nobody would notice a new one. The brief warns specifically about
    /// <c>IgnoreQueryFilters</c>, and this is the answer to that warning: it is used in one
    /// place, it is always paired, and it always restores.
    /// </summary>
    private sealed class GlobalQueryScope : IDisposable
    {
        private readonly TenantContext _context;
        private readonly bool _previous;

        internal GlobalQueryScope(TenantContext context)
        {
            _context = context;
            _previous = context.IsGlobalQueryScope;
            context.IsGlobalQueryScope = true;
        }

        public void Dispose() => _context.IsGlobalQueryScope = _previous;
    }
}

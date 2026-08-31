using YDot.PAY.Application.Common.Abstractions.Security;

namespace YDot.PAY.Infrastructure.Multitenancy;

/// <summary>
/// The mutable, request-scoped implementation of <see cref="ITenantContext"/>.
///
/// WHY IT IS MUTABLE, AND WHY THAT IS SAFE. The Organisation is not known when the container
/// builds the graph - it is discovered part-way through the pipeline. So this object is created
/// empty and filled in once. The safety comes from three things:
///
///   1. It is registered SCOPED, so one instance serves exactly one request.
///   2. Both setters are internal, so only this assembly can call them - no application handler
///      can change the Organisation it is operating in.
///   3. It is SINGLE-ASSIGNMENT. A second call is ignored rather than honoured, so nothing
///      downstream can re-point the request at a different Organisation half-way through.
///
/// THE THIRD POINT IS THE ONE THAT MATTERS MOST HERE. In CAM a re-pointed context would show the
/// wrong campaigns; in this service it would write a donation into another charity's books.
///
/// PAY HAS TWO SOURCES OF TRUTH, WHICH NO OTHER SERVICE DOES. A staff request resolves the
/// Organisation from the validated token, exactly as CAM does. A PUBLIC DONATION request has no
/// token at all - it arrives with an intent reference or a tracking reference, and the
/// Organisation is resolved from the row those name. <see cref="SetFromPublicContext"/> is that
/// second path, and it is deliberately a separate method rather than a second caller of
/// <see cref="Set"/> so that <see cref="IsPublicDonorContext"/> can record which one happened.
///
/// The public path is safe for one specific reason: an intent reference is twelve unguessable
/// characters that resolve to exactly one row. The caller is NAMING A RECORD that already
/// belongs to an Organisation, not CHOOSING which Organisation to act in. If references were
/// sequential, or resolvable to more than one row, this would be a hole rather than a feature.
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

    /// <summary>True when the Organisation came from a public donation reference, not a token.</summary>
    public bool IsPublicDonorContext { get; private set; }

    /// <summary>
    /// The Organisation, or an exception.
    ///
    /// A MONEY-OWNING WRITE MUST NOT PROCEED WITHOUT ONE. Returning Guid.Empty would write a
    /// donation owned by nobody - a row no query filter would ever return again, which is income
    /// that silently vanishes from the books. Failing loudly is the only correct behaviour.
    /// </summary>
    public Guid RequireTenantId() =>
        TenantId ?? throw new InvalidOperationException(
            "No organisation is resolved for this request. A donation, receipt or refund cannot "
            + "be written without one - it would be owned by nobody and invisible to every "
            + "subsequent query.");

    /// <summary>
    /// Called once by the tenant-resolution middleware, from the validated token.
    ///
    /// Subsequent calls are ignored rather than throwing: the middleware may legitimately run
    /// after something else has already resolved the context, and an exception there would turn
    /// a harmless duplicate into a 500.
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
        IsPublicDonorContext = false;

        _isResolved = true;
    }

    /// <summary>
    /// Resolves the Organisation from a public donation reference, where there is no token.
    ///
    /// IT CANNOT OVERRIDE A TOKEN-RESOLVED ORGANISATION, because of the same single-assignment
    /// guard. That ordering matters: an authenticated staff member who happens to open a public
    /// donation link keeps operating in their own Organisation rather than being silently moved
    /// into the one the link belongs to.
    ///
    /// IT NEVER GRANTS SUPER-ADMIN, and never can. A public caller is by definition the least
    /// privileged actor on the platform.
    /// </summary>
    internal void SetFromPublicContext(Guid tenantId, Guid businessUnitId, string? tenantCode)
    {
        if (_isResolved)
        {
            return;
        }

        TenantId = tenantId;
        BusinessUnitId = businessUnitId;
        TenantCode = tenantCode;
        TenantName = null;
        IsSuperAdmin = false;
        IsPublicDonorContext = true;

        _isResolved = true;
    }
}

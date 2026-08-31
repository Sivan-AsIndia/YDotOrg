using YDots.DON.Application.Common.Abstractions.Security;

namespace YDots.DON.Infrastructure.Multitenancy;

/// <summary>
/// The mutable, request-scoped implementation of <see cref="ITenantContext"/>.
///
/// WHY IT IS MUTABLE, AND WHY THAT IS SAFE. The Organisation is not known when the DI container
/// builds the graph - it is discovered part-way through the pipeline, by
/// <c>OrganisationResolutionMiddleware</c>, from the validated token. So this object is created
/// empty and filled in once.
///
/// The safety comes from three things: it is registered SCOPED, so one instance serves exactly
/// one request; <see cref="Set"/> is internal, so only the middleware in this assembly can call
/// it; and <see cref="_isResolved"/> makes it single-assignment, so nothing downstream can
/// quietly re-point the request at a different Organisation half-way through.
///
/// That third point is the important one. The query filters trust this object completely, so it
/// must not be possible to change it after the first read.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private bool _isResolved;

    public Guid? OrganisationId { get; private set; }

    public bool HasOrganisation => OrganisationId.HasValue;

    public Guid RequireOrganisationId() =>
        OrganisationId ?? throw new InvalidOperationException(
            "No organisation is resolved for this request. "
            + "An organisation-owned write cannot proceed without one.");

    /// <summary>
    /// Called once by the resolution middleware. Internal, so nothing in the application layer
    /// can reach it.
    ///
    /// A second call is ignored rather than throwing: the middleware may legitimately run after
    /// something else has already resolved the context, and an exception there would turn a
    /// harmless duplicate into a 500.
    /// </summary>
    internal void Set(Guid? organisationId)
    {
        if (_isResolved)
        {
            return;
        }

        OrganisationId = organisationId;
        _isResolved = true;
    }
}

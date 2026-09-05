using YDot.IAM.Application.Common.Abstractions.Security;
using YDot.IAM.Application.Common.Constants;
using YDot.IAM.Application.Common.Results;

namespace YDot.IAM.Application.Features.Configuration.PaymentGateways;

/// <summary>
/// Who may configure whose merchant account.
///
/// THE WHOLE ACCESS MODEL OF THIS FEATURE, IN ONE PLACE. It is short, and it is the piece worth
/// reading twice:
///
///   SUPERADMIN    may read every Organisation's configuration and write any of them, but must
///                 SAY WHICH. There is no "all organisations" write.
///   TENANTADMIN   may read and write their OWN Organisation's, and the Organisation comes from
///                 their token. A TenantId in the request body is ignored, not honoured and not
///                 rejected - ignored, because a rejection would tell a prober that the field is
///                 read at all.
///   EVERYBODY ELSE never reaches here: the four permission codes are in
///                 <c>RoleAccessProfiles.AdministratorOnlyCodes</c>, so no system role carries
///                 them and the endpoint attributes refuse the request before a handler runs.
///
/// WHY THE BODY FIELD EXISTS AT ALL. A root user supporting an Organisation needs to fix its
/// gateway without impersonating somebody, and the alternative - entering the Organisation
/// first - changes their whole session for one edit.
/// </summary>
public sealed class PaymentGatewayScope(ICurrentUser currentUser, ITenantContext tenantContext)
{
    public bool IsSuperAdmin => currentUser.IsSuperAdmin || tenantContext.IsSuperAdmin;

    public Guid BusinessUnitId => tenantContext.BusinessUnitId;

    /// <summary>True when the caller may see across Organisations rather than only their own.</summary>
    public bool CanReadAllOrganisations => IsSuperAdmin;

    /// <summary>
    /// The Organisation a write applies to.
    ///
    /// A FAILURE HERE IS THE COMMON CASE FOR A ROOT USER who has neither entered an Organisation
    /// nor named one, and the message says so rather than reporting a missing tenant context -
    /// which is true but unhelpful, since they have nothing to fix in their own session.
    /// </summary>
    public Result<Guid> ResolveWriteTenant(Guid? requestedTenantId)
    {
        if (!IsSuperAdmin)
        {
            // The token's Organisation, always. The request body has no say.
            var ownTenantId = tenantContext.TenantId;

            return ownTenantId.HasValue && ownTenantId.Value != Guid.Empty
                ? ownTenantId.Value
                : Result.Failure<Guid>(Error.TenantNotResolved(
                    "Your organisation could not be determined, so there is nothing to configure."));
        }

        if (requestedTenantId.HasValue && requestedTenantId.Value != Guid.Empty)
        {
            return requestedTenantId.Value;
        }

        // A root user standing inside an Organisation is configuring that one.
        if (tenantContext.TenantId is { } current && current != Guid.Empty)
        {
            return current;
        }

        return Result.Failure<Guid>(Error.TenantSelectionRequired(
            "Choose an organisation before configuring a payment gateway. A gateway belongs to "
            + "one organisation's merchant account, so there is no platform-wide setting."));
    }

    /// <summary>
    /// The Organisation a READ applies to, or null for "every Organisation in scope".
    ///
    /// Null is only ever returned to a root user. For anybody else the filter's TenantId is
    /// discarded and their own is used, so a crafted query string buys nothing.
    /// </summary>
    public Guid? ResolveReadTenant(Guid? requestedTenantId)
    {
        if (!IsSuperAdmin)
        {
            return tenantContext.TenantId;
        }

        return requestedTenantId.HasValue && requestedTenantId.Value != Guid.Empty
            ? requestedTenantId.Value
            : tenantContext.TenantId;
    }

    /// <summary>
    /// What this caller may do to a configuration, decided here so the buttons on the screen
    /// cannot disagree with what the API will allow.
    /// </summary>
    public IReadOnlyList<string> PermittedActions(bool isActive)
    {
        var actions = new List<string>(5);

        if (currentUser.HasPermission(PermissionCodes.PaymentGatewaysManage))
        {
            actions.Add("edit");
            actions.Add(isActive ? "deactivate" : "activate");
        }

        if (currentUser.HasPermission(PermissionCodes.PaymentGatewaysTest))
        {
            actions.Add("test");
        }

        // DELETE IS OFFERED ONLY ON AN INACTIVE ROW, and the handler refuses it on an active one
        // regardless of what the client sends. Removing the configuration donations are
        // currently flowing through stops every payment for that Organisation, and the two-step
        // - stand it down, then delete it - is what makes that a decision rather than a slip.
        if (!isActive && currentUser.HasPermission(PermissionCodes.PaymentGatewaysDelete))
        {
            actions.Add("delete");
        }

        return actions;
    }
}

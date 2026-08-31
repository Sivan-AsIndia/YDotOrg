using YDot.IAM.Application.Common.Models;

namespace YDot.IAM.Application.Common.Services;

/// <summary>
/// Assembles the navigation tree the client renders.
///
/// THE TREE IS BUILT ON THE SERVER, DELIBERATELY. Four things have to be combined before
/// anything can be drawn — the global catalogue, the Organisation overrides, the role
/// mappings and the caller permission set — and sending the raw tables to Angular to join
/// would put an authorisation decision in the browser, where it can be edited.
///
/// A NODE THE CALLER CANNOT USE IS NOT RETURNED AT ALL. Not disabled, not greyed out:
/// absent. A greyed-out item still tells somebody the screen exists and invites them to go
/// looking for it.
///
/// THE ORDER OF THE FILTERS MATTERS:
///
/// <code>
/// 1. catalogue           what screens exist in this build
/// 2. platform-only       dropped unless the caller is SuperAdmin
/// 3. no Organisation     Tenant work dropped for a SuperAdmin who has not entered one
/// 4. Organisation switch dropped if this Tenant disabled the node
/// 5. approval            dropped while the Organisation is still onboarding
/// 6. permission          dropped if the caller lacks RequiredPermissionCode
/// 7. role mapping        dropped if every one of the caller roles hides it
/// 8. empty groups        a group whose children all vanished is dropped too
/// </code>
///
/// Step 8 is what stops the sidebar showing a heading that expands to nothing.
///
/// STEP 5 IS A LIFECYCLE GATE, NOT A PERMISSION ONE. An Organisation that has not been
/// approved sees only the screens onboarding needs, however complete its administrator's
/// permission set is - and a TenantAdmin's is complete from the moment the Organisation is
/// created. SuperAdmin is exempt, because reviewing a submission means entering an
/// Organisation that is by definition not approved yet.
/// </summary>
public interface IMenuBuilderService
{
    /// <summary>
    /// The navigation for one caller, in the Organisation they are operating in.
    ///
    /// SuperAdmin gets the platform branch plus the full Tenant tree of whichever
    /// Organisation they selected, which is what section 4.1 means by seeing "the same Tenant
    /// menus/modules available to TenantAdmin".
    /// </summary>
    Task<IReadOnlyList<MenuNode>> BuildForCurrentUserAsync(CancellationToken cancellationToken);

    /// <summary>The navigation a given role would see. Used by the menu-mapping screen preview.</summary>
    Task<IReadOnlyList<MenuNode>> BuildForRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// The whole catalogue as a tree, with no permission filtering, for the configuration
    /// screens. Reaching it requires the menu-configure permission.
    /// </summary>
    Task<IReadOnlyList<MenuNode>> BuildCatalogueAsync(
        Guid? tenantId, bool includePlatformNodes, CancellationToken cancellationToken);

    /// <summary>Where a caller should land after signing in, honouring any role landing page.</summary>
    Task<string?> ResolveLandingRouteAsync(CancellationToken cancellationToken);
}

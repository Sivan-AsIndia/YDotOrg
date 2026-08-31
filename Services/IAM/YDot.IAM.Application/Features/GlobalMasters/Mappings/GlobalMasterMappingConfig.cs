using YDot.IAM.Application.Features.GlobalMasters.DTOs;
using YDot.IAM.Domain.Common;
using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.GlobalMasters.Mappings;

/// <summary>
/// The pieces every master mapping needs: how a status reads, what a record's state allows,
/// and how a row's scope is labelled.
///
/// WHY A SHARED FILE RATHER THAN A COPY IN EACH OF THE FIVE. "Active" has to read the same
/// way on the currency screen as on the city screen, and <see cref="PermittedActionsFor"/> is
/// the rule that stops the client drawing a button the server will refuse. Five copies of
/// that rule is four opportunities for one screen to offer Delete on a platform row.
/// </summary>
public static class GlobalMasterMappingConfig
{
    /// <summary>
    /// Turns the body of an <c>/activate</c> or <c>/deactivate</c> call into the command the
    /// handler expects, with the direction supplied by the ROUTE rather than by the caller.
    ///
    /// This is the small piece of glue that keeps the two routes separately permissioned: the
    /// status is never read from the request, so holding <c>gm.countries.activate</c> cannot be
    /// used to deactivate anything.
    /// </summary>
    public static ChangeMasterStatusRequest ToCommandRequest(
        this MasterStatusChangeRequest request, MasterDataStatus status)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ChangeMasterStatusRequest(status, request.ExpectedVersion, request.Reason);
    }

    /// <summary>The human-readable form of a master status, for the grid's status chip.</summary>
    public static string DescribeStatus(MasterDataStatus status) => status switch
    {
        MasterDataStatus.Draft => "Draft - not offered for selection",
        MasterDataStatus.Active => "Active",
        MasterDataStatus.Inactive => "Inactive - kept for existing records only",
        _ => status.ToString()
    };

    /// <summary>How a row's ownership is labelled in the grid and the export.</summary>
    public static string DescribeScope(bool isPlatformRow) =>
        isPlatformRow ? "Platform" : "Organisation";

    /// <summary>
    /// What the RECORD allows, before permission is considered. The controller checks
    /// permission separately on each endpoint; this answers the different question of whether
    /// the action makes sense against this row at all.
    ///
    /// THE PLATFORM-ROW RULE IS THE IMPORTANT ONE. A seeded country belongs to the platform,
    /// so a Tenant caller gets View and nothing else however their roles are configured — and
    /// the screen, reading this list, does not draw an Edit pencil that would answer 403.
    /// SuperAdmin gets the full set, because maintaining that catalogue is their job.
    ///
    /// <paramref name="dependentCount"/> is what makes Delete honest: a country with states
    /// beneath it cannot be removed, and offering the button anyway would produce a 409 the
    /// operator could have been spared.
    /// </summary>
    public static IReadOnlyList<string> PermittedActionsFor(
        GlobalMasterEntity entity, bool isSuperAdmin, int dependentCount)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var actions = new List<string> { "View", "Export" };

        // A platform row is read-only to everybody but the platform.
        if (entity.IsPlatformRow && !isSuperAdmin)
        {
            return actions;
        }

        actions.Add("Edit");

        switch (entity.Status)
        {
            case MasterDataStatus.Active:
                actions.Add("Deactivate");
                break;

            case MasterDataStatus.Draft:
            case MasterDataStatus.Inactive:
                actions.Add("Activate");
                break;
        }

        // Nothing may be deleted while something still points at it. Retiring it is the
        // operation that remains available, and it is the one that is almost always meant.
        if (dependentCount == 0)
        {
            actions.Add("Delete");
        }

        return actions;
    }

    /// <summary>
    /// The <c>isActive</c> boolean the existing Masters grids bind their toggle to.
    ///
    /// Kept alongside the richer <c>Status</c> because the Angular screens were written
    /// against a two-state model and rewriting five working grids to change a checkbox into a
    /// tri-state select is not an improvement anybody asked for. Draft and Inactive both read
    /// as false, which is exactly what a toggle labelled "Active" should show for them.
    /// </summary>
    public static bool IsActiveFlag(MasterDataStatus status) => status == MasterDataStatus.Active;
}

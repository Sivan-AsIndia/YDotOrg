namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The roles the seeder creates inside every Organisation, and what each one holds.
///
/// THREE ROLES, NOT FOURTEEN. The set this replaces modelled job titles - Campaign Manager,
/// Finance Officer, Data Steward, Donor Care - which put IAM in the position of deciding, for
/// every permission any module ever added, which of thirteen job descriptions ought to own it.
/// That is a question IAM cannot answer, and the evidence that it was answering it badly is in
/// the catalogue: several codes belonged to no role at all, so the only account that could reach
/// the screens behind them was the Organisation administrator.
///
/// These three model AUTHORITY, which is a question IAM CAN answer, because authority is what an
/// identity platform is for:
///
///   TENANT_ADMIN  everything, inside one Organisation
///   INITIATOR     does the work and approves none of it
///   APPROVER      decides the work and creates none of it
///
/// A FOURTH ROLE, DONOR, IS SEEDED ALONGSIDE THEM AND IS NOT ONE OF THE THREE. It is not a level
/// of authority over the Organisation's records; it is a member of the public with a login,
/// scoped to their own giving. It exists because a donation converts a lead into an account
/// holder, and that account has to land somewhere other than the staff default.
///
/// An Organisation that wants job-shaped roles builds them itself, in Roles and Permissions, from
/// the same catalogue. That is the right place for a decision about how one charity is staffed.
///
/// THE FOURTH ROLE, SUPER_ADMIN, IS NOT HERE. It is a platform role with a null TenantId, seeded
/// once by <c>SeedPlatformRoleAsync</c>, and it is never copied into an Organisation.
/// </summary>
public static class TenantRoleDefinitions
{
    /// <summary>
    /// One role, as the seeder needs it.
    ///
    /// <paramref name="GrantsAll"/> is the alternative to enumerating grants: a role carrying it
    /// holds every Tenant permission that exists, now and in future, with no RolePermission rows
    /// at all. It is how TENANT_ADMIN avoids needing re-mapping in every customer database each
    /// time a module ships a permission.
    /// </summary>
    public sealed record RoleDefinition(
        string Code,
        string Name,
        string Description,
        int Priority,
        IReadOnlyList<string> PermissionCodes,
        bool GrantsAll = false,
        bool IsDefault = false,
        bool IsPrivileged = false);

    public static readonly IReadOnlyList<RoleDefinition> All =
    [
        // ============ TENANT_ADMIN ==========================================================
        //
        // Full control of one Organisation and nothing outside it. The scoping is not a property
        // of this role at all - it comes from TenantId on the row and the Organisation filter on
        // every query - which is why "full control" here is safe to express as GrantsAll.
        new(
            RoleCodes.TenantAdmin,
            "Organisation Administrator",
            "Full control of this organisation: users, roles, menus, settings and every module. "
            + "Scoped to this organisation and never beyond it.",
            Priority: 100,
            PermissionCodes: [],
            GrantsAll: true,
            IsPrivileged: true),

        // ============ INITIATOR =============================================================
        //
        // The maker. Everything in IAM, CAM, DON and PAY except approvals - see
        // RoleAccessProfiles for how that set is computed and why it is computed rather than
        // listed.
        //
        // IT IS THE DEFAULT ROLE. A new user created without one chooses work over authority,
        // which is the safer of the two mistakes: an account that can raise things but decide
        // none of them cannot approve its own work by accident.
        new(
            RoleCodes.Initiator,
            "Initiator",
            "Creates, edits, submits and deletes across IAM, Campaigns, Donors and Payments. "
            + "Approves nothing - everything raised here waits for an Approver.",
            Priority: 60,
            PermissionCodes: RoleAccessProfiles.Initiator,
            IsDefault: true),

        // ============ APPROVER ==============================================================
        //
        // The checker. Views, edits and approves, plus the operations that follow a decision -
        // and creates and deletes nothing.
        //
        // MARKED PRIVILEGED, which INITIATOR is not. The flag drives the enhanced audit rows and
        // the access-review campaigns: the ability to approve is the thing worth reviewing
        // periodically, and the ability to type is not.
        new(
            RoleCodes.Approver,
            "Approver",
            "Approves, edits and views across IAM, Campaigns, Donors and Payments, and runs the "
            + "operations that follow a decision. Creates and deletes nothing.",
            Priority: 75,
            PermissionCodes: RoleAccessProfiles.Approver,
            IsPrivileged: true),

        // ============ DONOR =================================================================
        //
        // The person who gives, once they have an account. Not staff, and not a reduced member
        // of staff either - see RoleCodes.Donor for why it is not on the authority ladder.
        //
        // NOT THE DEFAULT ROLE, and it must never become one: the default is what an account
        // created with no roles falls back to, and a staff account that quietly became a donor
        // would lose every screen it needs. CreateUserCommand picks this one from the account
        // CATEGORY instead, which is the fact that actually distinguishes the two.
        //
        // NOT PRIVILEGED. The flag drives enhanced audit and periodic access review, and both
        // exist for authority somebody could misuse against the Organisation. A donor can reach
        // one person's records - their own - so reviewing them quarterly would bury the reviews
        // that matter under a list of every donor who has ever given.
        //
        // LOWEST PRIORITY, so a donor who is also a member of staff - a volunteer who gives, an
        // employee who gives - is labelled and sorted by the staff role they hold. Holding both
        // is legitimate and neither role takes anything away from the other.
        new(
            RoleCodes.Donor,
            "Donor",
            "Views and pays their own donations and views their own receipts. Sees no other "
            + "donor's records and no staff screens.",
            Priority: 10,
            PermissionCodes: RoleAccessProfiles.Donor)
    ];
}

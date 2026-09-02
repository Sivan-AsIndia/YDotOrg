namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// The role codes the Campaign module recognises, mirrored from IAM where the roles themselves
/// are created.
///
/// THREE TENANT ROLES, AND ONLY THREE. CAMPAIGN_MANAGER and CAMPAIGN_OWNER used to be declared
/// here and are gone. They modelled job titles rather than authority, they had no counterpart
/// left in <c>YDot.IAM.Application/Common/Constants/RoleCodes.cs</c> after IAM reduced its
/// catalogue to four, and nothing in CAM ever read them - so a token could never carry either
/// one, and any rule written against them would have been dead the moment it was written.
/// Everything they used to mean is now expressed by what a caller can DO:
///
///   CAMPAIGN_MANAGER, when it approved  -> APPROVER      (or TENANT_ADMIN)
///   CAMPAIGN_MANAGER, when it prepared  -> INITIATOR
///   CAMPAIGN_OWNER                      -> INITIATOR, plus a row in cam_campaign_owners
///
/// OWNERSHIP IS NOT A ROLE, and separating the two is what let the third mapping above work.
/// Being accountable for a campaign is a fact about that campaign - a <c>CampaignOwner</c> row -
/// not a fact about the person's authority in the Organisation. It scopes what they SEE
/// (<c>AccessScope.IsOwnRecordsOnly</c>); their role decides what they may DO.
///
/// CAM DOES NOT CREATE OR ASSIGN ROLES. It reads role claims off the token, and the
/// authoritative definitions live in IAM's <c>RoleCodes</c> and <c>TenantRoleDefinitions</c>.
/// </summary>
public static class RoleCodes
{
    /// <summary>The platform root. Not a tenant role and never copied into an Organisation.</summary>
    public const string SuperAdmin = "SUPER_ADMIN";

    /// <summary>
    /// Everything, inside one Organisation. Holds every tenant-assignable permission through
    /// <c>GrantsAllTenantPermissions</c> rather than through enumerated grants.
    /// </summary>
    public const string TenantAdmin = "TENANT_ADMIN";

    /// <summary>
    /// The maker: views, creates, edits, submits, deletes and exports - and approves nothing.
    /// Anything raised here stops at the approval gate and waits for somebody else.
    /// </summary>
    public const string Initiator = "INITIATOR";

    /// <summary>
    /// The checker: views, edits, approves, runs the operations that FOLLOW a decision, and
    /// exports - and creates and deletes nothing.
    /// </summary>
    public const string Approver = "APPROVER";

    /// <summary>The three roles IAM seeds into every Organisation.</summary>
    public static readonly IReadOnlyList<string> TenantRoles = [TenantAdmin, Initiator, Approver];

    /// <summary>
    /// "The Rule": which of the three roles holds a permission, decided from its action type.
    ///
    /// This is the table the Campaign module is measured against, expressed once so a reviewer
    /// can read it instead of reconstructing it from forty <c>[HasPermission]</c> attributes:
    ///
    ///   View                    TENANT_ADMIN  INITIATOR  APPROVER
    ///   Create                  TENANT_ADMIN  INITIATOR
    ///   Edit                    TENANT_ADMIN  INITIATOR  APPROVER
    ///   Submit                  TENANT_ADMIN  INITIATOR
    ///   Approve                 TENANT_ADMIN             APPROVER
    ///   Operate (destructive)   TENANT_ADMIN  INITIATOR
    ///   Operate (post-decision) TENANT_ADMIN  INITIATOR  APPROVER
    ///   Export                  TENANT_ADMIN  INITIATOR  APPROVER
    ///
    /// TENANT_ADMIN IS IN EVERY ROW, which is why it never appears in a handler's rules: it
    /// holds everything by definition, so a check for it would be a check that always passes.
    /// The two rules that DO constrain it are the ones that constrain everybody - segregation
    /// of duties, and the status a record is currently in.
    /// </summary>
    public static IReadOnlyList<string> HoldersOf(PermissionAction action, bool isPostDecision = false) =>
        action switch
        {
            PermissionAction.Create or PermissionAction.Submit => [TenantAdmin, Initiator],
            PermissionAction.Approve => [TenantAdmin, Approver],
            PermissionAction.Operate => isPostDecision
                ? [TenantAdmin, Initiator, Approver]
                : [TenantAdmin, Initiator],
            _ => [TenantAdmin, Initiator, Approver]
        };

    /// <summary>
    /// Whether the caller administers the Organisation, by role claim.
    ///
    /// USED FOR ONE THING ONLY - deciding which buttons the readiness screen draws, where an
    /// administrator approves rather than requests. It is NEVER used to grant access: that is
    /// <c>ICurrentUser.HasPermission</c>, which reads the permission claims the token carries.
    /// A role name that let something through would be a second, weaker authorisation path
    /// beside the one the endpoints already enforce.
    /// </summary>
    public static bool IsTenantAdministrator(IEnumerable<string> roles) =>
        roles is not null && roles.Contains(TenantAdmin, StringComparer.OrdinalIgnoreCase);
}

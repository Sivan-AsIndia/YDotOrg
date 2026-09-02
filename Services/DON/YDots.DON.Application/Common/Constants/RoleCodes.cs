namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// The role codes the Donors module recognises, mirrored from IAM where the roles themselves
/// are created.
///
/// THREE TENANT ROLES, AND ONLY THREE. FUNDRAISER, FUNDRAISING_MANAGER, RELATIONSHIP_USER,
/// DATA_STEWARD, DONOR_CARE, AUTHORISED_STAFF and SYSTEM_INTEGRATION used to be declared here
/// and are gone. They modelled job titles rather than authority, they had no counterpart left in
/// <c>YDot.IAM.Application/Common/Constants/RoleCodes.cs</c> after IAM reduced its catalogue to
/// four, and nothing in DON ever read them - so a token could never carry any of the seven, and
/// any rule written against them would have been dead the moment it was written.
///
/// Everything they used to mean is now expressed by what a caller can DO:
///
///   FUNDRAISER                            -> INITIATOR
///   RELATIONSHIP_USER                     -> INITIATOR
///   DONOR_CARE                            -> INITIATOR
///   AUTHORISED_STAFF                      -> INITIATOR
///   SYSTEM_INTEGRATION                    -> INITIATOR
///   DATA_STEWARD, when it merged          -> INITIATOR   (a merge is destructive)
///   DATA_STEWARD, when it rejected a match-> APPROVER    (or INITIATOR; see below)
///   FUNDRAISING_MANAGER, when it approved -> APPROVER    (or TENANT_ADMIN)
///   FUNDRAISING_MANAGER, when it prepared -> INITIATOR
///
/// DATA_STEWARD SPLITS ACROSS THE LINE and that is the whole point of the exercise. It used to
/// hold merge and reject-candidate together because both were "duplicate work". Merging takes two
/// donors' donations, receipts and consent history and joins them irreversibly, so it is a maker's
/// destructive act; rejecting a candidate ENDS a review, so it is a decision a checker may take.
/// One job title, two different authorities - which is exactly the confusion the seven-role
/// catalogue hid.
///
/// OWNERSHIP IS NOT A ROLE, and separating the two is what lets "My leads" work at all. Owning a
/// lead is a fact about that lead - the owner recorded against it by the Assignment Board - not a
/// fact about the person's authority in the Organisation. It scopes what they SEE
/// (<c>AccessScope.IsOwnRecordsOnly</c>, which is what turns the Lead Work Queue into My Leads);
/// their role decides what they may DO. This is why a lead keeps its owner when it converts to a
/// donor: the owner travels with the record, and no role has to be reassigned.
///
/// DON DOES NOT CREATE OR ASSIGN ROLES. It reads role claims off the token, and the authoritative
/// definitions live in IAM's <c>RoleCodes</c> and <c>RoleAccessProfiles</c>.
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
    /// The maker: views, creates, edits, submits, runs the operational verbs - capture, assign,
    /// contact, qualify, schedule, merge, close - and exports. It approves nothing. Anything
    /// raised here stops at the approval gate and waits for somebody else.
    /// </summary>
    public const string Initiator = "INITIATOR";

    /// <summary>
    /// The checker: views, edits, approves, runs the operations that FOLLOW a decision, and
    /// exports. It creates and deletes nothing - no lead capture, no draft deletion, no merge.
    /// </summary>
    public const string Approver = "APPROVER";

    /// <summary>The three roles IAM seeds into every Organisation.</summary>
    public static readonly IReadOnlyList<string> TenantRoles = [TenantAdmin, Initiator, Approver];

    /// <summary>
    /// "The Rule": which of the three roles holds a permission, decided from its action type.
    ///
    /// This is the table the Donors module is measured against, expressed once so a reviewer can
    /// read it instead of reconstructing it from forty-nine <c>[HasPermission]</c> attributes:
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
    /// THE TWO OPERATE ROWS ARE SEPARATED BY AN ALLOW-LIST, NOT BY THE VERB. Operate is the
    /// catch-all bucket, so <paramref name="isPostDecision"/> is answered by
    /// <see cref="PermissionCodes.PostDecisionOperations"/> naming the few codes to keep - never
    /// by a pattern over the code. A rule reading verbs would have to decide what "cancel" means,
    /// and DON spells it three different ways: cancelling a donor, cancelling a verification and
    /// cancelling a follow-up task are three different acts.
    ///
    /// TENANT_ADMIN IS IN EVERY ROW, which is why it never appears in a handler's rules: it holds
    /// everything by definition, so a check for it would be a check that always passes. The two
    /// rules that DO constrain it are the ones that constrain everybody - segregation of duties,
    /// and the status a record is currently in.
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
    /// FOR PRESENTATION ONLY - deciding which buttons a screen payload draws. It is NEVER used to
    /// grant access: that is <c>ICurrentUser.HasPermission</c>, which reads the permission claims
    /// the token carries. A role name that let something through would be a second, weaker
    /// authorisation path beside the one the endpoints already enforce.
    /// </summary>
    public static bool IsTenantAdministrator(IEnumerable<string> roles) =>
        roles is not null && roles.Contains(TenantAdmin, StringComparer.OrdinalIgnoreCase);
}

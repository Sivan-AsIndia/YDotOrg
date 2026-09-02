namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The role codes the seeder creates. Tenant roles are created once per Organisation, so the
/// same code exists in every Organisation as a genuinely separate row.
///
/// THE CATALOGUE IS FOUR ROLES, and that is a deliberate reduction from the fourteen that
/// preceded it. The earlier set modelled job titles - Campaign Manager, Finance Officer, Data
/// Steward - which meant every new module had to decide which of thirteen roles should hold each
/// of its permissions, and got it wrong often enough that several codes belonged to nobody.
///
/// This set models AUTHORITY instead, which is the thing that actually differs:
///
///   SUPER_ADMIN    the platform, across every Organisation
///   TENANT_ADMIN   everything, inside one Organisation
///   INITIATOR      does the work: creates, edits, submits, deletes - and approves nothing
///   APPROVER       decides the work: views, edits, approves - and creates nothing
///
/// The separation of INITIATOR from APPROVER is the one that carries weight. Maker-checker is
/// only a real control if the two capabilities start in different roles; leaving one role able
/// to both raise and decide the same record makes every four-eyes rule in the platform advisory.
/// </summary>
public static class RoleCodes
{
    /// <summary>
    /// The platform root role. Exists once, with TenantId null, and only the SuperAdmin
    /// holds it. It is never copied into an Organisation.
    /// </summary>
    public const string SuperAdmin = "SUPER_ADMIN";

    /// <summary>
    /// The administrator of one Organisation. Seeded into every Tenant with
    /// GrantsAllTenantPermissions set, so a new module does not require every customer to
    /// re-map their administrator.
    /// </summary>
    public const string TenantAdmin = "TENANT_ADMIN";

    /// <summary>
    /// The maker. Holds every Tenant-assignable permission across IAM, CAM, DON and PAY whose
    /// action is not <c>Approve</c>: view, create, edit, submit, the operational verbs and
    /// export.
    ///
    /// IT APPROVES NOTHING, and that is the whole definition. Anything it raises stops at the
    /// approval gate and waits for somebody else.
    /// </summary>
    public const string Initiator = "INITIATOR";

    /// <summary>
    /// The checker. Views, edits and approves across IAM, CAM, DON and PAY, plus the operations
    /// that FOLLOW a decision - activate, close, reconcile, resolve - and export.
    ///
    /// IT CREATES AND DELETES NOTHING. A checker who could create the record they then approve
    /// would defeat the separation this role exists to enforce.
    /// </summary>
    public const string Approver = "APPROVER";

    /// <summary>Every role the seeder creates inside a new Organisation.</summary>
    public static readonly IReadOnlyList<string> TenantRoles =
    [
        TenantAdmin, Initiator, Approver
    ];
}

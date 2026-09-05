namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The role codes the seeder creates. Tenant roles are created once per Organisation, so the
/// same code exists in every Organisation as a genuinely separate row.
///
/// THE CATALOGUE MODELS AUTHORITY, not job titles. The set this replaced modelled the latter -
/// Campaign Manager, Finance Officer, Data Steward - which meant every new module had to decide
/// which of thirteen roles should hold each of its permissions, and got it wrong often enough
/// that several codes belonged to nobody.
///
///   SUPER_ADMIN    the platform, across every Organisation
///   TENANT_ADMIN   everything, inside one Organisation
///   INITIATOR      does the work: creates, edits, submits, deletes - and approves nothing
///   APPROVER       decides the work: views, edits, approves - and creates nothing
///   DONOR          not staff at all: sees and pays their OWN giving, and nothing else
///
/// The separation of INITIATOR from APPROVER is the one that carries weight among the staff
/// roles. Maker-checker is only a real control if the two capabilities start in different roles;
/// leaving one role able to both raise and decide the same record makes every four-eyes rule in
/// the platform advisory.
///
/// DONOR IS NOT ON THAT LADDER AT ALL, which is why it is listed last rather than lowest. The
/// three staff roles differ by how much AUTHORITY they carry over the Organisation's records;
/// DONOR differs by WHOSE records it can reach, and the answer is only its own. A donor holding
/// fewer staff permissions would still be staff; a donor scoped to their own giving is a member
/// of the public with a login.
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

    /// <summary>
    /// The person who gives. Not staff, and the distinction is the point of the role.
    ///
    /// WHY IT HAD TO EXIST. A donation by a lead or a stranger converts them to a Donor and
    /// creates them an account, and that account was created with no roles - so it fell through
    /// to the Organisation's DEFAULT role, which is INITIATOR. Every donor who activated the
    /// invitation in their e-mail therefore received maker rights across IAM, Campaigns, Donors
    /// and Payments: the campaign register, the donor list, the user directory. Nobody chose
    /// that; it is what "fall back to the default" means when the account being created is not a
    /// member of staff.
    ///
    /// WHAT IT HOLDS: view and pay their own donations, view and re-send their own receipts, and
    /// nothing else. It is the only role in the catalogue defined by an explicit list rather than
    /// computed from the permission actions, because it is the only one whose boundary is not a
    /// verb - see <see cref="RoleAccessProfiles.Donor"/>.
    ///
    /// THE PERMISSIONS ARE HALF THE ANSWER. They say WHICH screens; the donor data scope in PAY
    /// says WHOSE rows, and without it this role would show one donor every other donor's giving.
    /// </summary>
    public const string Donor = "DONOR";

    /// <summary>Every role the seeder creates inside a new Organisation.</summary>
    public static readonly IReadOnlyList<string> TenantRoles =
    [
        TenantAdmin, Initiator, Approver, Donor
    ];
}

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The role codes the seeder creates. Tenant roles are created once per Organisation, so the
/// same code exists in every Organisation as a genuinely separate row.
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

    public const string UserAdministrator = "USER_ADMINISTRATOR";
    public const string AccessApprover = "ACCESS_APPROVER";
    public const string Auditor = "AUDITOR";
    public const string CampaignManager = "CAMPAIGN_MANAGER";

    /// <summary>
    /// Section 5.3 of the campaign module brief. A Campaign Owner runs their OWN campaigns:
    /// creates them, edits their drafts, submits them and operates them - but approves nothing,
    /// not campaigns, not budget plans, not tracking assets, not readiness launches.
    ///
    /// It is a separate role from CAMPAIGN_MANAGER rather than a narrower scope on the same one,
    /// because the difference is about AUTHORITY and not about reach: a manager with no
    /// campaigns of their own still approves, and an owner with fifty campaigns still cannot.
    /// </summary>
    public const string CampaignOwner = "CAMPAIGN_OWNER";
    public const string FundraisingOfficer = "FUNDRAISING_OFFICER";
    public const string FinanceOfficer = "FINANCE_OFFICER";

    /// <summary>
    /// The payments module's day-to-day operator: works the payment support queue, the gateway
    /// event queue and receipt issuing.
    ///
    /// IT CAN RAISE A REFUND AND CANNOT APPROVE ONE, which is the whole reason it exists as a
    /// separate role from FINANCE_OFFICER. Money leaving the organisation needs two people, and
    /// the platform guarantees that the person who raised a case cannot decide it - but that
    /// guarantee only produces a real second pair of eyes if the two capabilities START in
    /// different roles. Putting both in one role would leave every organisation to notice the
    /// problem for themselves.
    ///
    /// It also carries NO gateway configuration permission. Deciding which merchant account the
    /// money settles into is a different kind of decision from processing the payments that reach
    /// it.
    /// </summary>
    public const string PaymentOperations = "PAYMENT_OPERATIONS";

    /// <summary>
    /// The donor data steward: duplicate review, merges and archive.
    ///
    /// A SEPARATE ROLE BECAUSE MERGING IS IRREVERSIBLE IN PRACTICE. Two donor records joined into
    /// one take their donations, receipts and consent history with them, and no fundraiser should
    /// be able to do that in passing while working their own queue. The steward is also the only
    /// non-administrator who may read confidential matching evidence, for the plain reason that
    /// deciding whether two records are the same person is impossible without seeing what matched.
    ///
    /// DON has expected this role to exist since it was written - it is in its own RoleCodes list -
    /// and IAM never seeded it, so every steward permission in the catalogue belonged to nobody
    /// and duplicate review was reachable only by the organisation administrator.
    /// </summary>
    public const string DataSteward = "DATA_STEWARD";

    /// <summary>
    /// Supporter care: identity verification, consent decisions and the follow-up queue.
    ///
    /// IT HOLDS THE CONSENT AND VERIFICATION CODES and no ability to create or approve a donor.
    /// The person who confirms who somebody is, and what they agreed to be contacted about, is
    /// doing compliance work rather than fundraising - and the two want different hands.
    /// </summary>
    public const string DonorCare = "DONOR_CARE";

    public const string Volunteer = "VOLUNTEER";

    /// <summary>The role a new Organisation user gets when none is chosen.</summary>
    public const string StandardUser = "STANDARD_USER";

    /// <summary>Future donor-portal accounts created off the back of a payment.</summary>
    public const string DonorPortalUser = "DONOR_PORTAL_USER";

    /// <summary>Every role the seeder creates inside a new Organisation.</summary>
    public static readonly IReadOnlyList<string> TenantRoles =
    [
        TenantAdmin, UserAdministrator, AccessApprover, Auditor, CampaignManager, CampaignOwner,
        FundraisingOfficer, FinanceOfficer, PaymentOperations, DataSteward, DonorCare, Volunteer,
        StandardUser, DonorPortalUser
    ];
}

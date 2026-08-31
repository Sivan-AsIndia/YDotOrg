namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// Screen identifiers and their UI routes, from UI section 2.1 and section 2 of the developer
/// contract. Every screen payload echoes its own id and route so the Angular shell can confirm
/// it rendered the view the server thinks it asked for.
/// </summary>
public static class ScreenIds
{
    public const string LeadWorkQueue = "SCR-DON-001";
    public const string LeadCapture = "SCR-DON-002";
    public const string Donor360 = "SCR-DON-003";
    public const string DuplicateReview = "SCR-DON-004";
    public const string ConsentAndPreferenceCentre = "SCR-DON-005";
    public const string AssignmentBoard = "SCR-DON-006";
    public const string DonorIdentityVerification = "DON-UI-07";
    public const string FollowUpPlanner = "DON-UI-08";
}

/// <summary>The Angular routes the screens live on. Used only for the echo described above.</summary>
public static class ScreenRoutes
{
    public const string LeadWorkQueue = "/fundraising/relationships/lead-work-queue";
    public const string LeadCapture = "/fundraising/relationships/lead-capture";
    public const string Donor360 = "/fundraising/relationships/donor-360";
    public const string DuplicateReview = "/fundraising/relationships/duplicate-review";
    public const string ConsentAndPreferenceCentre = "/fundraising/relationships/consent-and-preference-centre";
    public const string AssignmentBoard = "/fundraising/relationships/assignment-board";
    public const string DonorIdentityVerification = "/don/donor-identity-verification";
    public const string FollowUpPlanner = "/don/follow-up-planner";
}

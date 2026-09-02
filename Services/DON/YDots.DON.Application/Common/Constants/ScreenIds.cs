namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// Screen identifiers and their UI routes, from UI section 2.1 and section 2 of the developer
/// contract. Every screen payload echoes its own id and route so the Angular shell can confirm
/// it rendered the view the server thinks it asked for.
///
/// AN ID IS CLAIMED ONCE AND NEVER REUSED. That echo is the whole point of these constants, and
/// it is worth nothing if two screens answer to the same id: the shell would confirm a match on
/// a payload built for a different view. <see cref="MyLeads"/> in particular is NOT
/// <c>SCR-DON-005</c> - that belongs to <see cref="ConsentAndPreferenceCentre"/> and has since
/// the original six were numbered.
///
/// SCR-DON-nnn IS THE ORIGINAL SIX; everything added afterwards continues the DON-UI-nn series
/// rather than extending the first, so the numbering says when a screen arrived.
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

    // ---- The rest of the operational flow ------------------------------------------------
    //
    // These five screens are routed and guarded in the Angular shell and are the working half of
    // the module brief - capture, assign, work, follow up, convert - but the server model did not
    // know they existed, so GET /api/v1/donors/menu could not offer them and no payload could
    // echo an id for them.
    //
    // NONE OF THEM INTRODUCES A PERMISSION. Each reuses the view code of the screen it is a
    // different lens on, which is what <see cref="MenuCatalogue"/> declares and what the shell
    // already guards them with. My Leads is the Lead Work Queue narrowed to one owner - the same
    // records, scoped by AccessScope rather than by a second grant - so inventing don.my-leads.*
    // would have created codes IAM cannot seed and a token can never carry.

    /// <summary>The Lead Work Queue narrowed to the signed-in owner.</summary>
    public const string MyLeads = "DON-UI-09";

    /// <summary>Follow-ups scheduled against the owner's leads.</summary>
    public const string FollowUpQueue = "DON-UI-10";

    /// <summary>The execution form reached from Execute Follow-Up.</summary>
    public const string FollowUpExecution = "DON-UI-11";

    /// <summary>Activity history for one lead or donor, and where a completed follow-up lands.</summary>
    public const string CommunicationTimeline = "DON-UI-12";

    /// <summary>Donor records, including leads converted by an e-mail match on a donation.</summary>
    public const string DonorList = "DON-UI-13";
}

/// <summary>
/// The Angular routes the screens live on. Used only for the echo described above.
///
/// EVERY SCREEN IN THE GROUP SITS UNDER /fundraising/relationships. Donor identity verification
/// and the follow-up planner were declared here as <c>/don/...</c> while the shell routes them
/// under the group like everything else, so the echoed route named a path the menu never links
/// to. The <c>/don/...</c> paths remain registered in the shell as aliases for old bookmarks;
/// they are simply not the route this module names.
/// </summary>
public static class ScreenRoutes
{
    private const string Group = "/fundraising/relationships";

    public const string LeadWorkQueue = Group + "/lead-work-queue";
    public const string LeadCapture = Group + "/lead-capture";
    public const string Donor360 = Group + "/donor-360";
    public const string DuplicateReview = Group + "/duplicate-review";
    public const string ConsentAndPreferenceCentre = Group + "/consent-and-preference-centre";
    public const string AssignmentBoard = Group + "/assignment-board";
    public const string DonorIdentityVerification = Group + "/donor-identity-verification";
    public const string FollowUpPlanner = Group + "/follow-up-planner";
    public const string MyLeads = Group + "/my-leads";
    public const string FollowUpQueue = Group + "/follow-up-queue";
    public const string FollowUpExecution = Group + "/follow-up-execution";
    public const string CommunicationTimeline = Group + "/communication-timeline";
    public const string DonorList = Group + "/donor-list";
}

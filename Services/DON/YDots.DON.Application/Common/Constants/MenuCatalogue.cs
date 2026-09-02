namespace YDots.DON.Application.Common.Constants;

/// <summary>One menu entry: which screen it opens and which permission makes it visible.</summary>
public sealed record MenuEntry(string ScreenId, string Label, string Route, string ViewPermission);

/// <summary>
/// The approved Donors menu, "Relationships -> Leads and Donors" (UI section 2).
///
/// This is what makes a role-based menu possible without a second permission model. The UI calls
/// GET /api/v1/donors/menu once after sign-in, gets back only the entries the caller's token
/// permits, and renders exactly those. The rule from UI section 2 still holds on the server: a
/// hidden menu is a convenience, never the authorisation. Every route is rechecked by
/// [HasPermission] when it is actually called.
///
/// THE FOUR PRIMARY MENUS COME FIRST, in the order the module brief works through them - Lead
/// Work Queue, My Leads, Follow-Up Queue, Donor List - because that is the lifecycle: capture a
/// lead, work the ones you own, do the scheduled activity, and end up with a donor. The screens
/// underneath are reached FROM those four rather than browsed to, and are listed after.
///
/// SEVERAL ENTRIES SHARE A VIEW PERMISSION, and that is correct rather than an oversight. My
/// Leads is the Lead Work Queue narrowed to one owner, so it is gated on
/// <c>don.lead-work-queue.view</c>; the Follow-Up Queue and the execution form are two steps of
/// the planner, so both are gated on <c>don.follow-up-planner.view</c>. Narrowing WHAT a caller
/// sees is <c>AccessScope</c>'s job - a fact about the records - and giving each lens its own
/// code would mean inventing permissions IAM cannot seed, which a token could then never carry.
/// </summary>
public static class MenuCatalogue
{
    public const string MenuGroup = "Relationships -> Leads and Donors";

    public static readonly IReadOnlyList<MenuEntry> Entries =
    [
        // ---- The four primary menus ----------------------------------------------------------
        new(ScreenIds.LeadWorkQueue, "Lead work queue", ScreenRoutes.LeadWorkQueue, PermissionCodes.LeadWorkQueueView),
        new(ScreenIds.MyLeads, "My leads", ScreenRoutes.MyLeads, PermissionCodes.LeadWorkQueueView),
        new(ScreenIds.FollowUpQueue, "Follow-up queue", ScreenRoutes.FollowUpQueue, PermissionCodes.FollowUpPlannerView),
        new(ScreenIds.DonorList, "Donor list", ScreenRoutes.DonorList, PermissionCodes.DonorsView),

        // ---- Reached from the four above -----------------------------------------------------
        new(ScreenIds.LeadCapture, "Lead capture", ScreenRoutes.LeadCapture, PermissionCodes.LeadCaptureView),
        new(ScreenIds.AssignmentBoard, "Assignment board", ScreenRoutes.AssignmentBoard, PermissionCodes.AssignmentBoardView),
        new(ScreenIds.FollowUpPlanner, "Follow-up planner", ScreenRoutes.FollowUpPlanner, PermissionCodes.FollowUpPlannerView),
        new(ScreenIds.FollowUpExecution, "Follow-up execution", ScreenRoutes.FollowUpExecution, PermissionCodes.FollowUpPlannerView),
        new(ScreenIds.CommunicationTimeline, "Communication timeline", ScreenRoutes.CommunicationTimeline, PermissionCodes.Donor360View),
        new(ScreenIds.Donor360, "Donor 360", ScreenRoutes.Donor360, PermissionCodes.Donor360View),
        new(ScreenIds.DuplicateReview, "Duplicate review", ScreenRoutes.DuplicateReview, PermissionCodes.DuplicateReviewView),
        new(ScreenIds.ConsentAndPreferenceCentre, "Consent and preference centre", ScreenRoutes.ConsentAndPreferenceCentre, PermissionCodes.ConsentCentreView),
        new(ScreenIds.DonorIdentityVerification, "Donor identity verification", ScreenRoutes.DonorIdentityVerification, PermissionCodes.VerificationView)
    ];

    /// <summary>Fields that stay masked until the caller holds the listed sensitive permission.</summary>
    public static readonly IReadOnlyDictionary<string, string> SensitiveFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PermissionCodes.DonorsViewSensitiveContact] = "Donor e-mail and phone",
            [PermissionCodes.DonorsViewConfidentialEvidence] = "Consent evidence and matching evidence",
            [PermissionCodes.DonorsExport] = "Controlled donor export"
        };

    /// <summary>The subset of entries a caller with these permission codes may see.</summary>
    public static IReadOnlyList<MenuEntry> VisibleFor(IReadOnlyCollection<string> permissionCodes) =>
        [.. Entries.Where(entry => permissionCodes.Contains(entry.ViewPermission, StringComparer.Ordinal))];
}

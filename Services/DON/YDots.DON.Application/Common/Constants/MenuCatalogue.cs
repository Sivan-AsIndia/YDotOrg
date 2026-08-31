namespace YDots.DON.Application.Common.Constants;

/// <summary>One menu entry: which screen it opens and which permission makes it visible.</summary>
public sealed record MenuEntry(string ScreenId, string Label, string Route, string ViewPermission);

/// <summary>
/// The approved Donors menu, "Relationships → Leads and Donors" (UI section 2).
///
/// This is what makes a role-based menu possible without a second permission model. The UI
/// calls GET /api/v1/donors/menu once after sign-in, gets back only the entries the caller's
/// token permits, and renders exactly those. The rule from UI section 2 still holds on the
/// server: a hidden menu is a convenience, never the authorisation. Every route is rechecked
/// by [HasPermission] when it is actually called.
/// </summary>
public static class MenuCatalogue
{
    public const string MenuGroup = "Relationships -> Leads and Donors";

    public static readonly IReadOnlyList<MenuEntry> Entries =
    [
        new(ScreenIds.LeadWorkQueue, "Lead work queue", ScreenRoutes.LeadWorkQueue, PermissionCodes.LeadWorkQueueView),
        new(ScreenIds.LeadCapture, "Lead capture", ScreenRoutes.LeadCapture, PermissionCodes.LeadCaptureView),
        new(ScreenIds.Donor360, "Donor 360", ScreenRoutes.Donor360, PermissionCodes.Donor360View),
        new(ScreenIds.DuplicateReview, "Duplicate review", ScreenRoutes.DuplicateReview, PermissionCodes.DuplicateReviewView),
        new(ScreenIds.ConsentAndPreferenceCentre, "Consent and preference centre", ScreenRoutes.ConsentAndPreferenceCentre, PermissionCodes.ConsentCentreView),
        new(ScreenIds.AssignmentBoard, "Assignment board", ScreenRoutes.AssignmentBoard, PermissionCodes.AssignmentBoardView),
        new(ScreenIds.DonorIdentityVerification, "Donor identity verification", ScreenRoutes.DonorIdentityVerification, PermissionCodes.VerificationView),
        new(ScreenIds.FollowUpPlanner, "Follow-up planner", ScreenRoutes.FollowUpPlanner, PermissionCodes.FollowUpPlannerView)
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

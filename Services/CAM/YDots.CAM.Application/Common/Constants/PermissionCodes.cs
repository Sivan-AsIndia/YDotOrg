namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// Every permission code the Campaign module enforces, and the action type each one falls under.
///
/// THESE STRINGS ARE A CROSS-SERVICE CONTRACT. CAM cannot issue a claim - it never signs a
/// token - so each of these codes must ALSO exist in IAM
/// (<c>ModulePermissionCatalogue.Campaigns</c>), where it is seeded into the permission table
/// and attached to roles. If the two drift, the symptom is a 403 on an endpoint that looks
/// correctly configured, because the token never carried the claim the attribute asks for.
///
/// THE ACTION TYPE IS DECLARED, NOT DERIVED FROM THE VERB, and that is what makes the
/// three-role model checkable. <c>cam.campaigns.close</c> reads as an operation and is an
/// APPROVAL - it decides somebody else's close request - so a rule reading the word "close"
/// would hand it to INITIATOR and quietly break maker-checker. <see cref="Catalogue"/> states
/// the action for every code, <see cref="RoleCodes.HoldersOf"/> turns an action into the roles
/// that hold it, and <see cref="RolesFor"/> puts the two together so the matrix in the module
/// brief can be read off the model rather than reconstructed from the controllers.
///
/// Once published, a code may be retired but never renamed: a renamed code silently unreachable
/// is far worse than a retired one that is visibly gone.
/// </summary>
public static class PermissionCodes
{
    /// <summary>Section-level view permission. Every campaign screen requires it as a baseline.</summary>
    public const string Section = "CAM.View";

    // ---- Campaigns ---------------------------------------------------------------------
    public const string CampaignsView = "cam.campaigns.view";
    public const string CampaignsCreate = "cam.campaigns.create";
    public const string CampaignsEdit = "cam.campaigns.edit";
    public const string CampaignsSubmit = "cam.campaigns.submit";

    /// <summary>
    /// Approving a campaign - the readiness screen's "Approve launch", and the campaigns
    /// controller's approve route, which are the same decision reached from two screens.
    /// </summary>
    public const string CampaignsApprove = "cam.campaigns.approve";

    public const string CampaignsActivate = "cam.campaigns.activate";
    public const string CampaignsPause = "cam.campaigns.pause";
    public const string CampaignsResume = "cam.campaigns.resume";
    public const string CampaignsRequestClose = "cam.campaigns.request-close";
    public const string CampaignsApproveClose = "cam.campaigns.close";
    public const string CampaignsDeleteDraft = "cam.campaigns.delete-draft";
    public const string CampaignsExport = "cam.campaigns.export";
    public const string CampaignsViewHistory = "cam.campaigns.view-history";

    // ---- Tracking assets ------------------------------------------------------------------
    public const string TrackingAssetsView = "cam.tracking-assets.view";
    public const string TrackingAssetsCreate = "cam.tracking-assets.create";
    public const string TrackingAssetsEdit = "cam.tracking-assets.edit";
    public const string TrackingAssetsSubmit = "cam.tracking-assets.submit";
    public const string TrackingAssetsApprove = "cam.tracking-assets.approve";
    public const string TrackingAssetsActivate = "cam.tracking-assets.activate";

    /// <summary>
    /// Asking for a live asset to be taken down. The MAKER's half of the disable pair.
    ///
    /// Separate from <see cref="TrackingAssetsDeactivate"/> because disabling an asset stops a
    /// printed QR code resolving, and the person who created the asset must not be the person who
    /// decides to end it. This moves Active -> DisableRequested; the decision is the other code.
    /// </summary>
    public const string TrackingAssetsRequestDisable = "cam.tracking-assets.request-disable";

    /// <summary>Deciding a disable request, and the direct take-down. The CHECKER's half.</summary>
    public const string TrackingAssetsDeactivate = "cam.tracking-assets.deactivate";

    /// <summary>
    /// Permanently removing a DRAFT asset that has never been used.
    ///
    /// IT USED TO BORROW <see cref="TrackingAssetsDeactivate"/>, which was wrong in both
    /// directions: it let anybody who could take a live asset down also destroy drafts, and it
    /// meant the maker who owns a draft could not discard their own until they were also trusted
    /// with disabling live ones. Destroying a draft nothing points at is a maker's tidy-up;
    /// disabling a live asset is a checker's decision. Two acts, two codes.
    /// </summary>
    public const string TrackingAssetsDeleteDraft = "cam.tracking-assets.delete-draft";
    public const string TrackingAssetsExport = "cam.tracking-assets.export";

    // ---- Readiness checklist ------------------------------------------------------------------
    public const string ReadinessView = "cam.readiness.view";
    public const string ReadinessCreate = "cam.readiness.create";
    public const string ReadinessEdit = "cam.readiness.edit";
    public const string ReadinessPass = "cam.readiness.pass";
    public const string ReadinessFail = "cam.readiness.fail";

    /// <summary>
    /// RETIRED. Enforced by nothing, and it must stay that way.
    ///
    /// It once gated a second campaign-approval path that lived inside the readiness feature and
    /// carried no segregation-of-duties check, so somebody refused on the campaigns endpoint
    /// could approve the same campaign here. That path is gone. The readiness screen's "Approve
    /// launch" button is campaign approval reached from a different screen, so it is gated on
    /// <see cref="CampaignsApprove"/> - one decision, one code, one implementation.
    ///
    /// The constant survives because the code is published and a retired code must remain
    /// recognisable; it is absent from <see cref="All"/> and from <see cref="Catalogue"/>, so
    /// nothing in CAM can accidentally start honouring it again.
    /// </summary>
    public const string ReadinessApprove = "cam.readiness.approve";

    /// <summary>
    /// RAISING a blocker against a check - "this cannot pass yet, and here is why".
    ///
    /// NARROWED: it used to gate resolving one as well. Raising an obstacle and declaring it
    /// cleared are opposite acts, and holding both means being able to raise a blocker and then
    /// wave it away unilaterally - which empties the mechanism of its only purpose, since an open
    /// blocker is precisely what stops a check being passed. Clearing one is
    /// <see cref="ReadinessResolveBlockers"/>.
    /// </summary>
    public const string ReadinessManageBlockers = "cam.readiness.manage-blockers";

    /// <summary>Declaring a raised blocker cleared. The checker's half of the blocker pair.</summary>
    public const string ReadinessResolveBlockers = "cam.readiness.resolve-blockers";

    /// <summary>
    /// Removing a readiness check from a campaign's checklist.
    ///
    /// A MAKER'S ACT, and available on a Pending check only - see the handler. Deleting a check
    /// somebody has already passed or failed would destroy the verdict along with the question,
    /// and the verdict is the record of somebody having looked.
    /// </summary>
    public const string ReadinessDelete = "cam.readiness.delete";

    public const string ReadinessReturnToDraft = "cam.readiness.return-to-draft";

    // ---- Budget and target plans ---------------------------------------------------------------
    public const string BudgetPlansView = "cam.budget-plans.view";
    public const string BudgetPlansAllocate = "cam.budget-plans.allocate";
    public const string BudgetPlansRevise = "cam.budget-plans.revise";
    public const string BudgetPlansSubmit = "cam.budget-plans.submit";

    /// <summary>
    /// Approving a plan version - the point at which figures become the campaign's committed budget.
    ///
    /// SEPARATE FROM SUBMIT, and it has to be: the handler refuses to let one person do both to the
    /// same version, and that refusal is only meaningful if the two are separately grantable.
    /// </summary>
    public const string BudgetPlansApprove = "cam.budget-plans.approve";

    public const string BudgetPlansReject = "cam.budget-plans.reject";
    public const string BudgetPlansExport = "cam.budget-plans.export";

    // ---- Attribution ---------------------------------------------------------------------------
    public const string AttributionView = "cam.attribution.view";
    public const string AttributionExport = "cam.attribution.export";

    /// <summary>
    /// Asking for a donation's attribution to be corrected.
    ///
    /// A REQUEST, NOT A CHANGE. Re-attributing a gift moves money between campaigns in every report
    /// that follows, so CAM records the request and the correction itself is made where the
    /// donation lives.
    /// </summary>
    public const string AttributionRequestCorrection = "cam.attribution.request-correction";

    // ---- Reference data -----------------------------------------------------------------------
    public const string ReferenceView = "cam.reference.view";

    /// <summary>
    /// Maintaining the global Channel, Source and Medium tables.
    ///
    /// PLATFORM-ONLY, because those codes appear in tracking URLs and in reporting that spans
    /// Organisations. One code has to mean one thing platform-wide, so a Tenant role must never
    /// be able to carry this - not even TENANT_ADMIN.
    /// </summary>
    public const string ReferenceManage = "cam.reference.manage";

    /// <summary>
    /// The Operate codes that describe what happens to a record AFTER a decision has been taken,
    /// and which an APPROVER therefore keeps.
    ///
    /// AN ALLOW-LIST, NOT A BLOCK-LIST, and deliberately so - the same choice IAM's
    /// <c>RoleAccessProfiles.PostApprovalOperations</c> makes, for the same reason. Operate is the
    /// catch-all bucket: it holds activate, and it also holds delete-draft. A rule naming what to
    /// EXCLUDE would hand a checker every new destructive verb the day somebody added one. This
    /// names the few to keep, so anything new stays out until a person decides otherwise.
    ///
    /// NOTHING HERE CREATES OR DESTROYS. That is the test each entry has to pass, and it is why
    /// <see cref="CampaignsDeleteDraft"/> is absent.
    /// </summary>
    public static readonly IReadOnlySet<string> PostDecisionOperations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            CampaignsActivate,
            CampaignsPause,
            CampaignsResume,
            TrackingAssetsActivate,
            TrackingAssetsDeactivate,
            ReadinessFail,

            // RESOLVING A BLOCKER IS HERE; RAISING ONE IS NOT. Deciding that an obstacle is
            // cleared is a checker's call - it is what unblocks the pass - while raising one is
            // the maker noticing that something is not ready. `ReadinessManageBlockers` was in
            // this set and has come out with the split.
            ReadinessResolveBlockers,
            ReadinessReturnToDraft
        };

    /// <summary>
    /// Every enforced code with the action it falls under.
    ///
    /// THIS IS THE LIST IAM MUST AGREE WITH. Each entry has a twin in
    /// <c>ModulePermissionCatalogue.Campaigns</c> carrying the same code and the same
    /// <c>PermissionAction</c>; IAM derives INITIATOR and APPROVER from its copy, so a
    /// disagreement here shows up as a role holding a code the module thinks it should not.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, PermissionAction Action)> Catalogue =
    [
        (Section, PermissionAction.View),

        (CampaignsView, PermissionAction.View),
        (CampaignsCreate, PermissionAction.Create),
        (CampaignsEdit, PermissionAction.Edit),
        (CampaignsSubmit, PermissionAction.Submit),
        (CampaignsApprove, PermissionAction.Approve),
        (CampaignsActivate, PermissionAction.Operate),
        (CampaignsPause, PermissionAction.Operate),
        (CampaignsResume, PermissionAction.Operate),
        (CampaignsRequestClose, PermissionAction.Submit),
        (CampaignsApproveClose, PermissionAction.Approve),
        (CampaignsDeleteDraft, PermissionAction.Operate),
        (CampaignsExport, PermissionAction.Export),
        (CampaignsViewHistory, PermissionAction.View),

        (TrackingAssetsView, PermissionAction.View),
        (TrackingAssetsCreate, PermissionAction.Create),
        (TrackingAssetsEdit, PermissionAction.Edit),
        (TrackingAssetsSubmit, PermissionAction.Submit),
        (TrackingAssetsApprove, PermissionAction.Approve),
        (TrackingAssetsActivate, PermissionAction.Operate),

        // SUBMIT, because a disable request is a request: it asks somebody else to decide.
        (TrackingAssetsRequestDisable, PermissionAction.Submit),
        (TrackingAssetsDeactivate, PermissionAction.Operate),
        (TrackingAssetsDeleteDraft, PermissionAction.Operate),
        (TrackingAssetsExport, PermissionAction.Export),

        (ReadinessView, PermissionAction.View),
        (ReadinessCreate, PermissionAction.Create),
        (ReadinessEdit, PermissionAction.Edit),

        // PASS IS AN APPROVAL and fail is not, which is the asymmetry the checklist turns on.
        // Signing a check off is somebody declaring the campaign ready on that point; recording
        // that it is not ready yet is the person doing the work saying so.
        (ReadinessPass, PermissionAction.Approve),
        (ReadinessFail, PermissionAction.Operate),
        (ReadinessManageBlockers, PermissionAction.Operate),
        (ReadinessResolveBlockers, PermissionAction.Operate),
        (ReadinessDelete, PermissionAction.Operate),
        (ReadinessReturnToDraft, PermissionAction.Operate),

        (BudgetPlansView, PermissionAction.View),
        (BudgetPlansAllocate, PermissionAction.Create),
        (BudgetPlansRevise, PermissionAction.Edit),
        (BudgetPlansSubmit, PermissionAction.Submit),
        (BudgetPlansApprove, PermissionAction.Approve),
        (BudgetPlansReject, PermissionAction.Approve),
        (BudgetPlansExport, PermissionAction.Export),

        (AttributionView, PermissionAction.View),
        (AttributionExport, PermissionAction.Export),
        (AttributionRequestCorrection, PermissionAction.Submit),

        (ReferenceView, PermissionAction.View),
        (ReferenceManage, PermissionAction.Operate)
    ];

    /// <summary>
    /// Codes whose use always writes an enhanced audit row: approvals, exports, and anything
    /// that changes what donors can see or how money is attributed.
    /// </summary>
    public static readonly IReadOnlyList<string> Sensitive =
    [
        CampaignsApprove, CampaignsApproveClose, CampaignsDeleteDraft, CampaignsExport,
        TrackingAssetsApprove, TrackingAssetsDeactivate, TrackingAssetsDeleteDraft,
        TrackingAssetsExport,
        ReadinessPass, ReadinessDelete, ReadinessReturnToDraft,
        BudgetPlansApprove, BudgetPlansExport,
        AttributionExport, AttributionRequestCorrection,
        ReferenceManage
    ];

    /// <summary>Every code CAM enforces. Mirrored in IAM ModulePermissionCatalogue.Campaigns.</summary>
    public static readonly IReadOnlyList<string> All = [.. Catalogue.Select(entry => entry.Code)];

    public static bool IsSensitive(string permissionCode) =>
        Sensitive.Contains(permissionCode, StringComparer.Ordinal);

    /// <summary>The action a code falls under, or null when the code is not one CAM enforces.</summary>
    public static PermissionAction? ActionFor(string permissionCode)
    {
        foreach (var entry in Catalogue)
        {
            if (string.Equals(entry.Code, permissionCode, StringComparison.Ordinal))
            {
                return entry.Action;
            }
        }

        return null;
    }

    /// <summary>
    /// Which of the three tenant roles hold a code, by the rule in the module brief.
    ///
    /// <c>cam.reference.manage</c> answers with NOBODY, and that is correct rather than a gap:
    /// it is platform-only, held by SUPER_ADMIN through the root flag rather than by a grant, so
    /// a tenant role carrying it would be a row that grants nothing.
    /// </summary>
    public static IReadOnlyList<string> RolesFor(string permissionCode)
    {
        if (string.Equals(permissionCode, ReferenceManage, StringComparison.Ordinal))
        {
            return [];
        }

        var action = ActionFor(permissionCode);

        return action is null
            ? []
            : RoleCodes.HoldersOf(action.Value, PostDecisionOperations.Contains(permissionCode));
    }
}

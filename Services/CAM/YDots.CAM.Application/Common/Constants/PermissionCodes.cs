namespace YDots.CAM.Application.Common.Constants;

/// <summary>
/// Every permission code the Campaign module enforces.
///
/// THESE STRINGS ARE A CROSS-SERVICE CONTRACT. CAM cannot issue a claim - it never signs a
/// token - so each of these codes must ALSO exist in IAM
/// (<c>ModulePermissionCatalogue.Campaigns</c>), where it is seeded into the permission table
/// and attached to roles. If the two drift, the symptom is a 403 on an endpoint that looks
/// correctly configured, because the token never carried the claim the attribute asks for.
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
    public const string TrackingAssetsDeactivate = "cam.tracking-assets.deactivate";
    public const string TrackingAssetsExport = "cam.tracking-assets.export";

    // ---- Readiness checklist ------------------------------------------------------------------
    public const string ReadinessView = "cam.readiness.view";
    public const string ReadinessCreate = "cam.readiness.create";
    public const string ReadinessEdit = "cam.readiness.edit";
    public const string ReadinessPass = "cam.readiness.pass";
    public const string ReadinessFail = "cam.readiness.fail";
    public const string ReadinessApprove = "cam.readiness.approve";
    public const string ReadinessManageBlockers = "cam.readiness.manage-blockers";
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
    /// be able to carry this.
    /// </summary>
    public const string ReferenceManage = "cam.reference.manage";

    /// <summary>
    /// Codes whose use always writes an enhanced audit row: approvals, exports, and anything
    /// that changes what donors can see or how money is attributed.
    /// </summary>
    public static readonly IReadOnlyList<string> Sensitive =
    [
        CampaignsApprove, CampaignsApproveClose, CampaignsDeleteDraft, CampaignsExport,
        TrackingAssetsApprove, TrackingAssetsDeactivate, TrackingAssetsExport,
        ReadinessApprove, ReadinessReturnToDraft,
        BudgetPlansApprove, BudgetPlansExport,
        AttributionExport, AttributionRequestCorrection,
        ReferenceManage
    ];

    /// <summary>Every code CAM enforces. Mirrored in IAM ModulePermissionCatalogue.Campaigns.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Section,
        CampaignsView, CampaignsCreate, CampaignsEdit, CampaignsSubmit, CampaignsApprove,
        CampaignsActivate, CampaignsPause, CampaignsResume, CampaignsRequestClose,
        CampaignsApproveClose, CampaignsDeleteDraft, CampaignsExport, CampaignsViewHistory,
        TrackingAssetsView, TrackingAssetsCreate, TrackingAssetsEdit, TrackingAssetsSubmit,
        TrackingAssetsApprove, TrackingAssetsActivate, TrackingAssetsDeactivate, TrackingAssetsExport,
        ReadinessView, ReadinessCreate, ReadinessEdit, ReadinessPass, ReadinessFail,
        ReadinessApprove, ReadinessManageBlockers, ReadinessReturnToDraft,
        BudgetPlansView, BudgetPlansAllocate, BudgetPlansRevise, BudgetPlansSubmit,
        BudgetPlansApprove, BudgetPlansReject, BudgetPlansExport,
        AttributionView, AttributionExport, AttributionRequestCorrection,
        ReferenceView, ReferenceManage
    ];

    public static bool IsSensitive(string permissionCode) =>
        Sensitive.Contains(permissionCode, StringComparer.Ordinal);
}

using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The permission codes owned by the OTHER services, seeded here because IAM is the only
/// service that can put a claim into a token.
///
/// WHY IAM SEEDS SOMEBODY ELSE PERMISSIONS. DON enforces <c>don.donors.create</c> by looking
/// for a claim of that exact string. It cannot issue that claim itself — it never signs a
/// token — and it cannot create the permission row, because the permission catalogue and the
/// roles that carry it live in the IAM database. So the codes have to exist here, or the
/// endpoint on the other side is simply unreachable for everybody.
///
/// KEEPING IT IN SYNC. The DON list below mirrors
/// <c>YDots.DON.Application/Common/Constants/PermissionCodes.cs</c>. When that file gains a
/// code, add the same literal here and attach it to whichever roles should carry it. If the
/// two drift, the symptom is a 403 on an endpoint that looks correctly configured, because
/// the token never carried the claim the attribute asks for.
/// </summary>
public static class ModulePermissionCatalogue
{
    /// <summary>One permission the seeder should create, with everything it needs to do so.</summary>
    public sealed record PermissionSeed(
        string Code,
        string Name,
        string ModuleCode,
        string? GroupCode,
        PermissionAction Action,
        bool IsSensitive = false,
        bool IsPlatformOnly = false,
        string? Description = null);

    /// <summary>
    /// Section 04 Donors. Mirrors the 49 codes DON compiles against.
    /// </summary>
    public static readonly IReadOnlyList<PermissionSeed> Donors =
    [
        new("DON.View", "View donors section", "DON", "Section", PermissionAction.View),

        new("don.donors.view", "View donors", "DON", "Donors", PermissionAction.View),
        new("don.donors.create", "Create donors", "DON", "Donors", PermissionAction.Create, IsSensitive: true),
        new("don.donors.edit", "Edit donors", "DON", "Donors", PermissionAction.Edit),
        new("don.donors.submit", "Submit donors", "DON", "Donors", PermissionAction.Submit),
        new("don.donors.approve", "Approve donors", "DON", "Donors", PermissionAction.Approve, IsSensitive: true),
        new("don.donors.cancel", "Cancel donors", "DON", "Donors", PermissionAction.Operate, IsSensitive: true),
        new("don.donors.archive", "Archive donors", "DON", "Donors", PermissionAction.Operate, IsSensitive: true),
        new("don.donors.export", "Export donors", "DON", "Donors", PermissionAction.Export, IsSensitive: true),
        new("don.donors.view-sensitive-contact", "View unmasked donor contact", "DON", "Donors",
            PermissionAction.View, IsSensitive: true,
            Description: "Unmasks e-mail and phone in list, export and support views."),
        new("don.donors.view-confidential-evidence", "View confidential evidence", "DON", "Donors",
            PermissionAction.View, IsSensitive: true),

        new("don.lead-work-queue.view", "View lead work queue", "DON", "LeadWorkQueue", PermissionAction.View),
        new("don.lead-work-queue.accept", "Accept lead", "DON", "LeadWorkQueue", PermissionAction.Operate),
        new("don.lead-work-queue.assign", "Assign lead", "DON", "LeadWorkQueue", PermissionAction.Operate),
        new("don.lead-work-queue.contact", "Contact lead", "DON", "LeadWorkQueue", PermissionAction.Operate),
        new("don.lead-work-queue.qualify", "Qualify lead", "DON", "LeadWorkQueue", PermissionAction.Operate),
        new("don.lead-work-queue.close", "Close lead", "DON", "LeadWorkQueue", PermissionAction.Operate, IsSensitive: true),

        new("don.lead-capture.view", "View lead capture", "DON", "LeadCapture", PermissionAction.View),
        new("don.lead-capture.save", "Save lead capture", "DON", "LeadCapture", PermissionAction.Edit),
        new("don.lead-capture.deduplicate", "Deduplicate lead", "DON", "LeadCapture", PermissionAction.Operate),
        new("don.lead-capture.submit", "Submit lead", "DON", "LeadCapture", PermissionAction.Submit),
        new("don.lead-capture.delete-draft", "Delete lead draft", "DON", "LeadCapture", PermissionAction.Operate, IsSensitive: true),

        new("don.donor-360.view", "View Donor 360", "DON", "Donor360", PermissionAction.View),
        new("don.donor-360.correct", "Correct donor record", "DON", "Donor360", PermissionAction.Edit, IsSensitive: true),
        new("don.donor-360.follow-up", "Create follow-up", "DON", "Donor360", PermissionAction.Operate),
        new("don.donor-360.create-intent", "Create donation intent", "DON", "Donor360", PermissionAction.Create),
        new("don.donor-360.delete-draft", "Delete donor draft", "DON", "Donor360", PermissionAction.Operate, IsSensitive: true),

        new("don.duplicate-review.view", "View duplicate review", "DON", "DuplicateReview", PermissionAction.View),
        new("don.duplicate-review.merge", "Merge duplicates", "DON", "DuplicateReview", PermissionAction.Operate, IsSensitive: true),
        new("don.duplicate-review.reject-candidate", "Reject duplicate candidate", "DON", "DuplicateReview",
            PermissionAction.Operate, IsSensitive: true),

        new("don.consent-and-preference-centre.view", "View consent centre", "DON", "Consent", PermissionAction.View),
        new("don.consent-and-preference-centre.grant", "Grant consent", "DON", "Consent", PermissionAction.Operate, IsSensitive: true),
        new("don.consent-and-preference-centre.withdraw", "Withdraw consent", "DON", "Consent", PermissionAction.Operate, IsSensitive: true),
        new("don.consent-and-preference-centre.correct", "Correct consent", "DON", "Consent", PermissionAction.Edit, IsSensitive: true),

        new("don.assignment-board.view", "View assignment board", "DON", "AssignmentBoard", PermissionAction.View),
        new("don.assignment-board.assign", "Assign from board", "DON", "AssignmentBoard", PermissionAction.Operate),
        new("don.assignment-board.reassign", "Reassign from board", "DON", "AssignmentBoard", PermissionAction.Operate),
        new("don.assignment-board.bulk-route", "Bulk route leads", "DON", "AssignmentBoard", PermissionAction.Operate, IsSensitive: true),

        new("don.donor-identity-verification.view", "View identity verification", "DON", "Verification", PermissionAction.View),
        new("don.donor-identity-verification.send-challenge", "Send verification challenge", "DON", "Verification",
            PermissionAction.Operate, IsSensitive: true),
        new("don.donor-identity-verification.verify-code", "Verify code", "DON", "Verification",
            PermissionAction.Operate, IsSensitive: true),
        new("don.donor-identity-verification.escalate-review", "Escalate verification", "DON", "Verification",
            PermissionAction.Operate, IsSensitive: true),
        new("don.donor-identity-verification.cancel-verification", "Cancel verification", "DON", "Verification",
            PermissionAction.Operate, IsSensitive: true),

        new("don.follow-up-planner.view", "View follow-up planner", "DON", "FollowUp", PermissionAction.View),
        new("don.follow-up-planner.schedule-follow-up", "Schedule follow-up", "DON", "FollowUp", PermissionAction.Operate),
        new("don.follow-up-planner.assign", "Assign follow-up", "DON", "FollowUp", PermissionAction.Operate),
        new("don.follow-up-planner.mark-complete", "Complete follow-up", "DON", "FollowUp", PermissionAction.Operate),
        new("don.follow-up-planner.reschedule", "Reschedule follow-up", "DON", "FollowUp", PermissionAction.Operate),
        new("don.follow-up-planner.cancel-task", "Cancel follow-up", "DON", "FollowUp", PermissionAction.Operate, IsSensitive: true)
    ];

    /// <summary>
    /// Section 03 Campaigns. Kept deliberately small: the CAM service does not yet enforce
    /// permission claims, so these exist to make the menu and role screens meaningful rather
    /// than to gate an endpoint. Expand when CAM adopts the same attribute.
    /// </summary>
    public static readonly IReadOnlyList<PermissionSeed> Campaigns =
    [
        new("CAM.View", "View campaigns section", "CAM", "Section", PermissionAction.View),

        // ---- Campaigns ----------------------------------------------------------------
        //
        // ONE CODE PER LIFECYCLE TRANSITION, not one "operate" code covering all of them. That
        // is what lets an Organisation grant a Campaign Owner the ability to pause a campaign
        // without also granting the ability to approve one - a distinction the seven codes below
        // can express and a single cam.campaigns.operate could not.
        new("cam.campaigns.view", "View campaigns", "CAM", "Campaigns", PermissionAction.View),
        new("cam.campaigns.create", "Create campaigns", "CAM", "Campaigns", PermissionAction.Create),
        new("cam.campaigns.edit", "Edit campaigns", "CAM", "Campaigns", PermissionAction.Edit),
        new("cam.campaigns.submit", "Submit campaigns for approval", "CAM", "Campaigns", PermissionAction.Submit),
        new("cam.campaigns.approve", "Approve campaigns", "CAM", "Campaigns", PermissionAction.Approve, IsSensitive: true),
        new("cam.campaigns.activate", "Activate campaigns", "CAM", "Campaigns", PermissionAction.Operate, IsSensitive: true),
        new("cam.campaigns.pause", "Pause campaigns", "CAM", "Campaigns", PermissionAction.Operate),
        new("cam.campaigns.resume", "Resume campaigns", "CAM", "Campaigns", PermissionAction.Operate),
        new("cam.campaigns.request-close", "Request campaign closure", "CAM", "Campaigns", PermissionAction.Submit),
        new("cam.campaigns.close", "Approve campaign closure", "CAM", "Campaigns", PermissionAction.Approve, IsSensitive: true),
        new("cam.campaigns.delete-draft", "Delete draft campaigns", "CAM", "Campaigns", PermissionAction.Operate, IsSensitive: true),
        new("cam.campaigns.export", "Export campaigns", "CAM", "Campaigns", PermissionAction.Export, IsSensitive: true),
        new("cam.campaigns.view-history", "View campaign history", "CAM", "Campaigns", PermissionAction.View),

        // ---- Tracking assets -------------------------------------------------------------------
        new("cam.tracking-assets.view", "View tracking assets", "CAM", "TrackingAssets", PermissionAction.View),
        new("cam.tracking-assets.create", "Create tracking assets", "CAM", "TrackingAssets", PermissionAction.Create),
        new("cam.tracking-assets.edit", "Edit tracking assets", "CAM", "TrackingAssets", PermissionAction.Edit),
        new("cam.tracking-assets.submit", "Submit tracking assets", "CAM", "TrackingAssets", PermissionAction.Submit),
        new("cam.tracking-assets.approve", "Approve tracking assets", "CAM", "TrackingAssets", PermissionAction.Approve, IsSensitive: true),
        new("cam.tracking-assets.activate", "Activate tracking assets", "CAM", "TrackingAssets", PermissionAction.Operate),
        new("cam.tracking-assets.deactivate", "Deactivate tracking assets", "CAM", "TrackingAssets", PermissionAction.Operate, IsSensitive: true),
        new("cam.tracking-assets.export", "Export tracking assets", "CAM", "TrackingAssets", PermissionAction.Export, IsSensitive: true),

        // ---- Readiness checklist -------------------------------------------------------------------
        //
        // Pass and fail are SEPARATE codes on purpose: an Organisation may well want somebody to
        // be able to record a problem without being able to sign a check off as clear.
        new("cam.readiness.view", "View readiness checklist", "CAM", "Readiness", PermissionAction.View),
        new("cam.readiness.create", "Add readiness checks", "CAM", "Readiness", PermissionAction.Create),
        new("cam.readiness.edit", "Edit readiness checks", "CAM", "Readiness", PermissionAction.Edit),
        new("cam.readiness.pass", "Pass readiness checks", "CAM", "Readiness", PermissionAction.Approve, IsSensitive: true),
        new("cam.readiness.fail", "Fail readiness checks", "CAM", "Readiness", PermissionAction.Operate),
        new("cam.readiness.approve", "Approve readiness", "CAM", "Readiness", PermissionAction.Approve, IsSensitive: true),
        new("cam.readiness.manage-blockers", "Manage readiness blockers", "CAM", "Readiness", PermissionAction.Operate),
        new("cam.readiness.return-to-draft", "Return a campaign to draft", "CAM", "Readiness", PermissionAction.Operate, IsSensitive: true),

        // ---- Budget and target plans ------------------------------------------------------------------
        //
        // ALLOCATE, REVISE, SUBMIT AND APPROVE ARE FOUR CODES, not one "manage budgets". A budget
        // is where an organisation commits its money, and the whole point of the separation is that
        // a person who prepares figures must not also be the person who commits to them. CAM's
        // handler refuses to let one person do both to the same version - a refusal only meaningful
        // if the two rights are separately grantable.
        new("cam.budget-plans.view", "View budget and target plans", "CAM", "BudgetPlans", PermissionAction.View),
        new("cam.budget-plans.allocate", "Allocate budget and target plans", "CAM", "BudgetPlans", PermissionAction.Create),
        new("cam.budget-plans.revise", "Revise budget and target plans", "CAM", "BudgetPlans", PermissionAction.Edit),
        new("cam.budget-plans.submit", "Submit budget plans for approval", "CAM", "BudgetPlans", PermissionAction.Submit),
        new("cam.budget-plans.approve", "Approve budget plans", "CAM", "BudgetPlans", PermissionAction.Approve, IsSensitive: true),
        new("cam.budget-plans.reject", "Reject budget plans", "CAM", "BudgetPlans", PermissionAction.Approve),
        new("cam.budget-plans.export", "Export budget plans", "CAM", "BudgetPlans", PermissionAction.Export, IsSensitive: true),

        // ---- Attribution ------------------------------------------------------------------------------
        //
        // The export is sensitive and the view is not: the explorer shows donor names alongside
        // amounts on screen, but an export puts them in a file that outlives the session.
        new("cam.attribution.view", "View donation attribution", "CAM", "Attribution", PermissionAction.View),
        new("cam.attribution.export", "Export attributed donations", "CAM", "Attribution", PermissionAction.Export, IsSensitive: true),
        new("cam.attribution.request-correction", "Request an attribution correction", "CAM", "Attribution", PermissionAction.Submit),

        // ---- Reference data -------------------------------------------------------------------------
        new("cam.reference.view", "View campaign reference data", "CAM", "Reference", PermissionAction.View),

        // PLATFORM-ONLY. Channel, Source and Medium codes appear in tracking URLs and in
        // reporting that spans Organisations, so one code has to mean one thing platform-wide -
        // which is exactly why a Tenant role must never be able to carry this.
        new("cam.reference.manage", "Maintain campaign reference data", "CAM", "Reference",
            PermissionAction.Operate, IsSensitive: true, IsPlatformOnly: true)
    ];

    // ---- Section 12 Global masters: NO LONGER LISTED HERE ---------------------------------
    //
    // GlobalMaster used to be a separate service, so its five coarse codes (gm.masters.view
    // and friends) lived in this file alongside DON and CAM. It has since been migrated INTO
    // IAM, which now owns the Country, StateProvince, City, Currency and TimeZone endpoints
    // and enforces them itself.
    //
    // Its codes therefore moved to PermissionCodes.GlobalMaster, where they are seeded through
    // PermissionCodes.AllTenant like every other code IAM owns, and were made granular in the
    // move: gm.countries.create rather than gm.masters.create, so an Organisation can be given
    // the city list without also being given the currency list.
    //
    // The old gm.masters.* rows are left alone in databases that already have them. The seeder
    // only ever inserts, so nothing is dropped; they are simply no longer attached to a role
    // or checked by an endpoint. Do not re-add them here - two parallel schemes for the same
    // module is how a permission ends up enforced in one place and ignored in another.

    /// <summary>
    /// Section 06 Donations and payments. Mirrors the 33 codes PAY compiles against, in
    /// <c>YDot.PAY.Application/Common/Constants/PermissionCodes.cs</c>.
    ///
    /// MORE OF THESE ARE MARKED SENSITIVE THAN IN ANY OTHER MODULE, and the reason is simply
    /// what they do: approving a refund moves money out of the organisation, voiding a receipt
    /// invalidates a tax document a donor may already have claimed on, and configuring a gateway
    /// account decides which bank account a charity's income lands in.
    ///
    /// THE SEGREGATION OF DUTIES ON REFUNDS IS NOT EXPRESSIBLE HERE, and that is worth stating
    /// so nobody later assumes the permissions are the whole control. <c>pay.refunds.request</c>
    /// and <c>pay.refunds.approve</c> can both be granted to one person - what the PAY handler
    /// enforces is that the same person cannot approve the case THEY raised, which is a rule
    /// about a record rather than about a role.
    /// </summary>
    public static readonly IReadOnlyList<PermissionSeed> Payments =
    [
        new("PAY.View", "View donations and payments section", "PAY", "Section", PermissionAction.View),

        // ---- Donation intents ----------------------------------------------------------------
        new("pay.intents.view", "View donation intents", "PAY", "Intents", PermissionAction.View),
        new("pay.intents.create", "Create donation intents", "PAY", "Intents", PermissionAction.Create),
        new("pay.intents.cancel", "Cancel donation intents", "PAY", "Intents", PermissionAction.Operate, IsSensitive: true),
        new("pay.intents.resend-link", "Re-send a payment link", "PAY", "Intents", PermissionAction.Operate),
        new("pay.intents.export", "Export donation intents", "PAY", "Intents", PermissionAction.Export, IsSensitive: true),

        // ---- Donations -------------------------------------------------------------------------
        new("pay.donations.view", "View donations", "PAY", "Donations", PermissionAction.View),

        // Recording a gift with no gateway to corroborate it is an assertion by a person, which
        // is why it is separated from every other donation permission and marked sensitive.
        new("pay.donations.record-offline", "Record an offline donation", "PAY", "Donations",
            PermissionAction.Create, IsSensitive: true,
            Description: "Records a cheque, bank transfer or cash gift taken outside the gateway."),

        new("pay.donations.export", "Export donations", "PAY", "Donations", PermissionAction.Export, IsSensitive: true),
        new("pay.donations.reconcile", "Reconcile donations", "PAY", "Donations", PermissionAction.Operate, IsSensitive: true),

        // The one permission that changes what a READ returns rather than what a write may do.
        new("pay.donations.view-sensitive-donor", "View unmasked donor details", "PAY", "Donations",
            PermissionAction.View, IsSensitive: true,
            Description:
                "Unmasks the donor's e-mail, mobile, address and tax identifier on donation "
                + "screens and in exports. Using it is audited as well as holding it."),

        // ---- Payments and the gateway event queue -------------------------------------------------
        new("pay.payments.verify", "Verify payments with the gateway", "PAY", "Payments", PermissionAction.Operate),
        new("pay.payments.view-events", "View gateway events", "PAY", "Payments", PermissionAction.View, IsSensitive: true,
            Description: "Includes the verbatim webhook payload, which can contain donor contact details."),
        new("pay.payments.reprocess-event", "Reprocess a gateway event", "PAY", "Payments",
            PermissionAction.Operate, IsSensitive: true),
        new("pay.payments.dismiss-event", "Dismiss a gateway event", "PAY", "Payments",
            PermissionAction.Operate, IsSensitive: true),

        // Safe retry verifies with the gateway BEFORE retrying and refuses if the payment
        // actually succeeded - which is why it is an operate permission of its own rather than
        // part of the ordinary payment flow.
        new("pay.payments.safe-retry", "Safely retry a failed payment", "PAY", "Payments",
            PermissionAction.Operate, IsSensitive: true,
            Description: "Verifies the previous attempt first and refuses if the donor already paid."),

        // ---- Receipts ------------------------------------------------------------------------------
        new("pay.receipts.view", "View receipts", "PAY", "Receipts", PermissionAction.View),
        new("pay.receipts.issue", "Issue receipts", "PAY", "Receipts", PermissionAction.Create, IsSensitive: true),
        new("pay.receipts.correct", "Correct receipts", "PAY", "Receipts", PermissionAction.Edit, IsSensitive: true,
            Description: "Issues a new version superseding the original. The original is never edited."),
        new("pay.receipts.void", "Void receipts", "PAY", "Receipts", PermissionAction.Operate, IsSensitive: true),
        new("pay.receipts.resend", "Re-send receipts", "PAY", "Receipts", PermissionAction.Operate),
        new("pay.receipts.export", "Export receipts", "PAY", "Receipts", PermissionAction.Export, IsSensitive: true),

        // ---- Refunds ---------------------------------------------------------------------------------
        //
        // REQUEST AND APPROVE ARE SEPARATE CODES so an organisation CAN split them between two
        // people. The platform additionally guarantees that one person cannot do both to the
        // same case, whatever it grants.
        new("pay.refunds.view", "View refunds", "PAY", "Refunds", PermissionAction.View),
        new("pay.refunds.request", "Request refunds", "PAY", "Refunds", PermissionAction.Submit),
        new("pay.refunds.approve", "Approve refunds", "PAY", "Refunds", PermissionAction.Approve, IsSensitive: true,
            Description: "Sends money back to the donor. Never available on a case the caller raised."),
        new("pay.refunds.reject", "Reject refunds", "PAY", "Refunds", PermissionAction.Approve, IsSensitive: true),
        new("pay.refunds.export", "Export refunds", "PAY", "Refunds", PermissionAction.Export, IsSensitive: true),

        // ---- Chargebacks ---------------------------------------------------------------------------------
        new("pay.chargebacks.view", "View chargebacks", "PAY", "Chargebacks", PermissionAction.View),
        new("pay.chargebacks.assign", "Assign chargeback cases", "PAY", "Chargebacks", PermissionAction.Operate),
        new("pay.chargebacks.submit-evidence", "Submit chargeback evidence", "PAY", "Chargebacks",
            PermissionAction.Submit, IsSensitive: true),
        new("pay.chargebacks.resolve", "Resolve chargeback cases", "PAY", "Chargebacks",
            PermissionAction.Approve, IsSensitive: true),

        // ---- Gateway configuration -------------------------------------------------------------------------
        new("pay.gateway.view", "View gateway configuration", "PAY", "Gateway", PermissionAction.View, IsSensitive: true),

        // THE MOST CONSEQUENTIAL PERMISSION IN THE MODULE. It decides which merchant account an
        // organisation's donations settle into - which is to say, whose bank account the money
        // reaches. It is deliberately not part of any role that also handles donations.
        new("pay.gateway.manage", "Configure the payment gateway", "PAY", "Gateway",
            PermissionAction.Operate, IsSensitive: true,
            Description:
                "Sets the merchant account donations settle into. Holds only secret REFERENCES, "
                + "never secrets, but changing one re-points where the money goes.")
    ];

    /// <summary>Every non-IAM code the seeder creates.</summary>
    public static IReadOnlyList<PermissionSeed> AllOtherModules =>
        [.. Donors, .. Campaigns, .. Payments];
}

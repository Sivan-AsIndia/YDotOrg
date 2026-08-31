namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// The standard role set every new Organisation is created with.
///
/// WHY THIS IS A BLUEPRINT AND NOT SHARED ROWS. Roles are Tenant-specific, so these
/// definitions are COPIED into each Organisation as its own rows. Two Organisations both
/// having CAMPAIGN_MANAGER is expected, and the two have nothing to do with one another —
/// one can edit or delete its copy without the other noticing. Sharing a single row would
/// mean an Organisation adjusting its administrator role adjusted everybody else too.
///
/// A NOTE ON TENANT_ADMIN. It carries no permission list at all. Instead it is created with
/// <c>GrantsAllTenantPermissions</c>, which means "everything inside this Organisation,
/// whatever that turns out to be". That is what stops the addition of a new module requiring
/// every existing customer to re-map their administrator — and it is emphatically not a way
/// out of the Organisation, because the query filter and the token tenant_id still apply.
///
/// NOTHING HERE MAY CARRY A PLATFORM CODE. The seeder draws only from the Tenant-assignable
/// catalogue, so <c>platform.organisations.approve</c> and its siblings can never reach a
/// Tenant role however this file is edited.
/// </summary>
public static class TenantRoleDefinitions
{
    /// <summary>One role blueprint.</summary>
    public sealed record RoleDefinition(
        string Code,
        string Name,
        string Description,
        int Priority,
        IReadOnlyList<string> PermissionCodes,
        bool GrantsAll = false,
        bool IsDefault = false,
        bool IsPrivileged = false);

    public static readonly IReadOnlyList<RoleDefinition> All =
    [
        new(
            RoleCodes.TenantAdmin,
            "Organisation Administrator",
            "Full control of this organisation: users, roles, menus, settings and every module.",
            Priority: 100,
            // Deliberately empty. GrantsAll is the grant.
            PermissionCodes: [],
            GrantsAll: true,
            IsPrivileged: true),

        new(
            RoleCodes.UserAdministrator,
            "User Administrator",
            "Creates and manages users, and assigns them to roles.",
            Priority: 80,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                PermissionCodes.UsersView, PermissionCodes.UsersCreate, PermissionCodes.UsersEdit,
                PermissionCodes.UsersInvite, PermissionCodes.UsersSuspend, PermissionCodes.UsersReactivate,
                PermissionCodes.UsersDeactivate, PermissionCodes.UsersResetPassword, PermissionCodes.UsersUnlock,
                PermissionCodes.UsersExport, PermissionCodes.UsersBulkAdminister,
                PermissionCodes.RolesView, PermissionCodes.RolesAssignUsers,
                PermissionCodes.PermissionsView,
                PermissionCodes.UserSecurityView, PermissionCodes.UserSecurityRevokeSession,
                PermissionCodes.UserSecurityResetMfa, PermissionCodes.UserSecurityForceSignOut,
                PermissionCodes.MenusView,
                PermissionCodes.OrganisationView
            ],
            IsPrivileged: true),

        new(
            RoleCodes.AccessApprover,
            "Access Approver",
            "Decides access requests and completes access reviews. Cannot create users, by design.",
            Priority: 75,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                PermissionCodes.UsersView,
                PermissionCodes.RolesView,
                PermissionCodes.PermissionsView,
                PermissionCodes.AccessRequestsView, PermissionCodes.AccessRequestsApprove,
                PermissionCodes.AccessRequestsReject,
                PermissionCodes.AccessReviewsView, PermissionCodes.AccessReviewsDecide,
                PermissionCodes.AuditView,
                PermissionCodes.OrganisationView
            ],
            IsPrivileged: true),

        new(
            RoleCodes.Auditor,
            "Auditor",
            "Read-only across the organisation, including the audit trail. Changes nothing.",
            Priority: 70,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                PermissionCodes.UsersView, PermissionCodes.UsersExport,
                PermissionCodes.RolesView, PermissionCodes.RolesExport,
                PermissionCodes.PermissionsView, PermissionCodes.PermissionsExport,
                PermissionCodes.AccessRequestsView,
                PermissionCodes.AccessReviewsView, PermissionCodes.AccessReviewsExport,
                PermissionCodes.AuditView, PermissionCodes.AuditExport, PermissionCodes.AuditViewSensitive,
                PermissionCodes.UserSecurityView,
                PermissionCodes.MenusView,
                PermissionCodes.OrganisationView,

                // ---- Read-only across the money module -----------------------------------
                //
                // AN AUDITOR WHO CANNOT SEE THE PAYMENTS CANNOT AUDIT ANYTHING THAT MATTERS.
                // This is the module where the questions an audit exists to ask actually live:
                // who approved this refund, why was that receipt voided, which donations have
                // never been reconciled.
                //
                // EXPORT IS INCLUDED AND SENSITIVE-DONOR IS NOT. An auditor needs to take the
                // figures away; they do not need donors' unmasked tax identifiers to check that
                // the figures agree with the bank.
                "PAY.View",
                "pay.donations.view", "pay.donations.export",
                "pay.intents.view", "pay.intents.export",
                "pay.receipts.view", "pay.receipts.export",
                "pay.refunds.view", "pay.refunds.export",
                "pay.chargebacks.view",
                "pay.gateway.view",

                // ---- Read-only across budgets and attribution -----------------------------
                //
                // "WHAT WAS BUDGETED, WHO APPROVED IT, AND DID THE MONEY ARRIVE WHERE THE
                // TRACKING SAYS IT DID" are audit questions, and neither can be answered from
                // the payments tables alone. No write code of any kind - not even the
                // correction request, which despite its read-only effect on the donation is
                // still somebody asking for a change.
                "CAM.View", "cam.campaigns.view", "cam.reference.view",
                "cam.budget-plans.view", "cam.budget-plans.export",
                "cam.attribution.view", "cam.attribution.export",

                // ---- Read-only across donors and leads ------------------------------------
                //
                // THE MODULE THIS ROLE WAS MISSING ENTIRELY. The description above says
                // "read-only across the organisation" and the grant delivered every module
                // except this one, so an Auditor opening any Donors and Leads screen got a
                // flat 403 - including the consent register, which is the one place the
                // question "were we allowed to contact this person" can actually be answered.
                //
                // SAME LINE AS THE MONEY MODULE ABOVE: view and export, and neither of the two
                // sensitive codes. An auditor needs to see that a consent exists and take the
                // register away; they do not need the donor's unmasked telephone number or the
                // evidence document behind it to check that the record is in order.
                "DON.View",
                "don.donors.view", "don.donors.export",
                "don.donor-360.view",
                "don.lead-work-queue.view",
                "don.lead-capture.view",
                "don.assignment-board.view",
                "don.follow-up-planner.view",
                "don.duplicate-review.view",
                "don.consent-and-preference-centre.view",
                "don.donor-identity-verification.view"
            ]),

        new(
            RoleCodes.CampaignManager,
            "Campaign Manager",
            "Runs fundraising campaigns and works the donor and lead queues.",
            Priority: 60,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                PermissionCodes.UsersView,
                // CAMPAIGN MANAGER: section 5.2 of the module brief. Creates, edits, submits,
                // APPROVES and runs the whole lifecycle - but the API still refuses an approval
                // of anything they personally created or submitted, whatever this list says.
                // That segregation-of-duties rule is enforced per record and cannot be granted
                // away by a role.
                "CAM.View",
                "cam.campaigns.view", "cam.campaigns.create", "cam.campaigns.edit",
                "cam.campaigns.submit", "cam.campaigns.approve", "cam.campaigns.activate",
                "cam.campaigns.pause", "cam.campaigns.resume", "cam.campaigns.request-close",
                "cam.campaigns.close", "cam.campaigns.delete-draft", "cam.campaigns.export",
                "cam.campaigns.view-history",
                "cam.tracking-assets.view", "cam.tracking-assets.create", "cam.tracking-assets.edit",
                "cam.tracking-assets.submit", "cam.tracking-assets.approve",
                "cam.tracking-assets.activate", "cam.tracking-assets.deactivate",
                "cam.tracking-assets.export",
                "cam.readiness.view", "cam.readiness.create", "cam.readiness.edit",
                "cam.readiness.pass", "cam.readiness.fail", "cam.readiness.approve",
                "cam.readiness.manage-blockers", "cam.readiness.return-to-draft",

                // Budget plans: the whole surface INCLUDING approval, on the same footing as
                // campaign approval above - and with the same per-record refusal behind it. A
                // Campaign Manager who submits a plan version still cannot approve that version,
                // whatever this list says.
                "cam.budget-plans.view", "cam.budget-plans.allocate", "cam.budget-plans.revise",
                "cam.budget-plans.submit", "cam.budget-plans.approve", "cam.budget-plans.reject",
                "cam.budget-plans.export",

                "cam.attribution.view", "cam.attribution.export",
                "cam.attribution.request-correction",

                "cam.reference.view",
                "DON.View", "don.donors.view", "don.donors.create", "don.donors.edit",
                "don.donor-360.view", "don.donor-360.follow-up", "don.donor-360.create-intent",
                "don.lead-work-queue.view", "don.lead-work-queue.accept", "don.lead-work-queue.assign",
                "don.lead-work-queue.contact", "don.lead-work-queue.qualify",
                "don.lead-work-queue.close",
                "don.lead-capture.view", "don.lead-capture.save", "don.lead-capture.submit",
                "don.lead-capture.delete-draft",
                "don.assignment-board.view", "don.assignment-board.assign", "don.assignment-board.reassign",

                // BULK ROUTING IS A SUPERVISOR'S ACT and belonged to no role at all, so the
                // assignment board's bulk route - the whole reason that screen exists - could be
                // used by nobody but the organisation administrator. It sits here rather than
                // with the fundraising officer because moving fifty leads between colleagues is
                // a decision about the team's workload, not about one's own.
                "don.assignment-board.bulk-route",

                // APPROVE AND CANCEL, NOT SUBMIT. The fundraising officer submits a donor record
                // and this role decides it. Keeping submit out of this list is what stops one
                // person completing both halves.
                "don.donors.approve", "don.donors.cancel",
                "don.donor-360.correct", "don.donor-360.delete-draft",
                "don.follow-up-planner.cancel-task",

                "don.follow-up-planner.view", "don.follow-up-planner.schedule-follow-up",
                "don.follow-up-planner.assign", "don.follow-up-planner.mark-complete",
                .. PermissionCodes.GlobalMaster.ReadOnly
            ]),

        new(
            RoleCodes.CampaignOwner,
            "Campaign Owner",
            "Runs their own campaigns end to end. Creates, edits and submits them, and operates "
            + "them once approved - but approves nothing.",
            Priority: 55,
            PermissionCodes:
            [
                PermissionCodes.IamView,

                // SECTION 5.3: NO APPROVAL CODES AT ALL. Not cam.campaigns.approve, not
                // cam.campaigns.close, not cam.tracking-assets.approve, not cam.readiness.pass.
                // An owner prepares and operates; a second person decides.
                //
                // They DO hold request-close, because raising a close request is asking for a
                // decision rather than making one - and the API refuses the person who raised it
                // as the one who approves it.
                "CAM.View",
                "cam.campaigns.view", "cam.campaigns.create", "cam.campaigns.edit",
                "cam.campaigns.submit", "cam.campaigns.activate", "cam.campaigns.pause",
                "cam.campaigns.resume", "cam.campaigns.request-close",
                "cam.campaigns.delete-draft", "cam.campaigns.export", "cam.campaigns.view-history",

                "cam.tracking-assets.view", "cam.tracking-assets.create",
                "cam.tracking-assets.edit", "cam.tracking-assets.submit",

                // Readiness: they prepare the checklist and record failures, and they raise and
                // clear blockers. Signing a check off as PASSED is an approval, so it is not here.
                "cam.readiness.view", "cam.readiness.create", "cam.readiness.edit",
                "cam.readiness.fail", "cam.readiness.manage-blockers",

                // Budget plans: allocate, revise and SUBMIT - but neither approve nor reject.
                // Consistent with everything else this role holds: an owner prepares and a second
                // person decides. Committing an organisation's money is precisely the decision
                // that rule exists for.
                "cam.budget-plans.view", "cam.budget-plans.allocate", "cam.budget-plans.revise",
                "cam.budget-plans.submit", "cam.budget-plans.export",

                // They can see how their campaign's income was attributed and raise a correction
                // request; they cannot resolve it into a change.
                "cam.attribution.view", "cam.attribution.request-correction",

                "cam.reference.view",

                // Enough of the donor module to see who is giving to their campaigns.
                "DON.View", "don.donors.view", "don.donor-360.view",

                .. PermissionCodes.GlobalMaster.ReadOnly
            ]),

        new(
            RoleCodes.FundraisingOfficer,
            "Fundraising Officer",
            "Works donors and leads day to day, without campaign administration.",
            Priority: 50,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                "DON.View", "don.donors.view", "don.donors.create", "don.donors.edit",
                "don.donor-360.view", "don.donor-360.follow-up", "don.donor-360.create-intent",
                "don.lead-work-queue.view", "don.lead-work-queue.accept", "don.lead-work-queue.contact",
                "don.lead-work-queue.qualify",

                // CLOSING A LEAD IS PART OF WORKING ONE. This code belonged to no role, so the
                // person who owns a lead could take it, contact it and qualify it - and then had
                // no way to record that it went nowhere. A queue nobody can close only grows.
                "don.lead-work-queue.close",

                "don.lead-capture.view", "don.lead-capture.save", "don.lead-capture.deduplicate",
                "don.lead-capture.submit",

                // Their own unsubmitted draft. The server already refuses this once the draft has
                // consent evidence, an assignment or a donor behind it.
                "don.lead-capture.delete-draft",

                // SUBMIT AND NOT APPROVE. The officer puts a donor record forward; the campaign
                // manager decides. Splitting the pair across two roles is what makes the approval
                // a second pair of eyes rather than a formality the same person completes.
                "don.donors.submit",
                "don.donor-360.correct", "don.donor-360.delete-draft",
                "don.follow-up-planner.cancel-task",

                "don.consent-and-preference-centre.view",
                "don.follow-up-planner.view", "don.follow-up-planner.schedule-follow-up",
                "don.follow-up-planner.mark-complete", "don.follow-up-planner.reschedule",
                "CAM.View", "cam.campaigns.view", "cam.reference.view",

                // Which channel produced a gift is exactly what a fundraising officer needs when
                // deciding where to spend their time. Read-only, plus the ability to flag one that
                // looks wrong.
                "cam.attribution.view", "cam.attribution.request-correction",

                .. PermissionCodes.GlobalMaster.ReadOnly
            ]),

        new(
            RoleCodes.FinanceOfficer,
            "Finance Officer",
            "Reconciliation, receipts, refund decisions and financial review.",
            Priority: 50,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                "DON.View", "don.donors.view", "don.donors.export",
                "don.donors.view-sensitive-contact",
                "CAM.View", "cam.campaigns.view", "cam.reference.view",

                // ---- Budgets -----------------------------------------------------------------
                //
                // FINANCE APPROVES BUDGETS AND DOES NOT WRITE THEM, which mirrors exactly how this
                // role treats refunds below. Allocate and revise are absent; approve and reject are
                // present. A finance officer who both prepared a budget and approved it would be
                // the single pair of eyes the whole arrangement exists to avoid.
                "cam.budget-plans.view", "cam.budget-plans.approve", "cam.budget-plans.reject",
                "cam.budget-plans.export",

                // Attribution is read-only for finance: they need to see which campaign income was
                // credited to when reconciling, and a mis-attribution is raised as a request.
                "cam.attribution.view", "cam.attribution.export",
                "cam.attribution.request-correction",

                // ---- Payments -------------------------------------------------------------
                //
                // THIS ROLE DECIDES REFUNDS AND DOES NOT RAISE THEM. pay.refunds.request is
                // deliberately absent and belongs to PAYMENT_OPERATIONS instead. The platform
                // already refuses to let anybody approve the case they raised, but that only
                // produces a real second pair of eyes if the two capabilities start in different
                // roles - otherwise every organisation has to notice the problem for itself.
                "PAY.View",
                "pay.donations.view", "pay.donations.export", "pay.donations.reconcile",
                "pay.donations.view-sensitive-donor",
                "pay.intents.view", "pay.intents.export",

                // Correcting and voiding a receipt change a tax document a donor may already
                // have claimed on, which is why they sit with finance rather than with the
                // operator who issues the original.
                "pay.receipts.view", "pay.receipts.issue", "pay.receipts.correct",
                "pay.receipts.void", "pay.receipts.resend", "pay.receipts.export",

                "pay.refunds.view", "pay.refunds.approve", "pay.refunds.reject",
                "pay.refunds.export",
                "pay.chargebacks.view", "pay.chargebacks.resolve",

                .. PermissionCodes.GlobalMaster.ReadOnly
            ],
            IsPrivileged: true),

        new(
            RoleCodes.PaymentOperations,
            "Payment Operations",
            "Works the payment support and gateway event queues, issues receipts and raises refunds.",
            Priority: 50,
            PermissionCodes:
            [
                PermissionCodes.IamView,

                // Enough of the donor and campaign modules to make a payment screen legible -
                // a donation with no donor name and no campaign is very hard to work with.
                "DON.View", "don.donors.view",
                "CAM.View", "cam.campaigns.view",

                "PAY.View",

                // Section 23: the support queue and safe retry are the core of this role.
                "pay.intents.view", "pay.intents.create", "pay.intents.cancel",
                "pay.intents.resend-link",
                "pay.payments.verify", "pay.payments.safe-retry",
                "pay.payments.view-events", "pay.payments.reprocess-event",
                "pay.payments.dismiss-event",

                "pay.donations.view", "pay.donations.record-offline",

                // Issue and re-send, but NOT correct or void. Re-issuing a corrected tax
                // document is a finance decision.
                "pay.receipts.view", "pay.receipts.issue", "pay.receipts.resend",

                // RAISES a refund, never decides one. See the note on FINANCE_OFFICER above.
                "pay.refunds.view", "pay.refunds.request",

                "pay.chargebacks.view", "pay.chargebacks.assign",
                "pay.chargebacks.submit-evidence",

                .. PermissionCodes.GlobalMaster.ReadOnly
            ],
            IsPrivileged: true),

        new(
            RoleCodes.DataSteward,
            "Data Steward",
            "Donor record quality: duplicate review, merges and archiving.",
            Priority: 50,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                "DON.View", "don.donors.view", "don.donors.edit",
                "don.donor-360.view", "don.donor-360.correct",

                // ---- Duplicate review -------------------------------------------------------
                //
                // THE WHOLE POINT OF THE ROLE. These three codes belonged to no role at all, so
                // the duplicate review screen was reachable only by the organisation
                // administrator - which in practice meant duplicates were never worked.
                "don.duplicate-review.view", "don.duplicate-review.merge",
                "don.duplicate-review.reject-candidate",

                // DECIDING WHETHER TWO RECORDS ARE THE SAME PERSON REQUIRES SEEING WHAT MATCHED.
                // A steward comparing two records with both contact details masked is being asked
                // to make the call blind, so these two unmask for this role and this role alone
                // outside the administrator.
                "don.donors.view-sensitive-contact", "don.donors.view-confidential-evidence",

                // Archive is the steward's disposal route for a record that should no longer be
                // worked. It is NOT delete: the history stays.
                "don.donors.archive",

                // Consent CORRECTION without grant or withdraw. A steward fixes a mis-recorded
                // consent; they do not make the consent decision itself - that is the donor's,
                // captured by supporter care.
                "don.consent-and-preference-centre.view",
                "don.consent-and-preference-centre.correct",

                "CAM.View", "cam.campaigns.view", "cam.reference.view",
                .. PermissionCodes.GlobalMaster.ReadOnly
            ]),

        new(
            RoleCodes.DonorCare,
            "Supporter Care",
            "Identity verification, consent decisions and supporter follow-up.",
            Priority: 50,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                "DON.View", "don.donors.view", "don.donor-360.view",

                // ---- Identity verification ---------------------------------------------------
                //
                // ALL FIVE CODES BELONGED TO NO ROLE, so nobody but the organisation
                // administrator could send a challenge or verify a code - and identity
                // verification is a thing somebody does on a phone call, not an admin task.
                "don.donor-identity-verification.view",
                "don.donor-identity-verification.send-challenge",
                "don.donor-identity-verification.verify-code",
                "don.donor-identity-verification.escalate-review",
                "don.donor-identity-verification.cancel-verification",

                // ---- Consent ------------------------------------------------------------------
                //
                // The person on the call is the person who hears "stop e-mailing me", so they are
                // the person who must be able to record it the moment it is said.
                "don.consent-and-preference-centre.view",
                "don.consent-and-preference-centre.grant",
                "don.consent-and-preference-centre.withdraw",
                "don.consent-and-preference-centre.correct",

                // Speaking to a supporter means knowing their number. This role is on the phone
                // to them, so the mask would stop the work rather than protect anybody.
                "don.donors.view-sensitive-contact",

                "don.donor-360.follow-up",
                "don.follow-up-planner.view", "don.follow-up-planner.schedule-follow-up",
                "don.follow-up-planner.mark-complete", "don.follow-up-planner.reschedule",
                "don.follow-up-planner.cancel-task",

                "don.lead-work-queue.view", "don.lead-work-queue.contact",

                "CAM.View", "cam.campaigns.view", "cam.reference.view",
                .. PermissionCodes.GlobalMaster.ReadOnly
            ]),

        new(
            RoleCodes.Volunteer,
            "Volunteer",
            "Limited access for volunteers: capture leads and see their own work.",
            Priority: 20,
            PermissionCodes:
            [
                PermissionCodes.IamView,
                "DON.View", "don.lead-capture.view", "don.lead-capture.save", "don.lead-capture.submit",
                "don.lead-work-queue.view", "don.lead-work-queue.contact",
                "CAM.View", "cam.campaigns.view"
            ]),

        new(
            RoleCodes.StandardUser,
            "Standard User",
            "The baseline role every new user gets when none is chosen. Sign in and see the dashboard.",
            Priority: 10,
            PermissionCodes: [PermissionCodes.IamView],
            IsDefault: true),

        new(
            RoleCodes.DonorPortalUser,
            "Donor Portal User",
            "For donor-portal accounts created from a payment. Sees only their own record.",
            Priority: 5,
            PermissionCodes:
            [
                "DON.View", "don.donor-360.view",
                "don.consent-and-preference-centre.view",
                "don.consent-and-preference-centre.grant",
                "don.consent-and-preference-centre.withdraw",

                // A donor may see their own donations and download their own receipts. The
                // "own" narrowing is a DATA SCOPE on the user, not a permission - these two
                // codes plus the scope are what produce "my giving history" rather than the
                // organisation's whole register.
                "PAY.View", "pay.donations.view", "pay.receipts.view"
            ])
    ];

    /// <summary>The blueprint for one code, or null when there is none.</summary>
    public static RoleDefinition? Find(string code) =>
        All.FirstOrDefault(definition => string.Equals(definition.Code, code, StringComparison.Ordinal));
}

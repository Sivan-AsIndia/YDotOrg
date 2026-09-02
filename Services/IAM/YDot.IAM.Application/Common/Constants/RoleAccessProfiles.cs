using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// What INITIATOR and APPROVER hold, computed from the permission catalogue rather than typed
/// out as two lists of strings.
///
/// WHY IT IS COMPUTED. The alternative is roughly three hundred hand-written codes across the two
/// roles, and a typo in one of them is invisible: the seeder skips a code it cannot find in the
/// catalogue, so a mistyped grant does not fail - it silently does not exist, and the first anyone
/// hears of it is a 403 on a screen that should have worked. Deriving both sets from the same
/// catalogue the permissions are seeded from makes that class of error impossible, and means a
/// permission added to CAM, DON or PAY next month lands in the right role by itself.
///
/// THE RULE, IN ONE LINE EACH:
///
///   INITIATOR  everything except approvals.
///   APPROVER   view, edit, approve, export - plus the operations that follow a decision.
///
/// THE PIVOT IS <c>PermissionAction</c>, not the verb in the code. CAM, DON and PAY declare their
/// action in <see cref="ModulePermissionCatalogue"/>; IAM and GM codes have theirs derived by
/// <see cref="PermissionCodeConventions"/>. That distinction is load-bearing:
/// <c>cam.campaigns.close</c> is declared Approve ("approve a closure request") while
/// <c>don.lead-work-queue.close</c> is declared Operate ("finish with this lead"). A rule reading
/// the word "close" would put one of them in the wrong role.
/// </summary>
public static class RoleAccessProfiles
{
    /// <summary>
    /// Decisions whose code does not spell the word "approve", and which the derivation in
    /// <see cref="PermissionCodeConventions"/> therefore files as Operate.
    ///
    /// These are IAM codes only - CAM, DON and PAY declare their own action and need no help.
    /// Each one ends a request rather than progressing it, so it belongs to the checker and must
    /// be kept out of the maker.
    /// </summary>
    private static readonly IReadOnlyList<string> AdditionalApprovalCodes =
    [
        // Refusing an access request is half of deciding it; the "approve" half is already
        // classified. Leaving this one in INITIATOR would let the raiser close their own request.
        PermissionCodes.AccessRequestsReject,

        // Certifying or revoking access in a review campaign. The verb is "decide", which is
        // exactly what it does.
        PermissionCodes.AccessReviewsDecide
    ];

    /// <summary>
    /// The operational verbs a checker keeps, because they are what HAPPENS to a record once the
    /// decision has been taken.
    ///
    /// AN ALLOW-LIST, NOT A BLOCK-LIST, and deliberately so. Operate is the catch-all bucket - it
    /// holds activate and it also holds delete-draft, archive, void and merge - so a rule naming
    /// what to exclude would hand APPROVER a new destructive verb the day somebody adds one. This
    /// names the few to keep, so anything new stays out until a person decides otherwise.
    ///
    /// NOTHING HERE CREATES OR DESTROYS. That is the test each entry has to pass.
    /// </summary>
    private static readonly IReadOnlyList<string> PostApprovalOperations =
    [
        // ---- IAM: acting on a person's access after reviewing it ----------------------------
        PermissionCodes.UsersSuspend,
        PermissionCodes.UsersReactivate,
        PermissionCodes.RolesActivate,
        PermissionCodes.RolesDeactivate,

        // ---- Global masters: publishing and withdrawing a reference row ----------------------
        PermissionCodes.GlobalMaster.CountriesActivate, PermissionCodes.GlobalMaster.CountriesDeactivate,
        PermissionCodes.GlobalMaster.StatesActivate, PermissionCodes.GlobalMaster.StatesDeactivate,
        PermissionCodes.GlobalMaster.CitiesActivate, PermissionCodes.GlobalMaster.CitiesDeactivate,
        PermissionCodes.GlobalMaster.CurrenciesActivate, PermissionCodes.GlobalMaster.CurrenciesDeactivate,
        PermissionCodes.GlobalMaster.TimeZonesActivate, PermissionCodes.GlobalMaster.TimeZonesDeactivate,

        // ---- CAM: running a campaign that has been approved ----------------------------------
        // Activate, pause and resume are all downstream of the approval; returning a campaign to
        // draft is the checker sending work back, which is the other half of refusing it.
        "cam.campaigns.activate",
        "cam.campaigns.pause",
        "cam.campaigns.resume",
        "cam.tracking-assets.activate",

        // DECIDING A DISABLE REQUEST. `cam.tracking-assets.request-disable` is the maker's half
        // and is a Submit, so it stays out of this role by the action filter alone.
        "cam.tracking-assets.deactivate",

        // FAILING A READINESS CHECK IS THE OTHER HALF OF PASSING IT, and leaving it out gave
        // APPROVER the strange shape of a checker who could sign a check off but not record that
        // it was not ready. Pass is declared Approve and reaches this role through the action
        // filter; fail is declared Operate, so it has to be named here or the pair comes apart.
        // It stays with INITIATOR too - noticing that something is not ready is the maker's job
        // as much as the checker's.
        "cam.readiness.fail",

        // RESOLVING A BLOCKER IS HERE; RAISING ONE IS NOT.
        //
        // `cam.readiness.manage-blockers` used to be on this list, back when raising and clearing
        // a blocker were the same code. They are two codes now, and only the second belongs to a
        // checker: raising an obstacle is the maker saying something is not ready, and declaring
        // it cleared is what unblocks the pass. One person holding both can wave away their own
        // flag, which is the one thing the blocker exists to prevent.
        "cam.readiness.resolve-blockers",

        "cam.readiness.return-to-draft",

        // ---- DON: decisions on a match and on an identity ------------------------------------
        // Rejecting a duplicate candidate is a decision. MERGING one is not on this list and must
        // never be: a merge takes two donors' donations, receipts and consent history and joins
        // them irreversibly, which is a destructive act however sound the reasoning behind it.
        "don.duplicate-review.reject-candidate",
        "don.donor-identity-verification.escalate-review",

        // ---- PAY: confirming what actually happened to the money -----------------------------
        // Reconciling matches what the gateway reported against what the bank received, and
        // verifying re-asks the gateway about one payment. Both read and confirm; neither moves
        // money, and neither can destroy a record.
        "pay.donations.reconcile",
        "pay.payments.verify"
    ];

    /// <summary>
    /// Approval-classified codes that the MAKER holds anyway.
    ///
    /// ONE ENTRY, AND IT IS A DELIBERATE PRODUCT DECISION rather than a hole in the rule.
    /// <c>cam.readiness.pass</c> is classified Approve because signing a readiness check off is a
    /// declaration that the campaign is ready on that point - but on the campaign checklist the
    /// person who DID the work is the one who knows the work is done, and the gate that actually
    /// protects the campaign is <c>cam.campaigns.approve</c>, which decides the launch itself and
    /// which no maker holds. Requiring a checker to tick off each individual line item made the
    /// checklist a second approval queue in front of the real one.
    ///
    /// THE FOUR-EYES RULE IS UNAFFECTED. A campaign still cannot be approved by the person who
    /// submitted it, and every check on the list being passed does not launch anything.
    ///
    /// Nothing else belongs here. An entry on this list is a maker signing something off, so each
    /// one needs an answer to "what stops them approving their own work", and
    /// <c>cam.campaigns.approve</c> is that answer for this one.
    /// </summary>
    private static readonly IReadOnlyList<string> MakerApprovalExceptions =
    [
        "cam.readiness.pass"
    ];

    /// <summary>
    /// Operate codes the MAKER must not hold, despite Operate being the maker's bucket.
    ///
    /// THE MIRROR OF <see cref="PostApprovalOperations"/>, and needed for the same reason: Operate
    /// is a catch-all, so a handful of genuinely decision-shaped verbs land in it. Each of these
    /// ends something a maker created, which is exactly the act the split exists to send to
    /// somebody else.
    /// </summary>
    private static readonly IReadOnlyList<string> CheckerOnlyOperations =
    [
        // DISABLING A LIVE TRACKING ASSET. It stops a printed QR code and a circulated short link
        // resolving, so the campaign stops being able to attribute anything that arrives through
        // them - not recoverable by reprinting. The maker asks with
        // `cam.tracking-assets.request-disable` and a checker decides.
        "cam.tracking-assets.deactivate",

        // CLEARING A BLOCKER. See the note on the pair in PostApprovalOperations: raising one and
        // waving it away must not be the same person's call.
        "cam.readiness.resolve-blockers"
    ];

    /// <summary>
    /// CAM codes the CHECKER does not hold, beyond what the action filter already excludes.
    ///
    /// A checker's business on a campaign is to approve it, refuse it, or read it. Editing the
    /// campaign, its tracking assets or its checklist is the maker's work, and a checker who
    /// edits the thing they are about to approve has approved their own change. Edit is admitted
    /// platform-wide by the action filter - these three are the campaign module's exceptions.
    /// </summary>
    private static readonly IReadOnlyList<string> CheckerExcludedCodes =
    [
        "cam.campaigns.edit",
        "cam.tracking-assets.edit",
        "cam.readiness.edit"
    ];

    /// <summary>
    /// Every Tenant-assignable code in the platform: IAM and GM from
    /// <see cref="PermissionCodes.AllTenant"/>, and CAM, DON and PAY from
    /// <see cref="ModulePermissionCatalogue"/>.
    ///
    /// Platform-only codes are excluded here and not merely unassigned - they belong to
    /// SUPER_ADMIN, whose authority comes from a flag rather than from grants, so a Tenant role
    /// holding one would be a row that grants nothing and confuses the access-preview screen.
    /// <c>cam.reference.manage</c> is the one non-IAM code this removes.
    /// </summary>
    public static IReadOnlyList<(string Code, PermissionAction Action)> TenantAssignable { get; } =
    [
        .. PermissionCodes.AllTenant
            .Select(code => (Code: code, Action: PermissionCodeConventions.DeriveAction(code))),

        .. ModulePermissionCatalogue.AllOtherModules
            .Where(seed => !seed.IsPlatformOnly)
            .Select(seed => (seed.Code, seed.Action))
    ];

    /// <summary>
    /// Every code that represents an approval decision, however its verb is spelled.
    ///
    /// This is the set INITIATOR is defined by NOT holding, so it is the one place to look when
    /// asking whether the maker-checker split is intact.
    /// </summary>
    public static IReadOnlyList<string> ApprovalCodes { get; } =
    [
        .. TenantAssignable
            .Where(item => item.Action == PermissionAction.Approve)
            .Select(item => item.Code),

        .. AdditionalApprovalCodes
    ];

    /// <summary>
    /// Grants a system role USED TO HOLD and must no longer, per role code.
    ///
    /// WHY THIS LIST HAS TO EXIST. <c>ReconcileSystemRolePermissionsAsync</c> only ever ADDS the
    /// rows a definition is missing; it has never removed one, deliberately, because an
    /// Organisation administrator may have granted a system role something extra and a blanket
    /// "delete anything not in the profile" would silently undo their decision on every restart.
    ///
    /// The cost of that choice is that narrowing a role does not reach a database that has
    /// already run: the row is simply still there, and the role goes on holding a permission the
    /// profile says it does not have. So a narrowing has to be stated, and this is where. Each
    /// entry is removed from that role in every Organisation, once, on start-up.
    ///
    /// AN ENTRY HERE IS A DELIBERATE REMOVAL OF ACCESS SOMEBODY CURRENTLY HAS. Add one only when
    /// the role genuinely must not keep the right, and say why - as opposed to simply not
    /// granting a code to begin with, which needs nothing here.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> WithdrawnGrants { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [RoleCodes.Initiator] =
            [
                // THE MAKER NO LONGER DISABLES A LIVE TRACKING ASSET. Taking one down stops a
                // printed QR code resolving, which is a decision and not an edit; the maker now
                // asks with `cam.tracking-assets.request-disable` and a checker decides.
                "cam.tracking-assets.deactivate"
            ],

            [RoleCodes.Approver] =
            [
                // RAISING A BLOCKER IS THE MAKER'S. The checker's half is the new
                // `cam.readiness.resolve-blockers`, which the profile grants instead.
                "cam.readiness.manage-blockers",

                // A CHECKER DOES NOT EDIT THE THING IT IS ABOUT TO APPROVE. Editing a campaign,
                // its tracking assets or its checklist is the maker's work; a checker who edits
                // and then approves has approved their own change.
                "cam.campaigns.edit",
                "cam.tracking-assets.edit",
                "cam.readiness.edit"
            ]
        };

    /// <summary>
    /// INITIATOR: create, view, edit, submit, operate and export across every module - and no
    /// approval of any kind.
    /// </summary>
    public static IReadOnlyList<string> Initiator { get; } =
    [
        .. TenantAssignable
            .Select(item => item.Code)
            .Where(code => !ApprovalCodes.Contains(code, StringComparer.Ordinal))
            .Concat(MakerApprovalExceptions)
            .Where(code => !CheckerOnlyOperations.Contains(code, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>
    /// APPROVER: view, edit, approve and export across every module, plus
    /// <see cref="PostApprovalOperations"/> - and no creation and no deletion.
    ///
    /// Create is excluded by the action filter, since <c>Create</c> is not one of the four
    /// actions admitted. Deletion is excluded the same way: every destructive verb in the
    /// catalogue - delete, delete-draft, archive, void, cancel, withdraw, merge - is declared or
    /// derived as <c>Operate</c>, and Operate enters this set only by being named explicitly
    /// above.
    /// </summary>
    public static IReadOnlyList<string> Approver { get; } =
    [
        .. TenantAssignable
            .Where(item => item.Action is PermissionAction.View
                                       or PermissionAction.Edit
                                       or PermissionAction.Approve
                                       or PermissionAction.Export)
            .Select(item => item.Code)
            .Concat(AdditionalApprovalCodes)
            .Concat(PostApprovalOperations)
            .Where(code => !CheckerExcludedCodes.Contains(code, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
    ];
}

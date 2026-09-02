namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// Every permission code used by Section 04 - Donors, and the action type each one falls under.
///
/// THESE STRINGS ARE A CROSS-SERVICE CONTRACT. DON cannot issue a claim - it never signs a token -
/// so each of these 49 codes must ALSO exist in IAM (<c>ModulePermissionCatalogue.Donors</c>),
/// where it is seeded into the permission table and attached to roles. If the two drift, the
/// symptom is a 403 on an endpoint that looks correctly configured, because the token never
/// carried the claim the attribute asks for.
///
/// THE ACTION TYPE IS DECLARED, NOT DERIVED FROM THE VERB, and that is what makes the three-role
/// model checkable. DON spells "close" and "cancel" across three unrelated acts -
/// <see cref="LeadWorkQueueClose"/> finishes with a lead, <see cref="VerificationCancel"/> abandons
/// an identity challenge, <see cref="FollowUpPlannerCancelTask"/> drops a scheduled task - so a
/// rule reading the word would classify all three the same and get at least one of them wrong.
/// <see cref="Catalogue"/> states the action for every code, <see cref="RoleCodes.HoldersOf"/>
/// turns an action into the roles that hold it, and <see cref="RolesFor"/> puts the two together
/// so the matrix in the module brief can be read off the model rather than reconstructed from the
/// controllers.
///
/// TO ADD A PERMISSION: add the constant here, add it to <see cref="Catalogue"/> with its action,
/// then add the same literal and the same action to ModulePermissionCatalogue in IAM. Once
/// published, a code may be retired but never renamed: a renamed code silently unreachable is far
/// worse than a retired one that is visibly gone.
/// </summary>
public static class PermissionCodes
{
    // ---- Section level view permission from the developer contract -----------------------
    public const string DonView = "DON.View";

    // ---- Donor resource (section 7 CQRS inventory and section 8 endpoints) ---------------
    public const string DonorsView = "don.donors.view";
    public const string DonorsCreate = "don.donors.create";
    public const string DonorsEdit = "don.donors.edit";
    public const string DonorsSubmit = "don.donors.submit";
    public const string DonorsApprove = "don.donors.approve";
    public const string DonorsCancel = "don.donors.cancel";
    public const string DonorsArchive = "don.donors.archive";
    public const string DonorsExport = "don.donors.export";

    /// <summary>Unmasks e-mail and phone in list, export and support views.</summary>
    public const string DonorsViewSensitiveContact = "don.donors.view-sensitive-contact";

    /// <summary>Unmasks matching evidence, consent evidence and documents.</summary>
    public const string DonorsViewConfidentialEvidence = "don.donors.view-confidential-evidence";

    // ---- SCR-DON-001 Lead work queue -----------------------------------------------------
    public const string LeadWorkQueueView = "don.lead-work-queue.view";
    public const string LeadWorkQueueAccept = "don.lead-work-queue.accept";
    public const string LeadWorkQueueAssign = "don.lead-work-queue.assign";
    public const string LeadWorkQueueContact = "don.lead-work-queue.contact";
    public const string LeadWorkQueueQualify = "don.lead-work-queue.qualify";

    /// <summary>
    /// Finishing with a lead - marking it lost or dormant.
    ///
    /// AN OPERATION, NOT AN APPROVAL, however much the word "close" suggests otherwise. It is the
    /// person working the lead recording that there is nothing more to do, which is a maker's act.
    /// <c>cam.campaigns.close</c> spells its verb the same way and IS an approval - it decides
    /// somebody else's close request - which is precisely why neither is classified by its verb.
    /// </summary>
    public const string LeadWorkQueueClose = "don.lead-work-queue.close";

    // ---- SCR-DON-002 Lead capture --------------------------------------------------------
    public const string LeadCaptureView = "don.lead-capture.view";
    public const string LeadCaptureSave = "don.lead-capture.save";
    public const string LeadCaptureDeduplicate = "don.lead-capture.deduplicate";
    public const string LeadCaptureSubmit = "don.lead-capture.submit";
    public const string LeadCaptureDeleteDraft = "don.lead-capture.delete-draft";

    // ---- SCR-DON-003 Donor 360 -----------------------------------------------------------
    public const string Donor360View = "don.donor-360.view";
    public const string Donor360Correct = "don.donor-360.correct";
    public const string Donor360FollowUp = "don.donor-360.follow-up";
    public const string Donor360CreateIntent = "don.donor-360.create-intent";
    public const string Donor360DeleteDraft = "don.donor-360.delete-draft";

    // ---- SCR-DON-004 Duplicate review ----------------------------------------------------
    public const string DuplicateReviewView = "don.duplicate-review.view";

    /// <summary>
    /// Joining two donor records into one.
    ///
    /// DESTRUCTIVE, AND DELIBERATELY NOT A POST-DECISION OPERATION. A merge takes the donations,
    /// receipts and consent history of two donors and joins them irreversibly, which is a
    /// destructive act however sound the reasoning behind it - so it stays out of
    /// <see cref="PostDecisionOperations"/> and away from APPROVER. IAM's
    /// <c>RoleAccessProfiles.PostApprovalOperations</c> calls this out in the same words, and the
    /// two must not drift.
    /// </summary>
    public const string DuplicateReviewMerge = "don.duplicate-review.merge";

    /// <summary>Refusing a match. A decision that ENDS a review, so a checker may take it.</summary>
    public const string DuplicateReviewRejectCandidate = "don.duplicate-review.reject-candidate";

    // ---- SCR-DON-005 Consent and preference centre ---------------------------------------
    public const string ConsentCentreView = "don.consent-and-preference-centre.view";
    public const string ConsentCentreGrant = "don.consent-and-preference-centre.grant";
    public const string ConsentCentreWithdraw = "don.consent-and-preference-centre.withdraw";
    public const string ConsentCentreCorrect = "don.consent-and-preference-centre.correct";

    // ---- SCR-DON-006 Assignment board ----------------------------------------------------
    public const string AssignmentBoardView = "don.assignment-board.view";
    public const string AssignmentBoardAssign = "don.assignment-board.assign";
    public const string AssignmentBoardReassign = "don.assignment-board.reassign";

    /// <summary>Bulk Assign: one owner applied to many selected leads in a single act.</summary>
    public const string AssignmentBoardBulkRoute = "don.assignment-board.bulk-route";

    // ---- DON-UI-07 Donor identity verification -------------------------------------------
    public const string VerificationView = "don.donor-identity-verification.view";
    public const string VerificationSendChallenge = "don.donor-identity-verification.send-challenge";
    public const string VerificationVerifyCode = "don.donor-identity-verification.verify-code";

    /// <summary>Sending an identity check for review. A decision, so a checker may take it.</summary>
    public const string VerificationEscalateReview = "don.donor-identity-verification.escalate-review";

    public const string VerificationCancel = "don.donor-identity-verification.cancel-verification";

    // ---- DON-UI-08 Follow-up planner, queue and execution ---------------------------------
    public const string FollowUpPlannerView = "don.follow-up-planner.view";
    public const string FollowUpPlannerSchedule = "don.follow-up-planner.schedule-follow-up";
    public const string FollowUpPlannerAssign = "don.follow-up-planner.assign";

    /// <summary>"Complete Follow-Up" on the Follow-Up Execution page.</summary>
    public const string FollowUpPlannerMarkComplete = "don.follow-up-planner.mark-complete";

    public const string FollowUpPlannerReschedule = "don.follow-up-planner.reschedule";
    public const string FollowUpPlannerCancelTask = "don.follow-up-planner.cancel-task";

    /// <summary>
    /// The Operate codes that describe what happens to a record AFTER a decision has been taken,
    /// and which an APPROVER therefore keeps.
    ///
    /// AN ALLOW-LIST, NOT A BLOCK-LIST, and deliberately so - the same choice IAM's
    /// <c>RoleAccessProfiles.PostApprovalOperations</c> makes, for the same reason. Operate is the
    /// catch-all bucket: it holds escalate-review, and it also holds merge, delete-draft, archive
    /// and cancel. A rule naming what to EXCLUDE would hand a checker every new destructive verb
    /// the day somebody added one. This names the few to keep, so anything new stays out until a
    /// person decides otherwise.
    ///
    /// NOTHING HERE CREATES OR DESTROYS. That is the test each entry has to pass, and it is why
    /// <see cref="DuplicateReviewMerge"/> is absent while
    /// <see cref="DuplicateReviewRejectCandidate"/> is present: refusing a match ends a review,
    /// and joining two donors destroys one of them.
    ///
    /// THESE TWO ENTRIES ARE THE ONES IAM ALREADY NAMES FOR DON. Adding a third here without
    /// adding it there gives APPROVER a code in DON's model that its token will never carry.
    /// </summary>
    public static readonly IReadOnlySet<string> PostDecisionOperations =
        new HashSet<string>(StringComparer.Ordinal)
        {
            DuplicateReviewRejectCandidate,
            VerificationEscalateReview
        };

    /// <summary>
    /// Every enforced code with the action it falls under, in the same order as the IAM catalogue.
    ///
    /// THIS IS THE LIST IAM MUST AGREE WITH. Each entry has a twin in
    /// <c>ModulePermissionCatalogue.Donors</c> carrying the same code and the same
    /// <c>PermissionAction</c>; IAM derives INITIATOR and APPROVER from its copy, so a disagreement
    /// here shows up as a role holding a code the module thinks it should not.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, PermissionAction Action)> Catalogue =
    [
        (DonView, PermissionAction.View),

        (DonorsView, PermissionAction.View),
        (DonorsCreate, PermissionAction.Create),
        (DonorsEdit, PermissionAction.Edit),
        (DonorsSubmit, PermissionAction.Submit),
        (DonorsApprove, PermissionAction.Approve),
        (DonorsCancel, PermissionAction.Operate),
        (DonorsArchive, PermissionAction.Operate),
        (DonorsExport, PermissionAction.Export),

        // UNMASKING IS A VIEW, not an Operate, so all three roles may hold it. What keeps donor
        // contact detail covered is that it is a SEPARATE code from don.donors.view - a grant
        // somebody has to make on purpose - not that a particular role is shut out of it.
        (DonorsViewSensitiveContact, PermissionAction.View),
        (DonorsViewConfidentialEvidence, PermissionAction.View),

        (LeadWorkQueueView, PermissionAction.View),
        (LeadWorkQueueAccept, PermissionAction.Operate),
        (LeadWorkQueueAssign, PermissionAction.Operate),
        (LeadWorkQueueContact, PermissionAction.Operate),
        (LeadWorkQueueQualify, PermissionAction.Operate),
        (LeadWorkQueueClose, PermissionAction.Operate),

        (LeadCaptureView, PermissionAction.View),

        // SAVE IS AN EDIT AND SUBMIT IS A SUBMIT, which is the split the capture screen turns on:
        // filling a draft in is editing, and handing it to the queue is what stops at the gate.
        (LeadCaptureSave, PermissionAction.Edit),
        (LeadCaptureDeduplicate, PermissionAction.Operate),
        (LeadCaptureSubmit, PermissionAction.Submit),
        (LeadCaptureDeleteDraft, PermissionAction.Operate),

        (Donor360View, PermissionAction.View),
        (Donor360Correct, PermissionAction.Edit),
        (Donor360FollowUp, PermissionAction.Operate),
        (Donor360CreateIntent, PermissionAction.Create),
        (Donor360DeleteDraft, PermissionAction.Operate),

        (DuplicateReviewView, PermissionAction.View),
        (DuplicateReviewMerge, PermissionAction.Operate),
        (DuplicateReviewRejectCandidate, PermissionAction.Operate),

        (ConsentCentreView, PermissionAction.View),

        // GRANTING AND WITHDRAWING CONSENT ARE OPERATIONS ON THE DONOR'S WISHES, not edits to a
        // field, and neither follows a decision - so both stay with the maker. Correcting a
        // mistyped consent record is an ordinary Edit, which is why the third one differs.
        (ConsentCentreGrant, PermissionAction.Operate),
        (ConsentCentreWithdraw, PermissionAction.Operate),
        (ConsentCentreCorrect, PermissionAction.Edit),

        (AssignmentBoardView, PermissionAction.View),
        (AssignmentBoardAssign, PermissionAction.Operate),
        (AssignmentBoardReassign, PermissionAction.Operate),
        (AssignmentBoardBulkRoute, PermissionAction.Operate),

        (VerificationView, PermissionAction.View),
        (VerificationSendChallenge, PermissionAction.Operate),
        (VerificationVerifyCode, PermissionAction.Operate),
        (VerificationEscalateReview, PermissionAction.Operate),
        (VerificationCancel, PermissionAction.Operate),

        (FollowUpPlannerView, PermissionAction.View),
        (FollowUpPlannerSchedule, PermissionAction.Operate),
        (FollowUpPlannerAssign, PermissionAction.Operate),
        (FollowUpPlannerMarkComplete, PermissionAction.Operate),
        (FollowUpPlannerReschedule, PermissionAction.Operate),
        (FollowUpPlannerCancelTask, PermissionAction.Operate)
    ];

    /// <summary>Codes whose use always writes an enhanced audit row. Mirrors the IAM sensitive set.</summary>
    public static readonly IReadOnlyList<string> Sensitive =
    [
        DonorsCreate, DonorsApprove, DonorsCancel, DonorsArchive, DonorsExport,
        DonorsViewSensitiveContact, DonorsViewConfidentialEvidence,
        LeadWorkQueueClose, LeadCaptureDeleteDraft,
        Donor360Correct, Donor360DeleteDraft,
        DuplicateReviewMerge, DuplicateReviewRejectCandidate,
        ConsentCentreGrant, ConsentCentreWithdraw, ConsentCentreCorrect,
        AssignmentBoardBulkRoute,
        VerificationSendChallenge, VerificationVerifyCode, VerificationEscalateReview, VerificationCancel,
        FollowUpPlannerCancelTask
    ];

    /// <summary>
    /// Every code the Donors section owns. Derived from <see cref="Catalogue"/> rather than typed
    /// out a second time, so a code can no longer exist in one list and be missing from the other.
    /// </summary>
    public static readonly IReadOnlyList<string> All = [.. Catalogue.Select(entry => entry.Code)];

    public static bool IsSensitive(string permissionCode) =>
        Sensitive.Contains(permissionCode, StringComparer.Ordinal);

    /// <summary>The action a code falls under, or null when the code is not one DON enforces.</summary>
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
    /// An unknown code answers with NOBODY rather than throwing, which is the safe direction: a
    /// code DON does not enforce is a code nothing should be drawn for.
    /// </summary>
    public static IReadOnlyList<string> RolesFor(string permissionCode)
    {
        var action = ActionFor(permissionCode);

        return action is null
            ? []
            : RoleCodes.HoldersOf(action.Value, PostDecisionOperations.Contains(permissionCode));
    }
}

namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// Every permission code used by Section 04 — Donors.
///
/// THESE STRINGS ARE A CONTRACT WITH IAM. IAM is the only service that issues tokens, so it is
/// the only service that can put a permission claim into one. The same 49 codes are listed in
/// YDots.IAM.Application/Common/Constants/ModulePermissionCatalogue.cs, and IAM seeds them and
/// hands them out through the DON roles. If a string here stops matching the string there, the
/// endpoint becomes unreachable: the token will simply never carry the claim this file asks for.
///
/// TO ADD A PERMISSION: add the constant here, add it to <see cref="All"/>, then add the same
/// literal to ModulePermissionCatalogue in IAM and attach it to the roles that should carry it.
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
    public const string DuplicateReviewMerge = "don.duplicate-review.merge";
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
    public const string AssignmentBoardBulkRoute = "don.assignment-board.bulk-route";

    // ---- DON-UI-07 Donor identity verification -------------------------------------------
    public const string VerificationView = "don.donor-identity-verification.view";
    public const string VerificationSendChallenge = "don.donor-identity-verification.send-challenge";
    public const string VerificationVerifyCode = "don.donor-identity-verification.verify-code";
    public const string VerificationEscalateReview = "don.donor-identity-verification.escalate-review";
    public const string VerificationCancel = "don.donor-identity-verification.cancel-verification";

    // ---- DON-UI-08 Follow-up planner ------------------------------------------------------
    public const string FollowUpPlannerView = "don.follow-up-planner.view";
    public const string FollowUpPlannerSchedule = "don.follow-up-planner.schedule-follow-up";
    public const string FollowUpPlannerAssign = "don.follow-up-planner.assign";
    public const string FollowUpPlannerMarkComplete = "don.follow-up-planner.mark-complete";
    public const string FollowUpPlannerReschedule = "don.follow-up-planner.reschedule";
    public const string FollowUpPlannerCancelTask = "don.follow-up-planner.cancel-task";

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

    /// <summary>Every code owned by the Donors section, in the same order as the IAM catalogue.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        DonView,
        DonorsView, DonorsCreate, DonorsEdit, DonorsSubmit, DonorsApprove, DonorsCancel, DonorsArchive,
        DonorsExport, DonorsViewSensitiveContact, DonorsViewConfidentialEvidence,
        LeadWorkQueueView, LeadWorkQueueAccept, LeadWorkQueueAssign, LeadWorkQueueContact,
        LeadWorkQueueQualify, LeadWorkQueueClose,
        LeadCaptureView, LeadCaptureSave, LeadCaptureDeduplicate, LeadCaptureSubmit, LeadCaptureDeleteDraft,
        Donor360View, Donor360Correct, Donor360FollowUp, Donor360CreateIntent, Donor360DeleteDraft,
        DuplicateReviewView, DuplicateReviewMerge, DuplicateReviewRejectCandidate,
        ConsentCentreView, ConsentCentreGrant, ConsentCentreWithdraw, ConsentCentreCorrect,
        AssignmentBoardView, AssignmentBoardAssign, AssignmentBoardReassign, AssignmentBoardBulkRoute,
        VerificationView, VerificationSendChallenge, VerificationVerifyCode, VerificationEscalateReview,
        VerificationCancel,
        FollowUpPlannerView, FollowUpPlannerSchedule, FollowUpPlannerAssign, FollowUpPlannerMarkComplete,
        FollowUpPlannerReschedule, FollowUpPlannerCancelTask
    ];

    public static bool IsSensitive(string permissionCode) =>
        Sensitive.Contains(permissionCode, StringComparer.Ordinal);
}

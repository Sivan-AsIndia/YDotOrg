namespace YDots.DON.Application.Common.Constants;

/// <summary>
/// Stable dotted action codes written to the audit trail. Section 10 of the contract requires
/// create, edit, submit, approve, reject, cancel, archive, export and sensitive view to be
/// recorded, so there is one code for each of them here.
/// </summary>
public static class AuditActionCodes
{
    // ---- Donor resource -------------------------------------------------------------------
    public const string DonorCreated = "don.donor.create";
    public const string DonorUpdated = "don.donor.update";
    public const string DonorSubmitted = "don.donor.submit";
    public const string DonorApproved = "don.donor.approve";
    public const string DonorRejected = "don.donor.reject";
    public const string DonorCancelled = "don.donor.cancel";
    public const string DonorArchived = "don.donor.archive";
    public const string DonorExported = "don.donor.export";
    public const string DonorSensitiveViewed = "don.donor.view-sensitive";
    public const string DonorCorrected = "don.donor-360.correct";
    public const string DonorIntentCreated = "don.donor-360.create-intent";
    public const string DonorDraftDeleted = "don.donor-360.delete-draft";

    // ---- Lead work queue and lead capture -------------------------------------------------
    public const string LeadCreated = "don.lead.create";
    public const string LeadUpdated = "don.lead.update";
    public const string LeadSubmitted = "don.lead.submit";
    public const string LeadAccepted = "don.lead.accept";
    public const string LeadAssigned = "don.lead.assign";
    public const string LeadContacted = "don.lead.contact";
    public const string LeadQualified = "don.lead.qualify";
    public const string LeadClosed = "don.lead.close";
    public const string LeadDeduplicated = "don.lead.deduplicate";
    public const string LeadDraftDeleted = "don.lead.delete-draft";
    public const string LeadConverted = "don.lead.convert";

    // ---- Duplicate review ------------------------------------------------------------------
    public const string MergeCaseCreated = "don.duplicate-review.create";
    public const string MergeCaseMerged = "don.duplicate-review.merge";
    public const string MergeCaseRejected = "don.duplicate-review.reject-candidate";

    // ---- Consent and preference centre -------------------------------------------------------
    public const string ConsentGranted = "don.consent.grant";
    public const string ConsentWithdrawn = "don.consent.withdraw";
    public const string ConsentCorrected = "don.consent.correct";
    public const string ConsentEvidenceViewed = "don.consent.view-evidence";

    // ---- Assignment board ---------------------------------------------------------------------
    public const string AssignmentAssigned = "don.assignment-board.assign";
    public const string AssignmentReassigned = "don.assignment-board.reassign";
    public const string AssignmentBulkRouted = "don.assignment-board.bulk-route";

    // ---- Identity verification --------------------------------------------------------------------
    public const string VerificationChallengeSent = "don.verification.send-challenge";
    public const string VerificationCodeVerified = "don.verification.verify-code";
    public const string VerificationEscalated = "don.verification.escalate-review";
    public const string VerificationCancelled = "don.verification.cancel";

    // ---- Follow-up planner ------------------------------------------------------------------------
    public const string FollowUpScheduled = "don.follow-up.schedule";
    public const string FollowUpAssigned = "don.follow-up.assign";
    public const string FollowUpCompleted = "don.follow-up.mark-complete";
    public const string FollowUpRescheduled = "don.follow-up.reschedule";
    public const string FollowUpCancelled = "don.follow-up.cancel-task";
}

namespace YDot.IAM.Application.Common.Constants;

/// <summary>
/// Stable dotted codes written into <c>AuditEvent.ActionCode</c>.
///
/// Codes are how the audit screen filters and how a compliance report groups, so they are
/// append-only in exactly the same way permission codes are. A renamed code orphans every
/// historical row that used it.
/// </summary>
public static class AuditActionCodes
{
    // ---- Authentication ---------------------------------------------------------------
    public const string SignInSucceeded = "iam.auth.sign-in.succeeded";
    public const string SignInFailed = "iam.auth.sign-in.failed";
    public const string SignInLockedOut = "iam.auth.sign-in.locked-out";
    public const string SignOut = "iam.auth.sign-out";
    public const string SignOutEverywhere = "iam.auth.sign-out-everywhere";
    public const string TokenRefreshed = "iam.auth.token.refreshed";
    public const string TokenReuseDetected = "iam.auth.token.reuse-detected";
    public const string TokenRevoked = "iam.auth.token.revoked";
    public const string Reauthenticated = "iam.auth.reauthenticated";
    public const string MfaChallengeIssued = "iam.auth.mfa.challenge-issued";

    public const string MfaChallengeCancelled = "iam.auth.mfa.cancelled";

    /// <summary>Somebody who could not sign in sent a message to the service desk.</summary>
    public const string SupportRequested = "iam.auth.support.requested";

    // ---- Organisation structure -------------------------------------------------------------
    // ---- Governance handovers ---------------------------------------------------------------
    public const string AccessRequestReturned = "iam.access-request.returned";
    public const string AccessReviewDelegated = "iam.access-review.delegated";
    public const string AccessReviewEscalated = "iam.access-review.escalated";

    public const string DepartmentCreated = "iam.organisation.department.created";
    public const string DepartmentUpdated = "iam.organisation.department.updated";
    public const string DepartmentDeleted = "iam.organisation.department.deleted";
    public const string OrganisationUnitCreated = "iam.organisation.unit.created";
    public const string OrganisationUnitUpdated = "iam.organisation.unit.updated";
    public const string OrganisationUnitDeleted = "iam.organisation.unit.deleted";
    public const string MfaVerified = "iam.auth.mfa.verified";
    public const string MfaFailed = "iam.auth.mfa.failed";
    public const string MfaEnrolled = "iam.auth.mfa.enrolled";
    public const string MfaRevoked = "iam.auth.mfa.revoked";
    public const string MfaReset = "iam.auth.mfa.reset";
    public const string RecoveryCodesGenerated = "iam.auth.recovery-codes.generated";
    public const string RecoveryCodeRedeemed = "iam.auth.recovery-code.redeemed";
    public const string PasswordResetRequested = "iam.auth.password.reset-requested";
    public const string PasswordResetCompleted = "iam.auth.password.reset-completed";
    public const string PasswordChanged = "iam.auth.password.changed";
    public const string EmailConfirmed = "iam.auth.email.confirmed";
    public const string DeviceTrusted = "iam.auth.device.trusted";
    public const string DeviceRevoked = "iam.auth.device.revoked";
    public const string SessionRevoked = "iam.auth.session.revoked";

    // ---- Tenant selection -----------------------------------------------------------------
    public const string TenantSelected = "iam.auth.tenant.selected";
    public const string TenantSwitched = "iam.auth.tenant.switched";

    // ---- Users -----------------------------------------------------------------------------
    public const string UserCreated = "iam.user.created";
    public const string UserUpdated = "iam.user.updated";
    public const string UserInvited = "iam.user.invited";
    public const string UserInvitationResent = "iam.user.invitation.resent";

    /// <summary>The recipient asked for a replacement link from the activation screen.</summary>
    public const string InvitationResent = "iam.user.invitation.resent-by-recipient";

    /// <summary>The recipient opened the activation screen and left without finishing.</summary>
    public const string InvitationActivationAbandoned = "iam.user.invitation.activation-abandoned";
    public const string UserInvitationRevoked = "iam.user.invitation.revoked";
    public const string UserActivated = "iam.user.activated";
    public const string UserSubmitted = "iam.user.submitted";
    public const string UserApproved = "iam.user.approved";
    public const string UserSuspended = "iam.user.suspend";
    public const string UserReactivated = "iam.user.reactivated";
    public const string UserDeactivated = "iam.user.deactivated";
    public const string UserArchived = "iam.user.archived";
    public const string UserCancelled = "iam.user.cancelled";
    public const string UserUnlocked = "iam.user.unlocked";
    public const string UserPasswordResetByAdmin = "iam.user.password.reset-by-admin";
    public const string UserLoginIdentifierChanged = "iam.user.login-identifier.changed";
    public const string UserBulkOperation = "iam.user.bulk-operation";
    public const string UserExported = "iam.user.exported";

    // ---- Roles and permissions -------------------------------------------------------------
    public const string RoleCreated = "iam.role.created";
    public const string RoleUpdated = "iam.role.updated";
    public const string RoleDeleted = "iam.role.deleted";
    public const string RoleActivated = "iam.role.activated";
    public const string RoleDeactivated = "iam.role.deactivated";
    public const string RolePermissionsChanged = "iam.role.permissions.changed";
    public const string UserRoleAssigned = "iam.user-role.assigned";
    public const string UserRoleRevoked = "iam.user-role.revoked";
    public const string DataScopeGranted = "iam.data-scope.granted";
    public const string DataScopeRevoked = "iam.data-scope.revoked";

    // ---- Menu -------------------------------------------------------------------------------
    public const string MenuConfigured = "iam.menu.configured";
    public const string MenuRoleMapped = "iam.menu.role-mapped";

    // ---- Access governance --------------------------------------------------------------------
    public const string AccessRequestCreated = "iam.access-request.created";
    public const string AccessRequestSubmitted = "iam.access-request.submitted";
    public const string AccessRequestApproved = "iam.access-request.approved";
    public const string AccessRequestRejected = "iam.access-request.rejected";
    public const string AccessRequestWithdrawn = "iam.access-request.withdrawn";
    public const string AccessReviewCreated = "iam.access-review.created";
    public const string AccessReviewDecided = "iam.access-review.decided";
    public const string AccessReviewCancelled = "iam.access-review.cancelled";

    // ---- Platform: BusinessUnit and Organisation -------------------------------------------------
    public const string BusinessUnitCreated = "platform.business-unit.created";
    public const string BusinessUnitUpdated = "platform.business-unit.updated";
    public const string TenantCreated = "platform.organisation.created";
    public const string TenantUpdated = "platform.organisation.updated";
    public const string TenantAdminInvited = "platform.organisation.admin-invited";
    public const string TenantProfileSubmitted = "platform.organisation.profile-submitted";
    public const string TenantReviewStarted = "platform.organisation.review-started";
    public const string TenantApproved = "platform.organisation.approved";
    public const string TenantRejected = "platform.organisation.rejected";
    public const string TenantResubmitted = "platform.organisation.resubmitted";
    public const string TenantActivated = "platform.organisation.activated";
    public const string TenantSuspended = "platform.organisation.suspended";
    public const string TenantArchived = "platform.organisation.archived";
    public const string TenantDomainAdded = "platform.organisation.domain-added";
    public const string TenantDomainVerified = "platform.organisation.domain-verified";
    public const string TenantDocumentUploaded = "platform.organisation.document-uploaded";
    public const string TenantDocumentReviewed = "platform.organisation.document-reviewed";

    // ---- Grouped document submissions -------------------------------------------------------
    //
    // A submission is what a reviewer decides on, so it is what the audit trail records. The
    // per-file codes above still fire for the individual uploads inside one, which is what lets
    // a compliance question like "who attached this scan, and when?" be answered separately
    // from "who approved the certificate?".
    public const string DocumentSubmissionCreated = "platform.organisation.document-submission-created";
    public const string DocumentSubmissionSubmitted = "platform.organisation.document-submission-submitted";
    public const string DocumentSubmissionReviewStarted = "platform.organisation.document-submission-review-started";
    public const string DocumentSubmissionApproved = "platform.organisation.document-submission-approved";
    public const string DocumentSubmissionRejected = "platform.organisation.document-submission-rejected";
    public const string DocumentSubmissionReuploadRequested = "platform.organisation.document-submission-reupload-requested";
    public const string DocumentSubmissionFileRemoved = "platform.organisation.document-submission-file-removed";

    /// <summary>An unstarted draft submission was withdrawn by the organisation that opened it.</summary>
    public const string DocumentSubmissionDiscarded = "platform.organisation.document-submission-discarded";
    public const string TenantDocumentDownloaded = "platform.organisation.document-downloaded";

    // ---- Global masters, migrated in from the standalone GlobalMaster service ---------------------
    //
    // ONE PAIR OF CODES FOR ALL FIVE MASTERS RATHER THAN FIVE PAIRS. The audit row already
    // carries TargetType - "Country", "Currency" - so a per-entity code would encode the same
    // fact twice and give the audit screen two different things to filter on for one question.
    // "Show me every master change last month" is one predicate this way and five with the
    // alternative.
    public const string GlobalMasterCreated = "gm.master.created";
    public const string GlobalMasterUpdated = "gm.master.updated";
    public const string GlobalMasterActivated = "gm.master.activated";
    public const string GlobalMasterDeactivated = "gm.master.deactivated";
    public const string GlobalMasterDeleted = "gm.master.deleted";
    public const string GlobalMasterExported = "gm.master.exported";

    // ---- Audit trail itself ---------------------------------------------------------------------
    /// <summary>Somebody read the trail. Recorded because who looked, and when, is itself evidence.</summary>
    public const string AuditViewed = "iam.audit.viewed";

    /// <summary>Somebody took a copy of the trail. The event a later investigation looks for.</summary>
    public const string AuditExported = "iam.audit.exported";

    // ---- Cross-tenant alarm -----------------------------------------------------------------------
    /// <summary>
    /// Written when somebody tries to reach another Organisation data. The query filter makes
    /// this all but unreachable, so a row here is worth investigating rather than ignoring.
    /// </summary>
    public const string CrossTenantAccessAttempt = "iam.security.cross-tenant-attempt";
}

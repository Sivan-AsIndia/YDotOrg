using YDot.IAM.Domain.Enums;

namespace YDot.IAM.Application.Features.Users.DTOs;

// =====================================================================================
// Create and edit — IAM-USR-01, IAM-USR-02
// =====================================================================================

/// <summary>
/// Creating a user inside the caller Organisation.
///
/// THERE IS NO TenantId FIELD, and there must not be. The Organisation comes from the token,
/// so a TenantAdmin cannot create a user in somebody else Organisation by editing the body,
/// and SuperAdmin creates into whichever Organisation they have selected. That is section 47
/// of the brief applied to the most obvious place somebody would try it.
/// </summary>
public sealed record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? MiddleName = null,
    string? DisplayName = null,
    string? Username = null,
    string? EmployeeNumber = null,
    string? MobileCountryCode = null,
    string? MobileNumber = null,
    UserAccountCategory AccountCategory = UserAccountCategory.Employee,
    EngagementType EngagementType = EngagementType.FullTime,
    Guid? DepartmentId = null,
    Guid? OrganisationUnitId = null,
    string? Designation = null,
    Guid? ManagerUserId = null,
    DateTimeOffset? AccessStartsAtUtc = null,
    DateTimeOffset? AccessEndsAtUtc = null,
    MfaRequirement MfaRequirement = MfaRequirement.Inherited,
    DateTimeOffset? JoinedOn = null,

    /// <summary>Roles granted on creation. Empty falls back to the Organisation default role.</summary>
    IReadOnlyList<Guid>? RoleIds = null,

    /// <summary>Narrowing scopes granted on creation.</summary>
    IReadOnlyList<CreateUserDataScopeRequest>? DataScopes = null,

    /// <summary>
    /// Sends the activation link. When false the account is created in Draft with no
    /// credentials and no way in, which is what a staged import wants.
    /// </summary>
    bool SendInvitation = true,

    CredentialSetupMethod CredentialSetupMethod = CredentialSetupMethod.InvitationLink,
    string? InvitationMessage = null);

/// <summary>
/// Checks whether an e-mail address or username is free, before the create form is submitted.
///
/// THE ORGANISATION IS NOT A PARAMETER. The check runs inside whichever Organisation the caller
/// token names, which is exactly the scope the uniqueness rule uses: the same address may exist
/// in another Organisation, and that is not a clash. Accepting an Organisation here would let the
/// endpoint be aimed at somebody else's Organisation to answer a question about their people.
/// </summary>
public sealed record CheckUserIdentityRequest(
    string? Email = null,
    string? Username = null,

    /// <summary>Set when editing, so a record does not report a clash with itself.</summary>
    Guid? ExcludeUserId = null);

/// <summary>
/// Whether the address or username may be used.
///
/// It never names the holder of a taken value. "Already in use" is the whole answer, because
/// saying who has it would turn the create form into a directory lookup for anybody who can
/// reach it.
/// </summary>
public sealed record CheckUserIdentityResponse(
    bool IsAvailable,
    bool EmailAvailable,
    bool UsernameAvailable,
    string Message,

    /// <summary>Alternatives when a username is taken, so the form can offer a way forward.</summary>
    IReadOnlyList<string> Suggestions);

/// <summary>One narrowing scope granted at creation.</summary>
public sealed record CreateUserDataScopeRequest(
    DataScopeType ScopeType,
    string ScopeValue,
    string? DisplayLabel = null,
    DateTimeOffset? EffectiveToUtc = null);

/// <summary>
/// Editing a user.
///
/// <c>ExpectedVersion</c> is mandatory: two administrators on the same record must not
/// silently overwrite one another, and the loser is told to reload rather than discovering it
/// at the next audit.
/// </summary>
public sealed record UpdateUserRequest(
    long ExpectedVersion,
    string? FirstName = null,
    string? MiddleName = null,
    string? LastName = null,
    string? DisplayName = null,
    string? EmployeeNumber = null,
    string? MobileCountryCode = null,
    string? MobileNumber = null,
    UserAccountCategory? AccountCategory = null,
    EngagementType? EngagementType = null,
    Guid? DepartmentId = null,
    Guid? OrganisationUnitId = null,
    string? Designation = null,
    Guid? ManagerUserId = null,
    DateTimeOffset? AccessStartsAtUtc = null,
    DateTimeOffset? AccessEndsAtUtc = null,
    MfaRequirement? MfaRequirement = null,
    DateTimeOffset? JoinedOn = null,
    DateTimeOffset? ExitedOn = null,
    string? PreferredCulture = null,
    string? TimeZone = null,
    string? AvatarUrl = null,

    /// <summary>
    /// Why the change is being made.
    ///
    /// Optional, and recorded on the audit row when supplied. The audit trail captures WHAT
    /// changed regardless; this captures the part no system can infer. "Corrected a misspelt
    /// surname" and "changed at the person's request after marriage" look identical in a diff,
    /// and the difference is exactly what somebody reviewing it six months later needs.
    /// </summary>
    string? Reason = null);

// =====================================================================================
// Lifecycle
// =====================================================================================

/// <summary>Suspending, deactivating or withdrawing. A reason is always required.</summary>
public sealed record UserLifecycleRequest(string Reason, long ExpectedVersion);

/// <summary>Reactivating a suspended account.</summary>
public sealed record ReactivateUserRequest(long ExpectedVersion, string? Notes = null);

/// <summary>
/// An administrator resetting somebody password.
///
/// The link is by far the better route: a temporary password has to be communicated out of
/// band, and in practice it gets sent over the same channel it was meant to protect.
/// </summary>
public sealed record AdminResetPasswordRequest(
    long ExpectedVersion,
    bool SendResetLink = true,
    string? TemporaryPassword = null,
    bool RequireChangeOnNextSignIn = true,
    bool SignOutAllSessions = true);

/// <summary>Clearing a lockout by hand, rather than waiting it out.</summary>
public sealed record UnlockUserRequest(long ExpectedVersion, string? Reason = null);

/// <summary>Extending or shortening an access window.</summary>
public sealed record ExtendUserAccessRequest(
    DateTimeOffset? AccessEndsAtUtc,
    long ExpectedVersion,
    string? Reason = null);

// =====================================================================================
// Role and scope assignment
// =====================================================================================

/// <summary>
/// Replacing a user role set.
///
/// The WHOLE set is sent, not a delta. A delta of "add these, remove those" computed against a
/// stale screen is how somebody quietly loses a role nobody meant to touch; sending the
/// intended end state makes the outcome unambiguous.
/// </summary>
public sealed record AssignUserRolesRequest(
    IReadOnlyList<Guid> RoleIds,
    long ExpectedVersion,
    Guid? PrimaryRoleId = null,
    string? Justification = null,
    DateTimeOffset? EffectiveToUtc = null);

/// <summary>Replacing a user narrowing data scopes.</summary>
public sealed record AssignUserDataScopesRequest(
    IReadOnlyList<CreateUserDataScopeRequest> DataScopes,
    long ExpectedVersion,
    string? Justification = null);

/// <summary>
/// Asking what a role change WOULD do, before committing.
///
/// Adding one role to somebody who already holds three is not obviously safe: it may overlap
/// entirely, or quietly hand over an export permission nobody intended.
/// </summary>
public sealed record PreviewUserAccessRequest(IReadOnlyList<Guid> RoleIds);

// =====================================================================================
// Login identifier change — IAM-USR-05
// =====================================================================================

/// <summary>Requesting a change to the e-mail or username somebody signs in with.</summary>
public sealed record RequestLoginIdentifierChangeRequest(
    bool IsEmailChange,
    string RequestedValue,
    string? Reason = null);

/// <summary>Proving control of the new address with the code sent to it.</summary>
public sealed record VerifyLoginIdentifierChangeRequest(Guid RequestId, string Code);

/// <summary>A second person approving the change, on a privileged account.</summary>
public sealed record DecideLoginIdentifierChangeRequest(
    Guid RequestId,
    bool Approved,
    string? Reason = null);

// =====================================================================================
// Bulk — IAM-USR-06
// =====================================================================================

/// <summary>Starting a bulk job. Validation runs first; nothing is written until it passes.</summary>
public sealed record CreateBulkOperationRequest(
    BulkActionType ActionType,
    IReadOnlyList<Guid>? UserIds = null,
    string? SourceFileName = null,
    string? SourceStoragePath = null,
    Guid? RoleId = null,
    DateTimeOffset? AccessEndsAtUtc = null,
    string? Reason = null,

    /// <summary>Applies immediately after validation, rather than waiting for a second call.</summary>
    bool ApplyImmediately = false);

/// <summary>Applying a validated job.</summary>
public sealed record ApplyBulkOperationRequest(Guid OperationId, long ExpectedVersion);

// =====================================================================================
// Responses
// =====================================================================================

/// <summary>One row of the user directory. Compact by design.</summary>
public sealed record UserListItemResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string Email,
    string Username,
    UserStatus Status,
    string StatusDisplay,
    UserAccountCategory AccountCategory,
    string? DepartmentName,
    string? OrganisationUnitName,
    string? Designation,
    IReadOnlyList<string> RoleNames,
    bool MfaEnabled,
    bool IsLockedOut,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? AccessEndsAtUtc,
    string? AvatarUrl,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>
/// The full user record.
///
/// <c>EmailMasked</c> and <c>MobileMasked</c> say whether the values above them were redacted,
/// so the screen can show a "reveal" affordance to somebody who holds the sensitive-contact
/// permission instead of silently displaying asterisks as if they were the real value.
/// </summary>
public sealed record UserDetailResponse(
    Guid Id,
    Guid? TenantId,
    Guid BusinessUnitId,
    string Code,
    string? EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    string DisplayName,
    string Email,
    bool EmailConfirmed,
    DateTimeOffset? EmailConfirmedAtUtc,
    string Username,
    string? MobileCountryCode,
    string? MobileNumber,
    bool MobileConfirmed,
    UserAccountCategory AccountCategory,
    EngagementType EngagementType,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? OrganisationUnitId,
    string? OrganisationUnitName,
    string? Designation,
    Guid? ManagerUserId,
    string? ManagerName,
    UserStatus Status,
    string StatusDisplay,
    DateTimeOffset AccessStartsAtUtc,
    DateTimeOffset? AccessEndsAtUtc,
    MfaRequirement MfaRequirement,
    bool MfaEnabled,
    PrivilegeLevel PrivilegeLevel,
    bool IsSuperAdmin,
    bool IsTenantAdmin,
    bool IsSystemAccount,
    bool MustChangePassword,
    bool IsLockedOut,
    DateTimeOffset? LockoutEndUtc,
    string? LockoutReason,
    int AccessFailedCount,
    DateTimeOffset? LastLoginAtUtc,
    string? LastLoginIpAddress,
    ClientType LastLoginClientType,
    string? LastLoginBrowser,
    string? LastLoginOperatingSystem,
    DateTimeOffset? JoinedOn,
    DateTimeOffset? ExitedOn,
    string? PreferredCulture,
    string? TimeZone,
    string? AvatarUrl,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version,
    IReadOnlyList<UserRoleAssignmentResponse> Roles,
    IReadOnlyList<UserDataScopeResponse> DataScopes,
    bool EmailMasked,
    bool MobileMasked,
    bool HasPendingInvitation,
    DateTimeOffset? InvitationExpiresAtUtc,
    IReadOnlyList<string> PermittedActions);

/// <summary>One option in a user picker.</summary>
public sealed record UserLookupResponse(Guid Id, string Code, string DisplayName, string Email, UserStatus Status);

/// <summary>
/// One colleague, for a picker.
///
/// DELIBERATELY THINNER THAN <see cref="UserLookupResponse"/>: no e-mail address and no account
/// status. Naming somebody as a campaign owner or a lead owner needs to know who they are, not
/// how to contact them or whether they are suspended - and this is readable by every member of
/// the Organisation rather than by user administrators alone, so the less it carries the better.
/// </summary>
public sealed record PersonLookupResponse(Guid Id, string DisplayName, string? Code);

/// <summary>One role assignment, with why it counts and when it stops.</summary>
public sealed record UserRoleAssignmentResponse(
    Guid Id,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    UserRoleAssignmentStatus Status,
    bool IsPrimary,
    bool IsEffective,
    DateTimeOffset AssignedAtUtc,
    string? AssignedByName,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string? Justification,
    int PermissionCount);

/// <summary>One narrowing scope.</summary>
public sealed record UserDataScopeResponse(
    Guid Id,
    DataScopeType ScopeType,
    string ScopeValue,
    string? DisplayLabel,
    bool IsEffective,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc);

/// <summary>
/// IAM-USR-03. Everything the person can actually do, resolved from every source at once, so
/// an administrator can answer "what can they see?" from one screen.
/// </summary>
public sealed record UserAccessPreviewResponse(
    Guid UserId,
    string DisplayName,
    bool IsSuperAdmin,
    bool HasAllTenantPermissions,
    IReadOnlyList<UserRoleAssignmentResponse> Roles,
    IReadOnlyList<PermissionGroupResponse> PermissionGroups,
    IReadOnlyList<UserDataScopeResponse> DataScopes,
    IReadOnlyList<string> DirectClaims,
    int TotalPermissionCount,
    int SensitivePermissionCount);

/// <summary>Permissions grouped by module, so the preview is readable rather than a flat list of eighty.</summary>
public sealed record PermissionGroupResponse(
    string ModuleCode,
    string? GroupCode,
    IReadOnlyList<PermissionSummaryResponse> Permissions);

/// <summary>One permission in the preview.</summary>
public sealed record PermissionSummaryResponse(
    string Code,
    string Name,
    PermissionAction Action,
    bool IsSensitive,
    bool IsGranted,

    /// <summary>Which role granted it, or "Direct" for a user claim. Answers "why do they have this?".</summary>
    string? GrantedVia);

/// <summary>IAM-USR-04. Sessions, devices, MFA and recent sign-in history.</summary>
public sealed record UserSecurityResponse(
    Guid UserId,
    string DisplayName,
    bool MfaEnabled,
    MfaRequirement MfaRequirement,
    bool IsMfaEffectivelyRequired,
    DateTimeOffset? MfaEnrolledAtUtc,
    int RecoveryCodesRemaining,
    bool IsLockedOut,
    DateTimeOffset? LockoutEndUtc,
    string? LockoutReason,
    int AccessFailedCount,
    int AttemptsRemaining,
    DateTimeOffset? PasswordChangedAtUtc,
    bool MustChangePassword,
    IReadOnlyList<UserSessionResponse> ActiveSessions,
    IReadOnlyList<TrustedDeviceResponse> TrustedDevices,
    IReadOnlyList<MfaMethodResponse> MfaMethods,
    IReadOnlyList<SignInAttemptResponse> RecentAttempts);

/// <summary>One live session.</summary>
public sealed record UserSessionResponse(
    Guid Id,
    string? DeviceName,
    ClientType ClientType,
    string? Browser,
    string? OperatingSystem,
    string? IpAddress,
    string? Location,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool MfaCompleted,
    bool IsTrustedDevice,
    bool IsCurrent,
    Guid? OperatingTenantId,
    string? OperatingTenantName);

/// <summary>One remembered device.</summary>
public sealed record TrustedDeviceResponse(
    Guid Id,
    string? DeviceName,
    ClientType ClientType,
    string? Browser,
    string? OperatingSystem,
    string? IpAddress,
    string? Location,
    DateTimeOffset TrustedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    bool IsExpired);

/// <summary>One enrolled second factor. The destination is always masked.</summary>
public sealed record MfaMethodResponse(
    Guid Id,
    MfaMethodType MethodType,
    string? Label,
    string? MaskedDestination,
    bool IsPrimary,
    MfaMethodStatus Status,
    DateTimeOffset? VerifiedAtUtc,
    DateTimeOffset? LastUsedAtUtc);

/// <summary>One sign-in attempt, for the "recent activity" list.</summary>
public sealed record SignInAttemptResponse(
    Guid Id,
    SignInOutcome Outcome,
    string OutcomeDisplay,
    bool Succeeded,
    DateTimeOffset AttemptedAtUtc,
    string? IpAddress,
    ClientType ClientType,
    string? Browser,
    string? OperatingSystem,
    string? Location,
    int AttemptsRemaining,
    bool TriggeredLockout);

/// <summary>Counts for the directory summary tiles.</summary>
public sealed record UserStatisticsResponse(
    int Total,
    int Active,
    int Invited,
    int Suspended,
    int Deactivated,
    int LockedOut,
    int MfaEnabled,
    int NeverSignedIn,
    int AccessExpiringSoon,
    IReadOnlyDictionary<string, int> ByStatus);

/// <summary>One row of a CSV export. Already scoped and masked by the read service.</summary>
public sealed record UserExportRow(
    string Code,
    string DisplayName,
    string Email,
    string Username,
    string Status,
    string AccountCategory,
    string? Department,
    string? OrganisationUnit,
    string? Designation,
    string? Manager,
    string Roles,
    string MfaEnabled,
    string? LastLoginAtUtc,
    string? AccessStartsAtUtc,
    string? AccessEndsAtUtc);

/// <summary>The result of creating a user, including what happened to the invitation.</summary>
public sealed record CreateUserResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string Email,
    UserStatus Status,
    bool InvitationSent,
    DateTimeOffset? InvitationExpiresAtUtc,

    /// <summary>Present only when the mail relay is off, so a developer can still walk the flow.</summary>
    string? ActivationUrl,

    long Version);

/// <summary>What a proposed role change would gain and lose.</summary>
public sealed record UserAccessComparisonResponse(
    IReadOnlyList<string> Gained,
    IReadOnlyList<string> Lost,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> SensitiveGained,
    bool RequiresJustification,
    IReadOnlyList<string> SegregationOfDutiesConflicts);

// IAM-USR-05 responses live in Features/Governance/DTOs/GovernanceDtos.cs.
// The screen belongs to the Users area, but the request is an approval workflow with the
// same shape as the other governance items, so one definition serves both rather than two
// that would drift.

/// <summary>One row of the bulk job list.</summary>
public sealed record BulkOperationListItemResponse(
    Guid Id,
    string OperationNumber,
    BulkActionType ActionType,
    string ActionDisplay,
    BulkOperationStatus Status,
    string StatusDisplay,
    int TotalItemCount,
    int ProcessedItemCount,
    int SucceededItemCount,
    int FailedItemCount,
    int PercentComplete,
    DateTimeOffset CreatedAtUtc,
    string? RequestedByName,
    DateTimeOffset? CompletedAtUtc,
    long Version);

/// <summary>A bulk job with its per-row outcomes.</summary>
public sealed record BulkOperationDetailResponse(
    Guid Id,
    string OperationNumber,
    BulkActionType ActionType,
    string ActionDisplay,
    BulkOperationStatus Status,
    string StatusDisplay,
    string? SourceFileName,
    int TotalItemCount,
    int ProcessedItemCount,
    int SucceededItemCount,
    int FailedItemCount,
    int SkippedItemCount,
    int PercentComplete,
    DateTimeOffset? ValidatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureSummary,
    DateTimeOffset CreatedAtUtc,
    string? RequestedByName,
    long Version,
    IReadOnlyList<BulkOperationItemResponse> Items,
    IReadOnlyList<string> PermittedActions);

/// <summary>One row of a bulk job.</summary>
public sealed record BulkOperationItemResponse(
    Guid Id,
    int RowNumber,
    Guid? UserId,
    string? SourceIdentifier,
    bool IsValid,
    string? ValidationMessage,
    bool IsProcessed,
    bool Succeeded,
    bool WasSkipped,
    string? ResultMessage);

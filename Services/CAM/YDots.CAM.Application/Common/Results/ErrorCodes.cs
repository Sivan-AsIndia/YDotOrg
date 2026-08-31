namespace YDots.CAM.Application.Common.Results;

/// <summary>
/// The stable error codes the Campaign API returns.
///
/// THE FIRST BLOCK IS A CROSS-SERVICE CONTRACT. Those nine codes are identical to the ones in
/// IAM and DON, so the Angular interceptor written for one service branches correctly on all
/// three. Renaming one here would silently break error handling on a screen that never
/// changed.
///
/// The campaign-specific codes below are new, and are the ones the campaign screens switch on
/// to tell an operator WHY a lifecycle action was refused - which is a different question from
/// whether they were allowed to attempt it.
/// </summary>
public static class ErrorCodes
{
    // ---- Shared with IAM and DON. Do not rename. ---------------------------------------
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string RecordNotFound = "RECORD_NOT_FOUND";
    public const string DuplicateRecord = "DUPLICATE_RECORD";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string DependencyFailure = "DEPENDENCY_FAILURE";
    public const string RecordInUse = "RECORD_IN_USE";

    // ---- Tenancy, mirrored from IAM so the client can react the same way ------------------
    public const string TenantSelectionRequired = "TENANT_SELECTION_REQUIRED";
    public const string CrossTenantAccessDenied = "CROSS_TENANT_ACCESS_DENIED";

    // ---- Campaign specific -----------------------------------------------------------------

    /// <summary>The caller created or submitted the thing they are trying to approve.</summary>
    public const string SegregationOfDutiesViolation = "SEGREGATION_OF_DUTIES_VIOLATION";

    /// <summary>A required readiness check has not passed, so the campaign cannot launch.</summary>
    public const string ReadinessIncomplete = "READINESS_INCOMPLETE";

    /// <summary>The campaign window has closed, or has not opened yet.</summary>
    public const string CampaignWindowClosed = "CAMPAIGN_WINDOW_CLOSED";

    /// <summary>A tracking asset was asked to do something its own live window forbids.</summary>
    public const string TrackingAssetNotLive = "TRACKING_ASSET_NOT_LIVE";
}

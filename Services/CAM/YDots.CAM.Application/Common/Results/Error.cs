namespace YDots.CAM.Application.Common.Results;

/// <summary>
/// A failure description. <see cref="Code"/> is the stable catalogue code,
/// <see cref="StatusCode"/> is the HTTP status the API returns, and <see cref="Errors"/>
/// carries the field-level messages for a validation failure.
///
/// ONE FACTORY PER ROW OF THE CATALOGUE, so no handler ever invents a code and the Angular
/// client can switch on a closed set of strings. A handler that needs a new kind of failure
/// adds a factory here rather than passing a loose string, which is what keeps the set closed.
/// </summary>
public sealed record Error(string Code, string Message, int StatusCode, IReadOnlyList<ValidationError>? Errors = null)
{
    public static Error Validation(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new(ErrorCodes.ValidationFailed, message, 400, errors);

    public static Error Unauthorised(string message = "Authentication is required.") =>
        new(ErrorCodes.AuthenticationRequired, message, 401);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        new(ErrorCodes.PermissionDenied, message, 403);

    public static Error NotFound(string message = "The record was not found inside your scope.") =>
        new(ErrorCodes.RecordNotFound, message, 404);

    public static Error Duplicate(string message) =>
        new(ErrorCodes.DuplicateRecord, message, 409);

    public static Error InvalidTransition(string message) =>
        new(ErrorCodes.InvalidStatusTransition, message, 409);

    public static Error Concurrency(string message =
        "This record changed after you opened it. Review the latest version before continuing.") =>
        new(ErrorCodes.ConcurrencyConflict, message, 409);

    /// <summary>
    /// Something the request depends on is unavailable or misconfigured. 502, because the
    /// CALLER did nothing wrong and retrying the same request later may well work.
    /// </summary>
    public static Error Dependency(string message) =>
        new(ErrorCodes.DependencyFailure, message, 502);

    /// <summary>
    /// The record is still referenced by something, so it cannot be removed.
    ///
    /// 409 rather than the 502 above, and the distinction matters: this is not a fault, it is
    /// the system refusing to orphan data. The caller can act on it and try again.
    /// </summary>
    public static Error InUse(string message) =>
        new(ErrorCodes.RecordInUse, message, 409);

    // ---- Tenancy -----------------------------------------------------------------------------

    public static Error TenantSelectionRequired(string message = "Select an organisation to continue.") =>
        new(ErrorCodes.TenantSelectionRequired, message, 409);

    /// <summary>
    /// The caller tried to touch a record belonging to a different Organisation. This should be
    /// unreachable - the query filter makes such a record invisible rather than forbidden - so
    /// returning it at all means a deliberate cross-Tenant attempt worth alerting on.
    /// </summary>
    public static Error CrossTenantAccessDenied(string message =
        "That record belongs to a different organisation.") =>
        new(ErrorCodes.CrossTenantAccessDenied, message, 403);

    // ---- Campaign specific ---------------------------------------------------------------------

    /// <summary>
    /// The segregation-of-duties refusal from section 5.2 of the module brief: a Campaign
    /// Manager may not approve a campaign they personally created or submitted.
    /// </summary>
    public static Error SegregationOfDuties(string message =
        "You cannot approve something you created or submitted. Ask a colleague to review it.") =>
        new(ErrorCodes.SegregationOfDutiesViolation, message, 409);

    public static Error ReadinessIncomplete(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new(ErrorCodes.ReadinessIncomplete, message, 409, errors);

    public static Error CampaignWindowClosed(string message =
        "This campaign is outside its scheduled dates.") =>
        new(ErrorCodes.CampaignWindowClosed, message, 409);

    public static Error TrackingAssetNotLive(string message =
        "This tracking asset is not live.") =>
        new(ErrorCodes.TrackingAssetNotLive, message, 409);
}

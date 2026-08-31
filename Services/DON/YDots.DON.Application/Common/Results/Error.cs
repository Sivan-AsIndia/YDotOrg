namespace YDots.DON.Application.Common.Results;

/// <summary>
/// A failure description. Code is the stable catalogue code from section 11, StatusCode is the
/// HTTP status the API returns and Errors carries the field level messages for a validation
/// failure. One factory per row of the error catalogue, so no handler invents a code.
/// </summary>
public sealed record Error(string Code, string Message, int StatusCode, IReadOnlyList<ValidationError>? Errors = null)
{
    public static Error Validation(string message, IReadOnlyList<ValidationError>? errors = null) =>
        new(ErrorCodes.ValidationFailed, message, 400, errors);

    public static Error Unauthorised(string message = "Authentication is required.") =>
        new(ErrorCodes.AuthenticationRequired, message, 401);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        new(ErrorCodes.PermissionDenied, message, 403);

    /// <summary>404 DONOR_NOT_FOUND. Used for a donor that is unavailable inside the caller's scope.</summary>
    public static Error DonorNotFound(string message = "That donor was not found inside your scope.") =>
        new(ErrorCodes.DonorNotFound, message, 404);

    /// <summary>404 for the supporting records: lead, consent, merge case, verification, follow-up.</summary>
    public static Error NotFound(string message = "The record was not found inside your scope.") =>
        new(ErrorCodes.RecordNotFound, message, 404);

    public static Error Duplicate(string message) =>
        new(ErrorCodes.DuplicateRecord, message, 409);

    public static Error InvalidTransition(string message) =>
        new(ErrorCodes.InvalidStatusTransition, message, 409);

    public static Error Concurrency(string message = "This record changed after you opened it. Review the latest version before continuing.") =>
        new(ErrorCodes.ConcurrencyConflict, message, 409);

    public static Error Dependency(string message) =>
        new(ErrorCodes.DependencyFailure, message, 502);

    /// <summary>
    /// 409 SEGREGATION_OF_DUTIES_VIOLATION. The caller holds the permission and is still refused,
    /// because they raised the record they are trying to decide on. Deliberately not Forbidden:
    /// a 403 reads as "ask for access", and there is no access to ask for.
    /// </summary>
    public static Error SegregationOfDuties(string message =
        "You cannot approve a record you created or submitted. Ask a colleague to review it.") =>
        new(ErrorCodes.SegregationOfDutiesViolation, message, 409);
}

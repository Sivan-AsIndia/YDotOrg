namespace YDots.DON.Application.Common.Results;

/// <summary>
/// The stable error codes from section 11 of the Donors contract. The UI switches on these
/// strings, so they must never change once they are published.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string DonorNotFound = "DONOR_NOT_FOUND";
    public const string RecordNotFound = "RECORD_NOT_FOUND";
    public const string DuplicateRecord = "DUPLICATE_RECORD";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string DependencyFailure = "DEPENDENCY_FAILURE";

    /// <summary>
    /// A maker/checker refusal, which is NOT a permission failure however much it looks like one.
    /// It is reported separately because the two demand opposite responses: a PERMISSION_DENIED is
    /// fixed by granting a permission, while this one can never be granted away and is fixed only
    /// by a second person acting. CAM and PAY already answer 409 with this code; DON now matches,
    /// so a client can branch on one code across every module.
    /// </summary>
    public const string SegregationOfDutiesViolation = "SEGREGATION_OF_DUTIES_VIOLATION";
}

namespace YDot.PAY.Application.Common.Results;

/// <summary>
/// A failure description. <see cref="Code"/> is the stable catalogue code,
/// <see cref="StatusCode"/> is the HTTP status the API returns, and <see cref="Errors"/> carries
/// the field-level messages for a validation failure.
///
/// ONE FACTORY PER ROW OF THE CATALOGUE, so no handler ever invents a code and the client can
/// switch on a closed set of strings.
///
/// THE DONOR-FACING MESSAGES ARE WRITTEN FOR A PERSON WHO HAS JUST TRIED TO GIVE MONEY. That is
/// not a cosmetic choice: somebody who sees a bare "transaction failed" tries again, and if the
/// first attempt actually succeeded they have now given twice.
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

    public static Error Dependency(string message) =>
        new(ErrorCodes.DependencyFailure, message, 502);

    public static Error InUse(string message) =>
        new(ErrorCodes.RecordInUse, message, 409);

    public static Error TenantSelectionRequired(string message = "Select an organisation to continue.") =>
        new(ErrorCodes.TenantSelectionRequired, message, 409);

    /// <summary>Money leaving the organisation needs two people. The requester cannot decide.</summary>
    public static Error SegregationOfDuties(string message =
        "You cannot approve a request you raised yourself. Ask a colleague to review it.") =>
        new(ErrorCodes.SegregationOfDutiesViolation, message, 409);

    // ---- Donation intent ---------------------------------------------------------------------

    public static Error IntentExpired(string message =
        "This donation link has expired. Please start again from the campaign page.") =>
        new(ErrorCodes.IntentExpired, message, 409);

    /// <summary>
    /// Already paid.
    ///
    /// PHRASED AS REASSURANCE, not as an error. The donor pressing pay twice is the usual cause,
    /// and the honest answer is that their money is safely received.
    /// </summary>
    public static Error IntentAlreadyPaid(string message =
        "This donation has already been paid. Thank you - a receipt is on its way.") =>
        new(ErrorCodes.IntentAlreadyPaid, message, 409);

    public static Error IntentCancelled(string message =
        "This donation was cancelled. Please start again from the campaign page.") =>
        new(ErrorCodes.IntentCancelled, message, 409);

    /// <summary>
    /// Sections 12 and 13: an existing donor for this organisation must sign in.
    ///
    /// 409 rather than 401, deliberately. Nothing about the caller's credentials is wrong; the
    /// flow simply has a branch, and the client reads this code to show the sign-in step with
    /// the donation intent preserved - which section 13 requires explicitly.
    /// </summary>
    public static Error ExistingDonorSignInRequired(string message =
        "You already have an account with this organisation. Please sign in to continue your donation.") =>
        new(ErrorCodes.ExistingDonorSignInRequired, message, 409);

    // ---- Payment ---------------------------------------------------------------------------------

    public static Error PaymentDeclined(string message =
        "The payment was not completed. You have not been charged. Please try again or use another method.") =>
        new(ErrorCodes.PaymentDeclined, message, 402);

    public static Error PaymentGatewayUnavailable(string message =
        "We could not reach the payment provider. Please try again in a few minutes.") =>
        new(ErrorCodes.PaymentGatewayUnavailable, message, 502);

    public static Error PaymentGatewayNotConfigured(string message =
        "This organisation has not finished setting up payments. Please contact them directly.") =>
        new(ErrorCodes.PaymentGatewayNotConfigured, message, 502);

    /// <summary>
    /// The outcome is unknown and being confirmed.
    ///
    /// THE MESSAGE MUST NOT SAY THE PAYMENT FAILED. It may well have succeeded, and a donor told
    /// it failed will try again - which is how one gift becomes two.
    /// </summary>
    public static Error PaymentVerificationPending(string message =
        "We are still confirming this payment with the provider. Please do not try again - "
        + "we will e-mail you as soon as it is confirmed.") =>
        new(ErrorCodes.PaymentVerificationPending, message, 202);

    public static Error PaymentInProgress(string message =
        "A payment is already in progress for this donation. Please wait for it to finish.") =>
        new(ErrorCodes.PaymentInProgress, message, 409);

    // ---- Receipts ---------------------------------------------------------------------------------------

    public static Error ReceiptNotEligible(string message) =>
        new(ErrorCodes.ReceiptNotEligible, message, 409);

    public static Error ReceiptAlreadyIssued(string message =
        "A receipt has already been issued for this donation. Correct the existing one instead.") =>
        new(ErrorCodes.ReceiptAlreadyIssued, message, 409);

    public static Error ReceiptNotCorrectable(string message =
        "Only an issued receipt can be corrected.") =>
        new(ErrorCodes.ReceiptNotCorrectable, message, 409);

    // ---- Refunds and chargebacks ---------------------------------------------------------------------------

    public static Error RefundExceedsBalance(string message) =>
        new(ErrorCodes.RefundExceedsBalance, message, 409);

    public static Error RefundAlreadyInProgress(string message =
        "A refund is already in progress for this donation.") =>
        new(ErrorCodes.RefundAlreadyInProgress, message, 409);

    public static Error ChargebackDeadlinePassed(string message =
        "The evidence deadline for this chargeback has passed.") =>
        new(ErrorCodes.ChargebackDeadlinePassed, message, 409);
}

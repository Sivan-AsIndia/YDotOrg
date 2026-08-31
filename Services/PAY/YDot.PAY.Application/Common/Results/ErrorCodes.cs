namespace YDot.PAY.Application.Common.Results;

/// <summary>
/// The stable error codes the Payments API returns.
///
/// THE FIRST BLOCK IS A CROSS-SERVICE CONTRACT, identical to the codes in IAM, DON and CAM, so
/// the Angular interceptor written for one service branches correctly on all four.
///
/// THE PAYMENT-SPECIFIC CODES BELOW ARE WHAT THE DONOR-FACING SCREENS SWITCH ON. "Your card was
/// declined", "this link has expired" and "we are still confirming your payment" are three
/// completely different things to tell somebody who has just tried to give money, and a single
/// generic failure would collapse them into one unhelpful message.
/// </summary>
public static class ErrorCodes
{
    // ---- Shared with IAM, DON and CAM. Do not rename. ---------------------------------
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string RecordNotFound = "RECORD_NOT_FOUND";
    public const string DuplicateRecord = "DUPLICATE_RECORD";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string DependencyFailure = "DEPENDENCY_FAILURE";
    public const string RecordInUse = "RECORD_IN_USE";
    public const string TenantSelectionRequired = "TENANT_SELECTION_REQUIRED";
    public const string SegregationOfDutiesViolation = "SEGREGATION_OF_DUTIES_VIOLATION";

    // ---- Donation intent ------------------------------------------------------------------

    /// <summary>The payment link has lapsed. Section 24: a link does not live forever.</summary>
    public const string IntentExpired = "INTENT_EXPIRED";

    /// <summary>Already paid. The donor probably pressed the button twice.</summary>
    public const string IntentAlreadyPaid = "INTENT_ALREADY_PAID";

    /// <summary>Cancelled by the donor or by an operator.</summary>
    public const string IntentCancelled = "INTENT_CANCELLED";

    /// <summary>
    /// Sections 12 and 13: this e-mail already belongs to a donor in THIS organisation, so the
    /// donor must sign in rather than have a second account created.
    /// </summary>
    public const string ExistingDonorSignInRequired = "EXISTING_DONOR_SIGN_IN_REQUIRED";

    // ---- Payment -----------------------------------------------------------------------------

    /// <summary>The gateway refused the payment. Retryable.</summary>
    public const string PaymentDeclined = "PAYMENT_DECLINED";

    /// <summary>The gateway itself is unreachable or erroring. Not the donor's fault.</summary>
    public const string PaymentGatewayUnavailable = "PAYMENT_GATEWAY_UNAVAILABLE";

    /// <summary>No gateway account is configured for this organisation.</summary>
    public const string PaymentGatewayNotConfigured = "PAYMENT_GATEWAY_NOT_CONFIGURED";

    /// <summary>
    /// The outcome is genuinely unknown and is being verified.
    ///
    /// THE MOST IMPORTANT CODE IN THIS FILE. It must never be shown as a failure: the donor may
    /// well have been charged, and telling them it failed invites a second payment.
    /// </summary>
    public const string PaymentVerificationPending = "PAYMENT_VERIFICATION_PENDING";

    /// <summary>An attempt is already in flight. Guards the double-click.</summary>
    public const string PaymentInProgress = "PAYMENT_IN_PROGRESS";

    // ---- Receipts -----------------------------------------------------------------------------------

    /// <summary>The donation is voided or fully refunded, so no receipt may be issued.</summary>
    public const string ReceiptNotEligible = "RECEIPT_NOT_ELIGIBLE";

    /// <summary>A valid receipt already exists. Correct it rather than issuing a second.</summary>
    public const string ReceiptAlreadyIssued = "RECEIPT_ALREADY_ISSUED";

    /// <summary>Only an issued receipt can be corrected or voided.</summary>
    public const string ReceiptNotCorrectable = "RECEIPT_NOT_CORRECTABLE";

    // ---- Refunds and chargebacks -----------------------------------------------------------------------

    /// <summary>More was asked for than the donation has left to give back.</summary>
    public const string RefundExceedsBalance = "REFUND_EXCEEDS_BALANCE";

    /// <summary>A refund case is already open on this donation.</summary>
    public const string RefundAlreadyInProgress = "REFUND_ALREADY_IN_PROGRESS";

    /// <summary>The evidence deadline has passed.</summary>
    public const string ChargebackDeadlinePassed = "CHARGEBACK_DEADLINE_PASSED";
}

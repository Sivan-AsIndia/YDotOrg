namespace YDot.PAY.Application.Common.Constants;

/// <summary>
/// Stable dotted codes written into <c>PaymentAuditEvent.ActionCode</c>.
///
/// Codes are how the audit screen filters and how a compliance report groups, so they are
/// append-only in exactly the way permission codes are: a renamed code orphans every historical
/// row that used it.
///
/// THIS TRAIL IS READ IN ANGER MORE OFTEN THAN THE OTHERS. When a donor says they were charged
/// twice, or a bank raises a chargeback, or a receipt total does not match a bank statement,
/// these rows are the evidence.
/// </summary>
public static class AuditActionCodes
{
    // ---- Donation intents ----------------------------------------------------------------
    public const string IntentCreated = "pay.intent.created";
    public const string IntentExistingDonorMatched = "pay.intent.existing-donor-matched";
    public const string IntentPaymentLinkCreated = "pay.intent.payment-link-created";
    public const string IntentPaymentLinkResent = "pay.intent.payment-link-resent";
    public const string IntentCancelled = "pay.intent.cancelled";
    public const string IntentExpired = "pay.intent.expired";
    public const string IntentExported = "pay.intent.exported";

    // ---- Payment attempts ---------------------------------------------------------------------
    public const string PaymentAttemptStarted = "pay.attempt.started";
    public const string PaymentAttemptSucceeded = "pay.attempt.succeeded";
    public const string PaymentAttemptFailed = "pay.attempt.failed";
    public const string PaymentAttemptTimedOut = "pay.attempt.timed-out";

    /// <summary>The gateway was asked what actually happened. The safe-retry precondition.</summary>
    public const string PaymentVerified = "pay.payment.verified";

    public const string PaymentSafeRetryRequested = "pay.payment.safe-retry-requested";

    // ---- The gateway event queue -----------------------------------------------------------------
    public const string PaymentEventReceived = "pay.event.received";
    public const string PaymentEventProcessed = "pay.event.processed";
    public const string PaymentEventDuplicate = "pay.event.duplicate";
    public const string PaymentEventFailed = "pay.event.failed";
    public const string PaymentEventDismissed = "pay.event.dismissed";

    /// <summary>
    /// A webhook arrived whose signature did not verify.
    ///
    /// THE ROW WORTH ALERTING ON. Anybody can post to a webhook URL, and a failed signature is
    /// either a misconfiguration or somebody trying to fabricate a payment.
    /// </summary>
    public const string PaymentEventSignatureRejected = "pay.event.signature-rejected";

    // ---- Donations --------------------------------------------------------------------------------------
    public const string DonationRecorded = "pay.donation.recorded";
    public const string DonationOfflineRecorded = "pay.donation.offline-recorded";
    public const string DonationSettled = "pay.donation.settled";
    public const string DonationReconciled = "pay.donation.reconciled";
    public const string DonationVoided = "pay.donation.voided";
    public const string DonationExported = "pay.donation.exported";

    /// <summary>Somebody unmasked a donor's contact details on a payment screen.</summary>
    public const string DonationSensitiveDonorViewed = "pay.donation.sensitive-donor-viewed";

    // ---- Donor creation, sections 15 to 17 ------------------------------------------------------------------
    public const string DonorCreatedFromIntent = "pay.donor.created-from-intent";
    public const string DonorAccountInvited = "pay.donor.account-invited";
    public const string LeadConverted = "pay.lead.converted";

    // ---- Receipts ---------------------------------------------------------------------------------------------
    public const string ReceiptIssued = "pay.receipt.issued";
    public const string ReceiptCorrected = "pay.receipt.corrected";
    public const string ReceiptVoided = "pay.receipt.voided";
    public const string ReceiptDelivered = "pay.receipt.delivered";
    public const string ReceiptDeliveryFailed = "pay.receipt.delivery-failed";
    public const string ReceiptResent = "pay.receipt.resent";
    public const string ReceiptExported = "pay.receipt.exported";

    // ---- Refunds -----------------------------------------------------------------------------------------------
    public const string RefundRequested = "pay.refund.requested";
    public const string RefundApproved = "pay.refund.approved";
    public const string RefundRejected = "pay.refund.rejected";
    public const string RefundCompleted = "pay.refund.completed";
    public const string RefundFailed = "pay.refund.failed";
    public const string RefundExported = "pay.refund.exported";

    // ---- Chargebacks ---------------------------------------------------------------------------------------------
    public const string ChargebackOpened = "pay.chargeback.opened";
    public const string ChargebackAssigned = "pay.chargeback.assigned";
    public const string ChargebackEvidenceSubmitted = "pay.chargeback.evidence-submitted";
    public const string ChargebackResolved = "pay.chargeback.resolved";

    // ---- Gateway configuration ---------------------------------------------------------------------------------
    public const string GatewayAccountCreated = "pay.gateway.account-created";
    public const string GatewayAccountUpdated = "pay.gateway.account-updated";

    /// <summary>
    /// The payout destination changed.
    ///
    /// ITS OWN CODE, separate from a general update, because this is the change that redirects
    /// an organisation's income - and it is the first thing anybody would look for if money
    /// started arriving somewhere unexpected.
    /// </summary>
    public const string GatewayPayoutDestinationChanged = "pay.gateway.payout-destination-changed";
}

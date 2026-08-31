namespace YDot.PAY.Application.Common.Constants;

/// <summary>
/// Every permission code the Payments module enforces.
///
/// THESE STRINGS ARE A CROSS-SERVICE CONTRACT. PAY cannot issue a claim - it never signs a token
/// - so each of these must ALSO exist in IAM's <c>ModulePermissionCatalogue</c>, where it is
/// seeded and attached to roles. If the two drift, the symptom is a 403 on an endpoint that
/// looks correctly configured.
///
/// THE PUBLIC DONATION ENDPOINTS REQUIRE NONE OF THESE. Sections 19 to 22 describe a stranger
/// with a QR code and no account; those routes are [AllowAnonymous] and protected by the
/// unguessability of the intent reference instead.
/// </summary>
public static class PermissionCodes
{
    /// <summary>Section-level view permission. Every payments screen requires it as a baseline.</summary>
    public const string Section = "PAY.View";

    // ---- Donation intents ----------------------------------------------------------------
    public const string IntentsView = "pay.intents.view";
    public const string IntentsCreate = "pay.intents.create";
    public const string IntentsCancel = "pay.intents.cancel";
    public const string IntentsResendLink = "pay.intents.resend-link";
    public const string IntentsExport = "pay.intents.export";

    // ---- Donations -------------------------------------------------------------------------
    public const string DonationsView = "pay.donations.view";

    /// <summary>Recording a donation taken outside the gateway: a cheque, a bank transfer, cash.</summary>
    public const string DonationsRecordOffline = "pay.donations.record-offline";

    public const string DonationsExport = "pay.donations.export";

    /// <summary>Marking a donation settled and reconciled against a bank statement.</summary>
    public const string DonationsReconcile = "pay.donations.reconcile";

    /// <summary>
    /// Unmasks the donor's e-mail, mobile and tax identifier on payment screens.
    ///
    /// SEPARATE FROM DonationsView, because seeing that a donation happened and seeing who made
    /// it are different levels of access - a reconciliation clerk needs the first and not the
    /// second.
    /// </summary>
    public const string DonationsViewSensitiveDonor = "pay.donations.view-sensitive-donor";

    // ---- Payment verification and the event queue -----------------------------------------------
    public const string PaymentsVerify = "pay.payments.verify";
    public const string PaymentsViewEvents = "pay.payments.view-events";
    public const string PaymentsReprocessEvent = "pay.payments.reprocess-event";
    public const string PaymentsDismissEvent = "pay.payments.dismiss-event";

    /// <summary>
    /// Re-driving a payment through Payment Support and Safe Retry.
    ///
    /// SENSITIVE, because a retry against an attempt whose outcome is unknown can charge a donor
    /// twice. The endpoint verifies with the gateway first; the permission is what limits who
    /// can ask for that at all.
    /// </summary>
    public const string PaymentsSafeRetry = "pay.payments.safe-retry";

    // ---- Receipts ------------------------------------------------------------------------------------
    public const string ReceiptsView = "pay.receipts.view";
    public const string ReceiptsIssue = "pay.receipts.issue";
    public const string ReceiptsCorrect = "pay.receipts.correct";
    public const string ReceiptsVoid = "pay.receipts.void";
    public const string ReceiptsResend = "pay.receipts.resend";
    public const string ReceiptsExport = "pay.receipts.export";

    // ---- Refunds -----------------------------------------------------------------------------------------
    public const string RefundsView = "pay.refunds.view";
    public const string RefundsRequest = "pay.refunds.request";

    /// <summary>
    /// Deciding a refund.
    ///
    /// SEPARATE FROM RefundsRequest, and the handler ALSO refuses the person who raised it -
    /// money leaving the organisation needs two people, and one permission held by one person
    /// cannot express that on its own.
    /// </summary>
    public const string RefundsApprove = "pay.refunds.approve";

    public const string RefundsReject = "pay.refunds.reject";
    public const string RefundsExport = "pay.refunds.export";

    // ---- Chargebacks ---------------------------------------------------------------------------------------
    public const string ChargebacksView = "pay.chargebacks.view";
    public const string ChargebacksAssign = "pay.chargebacks.assign";
    public const string ChargebacksSubmitEvidence = "pay.chargebacks.submit-evidence";
    public const string ChargebacksResolve = "pay.chargebacks.resolve";

    // ---- Gateway configuration ----------------------------------------------------------------------------------
    public const string GatewayView = "pay.gateway.view";

    /// <summary>
    /// Changing where an organisation's money is paid out to.
    ///
    /// THE MOST DANGEROUS PERMISSION IN THE MODULE. Whoever holds it can redirect every future
    /// donation to a different merchant account, so it belongs to the organisation's
    /// administrator and to nobody else by default.
    /// </summary>
    public const string GatewayManage = "pay.gateway.manage";

    /// <summary>Codes whose use always writes an enhanced audit row.</summary>
    public static readonly IReadOnlyList<string> Sensitive =
    [
        IntentsCancel, IntentsExport,
        DonationsRecordOffline, DonationsExport, DonationsReconcile, DonationsViewSensitiveDonor,
        PaymentsSafeRetry, PaymentsDismissEvent,
        ReceiptsIssue, ReceiptsCorrect, ReceiptsVoid, ReceiptsExport,
        RefundsApprove, RefundsReject, RefundsExport,
        ChargebacksResolve, ChargebacksSubmitEvidence,
        GatewayManage
    ];

    /// <summary>Every code PAY enforces. Mirrored in IAM ModulePermissionCatalogue.Payments.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Section,
        IntentsView, IntentsCreate, IntentsCancel, IntentsResendLink, IntentsExport,
        DonationsView, DonationsRecordOffline, DonationsExport, DonationsReconcile,
        DonationsViewSensitiveDonor,
        PaymentsVerify, PaymentsViewEvents, PaymentsReprocessEvent, PaymentsDismissEvent,
        PaymentsSafeRetry,
        ReceiptsView, ReceiptsIssue, ReceiptsCorrect, ReceiptsVoid, ReceiptsResend, ReceiptsExport,
        RefundsView, RefundsRequest, RefundsApprove, RefundsReject, RefundsExport,
        ChargebacksView, ChargebacksAssign, ChargebacksSubmitEvidence, ChargebacksResolve,
        GatewayView, GatewayManage
    ];

    public static bool IsSensitive(string permissionCode) =>
        Sensitive.Contains(permissionCode, StringComparer.Ordinal);
}

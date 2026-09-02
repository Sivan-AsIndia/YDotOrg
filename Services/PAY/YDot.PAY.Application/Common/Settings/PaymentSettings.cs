namespace YDot.PAY.Application.Common.Settings;

/// <summary>
/// The payment rules an organisation might reasonably want to tune, bound through the option
/// pattern so none of them is a literal buried in a handler.
/// </summary>
public sealed class PaymentSettings
{
    public const string SectionName = "PaymentSettings";

    /// <summary>
    /// How long a payment link stays valid, when the gateway account does not set its own.
    ///
    /// A link that never expires can be replayed months later against a campaign that has since
    /// closed.
    /// </summary>
    public int DefaultPaymentLinkValidityMinutes { get; set; } = 60;

    /// <summary>
    /// How long an intent sits unpaid before it is expired by the sweeper.
    ///
    /// Longer than the link validity on purpose: the link dying is recoverable by issuing
    /// another, while expiring the intent ends the attempt entirely.
    /// </summary>
    public int IntentExpiryHours { get; set; } = 72;

    /// <summary>
    /// How many attempts one intent may make before it stops offering a retry and goes to
    /// Payment Support and Safe Retry instead.
    ///
    /// TWO, AND THE DOCUMENT FIXES THE NUMBER. Section 4: "For a row with status Fail, the eye
    /// icon view also shows a Retry button... If the retry ALSO fails, the payment record moves
    /// to the Payment Support and Safe Retry page for the admin to handle." That is the original
    /// attempt plus one retry - two - and the record leaves the queue.
    ///
    /// IT WAS THREE, WHICH SILENTLY BROKE THAT HANDOVER. A donor who failed, was retried and
    /// failed again sat at two attempts, below the threshold, so the record never reached the
    /// support queue: the Payment Queue kept offering a third identical Retry and nobody was
    /// ever asked to look at it. The cap exists because a donor whose card keeps failing needs a
    /// person, not another identical button - and one retry is where that point arrives.
    /// </summary>
    public int MaximumAttemptsBeforeSupport { get; set; } = 2;

    /// <summary>
    /// How long to wait for a gateway answer before treating the outcome as UNKNOWN.
    ///
    /// Unknown is not failure: the attempt becomes TimedOut and is verified with the provider
    /// rather than retried, because retrying an attempt that actually succeeded charges twice.
    /// </summary>
    public int GatewayTimeoutSeconds { get; set; } = 30;

    /// <summary>Whether a receipt is issued automatically the moment a donation is recorded.</summary>
    public bool AutoIssueReceiptOnDonation { get; set; } = true;

    /// <summary>Whether the issued receipt is e-mailed automatically.</summary>
    public bool AutoDeliverReceipt { get; set; } = true;

    /// <summary>
    /// Whether a successful payment by a new donor creates their account and invites them.
    ///
    /// Sections 15 and 17. A setting rather than a hard-coded branch because an organisation
    /// that does not run a donor portal has no use for the account or the invitation.
    /// </summary>
    public bool CreateDonorAccountOnSuccess { get; set; } = true;

    /// <summary>
    /// The prefix on a receipt number, before the financial year and the sequence.
    ///
    /// Per deployment rather than per organisation, because it identifies the platform on the
    /// document; the per-organisation part is the sequence itself.
    /// </summary>
    public string ReceiptNumberPrefix { get; set; } = "RCPT";

    /// <summary>
    /// The month a financial year starts, 1 to 12.
    ///
    /// FOUR BY DEFAULT: the Indian financial year runs April to March, and receipt numbering
    /// follows it. Configurable because a deployment elsewhere will want January or July.
    /// </summary>
    public int FinancialYearStartMonth { get; set; } = 4;

    /// <summary>How many days a chargeback allows for evidence, when the gateway does not say.</summary>
    public int DefaultChargebackEvidenceDays { get; set; } = 7;

    /// <summary>
    /// Where rendered receipt documents are kept.
    ///
    /// IT MUST BE A MOUNTED VOLUME, not a path inside the container. A container filesystem is
    /// discarded on the next deployment, and discarding donors' tax documents is not a
    /// recoverable mistake - a receipt has to be reproducible for the seven-plus years the donor
    /// may be asked to justify their claim.
    ///
    /// Empty falls back to a directory beside the application, which is correct for a developer
    /// running locally and wrong for anything else.
    /// </summary>
    public string ReceiptDocumentRoot { get; set; } = string.Empty;

    /// <summary>
    /// How many gateway events one pass of the queue processor takes.
    ///
    /// Capped so a backlog - a provider that was down for an hour and then redelivered
    /// everything - is worked through in batches rather than loaded whole.
    /// </summary>
    public int EventProcessingBatchSize { get; set; } = 100;
}

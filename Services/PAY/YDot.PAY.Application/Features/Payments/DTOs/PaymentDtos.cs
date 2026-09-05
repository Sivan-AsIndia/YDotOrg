using YDot.PAY.Application.Common.Models;
using YDot.PAY.Domain.Enums;

namespace YDot.PAY.Application.Features.Payments.DTOs;

// =====================================================================================
// Payment verification - SCR-PAY-002
// =====================================================================================

/// <summary>
/// What the payment verification screen shows.
///
/// THE FIELD NAMES MATCH THE UI CONTRACT in <c>payment-verification.model.ts</c>, so the screen
/// binds to the API without a translation layer.
/// </summary>
public sealed record PaymentVerificationResponse(
    string DonationReference,
    MoneyResponse RequestedAmount,

    /// <summary>Pending, Confirmed or Failed. The three states the screen renders.</summary>
    string BackendPaymentState,

    DateTimeOffset? LastVerifiedTimeUtc,
    string? GatewayReference,

    /// <summary>Eligible or Not yet eligible.</summary>
    string ReceiptEligibility,

    string? ReceiptLink,

    /// <summary>What the donor quotes to support. The correlation id of the verifying request.</summary>
    string SupportCorrelationReference,

    /// <summary>The attempt history, newest first.</summary>
    IReadOnlyList<PaymentVerificationHistoryRow> History,

    IReadOnlyList<string> PermittedActions,

    /// <summary>
    /// Which provider actually took this payment, as the attempt recorded it.
    ///
    /// IT IS THE ATTEMPT'S NAME, NOT THE ORGANISATION'S CURRENT SETTING. An administrator who
    /// switches from Razorpay to Stripe between the payment and the donor pressing Check again
    /// must not make the result page claim the money went through Stripe. The attempt holds
    /// what was true when it ran, and that is what a support call needs.
    /// </summary>
    string GatewayName,

    /// <summary>
    /// True when this donation was started by somebody who was a LEAD rather than a Donor.
    ///
    /// WHY THE RESULT PAGE NEEDS IT. A confirmed lead donation ends differently from a confirmed
    /// donor donation: the lead has just been converted, given a Donor login and e-mailed an
    /// activation link, so they have no session and no password yet. Sending them to a screen
    /// under /app would put them through the authentication guard and land them on a sign-in
    /// form no password of theirs opens. They go back to the public donor form instead.
    /// </summary>
    bool OriginatedFromLead,

    /// <summary>
    /// True when the existing-donor check recognised this person before they paid.
    ///
    /// NULL ON THE INTENT MEANS "NEVER ASKED", which reads here as false. It is the other half
    /// of the redirect decision: a recognised Donor goes back to the in-app donation screen, and
    /// everybody else is treated as new.
    /// </summary>
    bool ExistingDonorMatched);

/// <summary>One row of the verification history.</summary>
public sealed record PaymentVerificationHistoryRow(string Primary, string Secondary, string Meta);

/// <summary>Asking the gateway what actually happened.</summary>
public sealed record VerifyPaymentRequest(
    /// <summary>
    /// The intent reference. Used by the PUBLIC result page, which has no session.
    ///
    /// Safe because the reference is unguessable and resolves to exactly one intent: the caller
    /// is naming a record, not choosing one.
    /// </summary>
    string? IntentReference = null,

    /// <summary>The attempt to verify. Used by staff, who have the id to hand.</summary>
    Guid? PaymentAttemptId = null);

// =====================================================================================
// Payment event queue - SCR-PAY-003
// =====================================================================================

/// <summary>
/// One row of the payment queue.
///
/// THE LAST FOUR FIELDS COME FROM THE INTENT, NOT FROM THE EVENT, and they are here because the
/// screen the workflow document draws is a list of DONORS whose payment did not complete - donor
/// name, e-mail, campaign, amount, status, received time - rather than a list of webhook bodies.
/// Without them the browser could only show a gateway event id and would have to fetch each
/// intent separately to fill a row, which is one request per row on every page.
/// </summary>
public sealed record PaymentEventListItemResponse(
    Guid Id,
    PaymentEventType EventType,
    string EventTypeDescription,
    PaymentEventStatus Status,
    string StatusDescription,
    string GatewayName,
    string GatewayEventId,
    string? GatewayReference,
    MoneyResponse? Amount,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    bool SignatureVerified,
    string? ProcessingError,
    int ProcessingAttempts,
    Guid? DonationIntentId,
    string? IntentReference,
    long Version,

    /// <summary>The donor as the intent recorded them. Null when the event matched no intent.</summary>
    string? DonorName,

    /// <summary>
    /// The donor's e-mail, MASKED unless the caller holds pay.donations.view-sensitive-donor.
    ///
    /// Masked here rather than in the browser, because a value the browser is trusted to hide is
    /// a value the browser was sent.
    /// </summary>
    string? DonorEmail,

    string? CampaignName,

    /// <summary>
    /// Fail, Pending or Success - the DONATION's outcome, which is not the same question as
    /// <see cref="Status"/>.
    ///
    /// THE TWO ARE ROUTINELY DIFFERENT AND THAT IS THE POINT. `Status` says whether we finished
    /// processing the webhook; this says whether the donor's money moved. A `payment.failed`
    /// webhook that we processed perfectly is Status=Processed and PaymentOutcome=Fail, and it is
    /// the second one the queue is triaging.
    /// </summary>
    string PaymentOutcome);

/// <summary>One queued event in full, with its raw payload.</summary>
public sealed record PaymentEventDetailResponse(
    Guid Id,
    PaymentEventType EventType,
    string EventTypeDescription,
    PaymentEventStatus Status,
    string StatusDescription,
    string GatewayName,
    string GatewayEventId,
    string? GatewayReference,
    MoneyResponse? Amount,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    bool SignatureVerified,
    string? ProcessingError,
    int ProcessingAttempts,
    Guid? DonationIntentId,
    string? IntentReference,
    Guid? PaymentAttemptId,

    /// <summary>
    /// The verbatim webhook body.
    ///
    /// Returned only to a caller holding pay.payments.view-events, because a raw gateway payload
    /// can contain donor contact details the masked views deliberately hide.
    /// </summary>
    string? RawPayload,

    Guid? DismissedByUserId,
    string? DismissalReason,
    long Version,
    IReadOnlyList<string> PermittedActions);

/// <summary>Re-running a failed event through the processor.</summary>
public sealed record ReprocessPaymentEventRequest(long ExpectedVersion, string? Note = null);

/// <summary>Marking an event as needing no action.</summary>
public sealed record DismissPaymentEventRequest(long ExpectedVersion, string Reason);

/// <summary>Filter for the event queue.</summary>
public sealed class PaymentEventSearchFilter : PaginationRequest
{
    public PaymentEventStatus? Status { get; set; }

    public PaymentEventType? EventType { get; set; }

    public string? GatewayName { get; set; }

    public DateTimeOffset? ReceivedFromUtc { get; set; }

    public DateTimeOffset? ReceivedToUtc { get; set; }

    /// <summary>
    /// Events that failed their signature check.
    ///
    /// The first thing to look at when something is wrong: a failed signature is either a
    /// misconfiguration or somebody trying to fabricate a payment.
    /// </summary>
    public bool? SignatureFailedOnly { get; set; }

    /// <summary>Pending or Failed - the events still needing somebody.</summary>
    public bool? OutstandingOnly { get; set; }

    /// <summary>
    /// Fail / Pending / Success - the DONATION outcome the queue filters on.
    ///
    /// THE DOCUMENT'S QUEUE IS DEFINED BY THIS, not by the webhook status: "Success -> the payment
    /// does NOT appear in the Payment Event Queue", "Fail -> appears with status Fail", "Pending ->
    /// appears with status Pending". Leaving it unset returns Fail and Pending together, which is
    /// the queue as the document describes it.
    /// </summary>
    public PaymentOutcomeFilter? PaymentOutcome { get; set; }

    /// <summary>
    /// Donor name, e-mail, campaign and intent reference, as well as the gateway ids.
    ///
    /// The base <c>Search</c> only matched gateway identifiers, which nobody triaging a queue has
    /// to hand - they have the donor's name because the donor rang up about it.
    /// </summary>
    public bool SearchIncludesDonor { get; set; } = true;
}

// =====================================================================================
// Payment support and safe retry - SCR-PAY-007
// =====================================================================================

/// <summary>
/// Asking for a payment to be safely retried.
///
/// SAFE RETRY IS NOT A PLAIN RETRY. The handler verifies the previous attempt with the gateway
/// FIRST, and refuses if it actually succeeded - which is the difference between helping a donor
/// whose card failed and charging one who already paid.
/// </summary>
public sealed record SafeRetryRequest(long ExpectedVersion, string Reason);

/// <summary>What safe retry decided, and why.</summary>
public sealed record SafeRetryResponse(
    Guid IntentId,
    string IntentReference,

    /// <summary>Retried, AlreadyPaid, StillPending or Refused.</summary>
    string Outcome,

    string Message,

    /// <summary>The new payment link, when a retry was actually started.</summary>
    string? PaymentLinkUrl,

    DonationIntentStatus IntentStatus,
    int AttemptCount,
    IReadOnlyList<string> PermittedActions);

/// <summary>One row of the support queue: intents that failed and need a person.</summary>
public sealed record PaymentSupportCaseResponse(
    Guid IntentId,
    string IntentReference,
    string DonorName,
    string DonorEmail,
    MoneyResponse Amount,
    DonationIntentStatus Status,
    int AttemptCount,
    DateTimeOffset? LastAttemptAtUtc,
    string? LastFailureReason,
    string? LastGatewayResultCode,

    /// <summary>True when the last attempt's outcome is unknown and must be verified.</summary>
    bool RequiresVerification,

    Guid? CampaignId,
    string? CampaignName,
    DateTimeOffset CreatedAtUtc);

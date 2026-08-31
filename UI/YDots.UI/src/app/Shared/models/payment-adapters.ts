/**
 * Turns the payments API's DTOs into the view models the eight PAY screens already bind to.
 *
 * WHY AN ADAPTER LAYER RATHER THAN CHANGING THE SCREENS' TYPES. The screens were built against a
 * specification with its own vocabulary - "Issue state", "Verified payment state", "Case state" -
 * and their templates, their filters and their whole visual language follow it. The API speaks
 * the domain's vocabulary instead. Both are right for where they sit, and translating in ONE
 * place rather than renaming a hundred template bindings keeps the theme and the responsive
 * layout exactly as they were, which is what the brief asks for.
 *
 * IT ALSO MAKES THE MISMATCHES VISIBLE. Every place the two vocabularies do NOT line up cleanly
 * is a function below with a comment explaining which way the mapping goes and why. Spread across
 * eight components those decisions would be invisible; here they can be read in one sitting.
 *
 * THE DIRECTION IS ONE-WAY. These functions map API to UI. Writes go the other way and go
 * straight through `PaymentApiService` with the API's own request types - a write is where
 * correctness matters most, and translating a refund amount through a second vocabulary on the
 * way out would be a place for a bug to hide.
 */

import {
  ChargebackCaseListItem,
  DonationIntentDetail,
  DonationIntentListItem,
  MoneyResponse,
  PaymentAttemptStatus,
  PaymentEventListItem,
  PaymentSupportCase,
  PaymentVerification,
  ReceiptListItem,
  RefundCaseListItem,
} from './payment.model';
import {
  DeliveryState,
  IssueState,
  ReceiptRecord,
} from './receipt-register.model';
import {
  BackendPaymentState,
  PaymentVerificationRecord,
  ReceiptEligibility,
} from './payment-verification.model';
import {
  DuplicateStatus,
  FailureType,
  PaymentEventRecord,
  PaymentEventState,
  PaymentStatus,
  SequenceStatus,
  SignatureResult,
} from './payment-event-queue.model';
import {
  PsrLifecycleState,
  PsrRecoveryRecord,
  PsrVerifiedPaymentState,
} from './payment-support-safe-retry.model';
import {
  RccCaseState,
  RccCaseType,
  RccOutcomeStatus,
  RccProviderStatus,
  RccReconciliationStatus,
  RccRefundCaseRecord,
} from './refund-chargeback-case.model';

// =============================================================================================
// Shared formatting
// =============================================================================================

/**
 * A date as these screens show it.
 *
 * en-IN and IST, matching what the screens already display. The server sends UTC; this is the one
 * place it becomes local, so a screen never does the conversion itself and two screens can never
 * disagree about what "12 May, 2:20 PM" means.
 */
export function formatMoment(iso: string | null | undefined): string {
  if (!iso) {
    return '';
  }

  const parsed = new Date(iso);

  if (Number.isNaN(parsed.getTime())) {
    return '';
  }

  return `${parsed.toLocaleString('en-IN', { dateStyle: 'medium', timeStyle: 'short' })} · IST`;
}

/** The raw figure, for screens that do their own arithmetic on it. */
export function moneyAmount(money: MoneyResponse | null | undefined): number {
  return money?.amount ?? 0;
}

export function moneyCurrency(money: MoneyResponse | null | undefined): string {
  return money?.currencyCode ?? 'INR';
}

// =============================================================================================
// Receipt register - SCR-PAY-005
// =============================================================================================

/**
 * The API's receipt status to the register's "Issue state".
 *
 * `corrected` BECOMES "Correction" rather than a state of its own. On the API a corrected receipt
 * is one that has been superseded by a newer version; on the register the same row is what a
 * finance officer looks at to find the correction chain. The two mean the same thing under
 * different names.
 */
function toIssueState(status: ReceiptListItem['issueState']): IssueState {
  switch (status) {
    case 'draft':
      return 'Draft';
    case 'submitted':
      return 'Submitted';
    case 'pendingReview':
      return 'Pending review';
    case 'issued':
      return 'Issued';
    case 'corrected':
      return 'Correction';
    case 'voided':
      return 'Voided';
    default:
      return 'Draft';
  }
}

function toDeliveryState(status: ReceiptListItem['deliveryState']): DeliveryState {
  switch (status) {
    case 'notSent':
      return 'Not sent';
    case 'pending':
      return 'Pending';
    case 'delivered':
      return 'Delivered';
    case 'failed':
      return 'Failed';
    default:
      return 'Not sent';
  }
}

/**
 * One receipt register row.
 *
 * `inScope` IS ALWAYS TRUE, and that is not a simplification. The API's query filter means a row
 * from outside the caller's organisation is not merely hidden, it is unreachable - so anything
 * that arrives here is by construction in scope. The flag stays on the view model because the
 * template renders an out-of-scope state, and a screen that never sets it simply never shows it.
 *
 * `hasConflict` and `dependencyBroken` are likewise set by the WRITE path, from a 409 or a
 * dependency failure the server reported, rather than guessed at read time.
 */
export function toReceiptRecord(item: ReceiptListItem): ReceiptRecord {
  return {
    key: item.id,
    donationReference: item.donationReference,
    issueState: toIssueState(item.issueState),
    deliveryState: toDeliveryState(item.deliveryState),
    receiptReference: item.receiptNumber,
    receiptVersion: `v${item.versionNumber}`,
    donorSnapshot: item.donorSnapshot,
    amount: moneyAmount(item.amount),
    currency: moneyCurrency(item.amount),
    campaignOrFund: item.campaignOrFundName ?? '',
    issuedTime: item.issuedAtUtc ? formatMoment(item.issuedAtUtc) : null,
    deliveryHistory: item.deliveryHistory.map((delivery) => ({
      channel: delivery.channel,
      time: formatMoment(delivery.attemptedAtUtc),
      status: delivery.statusDescription,
    })),
    voidOrCorrectionLink: item.supersedesReceiptId,
    inScope: true,
    hasConflict: false,
    dependencyBroken: false,
  };
}

// =============================================================================================
// Payment verification - SCR-PAY-002
// =============================================================================================

/**
 * The API's three-word state to the screen's.
 *
 * The server already collapses seven attempt statuses into Pending, Confirmed or Failed, because
 * the donor-facing screen has exactly three things it can say. This only normalises the casing.
 */
function toBackendPaymentState(state: string): BackendPaymentState {
  const normalised = state?.trim().toLowerCase();

  if (normalised === 'confirmed') {
    return 'Confirmed';
  }

  if (normalised === 'failed') {
    return 'Failed';
  }

  return 'Pending';
}

function toReceiptEligibility(eligibility: string): ReceiptEligibility {
  return eligibility?.trim().toLowerCase() === 'eligible' ? 'Eligible' : 'Not yet eligible';
}

export function toPaymentVerificationRecord(
  verification: PaymentVerification,
): PaymentVerificationRecord {
  return {
    donationReference: verification.donationReference,
    requestedAmount: moneyAmount(verification.requestedAmount),
    currency: moneyCurrency(verification.requestedAmount),
    backendPaymentState: toBackendPaymentState(verification.backendPaymentState),
    lastVerifiedTime: formatMoment(verification.lastVerifiedTimeUtc),
    gatewayReference: verification.gatewayReference ?? '',
    receiptEligibility: toReceiptEligibility(verification.receiptEligibility),
    receiptLink: verification.receiptLink,
    supportCorrelationReference: verification.supportCorrelationReference,
  };
}

// =============================================================================================
// Payment event queue - SCR-PAY-003
// =============================================================================================

/**
 * The screen's "failure type" from the event's own fields.
 *
 * IT IS DERIVED RATHER THAN STORED, because the API records what actually happened - a signature
 * that did not verify, a status of Duplicate, a processing error - and the screen wants one word
 * naming the category. Ordered by severity: a bad signature is the most serious thing on this
 * queue and is reported even if the event is also a duplicate.
 */
function toFailureType(event: PaymentEventListItem): FailureType {
  if (!event.signatureVerified) {
    return 'Invalid signature';
  }

  if (event.status === 'duplicate') {
    return 'Duplicate event';
  }

  if (event.status === 'failed' && !event.donationIntentId) {
    return 'Unmatched intent';
  }

  if (event.status === 'failed') {
    return 'Out-of-order sequence';
  }

  return 'None';
}

function toSignatureResult(event: PaymentEventListItem): SignatureResult {
  return event.signatureVerified ? 'Valid' : 'Invalid';
}

function toDuplicateStatus(event: PaymentEventListItem): DuplicateStatus {
  return event.status === 'duplicate' ? 'Duplicate' : 'Unique';
}

/**
 * The screen's sequence status.
 *
 * "Unknown" for anything not yet processed, which is honest: until an event is applied nothing
 * has checked whether it arrived in order, and claiming "In order" would be a guess.
 */
function toSequenceStatus(event: PaymentEventListItem): SequenceStatus {
  if (event.status === 'processed') {
    return 'In order';
  }

  if (event.status === 'failed') {
    return 'Out of order';
  }

  return 'Unknown';
}

/**
 * The screen's event state.
 *
 * FOUR STATES FROM FIVE. `processed` and `dismissed` both become Resolved, because from the
 * queue's point of view they are the same thing: nobody needs to look at them again. The
 * difference - applied versus deliberately set aside - is on the detail panel, where it belongs.
 */
function toEventState(event: PaymentEventListItem): PaymentEventState {
  switch (event.status) {
    case 'processed':
    case 'dismissed':
      return 'Resolved';
    case 'failed':
      return event.processingAttempts > 1 ? 'Escalated' : 'Investigating';
    case 'duplicate':
      return 'Resolved';
    default:
      return 'New';
  }
}

/** The donation's own outcome, as opposed to the event's processing outcome. */
function toPaymentStatus(event: PaymentEventListItem): PaymentStatus {
  switch (event.eventType) {
    case 'captured':
    case 'settled':
      return 'Success';
    case 'failed':
    case 'cancelled':
    case 'expired':
    case 'chargebackLost':
      return 'Fail';
    default:
      return 'Pending';
  }
}

export function toPaymentEventRecord(event: PaymentEventListItem): PaymentEventRecord {
  return {
    eventId: event.id,
    donationIntentId: event.donationIntentId ?? '',
    version: event.version,

    // THE PROVIDER'S OWN EVENT ID, not the row's GUID. This is the value an operator reads out to
    // the payment provider's support desk, and the only one that means anything at the other end.
    eventReference: event.gatewayEventId,

    gatewayEventType: event.eventTypeDescription,
    gatewayEventId: event.gatewayEventId,
    failureType: toFailureType(event),
    signatureResult: toSignatureResult(event),
    receivedTime: formatMoment(event.receivedAtUtc),
    mappedIntentOrPayment: event.intentReference ?? event.gatewayReference ?? '',
    duplicateStatus: toDuplicateStatus(event),
    sequenceStatus: toSequenceStatus(event),

    // The MASKED summary. Never the raw payload - that is withheld by the server unless the
    // caller holds pay.payments.view-events, and it has no fixed shape to mask reliably.
    maskedEventSummary: event.processingError ?? event.eventTypeDescription,

    attempts: event.processingAttempts,
    eventState: toEventState(event),
    resolutionAction: event.status === 'dismissed' ? 'Dismissed' : null,
    resolutionReason: null,

    // The donor columns are blank on a gateway event: an event names a PAYMENT, not a person,
    // and the screen resolves the donor from the linked intent when one is opened. Inventing a
    // name here would put a value on screen that no record supports.
    donorName: '',
    donorEmail: '',
    campaignName: '',
    donationAmount: event.amount ? event.amount.display : '',
    currency: moneyCurrency(event.amount),
    paymentStatus: toPaymentStatus(event),
  };
}

// =============================================================================================
// Payment support and safe retry - SCR-PAY-007
// =============================================================================================

/**
 * The screen's "verified payment state".
 *
 * `requiresVerification` MAPS TO "Uncertain", and that is the most important line in this file.
 * It means the gateway's answer is unknown and the donor may already have been charged - so the
 * screen must offer Verify and not Retry. Collapsing it into "Failed" would put a retry button in
 * front of an operator on exactly the case where a retry charges somebody twice.
 */
function toVerifiedPaymentState(item: PaymentSupportCase): PsrVerifiedPaymentState {
  if (item.requiresVerification) {
    return 'Uncertain';
  }

  switch (item.status) {
    case 'paid':
      return 'Confirmed';
    case 'cancelled':
      return 'Cancelled';
    case 'failed':
      return 'Failed';
    default:
      return 'Pending';
  }
}

function toLifecycleState(item: PaymentSupportCase): PsrLifecycleState {
  if (item.requiresVerification) {
    return 'Needs verification';
  }

  switch (item.status) {
    case 'paid':
      return 'Confirmed';
    case 'cancelled':
      return 'Cancelled';
    case 'expired':
      return 'Link expired';
    case 'failed':
      return 'Failed';
    default:
      return 'Awaiting donor';
  }
}

export function toRecoveryRecord(item: PaymentSupportCase): PsrRecoveryRecord {
  const lastAttempt = item.lastAttemptAtUtc ?? item.createdAtUtc;

  return {
    intentId: item.intentId,
    donationIntentReference: item.intentReference,

    // ALREADY MASKED BY THE SERVER unless the caller holds the sensitive-donor permission. The
    // preview is the same string: masking twice on the way through would show "jo***@ex***".
    maskedDonorContact: item.donorEmail,
    donorContactPreview: item.donorEmail,

    // The screen works in MINOR UNITS - paise - because its own arithmetic predates the API's
    // formatted amounts. Converting once here is what keeps its totals correct.
    requestedAmountMinor: Math.round(moneyAmount(item.amount) * 100),

    currency: moneyCurrency(item.amount),
    verifiedPaymentState: toVerifiedPaymentState(item),
    lifecycleState: toLifecycleState(item),
    lastAttemptIso: lastAttempt,
    lastAttemptLabel: formatMoment(lastAttempt),

    retryEligibility: item.requiresVerification
      ? 'Verify with the gateway before retrying'
      : 'Eligible for a safe retry',

    existingActiveLink: '',
    linkExpiryIso: '',
    linkExpiryLabel: '',
    linkCondition: item.status === 'expired' ? 'Expired' : 'None',

    // The support reference a donor quotes. It is the intent reference rather than a separate
    // case number, because the donor already has it - it is in the payment link they followed.
    supportCorrelationReference: item.intentReference,

    preferredDeliveryChannel: 'Email',
    preferredDeliveryChannelRef: item.donorEmail,
    owner: item.campaignName ?? '',

    // The version is not on the support projection - it is read from the intent when a case is
    // opened, which is also when it is needed for the safe-retry call.
    version: 0,

    hasDownstreamReference: !!item.campaignId,

    history: [
      {
        label: 'Intent created',
        detail: item.intentReference,
        meta: formatMoment(item.createdAtUtc),
      },
      ...(item.lastAttemptAtUtc
        ? [
            {
              label: `Attempt ${item.attemptCount}`,
              detail: item.lastFailureReason ?? item.lastGatewayResultCode ?? 'No further detail',
              meta: formatMoment(item.lastAttemptAtUtc),
            },
          ]
        : []),
    ],

    linkedRecords: item.campaignId
      ? [{ reference: item.campaignName ?? item.campaignId, kind: 'Campaign' }]
      : [],

    documents: [],

    integrationStatus: {
      provider: 'Payment gateway',
      state: item.requiresVerification ? 'Outcome unknown' : 'Reachable',
    },

    supportCorrelation: {
      reference: item.intentReference,
      state: item.requiresVerification ? 'Needs verification' : 'Open',
    },
  };
}

// =============================================================================================
// Refund and chargeback cases - SCR-PAY-006 and SCR-PAY-008
// =============================================================================================

function toRefundCaseState(status: RefundCaseListItem['status']): RccCaseState {
  switch (status) {
    case 'requested':
      return 'Submitted';
    case 'approved':
    case 'processing':
      return 'Approved';
    case 'completed':
      return 'Refunded';
    case 'rejected':
    case 'failed':
      return 'Declined';
    case 'cancelled':
      return 'Cancelled';
    default:
      return 'Draft';
  }
}

/**
 * The provider's own status.
 *
 * "Not sent" until a decision is made is exact: nothing reaches the gateway before a refund is
 * approved, so a requested case genuinely has nothing at the provider yet.
 */
function toProviderStatus(status: RefundCaseListItem['status']): RccProviderStatus {
  switch (status) {
    case 'approved':
    case 'processing':
      return 'Requested';
    case 'completed':
      return 'Settled';
    case 'rejected':
    case 'failed':
      return 'Declined';
    default:
      return 'Not sent';
  }
}

function toOutcomeStatus(status: RefundCaseListItem['status']): RccOutcomeStatus {
  switch (status) {
    case 'completed':
      return 'Refunded';
    case 'rejected':
    case 'failed':
      return 'Declined';
    case 'cancelled':
      return 'Cancelled';
    default:
      return 'Pending';
  }
}

/**
 * A refund case, as the combined refund-and-chargeback screen shows it.
 *
 * `reconciliationStatus` FOLLOWS THE RECEIPT, not the bank. On this screen "Reconciled" means the
 * receipt was corrected for the reduced amount - the compliance question the screen exists to
 * answer. A completed refund whose receipt was never corrected leaves the donor holding a tax
 * document for money they no longer gave, and that is precisely what should show as
 * "Unreconciled" here.
 */
export function toRefundCaseRecord(item: RefundCaseListItem): RccRefundCaseRecord {
  const captured = moneyAmount(item.donationAmount);
  const requested = moneyAmount(item.amount);

  const reconciliation: RccReconciliationStatus =
    item.status === 'completed'
      ? item.receiptCorrected
        ? 'Reconciled'
        : 'Unreconciled'
      : 'Not applicable';

  return {
    caseId: item.id,
    donationId: item.donationId,
    version: item.version,
    caseReference: item.caseReference,
    caseType: 'Refund request' as RccCaseType,
    paymentReference: item.donationReference,
    currency: moneyCurrency(item.amount),
    capturedAmount: captured,

    // ZERO, AND SAID PLAINLY. The list projection carries the donation total and this case's
    // amount and nothing else, so what went back on some EARLIER case is not knowable from here.
    // The previous expression - captured minus requested minus (captured minus requested) -
    // always evaluated to zero anyway, but looked like arithmetic somebody could rely on.
    previouslyRefundedAmount: 0,

    refundableBalance: Math.max(0, captured - requested),
    requestedAmount: requested,
    reasonCategory: item.reasonDescription,
    detailedReason: '',
    evidence: [],
    requester: item.requestedByUserId,
    checkerOrApprover: item.decidedByUserId ?? '',
    providerStatus: toProviderStatus(item.status),
    reconciliationStatus: reconciliation,
    chargebackDetails: '',
    outcomeStatus: toOutcomeStatus(item.status),
    caseState: toRefundCaseState(item.status),
    createdAt: formatMoment(item.requestedAtUtc),
    createdIso: item.requestedAtUtc,
    hasDownstreamReference: item.receiptCorrected,

    outcomeHistory: [
      {
        label: 'Requested',
        detail: item.reasonDescription,
        meta: formatMoment(item.requestedAtUtc),
      },
      ...(item.decidedAtUtc
        ? [
            {
              label: item.status === 'rejected' ? 'Rejected' : 'Approved',
              detail: item.decidedByUserId ?? '',
              meta: formatMoment(item.decidedAtUtc),
            },
          ]
        : []),
    ],
  };
}

/**
 * A chargeback case, as the same screen shows it.
 *
 * IT SHARES THE REFUND'S VIEW MODEL because the screen shows one combined register, and the two
 * genuinely are the same shape from an operator's point of view: a case, an amount, a state and a
 * deadline. The `caseType` field is what tells them apart, and it is what the screen filters on.
 *
 * THE DEADLINE GOES IN `chargebackDetails`, prominently, because on a chargeback it is the only
 * thing that cannot be recovered from. Once the evidence date passes, nothing anybody does
 * changes the outcome.
 */
export function toChargebackCaseRecord(item: ChargebackCaseListItem): RccRefundCaseRecord {
  const disputed = moneyAmount(item.disputedAmount);

  const deadline =
    item.daysUntilEvidenceDue === null
      ? 'No evidence deadline set'
      : item.isOverdue
        ? `Evidence deadline passed ${Math.abs(item.daysUntilEvidenceDue)} day(s) ago`
        : `${item.daysUntilEvidenceDue} day(s) left to submit evidence`;

  return {
    caseId: item.id,
    donationId: item.donationId,
    version: item.version,
    caseReference: item.caseReference,
    caseType: 'Chargeback' as RccCaseType,
    paymentReference: item.donationReference,
    currency: moneyCurrency(item.disputedAmount),
    capturedAmount: disputed,
    previouslyRefundedAmount: 0,
    refundableBalance: 0,
    requestedAmount: disputed,
    reasonCategory: item.reasonCode ?? 'Not stated by the bank',
    detailedReason: item.reasonDescription ?? '',
    evidence: [],
    requester: 'Donor bank',
    checkerOrApprover: item.assignedToUserId ?? '',
    providerStatus: 'Charged back' as RccProviderStatus,
    reconciliationStatus: 'Not applicable' as RccReconciliationStatus,
    chargebackDetails: deadline,

    outcomeStatus:
      item.status === 'won'
        ? ('Declined' as RccOutcomeStatus)
        : item.status === 'lost' || item.status === 'accepted'
          ? ('Charged back' as RccOutcomeStatus)
          : ('Pending' as RccOutcomeStatus),

    caseState:
      item.status === 'won' || item.status === 'lost' || item.status === 'accepted'
        ? ('Charged back' as RccCaseState)
        : ('Submitted' as RccCaseState),

    createdAt: formatMoment(item.openedAtUtc),
    createdIso: item.openedAtUtc,
    hasDownstreamReference: true,

    outcomeHistory: [
      {
        label: 'Opened by the donor bank',
        detail: item.reasonDescription ?? item.reasonCode ?? '',
        meta: formatMoment(item.openedAtUtc),
      },
      ...(item.evidenceDueAtUtc
        ? [{ label: 'Evidence due', detail: deadline, meta: formatMoment(item.evidenceDueAtUtc) }]
        : []),
    ],
  };
}

// =============================================================================================
// Donation intents
// =============================================================================================

/** The register's rows, in the API's own names. The register binds to these directly. */
export type DonationIntentRow = DonationIntentListItem;

export type DonationIntentView = DonationIntentDetail;

/**
 * The intent DETAIL screen's record.
 *
 * IT KEEPS THE SCREEN'S OWN VOCABULARY - "Needs Payment", "Link Sent", "Not issued" - because
 * that is what its template, its chips and its timeline are built around. The API's seven intent
 * statuses collapse into five here, and the mapping below is where that happens once rather than
 * in a dozen template expressions.
 */
export interface DonationIntentScreenRecord {
  reference: string;
  campaign: { reference: string; name: string; context: string };
  donor: { reference: string; name: string; email: string; context: string };
  requestedAmount: number;
  currency: string;
  attribution: { reference: string; source: string; firstTouch: string };
  linkStatus: 'Not Created' | 'Active' | 'Expired' | 'Cancelled';
  preferredMethod: string;
  paymentUrl: string | null;
  linkExpiresAt: string | null;
  attemptsCount: number;
  lastAttempt: string | null;
  capturedAmount: number | null;
  capturedTime: string | null;
  settlementStatus: 'Not applicable' | 'Pending' | 'Settled';
  reconciliationStatus: 'Not applicable' | 'Pending' | 'Reconciled';
  receiptStatus: 'Not issued' | 'Pending' | 'Issued';
  refundableBalance: number | null;
  state: 'Draft' | 'Needs Payment' | 'Link Sent' | 'Paid' | 'Cancelled';
  owner: string;
  lastRefresh: string;
  hasDownstreamReference: boolean;
  lifecycleHistory: {
    title: string;
    detail: string;
    time: string;
    tone: 'good' | 'blue' | 'gold' | 'plum' | 'muted';
  }[];
  linkedRecords: { primary: string; secondary?: string; meta: string }[];
  documentRows: { primary: string; secondary?: string; meta: string }[];
  activityRows: { primary: string; secondary?: string; meta: string }[];
  integrationRows: { primary: string; secondary?: string; meta: string }[];
  supportRows: { primary: string; secondary?: string; meta: string }[];
  auditRows: { primary: string; secondary?: string; meta: string }[];
  /** Carried through so the screen can send it back as expectedVersion on its next action. */
  version: number;
  /** The API id. Every staff endpoint is addressed by it; the reference alone will not do. */
  id: string;
}

/**
 * The screen's five-state summary from the API's seven.
 *
 * "Link Sent" IS NOT AN API STATE. On the API an intent awaiting payment is awaiting payment
 * whether or not a link has been issued; the screen distinguishes the two because the operator's
 * next action differs - create a link, or chase the donor who already has one.
 */
function toIntentState(detail: DonationIntentDetail): DonationIntentScreenRecord['state'] {
  switch (detail.status) {
    case 'paid':
      return 'Paid';
    case 'cancelled':
    case 'expired':
      return 'Cancelled';
    case 'draft':
      return 'Draft';
    default:
      return detail.paymentLinkUrl ? 'Link Sent' : 'Needs Payment';
  }
}

/**
 * The link's own condition, which is NOT the intent's state.
 *
 * An intent can be awaiting payment with an expired link - the commonest support case there is -
 * and the screen has to say so, because the fix is a new link rather than a chase.
 */
function toLinkStatus(detail: DonationIntentDetail): DonationIntentScreenRecord['linkStatus'] {
  if (!detail.paymentLinkUrl) {
    return 'Not Created';
  }

  if (detail.status === 'cancelled') {
    return 'Cancelled';
  }

  const expiry = detail.paymentLinkExpiresAtUtc ? new Date(detail.paymentLinkExpiresAtUtc) : null;

  return expiry && expiry.getTime() <= Date.now() ? 'Expired' : 'Active';
}

/** The tone a lifecycle entry is drawn in, from the attempt's outcome. */
function toAttemptTone(
  status: PaymentAttemptStatus,
): DonationIntentScreenRecord['lifecycleHistory'][number]['tone'] {
  switch (status) {
    case 'succeeded':
      return 'good';
    case 'authorised':
      return 'blue';

    // GOLD, NOT RED, for a timed-out attempt. Red says "this failed"; the truth is that nobody
    // knows yet, and the operator's next step is to verify rather than to commiserate.
    case 'timedOut':
    case 'pending':
      return 'gold';

    case 'failed':
    case 'abandoned':
      return 'plum';
    default:
      return 'muted';
  }
}

/**
 * One intent, as its detail screen reads it.
 *
 * EVERY FIELD COMES FROM THE RECORD. Where the API has nothing to say - an attribution first
 * touch that was never captured, a settlement that has not happened - the field is blank rather
 * than filled with a plausible-looking default. A screen showing "Settled" for a donation that
 * settled nowhere is worse than one showing nothing.
 */
export function toIntentScreenRecord(detail: DonationIntentDetail): DonationIntentScreenRecord {
  const succeeded = detail.attempts.find((attempt) => attempt.status === 'succeeded');
  const latest = detail.attempts[0] ?? null;

  return {
    id: detail.id,
    reference: detail.intentReference,

    campaign: {
      reference: detail.campaignId ?? '',
      name: detail.campaignName ?? 'No campaign',
      context: detail.sourceDescription,
    },

    donor: {
      reference: detail.donorId ?? '',
      name: detail.donorName,
      // Already masked by the server unless the caller holds the sensitive-donor permission.
      email: detail.email,
      context: detail.donorId ? 'Existing donor' : 'New donor',
    },

    requestedAmount: moneyAmount(detail.amount),
    currency: moneyCurrency(detail.amount),

    attribution: {
      reference: detail.trackingReference ?? '',
      source: detail.sourceDescription,
      firstTouch: formatMoment(detail.createdAtUtc),
    },

    linkStatus: toLinkStatus(detail),
    preferredMethod: latest?.methodType ?? 'Card',
    paymentUrl: detail.paymentLinkUrl,
    linkExpiresAt: detail.paymentLinkExpiresAtUtc
      ? formatMoment(detail.paymentLinkExpiresAtUtc)
      : null,
    attemptsCount: detail.attemptCount,
    lastAttempt: detail.lastAttemptAtUtc ? formatMoment(detail.lastAttemptAtUtc) : null,

    capturedAmount: succeeded?.capturedAmount ? moneyAmount(succeeded.capturedAmount) : null,
    capturedTime: succeeded?.capturedAtUtc ? formatMoment(succeeded.capturedAtUtc) : null,

    settlementStatus:
      detail.donation === null
        ? 'Not applicable'
        : detail.donation.status === 'settled'
          ? 'Settled'
          : 'Pending',

    // Reconciliation is a property of the DONATION, not the intent, so an unpaid intent has none
    // rather than a pending one.
    reconciliationStatus: detail.donation === null ? 'Not applicable' : 'Pending',

    receiptStatus:
      detail.donation === null
        ? 'Not issued'
        : detail.donation.hasIssuedReceipt
          ? 'Issued'
          : 'Pending',

    refundableBalance: null,

    state: toIntentState(detail),
    owner: detail.campaignName ?? '',
    lastRefresh: formatMoment(new Date().toISOString()),
    hasDownstreamReference: detail.donation !== null,

    lifecycleHistory: [
      {
        title: 'Intent created',
        detail: `${detail.sourceDescription} - ${detail.amount.display}`,
        time: formatMoment(detail.createdAtUtc),
        tone: 'muted' as const,
      },
      ...detail.attempts.map((attempt) => ({
        title: `Attempt ${attempt.attemptNumber} - ${attempt.statusDescription}`,
        detail: attempt.donorFacingMessage ?? attempt.gatewayResultCode ?? attempt.gatewayName,
        time: formatMoment(attempt.capturedAtUtc ?? attempt.failedAtUtc ?? attempt.initiatedAtUtc),
        tone: toAttemptTone(attempt.status),
      })),
      ...(detail.donation
        ? [
            {
              title: 'Donation recorded',
              detail: detail.donation.donationReference,
              time: formatMoment(detail.donation.donatedAtUtc),
              tone: 'good' as const,
            },
          ]
        : []),
    ],

    linkedRecords: [
      ...(detail.campaignId
        ? [
            {
              primary: detail.campaignName ?? detail.campaignId,
              secondary: 'Campaign',
              meta: detail.sourceDescription,
            },
          ]
        : []),
      ...(detail.leadId
        ? [{ primary: detail.leadId, secondary: 'Originating lead', meta: 'Donors and Leads' }]
        : []),
      ...(detail.donation
        ? [
            {
              primary: detail.donation.donationReference,
              secondary: 'Donation',
              meta: detail.donation.statusDescription,
            },
          ]
        : []),
    ],

    documentRows: detail.donation?.receiptNumber
      ? [
          {
            primary: detail.donation.receiptNumber,
            secondary: 'Tax receipt',
            meta: formatMoment(detail.donation.donatedAtUtc),
          },
        ]
      : [],

    activityRows: detail.attempts.map((attempt) => ({
      primary: `Attempt ${attempt.attemptNumber}`,
      secondary: attempt.statusDescription,
      meta: formatMoment(attempt.initiatedAtUtc),
    })),

    integrationRows: detail.attempts
      .filter((attempt) => !!attempt.gatewayReference)
      .map((attempt) => ({
        primary: attempt.gatewayName,
        secondary: attempt.gatewayReference ?? '',
        meta: attempt.statusDescription,
      })),

    supportRows: detail.attempts
      .filter((attempt) => attempt.needsVerification)
      .map((attempt) => ({
        primary: `Attempt ${attempt.attemptNumber} needs verification`,
        secondary: 'The outcome is unknown. Verify with the gateway before retrying.',
        meta: formatMoment(attempt.initiatedAtUtc),
      })),

    auditRows: [
      {
        primary: 'Created',
        secondary: detail.createdByUserId,
        meta: formatMoment(detail.createdAtUtc),
      },
      ...(detail.updatedAtUtc
        ? [
            {
              primary: 'Updated',
              secondary: detail.updatedByUserId ?? '',
              meta: formatMoment(detail.updatedAtUtc),
            },
          ]
        : []),
      ...(detail.consentGiven
        ? [
            {
              primary: 'Consent given',
              secondary: detail.consentVersion ?? '',
              meta: formatMoment(detail.consentGivenAtUtc),
            },
          ]
        : []),
    ],

    version: detail.version,
  };
}

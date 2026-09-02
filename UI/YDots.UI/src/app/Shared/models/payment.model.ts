import { PagedResponse } from './api-response.model';

/**
 * The typed contract for the Donations and Payments service.
 *
 * IT MIRRORS THE SERVER'S DTOs FIELD FOR FIELD, and the field NAMES are copied rather than
 * improved. A name invented on this side is `undefined` at runtime, which is the failure this
 * file exists to prevent: TypeScript cannot check a shape it was told the wrong name for.
 *
 * TWO CONVENTIONS RUN THROUGH THE WHOLE MODULE AND ARE WORTH READING ONCE.
 *
 * `permittedActions` IS THE SERVER'S ANSWER TO "what may this caller do next". It is computed
 * from the record's state AND the caller's permissions AND, on a refund, who raised the case -
 * which is a rule no permission code can express. Render buttons FROM it. A screen that decides
 * for itself will eventually draw a button that answers 409, and the refund case is exactly
 * where that happens: approve is withheld from the person who requested it, whatever permissions
 * they hold.
 *
 * `version` IS THE OPTIMISTIC-CONCURRENCY STAMP. Every state-changing call sends the version it
 * read back as `expectedVersion`, and a screen holding a stale one gets a 409 rather than
 * silently overwriting somebody else's change. On this module that somebody else may have
 * refunded the donation.
 */

// =============================================================================================
// Enumerations - the string values the server serialises
// =============================================================================================

export type DonationIntentStatus =
  | 'draft'
  | 'awaitingPayment'
  | 'paymentInProgress'
  | 'paid'
  | 'failed'
  | 'expired'
  | 'cancelled';

/**
 * `timedOut` IS NOT A FAILURE, and treating it as one is the most expensive mistake this module
 * can make. It means the outcome is UNKNOWN - the donor may already have been charged - so it is
 * resolved by VERIFYING with the gateway, never by retrying.
 */
export type PaymentAttemptStatus =
  | 'initiated'
  | 'pending'
  | 'authorised'
  | 'succeeded'
  | 'failed'
  | 'abandoned'
  | 'timedOut';

export type DonationStatus =
  | 'recorded'
  | 'settled'
  | 'partiallyRefunded'
  | 'refunded'
  | 'chargedBack'
  | 'voided';

export type SettlementStatus = 'pending' | 'settled' | 'onHold' | 'reversed';

export type ReconciliationStatus = 'unreconciled' | 'matched' | 'discrepancy' | 'manuallyResolved';

export type DonationSourceType =
  | 'fundraiserLead'
  | 'qrCode'
  | 'website'
  | 'directLink'
  | 'email'
  | 'social'
  | 'campaignLink'
  | 'offlineEntry';

export type PaymentMethodType =
  | 'card'
  | 'netBanking'
  | 'upi'
  | 'wallet'
  | 'bankTransfer'
  | 'cheque'
  | 'cash'
  | 'directDebit'
  | 'other';

export type PaymentEventStatus = 'pending' | 'processed' | 'duplicate' | 'failed' | 'dismissed';

export type PaymentEventType =
  | 'authorised'
  | 'captured'
  | 'failed'
  | 'cancelled'
  | 'expired'
  | 'refunded'
  | 'partiallyRefunded'
  | 'chargebackOpened'
  | 'chargebackWon'
  | 'chargebackLost'
  | 'settled'
  | 'unknown';

export type ReceiptStatus = 'draft' | 'submitted' | 'pendingReview' | 'issued' | 'corrected' | 'voided';

export type ReceiptDeliveryStatus = 'notSent' | 'pending' | 'delivered' | 'failed';

export type RefundStatus =
  | 'requested'
  | 'approved'
  | 'processing'
  | 'completed'
  | 'rejected'
  | 'failed'
  | 'cancelled';

export type RefundReason =
  | 'donorRequested'
  | 'duplicateCharge'
  | 'incorrectAmount'
  | 'fraudulent'
  | 'campaignCancelled'
  | 'testTransaction'
  | 'other';

export type ChargebackStatus = 'opened' | 'evidenceRequired' | 'underReview' | 'won' | 'lost' | 'accepted';

// =============================================================================================
// Shared shapes
// =============================================================================================

/**
 * An amount as the server sends it.
 *
 * `display` IS PRE-FORMATTED ON THE SERVER, and using it rather than formatting `amount` here is
 * deliberate. Rendering money correctly needs the currency's symbol, its position and its decimal
 * places, all of which live on the IAM currency master. Doing it in the browser would mean every
 * screen fetching that master and reimplementing the rule - and a receipt total that disagrees
 * with the screen by a rounding place is a support call.
 */
export interface MoneyResponse {
  amount: number;
  currencyCode: string;
  display: string;
}

/** Paging input every grid endpoint accepts. */
export interface PaginationRequest {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
}

// =============================================================================================
// Donation intents - SCR-PAY-001, and the public flow of sections 11 to 14 and 19 to 22
// =============================================================================================

/**
 * Starting a donation.
 *
 * ONE REQUEST FOR EVERY ENTRY CHANNEL. A QR scan, a website button, an e-mail link and a
 * fundraiser's lead link differ ONLY in `sourceType`, `trackingReference` and `leadReference`.
 *
 * THERE IS NO organisationId, and there must not be. The organisation is resolved on the server
 * from the tracking reference or the campaign; a field here would let anybody create donations
 * against any charity on the platform.
 */
export interface CreateDonationIntentRequest {
  donorName: string;
  email: string;
  amount: number;
  currencyCode: string;
  mobile?: string | null;
  campaignId?: string | null;
  /** From the QR code or link the donor followed. Resolves to a campaign, channel and source. */
  trackingReference?: string | null;
  sourceType?: DonationSourceType;
  /** The lead this came from, where a fundraiser captured the donor first. */
  leadReference?: string | null;
  taxIdentifier?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  countryId?: string | null;
  stateId?: string | null;
  cityId?: string | null;
  postalCode?: string | null;
  /** Section 11: captured BEFORE the intent is created, not after. */
  consentGiven?: boolean;
  consentVersion?: string | null;
  allowPublicRecognition?: boolean;
  publicRecognitionName?: string | null;
}

export interface DonationIntentResponse {
  id: string;
  intentReference: string;
  status: DonationIntentStatus;
  statusDescription: string;
  amount: MoneyResponse;
  donorName: string;
  email: string;
  mobile: string | null;
  campaignId: string | null;
  campaignName: string | null;
  sourceType: DonationSourceType;
  trackingReference: string | null;
  /**
   * Section 12. Null means the check has not run; true means send the donor to sign in with the
   * intent preserved; false means carry straight on to payment.
   */
  existingDonorMatched: boolean | null;
  paymentLinkUrl: string | null;
  paymentLinkExpiresAtUtc: string | null;
  attemptCount: number;
  createdAtUtc: string;
  version: number;
  permittedActions: string[];
}

/** The answer to section 12's check, on its own. */
export interface ExistingDonorCheckResponse {
  existingDonorFound: boolean;
  /** Masked, always: confirms recognition without confirming the address. */
  maskedEmail: string | null;
  hasActiveAccount: boolean;
  /** SignIn or Continue. */
  nextStep: string;
  message: string;
}

export interface CreatePaymentLinkRequest {
  expectedVersion: number;
  preferredMethod?: string | null;
}

export interface PaymentLinkResponse {
  intentId: string;
  intentReference: string;
  paymentLinkUrl: string;
  expiresAtUtc: string;
  amount: MoneyResponse;
  gatewayName: string;
  attemptNumber: number;
}

export interface CancelDonationIntentRequest {
  expectedVersion: number;
  reason: string;
}

export interface DonationIntentListItem {
  id: string;
  intentReference: string;
  donorName: string;
  /** Masked unless the caller holds pay.donations.view-sensitive-donor. */
  email: string;
  amount: MoneyResponse;
  status: DonationIntentStatus;
  statusDescription: string;
  sourceType: DonationSourceType;
  campaignId: string | null;
  campaignName: string | null;
  attemptCount: number;
  lastAttemptAtUtc: string | null;
  existingDonorMatched: boolean | null;
  createdAtUtc: string;
  version: number;
}

/** One attempt, as the support timeline shows it. */
export interface PaymentAttemptResponse {
  id: string;
  attemptNumber: number;
  status: PaymentAttemptStatus;
  statusDescription: string;
  gatewayName: string;
  gatewayReference: string | null;
  methodType: PaymentMethodType | null;
  maskedInstrument: string | null;
  requestedAmount: MoneyResponse;
  capturedAmount: MoneyResponse | null;
  initiatedAtUtc: string;
  capturedAtUtc: string | null;
  failedAtUtc: string | null;
  gatewayResultCode: string | null;
  /**
   * What to show the DONOR. Deliberately not the gateway's own message, which often names the
   * issuing bank's decline reason - something a donor cannot act on.
   */
  donorFacingMessage: string | null;
  /** True when the outcome is unknown and must be verified rather than retried. */
  needsVerification: boolean;
}

export interface DonationIntentDetail {
  id: string;
  tenantId: string;
  intentReference: string;
  status: DonationIntentStatus;
  statusDescription: string;
  amount: MoneyResponse;
  donorName: string;
  email: string;
  mobile: string | null;
  taxIdentifier: string | null;
  addressLine1: string | null;
  addressLine2: string | null;
  countryId: string | null;
  stateId: string | null;
  cityId: string | null;
  postalCode: string | null;
  campaignId: string | null;
  campaignName: string | null;
  sourceType: DonationSourceType;
  sourceDescription: string;
  trackingReference: string | null;
  trackingAssetId: string | null;
  leadId: string | null;
  donorId: string | null;
  consentGiven: boolean;
  consentVersion: string | null;
  consentGivenAtUtc: string | null;
  allowPublicRecognition: boolean;
  publicRecognitionName: string | null;
  paymentLinkUrl: string | null;
  paymentLinkExpiresAtUtc: string | null;
  existingDonorMatched: boolean | null;
  existingDonorCheckedAtUtc: string | null;
  attemptCount: number;
  lastAttemptAtUtc: string | null;
  failureReason: string | null;
  cancellationReason: string | null;
  /** Section 24: the lifecycle history the intent has to retain, newest first. */
  attempts: PaymentAttemptResponse[];
  donation: DonationSummary | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  permittedActions: string[];
}

export interface DonationIntentSearchFilter extends PaginationRequest {
  status?: DonationIntentStatus | null;
  sourceType?: DonationSourceType | null;
  campaignId?: string | null;
  leadId?: string | null;
  createdFromUtc?: string | null;
  createdToUtc?: string | null;
  /** Failed and not retried - the queue Payment Support works from. */
  needsAttention?: boolean | null;
}

// =============================================================================================
// Donations
// =============================================================================================

export interface DonationSummary {
  id: string;
  donationReference: string;
  amount: MoneyResponse;
  status: DonationStatus;
  statusDescription: string;
  donatedAtUtc: string;
  hasIssuedReceipt: boolean;
  receiptNumber: string | null;
}

export interface DonationListItem {
  id: string;
  donationReference: string;
  donorName: string;
  donorEmail: string;
  amount: MoneyResponse;
  netAmount: MoneyResponse | null;
  status: DonationStatus;
  statusDescription: string;
  settlementStatus: SettlementStatus;
  reconciliationStatus: ReconciliationStatus;
  donatedAtUtc: string;
  methodType: PaymentMethodType | null;
  campaignId: string | null;
  campaignName: string | null;
  sourceType: DonationSourceType;
  hasIssuedReceipt: boolean;
  receiptNumber: string | null;
  hasOpenCase: boolean;
  version: number;
}

export interface DonationDetail {
  id: string;
  tenantId: string;
  donationReference: string;
  donationIntentId: string;
  intentReference: string;
  paymentAttemptId: string;
  donorId: string | null;
  campaignId: string | null;
  campaignName: string | null;
  amount: MoneyResponse;
  gatewayFee: MoneyResponse | null;
  netAmount: MoneyResponse | null;
  refundedAmount: MoneyResponse;
  /** What could still be given back. The refund form's ceiling. */
  refundableAmount: MoneyResponse;
  donorName: string;
  donorEmail: string;
  donorMobile: string | null;
  donorTaxIdentifier: string | null;
  donorAddress: string | null;
  status: DonationStatus;
  statusDescription: string;
  donatedAtUtc: string;
  methodType: PaymentMethodType | null;
  gatewayReference: string | null;
  settlementStatus: SettlementStatus;
  settledAtUtc: string | null;
  settlementBatchReference: string | null;
  reconciliationStatus: ReconciliationStatus;
  reconciledAtUtc: string | null;
  reconciliationNote: string | null;
  sourceType: DonationSourceType;
  sourceDescription: string;
  trackingAssetId: string | null;
  leadId: string | null;
  isReceiptable: boolean;
  receipts: ReceiptSummary[];
  refundCases: RefundCaseSummary[];
  chargebackCases: ChargebackCaseSummary[];
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  permittedActions: string[];
}

/**
 * Recording a gift taken outside the gateway.
 *
 * `receivedAtUtc` IS THE DATE THE MONEY ARRIVED, not today. A cheque banked in April for a gift
 * received in March belongs to March's financial year, and getting that wrong puts the receipt in
 * a tax year the donor cannot claim in.
 */
export interface RecordOfflineDonationRequest {
  donorName: string;
  email: string;
  amount: number;
  currencyCode: string;
  methodType: PaymentMethodType;
  receivedAtUtc: string;
  campaignId?: string | null;
  mobile?: string | null;
  taxIdentifier?: string | null;
  addressLine1?: string | null;
  postalCode?: string | null;
  /** The cheque number or bank transfer reference. What reconciliation matches on. */
  externalReference?: string | null;
  notes?: string | null;
  consentGiven?: boolean;
}

export interface ReconcileDonationRequest {
  expectedVersion: number;
  status: ReconciliationStatus;
  settlementBatchReference?: string | null;
  settledAtUtc?: string | null;
  note?: string | null;
}

export interface DonationStatistics {
  totalCount: number;
  totalAmount: MoneyResponse;
  totalRefunded: MoneyResponse;
  netAmount: MoneyResponse;
  recordedCount: number;
  settledCount: number;
  refundedCount: number;
  chargedBackCount: number;
  awaitingReceiptCount: number;
  unreconciledCount: number;
}

export interface DonationSearchFilter extends PaginationRequest {
  status?: DonationStatus | null;
  settlementStatus?: SettlementStatus | null;
  reconciliationStatus?: ReconciliationStatus | null;
  campaignId?: string | null;
  donorId?: string | null;
  sourceType?: DonationSourceType | null;
  methodType?: PaymentMethodType | null;
  donatedFromUtc?: string | null;
  donatedToUtc?: string | null;
  minimumAmount?: number | null;
  maximumAmount?: number | null;
  awaitingReceipt?: boolean | null;
  hasOpenCase?: boolean | null;
}

// =============================================================================================
// Payment verification, the event queue and safe retry
// =============================================================================================

export interface PaymentVerificationHistoryRow {
  primary: string;
  secondary: string;
  meta: string;
}

export interface PaymentVerification {
  donationReference: string;
  requestedAmount: MoneyResponse;
  /** Pending, Confirmed or Failed. */
  backendPaymentState: string;
  lastVerifiedTimeUtc: string | null;
  gatewayReference: string | null;
  /** Eligible or Not yet eligible. */
  receiptEligibility: string;
  receiptLink: string | null;
  /** What the donor quotes to support. */
  supportCorrelationReference: string;
  history: PaymentVerificationHistoryRow[];
  permittedActions: string[];
}

export interface VerifyPaymentRequest {
  intentReference?: string | null;
  paymentAttemptId?: string | null;
}

export interface PaymentEventListItem {
  id: string;
  eventType: PaymentEventType;
  eventTypeDescription: string;
  status: PaymentEventStatus;
  statusDescription: string;
  gatewayName: string;
  gatewayEventId: string;
  gatewayReference: string | null;
  amount: MoneyResponse | null;
  occurredAtUtc: string;
  receivedAtUtc: string;
  processedAtUtc: string | null;
  /** False means the event could not be proved to come from the gateway. Never acted on. */
  signatureVerified: boolean;
  processingError: string | null;
  processingAttempts: number;
  donationIntentId: string | null;
  intentReference: string | null;
  version: number;

  /** The donor as the intent recorded them. Null when the event matched no intent. */
  donorName: string | null;
  /** Masked by the SERVER unless the caller holds pay.donations.view-sensitive-donor. */
  donorEmail: string | null;
  campaignName: string | null;
  /**
   * Fail | Pending | Success - the DONATION's outcome, which is a different question from
   * `status`. `status` says whether the webhook finished processing; this says whether the
   * donor's money moved, and it is the one the queue triages on.
   */
  paymentOutcome: 'Fail' | 'Pending' | 'Success';
}

export interface PaymentEventDetail extends PaymentEventListItem {
  paymentAttemptId: string | null;
  /** Withheld unless the caller holds pay.payments.view-events. */
  rawPayload: string | null;
  dismissedByUserId: string | null;
  dismissalReason: string | null;
  permittedActions: string[];
}

export interface ReprocessPaymentEventRequest {
  expectedVersion: number;
  note?: string | null;
}

export interface DismissPaymentEventRequest {
  expectedVersion: number;
  reason: string;
}

export interface PaymentEventSearchFilter extends PaginationRequest {
  status?: PaymentEventStatus | null;
  eventType?: PaymentEventType | null;
  gatewayName?: string | null;
  receivedFromUtc?: string | null;
  receivedToUtc?: string | null;
  /** A failed signature is either a misconfiguration or an attempted forgery. */
  signatureFailedOnly?: boolean | null;
  outstandingOnly?: boolean | null;
  /**
   * Fail | Pending | Success - the donation outcome.
   *
   * LEAVING IT UNSET STILL EXCLUDES SUCCESS. The document says a successful payment never
   * reaches this queue, so "no filter" means Fail and Pending together rather than everything.
   */
  paymentOutcome?: 'Fail' | 'Pending' | 'Success' | null;
}

export interface SafeRetryRequest {
  expectedVersion: number;
  reason: string;
}

export interface SafeRetryResponse {
  intentId: string;
  intentReference: string;
  /** Retried, AlreadyPaid, StillPending or Refused. */
  outcome: string;
  message: string;
  paymentLinkUrl: string | null;
  intentStatus: DonationIntentStatus;
  attemptCount: number;
  permittedActions: string[];
}

export interface PaymentSupportCase {
  intentId: string;
  intentReference: string;
  donorName: string;
  donorEmail: string;
  amount: MoneyResponse;
  status: DonationIntentStatus;
  attemptCount: number;
  lastAttemptAtUtc: string | null;
  lastFailureReason: string | null;
  lastGatewayResultCode: string | null;
  /** True when the last attempt's outcome is unknown. These come first in the queue. */
  requiresVerification: boolean;
  campaignId: string | null;
  campaignName: string | null;
  createdAtUtc: string;
}

// =============================================================================================
// Receipts - SCR-PAY-005
// =============================================================================================

export interface IssueReceiptRequest {
  organisationTaxReference?: string | null;
  taxExemptionReference?: string | null;
  deliverImmediately?: boolean;
}

/** A correction is a NEW VERSION, never an edit. The original stays exactly as issued. */
export interface CorrectReceiptRequest {
  expectedVersion: number;
  correctionReason: string;
  donorName?: string | null;
  donorAddress?: string | null;
  donorTaxIdentifier?: string | null;
  deliverImmediately?: boolean;
}

export interface VoidReceiptRequest {
  expectedVersion: number;
  reason: string;
}

export interface ResendReceiptRequest {
  channel?: string;
  /** Null uses the address on the receipt. An override is audited. */
  destination?: string | null;
}

export interface ReceiptSummary {
  id: string;
  receiptNumber: string | null;
  versionNumber: number;
  status: ReceiptStatus;
  statusDescription: string;
  deliveryStatus: ReceiptDeliveryStatus;
  amount: MoneyResponse;
  issuedAtUtc: string | null;
  documentUrl: string | null;
}

export interface ReceiptDeliveryResponse {
  id: string;
  channel: string;
  destination: string;
  status: ReceiptDeliveryStatus;
  statusDescription: string;
  attemptedAtUtc: string;
  deliveredAtUtc: string | null;
  failureReason: string | null;
}

export interface ReceiptListItem {
  id: string;
  donationReference: string;
  issueState: ReceiptStatus;
  issueStateDescription: string;
  deliveryState: ReceiptDeliveryStatus;
  deliveryStateDescription: string;
  receiptNumber: string | null;
  versionNumber: number;
  /** The donor AS PRINTED on the receipt, not as they are today. */
  donorSnapshot: string;
  amount: MoneyResponse;
  campaignOrFundName: string | null;
  issuedAtUtc: string | null;
  financialYear: string;
  deliveryHistory: ReceiptDeliveryResponse[];
  supersedesReceiptId: string | null;
  documentUrl: string | null;
  version: number;
}

export interface ReceiptDetail {
  id: string;
  tenantId: string;
  receiptNumber: string | null;
  versionNumber: number;
  donationId: string;
  donationReference: string;
  supersedesReceiptId: string | null;
  supersedesReceiptNumber: string | null;
  status: ReceiptStatus;
  statusDescription: string;
  deliveryStatus: ReceiptDeliveryStatus;
  financialYear: string;
  amount: MoneyResponse;
  donorName: string;
  donorEmail: string;
  donorAddress: string | null;
  donorTaxIdentifier: string | null;
  campaignOrFundName: string | null;
  organisationTaxReference: string | null;
  taxExemptionReference: string | null;
  issuedAtUtc: string | null;
  issuedByUserId: string | null;
  voidedAtUtc: string | null;
  voidedByUserId: string | null;
  voidReason: string | null;
  correctionReason: string | null;
  documentUrl: string | null;
  deliveries: ReceiptDeliveryResponse[];
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  permittedActions: string[];
}

export interface ReceiptSearchFilter extends PaginationRequest {
  issueState?: ReceiptStatus | null;
  deliveryState?: ReceiptDeliveryStatus | null;
  financialYear?: string | null;
  campaignId?: string | null;
  issuedFromUtc?: string | null;
  issuedToUtc?: string | null;
  /** Issued but never reached the donor. A queue somebody has to work. */
  undeliveredOnly?: boolean | null;
}

// =============================================================================================
// Refunds and chargebacks - SCR-PAY-006 and SCR-PAY-008
// =============================================================================================

export interface RequestRefundRequest {
  amount: number;
  reason: RefundReason;
  reasonDetail?: string | null;
}

export interface DecideRefundRequest {
  expectedVersion: number;
  note?: string | null;
}

export interface RejectRefundRequest {
  expectedVersion: number;
  reason: string;
}

export interface RefundCaseSummary {
  id: string;
  caseReference: string;
  status: RefundStatus;
  statusDescription: string;
  amount: MoneyResponse;
  reason: RefundReason;
  requestedAtUtc: string;
}

export interface RefundCaseListItem {
  id: string;
  caseReference: string;
  donationId: string;
  donationReference: string;
  donorName: string;
  amount: MoneyResponse;
  donationAmount: MoneyResponse;
  status: RefundStatus;
  statusDescription: string;
  reason: RefundReason;
  reasonDescription: string;
  requestedByUserId: string;
  requestedAtUtc: string;
  decidedByUserId: string | null;
  decidedAtUtc: string | null;
  receiptCorrected: boolean;
  version: number;
}

export interface RefundCaseDetail {
  id: string;
  tenantId: string;
  caseReference: string;
  donationId: string;
  donationReference: string;
  donorName: string;
  donorEmail: string;
  amount: MoneyResponse;
  donationAmount: MoneyResponse;
  refundableBalance: MoneyResponse;
  status: RefundStatus;
  statusDescription: string;
  reason: RefundReason;
  reasonDescription: string;
  reasonDetail: string | null;
  requestedByUserId: string;
  requestedAtUtc: string;
  decidedByUserId: string | null;
  decidedAtUtc: string | null;
  decisionNote: string | null;
  rejectionReason: string | null;
  gatewayRefundReference: string | null;
  processedAtUtc: string | null;
  completedAtUtc: string | null;
  gatewayFailureReason: string | null;
  /** A refund without a corrected receipt leaves the donor holding a document for money they no longer gave. */
  receiptCorrected: boolean;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  permittedActions: string[];
}

export interface RefundSearchFilter extends PaginationRequest {
  status?: RefundStatus | null;
  reason?: RefundReason | null;
  donationId?: string | null;
  requestedFromUtc?: string | null;
  requestedToUtc?: string | null;
  openOnly?: boolean | null;
  awaitingReceiptCorrection?: boolean | null;
}

export interface AssignChargebackRequest {
  expectedVersion: number;
  assignToUserId: string;
}

export interface SubmitChargebackEvidenceRequest {
  expectedVersion: number;
  evidenceSummary: string;
  evidenceDocumentUrls?: string | null;
}

export interface ResolveChargebackRequest {
  expectedVersion: number;
  outcome: ChargebackStatus;
  resolutionNote: string;
}

export interface ChargebackCaseSummary {
  id: string;
  caseReference: string;
  status: ChargebackStatus;
  statusDescription: string;
  disputedAmount: MoneyResponse;
  openedAtUtc: string;
  evidenceDueAtUtc: string | null;
  isOverdue: boolean;
}

export interface ChargebackCaseListItem {
  id: string;
  caseReference: string;
  donationId: string;
  donationReference: string;
  donorName: string;
  disputedAmount: MoneyResponse;
  chargebackFee: MoneyResponse | null;
  status: ChargebackStatus;
  statusDescription: string;
  reasonCode: string | null;
  reasonDescription: string | null;
  openedAtUtc: string;
  evidenceDueAtUtc: string | null;
  /** Negative once the deadline has passed. Computed on the server so every client agrees. */
  daysUntilEvidenceDue: number | null;
  isOverdue: boolean;
  assignedToUserId: string | null;
  version: number;
}

export interface ChargebackCaseDetail {
  id: string;
  tenantId: string;
  caseReference: string;
  donationId: string;
  donationReference: string;
  donorName: string;
  donorEmail: string;
  disputedAmount: MoneyResponse;
  chargebackFee: MoneyResponse | null;
  status: ChargebackStatus;
  statusDescription: string;
  gatewayDisputeReference: string | null;
  reasonCode: string | null;
  reasonDescription: string | null;
  openedAtUtc: string;
  evidenceDueAtUtc: string | null;
  daysUntilEvidenceDue: number | null;
  isOverdue: boolean;
  evidenceSubmittedAtUtc: string | null;
  evidenceSubmittedByUserId: string | null;
  evidenceSummary: string | null;
  evidenceDocumentUrls: string[];
  resolvedAtUtc: string | null;
  resolutionNote: string | null;
  assignedToUserId: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  permittedActions: string[];
}

export interface ChargebackSearchFilter extends PaginationRequest {
  status?: ChargebackStatus | null;
  assignedToUserId?: string | null;
  donationId?: string | null;
  /** Open cases past their evidence deadline. The most urgent queue in the module. */
  overdueOnly?: boolean | null;
  openOnly?: boolean | null;
}

// =============================================================================================
// Gateway configuration
// =============================================================================================

/**
 * NO SECRET IS SENT. The request carries the REFERENCE to a key already placed in the server's
 * secret store, never the key itself - a merchant secret in a request body ends up in a request
 * log, a proxy buffer and an exception message.
 */
export interface UpsertGatewayAccountRequest {
  gatewayName: string;
  merchantId: string;
  settlementCurrencyCode: string;
  apiKeyReference?: string | null;
  webhookSecretReference?: string | null;
  isTestMode?: boolean;
  isActive?: boolean;
  returnUrl?: string | null;
  webhookUrl?: string | null;
  paymentLinkValidityMinutes?: number;
  enabledMethods?: string | null;
  notes?: string | null;
  expectedVersion?: number | null;
}

export interface GatewayAccountResponse {
  id: string;
  tenantId: string;
  gatewayName: string;
  merchantId: string;
  settlementCurrencyCode: string;
  /** True when a key reference is configured. The key itself never leaves the server. */
  hasApiKey: boolean;
  hasWebhookSecret: boolean;
  /** Shown prominently: a test account that looks live is how income is reported that never arrived. */
  isTestMode: boolean;
  isActive: boolean;
  returnUrl: string | null;
  webhookUrl: string | null;
  paymentLinkValidityMinutes: number;
  enabledMethods: string[];
  notes: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
  permittedActions: string[];
}

// =============================================================================================
// Helpers
// =============================================================================================

/**
 * Whether the server said this caller may do something.
 *
 * ALWAYS USE THIS RATHER THAN A LOCAL CONDITION. The server's answer folds in the record's state,
 * the caller's permissions and - on a refund - whether they are the person who raised the case.
 * The last one is invisible from here, so a screen deciding for itself would draw an Approve
 * button that answers 409.
 */
export function canPerform(
  permittedActions: readonly string[] | null | undefined,
  action: string,
): boolean {
  return !!permittedActions?.some((candidate) => candidate.toLowerCase() === action.toLowerCase());
}

/**
 * The colour class for a status chip, so the six registers agree with one another.
 *
 * Returns a token the existing theme already defines rather than a hex value - the screens are
 * themed and a hard-coded colour here would be the one thing that did not change with the theme.
 */
export function paymentStatusTone(
  status:
    | DonationIntentStatus
    | DonationStatus
    | PaymentAttemptStatus
    | ReceiptStatus
    | RefundStatus
    | ChargebackStatus
    | PaymentEventStatus,
): 'success' | 'warning' | 'danger' | 'info' | 'muted' {
  switch (status) {
    case 'paid':
    case 'succeeded':
    case 'settled':
    case 'recorded':
    case 'issued':
    case 'completed':
    case 'processed':
    case 'won':
      return 'success';

    case 'awaitingPayment':
    case 'paymentInProgress':
    case 'pending':
    case 'initiated':
    case 'authorised':
    case 'requested':
    case 'approved':
    case 'processing':
    case 'opened':
    case 'evidenceRequired':
    case 'underReview':
    case 'submitted':
    case 'pendingReview':
      return 'warning';

    // timedOut is DANGER rather than warning on purpose: it means the outcome is unknown and the
    // donor may already have been charged, which is the most urgent state in the module.
    case 'timedOut':
    case 'failed':
    case 'chargedBack':
    case 'lost':
    case 'rejected':
      return 'danger';

    case 'refunded':
    case 'partiallyRefunded':
    case 'corrected':
    case 'duplicate':
      return 'info';

    default:
      return 'muted';
  }
}

// =============================================================================================
// The public donation form's presentation configuration
// =============================================================================================

/**
 * What the donor-facing donation page needs in order to render, as distinct from what it needs
 * in order to work.
 *
 * IT CARRIES NO CAMPAIGN LIST, and that absence is the design. A public page must not offer a
 * stranger a menu of campaigns - whose campaigns would they be? The campaign, and with it the
 * organisation the gift belongs to, is resolved by the API from the tracking reference in the
 * link the donor followed. The list stays on the type because the screen renders a chosen
 * campaign, and stays empty because nothing may fill it from here.
 */
export interface PublicDonationFormConfig {
  pageTitle: string;
  pageSubtitle: string;
  operatingTimeZone: string;
  /** Recorded on the intent, so a consent given today is distinguishable from last year's. */
  consentPolicyVersion: string;
  campaigns: readonly { reference: string; name: string; context: string }[];
  currencies: readonly { reference: string; label: string }[];
  geographies: readonly { reference: string; label: string }[];
  permissions: { view: boolean; submit: boolean; continueToPayment: boolean };
  /** A typo guard on the form. The API has its own limit and enforces it. */
  maxDonationAmount: number;
}

// =========================================================================================
// Receipt Register - SCR-PAY-005 as the workflow document describes it
// =========================================================================================

/**
 * One line of the register.
 *
 * IT IS NOT ALWAYS A RECEIPT. The document says "whether a payment ends in Success or Fail, the
 * result is recorded and shown", so the register unions issued receipts with failed donation
 * intents. A failed payment has no `receiptNumber` and no `documentUrl`, because no tax receipt
 * exists for money that never moved - it quotes its donation reference instead.
 */
export interface ReceiptRegisterRow {
  id: string;
  receiptNumber: string | null;
  /** The receipt number, or the donation reference when the payment failed. */
  reference: string;
  receiptDateUtc: string | null;
  /** The donor AS PRINTED on the receipt, not as they are today. */
  donorSnapshot: string;
  amount: MoneyResponse;
  status: 'Success' | 'Failed';
  campaignOrFundName: string | null;
  documentUrl: string | null;
  deliveryState: string;
}

/** The four cards across the top, counted over the whole scope rather than the page. */
export interface ReceiptRegisterSummary {
  totalReceipts: number;
  /** SUCCESSFUL MONEY ONLY. A failed payment moved nothing. */
  totalAmount: MoneyResponse;
  successful: number;
  failed: number;
}

export interface ReceiptRegisterResponse {
  rows: PagedResponse<ReceiptRegisterRow>;
  summary: ReceiptRegisterSummary;
  permittedActions: string[];
}

export interface ReceiptRegisterFilter extends PaginationRequest {
  /** Success | Failed. Unset returns both, which is the register as documented. */
  status?: 'Success' | 'Failed' | null;
  campaignId?: string | null;
  fromUtc?: string | null;
  toUtc?: string | null;
}

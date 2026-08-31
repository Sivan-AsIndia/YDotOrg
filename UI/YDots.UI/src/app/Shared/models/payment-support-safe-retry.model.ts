/**
 * Payment support and safe retry — PAY-UI-07 model types.
 *
 * Faithful implementation of §4.7 of the YDot PAY Practical UI/UX Generation
 * Specification (v1.2, Dark Meadow).
 */

/** Verified payment state — controlled catalogue (§4.7.2). */
export type PsrVerifiedPaymentState = 'Pending' | 'Uncertain' | 'Failed' | 'Confirmed' | 'Cancelled';

/** Lifecycle state — controlled catalogue (§4.7.2). */
export type PsrLifecycleState =
  | 'Needs verification'
  | 'Awaiting donor'
  | 'Link expired'
  | 'Confirmed'
  | 'Cancelled'
  | 'Failed';

/** UI state — controlled catalogue (§4.7.4). */
export type PsrUiState = 'ready' | 'loading' | 'success' | 'no-access' | 'empty' | 'duplicate' | 'conflict' | 'dependency-failure';

/** History entry — audit chronology row (§4.7.1 Related and history). */
export interface PsrHistoryEntry {
  label: string;
  detail: string;
  meta: string;
}

/** Linked record — related record reference (§4.7.1). */
export interface PsrLinkedRecord {
  reference: string;
  kind: string;
}

/** Document — supporting evidence (§4.7.1). */
export interface PsrDocumentItem {
  name: string;
  classification: string;
}

/** Integration status — provider state (§4.7.1). */
export interface PsrIntegrationStatus {
  provider: string;
  state: string;
}

/** Support correlation — open case reference (§4.7.1). */
export interface PsrSupportCorrelation {
  reference: string;
  state: string;
}

/** Recovery record — an incomplete-payment record (§4.7.2). */
export interface PsrRecoveryRecord {
  /**
   * The intent's identifier, which the safe-retry and cancel routes are addressed to.
   *
   * IT IS NOT RENDERED. `donationIntentReference` is what the donor holds and what an operator
   * quotes; this is what the route takes. Sending the reference where the route wants the
   * identifier is a 404 that looks like a missing donation.
   */
  intentId: string;

  donationIntentReference: string;
  maskedDonorContact: string;
  donorContactPreview: string;
  requestedAmountMinor: number;
  currency: string;
  verifiedPaymentState: PsrVerifiedPaymentState;
  lifecycleState: PsrLifecycleState;
  lastAttemptIso: string;
  lastAttemptLabel: string;
  retryEligibility: string;
  existingActiveLink: string;
  linkExpiryIso: string;
  linkExpiryLabel: string;
  linkCondition: 'Active' | 'Expired' | 'None';
  supportCorrelationReference: string;
  preferredDeliveryChannel: string;
  preferredDeliveryChannelRef: string;
  owner: string;
  version: number;
  hasDownstreamReference: boolean;
  history: PsrHistoryEntry[];
  linkedRecords: PsrLinkedRecord[];
  documents: PsrDocumentItem[];
  integrationStatus: PsrIntegrationStatus;
  supportCorrelation: PsrSupportCorrelation;
}

/** Recovery permissions — effective permissions (§4.7.3). */
export interface PsrRecoveryPermissions {
  view: boolean;
  verifyStatus: boolean;
  resendActiveLink: boolean;
  replaceExpiredLink: boolean;
  cancelIntent: boolean;
  openSupportCase: boolean;
}

/** Persistent outcome — confirmed result shown persistently (§4.7.1). */
export interface PsrPersistentOutcome {
  reference: string;
  state: string;
  effectiveTime: string;
  downstreamStatus: string;
  owner: string;
  nextAction: string;
}
/**
 * Refund and chargeback case — SCR-PAY-006 model types.
 *
 * Faithful implementation of §7.6 of the YDot PAY Practical UI/UX Generation
 * Specification (v1.2, Dark Meadow).
 */

/** Case type — controlled choice (§7.6.2). */
export type RccCaseType = 'Refund request' | 'Chargeback';

/** Case state — lifecycle catalogue (§7.6.3 / §5.5). */
export type RccCaseState =
  | 'Draft'
  | 'Submitted'
  | 'Approved'
  | 'Refunded'
  | 'Charged back'
  | 'Reconciled'
  | 'Declined'
  | 'Cancelled';

/** Provider status — controlled choice (§7.6.2). */
export type RccProviderStatus = 'Not sent' | 'Requested' | 'Settled' | 'Declined' | 'Charged back';

/** Reconciliation status — controlled choice (§7.6.2). */
export type RccReconciliationStatus = 'Unreconciled' | 'Reconciled' | 'Not applicable';

/** Outcome status — controlled choice (§7.6.2). */
export type RccOutcomeStatus = 'Pending' | 'Refunded' | 'Charged back' | 'Declined' | 'Cancelled';

/** UI state — controlled catalogue (§7.6.4). */
export type RccUiState = 'ready' | 'loading' | 'success' | 'no-access' | 'empty' | 'duplicate' | 'conflict' | 'dependency-failure';

/** History entry — outcome history row (§7.6.1). */
export interface RccHistoryEntry {
  label: string;
  detail: string;
  meta: string;
}

/** Evidence item — uploaded file with scan/link status (§7.6.2). */
export interface RccEvidenceItem {
  name: string;
  classification: string;
  uploadStatus: string;
  scanStatus: string;
  linkStatus: string;
}

/** Refund case record — a refund/chargeback case (§7.6.2). */
export interface RccRefundCaseRecord {
  /**
   * The case's identifier, which the approve, reject and chargeback routes are addressed to.
   *
   * IT IS NOT RENDERED. `caseReference` is what an operator quotes; this is what the route takes.
   */
  caseId: string;

  /** The donation the case is raised against, for the reconcile action and the refund request. */
  donationId: string;

  /** The case's concurrency stamp, sent back with every decision. */
  version: number;

  caseReference: string;
  caseType: RccCaseType;
  paymentReference: string;
  currency: string;
  capturedAmount: number;
  previouslyRefundedAmount: number;
  refundableBalance: number;
  requestedAmount: number;
  reasonCategory: string;
  detailedReason: string;
  evidence: RccEvidenceItem[];
  requester: string;
  checkerOrApprover: string;
  providerStatus: RccProviderStatus;
  reconciliationStatus: RccReconciliationStatus;
  chargebackDetails: string;
  outcomeStatus: RccOutcomeStatus;
  caseState: RccCaseState;
  createdAt: string;
  createdIso: string;
  hasDownstreamReference: boolean;
  outcomeHistory: RccHistoryEntry[];
}

/** Refund case permissions — effective permissions (§7.6.3). */
export interface RccRefundCasePermissions {
  view: boolean;
  request: boolean;
  submit: boolean;
  approve: boolean;
  reconcile: boolean;
  deleteDraft: boolean;
}

/** Persistent outcome — confirmed result shown persistently (§7.6.1). */
export interface RccPersistentOutcome {
  reference: string;
  state: string;
  effectiveTime: string;
  downstreamStatus: string;
  owner: string;
  nextAction: string;
}
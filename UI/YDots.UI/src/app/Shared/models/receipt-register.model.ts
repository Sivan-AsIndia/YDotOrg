/**
 * Receipt register (SCR-PAY-005) model types.
 *
 * Faithful implementation of §4.5 of the YDot PAY Practical UI/UX Generation
 * Specification (v1.2, Dark Meadow).
 */

/** Issue state — controlled catalogue (§4.5.2). */
export type IssueState = 'Draft' | 'Submitted' | 'Pending review' | 'Issued' | 'Correction' | 'Voided';

/** Delivery state — controlled catalogue (§4.5.2). */
export type DeliveryState = 'Not sent' | 'Pending' | 'Delivered' | 'Failed';

/** UI state — controlled catalogue (§4.5.4 / §4.5.7). */
export type UiState = 'ready' | 'loading' | 'success' | 'no-access' | 'empty' | 'duplicate' | 'conflict' | 'dependency-failure';

/** History row — related and history table row (§4.5.1 Related and history). */
export interface HistoryRow {
  primary: string;
  secondary: string;
  meta: string;
}

/** Delivery history entry — channel delivery record (§4.5.2). */
export interface DeliveryHistoryEntry {
  channel: string;
  time: string;
  status: string;
}

/** Receipt record — a receipt-register row (§4.5.2). */
export interface ReceiptRecord {
  key: string;
  donationReference: string;
  issueState: IssueState;
  deliveryState: DeliveryState;
  receiptReference: string | null;
  receiptVersion: string | null;
  donorSnapshot: string;
  amount: number;
  currency: string;
  campaignOrFund: string;
  issuedTime: string | null;
  deliveryHistory: DeliveryHistoryEntry[];
  voidOrCorrectionLink: string | null;
  inScope: boolean;
  hasConflict: boolean;
  dependencyBroken: boolean;
}

/** Receipt register permissions — effective permissions (§4.5.3). */
export interface ReceiptRegisterPermissions {
  view: boolean;
  generate: boolean;
  resend: boolean;
  voidReissueThroughApproval: boolean;
}

/** Persistent outcome — confirmed result shown persistently (§4.5.1). */
export interface PersistentOutcome {
  reference: string;
  state: string;
  effectiveTime: string;
  downstreamStatus: string;
  owner: string;
  nextAction: string;
}
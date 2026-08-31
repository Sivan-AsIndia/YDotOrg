// ================= Payment Verification shared models =================

/** UI states used across the payment verification screen. */
export type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Backend payment states (4.3.2). */
export type BackendPaymentState = 'Pending' | 'Confirmed' | 'Failed';

/** Receipt eligibility (4.3.2). */
export type ReceiptEligibility = 'Eligible' | 'Not yet eligible';

/** Effective permissions (4.3.3). */
export interface EffectivePermissions {
  readonly view: boolean;
  readonly refreshSafeStatus: boolean;
  readonly retrieveReceiptWhenEligible: boolean;
}

/** One payment verification record (4.3.2 field contract). */
export interface PaymentVerificationRecord {
  readonly donationReference: string;
  readonly requestedAmount: number;
  readonly currency: string;
  readonly backendPaymentState: BackendPaymentState;
  readonly lastVerifiedTime: string;
  readonly gatewayReference: string;
  readonly receiptEligibility: ReceiptEligibility;
  readonly receiptLink: string | null;
  readonly supportCorrelationReference: string;
}

/** History row. */
export interface HistoryRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}

/** Persistent outcome (4.3.1). */
export interface PersistentOutcome {
  readonly reference: string;
  readonly state: string;
  readonly effectiveTime: string;
  readonly downstreamStatus: string;
  readonly owner: string;
  readonly nextAction: string;
}
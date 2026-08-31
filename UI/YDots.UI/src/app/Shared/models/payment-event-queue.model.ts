// ================= Payment Event Queue shared models =================

/** UI states used across the payment event queue screen. */
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

/** Payment event states (4.4.2). */
export type PaymentEventState = 'New' | 'Investigating' | 'Escalated' | 'Resolved';

/** Failure types (4.4.2). */
export type FailureType =
  | 'Invalid signature'
  | 'Unmatched intent'
  | 'Duplicate event'
  | 'Out-of-order sequence'
  | 'None';

/** Signature result (4.4.2). */
export type SignatureResult = 'Valid' | 'Invalid' | 'Not verified';

/** Duplicate status (4.4.2). */
export type DuplicateStatus = 'Unique' | 'Duplicate' | 'Possible duplicate';

/** Sequence status (4.4.2). */
export type SequenceStatus = 'In order' | 'Out of order' | 'Unknown';

/** Payment status — lifecycle of the donation payment (Pending / Success / Fail). */
export type PaymentStatus = 'Pending' | 'Success' | 'Fail';

/** Payment event queue permissions (4.4.3). */
export interface PaymentEventPermissions {
  readonly view: boolean;
  readonly retryCorrelation: boolean;
  readonly resolve: boolean;
  readonly escalate: boolean;
}

/** One payment event queue row (4.4.2 field contract). */
export interface PaymentEventRecord {
  /**
   * The event's identifier, which every write to the API is addressed to.
   *
   * IT IS NOT RENDERED. `eventReference` is what an operator reads and quotes to the payment
   * provider; this is what the route takes. Keeping both means the queue never has to put a GUID
   * in front of somebody in order to be able to reprocess a row.
   */
  readonly eventId: string;

  /** The linked donation intent, when the event correlated to one. Blank when it did not. */
  readonly donationIntentId: string;

  /** The intent's concurrency stamp at the time of reading, sent back with every write. */
  readonly version: number;

  readonly eventReference: string;
  readonly gatewayEventType: string;
  readonly gatewayEventId: string;
  readonly failureType: FailureType;
  readonly signatureResult: SignatureResult;
  readonly receivedTime: string;
  readonly mappedIntentOrPayment: string;
  readonly duplicateStatus: DuplicateStatus;
  readonly sequenceStatus: SequenceStatus;
  readonly maskedEventSummary: string;
  readonly attempts: number;
  readonly eventState: PaymentEventState;
  readonly resolutionAction: string | null;
  readonly resolutionReason: string | null;

  // ---- Donation submission fields (public donation initiation) ----
  /** Donor full name from the public donation initiation form. */
  readonly donorName: string;
  /** Donor email or mobile from the public donation initiation form. */
  readonly donorEmail: string;
  /** Selected campaign or appeal name. */
  readonly campaignName: string;
  /** Donation amount (string as entered). */
  readonly donationAmount: string;
  /** Currency label (e.g. INR). */
  readonly currency: string;
  /** Payment lifecycle status — Pending until the donor pays, then Success / Fail. */
  readonly paymentStatus: PaymentStatus;
}

/** Related tab. */
export interface RelatedTab {
  readonly key: string;
  readonly label: string;
  readonly rows: readonly HistoryRow[];
}

/** History row. */
export interface HistoryRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}
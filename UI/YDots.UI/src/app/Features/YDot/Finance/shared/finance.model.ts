// ================= Finance shared models (Section 07 — YDot FIN) =================

/** UI states used across finance components (4.1.4 / 7.x.4). */
export type FinanceUiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Dark Meadow semantic status tones — colour never carries meaning alone (1.2). */
export type StatusTone = 'info' | 'success' | 'warning' | 'danger' | 'muted' | 'gold';

/** A scope-aware selector option with stable reference and disambiguating context. */
export interface ScopeAwareOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
  readonly initials: string;
  readonly tone: string;
}

/** Read-only status badge with text + icon (colour alone never carries meaning). */
export interface StatusBadge {
  readonly label: string;
  readonly tone: StatusTone;
  readonly icon: string;
}

/** Related / history row. */
export interface FinanceHistoryRow {
  readonly primary: string;
  readonly secondary: string;
  readonly meta: string;
}

/** Related tab. */
export interface FinanceRelatedTab {
  readonly key: string;
  readonly label: string;
  readonly rows: readonly FinanceHistoryRow[];
}

/** Persistent outcome (4.1.1 Persistent outcome). */
export interface FinancePersistentOutcome {
  readonly reference: string;
  readonly state: string;
  readonly effectiveTime: string;
  readonly downstreamStatus: string;
  readonly owner: string;
  readonly nextAction: string;
}

// ================= SCR-FIN-001 — Finance workbench =================

/** Lifecycle / queue stage for a workbench record (4.1.2 Current stage). */
export type WorkbenchStage =
  | 'Captured'
  | 'Settlement'
  | 'Reconciliation'
  | 'Refund'
  | 'Exception';

/** One workbench queue row (4.1.2 field contract). */
export interface WorkbenchRecord {
  readonly workReference: string;
  readonly paymentOrSettlementReference: string;
  readonly workQueue: WorkbenchStage;
  readonly age: string;
  readonly priority: 'High' | 'Medium' | 'Low';
  readonly ownerReference: string;
  /** The Maker who prepared/submitted this record — used to block Verify self-approval (Master §3, R06/R07 boundary). */
  readonly preparedByReference: string;
  readonly campaignOrPeriod: string;
  readonly grossAmount: number;
  readonly variance: number;
  readonly currentStage: string;
  readonly slaState: StatusBadge;
  readonly nextAction: string;
  /** Optimistic-concurrency stamp — bumped on every mutation so a stale in-flight action (opened before, submitted after a concurrent change) can be detected and rejected instead of silently overwriting. */
  readonly version: number;
}

/** Finance workbench permissions (4.1.3). */
export interface FinanceWorkbenchPermissions {
  readonly view: boolean;
  readonly match: boolean;
  readonly verify: boolean;
  readonly escalate: boolean;
}

// ================= SCR-FIN-002 — Settlement batch detail =================

/** One settlement line row (4.2.1 Main work). */
export interface SettlementLine {
  readonly lineReference: string;
  readonly paymentReference: string;
  readonly amount: number;
  readonly fee: number;
  readonly tax: number;
  readonly net: number;
  readonly matchState: string;
}

/** Settlement batch detail — read-only summary (4.2.2). */
export interface SettlementBatchSummary {
  readonly settlementBatchReference: string;
  readonly providerAccount: string;
  readonly settlementDate: string;
  readonly bankCreditReference: string;
  readonly grossAmount: number;
  readonly fees: number;
  readonly tax: number;
  readonly netAmount: number;
  readonly lineCount: number;
  readonly matchedAmount: number;
  readonly unmatchedAmount: number;
  readonly variance: number;
  readonly approvalState: StatusBadge;
}

/** Settlement batch detail permissions (4.2.3). */
export interface SettlementBatchPermissions {
  readonly view: boolean;
  readonly import: boolean;
  readonly validate: boolean;
  readonly match: boolean;
  readonly approve: boolean;
}

// ================= SCR-FIN-003 — Offline donation entry =================

/** Instrument catalogue — approved values only (4.3.2 Instrument type). */
export type InstrumentType =
  | 'Cash'
  | 'Cheque'
  | 'Bank Transfer'
  | 'Demand Draft'
  | 'Pay Order'
  | 'Other';

/** Offline donation entry permissions (4.3.3). */
export interface OfflineDonationPermissions {
  readonly view: boolean;
  readonly draft: boolean;
  readonly duplicateCheck: boolean;
  readonly submit: boolean;
  readonly deleteDraft: boolean;
}

// ================= SCR-FIN-004 — Reconciliation workspace =================

/** A suggested match candidate (4.4.2 Selected match / Suggested match score). */
export interface MatchCandidate {
  readonly settlementLine: string;
  readonly bankReference: string;
  readonly grossAmount: number;
  readonly feesAndTax: number;
  readonly netAmount: number;
  readonly score: number;
}

/** Reconciliation workspace permissions (4.4.3). */
export interface ReconciliationPermissions {
  readonly view: boolean;
  readonly autoMatch: boolean;
  readonly manualMatch: boolean;
  readonly split: boolean;
  readonly unmatchWithControl: boolean;
}

// ================= SCR-FIN-005 — Finance exception case =================

/** Exception type catalogue — approved values only (4.5.2 Exception type). */
export type ExceptionType =
  | 'Missing settlement'
  | 'Duplicate payment'
  | 'Variance'
  | 'Fee variance'
  | 'Timing exception'
  | 'Bank reference mismatch';

/** Finance exception permissions (4.5.3). */
export interface FinanceExceptionPermissions {
  readonly view: boolean;
  readonly assign: boolean;
  readonly evidence: boolean;
  readonly correct: boolean;
  readonly resolve: boolean;
}

// ================= SCR-FIN-006 — Period / campaign close =================

/** Closure checklist item (4.6.1 Main work — Run closure checklist). */
export interface ClosureChecklistItem {
  readonly key: string;
  readonly label: string;
  readonly state: 'Complete' | 'Pending' | 'Blocking' | 'Exempt';
  readonly detail: string;
}

/** Period / campaign close permissions (4.6.3). */
export interface PeriodClosePermissions {
  readonly view: boolean;
  readonly validate: boolean;
  readonly signOff: boolean;
}

// ================= FIN-UI-07 — Maker-checker review =================

/** Maker-checker review permissions (4.7.3). */
export interface MakerCheckerPermissions {
  readonly view: boolean;
  readonly approve: boolean;
  readonly returnForCorrection: boolean;
  readonly reject: boolean;
  readonly delegate: boolean;
}

// ================= FIN-UI-08 — Financial correction or reversal =================

/** Financial correction permissions (4.8.3). */
export interface FinancialCorrectionPermissions {
  readonly view: boolean;
  readonly validateImpact: boolean;
  readonly submitCorrection: boolean;
  readonly approve: boolean;
  readonly postReversal: boolean;
  readonly cancelDraft: boolean;
}

// ================= Shared state entities (finance-state.service) =================

/** Reconciliation ledger row (4.4.2 field contract) — named export of the shape already used inline. */
export interface ReconciliationLedgerRow {
  readonly paymentReference: string;
  readonly settlementLine: string;
  readonly bankReference: string;
  readonly paymentState: string;
  readonly settlementState: string;
  readonly grossAmount: number;
  readonly feesAndTax: number;
  readonly netAmount: number;
  readonly variance: number;
  readonly suggestedMatchScore: number;
}

/** Finance exception case record (4.5.2 field contract) — named export of the shape already used inline. */
export interface FinanceExceptionRecord {
  readonly exceptionReference: string;
  readonly exceptionType: ExceptionType;
  readonly affectedPaymentOrBatch: string;
  readonly detectedTime: string;
  readonly amountOrVariance: number;
  readonly riskAndAge: { readonly label: string; readonly tone: string; readonly icon: string };
  readonly ownerReference: string;
  readonly status: string;
  readonly approvalRequirement: { readonly label: string; readonly tone: string };
  /** Set once Evidence is attached — the exception record's own memory of what was attached, not just a screen-local upload state. */
  readonly evidenceFileName?: string;
}

/**
 * A posted compensating action created by Financial Correction/Reversal's "Post reversal"
 * (FIN-UI-08). The original transaction is never overwritten — this is a distinct, linked
 * record so both the original and the compensating entry stay independently available.
 */
export interface ReversalRecord {
  readonly reversalReference: string;
  readonly originalTransactionReference: string;
  readonly correctionType: string;
  readonly affectedAmount: number;
  readonly postedTime: string;
}

/** Offline donation draft (4.3.2 field contract) — single-slot draft held by the Finance state service. */
export interface OfflineDonationDraft {
  readonly instrumentType: InstrumentType | '';
  readonly transactionReference: string;
  readonly donationDate: string;
  readonly amount: number | null;
  readonly currency: string;
  readonly donor: string;
  readonly campaign: string;
  readonly bankAccount: string;
  readonly depositDate: string;
  readonly evidenceFile: string;
  readonly notes: string;
  readonly draftReference: string;
  readonly approvalState: { readonly label: string; readonly tone: string; readonly icon: string };
}
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
  readonly campaignOrPeriod: string;
  readonly grossAmount: number;
  readonly variance: number;
  readonly currentStage: string;
  readonly slaState: StatusBadge;
  readonly nextAction: string;
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
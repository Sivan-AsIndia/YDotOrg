import { Injectable, computed, effect, signal } from '@angular/core';
import { WorkbenchRecord, StatusBadge, SettlementBatchSummary, SettlementLine, OfflineDonationDraft, ReconciliationLedgerRow, FinanceExceptionRecord, ExceptionType, ReversalRecord } from './finance.model';



/**
 * Single shared source of truth for the Finance domain (SCR-FIN-001…006, FIN-UI-07/08).
 *
 * Every Finance screen previously held its own private copy of the sample data as a
 * component-local signal, so an action on one screen never showed up anywhere else and
 * navigating away and back reset the screen to its original hardcoded literal. This
 * service lifts that data one level up into an app-wide singleton (`providedIn: 'root'`,
 * matching `ToastService`'s established signal-based pattern — this codebase has no
 * NgRx/RxJS store) so every screen reads and writes the same records, and the Finance
 * Workbench (SCR-FIN-001) can reflect what happened anywhere else.
 *
 * Every mutating method follows the same shape: read the current record, guard against
 * re-applying an already-applied action, mutate the owning signal exactly once, then
 * patch the matching Workbench row(s) via `syncWorkbenchByReference` so the Workbench
 * reflects the change without the caller having to know about it.
 */
@Injectable({ providedIn: 'root' })
export class FinanceStateService {
  /**
   * Session-only persistence key. A browser reload creates a brand new JS execution
   * context — the singleton service (and every signal in it) gets re-instantiated from
   * scratch — so without this, a reload silently reverted everything to the seeded
   * defaults even though in-app navigation preserved it correctly. `sessionStorage`
   * (not `localStorage`) survives a reload but still clears when the tab closes, so
   * sample/demo data from one session never lingers into a later one.
   */
  private static readonly storageKey = 'ydot-finance-state-v1';

  /**
   * Every screen's "Data updated …" header text and success-banner "effective time" read
   * from a local `lastRefresh` signal that was set once to a fixed literal at component
   * construction and never touched again — so no matter how many actions a user performed,
   * the displayed timestamp stayed frozen at whatever time the page happened to load.
   * Screens call this at the point each action actually succeeds, so the shown time matches
   * when the change really happened. Format matches the existing literals exactly
   * ("Today, 02:30 PM · IST") — same shape, just live instead of hardcoded.
   */
  nowDisplay(): string {
    const d = new Date();
    let hours = d.getHours();
    const minutes = d.getMinutes().toString().padStart(2, '0');
    const ampm = hours >= 12 ? 'PM' : 'AM';
    hours = hours % 12 || 12;
    return `Today, ${hours.toString().padStart(2, '0')}:${minutes} ${ampm} · IST`;
  }

  // ================= SCR-FIN-001 — Finance workbench =================
  private readonly initialWorkbenchRecords: readonly WorkbenchRecord[] = [
    { workReference: 'FIN-WK-2025-1041', paymentOrSettlementReference: 'PAY-2025-8812', workQueue: 'Captured', age: '2 hours', priority: 'High', ownerReference: 'USR-0244', preparedByReference: 'USR-0244', campaignOrPeriod: 'Educate a Child 2025', grossAmount: 25000, variance: 0, currentStage: 'Captured — pending validation', slaState: { label: 'Within SLA', tone: 'success', icon: '✓' }, nextAction: 'Validate', version: 1 },
    { workReference: 'FIN-WK-2025-1042', paymentOrSettlementReference: 'STL-2025-4421', workQueue: 'Settlement', age: '1 day', priority: 'Medium', ownerReference: 'USR-0231', preparedByReference: 'USR-0244', campaignOrPeriod: 'Health Camp Rural Drive', grossAmount: 184000, variance: 1250, currentStage: 'Settlement — variance detected', slaState: { label: 'At risk', tone: 'warning', icon: '!' }, nextAction: 'Investigate variance', version: 1 },
    { workReference: 'FIN-WK-2025-1043', paymentOrSettlementReference: 'PAY-2025-8819', workQueue: 'Reconciliation', age: '3 days', priority: 'High', ownerReference: 'USR-0258', preparedByReference: 'USR-0258', campaignOrPeriod: 'Women Empowerment 2025', grossAmount: 96500, variance: 0, currentStage: 'Reconciliation — ready to match', slaState: { label: 'Within SLA', tone: 'success', icon: '✓' }, nextAction: 'Auto-match', version: 1 },
    { workReference: 'FIN-WK-2025-1044', paymentOrSettlementReference: 'PAY-2025-8830', workQueue: 'Refund', age: '5 days', priority: 'Medium', ownerReference: 'USR-0244', preparedByReference: 'USR-0244', campaignOrPeriod: 'Educate a Child 2025', grossAmount: 5000, variance: 0, currentStage: 'Refund — submitted', slaState: { label: 'At risk', tone: 'warning', icon: '!' }, nextAction: 'Approve refund', version: 1 },
    { workReference: 'FIN-WK-2025-1045', paymentOrSettlementReference: 'STL-2025-4427', workQueue: 'Exception', age: '2 days', priority: 'High', ownerReference: 'USR-0258', preparedByReference: 'USR-0258', campaignOrPeriod: 'FY25 Q2 Period', grossAmount: 42000, variance: 3150, currentStage: 'Exception — missing settlement', slaState: { label: 'Breached', tone: 'danger', icon: '✕' }, nextAction: 'Assign exception', version: 1 },
    { workReference: 'FIN-WK-2025-1046', paymentOrSettlementReference: 'PAY-2025-8841', workQueue: 'Captured', age: '6 hours', priority: 'Low', ownerReference: 'USR-0231', preparedByReference: 'USR-0231', campaignOrPeriod: 'Women Empowerment 2025', grossAmount: 12000, variance: 0, currentStage: 'Captured — pending validation', slaState: { label: 'Within SLA', tone: 'success', icon: '✓' }, nextAction: 'Validate', version: 1 },
    { workReference: 'FIN-WK-2025-1047', paymentOrSettlementReference: 'STL-2025-4432', workQueue: 'Settlement', age: '4 days', priority: 'Medium', ownerReference: 'USR-0244', preparedByReference: 'USR-0258', campaignOrPeriod: 'Educate a Child 2025', grossAmount: 220000, variance: 0, currentStage: 'Settlement — matched', slaState: { label: 'Within SLA', tone: 'success', icon: '✓' }, nextAction: 'Verify', version: 1 },
    { workReference: 'FIN-WK-2025-1048', paymentOrSettlementReference: 'PAY-2025-8852', workQueue: 'Reconciliation', age: '8 days', priority: 'High', ownerReference: 'USR-0258', preparedByReference: 'USR-0244', campaignOrPeriod: 'FY25 Q2 Period', grossAmount: 78000, variance: 2100, currentStage: 'Reconciliation — unmatched amount', slaState: { label: 'Breached', tone: 'danger', icon: '✕' }, nextAction: 'Escalate', version: 1 },
    { workReference: 'FIN-WK-2025-1049', paymentOrSettlementReference: 'PAY-2025-8860', workQueue: 'Refund', age: '1 day', priority: 'Low', ownerReference: 'USR-0231', preparedByReference: 'USR-0231', campaignOrPeriod: 'Health Camp Rural Drive', grossAmount: 2500, variance: 0, currentStage: 'Refund — draft', slaState: { label: 'Within SLA', tone: 'success', icon: '✓' }, nextAction: 'Submit refund', version: 1 },
    { workReference: 'FIN-WK-2025-1050', paymentOrSettlementReference: 'STL-2025-4440', workQueue: 'Exception', age: '3 days', priority: 'Medium', ownerReference: 'USR-0244', preparedByReference: 'USR-0244', campaignOrPeriod: 'Women Empowerment 2025', grossAmount: 64000, variance: 480, currentStage: 'Exception — fee variance', slaState: { label: 'At risk', tone: 'warning', icon: '!' }, nextAction: 'Correct', version: 1 },
  ];
  readonly workbenchRecords = signal<WorkbenchRecord[]>([...this.initialWorkbenchRecords]);

 
  private patchWorkbenchRecord(workReference: string, patch: Partial<WorkbenchRecord>): void {
    this.workbenchRecords.update((rows) =>
      rows.map((r) => (r.workReference === workReference ? { ...r, ...patch, version: r.version + 1 } : r)),
    );
    this.broadcast();
  }

  
  private static readonly slaResolved: StatusBadge = { label: 'Within SLA', tone: 'success', icon: '✓' };

  /** Finds every workbench row for a given business reference and patches it, so actions taken on other screens show up on the Workbench without it knowing about them. */
  private syncWorkbenchByReference(
    reference: string,
    patch: Partial<Pick<WorkbenchRecord, 'currentStage' | 'nextAction' | 'variance' | 'slaState'>>,
  ): void {
    if (!reference) return;
    this.workbenchRecords.update((rows) =>
      rows.map((r) => (r.paymentOrSettlementReference === reference ? { ...r, ...patch, version: r.version + 1 } : r)),
    );
    this.broadcast();
  }

  matchWorkbenchRecord(workReference: string): WorkbenchRecord | null {
    const record = this.workbenchRecords().find((r) => r.workReference === workReference);
    if (!record) return null;
    this.patchWorkbenchRecord(workReference, {
      currentStage: `${record.workQueue} — match proposed`,
      nextAction: 'Awaiting match confirmation',
    });
    return this.workbenchRecords().find((r) => r.workReference === workReference) ?? null;
  }

  verifyWorkbenchRecord(workReference: string): WorkbenchRecord | null {
    const record = this.workbenchRecords().find((r) => r.workReference === workReference);
    if (!record) return null;
    this.patchWorkbenchRecord(workReference, {
      currentStage: `${record.workQueue} — verified`,
      nextAction: 'No further action',
      slaState: FinanceStateService.slaResolved,
    });
    return this.workbenchRecords().find((r) => r.workReference === workReference) ?? null;
  }

  
  verifyWorkbenchRecordIfCurrent(
    workReference: string,
    expectedVersion: number,
  ): { ok: true; record: WorkbenchRecord } | { ok: false; current: WorkbenchRecord | null } {
    const current = this.workbenchRecords().find((r) => r.workReference === workReference) ?? null;
    if (!current) return { ok: false, current: null };
    if (current.version !== expectedVersion) return { ok: false, current };
    const applied = this.verifyWorkbenchRecord(workReference);
    return applied ? { ok: true, record: applied } : { ok: false, current };
  }

  escalateWorkbenchRecord(workReference: string): WorkbenchRecord | null {
    const record = this.workbenchRecords().find((r) => r.workReference === workReference);
    if (!record) return null;
    this.patchWorkbenchRecord(workReference, {
      currentStage: `${record.workQueue} — escalated`,
      nextAction: 'Under exception investigation',
    });
    return this.workbenchRecords().find((r) => r.workReference === workReference) ?? null;
  }


  private readonly initialSettlementBatches: Record<string, { summary: SettlementBatchSummary; lines: SettlementLine[] }> = {
    'STL-2025-4421': {
      summary: {
        settlementBatchReference: 'STL-2025-4421',
        providerAccount: 'ACC-PROV-00214',
        settlementDate: '2025-07-28',
        bankCreditReference: 'BNK-CR-2025-77123',
        grossAmount: 184000,
        fees: 3200,
        tax: 540,
        netAmount: 180260,
        lineCount: 12,
        matchedAmount: 176500,
        unmatchedAmount: 3750,
        variance: 1250,
        approvalState: { label: 'Pending review', tone: 'warning', icon: '!' },
      },
      lines: [
        { lineReference: 'STL-LN-2025-88201', paymentReference: 'PAY-2025-8812', amount: 25000, fee: 350, tax: 60, net: 24590, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-88202', paymentReference: 'PAY-2025-8819', amount: 96500, fee: 1350, tax: 230, net: 94920, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-88203', paymentReference: 'PAY-2025-8830', amount: 55000, fee: 770, tax: 130, net: 54100, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-88204', paymentReference: 'PAY-2025-8841', amount: 12000, fee: 170, tax: 30, net: 11800, matchState: 'Unmatched' },
        { lineReference: 'STL-LN-2025-88205', paymentReference: 'PAY-2025-8852', amount: 3750, fee: 55, tax: 10, net: 3685, matchState: 'Variance' },
        { lineReference: 'STL-LN-2025-88206', paymentReference: 'PAY-2025-8860', amount: 42000, fee: 590, tax: 100, net: 41310, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-88207', paymentReference: 'PAY-2025-8871', amount: 8750, fee: 125, tax: 20, net: 8605, matchState: 'Blocking' },
        { lineReference: 'STL-LN-2025-88208', paymentReference: 'PAY-2025-8882', amount: 5000, fee: 75, tax: 12, net: 4913, matchState: 'Unmatched' },
      ],
    },
    'STL-2025-4432': {
      summary: {
        settlementBatchReference: 'STL-2025-4432',
        providerAccount: 'ACC-PROV-00229',
        settlementDate: '2025-08-01',
        bankCreditReference: 'BNK-CR-2025-77201',
        grossAmount: 220000,
        fees: 3080,
        tax: 528,
        netAmount: 216392,
        lineCount: 4,
        matchedAmount: 216392,
        unmatchedAmount: 0,
        variance: 0,
        approvalState: { label: 'Pending review', tone: 'warning', icon: '!' },
      },
      lines: [
        { lineReference: 'STL-LN-2025-89301', paymentReference: 'PAY-2025-8901', amount: 60000, fee: 840, tax: 144, net: 59016, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-89302', paymentReference: 'PAY-2025-8902', amount: 55000, fee: 770, tax: 132, net: 54098, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-89303', paymentReference: 'PAY-2025-8903', amount: 50000, fee: 700, tax: 120, net: 49180, matchState: 'Matched' },
        { lineReference: 'STL-LN-2025-89304', paymentReference: 'PAY-2025-8904', amount: 55000, fee: 770, tax: 132, net: 54098, matchState: 'Matched' },
      ],
    },
  };
  readonly settlementBatches = signal(this.initialSettlementBatches);
  readonly settlementBatchOptions = computed(() => Object.keys(this.settlementBatches()));

 
  private readonly initialBatchVersions: Record<string, number> = { 'STL-2025-4421': 1, 'STL-2025-4432': 1 };
  readonly batchVersions = signal<Record<string, number>>(this.initialBatchVersions);
  private bumpBatchVersion(ref: string): void {
    this.batchVersions.update((m) => ({ ...m, [ref]: (m[ref] ?? 1) + 1 }));
  }
  readonly settlementBatchVersion = computed(() => this.batchVersions()[this.selectedSettlementBatchRef()] ?? 1);

  private readonly initialSettlementLifecycles: Record<string, string> = {
    'STL-2025-4421': 'Imported',
    'STL-2025-4432': 'Matched',
  };
  readonly settlementLifecycles = signal(this.initialSettlementLifecycles);

  /** Which batch the Settlement Batch Detail screen is currently showing/acting on. */
  readonly selectedSettlementBatchRef = signal<string>('STL-2025-4421');
  selectSettlementBatch(ref: string): void {
    if (this.settlementBatches()[ref]) this.selectedSettlementBatchRef.set(ref);
  }

  readonly settlementSummary = computed(() => this.settlementBatches()[this.selectedSettlementBatchRef()].summary);
  readonly settlementLines = computed(() => this.settlementBatches()[this.selectedSettlementBatchRef()].lines);
  /** Documented lifecycle vocabulary only (Master §"Lifecycle states"). Drives the Persistent outcome "State" field. */
  readonly settlementLifecycle = computed(() => this.settlementLifecycles()[this.selectedSettlementBatchRef()] ?? 'Imported');

  /** Total settled across every batch, not just the one currently selected on screen — read by Period/Campaign Close. */
  readonly totalSettledNetAmount = computed(() =>
    Object.values(this.settlementBatches()).reduce((sum, b) => sum + b.summary.netAmount, 0),
  );

  importSettlementBatch(): SettlementBatchSummary {
    const ref = this.selectedSettlementBatchRef();
    this.settlementLifecycles.update((m) => ({ ...m, [ref]: 'Imported' }));
    this.bumpBatchVersion(ref);
    this.syncWorkbenchByReference(ref, { currentStage: 'Settlement — imported', nextAction: 'Validate' });
    return this.settlementSummary();
  }

  /** Blocks continuation when a blocking line exists (4.2 Validate), and opens a real, investigable exception case for it (not just a redirect to an empty screen). */
  validateSettlementBatch(): { blocking: boolean } {
    const ref = this.selectedSettlementBatchRef();
    const blockingLine = this.settlementBatches()[ref].lines.find((l) => l.matchState === 'Blocking');
    const blocking = !!blockingLine;
    this.settlementLifecycles.update((m) => ({ ...m, [ref]: blocking ? 'Validation failed' : 'Ready to match' }));
    this.bumpBatchVersion(ref);
    this.syncWorkbenchByReference(ref, {
      currentStage: blocking ? 'Settlement — validation failed' : 'Settlement — ready to match',
      nextAction: blocking ? 'Resolve blocking validation issues' : 'Match',
    });
    if (blockingLine) {
      this.ensureExceptionOpened(ref, 'Bank reference mismatch', blockingLine.net);
    }
    return { blocking };
  }

  /** Refuses to match while a blocking validation issue is outstanding (4.2 Validate → Match). */
  matchSettlementBatch(): { allMatched: boolean; blocked: boolean } {
    const ref = this.selectedSettlementBatchRef();
    if (this.settlementLifecycles()[ref] === 'Validation failed') {
      return { allMatched: false, blocked: true };
    }
    const lines = this.settlementBatches()[ref].lines;
    const allMatched = lines.every((l) => l.matchState === 'Matched');
    const matchedAmount = lines.filter((l) => l.matchState === 'Matched').reduce((sum, l) => sum + l.net, 0);
    const unmatchedAmount = lines.filter((l) => l.matchState !== 'Matched').reduce((sum, l) => sum + l.net, 0);
    this.settlementBatches.update((b) => ({ ...b, [ref]: { ...b[ref], summary: { ...b[ref].summary, matchedAmount, unmatchedAmount } } }));
    this.settlementLifecycles.update((m) => ({ ...m, [ref]: allMatched ? 'Matched' : 'Partially matched' }));
    this.bumpBatchVersion(ref);
    this.syncWorkbenchByReference(ref, {
      currentStage: allMatched ? 'Settlement — matched' : 'Settlement — partially matched',
      nextAction: allMatched ? 'Verify' : 'Investigate variance',
    });
    return { allMatched, blocked: false };
  }

  approveSettlementBatch(): SettlementBatchSummary {
    const ref = this.selectedSettlementBatchRef();
    const approved: StatusBadge = { label: 'Approved', tone: 'success', icon: '✓' };
    this.settlementBatches.update((b) => ({ ...b, [ref]: { ...b[ref], summary: { ...b[ref].summary, approvalState: approved } } }));
    this.settlementLifecycles.update((m) => ({ ...m, [ref]: 'Approved' }));
    this.bumpBatchVersion(ref);
    this.syncWorkbenchByReference(ref, { currentStage: 'Settlement — approved', nextAction: 'No further action', slaState: FinanceStateService.slaResolved });
    return this.settlementSummary();
  }

  /**
   * Optimistic-concurrency guard for Approve (4.2.3 Primary action). The caller
   * (Settlement Batch Detail) passes the batch `version` it captured when it opened
   * the Approve dialog. If the batch has since moved on — imported/validated/matched/
   * approved from this screen or another, in this tab or another — the action is
   * rejected and the current summary is returned instead, so the UI can route into a
   * real conflict state rather than silently overwriting newer data.
   */
  approveSettlementBatchIfCurrent(
    expectedVersion: number,
  ): { ok: true; summary: SettlementBatchSummary } | { ok: false; current: SettlementBatchSummary } {
    const ref = this.selectedSettlementBatchRef();
    const currentVersion = this.batchVersions()[ref] ?? 1;
    if (currentVersion !== expectedVersion) {
      return { ok: false, current: this.settlementSummary() };
    }
    return { ok: true, summary: this.approveSettlementBatch() };
  }

  // ================= SCR-FIN-003 — Offline donation entry =================
  /** Exactly one draft slot at a time (4.3 "Save exactly one draft"). Null once submitted. */
  readonly donationDraft = signal<OfflineDonationDraft | null>(null);

  saveDonationDraft(draft: OfflineDonationDraft): OfflineDonationDraft {
    this.donationDraft.set(draft);
    return draft;
  }

  deleteDonationDraft(): void {
    this.donationDraft.set(null);
  }

  /**
   * Commits the draft once: pushes a new Captured workbench record and a new reconciliation
   * ledger row (so the donation is reconcilable — 4.1 "resulting captured donation must
   * appear in the appropriate Finance Workbench captured data"), then clears the draft slot.
   */
  submitDonation(draft: OfflineDonationDraft, campaignLabel: string): void {
    const workReference = `FIN-WK-${draft.draftReference}`;
    this.workbenchRecords.update((rows) => {
      if (rows.some((r) => r.workReference === workReference)) return rows;
      const newRecord: WorkbenchRecord = {
        workReference,
        paymentOrSettlementReference: draft.transactionReference,
        workQueue: 'Captured',
        age: '0 hours',
        priority: 'Medium',
        ownerReference: 'USR-0244',
        preparedByReference: 'USR-0244',
        campaignOrPeriod: campaignLabel,
        grossAmount: draft.amount ?? 0,
        variance: 0,
        currentStage: 'Captured — pending validation',
        slaState: { label: 'Within SLA', tone: 'success', icon: '✓' },
        nextAction: 'Validate',
        version: 1,
      };
      return [...rows, newRecord];
    });
    this.broadcast();
    this.reconciliationLedger.update((rows) => {
      if (rows.some((r) => r.paymentReference === draft.transactionReference)) return rows;
      const newRow: ReconciliationLedgerRow = {
        paymentReference: draft.transactionReference,
        settlementLine: '—',
        bankReference: draft.bankAccount,
        paymentState: 'Captured',
        settlementState: 'Imported',
        grossAmount: draft.amount ?? 0,
        feesAndTax: 0,
        netAmount: draft.amount ?? 0,
        variance: 0,
        suggestedMatchScore: 0,
      };
      return [...rows, newRow];
    });
    this.donationDraft.set(null);
  }

  // ================= SCR-FIN-004 — Reconciliation workspace =================
  private readonly initialLedgerRows: readonly ReconciliationLedgerRow[] = [
    { paymentReference: 'PAY-2025-8812', settlementLine: 'STL-LN-2025-88201', bankReference: 'BNK-CR-2025-77123', paymentState: 'Matched', settlementState: 'Matched', grossAmount: 25000, feesAndTax: 410, netAmount: 24590, variance: 0, suggestedMatchScore: 98 },
    { paymentReference: 'PAY-2025-8819', settlementLine: 'STL-LN-2025-88202', bankReference: 'BNK-CR-2025-77124', paymentState: 'Unmatched', settlementState: 'Ready to match', grossAmount: 96500, feesAndTax: 1580, netAmount: 94920, variance: 0, suggestedMatchScore: 94 },
    { paymentReference: 'PAY-2025-8830', settlementLine: 'STL-LN-2025-88203', bankReference: 'BNK-CR-2025-77125', paymentState: 'Captured', settlementState: 'Imported', grossAmount: 55000, feesAndTax: 900, netAmount: 54100, variance: 250, suggestedMatchScore: 82 },
    { paymentReference: 'PAY-2025-8841', settlementLine: 'STL-LN-2025-88204', bankReference: 'BNK-CR-2025-77126', paymentState: 'Validated', settlementState: 'Partially matched', grossAmount: 12000, feesAndTax: 200, netAmount: 11800, variance: 0, suggestedMatchScore: 88 },
    { paymentReference: 'PAY-2025-8852', settlementLine: 'STL-LN-2025-88205', bankReference: 'BNK-CR-2025-77127', paymentState: 'Unmatched', settlementState: 'Imported', grossAmount: 3750, feesAndTax: 65, netAmount: 3685, variance: 2100, suggestedMatchScore: 61 },
    { paymentReference: 'PAY-2025-8860', settlementLine: 'STL-LN-2025-88206', bankReference: 'BNK-CR-2025-77128', paymentState: 'Matched', settlementState: 'Matched', grossAmount: 42000, feesAndTax: 690, netAmount: 41310, variance: 0, suggestedMatchScore: 99 },
  ];
  readonly reconciliationLedger = signal<ReconciliationLedgerRow[]>([...this.initialLedgerRows]);

  private patchLedgerRow(paymentReference: string, patch: Partial<ReconciliationLedgerRow>): void {
    this.reconciliationLedger.update((rows) =>
      rows.map((r) => (r.paymentReference === paymentReference ? { ...r, ...patch } : r)),
    );
  }

  /**
   * Real settlement lines not currently claimed by any *other* payment reference — the
   * "Selected match" dropdown's manual alternatives previously offered the same two fixed
   * literals (STL-LN-2025-88203 / -88204) on every row regardless of context, which let two
   * different payments end up claiming the identical settlement line. A line already on the
   * given payment's own row is still offered (it isn't "claimed by someone else"); every
   * other already-matched-elsewhere line is excluded.
   */
  availableSettlementLinesFor(paymentReference: string): SettlementLine[] {
    const claimedByOthers = new Set(
      this.reconciliationLedger()
        .filter((r) => r.paymentReference !== paymentReference && r.settlementLine !== '—')
        .map((r) => r.settlementLine),
    );
    const lines: SettlementLine[] = [];
    for (const bundle of Object.values(this.settlementBatches())) {
      for (const line of bundle.lines) {
        if (!claimedByOthers.has(line.lineReference)) lines.push(line);
      }
    }
    return lines;
  }

  autoMatchLedger(): void {
    // Proposes matches for rows already in a matchable state — persisted result, not a fake refresh.
    const newlyMatched: string[] = [];
    this.reconciliationLedger.update((rows) =>
      rows.map((r) => {
        if (r.paymentState === 'Unmatched' && r.settlementState === 'Ready to match') {
          newlyMatched.push(r.paymentReference);
          return { ...r, paymentState: 'Matched', settlementState: 'Matched', variance: 0 };
        }
        return r;
      }),
    );
    for (const ref of newlyMatched) {
      this.syncWorkbenchByReference(ref, { currentStage: 'Reconciliation — matched', nextAction: 'Verify' });
    }
  }

  /**
   * The real proposed match currently under review — captured *before* the mutation below,
   * so Maker-Checker Review can show a genuine before/after instead of comparing the row's
   * already-matched state against itself. Also carries the settlement line the maker actually
   * chose in the "Selected match" dropdown, which `manualMatchLedger` previously discarded
   * entirely (the row's `settlementLine` never changed no matter what was selected there).
   */
  readonly pendingMatchForReview = signal<{
    paymentReference: string;
    settlementLine: string;
    beforePaymentState: string;
    beforeSettlementState: string;
    netAmount: number;
    suggestedMatchScore: number;
  } | null>(null);

  manualMatchLedger(paymentReference: string, selectedSettlementLine?: string): ReconciliationLedgerRow | null {
    const before = this.reconciliationLedger().find((r) => r.paymentReference === paymentReference);
    const settlementLine = selectedSettlementLine || before?.settlementLine || '—';
    if (before) {
      this.pendingMatchForReview.set({
        paymentReference,
        settlementLine,
        beforePaymentState: before.paymentState,
        beforeSettlementState: before.settlementState,
        netAmount: before.netAmount,
        suggestedMatchScore: before.suggestedMatchScore,
      });
    }
    this.patchLedgerRow(paymentReference, { paymentState: 'Matched', settlementState: 'Matched', settlementLine });
    this.syncWorkbenchByReference(paymentReference, {
      currentStage: 'Reconciliation — match proposed',
      nextAction: 'Awaiting checker review',
    });
    return this.reconciliationLedger().find((r) => r.paymentReference === paymentReference) ?? null;
  }

  /**
   * Split — divides the settlement line actually linked to this ledger row into two lines
   * (the user-entered amount and the remainder), recalculating the batch's real totals
   * (4.4.3 Split) instead of only relabelling the ledger row's status.
   */
  splitLedger(
    paymentReference: string,
    splitAmount: number,
  ): { ok: true; row: ReconciliationLedgerRow } | { ok: false; reason: string } {
    const row = this.reconciliationLedger().find((r) => r.paymentReference === paymentReference);
    if (!row) return { ok: false, reason: 'Record not found.' };
    const lineRef = row.settlementLine;
    const batchRef = lineRef && lineRef !== '—'
      ? Object.keys(this.settlementBatches()).find((ref) => this.settlementBatches()[ref].lines.some((l) => l.lineReference === lineRef))
      : undefined;
    if (!batchRef) return { ok: false, reason: 'No linked settlement line to split.' };
    const original = this.settlementBatches()[batchRef].lines.find((l) => l.lineReference === lineRef)!;
    if (!(splitAmount > 0 && splitAmount < original.net)) {
      return { ok: false, reason: `Enter a split amount between ₹1 and ₹${(original.net - 1).toLocaleString('en-IN')}.` };
    }
    const ratio = splitAmount / original.net;
    const partA: SettlementLine = {
      lineReference: `${original.lineReference}-A`,
      paymentReference: original.paymentReference,
      amount: Math.round(original.amount * ratio),
      fee: Math.round(original.fee * ratio),
      tax: Math.round(original.tax * ratio),
      net: splitAmount,
      matchState: original.matchState,
    };
    const partB: SettlementLine = {
      lineReference: `${original.lineReference}-B`,
      paymentReference: original.paymentReference,
      amount: original.amount - partA.amount,
      fee: original.fee - partA.fee,
      tax: original.tax - partA.tax,
      net: original.net - splitAmount,
      matchState: original.matchState,
    };
    this.settlementBatches.update((b) => {
      const bundle = b[batchRef];
      const lines = bundle.lines.flatMap((l) => (l.lineReference === lineRef ? [partA, partB] : [l]));
      const matchedAmount = lines.filter((l) => l.matchState === 'Matched').reduce((sum, l) => sum + l.net, 0);
      const unmatchedAmount = lines.filter((l) => l.matchState !== 'Matched').reduce((sum, l) => sum + l.net, 0);
      return { ...b, [batchRef]: { ...bundle, lines, summary: { ...bundle.summary, lineCount: lines.length, matchedAmount, unmatchedAmount } } };
    });
    this.bumpBatchVersion(batchRef);
    this.patchLedgerRow(paymentReference, { settlementLine: partA.lineReference, settlementState: 'Partially matched' });
    this.syncWorkbenchByReference(paymentReference, {
      currentStage: 'Reconciliation — split recorded',
      nextAction: 'Review split allocation',
    });
    return { ok: true, row: this.reconciliationLedger().find((r) => r.paymentReference === paymentReference)! };
  }

  /**
   * Reverts the settlement line linked to a ledger row back to Unmatched, and recalculates
   * that batch's matched/unmatched totals — the settlement side of the same relationship
   * the ledger row describes, so Unmatch actually reverses the relationship everywhere it's
   * shown (4.4.3 Unmatch), not just on the Reconciliation Workspace's own row.
   */
  private revertSettlementLineMatch(lineReference: string): void {
    if (!lineReference || lineReference === '—') return;
    const batchRef = Object.keys(this.settlementBatches()).find((ref) =>
      this.settlementBatches()[ref].lines.some((l) => l.lineReference === lineReference),
    );
    if (!batchRef) return;
    this.settlementBatches.update((b) => {
      const bundle = b[batchRef];
      const lines = bundle.lines.map((l) => (l.lineReference === lineReference ? { ...l, matchState: 'Unmatched' } : l));
      const matchedAmount = lines.filter((l) => l.matchState === 'Matched').reduce((sum, l) => sum + l.net, 0);
      const unmatchedAmount = lines.filter((l) => l.matchState !== 'Matched').reduce((sum, l) => sum + l.net, 0);
      return { ...b, [batchRef]: { ...bundle, lines, summary: { ...bundle.summary, matchedAmount, unmatchedAmount } } };
    });
    this.bumpBatchVersion(batchRef);
  }

  unmatchLedger(paymentReference: string): ReconciliationLedgerRow | null {
    const row = this.reconciliationLedger().find((r) => r.paymentReference === paymentReference);
    if (row) {
      this.revertSettlementLineMatch(row.settlementLine);
    }
    this.patchLedgerRow(paymentReference, { paymentState: 'Unmatched', settlementState: 'Ready to match' });
    this.syncWorkbenchByReference(paymentReference, {
      currentStage: 'Reconciliation — unmatched with control',
      nextAction: 'Under exception investigation',
    });
    // Unmatch routes to Exception Case for investigation — open a real case for it instead
    // of landing on an empty "no exceptions match" screen (the row stays re-matchable here
    // in the meantime via Auto-match/Manual match; investigating why it was wrong is separate).
    if (row) {
      this.ensureExceptionOpened(paymentReference, 'Variance', row.netAmount);
    }
    return this.reconciliationLedger().find((r) => r.paymentReference === paymentReference) ?? null;
  }

  /** Reflects a Maker-Checker decision back onto the ledger row that was sent for review (4.7 "reflected on the originating screen"). */
  applyReviewDecisionToLedger(paymentReference: string, decision: 'Approved' | 'Returned for correction' | 'Rejected'): void {
    if (!paymentReference) return;
    const row = this.reconciliationLedger().find((r) => r.paymentReference === paymentReference);
    if (!row) return;
    if (decision === 'Rejected') {
      this.patchLedgerRow(paymentReference, { paymentState: 'Unmatched', settlementState: 'Ready to match' });
      this.syncWorkbenchByReference(paymentReference, { currentStage: 'Reconciliation — match rejected', nextAction: 'Propose a new match' });
    } else if (decision === 'Returned for correction') {
      this.syncWorkbenchByReference(paymentReference, { currentStage: 'Reconciliation — returned for correction', nextAction: 'Awaiting correction' });
    } else {
      this.syncWorkbenchByReference(paymentReference, { currentStage: 'Reconciliation — checker approved', nextAction: 'No further action', slaState: FinanceStateService.slaResolved });
    }
  }

  /** Persists the delegation and reflects it on the Workbench — previously a no-op that only showed a toast (4.7.3 Delegate). */
  delegateReview(linkedFromPaymentRef: string | null): void {
    if (linkedFromPaymentRef) {
      this.syncWorkbenchByReference(linkedFromPaymentRef, {
        currentStage: 'Checker review — delegated',
        nextAction: 'Awaiting new checker',
      });
    }
  }

  // ================= SCR-FIN-005 — Finance exception case =================
  private readonly initialExceptionRecords: readonly FinanceExceptionRecord[] = [
    { exceptionReference: 'EXC-2025-3312', exceptionType: 'Missing settlement', affectedPaymentOrBatch: 'PAY-2025-8852', detectedTime: '2025-07-28T09:15:00', amountOrVariance: 42000, riskAndAge: { label: 'High · 2 days', tone: 'danger', icon: '✕' }, ownerReference: 'USR-0258', status: 'Assigned', approvalRequirement: { label: 'Checker approval required', tone: 'danger' } },
    { exceptionReference: 'EXC-2025-3313', exceptionType: 'Duplicate payment', affectedPaymentOrBatch: 'STL-2025-4427', detectedTime: '2025-07-28T10:00:00', amountOrVariance: 25000, riskAndAge: { label: 'Medium · 8 hours', tone: 'warn', icon: '!' }, ownerReference: 'USR-0244', status: 'Investigation', approvalRequirement: { label: 'Checker approval required', tone: 'warning' } },
    { exceptionReference: 'EXC-2025-3314', exceptionType: 'Variance', affectedPaymentOrBatch: 'PAY-2025-8830', detectedTime: '2025-07-27T16:30:00', amountOrVariance: 1250, riskAndAge: { label: 'Low · 1 day', tone: 'info', icon: 'i' }, ownerReference: 'USR-0231', status: 'Correction', approvalRequirement: { label: 'Auto-resolved eligible', tone: 'success' } },
    { exceptionReference: 'EXC-2025-3315', exceptionType: 'Fee variance', affectedPaymentOrBatch: 'STL-2025-4440', detectedTime: '2025-07-26T08:45:00', amountOrVariance: 480, riskAndAge: { label: 'Medium · 3 days', tone: 'warn', icon: '!' }, ownerReference: 'USR-0258', status: 'Open', approvalRequirement: { label: 'Checker approval required', tone: 'warning' } },
    { exceptionReference: 'EXC-2025-3316', exceptionType: 'Timing exception', affectedPaymentOrBatch: 'PAY-2025-8860', detectedTime: '2025-07-25T14:20:00', amountOrVariance: 5000, riskAndAge: { label: 'Low · 4 days', tone: 'info', icon: 'i' }, ownerReference: 'USR-0244', status: 'Assigned', approvalRequirement: { label: 'Auto-resolved eligible', tone: 'success' } },
  ];
  readonly exceptionRecords = signal<FinanceExceptionRecord[]>([...this.initialExceptionRecords]);

  /**
   * Opens a real, investigable exception for a reference that just failed validation or got
   * unmatched — deterministic reference (derived from the affected reference itself) so
   * repeat clicks never duplicate it. Without this, routing to Exception Case would land on
   * an empty "no exceptions match" screen instead of something the user can actually work.
   */
  private ensureExceptionOpened(affectedReference: string, type: ExceptionType, amount: number): string {
    const suffix = affectedReference.split('-').pop() ?? affectedReference;
    const exceptionReference = `EXC-2025-${suffix}`;
    this.exceptionRecords.update((rows) => {
      if (rows.some((r) => r.exceptionReference === exceptionReference)) return rows;
      const record: FinanceExceptionRecord = {
        exceptionReference,
        exceptionType: type,
        affectedPaymentOrBatch: affectedReference,
        detectedTime: new Date().toISOString(),
        amountOrVariance: amount,
        riskAndAge: { label: 'High · Just detected', tone: 'danger', icon: '✕' },
        ownerReference: 'USR-0244',
        status: 'Open',
        approvalRequirement: { label: 'Checker approval required', tone: 'danger' },
      };
      return [...rows, record];
    });
    return exceptionReference;
  }

  private patchExceptionStatus(exceptionReference: string, status: string): void {
    this.exceptionRecords.update((rows) =>
      rows.map((r) => (r.exceptionReference === exceptionReference ? { ...r, status } : r)),
    );
    const record = this.exceptionRecords().find((r) => r.exceptionReference === exceptionReference);
    if (record) {
      this.syncWorkbenchByReference(record.affectedPaymentOrBatch, {
        currentStage: `Exception — ${status.toLowerCase()}`,
        nextAction: status === 'Resolved' ? 'No further action' : 'Continue investigation',
        ...(status === 'Resolved' ? { slaState: FinanceStateService.slaResolved } : {}),
      });
    }
  }

  /** Assign — persists the selected owner onto the actual exception record (4.5.3), not just the status; the owner must remain visible after refresh/navigation. */
  assignException(exceptionReference: string, ownerReference: string): FinanceExceptionRecord | null {
    this.exceptionRecords.update((rows) =>
      rows.map((r) => (r.exceptionReference === exceptionReference ? { ...r, ownerReference } : r)),
    );
    this.patchExceptionStatus(exceptionReference, 'Assigned');
    return this.exceptionRecords().find((r) => r.exceptionReference === exceptionReference) ?? null;
  }

  correctException(exceptionReference: string): FinanceExceptionRecord | null {
    this.patchExceptionStatus(exceptionReference, 'Correction');
    return this.exceptionRecords().find((r) => r.exceptionReference === exceptionReference) ?? null;
  }

  resolveException(exceptionReference: string): FinanceExceptionRecord | null {
    const record = this.exceptionRecords().find((r) => r.exceptionReference === exceptionReference);
    this.patchExceptionStatus(exceptionReference, 'Resolved');
    // Resolving the exception opened for a blocking settlement line clears the block, so
    // Validate/Match can proceed again (4.2 ↔ 4.5 recovery loop) — matched by what it's
    // actually about (and whichever batch it belongs to, not just the one currently on screen).
    if (record?.exceptionType === 'Bank reference mismatch' && this.settlementBatches()[record.affectedPaymentOrBatch]) {
      this.clearSettlementBlockingLine(record.affectedPaymentOrBatch);
    }
    return this.exceptionRecords().find((r) => r.exceptionReference === exceptionReference) ?? null;
  }

  /** Evidence — links the attached file onto the actual exception record (4.5.2 Evidence field), not just a screen-local upload state; the exception record itself must know which evidence belongs to it. */
  attachExceptionEvidence(exceptionReference: string, fileName: string): FinanceExceptionRecord | null {
    this.exceptionRecords.update((rows) =>
      rows.map((r) => (r.exceptionReference === exceptionReference ? { ...r, evidenceFileName: fileName } : r)),
    );
    const record = this.exceptionRecords().find((r) => r.exceptionReference === exceptionReference);
    if (record) {
      this.syncWorkbenchByReference(record.affectedPaymentOrBatch, {
        currentStage: `Exception — evidence attached`,
        nextAction: 'Continue investigation',
      });
    }
    return record ?? null;
  }

  /** Resolving the exception opened for a blocking settlement line clears the block, so Validate/Match can proceed again (4.2 ↔ 4.5 recovery loop) — operates on whichever batch the exception was actually about. */
  private clearSettlementBlockingLine(batchRef: string): void {
    this.settlementBatches.update((b) => {
      const bundle = b[batchRef];
      if (!bundle) return b;
      return { ...b, [batchRef]: { ...bundle, lines: bundle.lines.map((l) => (l.matchState === 'Blocking' ? { ...l, matchState: 'Unmatched' } : l)) } };
    });
    this.settlementLifecycles.update((m) => ({ ...m, [batchRef]: 'Imported' }));
    this.bumpBatchVersion(batchRef);
    this.syncWorkbenchByReference(batchRef, { currentStage: 'Settlement — blocking issue resolved', nextAction: 'Validate' });
  }

  // ================= FIN-UI-07 — Maker-checker review =================
  /** Persists across navigation so revisiting this screen shows the decision already recorded, not the original literal. Written to directly via `.set()` by the component — it's the same signal, not a copy. */
  readonly reviewLifecycle = signal<string>('Checker review');

  /**
   * Resets the review lifecycle back to its starting state — called by whichever screen
   * (Reconciliation Workspace's Manual match, Financial Correction's Submit) is about to
   * hand a new record to Maker-Checker Review. Without this, a second review in the same
   * session would arrive already carrying the previous review's terminal decision (Approved/
   * Returned/Rejected), which permanently disables Approve/Return/Reject/Delegate since none
   * of those states are in the screen's workflowPermittedStates.
   */
  startReview(): void {
    this.reviewLifecycle.set('Checker review');
  }

  /**
   * The real proposed correction currently under review — Maker-Checker Review's
   * "Before / after impact" card previously showed a hardcoded "₹55,000 → ₹54,750"
   * literal regardless of which correction actually got submitted, so a checker
   * approving a ₹500 correction and a ₹50,000 correction saw the exact same numbers.
   * Financial Correction's Submit populates this with what the maker actually
   * entered; Maker-Checker Review reads it back instead of the literal.
   */
  readonly pendingCorrectionForReview = signal<{
    originalTransactionReference: string;
    correctionType: string;
    affectedAmount: number;
    beforeAmount: number | null;
    reasonCategory: string;
    detailedReason: string;
    evidenceFile: string;
  } | null>(null);

  /**
   * Finds a reference's current amount across whichever record type actually holds it, for
   * a real "before" comparison instead of a guess. Public — both Financial Correction (its
   * own "Correct value" preview) and Maker-Checker Review (the before/after impact card for
   * the same correction) need the identical lookup so their numbers agree.
   */
  findAmountForReference(reference: string): number | null {
    const workbenchRecord = this.workbenchRecords().find((r) => r.paymentOrSettlementReference === reference);
    if (workbenchRecord) return workbenchRecord.grossAmount;
    const ledgerRow = this.reconciliationLedger().find((r) => r.paymentReference === reference);
    if (ledgerRow) return ledgerRow.netAmount;
    for (const batch of Object.values(this.settlementBatches())) {
      const line = batch.lines.find((l) => l.paymentReference === reference);
      if (line) return line.net;
    }
    return null;
  }

  recordCorrectionForReview(request: {
    originalTransactionReference: string;
    correctionType: string;
    affectedAmount: number;
    reasonCategory: string;
    detailedReason: string;
    evidenceFile: string;
  }): void {
    this.pendingCorrectionForReview.set({
      ...request,
      beforeAmount: this.findAmountForReference(request.originalTransactionReference),
    });
  }

  // ================= FIN-UI-08 — Financial correction or reversal =================
  readonly correctionLifecycle = signal<string>('Draft');

  /** Reflects a Maker-Checker decision back on the correction that was sent for review (4.7 "reflected on the originating screen"). */
  applyReviewDecisionToCorrection(decision: 'Approved' | 'Returned for correction' | 'Rejected'): void {
    if (decision === 'Approved') this.correctionLifecycle.set('Ready to post');
    else if (decision === 'Returned for correction') this.correctionLifecycle.set('Draft');
    else this.correctionLifecycle.set('Rejected');
  }

  /**
   * Compensating actions posted for a correction (4.8.3 Post reversal). The original
   * transaction is never overwritten — each entry here is a distinct, linked record, so
   * both the original and every compensating entry stay independently available (4.8
   * "Do NOT overwrite/delete the original posted transaction").
   */
  readonly reversalRecords = signal<ReversalRecord[]>([]);
  reversalsFor(originalTransactionReference: string): ReversalRecord[] {
    return this.reversalRecords().filter((r) => r.originalTransactionReference === originalTransactionReference);
  }

  /**
   * Posts a reversal once: derives a reference from the original transaction so a repeat
   * click against the same original produces the same reversal reference instead of a new
   * record each time (4.8.3 "idempotent within the existing application architecture").
   */
  postReversal(originalTransactionReference: string, correctionType: string, affectedAmount: number): ReversalRecord {
    const reversalReference = `REV-${originalTransactionReference}`;
    const existing = this.reversalRecords().find((r) => r.reversalReference === reversalReference);
    if (existing) {
      this.correctionLifecycle.set('Posted');
      return existing;
    }
    const record: ReversalRecord = {
      reversalReference,
      originalTransactionReference,
      correctionType,
      affectedAmount,
      postedTime: new Date().toISOString(),
    };
    this.reversalRecords.update((rows) => [...rows, record]);
    this.correctionLifecycle.set('Posted');
    // The compensating entry changes the resulting financial truth for the affected record
    // without touching its original captured amount/history (4.8 "preserve linked history").
    this.syncWorkbenchByReference(originalTransactionReference, {
      currentStage: `Correction — reversal posted (${reversalReference})`,
      nextAction: 'No further action',
      slaState: FinanceStateService.slaResolved,
    });
    return record;
  }

  // ================= SCR-FIN-006 — Period / campaign close =================
  readonly closureLifecycle = signal<string>('Validation');

  /**
   * Live cross-tab channel — separate from the sessionStorage persistence below, which
   * stays tab-scoped exactly as before. Every mutating method that bumps a `version`
   * calls `broadcast()` once it's done, so a second tab open on the same record sees
   * the change arrive as a normal signal write within a fraction of a second — the same
   * path a stale-record check reacts to, whether the change came from this tab or another.
   * `applySnapshot(..., { remote: true })` never re-broadcasts what it just received, so
   * two tabs don't bounce the same snapshot back and forth.
   */
  private readonly channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('ydot-finance-state-v1') : null;

  constructor() {
    this.hydrateFromStorage();
    this.channel?.addEventListener('message', (event: MessageEvent) => {
      if (event.data?.type === 'snapshot' && event.data.snapshot) {
        this.applySnapshot(event.data.snapshot, { remote: true });
      }
    });
    // Any change to any domain signal re-serializes the whole snapshot — a single
    // reactive point instead of a persist() call at the end of every mutating method.
    effect(() => {
      const snapshot = this.buildSnapshot();
      try {
        sessionStorage.setItem(FinanceStateService.storageKey, JSON.stringify(snapshot));
      } catch {
        // Storage unavailable (private browsing, quota) — falls back to in-memory-only for this session.
      }
    });
  }

  private buildSnapshot() {
    return {
      workbenchRecords: this.workbenchRecords(),
      settlementBatches: this.settlementBatches(),
      settlementLifecycles: this.settlementLifecycles(),
      batchVersions: this.batchVersions(),
      selectedSettlementBatchRef: this.selectedSettlementBatchRef(),
      donationDraft: this.donationDraft(),
      reconciliationLedger: this.reconciliationLedger(),
      exceptionRecords: this.exceptionRecords(),
      reviewLifecycle: this.reviewLifecycle(),
      pendingCorrectionForReview: this.pendingCorrectionForReview(),
      pendingMatchForReview: this.pendingMatchForReview(),
      correctionLifecycle: this.correctionLifecycle(),
      reversalRecords: this.reversalRecords(),
      closureLifecycle: this.closureLifecycle(),
    };
  }

  /** Pushes the current snapshot to any other open tab so a record it's showing goes live-stale the moment this tab changes it — the mechanism a freshness check reacts to. */
  private broadcast(): void {
    this.channel?.postMessage({ type: 'snapshot', snapshot: this.buildSnapshot() });
  }

  private hydrateFromStorage(): void {
    let raw: string | null;
    try {
      raw = sessionStorage.getItem(FinanceStateService.storageKey);
    } catch {
      return;
    }
    if (!raw) return;
    try {
      this.applySnapshot(JSON.parse(raw), { remote: false });
    } catch {
      // Corrupt/incompatible stored snapshot — keep the seeded defaults already assigned above.
    }
  }

  /**
   * Applies a snapshot from either this tab's own sessionStorage (`remote: false`, on
   * reload) or another tab's live broadcast (`remote: true`). A remote snapshot skips
   * `selectedSettlementBatchRef` and `donationDraft` — which batch you're currently
   * looking at, and an in-progress unsaved draft, are per-tab UI concerns that another
   * tab's activity shouldn't yank out from under you.
   */
  private applySnapshot(snapshot: any, { remote }: { remote: boolean }): void {
    if (snapshot.workbenchRecords) this.workbenchRecords.set(snapshot.workbenchRecords);
    if (snapshot.settlementBatches) this.settlementBatches.set(snapshot.settlementBatches);
    if (snapshot.settlementLifecycles) this.settlementLifecycles.set(snapshot.settlementLifecycles);
    if (snapshot.batchVersions) this.batchVersions.set(snapshot.batchVersions);
    if (snapshot.reconciliationLedger) this.reconciliationLedger.set(snapshot.reconciliationLedger);
    if (snapshot.exceptionRecords) this.exceptionRecords.set(snapshot.exceptionRecords);
    if (snapshot.reviewLifecycle) this.reviewLifecycle.set(snapshot.reviewLifecycle);
    if ('pendingCorrectionForReview' in snapshot) this.pendingCorrectionForReview.set(snapshot.pendingCorrectionForReview);
    if ('pendingMatchForReview' in snapshot) this.pendingMatchForReview.set(snapshot.pendingMatchForReview);
    if (snapshot.correctionLifecycle) this.correctionLifecycle.set(snapshot.correctionLifecycle);
    if (snapshot.reversalRecords) this.reversalRecords.set(snapshot.reversalRecords);
    if (snapshot.closureLifecycle) this.closureLifecycle.set(snapshot.closureLifecycle);
    if (!remote) {
      if (snapshot.selectedSettlementBatchRef) this.selectedSettlementBatchRef.set(snapshot.selectedSettlementBatchRef);
      if ('donationDraft' in snapshot) this.donationDraft.set(snapshot.donationDraft);
    }
  }
}

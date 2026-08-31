import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  FinanceUiState,
  ReconciliationPermissions,
  MatchCandidate,
  ScopeAwareOption,
  SettlementLine,
} from '../../../../Shared/models/finance.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';

/**
 * SCR-FIN-004 — Reconciliation workspace.
 *
 * Faithful implementation of section 4.4 / 7.4 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /money/finance/reconciliation-workspace
 *  Purpose          : Propose and approve one-to-one or permitted grouped matches.
 *  Primary users    : Finance Maker / Checker
 *  View permission  : fin.reconciliation-workspace.view
 *  Primary action   : Auto-match
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress; non-striped tables.
 */

@Component({
  selector: 'app-reconciliation-workspace',
  imports: [CommonModule, FormsModule],
  templateUrl: './reconciliation-workspace.html',
  styleUrl: './reconciliation-workspace.css',
})
export class ReconciliationWorkspaceComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  // ================= Task header (4.4.1) =================
  protected readonly pageTitle = 'Reconciliation Workspace';
  protected readonly pageSubtitle =
    'Propose and approve one-to-one or permitted grouped matches. Every count, search and action is restricted by effective scope.';
  protected readonly lifecycleState = 'Ready to match';
  protected readonly owner = 'Divya Krishnan · Finance Checker';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 04:10 PM · IST');

  /** Effective permissions decided server-side (4.4.3, 4.4.7). */
  protected readonly permissions: ReconciliationPermissions = {
    view: true,
    autoMatch: true,
    manualMatch: true,
    split: true,
    unmatchWithControl: true,
  };

  private readonly workflowPermittedStates = ['Ready to match', 'Partially matched', 'Matched', 'Checker review'];

  // ================= Context and filters (4.4.1 + 4.4.2) =================
  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }
  /**
   * Pre-filled from an incoming handoff: ?donationRef= (Offline donation entry's
   * Submit action) or ?workRef= (Finance workbench's Match action).
   */
  protected readonly searchTerm = signal(
    this.route.snapshot.queryParamMap.get('donationRef') ??
    this.route.snapshot.queryParamMap.get('workRef') ??
    '',
  );
  protected readonly paymentStateFilter = signal('');
  protected readonly paymentStateOptions = ['All states', 'Captured', 'Validated', 'Matched', 'Unmatched'];
  protected readonly settlementStateFilter = signal('');
  protected readonly settlementStateOptions = ['All states', 'Imported', 'Ready to match', 'Partially matched', 'Matched'];
  protected readonly varianceFilter = signal('');
  protected readonly varianceOptions = ['Any variance', 'Zero variance', 'Non-zero variance'];

  protected readonly scopeOptions = [
    'My active organisation (default)',
    'YDot Foundation · National',
    'Southern Region · Tamil Nadu',
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.paymentStateFilter()) chips.push({ key: 'paystate', label: `Payment state: ${this.paymentStateFilter()}` });
    if (this.settlementStateFilter()) chips.push({ key: 'settstate', label: `Settlement state: ${this.settlementStateFilter()}` });
    if (this.varianceFilter()) chips.push({ key: 'variance', label: `Variance: ${this.varianceFilter()}` });
    if (this.searchTerm().trim()) chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    if (this.scopeFilter() !== this.scopeOptions[0]) chips.push({ key: 'scope', label: `Scope: ${this.scopeFilter()}` });
    return chips;
  });

  /**
   * Ledger rows (4.4.2 field contract) — backed by the shared Finance state so Match/
   * Split/Unmatch here persist across navigation and show up on the Finance Workbench,
   * and a just-submitted offline donation appears here ready to reconcile.
   */
  protected readonly ledgerRows = this.financeState.reconciliationLedger;

  /**
   * Suggested match candidate for the selected row (4.4.2 Selected match / Suggested match
   * score) — previously a single hardcoded literal, so the offcanvas panel showed the exact
   * same settlement line, bank reference and amounts no matter which ledger row was actually
   * selected. Every field it needs already lives on the row itself, so this now derives from
   * whichever row is active instead of a fixed sample.
   */
  protected readonly suggestedCandidate = computed<MatchCandidate | null>(() => {
    const row = this.activeLedgerRow();
    if (!row) return null;
    return {
      settlementLine: row.settlementLine,
      bankReference: row.bankReference,
      grossAmount: row.grossAmount,
      feesAndTax: row.feesAndTax,
      netAmount: row.netAmount,
      score: row.suggestedMatchScore,
    };
  });

  /**
   * Real manual alternatives for the "Selected match" dropdown — previously two fixed
   * literals (STL-LN-2025-88203 / -88204) offered on every row, which let two different
   * payments end up claiming the same settlement line. Now sourced from settlement lines
   * not already claimed by another payment, excluding the suggested line itself (already
   * shown as the first option).
   */
  protected readonly manualMatchAlternatives = computed<SettlementLine[]>(() => {
    const row = this.activeLedgerRow();
    if (!row) return [];
    return this.financeState
      .availableSettlementLinesFor(row.paymentReference)
      .filter((l) => l.lineReference !== row.settlementLine);
  });

  /** Maker / Checker / Approval state — read-only (4.4.2). */
  protected readonly maker = 'Arjun Menon · Finance Maker';
  protected readonly checker = 'Divya Krishnan · Finance Checker';
  protected readonly approvalState = { label: 'Pending review', tone: 'warning', icon: '!' };

  protected readonly splitAmount = signal<number | null>(null);

  /** Convert a string (or empty) to number|null — used by the template (4.4.2 Split amount). */
  protected toNumber(value: string | null): number | null {
    return value === '' || value === null ? null : Number(value);
  }
  protected readonly selectedSettlementLine = signal('');
  protected readonly matchReason = signal('');
  protected readonly matchReasonMin = 10;
  protected readonly matchReasonMax = 2000;
  protected readonly matchReasonValid = computed(() => {
    const len = this.matchReason().trim().length;
    return len >= this.matchReasonMin && len <= this.matchReasonMax;
  });
  protected readonly matchReasonCount = computed(() => this.matchReason().trim().length);

  protected readonly filteredRows = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const ps = this.paymentStateFilter();
    const ss = this.settlementStateFilter();
    const vf = this.varianceFilter();
    return this.ledgerRows().filter((r) => {
      if (q && !(r.paymentReference.toLowerCase().includes(q) || r.settlementLine.toLowerCase().includes(q) || r.bankReference.toLowerCase().includes(q))) {
        return false;
      }
      if (ps && ps !== 'All states' && r.paymentState !== ps) return false;
      if (ss && ss !== 'All states' && r.settlementState !== ss) return false;
      if (vf === 'Zero variance' && r.variance !== 0) return false;
      if (vf === 'Non-zero variance' && r.variance === 0) return false;
      return true;
    });
  });

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.paymentStateFilter.set('');
    this.settlementStateFilter.set('');
    this.varianceFilter.set('');
    this.scopeFilter.set(this.scopeOptions[0]);
  }

  // ================= Actions, eligibility and result (4.4.3) =================
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState);

  /**
   * Manual match / Split / Unmatch previously only checked the base permission flag
   * against a hardcoded page-level lifecycleState — never the actually-selected row's
   * real match state. That let Manual match re-match a row that was already Matched,
   * and let Unmatch fire on a row that was never matched in the first place. Both now
   * require the active row to genuinely be in the state each action expects.
   */
  private readonly activeLedgerRow = computed(
    () => this.ledgerRows().find((r) => r.paymentReference === this.activeRow()) ?? null,
  );
  protected readonly autoMatchAllowed = computed(() => this.permissions.autoMatch && this.inWorkflowState());
  protected readonly manualMatchAllowed = computed(() => {
    const row = this.activeLedgerRow();
    return this.permissions.manualMatch && this.inWorkflowState() && !!row && row.paymentState !== 'Matched';
  });
  protected readonly splitAllowed = computed(() => {
    const row = this.activeLedgerRow();
    return this.permissions.split && this.inWorkflowState() && !!row && row.paymentState !== 'Matched';
  });
  protected readonly unmatchAllowed = computed(() => {
    const row = this.activeLedgerRow();
    return this.permissions.unmatchWithControl && this.inWorkflowState() && !!row && row.paymentState === 'Matched';
  });

  /** Why Manual match / Split is disabled — shown as a toast since a native disabled button never fires click. */
  protected readonly matchOrSplitBlockedReason = computed(() => {
    const row = this.activeLedgerRow();
    if (!row) return 'Select a ledger row before matching or splitting.';
    if (row.paymentState === 'Matched') return `${row.paymentReference} is already matched — nothing left to match or split.`;
    return 'This action is not available with your current permissions.';
  });
  /** Why Unmatch is disabled. */
  protected readonly unmatchBlockedReason = computed(() => {
    const row = this.activeLedgerRow();
    if (!row) return 'Select a ledger row before unmatching.';
    if (row.paymentState !== 'Matched') return `${row.paymentReference} isn't matched yet — there's nothing to unmatch.`;
    return 'This action is not available with your current permissions.';
  });

  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  /** No access — "Return to the permitted landing page" (4.4.4). */
  protected returnToWorkspace(): void {
    this.router.navigate(['/app/workspace/my-workspace']);
  }

  // ----- Conflict recovery: Compare / Reapply eligible changes / Cancel (4.4.4 / 4.4.6) -----
  protected compareConflict(): void {
    this.toast.show('Comparing versions', 'Showing the latest version alongside your proposed values.', 'info');
    this.uiState.set('ready');
  }
  protected reapplyConflictChanges(): void {
    this.toast.show('Changes reapplied', 'Your eligible changes have been reapplied to the latest version.', 'success');
    this.uiState.set('ready');
  }
  protected cancelConflict(): void {
    this.uiState.set('ready');
  }

  /** Dependency failure — "Retry only the failed dependency using a stable correlation reference" (4.4.4). */
  protected retryDependency(): void {
    this.toast.show('Retrying', 'Retrying the failed dependency using correlation INT-77334…', 'info');
    this.uiState.set('success');
  }

  protected readonly activeRow = signal<string>('');
  protected selectRow(reference: string): void {
    this.activeRow.set(this.activeRow() === reference ? '' : reference);
    this.selectedSettlementLine.set(this.suggestedCandidate()?.settlementLine ?? '');
  }

  protected performAutoMatch(): void {
    if (!this.autoMatchAllowed()) {
      this.toast.show('Auto-match unavailable', 'This action is not available with your current permissions.', 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.financeState.autoMatchLedger();
    this.toast.show('Auto-match complete', 'Proposed matches refreshed for records in scope.', 'success');
  }

  /** Manual match — requires reason (4.4.3). */
  protected readonly manualMatchDialogOpen = signal(false);
  protected openManualMatch(): void {
    if (!this.manualMatchAllowed()) {
      this.toast.show('Manual match unavailable', this.matchOrSplitBlockedReason(), 'warning');
      return;
    }
    this.matchReason.set('');
    this.manualMatchDialogOpen.set(true);
  }
  protected cancelManualMatch(): void {
    this.manualMatchDialogOpen.set(false);
  }
  /** Confirmed match — matched items go on to independent approval (Master workflow: matched → Maker-Checker Review). */
  protected confirmManualMatch(): void {
    if (!this.matchReasonValid()) return;
    this.manualMatchDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const reference = this.activeRow();
    this.financeState.manualMatchLedger(reference, this.selectedSettlementLine());
    this.financeState.startReview();
    this.toast.show('Match confirmed', `${reference} matched to ${this.selectedSettlementLine()}. Routing to Maker-Checker review for approval.`, 'success');
    this.router.navigate(['/app/fin/maker-checker-review'], { queryParams: { paymentRef: reference } });
  }

  protected performSplit(): void {
    if (!this.splitAllowed()) {
      this.toast.show('Split unavailable', this.matchOrSplitBlockedReason(), 'warning');
      return;
    }
    const amount = this.splitAmount();
    if (amount === null) {
      this.toast.show('Split amount required', 'Enter the split or group amount before confirming.', 'warning');
      return;
    }
    const reference = this.activeRow();
    const result = this.financeState.splitLedger(reference, amount);
    if (!result.ok) {
      this.uiState.set('validation');
      this.toast.show('Split failed', result.reason, 'error');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.splitAmount.set(null);
    this.toast.show('Split recorded', `${reference} split into ${this.formatAmount(amount)} and the remainder.`, 'success');
  }

  /** Unmatch with control — unmatched items are routed to exception investigation (Master workflow: not matched → Exception). */
  protected performUnmatch(): void {
    if (!this.unmatchAllowed()) {
      this.toast.show('Unmatch unavailable', this.unmatchBlockedReason(), 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const reference = this.activeRow();
    this.financeState.unmatchLedger(reference);
    this.toast.show('Unmatch recorded', `${reference} unmatched with control. Routing to Finance exception case.`, 'success');
    this.router.navigate(['/app/money/finance/finance-exception-case'], { queryParams: { paymentRef: reference } });
  }

  // ================= Persistent outcome (4.4.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.activeRow() || '—',
    state: this.lifecycleState,
    effectiveTime: this.lastRefresh(),
    downstreamStatus: `${this.filteredRows().length} rows in view`,
    owner: this.owner,
    nextAction: 'Review and approve proposed matches',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
  protected scoreClass(score: number): string {
    if (score >= 90) return 'rw-score-high';
    if (score >= 75) return 'rw-score-mid';
    return 'rw-score-low';
  }
  protected varianceClass(variance: number): string {
    return variance === 0 ? 'rw-var-zero' : 'rw-var-nonzero';
  }
}
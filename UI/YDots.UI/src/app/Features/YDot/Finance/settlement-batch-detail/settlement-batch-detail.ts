import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';

import {
  FinanceUiState,
  SettlementBatchSummary,
  SettlementLine,
  SettlementBatchPermissions,
} from '../../../../Shared/models/finance.model';
import { FinanceStateService } from '../shared/finance-state.service';

/**
 * SCR-FIN-002 — Settlement batch detail.
 *
 * Faithful implementation of section 4.2 / 7.2 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /money/finance/settlement-batch-detail
 *  Purpose          : Show provider settlement lines, fees, bank evidence and variance.
 *  Primary users    : Finance / Auditor
 *  View permission  : fin.settlement-batch-detail.view
 *  Primary action   : Import
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress; non-striped tables.
 */

@Component({
  selector: 'app-settlement-batch-detail',
  imports: [CommonModule, FormsModule],
  templateUrl: './settlement-batch-detail.html',
  styleUrl: './settlement-batch-detail.css',
})
export class SettlementBatchDetailComponent {
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly financeState = inject(FinanceStateService);

  // ================= Task header (4.2.1) =================
  protected readonly pageTitle = 'Settlement Batch Detail';
  protected readonly pageSubtitle =
    'Show provider settlement lines, fees, bank evidence and variance. Every count and action is restricted by effective scope.';
  /** Documented lifecycle vocabulary, live from the shared Finance state (4.2 "update lifecycle state"). */
  protected get lifecycleState(): string {
    return this.financeState.settlementLifecycle();
  }
  protected readonly owner = 'Arjun Menon · Finance Maker';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 03:10 PM · IST');

  /** Effective permissions decided server-side (4.2.3, 4.2.7). */
  protected readonly permissions: SettlementBatchPermissions = {
    view: true,
    import: true,
    validate: true,
    match: true,
    approve: true,
  };

  // ================= Context and filters (4.2.1) =================
  protected readonly savedViews = ['Batch summary (Default)', 'Exceptions focus', 'All lines'];
  protected readonly savedView = signal(this.savedViews[0]);
  protected readonly searchTerm = signal('');
  protected readonly lineStateFilter = signal<string>('');
  protected readonly lineStateOptions = ['All states', 'Matched', 'Unmatched', 'Variance', 'Blocking'];

  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  protected readonly scopeOptions = [
    'My active organisation (default)',
    'YDot Foundation · National',
    'Southern Region · Tamil Nadu',
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);

  /** Which sample settlement batch this screen is currently showing/acting on. */
  protected readonly batchOptions = this.financeState.settlementBatchOptions;
  protected get selectedBatchRef(): string {
    return this.financeState.selectedSettlementBatchRef();
  }
  protected selectBatch(ref: string): void {
    this.financeState.selectSettlementBatch(ref);
    // The import evidence filename is this screen's in-progress upload for whichever batch
    // is on screen — without resetting it here, switching batches kept showing the *previous*
    // batch's filename (or a file you'd chosen but not yet imported) on a batch that was
    // never touched this session.
    this.importEvidenceFileName.set('Provider statement.pdf');
  }

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.lineStateFilter()) {
      chips.push({ key: 'state', label: `Line state: ${this.lineStateFilter()}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.scopeFilter() !== this.scopeOptions[0]) {
      chips.push({ key: 'scope', label: `Scope: ${this.scopeFilter()}` });
    }
    return chips;
  });

  /**
   * Read-only batch summary — server-derived (4.2.2 field contract), live from the shared
   * Finance state so Import/Validate/Match/Approve on this screen — and Match/Verify on the
   * Finance Workbench — are the same persisted batch every time this screen is opened.
   */
  protected get summary(): SettlementBatchSummary {
    return this.financeState.settlementSummary();
  }

  /** Mutable — Approve visibly updates the badge, not just a toast (4.2.2 Approval state). */
  protected readonly approvalState = computed(() => this.financeState.settlementSummary().approvalState);

  /** Settlement lines — read-only (4.2.1 Main work), live from the shared Finance state. */
  protected get settlementLines(): SettlementLine[] {
    return this.financeState.settlementLines();
  }

  /** Line totals (4.2.1 Main work). */
  protected readonly lineTotals = computed(() => {
    let gross = 0;
    let fees = 0;
    for (const l of this.settlementLines) {
      gross += l.amount;
      fees += l.fee;
    }
    return { gross, fees };
  });

  protected readonly filteredLines = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const state = this.lineStateFilter();
    return this.settlementLines.filter((l) => {
      if (q && !(l.lineReference.toLowerCase().includes(q) || l.paymentReference.toLowerCase().includes(q))) {
        return false;
      }
      if (state && state !== 'All states' && l.matchState !== state) return false;
      return true;
    });
  });

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.lineStateFilter.set('');
    this.scopeFilter.set(this.scopeOptions[0]);
    this.savedView.set(this.savedViews[0]);
  }

  protected removeFilterChip(key: string): void {
    if (key === 'state') this.lineStateFilter.set('');
    else if (key === 'search') this.searchTerm.set('');
    else if (key === 'scope') this.scopeFilter.set(this.scopeOptions[0]);
  }

  // ================= Actions, eligibility and result (4.2.3) =================
  /**
   * All four actions previously checked only the base permission flag, ignoring the
   * batch's actual lifecycle — an already-Approved batch (the documented terminal
   * state) could still be re-imported, re-validated, re-matched or re-approved,
   * silently overwriting a finalised result with stale data.
   */
  private readonly notFinalised = computed(() => this.lifecycleState !== 'Approved');
  protected readonly importAllowed = computed(() => this.permissions.import && this.notFinalised());
  protected readonly validateAllowed = computed(() => this.permissions.validate && this.notFinalised());
  protected readonly matchAllowed = computed(() => this.permissions.match && this.notFinalised());
  protected readonly approveAllowed = computed(() => this.permissions.approve && this.notFinalised());

  /** Why Import/Validate/Match/Approve is disabled — shown as a toast since a native disabled button never fires click. */
  protected readonly lifecycleBlockedReason = computed(() =>
    `Batch ${this.summary.settlementBatchReference} is already Approved — no further action is available on it.`,
  );

  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  /** Import evidence / Audit history — "copy/open action preserves the exact stable value" (4.2.2). */
  protected async copyReference(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toast.show('Copied', `Reference ${value} copied to clipboard.`, 'success', 2500);
    } catch {
      this.toast.show('Copy failed', 'Could not copy to clipboard. Copy the value manually.', 'error');
    }
  }

  /** No access — "Return to the permitted landing page" (4.2.4). */
  protected returnToWorkspace(): void {
    this.router.navigate(['/app/workspace/my-workspace']);
  }

  // ----- Conflict recovery: Compare / Reapply eligible changes / Cancel (4.2.4 / 4.2.6) -----
  /** Opens a real before/after diff — the batch as it was when Approve was opened vs. its current state — not a toast. */
  protected compareConflict(): void {
    this.compareOpen.set(true);
  }
  protected closeCompare(): void {
    this.compareOpen.set(false);
  }
  /**
   * Re-opens Approve against the current batch and its fresh version, so the retry is a
   * real re-submission against up-to-date data rather than a silent resend of the stale
   * payload. The reason already typed is safe, non-sensitive input — kept so the user
   * doesn't have to retype it.
   */
  protected reapplyConflictChanges(): void {
    const keptReason = this.approveReason();
    this.compareOpen.set(false);
    this.uiState.set('ready');
    this.approveExpectedVersion.set(this.financeState.settlementBatchVersion());
    this.approveReason.set(keptReason);
    this.approveDialogOpen.set(true);
    this.toast.show('Refreshed', `Batch ${this.summary.settlementBatchReference} — showing the latest version. Your reason was kept.`, 'info');
  }
  protected cancelConflict(): void {
    this.compareOpen.set(false);
    this.conflictCurrent.set(null);
    this.uiState.set('ready');
  }

  /** Dependency failure — "Retry only the failed dependency using a stable correlation reference" (4.2.4). */
  protected retryDependency(): void {
    this.toast.show('Retrying', 'Retrying the failed dependency using correlation INT-77332…', 'info');
    this.uiState.set('success');
  }

  protected readonly importDialogOpen = signal(false);
  /** Evidence file for this import — defaults to the documented sample evidence until the user picks a different file. */
  protected readonly importEvidenceFileName = signal('Provider statement.pdf');
  protected openImportDialog(): void {
    if (!this.importAllowed()) {
      this.toast.show('Import unavailable', this.lifecycleBlockedReason(), 'warning');
      return;
    }
    this.importDialogOpen.set(true);
  }
  protected cancelImport(): void {
    this.importDialogOpen.set(false);
  }
  protected onImportFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.importEvidenceFileName.set(file.name);
  }
  protected confirmImport(): void {
    this.importDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.financeState.importSettlementBatch();
    this.toast.show('Import Complete', `Batch ${this.summary.settlementBatchReference} imported successfully — evidence ${this.importEvidenceFileName()} linked.`, 'success');
  }

  protected readonly approveDialogOpen = signal(false);
  protected readonly approveReason = signal('');
  protected readonly approveReasonMin = 10;
  protected readonly approveReasonMax = 2000;
  protected readonly approveReasonValid = computed(() => {
    const len = this.approveReason().trim().length;
    return len >= this.approveReasonMin && len <= this.approveReasonMax;
  });
  protected readonly approveReasonCount = computed(() => this.approveReason().trim().length);

  /** The batch version captured at the moment the Approve dialog opened — the optimistic-concurrency precondition Approve is submitted with. */
  protected readonly approveExpectedVersion = signal(0);
  /** True the moment the batch moves out from under an open Approve dialog — detected live off the shared version signal, not just at submit time. */
  protected readonly approveStale = computed(
    () => this.approveDialogOpen() && this.financeState.settlementBatchVersion() !== this.approveExpectedVersion(),
  );
  /** Real, populated conflict state — the batch summary as it now stands in the shared store, not a canned banner. */
  protected readonly conflictCurrent = signal<SettlementBatchSummary | null>(null);
  protected readonly compareOpen = signal(false);

  constructor() {
    effect(() => {
      if (this.approveStale()) {
        this.approveDialogOpen.set(false);
        this.conflictCurrent.set(this.summary);
        this.uiState.set('conflict');
      }
    });
  }

  protected openApproveDialog(): void {
    if (!this.approveAllowed()) {
      this.toast.show('Approve unavailable', this.lifecycleBlockedReason(), 'warning');
      return;
    }
    this.approveReason.set('');
    this.approveExpectedVersion.set(this.financeState.settlementBatchVersion());
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
  }
  /**
   * Submits Approve only if the batch's version still matches what was loaded when the
   * dialog opened. A concurrent change — Import/Validate/Match run from this tab or
   * another — is rejected here instead of being silently approved on top of; the caller
   * is routed into the real conflict state with the current server-side summary.
   */
  protected confirmApprove(): void {
    if (!this.approveReasonValid()) return;
    const result = this.financeState.approveSettlementBatchIfCurrent(this.approveExpectedVersion());
    if (!result.ok) {
      this.approveDialogOpen.set(false);
      this.conflictCurrent.set(result.current);
      this.uiState.set('conflict');
      return;
    }
    this.approveDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.toast.show('Approval Complete', `Batch ${result.summary.settlementBatchReference} approved successfully.`, 'success');
  }

  protected performAction(action: 'Import' | 'Validate' | 'Match'): void {
    if (action === 'Import') {
      this.openImportDialog();
    } else if (action === 'Validate') {
      if (!this.validateAllowed()) {
        this.toast.show('Validate unavailable', this.lifecycleBlockedReason(), 'warning');
        return;
      }
      const { blocking } = this.financeState.validateSettlementBatch();
      const settlementRef = this.summary.settlementBatchReference;
      if (blocking) {
        this.uiState.set('validation');
        this.toast.show('Validation Failed', `Batch ${settlementRef} has a blocking line. Routing to Finance exception case to investigate.`, 'error');
        // Same recovery path Match already uses for its own unmatched-lines case — a blocking
        // validation issue has nowhere else to go, so route it to Exception Case for investigation.
        this.router.navigate(['/app/money/finance/finance-exception-case'], { queryParams: { settlementRef } });
      } else {
        this.uiState.set('success');
        this.lastRefresh.set(this.financeState.nowDisplay());
        this.toast.show('Validation Complete', `Batch ${settlementRef} passed validation.`, 'success');
      }
    } else if (action === 'Match') {
      if (!this.matchAllowed()) {
        this.toast.show('Match unavailable', this.lifecycleBlockedReason(), 'warning');
        return;
      }
      const settlementRef = this.summary.settlementBatchReference;
      const { allMatched, blocked } = this.financeState.matchSettlementBatch();
      if (blocked) {
        this.uiState.set('validation');
        this.toast.show('Match Blocked', `Batch ${settlementRef} has a blocking validation issue. Resolve it before matching.`, 'error');
        return;
      }
      this.uiState.set('success');
      this.lastRefresh.set(this.financeState.nowDisplay());
      if (allMatched) {
        this.toast.show('Match Complete', `Batch ${settlementRef} matched successfully. Routing to Finance workbench for verification.`, 'success');
        this.router.navigate(['/app/money/finance/finance-workbench'], { queryParams: { settlementRef } });
      } else {
        this.toast.show('Match Complete', `Batch ${settlementRef} has unmatched lines. Routing to Finance exception case.`, 'warning');
        this.router.navigate(['/app/money/finance/finance-exception-case'], { queryParams: { settlementRef } });
      }
    }
  }

  // ================= Persistent outcome (4.2.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.summary.settlementBatchReference,
    state: this.lifecycleState,
    effectiveTime: this.lastRefresh(),
    downstreamStatus: `${this.summary.matchedAmount.toLocaleString('en-IN')} matched · ${this.summary.unmatchedAmount.toLocaleString('en-IN')} unmatched`,
    owner: this.owner,
    nextAction: 'Resolve blocking validation issues',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
  protected formatDate(iso: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
  protected lineStateClass(state: string): string {
    switch (state) {
      case 'Matched':
        return 'sb-state-matched';
      case 'Unmatched':
        return 'sb-state-unmatched';
      case 'Variance':
        return 'sb-state-variance';
      case 'Blocking':
        return 'sb-state-blocking';
      default:
        return 'sb-state-muted';
    }
  }

  /** Badge class for match state — matches User Directory badge style. */
  protected matchStateBadgeClass(state: string): string {
    switch (state) {
      case 'Matched':
        return 'bg-success bg-opacity-10 text-success';
      case 'Unmatched':
        return 'bg-info bg-opacity-10 text-info';
      case 'Variance':
        return 'bg-warning bg-opacity-10 text-warning';
      case 'Blocking':
        return 'bg-danger bg-opacity-10 text-danger';
      default:
        return 'bg-secondary bg-opacity-10 text-secondary';
    }
  }

  /** Badge class for approval state — matches User Directory badge style. */
  protected approvalBadgeClass(): string {
    const tone = this.approvalState().tone;
    if (tone === 'success') return 'bg-success bg-opacity-10 text-success';
    if (tone === 'danger') return 'bg-danger bg-opacity-10 text-danger';
    return 'bg-warning bg-opacity-10 text-warning';
  }
}

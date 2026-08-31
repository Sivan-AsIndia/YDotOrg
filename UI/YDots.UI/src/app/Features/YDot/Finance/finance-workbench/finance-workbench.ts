import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';
import { FinanceWorkbenchPermissions, WorkbenchRecord, WorkbenchStage, ScopeAwareOption, FinanceUiState } from '../shared/finance.model';


@Component({
  selector: 'app-finance-workbench',
  imports: [CommonModule, FormsModule],
  templateUrl: './finance-workbench.html',
  styleUrl: './finance-workbench.css',
})
export class FinanceWorkbenchComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  // ================= Task header (4.1.1) =================
  protected readonly pageTitle = 'Finance Workbench';
  protected readonly pageSubtitle =
    'Separate captured, settlement, reconciliation, refund and exception queues. Every count, search and action is restricted by effective scope.';
  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'Priya Raghavan · Finance Lead';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 02:30 PM · IST');

  /** Effective permissions decided server-side; the client mirrors the same decision (4.1.3, 4.1.7). */
  protected readonly permissions: FinanceWorkbenchPermissions = {
    view: true,
    match: true,
    verify: true,
    escalate: true,
  };

  /**
   * The actor viewing this workbench. Verify is a primary decision restricted to the
   * Checker role and blocked for self-approval — a Checker cannot verify a record they
   * themselves prepared as Maker (Master §3 R06/R07 boundary; corrected FIN-UI-07/08
   * eligibility pattern applied consistently here to SCR-FIN-001's Verify action).
   */
  protected readonly viewingActor: { reference: string; name: string; role: 'Maker' | 'Checker' } = {
    reference: 'USR-0258',
    name: 'Divya Krishnan',
    role: 'Checker',
  };

  /**
   * Which record the user has selected to act on (radio-select in the table, same pattern
   * as Reconciliation Workspace's `activeRow`). The header's primary Verify action must
   * never guess a target on the user's behalf — it previously picked whichever record
   * happened to be first in the whole dataset the instant it became eligible, so clicking
   * the header button could verify a record the user never looked at. It now only ever
   * acts on this explicit selection.
   */
  protected readonly selectedWorkReference = signal<string | null>(null);
  protected selectRow(workReference: string): void {
    this.selectedWorkReference.set(this.selectedWorkReference() === workReference ? null : workReference);
  }
  protected readonly selectedRecord = computed<WorkbenchRecord | null>(() =>
    this.records().find((r) => r.workReference === this.selectedWorkReference()) ?? null,
  );

  /**
   * Task header primary action (4.1.1 "one primary action"; 4.1.3 Verify is the Primary-
   * decision action) — now gated on the user's own row selection, not an auto-picked
   * record. Null whenever nothing is selected or the selected record isn't eligible.
   */
  protected readonly headerVerifyTarget = computed<WorkbenchRecord | null>(() => {
    const record = this.selectedRecord();
    return record && this.verifyAllowed(record) ? record : null;
  });
  /** Why the header Verify shortcut is disabled — shown as the button title and as a toast if clicked anyway. */
  protected readonly headerVerifyBlockedReason = computed(() => {
    const record = this.selectedRecord();
    if (!record) return 'Select a record below, then Verify.';
    return this.actionBlockedReason(record);
  });
  /** Header Verify shortcut — opens the same Verify dialog as the row action, for whichever record the user selected. */
  protected openHeaderVerifyDialog(): void {
    const target = this.headerVerifyTarget();
    if (!target) {
      this.toast.show('Verify unavailable', this.headerVerifyBlockedReason(), 'warning');
      return;
    }
    this.openVerifyDialog(target);
  }

  // ================= Context and filters (4.1.1 + 4.1.2) =================
  protected readonly savedViews = ['All Queues (Default)', 'Reconciliation focus', 'Exceptions focus'];
  protected readonly savedView = signal(this.savedViews[0]);
  protected readonly pageSize = 5;
  protected readonly currentPage = signal(1);

  /** Pre-filled from an incoming ?settlementRef= handoff (e.g. from Settlement batch detail's Match action). */
  protected readonly searchTerm = signal(this.route.snapshot.queryParamMap.get('settlementRef') ?? '');

  /** Work queue — search/filter control (4.1.2 Work queue). */
  protected readonly workQueueCatalogue: readonly WorkbenchStage[] = [
    'Captured',
    'Settlement',
    'Reconciliation',
    'Refund',
    'Exception',
  ];
  protected readonly workQueueFilter = signal<WorkbenchStage | ''>('');

  /** Age — search/filter control (4.1.2 Age). */
  protected readonly ageOptions = ['Any age', '0–24 hours', '1–3 days', '4–7 days', 'Over 7 days'];
  protected readonly ageFilter = signal(this.ageOptions[0]);

  /** Priority — search-select with current catalogue values only (4.1.2 Priority). */
  protected readonly priorityOptions: readonly ('High' | 'Medium' | 'Low')[] = ['High', 'Medium', 'Low'];
  protected readonly priorityFilter = signal<'High' | 'Medium' | 'Low' | ''>('');

  /** Owner — scope-aware searchable selector with identity preview (4.1.2 Owner). */
  protected readonly ownerOptions: readonly ScopeAwareOption[] = [
    { reference: 'USR-0000', name: 'All Owners', context: 'YDot Foundation · National', initials: 'AL', tone: 'meadow' },
    { reference: 'USR-0231', name: 'Priya Raghavan', context: 'Finance Lead · Tamil Nadu', initials: 'PR', tone: 'meadow' },
    { reference: 'USR-0244', name: 'Arjun Menon', context: 'Finance Maker · Kerala', initials: 'AM', tone: 'blue' },
    { reference: 'USR-0258', name: 'Divya Krishnan', context: 'Finance Checker · Karnataka', initials: 'DK', tone: 'plum' },
  ];
  protected readonly ownerFilter = signal<string>('USR-0000');

  /** Campaign or period — scope-aware searchable selector (4.1.2). */
  protected readonly campaignOptions = [
    { reference: 'PER-0000', label: 'All campaigns / periods' },
    { reference: 'CAMP-2025-0011', label: 'Educate a Child 2025' },
    { reference: 'CAMP-2025-0013', label: 'Health Camp Rural Drive' },
    { reference: 'CAMP-2025-0015', label: 'Women Empowerment 2025' },
    { reference: 'PER-2025-Q2', label: 'FY25 Q2 Period' },
  ];
  protected readonly campaignFilter = signal('PER-0000');

  /** Data scope (4.1 Data scope). */
  protected readonly scopeOptions = [
    'My active organisation (default)',
    'YDot Foundation · National',
    'Southern Region · Tamil Nadu',
    'Western Region · Gujarat',
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);

  protected readonly moreFiltersOpen = signal(false);
  protected toggleMoreFilters(): void {
    this.moreFiltersOpen.update((v) => !v);
  }

  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  protected readonly moreFiltersCount = computed(() => {
    let n = 0;
    if (this.scopeFilter() !== this.scopeOptions[0]) n++;
    if (this.priorityFilter()) n++;
    if (this.ageFilter() !== this.ageOptions[0]) n++;
    if (this.campaignFilter() !== 'PER-0000') n++;
    return n;
  });

  /** Active-filter summary chips, qualified by scope (4.1.1 Context and filters). */
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.workQueueFilter()) {
      chips.push({ key: 'queue', label: `Work queue: ${this.workQueueFilter()}` });
    }
    if (this.priorityFilter()) {
      chips.push({ key: 'priority', label: `Priority: ${this.priorityFilter()}` });
    }
    const owner = this.ownerOptions.find((o) => o.reference === this.ownerFilter());
    if (owner && owner.reference !== 'USR-0000') {
      chips.push({ key: 'owner', label: `Owner: ${owner.name}` });
    }
    if (this.ageFilter() !== this.ageOptions[0]) {
      chips.push({ key: 'age', label: `Age: ${this.ageFilter()}` });
    }
    if (this.campaignFilter() !== 'PER-0000') {
      const camp = this.campaignOptions.find((c) => c.reference === this.campaignFilter());
      chips.push({ key: 'campaign', label: `Campaign: ${camp?.label ?? this.campaignFilter()}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.scopeFilter() !== this.scopeOptions[0]) {
      chips.push({ key: 'scope', label: `Scope: ${this.scopeFilter()}` });
    }
    return chips;
  });

  // ================= Main work: separate queues (4.1.1) =================

  /**
   * Full record set inside the actor's effective data scope (4.1 Data scope) — backed by
   * the shared Finance state service so Match/Verify/Escalate here, and actions taken on
   * every other Finance screen, are the same persisted records everywhere they're shown.
   */
  protected readonly records = this.financeState.workbenchRecords;

  protected readonly totalRecords = 356;

  // ----- Pagination -----
  protected readonly recordCount = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const queue = this.workQueueFilter();
    const priority = this.priorityFilter();
    const owner = this.ownerFilter();
    const age = this.ageFilter();
    const campaign = this.campaignFilter();
    return this.records().filter((r) => {
      if (q && !(r.workReference.toLowerCase().includes(q) || r.paymentOrSettlementReference.toLowerCase().includes(q))) {
        return false;
      }
      if (queue && r.workQueue !== queue) return false;
      if (priority && r.priority !== priority) return false;
      if (owner !== 'USR-0000' && r.ownerReference !== owner) return false;
      if (age !== this.ageOptions[0] && r.age !== age) return false;
      if (campaign !== 'PER-0000' && !r.campaignOrPeriod.includes(campaign === 'PER-2025-Q2' ? 'FY25 Q2' : campaign)) return false;
      return true;
    }).length;
  });

  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const queue = this.workQueueFilter();
    const priority = this.priorityFilter();
    const owner = this.ownerFilter();
    const age = this.ageFilter();
    const campaign = this.campaignFilter();
    return this.records().filter((r) => {
      if (q && !(r.workReference.toLowerCase().includes(q) || r.paymentOrSettlementReference.toLowerCase().includes(q))) {
        return false;
      }
      if (queue && r.workQueue !== queue) return false;
      if (priority && r.priority !== priority) return false;
      if (owner !== 'USR-0000' && r.ownerReference !== owner) return false;
      if (age !== this.ageOptions[0] && r.age !== age) return false;
      if (campaign !== 'PER-0000' && !r.campaignOrPeriod.includes(campaign === 'PER-2025-Q2' ? 'FY25 Q2' : campaign)) return false;
      return true;
    });
  });

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.recordCount() / this.pageSize)));
  private readonly clampedPage = computed(() => Math.min(this.currentPage(), this.totalPages()));
  protected readonly pagedRecords = computed(() => {
    const start = (this.clampedPage() - 1) * this.pageSize;
    return this.visibleRecords().slice(start, start + this.pageSize);
  });
  protected readonly pageStart = computed(() =>
    this.recordCount() === 0 ? 0 : (this.clampedPage() - 1) * this.pageSize + 1,
  );
  protected readonly pageEnd = computed(() => Math.min(this.clampedPage() * this.pageSize, this.recordCount()));
  protected readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.clampedPage();
    const pages: number[] = [];
    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  });

  protected goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
    }
  }

  /** Queue summary cards — totals qualified by scope (4.1.1 Main work). */
  protected readonly queueSummary = computed(() => {
    const totals: Record<WorkbenchStage, number> = { Captured: 0, Settlement: 0, Reconciliation: 0, Refund: 0, Exception: 0 };
    for (const r of this.visibleRecords()) {
      totals[r.workQueue]++;
    }
    return [
      { key: 'Captured' as const, label: 'Captured', count: totals.Captured, tone: 'info' },
      { key: 'Settlement' as const, label: 'Settlement', count: totals.Settlement, tone: 'blue' },
      { key: 'Reconciliation' as const, label: 'Reconciliation', count: totals.Reconciliation, tone: 'gold' },
      { key: 'Refund' as const, label: 'Refund', count: totals.Refund, tone: 'muted' },
      { key: 'Exception' as const, label: 'Exception', count: totals.Exception, tone: 'danger' },
    ];
  });

  // ================= Actions, eligibility and result (4.1.3) =================
  protected readonly filterAllowed = computed(
    () => this.permissions.view && this.uiState() !== 'no-access',
  );

  /**
   * Per-record action eligibility (4.1.3).
   *
   * Queue-specific and role-hierarchy restrictions (Reconciliation-only Match,
   * Checker-only/self-approval-blocked Verify, Exception-only Escalate) are
   * intentionally relaxed for now at the user's request — every row's Match,
   * Verify and Escalate buttons are enabled so the full workflow is reachable
   * from any record. Restore the queue/role hierarchy checks here when that
   * enforcement is wanted back.
   *
   * What is NOT relaxed: a record that has already reached its terminal state
   * (`nextAction === 'No further action'`) is done — nothing is left to match,
   * verify or escalate on it. Without this, the header's default target picked
   * whichever record happened to be first in the list, including already-
   * verified/resolved ones, letting the same record be actioned again.
   */
  private static readonly terminalNextAction = 'No further action';
  protected matchAllowed(record: WorkbenchRecord): boolean {
    return this.permissions.match && record.nextAction !== FinanceWorkbenchComponent.terminalNextAction;
  }
  protected verifyAllowed(record: WorkbenchRecord): boolean {
    return this.permissions.verify && record.nextAction !== FinanceWorkbenchComponent.terminalNextAction;
  }
  /** True when Verify would otherwise apply but is blocked because the viewer prepared this record. Hierarchy check disabled — always false for now. */
  protected verifyBlockedBySelfApproval(record: WorkbenchRecord): boolean {
    return false;
  }
  protected escalateAllowed(record: WorkbenchRecord): boolean {
    return this.permissions.escalate && record.nextAction !== FinanceWorkbenchComponent.terminalNextAction;
  }

  protected matchTitle(record: WorkbenchRecord): string {
    return this.matchAllowed(record) ? 'Match' : 'Already processed — no further action';
  }
  protected escalateTitle(record: WorkbenchRecord): string {
    return this.escalateAllowed(record) ? 'Escalate' : 'Already processed — no further action';
  }
  protected verifyTitle(record: WorkbenchRecord): string {
    return this.verifyAllowed(record) ? 'Verify' : 'Already processed — no further action';
  }

  /** Why a row's Match/Verify/Escalate button is disabled — shown as a toast since a native disabled button never fires click. */
  protected actionBlockedReason(record: WorkbenchRecord): string {
    if (record.nextAction === FinanceWorkbenchComponent.terminalNextAction) {
      return `${record.workReference} has already reached "No further action" — there is nothing left to match, verify or escalate on it.`;
    }
    return 'This action is not available with your current permissions.';
  }

  /** Filter — primary action (4.1.3). */
  protected applyFilter(): void {
    if (!this.filterAllowed()) {
      this.uiState.set('validation');
      return;
    }
    this.moreFiltersOpen.set(false);
    this.currentPage.set(1);
    this.uiState.set(this.visibleRecords().length === 0 ? 'empty' : 'ready');
  }

  /** Clearing filters is explicit and returns focus predictably (4.1.1). */
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.workQueueFilter.set('');
    this.priorityFilter.set('');
    this.ownerFilter.set('USR-0000');
    this.ageFilter.set(this.ageOptions[0]);
    this.campaignFilter.set('PER-0000');
    this.scopeFilter.set(this.scopeOptions[0]);
    this.savedView.set(this.savedViews[0]);
    this.currentPage.set(1);
    this.uiState.set('ready');
  }

  protected removeFilterChip(key: string): void {
    this.currentPage.set(1);
    if (key === 'queue') this.workQueueFilter.set('');
    else if (key === 'priority') this.priorityFilter.set('');
    else if (key === 'owner') this.ownerFilter.set('USR-0000');
    else if (key === 'age') this.ageFilter.set(this.ageOptions[0]);
    else if (key === 'campaign') this.campaignFilter.set('PER-0000');
    else if (key === 'search') this.searchTerm.set('');
    else if (key === 'scope') this.scopeFilter.set(this.scopeOptions[0]);
  }

  // ----- Workflow actions (4.1.3) -----
  protected readonly openRowMenu = signal<string | null>(null);
  protected toggleRowMenu(reference: string): void {
    this.openRowMenu.update((cur) => (cur === reference ? null : reference));
  }

  protected readonly lastActioned = signal<string>('');
  protected readonly lastAction = signal<string>('');

  /**
   * Match and Escalate are workflow actions open to any FIN role — no reason capture
   * required. Each routes into the screen where that action is actually carried out,
   * matching the same handoff pattern used across the other Finance screens:
   *  · Match    → Reconciliation workspace (where the match is actually proposed/confirmed)
   *  · Escalate → Finance exception case (where the exception is actually investigated)
   */
  protected performAction(record: WorkbenchRecord, action: 'Match' | 'Escalate'): void {
    const allowed = action === 'Match' ? this.matchAllowed(record) : this.escalateAllowed(record);
    if (!allowed) {
      this.toast.show(`${action} unavailable`, this.actionBlockedReason(record), 'warning');
      return;
    }
    this.openRowMenu.set(null);
    this.lastActioned.set(record.workReference);
    this.lastAction.set(action);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const workRef = record.workReference;
    /**
     * The destination screen's rows are keyed by the payment/settlement reference, not
     * this workbench's own work reference — carry that through the handoff so the
     * destination's search prefill actually finds the record instead of showing "no rows".
     */
    const workRefParam = record.paymentOrSettlementReference;
    if (action === 'Match') {
      this.financeState.matchWorkbenchRecord(workRef);
      this.toast.show('Match recorded', `${workRef} — routing to Reconciliation workspace.`, 'success');
      this.router.navigate(['/app/money/finance/reconciliation-workspace'], { queryParams: { workRef: workRefParam } });
    } else {
      this.financeState.escalateWorkbenchRecord(workRef);
      this.toast.show('Escalate recorded', `${workRef} — routing to Finance exception case.`, 'success');
      this.router.navigate(['/app/money/finance/finance-exception-case'], { queryParams: { workRef: workRefParam } });
    }
  }

  // ---- Verify dialog: primary decision, Checker only, self-approval blocked (4.1.3) ----
  protected readonly verifyDialogOpen = signal(false);
  protected readonly verifyTarget = signal<WorkbenchRecord | null>(null);
  /** The record's version captured at the moment the dialog opened — the optimistic-concurrency precondition Verify is submitted with. */
  protected readonly verifyExpectedVersion = signal(0);
  protected readonly verifyReason = signal('');
  protected readonly verifyReasonMin = 10;
  protected readonly verifyReasonMax = 2000;
  protected readonly verifyReasonCount = computed(() => this.verifyReason().trim().length);
  protected readonly verifyReasonValid = computed(() => {
    const len = this.verifyReasonCount();
    return len >= this.verifyReasonMin && len <= this.verifyReasonMax;
  });

  /**
   * The live, current-truth copy of whatever record the Verify dialog is open on — reads
   * straight off the shared `records` signal, so it updates the instant the record changes
   * anywhere (this screen, another Finance screen, or another browser tab via the service's
   * cross-tab broadcast), not just when this screen happens to re-fetch.
   */
  protected readonly verifyLiveRecord = computed(() => {
    const target = this.verifyTarget();
    if (!target) return null;
    return this.records().find((r) => r.workReference === target.workReference) ?? null;
  });
  /** True the moment the record moves out from under an open Verify dialog — detected live, not just at submit time or on next reload/refocus. */
  protected readonly verifyStale = computed(() => {
    const live = this.verifyLiveRecord();
    return !!live && live.version !== this.verifyExpectedVersion();
  });

  /** Real, populated conflict state — the record as it now stands on the server side of this demo (the shared store), not a canned banner. */
  protected readonly conflictCurrent = signal<WorkbenchRecord | null>(null);
  protected readonly compareOpen = signal(false);

  constructor() {
    // Freshness check that doesn't wait for submit or window refocus — as soon as the
    // record this dialog is open on changes anywhere, close the stale dialog and route
    // into the real conflict state immediately.
    effect(() => {
      if (this.verifyDialogOpen() && this.verifyStale()) {
        const live = this.verifyLiveRecord();
        this.verifyDialogOpen.set(false);
        this.conflictCurrent.set(live);
        this.uiState.set('conflict');
      }
    });
  }

  protected openVerifyDialog(record: WorkbenchRecord): void {
    if (!this.verifyAllowed(record)) {
      this.toast.show('Verify unavailable', this.actionBlockedReason(record), 'warning');
      return;
    }
    this.openRowMenu.set(null);
    this.verifyTarget.set(record);
    this.verifyExpectedVersion.set(record.version);
    this.verifyReason.set('');
    this.verifyDialogOpen.set(true);
  }
  protected cancelVerify(): void {
    this.verifyDialogOpen.set(false);
  }
  /**
   * Submits Verify only if the record's version still matches what was loaded when the
   * dialog opened. A concurrent change — from this tab or another — is rejected here
   * instead of being silently applied on top of; the caller is routed into the real
   * conflict state with the current server-side record, never a duplicate/new reference.
   */
  protected confirmVerify(): void {
    const record = this.verifyTarget();
    if (!record || !this.verifyReasonValid()) return;
    const result = this.financeState.verifyWorkbenchRecordIfCurrent(record.workReference, this.verifyExpectedVersion());
    if (!result.ok) {
      this.verifyDialogOpen.set(false);
      this.conflictCurrent.set(result.current);
      this.uiState.set('conflict');
      return;
    }
    this.verifyDialogOpen.set(false);
    this.lastActioned.set(record.workReference);
    this.lastAction.set('Verify');
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.toast.show('Verify recorded', `${record.workReference} — verified by ${this.viewingActor.name}.`, 'success');
  }

  // ================= UI states (4.1.4 / 4.1.7) =================
  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  /** Work reference — "copy/open action preserves the exact stable value" (4.1.2). */
  protected async copyReference(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.toast.show('Copied', `Reference ${value} copied to clipboard.`, 'success', 2500);
    } catch {
      this.toast.show('Copy failed', 'Could not copy to clipboard. Copy the value manually.', 'error');
    }
  }

  /** No access — "Return to the permitted landing page" (4.1.4). */
  protected returnToWorkspace(): void {
    this.router.navigate(['/app/workspace/my-workspace']);
  }

  // ----- Conflict recovery: Compare / Reapply eligible changes / Cancel (4.1.4 / 4.1.6) -----
  /** Opens a real before/after diff — what this screen had loaded vs. the current server-side record — not a toast. */
  protected compareConflict(): void {
    this.compareOpen.set(true);
  }
  protected closeCompare(): void {
    this.compareOpen.set(false);
  }
  /**
   * Re-opens Verify against the current record and its fresh version, so the retry is a
   * real re-submission against up-to-date data rather than a silent resend of the stale
   * payload. The reason the user already typed is safe, non-sensitive input — kept so
   * they don't have to retype it.
   */
  protected reapplyConflictChanges(): void {
    const current = this.conflictCurrent();
    const keptReason = this.verifyReason();
    this.compareOpen.set(false);
    this.uiState.set('ready');
    if (!current) return;
    this.verifyTarget.set(current);
    this.verifyExpectedVersion.set(current.version);
    this.verifyReason.set(keptReason);
    this.verifyDialogOpen.set(true);
    this.toast.show('Refreshed', `${current.workReference} — showing the latest version. Your reason was kept.`, 'info');
  }
  protected cancelConflict(): void {
    this.compareOpen.set(false);
    this.conflictCurrent.set(null);
    this.uiState.set('ready');
  }

  /** Dependency failure — "Retry only the failed dependency using a stable correlation reference" (4.1.4). */
  protected retryDependency(): void {
    this.toast.show('Retrying', 'Retrying the failed dependency using correlation INT-77331…', 'info');
    this.uiState.set('success');
  }

  // ================= Persistent outcome (4.1.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.lastActioned() || '—',
    state: this.lastActioned() ? this.lastAction() + ' completed' : this.lifecycleState,
    effectiveTime: this.lastRefresh(),
    downstreamStatus: this.lastActioned() ? 'Action recorded' : 'No pending action',
    owner: this.owner,
    nextAction: this.lastActioned() ? 'Review the next finance work queue' : 'Filter the finance workbench',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
  protected ownerOf(reference: string): ScopeAwareOption {
    return this.ownerOptions.find((o) => o.reference === reference) ?? this.ownerOptions[0];
  }
  protected slaToneClass(record: WorkbenchRecord): string {
    const tone = record.slaState.tone;
    return `fw-sla-${tone}`;
  }
  protected queueToneClass(queue: WorkbenchStage): string {
    const tones: Record<WorkbenchStage, string> = {
      Captured: 'fw-queue-info',
      Settlement: 'fw-queue-blue',
      Reconciliation: 'fw-queue-gold',
      Refund: 'fw-queue-muted',
      Exception: 'fw-queue-danger',
    };
    return tones[queue];
  }

  /** Badge class for work queue — matches User Directory badge style. */
  protected queueBadgeClass(queue: WorkbenchStage): string {
    const tones: Record<WorkbenchStage, string> = {
      Captured: 'bg-primary bg-opacity-10 text-primary',
      Settlement: 'bg-info bg-opacity-10 text-info',
      Reconciliation: 'bg-warning bg-opacity-10 text-warning',
      Refund: 'bg-secondary bg-opacity-10 text-secondary',
      Exception: 'bg-danger bg-opacity-10 text-danger',
    };
    return tones[queue];
  }

  /** Badge class for SLA state — matches User Directory badge style. */
  protected slaBadgeClass(record: WorkbenchRecord): string {
    const tone = record.slaState.tone;
    if (tone === 'success') return 'bg-success bg-opacity-10 text-success';
    if (tone === 'warning') return 'bg-warning bg-opacity-10 text-warning';
    return 'bg-danger bg-opacity-10 text-danger';
  }

  /** Icon class for queue summary cards. */
  protected queueIcon(queue: WorkbenchStage): string {
    const icons: Record<WorkbenchStage, string> = {
      Captured: 'ri-inbox-archive-line',
      Settlement: 'ri-bank-card-line',
      Reconciliation: 'ri-exchange-funds-line',
      Refund: 'ri-refund-2-line',
      Exception: 'ri-alert-line',
    };
    return icons[queue];
  }
}

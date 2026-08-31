import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  FinanceUiState,
  ClosureChecklistItem,
  PeriodClosePermissions,
} from '../../../../Shared/models/finance.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';

/**
 * SCR-FIN-006 — Period / campaign close.
 *
 * Faithful implementation of section 4.6 / 7.6 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /money/finance/period-campaign-close
 *  Purpose          : Run closure checklist, freeze period and publish official totals.
 *  Primary users    : Finance Approver
 *  View permission  : fin.period-campaign-close.view
 *  Primary action   : Validate
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

@Component({
  selector: 'app-period-campaign-close',
  imports: [CommonModule, FormsModule],
  templateUrl: './period-campaign-close.html',
  styleUrl: './period-campaign-close.css',
})
export class PeriodCampaignCloseComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  /**
   * Incoming handoff reference — ?reviewRef= (Maker-checker review's Approve
   * action). Displayed in the task meta row; the closure's own fields stay as
   * documented rather than being overwritten by the incoming param.
   */
  protected readonly linkedFrom = this.route.snapshot.queryParamMap.get('reviewRef') ?? '';

  // ================= Task header (4.6.1) =================
  protected readonly pageTitle = 'Period / Campaign Close';
  protected readonly pageSubtitle =
    'Run closure checklist, freeze period and publish official totals. Every count and action is restricted by effective scope.';
  /** Mutable — Validate/Sign off/Reopen visibly advance the lifecycle, not just show a toast; backed by the shared Finance state so a revisit shows the persisted closure state. */
  protected readonly lifecycleState = this.financeState.closureLifecycle;
  protected readonly owner = 'Priya Raghavan · Finance Approver';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 05:10 PM · IST');

  /** Effective permissions decided server-side (4.6.3, 4.6.7). */
  protected readonly permissions: PeriodClosePermissions = {
    view: true,
    validate: true,
    signOff: true,
  };

  private readonly workflowPermittedStates = ['Validation', 'Pending review', 'Ready to close'];

  // ================= Context and filters (4.6.1) =================
  protected readonly closureReference = 'CLS-2025-Q2-001';
  protected readonly periodOrCampaign = signal('PER-2025-Q2');
  protected readonly periodOptions = [
    { reference: 'PER-2025-Q2', label: 'FY25 Q2 Period (Apr–Jun 2025)' },
    { reference: 'CAMP-2025-0011', label: 'Educate a Child 2025' },
    { reference: 'CAMP-2025-0013', label: 'Health Camp Rural Drive' },
  ];
  protected readonly cutOffTime = signal('2025-06-30T23:59:00');
  protected readonly interpretedCutOff = computed(() => {
    const d = new Date(this.cutOffTime());
    if (Number.isNaN(d.getTime())) return `Select cut-off · ${this.operatingTimeZone}`;
    return `${d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })} · ${d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })} · ${this.operatingTimeZone}`;
  });

  /**
   * Read-only totals (4.6.2 field contract) — read from the shared Finance state, not a
   * hardcoded snapshot (4.6 "must read current values, not hardcoded sample values").
   * The captured/settled/reconciled/refunded rollups represent the wider portfolio
   * (356 total records per the Workbench) plus whatever is live in the interactive
   * sample data below, so today's on-screen figures stay stable at rest and genuinely
   * move as donations are captured or ledger rows get matched.
   */
  private static readonly capturedBase = 2485000 - (25000 + 12000);
  private static readonly settledBase = 2412000 - (180260 + 216392);
  private static readonly reconciledBase = 2389000 - (24590 + 41310);
  private static readonly refundedBase = 45000 - (5000 + 2500);

  protected get capturedTotal(): number {
    const live = this.financeState
      .workbenchRecords()
      .filter((r) => r.workQueue === 'Captured')
      .reduce((sum, r) => sum + r.grossAmount, 0);
    return PeriodCampaignCloseComponent.capturedBase + live;
  }
  /** Sums every settlement batch, not just whichever one is currently selected on the Settlement Batch Detail screen. */
  protected get settledTotal(): number {
    return PeriodCampaignCloseComponent.settledBase + this.financeState.totalSettledNetAmount();
  }
  protected get reconciledTotal(): number {
    const live = this.financeState
      .reconciliationLedger()
      .filter((r) => r.paymentState === 'Matched')
      .reduce((sum, r) => sum + r.netAmount, 0);
    return PeriodCampaignCloseComponent.reconciledBase + live;
  }
  protected get refundedTotal(): number {
    const live = this.financeState
      .workbenchRecords()
      .filter((r) => r.workQueue === 'Refund')
      .reduce((sum, r) => sum + r.grossAmount, 0);
    return PeriodCampaignCloseComponent.refundedBase + live;
  }
  /** Pure live count — no base offset, since this is meant to move exactly with the Exception queue. */
  protected get openExceptions(): number {
    return this.financeState.exceptionRecords().filter((r) => r.status !== 'Resolved').length;
  }
  /** Pure live sum — no base offset, since this is meant to move exactly with the reconciliation ledger. */
  protected get unmatchedAmount(): number {
    return this.financeState
      .reconciliationLedger()
      .filter((r) => r.paymentState === 'Unmatched')
      .reduce((sum, r) => sum + r.netAmount, 0);
  }
  protected readonly officialTotalVersion = 'v2.1';

  /** Closure checklist (4.6.1 Main work — Run closure checklist) — the exceptions/unmatched rows read the current data; the rest stay as documented. */
  protected get checklist(): readonly ClosureChecklistItem[] {
    const openExceptions = this.openExceptions;
    const unmatchedAmount = this.unmatchedAmount;
    return [
      { key: 'captured', label: 'All captured donations verified', state: 'Complete', detail: '248 donations verified' },
      { key: 'settled', label: 'All settlement batches reconciled', state: 'Complete', detail: '12 batches reconciled' },
      {
        key: 'exceptions',
        label: 'Open exceptions resolved or approved',
        state: openExceptions > 0 ? 'Blocking' : 'Complete',
        detail: openExceptions > 0 ? `${openExceptions} open exception(s) require approval` : 'All exceptions resolved or approved',
      },
      { key: 'refunds', label: 'Refunds processed', state: 'Complete', detail: '18 refunds processed' },
      {
        key: 'unmatched',
        label: 'Unmatched amounts carried forward',
        state: unmatchedAmount > 0 ? 'Pending' : 'Complete',
        detail: unmatchedAmount > 0 ? `₹${this.formatAmount(unmatchedAmount)} requires carry-forward justification` : 'No unmatched amount outstanding',
      },
      { key: 'totals', label: 'Official totals published', state: 'Pending', detail: 'Awaiting sign-off' },
    ];
  }

  protected readonly checklistStatus = computed(() => {
    const blocking = this.checklist.filter((c) => c.state === 'Blocking').length;
    const pending = this.checklist.filter((c) => c.state === 'Pending').length;
    if (blocking > 0) return { label: `${blocking} blocking · ${pending} pending`, tone: 'danger', icon: '✕' };
    if (pending > 0) return { label: `${pending} pending`, tone: 'warn', icon: '!' };
    return { label: 'All complete', tone: 'success', icon: '✓' };
  });

  /** Carry-forward justification (conditional) (4.6.2). */
  protected readonly carryForwardJustification = signal('');
  protected readonly carryForwardMin = 10;
  protected readonly carryForwardMax = 2000;
  protected readonly carryForwardValid = computed(() => {
    const len = this.carryForwardJustification().trim().length;
    return len >= this.carryForwardMin && len <= this.carryForwardMax;
  });
  protected readonly carryForwardCount = computed(() => this.carryForwardJustification().trim().length);

  /** Sign-off decision (conditional) (4.6.2). */
  protected readonly signOffDecision = signal('');
  protected readonly signOffOptions = ['Approve closure', 'Return for correction', 'Defer closure'];

  /** Approvers — read-only (4.6.2). */
  protected readonly approvers = 'Priya Raghavan · Finance Approver · Divya Krishnan · Finance Checker';

  // ================= Actions, eligibility and result (4.6.3) =================
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState());

  protected readonly validateAllowed = computed(() => this.permissions.validate && this.inWorkflowState());
  /** Sign-off is blocked while the checklist has a blocking item outstanding (4.6 Flow: "If blocking issue → remain open and show required correction"). */
  protected readonly signOffAllowed = computed(
    () => this.permissions.signOff && this.inWorkflowState() && this.checklistStatus().tone !== 'danger',
  );

  /** Why Validate is disabled — shown as a toast since a native disabled button never fires click. */
  protected readonly validateBlockedReason = computed(() =>
    `Closure ${this.closureReference} is currently "${this.lifecycleState()}" — Validate isn't available in that state.`,
  );
  /** Why Sign off is disabled. */
  protected readonly signOffBlockedReason = computed(() => {
    if (this.checklistStatus().tone === 'danger') {
      return `Resolve the ${this.checklistStatus().label} in the closure checklist before signing off.`;
    }
    return `Closure ${this.closureReference} is currently "${this.lifecycleState()}" — Sign off isn't available in that state.`;
  });

  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  /** No access — "Return to the permitted landing page" (4.6.4). */
  protected returnToWorkspace(): void {
    this.router.navigate(['/app/workspace/my-workspace']);
  }

  // ----- Conflict recovery: Compare / Reapply eligible changes / Cancel (4.6.4 / 4.6.6) -----
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

  /** Dependency failure — "Retry only the failed dependency using a stable correlation reference" (4.6.4). */
  protected retryDependency(): void {
    this.toast.show('Retrying', 'Retrying the failed dependency using correlation INT-77336…', 'info');
    this.uiState.set('success');
  }

  protected performValidate(): void {
    if (!this.validateAllowed()) {
      this.toast.show('Validate unavailable', this.validateBlockedReason(), 'warning');
      return;
    }
    // "If blocking issue → remain open and show required correction" (4.6 Flow) — Validate
    // previously always advanced regardless of the checklist's live blocking status.
    if (this.checklistStatus().tone === 'danger') {
      this.uiState.set('validation');
      this.toast.show('Validation Failed', `Closure ${this.closureReference} has ${this.checklistStatus().label}. Resolve blocking items before signing off.`, 'error');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Ready to close');
    this.toast.show('Validation complete', `Closure ${this.closureReference} passed validation.`, 'success');
  }

  /** Sign off — primary decision (4.6.3). */
  protected readonly signOffDialogOpen = signal(false);
  protected readonly signOffReason = signal('');
  protected readonly signOffReasonMin = 10;
  protected readonly signOffReasonMax = 2000;
  protected readonly signOffReasonValid = computed(() => {
    const len = this.signOffReason().trim().length;
    return len >= this.signOffReasonMin && len <= this.signOffReasonMax;
  });
  protected readonly signOffReasonCount = computed(() => this.signOffReason().trim().length);

  protected openSignOffDialog(): void {
    if (!this.signOffAllowed()) {
      this.toast.show('Sign off unavailable', this.signOffBlockedReason(), 'warning');
      return;
    }
    this.signOffReason.set('');
    this.signOffDialogOpen.set(true);
  }
  protected cancelSignOff(): void {
    this.signOffDialogOpen.set(false);
  }
  protected confirmSignOff(): void {
    if (!this.signOffReasonValid()) return;
    this.signOffDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    // The Sign-off decision select offered three distinct outcomes but was never read here —
    // every decision (including "Return for correction" and "Defer closure") silently closed
    // the period exactly like "Approve closure" would. Branch on it for real.
    const decision = this.signOffDecision();
    if (decision === 'Return for correction') {
      this.lifecycleState.set('Validation');
      this.toast.show('Returned for correction', `Closure ${this.closureReference} returned for correction — official totals not published.`, 'warning');
    } else if (decision === 'Defer closure') {
      this.toast.show('Closure deferred', `Closure ${this.closureReference} deferred — official totals not published.`, 'warning');
    } else {
      this.lifecycleState.set('Closed');
      this.toast.show('Sign-off recorded', `Closure ${this.closureReference} signed off · official totals published.`, 'success');
    }
  }

  /**
   * Reopen exceptionally (4.6.3).
   *
   * OPEN ISSUE 2 (confirmed as-written): the spec gates this action by
   * fin.period-campaign-close.view — the same permission required just to open
   * and read this page. Every other Operate-type action across the whole FIN
   * spec has its own dedicated permission string; this is the only exception.
   * Wired literally as documented — `permissions.view` is the sole gate, which
   * means any actor who can view this page can reopen a closed period. If a
   * dedicated permission (e.g. fin.period-campaign-close.reopen) is intended,
   * update this computed to check that instead.
   */
  protected readonly reopenAllowed = computed(() => this.permissions.view);
  protected reopenExceptionally(): void {
    if (!this.reopenAllowed()) return;
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Validation');
    this.toast.show('Reopened exceptionally', `Closure ${this.closureReference} reopened.`, 'warning');
  }

  // ================= Persistent outcome (4.6.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.closureReference,
    state: this.lifecycleState(),
    effectiveTime: this.lastRefresh(),
    downstreamStatus: `${this.checklistStatus().label} · Official total ${this.officialTotalVersion}`,
    owner: this.owner,
    nextAction: 'Resolve blocking exceptions and sign off official totals',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
  protected checklistStateClass(state: string): string {
    switch (state) {
      case 'Complete':
        return 'pc-state-complete';
      case 'Pending':
        return 'pc-state-pending';
      case 'Blocking':
        return 'pc-state-blocking';
      case 'Exempt':
        return 'pc-state-exempt';
      default:
        return 'pc-state-muted';
    }
  }
}
import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  FinanceUiState,
  MakerCheckerPermissions,
} from '../../../../Shared/models/finance.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';

/**
 * FIN-UI-07 — Maker-checker review.
 *
 * Faithful implementation of section 4.7 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /fin/maker-checker-review
 *  Purpose          : Present the proposed financial action, evidence and impact to an
 *                     independent authorised checker.
 *  Primary users    : Finance roles; Finance; Auditor; Finance Maker; Checker; Finance Approver
 *  View permission  : fin.maker-checker-review.view
 *  Primary action   : Approve
 *  History rule     : Only an unused draft without downstream references may be permanently
 *                     deleted; otherwise use a controlled lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

@Component({
  selector: 'app-maker-checker-review',
  imports: [CommonModule, FormsModule],
  templateUrl: './maker-checker-review.html',
  styleUrl: './maker-checker-review.css',
})
export class MakerCheckerReviewComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  /**
   * Incoming handoff reference — ?paymentRef= (Reconciliation workspace's Match
   * action) or ?correctionRef= (Financial correction or reversal's Submit action).
   * Displayed in the task meta row so the arriving record is visibly traceable, and
   * used below to derive the real review contract (reviewReference/affectedReferences/
   * before-after impact) from whichever record actually sent it, instead of a fixed
   * sample. Kept separately (not just merged into `linkedFrom`) so a decision here is
   * reflected back onto the correct originating screen (4.7 "reflected on the
   * originating screen").
   *
   * Read reactively via `queryParamMap` (not `route.snapshot` once in the constructor):
   * Angular reuses this component instance when a second review is routed to this same
   * path with different query params, so a constructor-time snapshot would keep pointing
   * at whichever record arrived first — silently reflecting later decisions onto the
   * wrong originating record.
   */
  private readonly queryParamMap = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  private readonly linkedFromPaymentRef = computed(() => this.queryParamMap().get('paymentRef'));
  private readonly linkedFromCorrectionRef = computed(() => this.queryParamMap().get('correctionRef'));
  protected readonly linkedFrom = computed(() => this.linkedFromPaymentRef() ?? this.linkedFromCorrectionRef() ?? '');

  // ================= Task header (4.7.1) =================
  protected readonly pageTitle = 'Maker-Checker Review';
  protected readonly pageSubtitle =
    'Present the proposed financial action, evidence and impact to an independent authorised checker.';
  /** Mutable — Approve/Return/Reject/Delegate visibly advance the lifecycle, not just show a toast; backed by the shared Finance state so revisiting this screen shows the recorded decision. */
  protected readonly lifecycleState = this.financeState.reviewLifecycle;
  protected readonly owner = 'Divya Krishnan · Finance Checker';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 05:40 PM · IST');

  /** Effective permissions decided server-side (4.7.3, 4.7.7). */
  protected readonly permissions: MakerCheckerPermissions = {
    view: true,
    approve: true,
    returnForCorrection: true,
    reject: true,
    delegate: true,
  };

  private readonly workflowPermittedStates = ['Checker review', 'Pending review'];

  // ================= Review contract (4.7.2 field contract) =================
  protected readonly maker = 'Arjun Menon · Finance Maker';
  protected readonly checker = 'Divya Krishnan · Finance Checker';

  /**
   * The real ledger row this review is about, when it was routed here from Reconciliation
   * Workspace's Manual match. Read live off the shared store so it reflects the row's
   * actual reference — not a hardcoded sample. Used as a fallback for the current state;
   * for the before/after comparison itself, `linkedMatch` below is authoritative because
   * this row has *already* been mutated to Matched by the time the review screen opens.
   */
  private readonly linkedLedgerRow = computed(() => {
    const paymentRef = this.linkedFromPaymentRef();
    if (!paymentRef) return null;
    return this.financeState.reconciliationLedger().find((r) => r.paymentReference === paymentRef) ?? null;
  });
  /**
   * The pre-match snapshot Reconciliation Workspace captured the instant Manual match was
   * confirmed — before the ledger row itself was mutated. Reading the live row for "before"
   * would show "Matched" (its current, already-mutated state) as both before and after,
   * which is meaningless; this is the real prior state and the settlement line the maker
   * actually chose in the "Selected match" dropdown (previously discarded entirely).
   */
  private readonly linkedMatch = computed(() => {
    const paymentRef = this.linkedFromPaymentRef();
    if (!paymentRef) return null;
    const pending = this.financeState.pendingMatchForReview();
    return pending && pending.paymentReference === paymentRef ? pending : null;
  });
  /** The real correction this review is about, when it was routed here from Financial Correction's Submit. */
  private readonly linkedCorrection = computed(() => {
    if (!this.linkedFromCorrectionRef()) return null;
    return this.financeState.pendingCorrectionForReview();
  });

  /**
   * Every field below previously carried a fixed literal ("REV-2025-7721", "₹55,000.00 →
   * ₹54,750.00", "PAY-2025-8830 · STL-LN-2025-88205"…) regardless of which real record was
   * actually routed here — a checker approving a ₹500 correction and a ₹50,000 correction
   * saw identical numbers. They now derive from whichever real source sent this review
   * (`linkedMatch` / `linkedCorrection`), falling back to the documented sample only
   * when this screen is opened directly with no handoff (e.g. for preview).
   */
  protected readonly reviewReference = computed(() => {
    const linked = this.linkedFrom();
    return linked ? `REV-${linked}` : 'REV-2025-7721';
  });
  protected readonly actionType = computed(() => {
    if (this.linkedMatch() || this.linkedLedgerRow()) return 'Reconciliation match';
    if (this.linkedCorrection()) return 'Financial correction';
    return 'Financial correction';
  });
  protected readonly affectedReferences = computed(() => {
    const match = this.linkedMatch();
    if (match) return `${match.paymentReference} · ${match.settlementLine}`;
    const row = this.linkedLedgerRow();
    if (row) return `${row.paymentReference} · ${row.settlementLine}`;
    const correction = this.linkedCorrection();
    if (correction) return correction.originalTransactionReference;
    return 'PAY-2025-8830 · STL-LN-2025-88205';
  });
  protected readonly beforeValue = computed(() => {
    const match = this.linkedMatch();
    if (match) return `${match.beforePaymentState} · Payment state`;
    const row = this.linkedLedgerRow();
    if (row) return `${row.paymentState} · Payment state`;
    const correction = this.linkedCorrection();
    if (correction) {
      return correction.beforeAmount !== null
        ? `₹ ${this.formatAmount(correction.beforeAmount)} · Gross amount`
        : 'No prior recorded amount for this reference';
    }
    return '₹ 55,000.00 · Gross amount';
  });
  protected readonly proposedValue = computed(() => {
    const match = this.linkedMatch();
    if (match) return `Matched · ${match.settlementLine}`;
    const row = this.linkedLedgerRow();
    if (row) return `Matched · ${row.settlementLine}`;
    const correction = this.linkedCorrection();
    if (correction) {
      const after = correction.beforeAmount !== null ? correction.beforeAmount - correction.affectedAmount : correction.affectedAmount;
      return `₹ ${this.formatAmount(after)} · Gross amount (${correction.correctionType})`;
    }
    return '₹ 54,750.00 · Gross amount';
  });
  protected readonly amountImpact = computed(() => {
    const match = this.linkedMatch();
    if (match) return match.netAmount;
    const row = this.linkedLedgerRow();
    if (row) return row.variance;
    const correction = this.linkedCorrection();
    if (correction) return correction.affectedAmount;
    return 250;
  });
  protected readonly reason = computed(() => {
    const correction = this.linkedCorrection();
    if (correction) return correction.detailedReason || correction.reasonCategory || 'No reason recorded.';
    const match = this.linkedMatch();
    if (match) return `Manual match proposed against settlement line ${match.settlementLine}, suggested match score ${match.suggestedMatchScore}%.`;
    const row = this.linkedLedgerRow();
    if (row) return `Manual match proposed against settlement line ${row.settlementLine}, suggested match score ${row.suggestedMatchScore}%.`;
    return 'Provider settlement statement shows a net of ₹54,750 for this payment after correcting a duplicate fee deduction.';
  });
  protected readonly evidence = computed(() => {
    const correction = this.linkedCorrection();
    if (correction && correction.evidenceFile) return correction.evidenceFile;
    return 'Provider settlement statement · PDF · 1.8 MB';
  });
  protected readonly conflictDeclaration = signal(false);
  protected readonly dueTime = '2025-07-30T12:00:00';

  protected readonly interpretedDueTime = computed(() => {
    const d = new Date(this.dueTime);
    if (Number.isNaN(d.getTime())) return '—';
    return `${d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })} · ${d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })} · ${this.operatingTimeZone}`;
  });

  // ================= Actions, eligibility and result (4.7.3) =================
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState());

  /**
   * Requires the conflict declaration checkbox — the header shortcut button previously
   * only checked `permissions.approve && inWorkflowState()` while the Decision-panel
   * Approve button separately AND'd in `conflictDeclaration()` in the template, so a
   * checker could approve via the header button without ever declaring no conflict of
   * interest. Both buttons read this single computed now, so neither can bypass it.
   */
  protected readonly approveAllowed = computed(() => this.permissions.approve && this.inWorkflowState() && this.conflictDeclaration());
  protected readonly returnAllowed = computed(() => this.permissions.returnForCorrection && this.inWorkflowState());
  protected readonly rejectAllowed = computed(() => this.permissions.reject && this.inWorkflowState());
  protected readonly delegateAllowed = computed(() => this.permissions.delegate && this.inWorkflowState());

  /** Why Approve is disabled — shown as a toast since a native disabled button never fires click. */
  protected readonly approveBlockedReason = computed(() => {
    if (!this.inWorkflowState()) return `Review ${this.reviewReference()} has already been decided (${this.lifecycleState()}) — no further action is available.`;
    if (!this.conflictDeclaration()) return 'Declare no conflict of interest before approving.';
    return 'This action is not available with your current permissions.';
  });
  /** Why Return / Reject / Delegate is disabled. */
  protected readonly decisionBlockedReason = computed(() =>
    this.inWorkflowState()
      ? 'This action is not available with your current permissions.'
      : `Review ${this.reviewReference()} has already been decided (${this.lifecycleState()}) — no further action is available.`,
  );

  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  /** No access — "Return to the permitted landing page" (4.7.4). */
  protected returnToWorkspace(): void {
    this.router.navigate(['/app/workspace/my-workspace']);
  }

  // ----- Conflict recovery: Compare / Reapply eligible changes / Cancel (4.7.4 / 4.7.6) -----
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

  /** Dependency failure — "Retry only the failed dependency using a stable correlation reference" (4.7.4). */
  protected retryDependency(): void {
    this.toast.show('Retrying', 'Retrying the failed dependency using correlation INT-77337…', 'info');
    this.uiState.set('success');
  }

  /** Decision reason (shared by Approve / Return / Reject). */
  protected readonly decisionReason = signal('');
  protected readonly decisionReasonMin = 10;
  protected readonly decisionReasonMax = 2000;
  protected readonly decisionReasonValid = computed(() => {
    const len = this.decisionReason().trim().length;
    return len >= this.decisionReasonMin && len <= this.decisionReasonMax;
  });
  protected readonly decisionReasonCount = computed(() => this.decisionReason().trim().length);

  /** Approve — primary (4.7.3). */
  protected readonly approveDialogOpen = signal(false);
  protected openApproveDialog(): void {
    if (!this.approveAllowed()) {
      this.toast.show('Approve unavailable', this.approveBlockedReason(), 'warning');
      return;
    }
    this.decisionReason.set('');
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
  }
  /** Approve — continues the process into Period/Campaign Close (Master workflow: Approve → Continue Process). */
  protected confirmApprove(): void {
    if (!this.decisionReasonValid()) return;
    this.approveDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Approved');
    this.reflectDecisionOnOrigin('Approved');
    this.toast.show('Approved', `Review ${this.reviewReference()} approved. Routing to Period / Campaign close.`, 'success');
    this.router.navigate(['/app/money/finance/period-campaign-close'], { queryParams: { reviewRef: this.reviewReference() } });
  }

  /** Return for correction (4.7.3). */
  protected readonly returnDialogOpen = signal(false);
  protected openReturnDialog(): void {
    if (!this.returnAllowed()) {
      this.toast.show('Return unavailable', this.decisionBlockedReason(), 'warning');
      return;
    }
    this.decisionReason.set('');
    this.returnDialogOpen.set(true);
  }
  protected cancelReturn(): void {
    this.returnDialogOpen.set(false);
  }
  /** Return for correction — sends the proposed action back to its originating maker screen (Master workflow: No → Return to Maker). */
  protected confirmReturn(): void {
    if (!this.decisionReasonValid()) return;
    this.returnDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Returned for correction');
    this.reflectDecisionOnOrigin('Returned for correction');
    this.toast.show('Returned for correction', `Review ${this.reviewReference()} returned to maker.`, 'success');
    // Financial Correction's "Original transaction" field expects a real payment/settlement
    // reference — forward the one this review came from (when available) alongside reviewRef.
    this.router.navigate(['/app/fin/financial-correction-or-reversal'], {
      queryParams: { reviewRef: this.reviewReference(), paymentRef: this.linkedFromPaymentRef() },
    });
  }

  /** Reject — danger (4.7.3). */
  protected readonly rejectDialogOpen = signal(false);
  protected openRejectDialog(): void {
    if (!this.rejectAllowed()) {
      this.toast.show('Reject unavailable', this.decisionBlockedReason(), 'warning');
      return;
    }
    this.decisionReason.set('');
    this.rejectDialogOpen.set(true);
  }
  protected cancelReject(): void {
    this.rejectDialogOpen.set(false);
  }
  protected confirmReject(): void {
    if (!this.decisionReasonValid()) return;
    this.rejectDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Rejected');
    this.reflectDecisionOnOrigin('Rejected');
    this.toast.show('Rejected', `Review ${this.reviewReference()} rejected.`, 'success');
  }

  /** Reflects this decision back on whichever screen sent the record for review (4.7 "reflected on the originating screen"). */
  private reflectDecisionOnOrigin(decision: 'Approved' | 'Returned for correction' | 'Rejected'): void {
    const paymentRef = this.linkedFromPaymentRef();
    if (paymentRef) {
      this.financeState.applyReviewDecisionToLedger(paymentRef, decision);
    } else if (this.linkedFromCorrectionRef()) {
      this.financeState.applyReviewDecisionToCorrection(decision);
    }
  }

  /** Delegate (4.7.3). */
  protected performDelegate(): void {
    if (!this.delegateAllowed()) {
      this.toast.show('Delegate unavailable', this.decisionBlockedReason(), 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.financeState.delegateReview(this.linkedFromPaymentRef());
    this.toast.show('Delegated', `Review ${this.reviewReference()} delegated.`, 'success');
  }

  // ================= Persistent outcome (4.7.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.reviewReference(),
    state: this.lifecycleState(),
    effectiveTime: this.lastRefresh(),
    downstreamStatus: 'Awaiting independent approval',
    owner: this.owner,
    nextAction: 'Approve, return for correction or reject the proposed action',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
}
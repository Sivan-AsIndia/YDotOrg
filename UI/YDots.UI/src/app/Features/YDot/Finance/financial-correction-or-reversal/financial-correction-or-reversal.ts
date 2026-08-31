import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import {
  FinanceUiState,
  FinancialCorrectionPermissions,
} from '../../../../Shared/models/finance.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';

/**
 * FIN-UI-08 — Financial correction or reversal.
 *
 * Faithful implementation of section 4.8 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /fin/financial-correction-or-reversal
 *  Purpose          : Correct posted financial truth with a linked compensating action and
 *                     complete audit history.
 *  Primary users    : Finance roles; Finance; Auditor; Finance Maker; Checker; Finance Approver
 *  View permission  : fin.financial-correction-or-reversal.view
 *  Primary action   : Validate impact
 *  History rule     : Only an unused draft without downstream references may be permanently
 *                     deleted; otherwise use a controlled lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

@Component({
  selector: 'app-financial-correction-or-reversal',
  imports: [CommonModule, FormsModule],
  templateUrl: './financial-correction-or-reversal.html',
  styleUrl: './financial-correction-or-reversal.css',
})
export class FinancialCorrectionOrReversalComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  // ================= Task header (4.8.1) =================
  protected readonly pageTitle = 'Financial Correction or Reversal';
  protected readonly pageSubtitle =
    'Correct posted financial truth with a linked compensating action and complete audit history.';
  /** Mutable — Validate/Submit/Approve/Post visibly advance the lifecycle, not just show a toast; backed by the shared Finance state so revisiting this screen (and a Maker-Checker decision on this correction) shows the persisted state. */
  protected readonly lifecycleState = this.financeState.correctionLifecycle;
  protected readonly owner = 'Priya Raghavan · Finance Approver';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 06:10 PM · IST');

  /** Effective permissions decided server-side (4.8.3, 4.8.7). */
  protected readonly permissions: FinancialCorrectionPermissions = {
    view: true,
    validateImpact: true,
    submitCorrection: true,
    approve: true,
    postReversal: true,
    cancelDraft: true,
  };

  private readonly workflowPermittedStates = ['Draft', 'Pending review', 'Ready to post'];

  // ================= Form fields (4.8.2 field contract) =================
  /**
   * Pre-filled from an incoming handoff: ?exceptionRef= (Finance exception case's Correct
   * action, carrying the affected payment/batch reference) or ?paymentRef= (Maker-checker
   * review's Return-for-correction action, when the review came from a payment match).
   * ?reviewRef= alone is the review's own reference, not a transaction — used only as a
   * last resort so the field is never left pointing at the wrong kind of reference.
   */
  protected readonly originalTransaction = signal(
    this.route.snapshot.queryParamMap.get('exceptionRef') ??
    this.route.snapshot.queryParamMap.get('paymentRef') ??
    this.route.snapshot.queryParamMap.get('reviewRef') ??
    'PAY-2025-8830',
  );
  protected readonly correctionType = signal('');
  protected readonly correctionTypeOptions = ['Amount correction', 'Fee correction', 'Tax correction', 'Full reversal', 'Partial reversal'];
  protected readonly affectedAmount = signal<number | null>(null);

  /** Convert a string (or empty) to number|null — used by the template (4.8.2 Affected amount). */
  protected toNumber(value: string | null): number | null {
    return value === '' || value === null ? null : Number(value);
  }
  /**
   * Read-only "Correct value" preview — previously a frozen literal that never moved no
   * matter what Original transaction / Affected amount were actually entered, so it never
   * matched what Maker-Checker Review later shows for the same correction. Derives the
   * before-amount via the same shared lookup Maker-Checker Review uses, so both screens
   * agree on the same real numbers for the same correction.
   */
  protected readonly correctValue = computed(() => {
    const before = this.financeState.findAmountForReference(this.originalTransaction());
    const amount = this.affectedAmount();
    if (before === null || amount === null) return '—';
    return `₹ ${this.formatAmount(before - amount)}`;
  });
  protected readonly reasonCategory = signal('');
  protected readonly reasonCategoryOptions = ['Provider statement correction', 'Duplicate processing', 'System error', 'Approved adjustment'];
  protected readonly detailedReason = signal('');
  protected readonly detailedReasonMin = 10;
  protected readonly detailedReasonMax = 2000;
  protected readonly detailedReasonValid = computed(() => {
    const len = this.detailedReason().trim().length;
    return len >= this.detailedReasonMin && len <= this.detailedReasonMax;
  });
  protected readonly detailedReasonCount = computed(() => this.detailedReason().trim().length);
  protected readonly accountingDate = signal('2025-07-30');
  protected readonly evidenceFile = signal('Correction evidence · PDF · 480 KB');
  protected readonly requester = 'Arjun Menon · Finance Maker';
  protected readonly checker = 'Divya Krishnan · Finance Checker';
  protected readonly downstreamImpact = signal('');
  protected readonly resultingBalance = { label: 'Balanced', tone: 'success', icon: '✓' };

  // ================= Validation (4.8.2) =================
  protected readonly amountValid = computed(() => {
    const v = this.affectedAmount();
    return v !== null && v !== undefined && v > 0 && v <= 1000000;
  });

  protected readonly formComplete = computed(
    () => !!this.originalTransaction() && !!this.correctionType() && this.amountValid() && !!this.reasonCategory(),
  );

  // ================= Actions, eligibility and result (4.8.3) =================
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState());

  protected readonly validateImpactAllowed = computed(() => this.permissions.validateImpact && this.inWorkflowState());
  protected readonly submitAllowed = computed(() => this.permissions.submitCorrection && this.formComplete() && this.inWorkflowState());
  protected readonly approveAllowed = computed(() => this.permissions.approve && this.inWorkflowState());
  protected readonly postReversalAllowed = computed(() => this.permissions.postReversal && this.inWorkflowState());
  protected readonly cancelDraftAllowed = computed(() => this.permissions.cancelDraft && this.lifecycleState() === 'Draft');

  /** Why Validate impact / Approve / Post reversal is disabled — shown as a toast since a native disabled button never fires click. */
  protected readonly workflowBlockedReason = computed(() =>
    `This correction is currently "${this.lifecycleState()}" — this action isn't available in that state.`,
  );
  /** Why Cancel draft is disabled. */
  protected readonly cancelDraftBlockedReason = computed(() =>
    `Cancel draft is only available while this correction is still a Draft — it's currently "${this.lifecycleState()}".`,
  );
  /** Explains why Submit is disabled, since a disabled button gives no other feedback when required fields are missing. */
  protected readonly submitTitle = computed(() => {
    if (this.submitAllowed()) return 'Submit';
    const missing: string[] = [];
    if (!this.originalTransaction()) missing.push('Original transaction');
    if (!this.correctionType()) missing.push('Correction type');
    if (!this.amountValid()) missing.push('Affected amount');
    if (!this.reasonCategory()) missing.push('Reason category');
    return missing.length ? `Complete required fields to submit: ${missing.join(', ')}` : this.workflowBlockedReason();
  });

  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  protected performValidateImpact(): void {
    if (!this.validateImpactAllowed()) {
      this.toast.show('Validate impact unavailable', this.workflowBlockedReason(), 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.toast.show('Impact validated', `${this.originalTransaction()} — downstream impact confirmed.`, 'success');
  }

  /** Submit — routes into Maker-Checker Review for independent approval (Master workflow). */
  protected submitCorrection(): void {
    if (!this.submitAllowed()) {
      this.toast.show('Submit unavailable', this.submitTitle(), 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Pending review');
    this.financeState.startReview();
    // Carries the real proposed correction into Maker-Checker Review's "Before / after
    // impact" card — without this it showed a hardcoded sample regardless of what was
    // actually submitted here.
    this.financeState.recordCorrectionForReview({
      originalTransactionReference: this.originalTransaction(),
      correctionType: this.correctionType(),
      affectedAmount: this.affectedAmount() ?? 0,
      reasonCategory: this.reasonCategory(),
      detailedReason: this.detailedReason(),
      evidenceFile: this.evidenceFile(),
    });
    this.toast.show('Correction submitted', 'Routing to Maker-Checker review for approval.', 'success');
    this.router.navigate(['/app/fin/maker-checker-review'], { queryParams: { correctionRef: this.originalTransaction() } });
  }

  protected approve(): void {
    if (!this.approveAllowed()) {
      this.toast.show('Approve unavailable', this.workflowBlockedReason(), 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.lifecycleState.set('Ready to post');
    this.toast.show('Correction approved', `${this.originalTransaction()} — ready to post.`, 'success');
  }

  /**
   * Posts a real, linked compensating record instead of only flipping a lifecycle label —
   * the original transaction (`originalTransaction`) is never touched; the reversal is a
   * separate record referencing it (4.8.3 Post reversal / 4.8 "preserve linked history").
   */
  protected postReversal(): void {
    if (!this.postReversalAllowed()) {
      this.toast.show('Post reversal unavailable', this.workflowBlockedReason(), 'warning');
      return;
    }
    const reversal = this.financeState.postReversal(this.originalTransaction(), this.correctionType(), this.affectedAmount() ?? 0);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.toast.show('Reversal posted', `${reversal.reversalReference} — compensating entry linked to ${this.originalTransaction()}.`, 'success');
  }

  /** Cancel draft — danger (4.8.3). */
  protected readonly cancelDialogOpen = signal(false);
  protected readonly cancelReason = signal('');
  protected readonly cancelReasonMin = 10;
  protected readonly cancelReasonMax = 2000;
  protected readonly cancelReasonValid = computed(() => {
    const len = this.cancelReason().trim().length;
    return len >= this.cancelReasonMin && len <= this.cancelReasonMax;
  });
  protected readonly cancelReasonCount = computed(() => this.cancelReason().trim().length);

  protected openCancelDialog(): void {
    if (!this.cancelDraftAllowed()) {
      this.toast.show('Cancel draft unavailable', this.cancelDraftBlockedReason(), 'warning');
      return;
    }
    this.cancelReason.set('');
    this.cancelDialogOpen.set(true);
  }
  protected cancelCancel(): void {
    this.cancelDialogOpen.set(false);
  }
  protected confirmCancel(): void {
    if (!this.cancelReasonValid()) return;
    this.cancelDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    // "Persist cancelled lifecycle state" (4.8.3 Cancel draft) — this was previously a no-op
    // that closed the dialog without recording anything.
    this.lifecycleState.set('Cancelled');
    this.toast.show('Draft cancelled', `${this.originalTransaction()} — draft cancelled.`, 'success');
  }

  // ================= Persistent outcome (4.8.1) =================
  /** Stable, deterministic reference derived from the original transaction — the same correction always resolves to the same reference instead of one hardcoded literal shared by every correction. */
  protected readonly correctionReference = computed(() => `CORR-${this.originalTransaction()}`);
  /** The linked compensating record once Post reversal has actually created one — read from the shared store, not a local flag. */
  protected readonly postedReversal = computed(() => this.financeState.reversalsFor(this.originalTransaction())[0] ?? null);
  protected readonly persistentOutcome = computed(() => {
    const reversal = this.postedReversal();
    return {
      reference: reversal ? reversal.reversalReference : this.correctionReference(),
      state: this.lifecycleState(),
      effectiveTime: this.lastRefresh(),
      downstreamStatus: reversal
        ? `Compensating entry linked to ${reversal.originalTransactionReference}`
        : this.submitAllowed()
          ? 'Ready to submit'
          : 'Draft — remaining required information',
      owner: this.owner,
      nextAction: reversal
        ? 'No further action'
        : this.submitAllowed()
          ? 'Submit correction for maker-checker review'
          : 'Complete required fields to submit',
    };
  });

  // ================= Formatting helpers =================
  protected formatAmount(value: number | null): string {
    if (value === null || value === undefined) return '—';
    return value.toLocaleString('en-IN');
  }
}
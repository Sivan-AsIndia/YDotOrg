import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import {
  FinanceUiState,
  InstrumentType,
  
  OfflineDonationPermissions,
  ScopeAwareOption,
} from '../../../../Shared/models/finance.model';
import { ToastService } from '../../../../Shared/services/toast.service';
import { FinanceStateService } from '../shared/finance-state.service';
import { OfflineDonationDraft } from '../shared/finance.model';
import { GeoMasterService } from '../../../../Shared/services/geo-master.service';

/**
 * SCR-FIN-003 — Offline donation entry.
 *
 * Faithful implementation of section 4.3 / 7.3 of the YDot FIN Practical UI/UX
 * Generation Specification (Dark Meadow v1.2).
 *
 *  Route            : /money/finance/offline-donation-entry
 *  Purpose          : Capture supported offline instrument and donor/campaign evidence.
 *  Primary users    : Finance Maker
 *  View permission  : fin.offline-donation-entry.view
 *  Primary action   : Draft
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

@Component({
  selector: 'app-offline-donation-entry',
  imports: [CommonModule, FormsModule],
  templateUrl: './offline-donation-entry.html',
  styleUrl: './offline-donation-entry.css',
})
export class OfflineDonationEntryComponent {
  private readonly geoMasters = inject(GeoMasterService);

  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly financeState = inject(FinanceStateService);

  constructor() {
    // The currency catalogue, loaded once. `GeoMasterService` caches it and never throws, so a
    // failure here leaves the dropdown empty rather than breaking the page.
    this.geoMasters
      .getCurrencies()
      .subscribe((currencies) => this.currencyCatalogue.set(currencies.map((currency) => currency.code)));

    this.hydrateFromSavedDraft();
  }

  // ================= Task header (4.3.1) =================
  protected readonly pageTitle = 'Offline Donation Entry';
  protected readonly pageSubtitle =
    'Capture supported offline instrument and donor/campaign evidence. All counts and actions are restricted by effective scope.';
  protected readonly lifecycleState = 'Draft';
  protected readonly owner = 'Arjun Menon · Finance Maker';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('Today, 03:40 PM · IST');

  /** Effective permissions decided server-side (4.3.3, 4.3.7). */
  protected readonly permissions: OfflineDonationPermissions = {
    view: true,
    draft: true,
    duplicateCheck: true,
    submit: true,
    deleteDraft: true,
  };

  private readonly workflowPermittedStates = ['Draft', 'No record'];

  /**
   * Reopening this screen must show the same draft that was last saved (4.3 "Reopen the
   * draft and verify all values remain") — Angular recreates this component on every
   * navigation, so the only place that draft can live is the shared Finance state service.
   */
  private hydrateFromSavedDraft(): void {
    const saved = this.financeState.donationDraft();
    if (!saved) return;
    this.instrumentType.set(saved.instrumentType);
    this.transactionReference.set(saved.transactionReference);
    this.donationDate.set(saved.donationDate);
    this.amount.set(saved.amount);
    this.currency.set(saved.currency);
    this.donor.set(saved.donor);
    this.campaign.set(saved.campaign);
    this.bankAccount.set(saved.bankAccount);
    this.depositDate.set(saved.depositDate);
    this.evidenceFile.set(saved.evidenceFile);
    this.notes.set(saved.notes);
    this.draftReference.set(saved.draftReference);
    this.approvalState.set(saved.approvalState);
  }

  // ================= Form fields (4.3.2 field contract) =================
  /** Instrument type — searchable controlled choice (4.3.2). */
  protected readonly instrumentType = signal<InstrumentType | ''>('');
  protected readonly instrumentCatalogue: readonly InstrumentType[] = [
    'Cash',
    'Cheque',
    'Bank Transfer',
    'Demand Draft',
    'Pay Order',
    'Other',
  ];

  /** Transaction or receipt reference — read-only (4.3.2). */
  protected readonly transactionReference = signal('TXN-OFF-2025-6612');

  /** Donation date — date picker with time-zone label (4.3.2). */
  protected readonly donationDate = signal('');
  protected readonly interpretedDate = computed(() => {
    if (!this.donationDate()) return `Select a date · ${this.operatingTimeZone}`;
    const d = new Date(this.donationDate());
    return `${d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })} · ${this.operatingTimeZone}`;
  });

  /** Amount — decimal currency input (4.3.2). */
  protected readonly amount = signal<number | null>(null);

  /** Convert a string (or empty) to number|null — used by the template (4.3.2 Amount). */
  protected toNumber(value: string | null): number | null {
    return value === '' || value === null ? null : Number(value);
  }

  /** Currency — searchable controlled choice (4.3.2). */
  protected readonly currency = signal('INR');
  /** The currency catalogue, from GlobalMaster rather than four literals. */
  protected readonly currencyCatalogue = signal<readonly string[]>([]);

  /** Donor — scope-aware searchable selector (conditional) (4.3.2). */
  protected readonly donor = signal('');
  protected readonly donorOptions: readonly ScopeAwareOption[] = [
    { reference: 'DON-0000', name: 'Anonymous donor', context: 'No identity captured', initials: 'AN', tone: 'muted' },
    { reference: 'DON-2025-0442', name: 'Ramesh Kumar', context: 'Individual · Tamil Nadu', initials: 'RK', tone: 'meadow' },
    { reference: 'DON-2025-0443', name: 'Anitha S', context: 'Individual · Kerala', initials: 'AS', tone: 'blue' },
    { reference: 'DON-2025-0444', name: 'Zentra Corporation', context: 'Corporate · Karnataka', initials: 'ZC', tone: 'plum' },
  ];

  /** Campaign or fund — scope-aware searchable selector (required) (4.3.2). */
  protected readonly campaign = signal('');
  protected readonly campaignOptions = [
    { reference: 'CAMP-2025-0011', label: 'Educate a Child 2025' },
    { reference: 'CAMP-2025-0013', label: 'Health Camp Rural Drive' },
    { reference: 'CAMP-2025-0015', label: 'Women Empowerment 2025' },
  ];

  /** Bank or collection account (required) (4.3.2). */
  protected readonly bankAccount = signal('');
  protected readonly bankAccountOptions = [
    { reference: 'ACC-BNK-00014', label: 'YDot Current Account · HDFC' },
    { reference: 'ACC-BNK-00022', label: 'YDot Collection Account · SBI' },
  ];

  /** Deposit date — conditional (4.3.2). */
  protected readonly depositDate = signal('');

  /** Evidence document — secure file uploader (required, confidential) (4.3.2). */
  protected readonly evidenceFile = signal('');
  protected readonly evidenceStatus = computed(() => {
    if (!this.evidenceFile()) return { label: 'No file uploaded', tone: 'muted' };
    return { label: 'Uploaded · Scan pending', tone: 'warn' };
  });

  /** Notes — structured textarea with character counter (conditional) (4.3.2). */
  protected readonly notes = signal('');
  protected readonly notesMin = 10;
  protected readonly notesMax = 2000;
  protected readonly notesCount = computed(() => this.notes().trim().length);

  /** Duplicate candidates — read-only (4.3.2). */
  protected readonly duplicateCandidates: readonly string[] = ['DON-2025-0442 · ₹5,000 · Cash', 'DON-2025-0411 · ₹4,500 · Cheque'];

  /** Draft reference — read-only (4.3.2). */
  protected readonly draftReference = signal('DRF-OFF-2025-8891');

  /** Approval state — read-only (4.3.2). */
  /** Mutable — Draft/Submit visibly update the badge, not just a toast (4.3.2 Approval state). */
  protected readonly approvalState = signal({ label: 'Draft — incomplete', tone: 'warn', icon: '!' });

  /** Whether duplicate candidates may exist (drives the duplicate-check action). */
  protected readonly showDuplicateCandidates = signal(false);

  // ================= Validation (4.3.2 / 4.3.6) =================
  protected readonly amountValid = computed(() => {
    const v = this.amount();
    return v !== null && v !== undefined && v > 0 && v <= 1000000;
  });

  protected readonly formComplete = computed(
    () =>
      !!this.instrumentType() &&
      !!this.donationDate() &&
      this.amountValid() &&
      !!this.campaign() &&
      !!this.bankAccount() &&
      !!this.evidenceFile(),
  );

  // ================= UI states (4.3.4 / 4.3.7) =================
  protected readonly uiState = signal<FinanceUiState>('ready');
  protected setUiState(state: FinanceUiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  /** No access — "Return to the permitted landing page" (4.3.4). */
  protected returnToWorkspace(): void {
    this.router.navigate(['/app/workspace/my-workspace']);
  }

  // ----- Conflict recovery: Compare / Reapply eligible changes / Cancel (4.3.4 / 4.3.6) -----
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

  /** Dependency failure — "Retry only the failed dependency using a stable correlation reference" (4.3.4). */
  protected retryDependency(): void {
    this.toast.show('Retrying', 'Retrying the failed dependency using correlation INT-77333…', 'info');
    this.uiState.set('success');
  }

  // ================= Actions, eligibility and result (4.3.3) =================
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState);

  protected readonly draftAllowed = computed(() => this.permissions.draft && this.inWorkflowState());
  protected readonly submitAllowed = computed(() => this.permissions.submit && this.formComplete() && this.inWorkflowState());

  /** Explains why Submit is disabled, since a disabled button gives no other feedback when required fields are missing. */
  protected readonly submitTitle = computed(() => {
    if (this.submitAllowed()) return 'Submit';
    const missing: string[] = [];
    if (!this.instrumentType()) missing.push('Instrument type');
    if (!this.donationDate()) missing.push('Donation date');
    if (!this.amountValid()) missing.push('Amount');
    if (!this.campaign()) missing.push('Campaign or fund');
    if (!this.bankAccount()) missing.push('Bank or collection account');
    if (!this.evidenceFile()) missing.push('Evidence document');
    return missing.length ? `Complete required fields to submit: ${missing.join(', ')}` : 'Submit is not available in the current state';
  });
  /** Previously allowed whenever the static 'Draft' label matched, even with nothing saved yet — now also requires a draft to actually exist in shared state, so "Delete unused draft" isn't clickable on a blank/never-saved form. */
  protected readonly deleteDraftAllowed = computed(
    () => this.permissions.deleteDraft && this.lifecycleState === 'Draft' && this.financeState.donationDraft() !== null,
  );
  /** Explains why Delete draft is disabled, since a disabled button gives no other feedback when nothing has been saved yet. */
  protected readonly deleteDraftTitle = computed(() =>
    this.deleteDraftAllowed() ? 'Delete draft' : 'No saved draft to delete — save a draft first.',
  );
  protected readonly duplicateCheckAllowed = computed(() => this.permissions.duplicateCheck && this.inWorkflowState());

  /** Draft — primary action (4.3.3). */
  protected saveDraft(): void {
    if (!this.draftAllowed()) return;
    if (!this.formComplete()) {
      this.uiState.set('validation');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.approvalState.set({ label: 'Draft — complete', tone: 'info', icon: 'i' });
    this.financeState.saveDonationDraft(this.currentDraftSnapshot());
    this.toast.show('Draft saved', `Reference ${this.draftReference()} saved.`, 'success');
  }

  /** Duplicate check (4.3.3). */
  protected runDuplicateCheck(): void {
    if (!this.duplicateCheckAllowed()) {
      this.toast.show('Duplicate check unavailable', 'This action is not available with your current permissions.', 'warning');
      return;
    }
    this.showDuplicateCandidates.set(true);
    this.uiState.set(this.showDuplicateCandidates() ? 'duplicate' : 'ready');
  }

  /** Submit — execute idempotently (4.3.3). Routes into Reconciliation per the documented FIN workflow. */
  protected submit(): void {
    if (!this.submitAllowed()) {
      this.toast.show('Submit unavailable', this.submitTitle(), 'warning');
      return;
    }
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    this.approvalState.set({ label: 'Submitted', tone: 'success', icon: '✓' });
    const draftReference = this.draftReference();
    const campaignLabel = this.campaignOptions.find((c) => c.reference === this.campaign())?.label ?? this.campaign();
    this.financeState.submitDonation(this.currentDraftSnapshot(), campaignLabel);
    this.toast.show('Submitted', `Reference ${draftReference} submitted for offline donation processing. Routing to Reconciliation workspace.`, 'success');
    // The new reconciliation ledger row is keyed by the transaction reference (the donation's
    // paymentReference), not the draft reference — pass that so the destination's search prefill finds it.
    this.router.navigate(['/app/money/finance/reconciliation-workspace'], { queryParams: { donationRef: this.transactionReference() } });
  }

  /** Delete unused draft — danger menu (4.3.3 / 4.3.6 high-risk). */
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteReason = signal('');
  protected readonly deleteReasonMin = 10;
  protected readonly deleteReasonMax = 2000;
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.deleteReasonMin && len <= this.deleteReasonMax;
  });
  protected readonly deleteReasonCount = computed(() => this.deleteReason().trim().length);

  protected openDeleteDialog(): void {
    if (!this.deleteDraftAllowed()) {
      this.toast.show('Delete unavailable', this.deleteDraftTitle(), 'warning');
      return;
    }
    this.deleteReason.set('');
    this.deleteDialogOpen.set(true);
  }
  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
  }
  protected confirmDelete(): void {
    if (!this.deleteReasonValid()) return;
    this.deleteDialogOpen.set(false);
    this.uiState.set('success');
    this.lastRefresh.set(this.financeState.nowDisplay());
    const deletedReference = this.draftReference();
    this.financeState.deleteDonationDraft();
    this.resetToFreshDraft();
    this.toast.show('Draft deleted', `Draft ${deletedReference} was deleted.`, 'success');
  }

  /** After deleting the unused draft, present a fresh blank draft slot rather than resurrecting the deleted reference. */
  private resetToFreshDraft(): void {
    const suffix = Math.floor(1000 + Math.random() * 9000);
    this.instrumentType.set('');
    this.transactionReference.set(`TXN-OFF-2025-${suffix}`);
    this.donationDate.set('');
    this.amount.set(null);
    this.currency.set('INR');
    this.donor.set('');
    this.campaign.set('');
    this.bankAccount.set('');
    this.depositDate.set('');
    this.evidenceFile.set('');
    this.notes.set('');
    this.showDuplicateCandidates.set(false);
    this.draftReference.set(`DRF-OFF-2025-${suffix}`);
    this.approvalState.set({ label: 'Draft — incomplete', tone: 'warn', icon: '!' });
  }

  // ================= Persistent outcome (4.3.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.draftReference(),
    state: this.lifecycleState,
    effectiveTime: this.lastRefresh(),
    downstreamStatus: this.submitAllowed() ? 'Ready to submit' : 'Draft — remaining required information',
    owner: this.owner,
    nextAction: this.submitAllowed() ? 'Submit for offline donation processing' : 'Complete required fields to submit',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number | null): string {
    if (value === null || value === undefined) return '—';
    return value.toLocaleString('en-IN');
  }

  /** Snapshot of the current form values in the shape the Finance state service persists. */
  private currentDraftSnapshot(): OfflineDonationDraft {
    return {
      instrumentType: this.instrumentType(),
      transactionReference: this.transactionReference(),
      donationDate: this.donationDate(),
      amount: this.amount(),
      currency: this.currency(),
      donor: this.donor(),
      campaign: this.campaign(),
      bankAccount: this.bankAccount(),
      depositDate: this.depositDate(),
      evidenceFile: this.evidenceFile(),
      notes: this.notes(),
      draftReference: this.draftReference(),
      approvalState: this.approvalState(),
    };
  }
}
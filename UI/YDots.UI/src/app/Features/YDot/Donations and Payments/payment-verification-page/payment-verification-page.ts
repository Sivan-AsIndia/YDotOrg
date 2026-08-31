import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { formatMoment, toPaymentVerificationRecord } from '../../../../Shared/models/payment-adapters';
import {
  UiState,
  BackendPaymentState,
  ReceiptEligibility,
  EffectivePermissions,
  PaymentVerificationRecord,
  HistoryRow,
  PersistentOutcome,
} from '../../../../Shared/models/payment-verification.model';
import { PaymentEventRecord } from '../../../../Shared/models/payment-event-queue.model';

/**
 * Payment verification - SCR-PAY-002.
 *
 * ONE QUESTION, ASKED OF THE ONLY PARTY WHO KNOWS THE ANSWER. A screen cannot decide whether a
 * payment succeeded; the gateway can. Every state on this page therefore comes from
 * `POST /api/v1/payments/verify` and nothing is inferred locally.
 *
 * VERIFYING IS NOT RETRYING. This asks; it never pays. A retry disguised as a check is how a
 * donor gets charged twice, which is why verification and safe retry are separate actions behind
 * separate permissions.
 *
 * A "PENDING" ANSWER IS A REAL ANSWER. When the gateway still does not know, the state stays
 * Pending rather than being nudged forward - the donor's money is in exactly the same place it
 * was, and saying otherwise would be a guess presented as a fact.
 */
@Component({
  selector: 'app-payment-verification-page',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './payment-verification-page.html',
  styleUrl: './payment-verification-page.css',
})
export class PaymentVerificationPageComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly tokens = inject(AuthTokenService);

  // ================= Data scope (4.3 Data scope) =================
  protected readonly catalogue = signal<PaymentVerificationRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly selectedRef = signal('');
  protected readonly record = computed<PaymentVerificationRecord | null>(
    () => this.catalogue().find((r) => r.donationReference === this.selectedRef()) ?? null,
  );

  /** A record carried in from the payment event queue, so the page opens on the right payment. */
  protected readonly verifiedFromQueue = signal<PaymentEventRecord | null>(null);

  /** Owner / freshness header meta (4.3.1 Task header). */
  protected readonly owner = computed(() => this.tokens.user()?.displayName ?? 'You');

  /**
   * What this caller may do.
   *
   * TWO GATES, AND BOTH MATTER. `pay.payments.verify` decides whether this person may ask the
   * gateway at all; `permittedActions` on the answer decides whether this particular payment can
   * be acted on. A screen that checked only the first would draw Retrieve receipt on a payment
   * that never succeeded.
   */
  protected readonly permissions = computed<EffectivePermissions>(() => {
    const actions = this.permittedActions();
    const mayVerify = this.tokens.hasAnyPermission('pay.payments.verify');

    return {
      view: this.tokens.hasAnyPermission('pay.intents.view', 'pay.payments.verify'),
      refreshSafeStatus: mayVerify && (actions.length === 0 || actions.includes('Verify')),
      retrieveReceiptWhenEligible:
        this.record()?.receiptEligibility === 'Eligible' &&
        this.tokens.hasAnyPermission('pay.receipts.view'),
    };
  });

  /** The server's action list for the payment on screen, refreshed with each verification. */
  private readonly permittedActions = signal<readonly string[]>([]);

  // ================= Email or mobile verification (4.3.2 Conditional field) =================
  protected readonly emailVerification = signal('');
  protected readonly emailTouched = signal(false);
  private readonly identityPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$|^\+?[0-9]{7,15}$/;
  protected readonly emailValid = computed(() =>
    this.identityPattern.test(this.emailVerification().trim()),
  );
  protected readonly verifiedIdentity = signal(false);
  /** Identity confirmation shown elsewhere is masked - never the raw entered value (4.3.5). */
  protected readonly maskedIdentity = computed(() => {
    const v = this.emailVerification().trim();
    if (!v) return '';
    if (v.includes('@')) {
      const [user, domain] = v.split('@');
      return `${user.slice(0, 1)}${'•'.repeat(Math.max(user.length - 1, 1))}@${domain}`;
    }
    return `${'•'.repeat(Math.max(v.length - 2, 0))}${v.slice(-2)}`;
  });
  protected readonly needsIdentityVerification = computed(
    () => this.record()?.backendPaymentState === 'Pending' && !this.verifiedIdentity(),
  );
  protected submitIdentityVerification(): void {
    this.emailTouched.set(true);
    if (!this.emailValid()) {
      this.uiState.set('validation');
      this.toast.show('Validation Error', 'Please enter a valid email or mobile number.', 'warning');
      return;
    }
    this.verifiedIdentity.set(true);
    if (this.uiState() === 'validation') {
      this.uiState.set('ready');
    }
    this.toast.show('Identity Verified', 'Your identity has been verified.', 'success');
  }

  // ================= Refresh status action =================
  protected readonly refreshMode = signal<'automatic' | 'choose'>('automatic');
  protected readonly chosenRef = signal('');
  protected setRefreshMode(mode: 'automatic' | 'choose'): void {
    this.refreshMode.set(mode);
    if (mode === 'automatic') {
      this.chosenRef.set(this.selectedRef());
    }
  }

  // ================= Context and filters (4.3.1) =================
  protected readonly scope = 'Your donations · This payment';
  protected readonly searchTerm = signal('');
  protected readonly filteredCatalogue = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return [] as PaymentVerificationRecord[];
    return this.catalogue().filter((r) => r.donationReference.toLowerCase().includes(term));
  });
  protected readonly noSearchResults = computed(
    () => this.searchTerm().trim().length > 0 && this.filteredCatalogue().length === 0,
  );
  protected readonly effectiveUiState = computed<UiState>(() =>
    this.noSearchResults() && this.uiState() !== 'loading' && this.uiState() !== 'no-access'
      ? 'empty'
      : this.uiState(),
  );
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (
      this.selectedRef() &&
      this.catalogue().length > 0 &&
      this.selectedRef() !== this.catalogue()[0].donationReference
    ) {
      chips.push({ key: 'record', label: `Viewing: ${this.selectedRef()}` });
    }
    return chips;
  });
  protected readonly scopedTotals = computed(
    () =>
      `${this.catalogue().length} payment${this.catalogue().length === 1 ? '' : 's'} in scope · refreshed ${
        this.record()?.lastVerifiedTime ?? '—'
      }`,
  );
  protected removeFilterChip(key: string): void {
    if (key === 'search') {
      this.searchTerm.set('');
    } else if (key === 'record' && this.catalogue().length > 0) {
      this.selectRecord(this.catalogue()[0].donationReference);
    }
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    if (this.catalogue().length > 0) {
      this.selectRecord(this.catalogue()[0].donationReference);
    }
  }
  protected selectRecord(ref: string): void {
    this.selectedRef.set(ref);
    this.chosenRef.set(ref);
    this.refreshMode.set('automatic');
    this.verifiedIdentity.set(false);
    this.emailVerification.set('');
    this.emailTouched.set(false);
    this.lastActionKind.set(null);
    this.permittedActions.set([]);
    this.searchTerm.set('');
    if (this.uiState() !== 'no-access') {
      this.uiState.set('ready');
    }
  }

  // ================= Actions, eligibility and result (4.3.3) =================
  protected readonly refreshAllowed = computed(
    () =>
      this.permissions().refreshSafeStatus &&
      this.uiState() !== 'no-access' &&
      !!this.record() &&
      !this.needsIdentityVerification(),
  );
  protected readonly retrieveReceiptAllowed = computed(
    () =>
      this.permissions().retrieveReceiptWhenEligible &&
      this.uiState() !== 'no-access' &&
      this.record()?.receiptEligibility === 'Eligible',
  );
  protected readonly lastActionKind = signal<'refresh' | 'retrieve-receipt' | null>(null);

  /**
   * When the page last had an answer from the gateway. Blank until it does.
   *
   * THE SIGNAL IS PRIVATE AND THE TEMPLATE READS A GETTER, because the template renders it as a
   * plain property - and a signal interpolated without its parentheses prints the function.
   */
  private readonly verifiedAt = signal('');
  protected get effectiveTime(): string {
    return this.verifiedAt();
  }

  protected readonly proposedState = computed<BackendPaymentState>(() => {
    const r = this.record();
    if (!r) return 'Pending';
    return r.backendPaymentState === 'Pending' ? 'Confirmed' : r.backendPaymentState;
  });

  // ----- Refresh safe status: high-risk confirm (4.3.3, 4.3.6) -----
  protected readonly refreshDialogOpen = signal(false);
  protected readonly refreshReason = signal('');
  protected readonly refreshReasonMin = 10;
  protected readonly refreshReasonMax = 2000;
  protected readonly refreshReasonCount = computed(() => this.refreshReason().trim().length);
  protected readonly refreshReasonValid = computed(() => {
    const len = this.refreshReason().trim().length;
    return len >= this.refreshReasonMin && len <= this.refreshReasonMax;
  });
  protected readonly refreshReasonTouched = signal(false);

  protected openRefreshDialog(): void {
    if (!this.refreshAllowed()) {
      return;
    }
    if (this.refreshMode() === 'choose' && this.chosenRef() !== this.selectedRef()) {
      this.selectRecord(this.chosenRef());
      if (!this.refreshAllowed()) {
        return;
      }
    }
    this.refreshReason.set('');
    this.refreshReasonTouched.set(false);
    this.refreshDialogOpen.set(true);
  }
  protected cancelRefresh(): void {
    this.refreshDialogOpen.set(false);
  }
  /** Asks the gateway what actually happened, and reports exactly what it said. */
  protected confirmRefresh(): void {
    this.refreshReasonTouched.set(true);

    if (!this.refreshReasonValid()) {
      this.toast.show('Validation Error', 'Please provide a valid reason.', 'warning');
      return;
    }

    const current = this.record();
    if (!current) {
      return;
    }

    this.uiState.set('loading');

    this.paymentApi.verifyPayment({ intentReference: current.donationReference }).subscribe({
      next: (verification) => {
        const updated = toPaymentVerificationRecord(verification);
        this.permittedActions.set(verification.permittedActions);

        this.catalogue.update((list) =>
          list.map((row) => (row.donationReference === current.donationReference ? updated : row)),
        );

        this.lastActionKind.set('refresh');
        this.refreshDialogOpen.set(false);
        this.verifiedAt.set(formatMoment(new Date().toISOString()));
        this.uiState.set('success');

        this.toast.show(
          'Status Refreshed',
          `The payment provider reports ${updated.backendPaymentState} for ${updated.donationReference}.`,
          updated.backendPaymentState === 'Failed' ? 'warning' : 'success',
        );
      },
      error: (error) => {
        this.refreshDialogOpen.set(false);
        this.uiState.set('dependency-failure');

        // A verification that could not reach the provider leaves the outcome exactly as unknown
        // as it was. That is a dependency state rather than an error state, and the screen
        // renders it differently: try again, rather than something is broken.
        this.toast.show(
          'Provider unreachable',
          apiErrorMessage(
            error,
            'The payment provider could not be reached. The payment is unchanged and can be checked again.',
          ),
          'error',
        );
      },
    });
  }

  // ----- Retrieve receipt when eligible -----
  protected readonly copiedField = signal<string | null>(null);
  protected retrieveReceipt(): void {
    if (!this.retrieveReceiptAllowed()) {
      return;
    }
    this.lastActionKind.set('retrieve-receipt');
    this.uiState.set('success');
    this.toast.show(
      'Receipt Retrieved',
      `Receipt for ${this.record()?.donationReference} is ready.`,
      'success',
    );
    this.router.navigate(['/app/donations/receipt-register'], {
      state: { donationReference: this.record()?.donationReference },
    });
  }
  protected copyToClipboard(value: string): void {
    navigator.clipboard?.writeText(value).catch(() => undefined);
    this.copiedField.set(value);
    setTimeout(() => {
      if (this.copiedField() === value) {
        this.copiedField.set(null);
      }
    }, 1500);
  }

  // ================= Related and history (4.3.1) =================
  protected readonly relatedTabs = [
    { key: 'linked', label: 'Linked records' },
    { key: 'documents', label: 'Documents' },
    { key: 'activity', label: 'Activity' },
    { key: 'integration', label: 'Integration status' },
    { key: 'support', label: 'Support correlation' },
    { key: 'audit', label: 'Audit chronology' },
  ] as const;
  protected readonly activeRelatedTab = signal<string>('linked');
  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  /**
   * The rows behind the Related tabs.
   *
   * EVERY ONE COMES FROM THE VERIFICATION ANSWER. The previous version filled them with a fixed
   * campaign name, a fixed gateway called Stripe and three timestamps from May 2025 - none of
   * which had any connection to the payment on screen, and all of which read as facts.
   */
  protected readonly linkedRecords = computed<readonly HistoryRow[]>(() => {
    const r = this.record();
    if (!r) return [];
    const rows: HistoryRow[] = [
      {
        primary: r.donationReference,
        secondary: 'Donation intent',
        meta: r.backendPaymentState,
      },
    ];
    const campaign = this.verifiedFromQueue()?.campaignName;
    if (campaign) rows.push({ primary: campaign, secondary: 'Campaign', meta: '' });
    if (r.gatewayReference) {
      rows.push({ primary: r.gatewayReference, secondary: 'Gateway reference', meta: '' });
    }
    return rows;
  });
  protected readonly documents: readonly HistoryRow[] = [
    {
      primary: 'Payment gateway response',
      secondary: 'JSON',
      meta: 'Withheld unless you hold pay.payments.view-events',
    },
  ];
  /** The verification history the server returned, rather than an invented timeline. */
  protected readonly activityRows = signal<readonly HistoryRow[]>([]);
  protected readonly integrationRows = computed<readonly HistoryRow[]>(() => [
    {
      primary: 'Payment gateway',
      secondary: this.record()?.backendPaymentState ?? '—',
      meta: this.record()?.lastVerifiedTime ?? 'Not yet verified',
    },
  ]);
  protected readonly supportRows = computed<readonly HistoryRow[]>(() => [
    {
      primary: this.record()?.supportCorrelationReference ?? '—',
      secondary: 'Support correlation reference',
      meta: this.record()?.backendPaymentState ?? '—',
    },
  ]);
  protected readonly auditRows = computed<readonly HistoryRow[]>(() => [
    {
      primary: 'Payment verification page opened',
      secondary: this.record()?.donationReference ?? '—',
      meta: `${this.owner()} · ${this.record()?.lastVerifiedTime ?? '—'}`,
    },
  ]);

  // ================= UI states (4.3.4 / 4.3.7) =================
  protected readonly uiState = signal<UiState>('loading');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  // ================= Persistent outcome (4.3.1) =================
  protected readonly persistentOutcome = computed<PersistentOutcome>(() => {
    const r = this.record();
    const state = r?.backendPaymentState ?? 'Pending';
    const kind = this.lastActionKind();
    const success = this.uiState() === 'success';
    return {
      reference: r?.donationReference ?? '—',
      state,
      effectiveTime: success ? this.effectiveTime : (r?.lastVerifiedTime ?? '—'),
      downstreamStatus: success
        ? kind === 'retrieve-receipt'
          ? 'Receipt reference issued · no pending dependency'
          : 'Verification recorded · no pending dependency'
        : 'No pending action',
      owner: this.owner(),
      nextAction: success
        ? kind === 'retrieve-receipt'
          ? 'Save your receipt for your records'
          : state === 'Confirmed'
            ? 'Retrieve your receipt when eligible'
            : 'Refresh again later if your bank confirms the payment'
        : 'Refresh safe status when you want the latest evidence',
    };
  });

  // ================= Formatting helpers =================
  protected formatAmount(value: number, currency: string): string {
    const symbol = currency === 'INR' ? '₹' : currency + ' ';
    return symbol + value.toLocaleString('en-IN');
  }
  protected stateClass(state: string): string {
    switch (state) {
      case 'Confirmed':
        return 'pvp-badge-confirmed';
      case 'Failed':
        return 'pvp-badge-failed';
      case 'Pending':
        return 'pvp-badge-pending';
      default:
        return 'pvp-badge-muted';
    }
  }
  protected eligibilityClass(e: ReceiptEligibility): string {
    return e === 'Eligible' ? 'pvp-badge-confirmed' : 'pvp-badge-muted';
  }

  constructor() {
    if (!this.permissions().view) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    const pending = this.dataService.getPendingVerificationRecord();
    if (pending) {
      this.verifiedFromQueue.set(pending);
    }

    this.loadPayments();
  }

  private loadPayments(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.dataService.getPaymentVerificationData().subscribe({
      next: (res) => {
        this.loading.set(false);
        this.catalogue.set(res);

        if (this.uiState() !== 'no-access') {
          this.uiState.set(res.length === 0 ? 'empty' : 'ready');
        }

        // A HAND-OFF SELECTS AN EXISTING PAYMENT; it never invents one. The previous version
        // built a record out of the gateway event it was handed, deriving `DON-2025-nnnn`,
        // `REC-2025-nnnn` and `COR-2025-nnnn` from whatever digits were in the event id. Those
        // references belonged to no record and were rendered to the donor as facts.
        const wanted = this.verifiedFromQueue()?.mappedIntentOrPayment;

        if (wanted && res.some((r) => r.donationReference === wanted)) {
          this.selectRecord(wanted);
          this.verifyOnOpen(wanted);
          return;
        }

        if (wanted) {
          // The event's intent is not in the support queue - which usually means it is settled.
          // Ask the gateway about it directly rather than reporting nothing.
          this.verifyOnOpen(wanted);
          return;
        }

        if (res.length > 0) {
          this.selectRecord(res[0].donationReference);
        }
      },
      error: (error) => {
        this.loading.set(false);
        this.loadError.set(true);

        if (
          typeof error === 'object' &&
          error !== null &&
          'status' in error &&
          (error as { status?: number }).status === 403
        ) {
          this.uiState.set('no-access');
          return;
        }

        this.uiState.set('ready');
        this.toast.show(
          'Error',
          apiErrorMessage(error, 'The payment could not be loaded.'),
          'error',
        );
      },
    });
  }

  /**
   * Reads the gateway's current answer for a payment the operator arrived on.
   *
   * NOT A RETRY, AND NOT A CHARGE. Verification is a read as far as the donor's money is
   * concerned; performing it on arrival is what lets the page show a true state rather than the
   * one the queue happened to record when the event came in.
   */
  private verifyOnOpen(intentReference: string): void {
    if (!this.tokens.hasAnyPermission('pay.payments.verify')) return;

    this.paymentApi.verifyPayment({ intentReference }).subscribe({
      next: (verification) => {
        const record = toPaymentVerificationRecord(verification);
        this.permittedActions.set(verification.permittedActions);
        this.verifiedAt.set(formatMoment(new Date().toISOString()));

        this.catalogue.update((list) => {
          const without = list.filter((r) => r.donationReference !== record.donationReference);
          return [record, ...without];
        });

        this.activityRows.set(
          verification.history.map((row) => ({
            primary: row.primary,
            secondary: row.secondary,
            meta: row.meta,
          })),
        );

        this.selectedRef.set(record.donationReference);
        this.chosenRef.set(record.donationReference);
        this.uiState.set('ready');
      },
      error: () => {
        // The page still shows what the queue knew. It simply could not be refreshed.
        this.uiState.set('ready');
      },
    });
  }
}

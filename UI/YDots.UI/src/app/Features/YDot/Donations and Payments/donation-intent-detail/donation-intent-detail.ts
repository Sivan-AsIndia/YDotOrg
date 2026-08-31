import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { DonationIntentScreenRecord, formatMoment } from '../../../../Shared/models/payment-adapters';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentEventRecord } from '../../../../Shared/models/payment-event-queue.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';

type IntentState = 'Draft' | 'Needs Payment' | 'Link Sent' | 'Paid' | 'Cancelled';
type LinkStatus = 'Not Created' | 'Active' | 'Expired' | 'Cancelled';
type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';
type LinkMode = 'create' | 'replace' | 'cancel';

/** Effective permission set for the acting Fundraiser / Donor Care / Finance (4.1.3). */
interface EffectivePermissions {
  readonly view: boolean;
  readonly createReplaceCancelLink: boolean;
  readonly share: boolean;
  readonly deleteDraft: boolean;
}

/** Row shape shared by the Related-and-history subtabs (4.1.1 Related and history). */
interface RelatedRow {
  readonly primary: string;
  readonly secondary?: string;
  readonly meta: string;
}

/** One entry of the Lifecycle history field, rendered as a timeline (4.1.2 Lifecycle history). */
interface TimelineEvent {
  readonly title: string;
  readonly detail: string;
  readonly time: string;
  readonly tone: 'good' | 'blue' | 'gold' | 'plum' | 'muted';
}

/**
 * Donation intent detail - SCR-PAY-001.
 *
 * THE INTENT IS THE RECORD OF SOMEBODY MEANING TO GIVE, which stays true even when they did not.
 * Everything on this screen is read from `/api/v1/donation-intents`, and every action goes back to
 * it: create or replace the payment link, re-send it, cancel the intent.
 *
 * THE PAYMENT LINK IS THE GATEWAY'S, NOT THIS SCREEN'S. The link is issued by the organisation's
 * own payment provider through the API, which records the attempt at the same moment; a URL built
 * in the browser leads nowhere and an attempt that was never recorded cannot be verified later.
 *
 * SAFE RETRY VERIFIES BEFORE IT PAYS. It asks the gateway what happened to the previous attempt
 * and refuses if it actually succeeded, which is the whole difference between helping a donor
 * whose card was declined and charging one who has already paid.
 */
@Component({
  selector: 'app-donation-intent-detail',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './donation-intent-detail.html',
  styleUrl: './donation-intent-detail.css',
})
export class DonationIntentDetailComponent {
  private readonly tokens = inject(AuthTokenService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly route = inject(ActivatedRoute);

  /**
   * The intent this screen is showing.
   *
   * IT COMES FROM THE ROUTE, not from a constant. Empty means "no reference given", which the
   * loader treats as "show the newest intent in the register" - the sensible landing state for the
   * menu link, which carries no id.
   */
  protected readonly reference = signal(this.route.snapshot.paramMap.get('reference') ?? '');
  protected readonly state = signal<IntentState>('Needs Payment');
  /** Owner - who is looking at the record. */
  protected readonly owner = signal(this.tokens.user()?.displayName ?? 'You');
  /** Freshness - when the record was actually read. */
  protected readonly lastRefresh = signal('');

  /**
   * The intent's API id.
   *
   * SEPARATE FROM THE REFERENCE because they address different things. The reference is what a
   * donor quotes and what appears in a payment link; the id is what every staff endpoint takes.
   * Conflating them means a cancel or a resend calling an endpoint that cannot find the row.
   */
  protected readonly intentId = signal('');

  protected readonly intents = signal<DonationIntentScreenRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  /**
   * What this caller may do, read from the token.
   *
   * ISSUING OR CANCELLING A PAYMENT LINK IS NOT A READ. A cancelled link stops a donor who is
   * mid-gift from completing it, and a replacement one can leave them holding two.
   */
  protected readonly permissions = computed<EffectivePermissions>(() => ({
    view: this.tokens.hasAnyPermission('pay.intents.view'),

    // ISSUING, REPLACING AND CANCELLING ARE THREE SERVER PERMISSIONS behind one screen control.
    // The control is offered when the caller holds any of them, and the server decides which of
    // the three the click actually is.
    createReplaceCancelLink: this.tokens.hasAnyPermission(
      'pay.intents.create',
      'pay.intents.resend-link',
      'pay.intents.cancel',
    ),

    share: this.tokens.hasAnyPermission('pay.intents.resend-link'),

    // Cancelling is what "delete a draft intent" means here: an intent with no payment behind it
    // is withdrawn rather than removed, because even an abandoned one records that somebody tried.
    deleteDraft: this.tokens.hasAnyPermission('pay.intents.cancel'),
  }));

  protected readonly hasDownstreamReference = signal(true);

  private readonly linkCompatibleStates: readonly IntentState[] = [
    'Draft',
    'Needs Payment',
    'Link Sent',
  ];

  // ================= Field and control contract (4.1.2) =================
  /** The campaign the gift is attributed to. Blank until an intent is loaded. */
  protected readonly campaign = signal({ reference: '', name: '', context: '' });

  /** The donor or guest on the intent. Blank until an intent is loaded. */
  protected readonly donor = signal({ reference: '', name: '', email: '', context: '' });

  protected readonly requestedAmount = signal(0);
  protected readonly currency = signal('INR');

  /** Attribution snapshot - read-only, server-derived, immutable (4.1.2). */
  protected readonly attribution = signal({ reference: '', source: '', firstTouch: '' });

  protected readonly linkStatus = signal<LinkStatus>('Not Created');
  protected readonly preferredMethod = signal('Card');
  protected readonly paymentUrl = signal<string | null>(null);
  protected readonly linkExpiresAt = signal<string | null>(null);

  protected readonly attemptsCount = signal(0);
  protected readonly lastAttempt = signal<string | null>(null);

  protected readonly capturedAmount = signal<number | null>(null);
  protected readonly capturedTime = signal<string | null>(null);

  protected readonly settlementStatus = signal<'Not applicable' | 'Pending' | 'Settled'>(
    'Not applicable',
  );
  protected readonly reconciliationStatus = signal<'Not applicable' | 'Pending' | 'Reconciled'>(
    'Not applicable',
  );
  protected readonly receiptStatus = signal<'Not issued' | 'Pending' | 'Issued'>('Not issued');

  protected readonly refundableBalance = signal<number | null>(null);

  protected readonly lifecycleHistory = signal<TimelineEvent[]>([]);

  // ================= Context and filters (4.1.1) =================
  /** The organisation the session is operating in, never a fixed name. */
  protected readonly scope = computed(
    () => `${this.tokens.tenant()?.tenantName ?? 'Your organisation'} · This record`,
  );

  protected readonly savedFilters = ['All activity (Default)', 'Payments only', 'Confidential only'];
  protected readonly savedFilter = signal(this.savedFilters[0]);

  protected readonly searchTerm = signal('');

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedFilter() !== this.savedFilters[0]) {
      chips.push({ key: 'saved', label: `View: ${this.savedFilter()}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    return chips;
  });

  /** The count of related items actually loaded, rather than a fixed number. */
  protected readonly relatedItemCount = computed(
    () =>
      this.linkedRecords().length +
      this.documentRows().length +
      this.activityRows().length +
      this.integrationRows().length +
      this.supportRows().length +
      this.auditRows().length,
  );
  protected readonly scopedTotals = computed(
    () => `${this.relatedItemCount()} related items in scope · refreshed ${this.lastRefresh()}`,
  );

  protected removeFilterChip(key: string): void {
    if (key === 'saved') {
      this.savedFilter.set(this.savedFilters[0]);
    } else if (key === 'search') {
      this.searchTerm.set('');
    }
  }
  protected clearFilters(): void {
    this.savedFilter.set(this.savedFilters[0]);
    this.searchTerm.set('');
  }

  // ================= Related and history (4.1.1) =================
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

  protected readonly linkedRecords = signal<readonly RelatedRow[]>([]);
  protected readonly documentRows = signal<readonly RelatedRow[]>([]);
  protected readonly activityRows = signal<readonly RelatedRow[]>([]);
  protected readonly integrationRows = signal<readonly RelatedRow[]>([]);
  protected readonly supportRows = signal<readonly RelatedRow[]>([]);
  protected readonly auditRows = signal<readonly RelatedRow[]>([]);

  protected readonly activeRelatedRows = computed<readonly RelatedRow[]>(() => {
    switch (this.activeRelatedTab()) {
      case 'linked':
        return this.linkedRecords();
      case 'documents':
        return this.documentRows();
      case 'activity':
        return this.activityRows();
      case 'integration':
        return this.integrationRows();
      case 'support':
        return this.supportRows();
      case 'audit':
        return this.auditRows();
      default:
        return [];
    }
  });

  // ================= Actions, eligibility and result (4.1.3) =================
  protected readonly linkActionEligible = computed(
    () =>
      this.permissions().createReplaceCancelLink &&
      this.linkCompatibleStates.includes(this.state()) &&
      this.uiState() !== 'no-access',
  );

  protected readonly availableLinkModes = computed<readonly LinkMode[]>(() => {
    const modes: LinkMode[] = [];
    if (
      this.linkStatus() === 'Not Created' ||
      this.linkStatus() === 'Expired' ||
      this.linkStatus() === 'Cancelled'
    ) {
      modes.push('create');
    } else {
      modes.push('replace', 'cancel');
    }
    return modes;
  });

  protected readonly linkDialogOpen = signal(false);
  protected readonly linkMode = signal<LinkMode>('create');
  protected readonly linkReason = signal('');
  protected readonly linkReasonMin = 10;
  protected readonly linkReasonMax = 2000;
  protected readonly linkReasonCount = computed(() => this.linkReason().trim().length);
  protected readonly linkReasonValid = computed(() => {
    const len = this.linkReason().trim().length;
    return len >= this.linkReasonMin && len <= this.linkReasonMax;
  });
  protected readonly linkReasonTouched = signal(false);

  /** When the last link action took effect. Blank until one does. */
  protected readonly linkEffectiveTime = signal('');

  protected readonly linkModeLabel: Record<LinkMode, string> = {
    create: 'Create link',
    replace: 'Replace link',
    cancel: 'Cancel link',
  };

  protected openLinkDialog(): void {
    if (!this.linkActionEligible()) {
      return;
    }
    this.linkMode.set(this.availableLinkModes()[0]);
    this.linkReason.set('');
    this.linkReasonTouched.set(false);
    this.linkDialogOpen.set(true);
  }
  protected setLinkMode(mode: LinkMode): void {
    this.linkMode.set(mode);
  }
  protected cancelLinkDialog(): void {
    this.linkDialogOpen.set(false);
  }
  /**
   * Creates, replaces or cancels the payment link.
   *
   * IT OPENS A REAL PAYMENT ATTEMPT. The API asks the organisation's own gateway for a hosted
   * checkout link, records the attempt, and returns the URL the donor actually pays on.
   *
   * THE VERSION GOES WITH IT. Two operators - or one operator with two tabs - would otherwise both
   * create a link for the same intent, and a donor holding two live links can pay twice. The
   * server refuses the second with a 409, which is reported here rather than swallowed.
   */
  protected confirmLinkDialog(): void {
    this.linkReasonTouched.set(true);

    if (!this.linkReasonValid()) {
      this.uiState.set('validation');
      this.toast.show('Validation Error', 'Please provide a valid reason.', 'warning');
      return;
    }

    const mode = this.linkMode();
    const reference = this.reference();
    const id = this.intentId();

    if (!reference || !id) {
      this.toast.show('No intent', 'This screen has no donation intent loaded.', 'warning');
      return;
    }

    this.uiState.set('loading');

    if (mode === 'cancel') {
      this.paymentApi
        .cancelIntent(id, {
          expectedVersion: this.currentVersion(),
          reason: this.linkReason().trim(),
        })
        .subscribe({
          next: () => {
            this.linkDialogOpen.set(false);
            this.linkEffectiveTime.set(formatMoment(new Date().toISOString()));
            this.uiState.set('success');
            this.toast.show('Link Cancelled', `Donation intent ${reference} cancelled.`, 'success');
            this.loadIntents();
          },
          error: (error) => this.reportActionFailure(error, 'The intent could not be cancelled.'),
        });

      return;
    }

    this.paymentApi
      .createPaymentLink(reference, {
        expectedVersion: this.currentVersion(),
        preferredMethod: this.preferredMethod(),
      })
      .subscribe({
        next: (link) => {
          this.linkStatus.set('Active');
          this.paymentUrl.set(link.paymentLinkUrl);
          this.linkExpiresAt.set(formatMoment(link.expiresAtUtc));
          this.state.set('Link Sent');
          this.linkDialogOpen.set(false);
          this.linkEffectiveTime.set(formatMoment(new Date().toISOString()));
          this.uiState.set('success');
          this.toast.show(
            'Link Created',
            `Payment link created for ${reference}, valid until ${this.linkExpiresAt()}.`,
            'success',
          );
          this.loadIntents();
        },
        error: (error) => this.reportActionFailure(error, 'The payment link could not be created.'),
      });
  }

  /**
   * The version to send with the next write.
   *
   * Read from the loaded record rather than held separately, so it cannot drift from what the
   * screen is displaying - which is exactly how a stale version reaches the server and turns a
   * legitimate action into a 409 the operator cannot explain.
   */
  private currentVersion(): number {
    return this.intents().find((intent) => intent.reference === this.reference())?.version ?? 0;
  }

  /**
   * Reports a failed write.
   *
   * A 409 IS CALLED OUT SEPARATELY because it means something an operator can act on - somebody
   * else changed this record - rather than something being broken.
   */
  private reportActionFailure(error: unknown, fallback: string): void {
    const conflict =
      typeof error === 'object' &&
      error !== null &&
      'errorCode' in error &&
      (error as { errorCode?: string }).errorCode === 'CONCURRENCY_CONFLICT';

    this.uiState.set(conflict ? 'conflict' : 'ready');
    this.toast.show(
      conflict ? 'Record changed' : 'Action failed',
      apiErrorMessage(error, fallback),
      conflict ? 'warning' : 'error',
    );
  }

  // ----- Share: workflow action (4.1.3 Share) -----
  protected readonly shareConfirmed = signal<string | null>(null);
  protected shareAllowed(): boolean {
    return this.permissions().share && this.uiState() !== 'no-access';
  }
  /**
   * Sends the payment link to the donor again.
   *
   * IT REUSES THE INTENT rather than creating a second one, so the donor cannot end up holding two
   * live links for one gift. The commonest support action there is.
   */
  protected share(): void {
    if (!this.shareAllowed()) {
      return;
    }

    const id = this.intentId();

    if (!id) {
      this.toast.show('No intent', 'This screen has no donation intent loaded.', 'warning');
      return;
    }

    this.paymentApi.resendPaymentLink(id, this.currentVersion()).subscribe({
      next: (link) => {
        this.shareConfirmed.set(formatMoment(new Date().toISOString()));
        this.paymentUrl.set(link.paymentLinkUrl);
        this.toast.show('Link Shared', `Payment link re-sent for ${this.reference()}.`, 'success');
        this.loadIntents();
      },
      error: (error) => this.reportActionFailure(error, 'The payment link could not be re-sent.'),
    });
  }
  protected dismissShareConfirmation(): void {
    this.shareConfirmed.set(null);
  }

  // ----- Inspect events (4.1.3 Inspect events) -----
  protected readonly eventsInspectedAt = signal<string | null>(null);
  protected inspectEvents(): void {
    this.eventsInspectedAt.set(formatMoment(new Date().toISOString()));
    this.router.navigate(['/app/donations/payment-event-queue'], {
      state: { intentReference: this.reference() },
    });
  }
  protected readonly inspectedRow = signal<number | null>(null);
  protected inspectRow(index: number): void {
    this.inspectedRow.set(this.inspectedRow() === index ? null : index);
  }

  // ----- Delete unused draft: danger menu (4.1.3) -----
  protected readonly deleteDraftEligible = computed(
    () =>
      this.permissions().deleteDraft && this.state() === 'Draft' && !this.hasDownstreamReference(),
  );
  protected readonly menuOpen = signal(false);
  protected toggleMenu(): void {
    this.menuOpen.set(!this.menuOpen());
  }
  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteReason = signal('');
  protected readonly deleteReasonMin = 10;
  protected readonly deleteReasonMax = 500;
  protected readonly deleteReasonCount = computed(() => this.deleteReason().trim().length);
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.deleteReasonMin && len <= this.deleteReasonMax;
  });
  protected readonly deleteReasonTouched = signal(false);

  protected openDeleteDialog(): void {
    this.menuOpen.set(false);
    if (!this.deleteDraftEligible()) {
      return;
    }
    this.deleteReason.set('');
    this.deleteReasonTouched.set(false);
    this.deleteDialogOpen.set(true);
  }
  protected cancelDeleteDialog(): void {
    this.deleteDialogOpen.set(false);
  }
  /**
   * Discards an unused draft.
   *
   * IT CANCELS RATHER THAN DELETES, and the API has no delete endpoint at all. A donation intent
   * records that somebody meant to give - which stays true even when they did not - and the consent
   * captured on it is evidence that has to survive.
   */
  protected confirmDeleteDraft(): void {
    this.deleteReasonTouched.set(true);

    if (!this.deleteReasonValid()) {
      this.toast.show('Validation Error', 'Please provide a valid reason.', 'warning');
      return;
    }

    const id = this.intentId();

    if (!id) {
      this.toast.show('No intent', 'This screen has no donation intent loaded.', 'warning');
      return;
    }

    this.paymentApi
      .cancelIntent(id, {
        expectedVersion: this.currentVersion(),
        reason: this.deleteReason().trim(),
      })
      .subscribe({
        next: () => {
          this.deleteDialogOpen.set(false);
          this.uiState.set('success');
          this.toast.show(
            'Draft Discarded',
            `Draft ${this.reference()} has been cancelled and removed from your queue.`,
            'success',
          );
          this.loadIntents();
        },
        error: (error) => this.reportActionFailure(error, 'The draft could not be discarded.'),
      });
  }

  // ================= UI states (4.1.4 / 4.1.7) =================
  protected readonly uiState = signal<UiState>('loading');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  // ================= Persistent outcome (4.1.1) =================
  protected readonly persistentOutcome = computed(() => ({
    reference: this.reference(),
    state: this.state(),
    effectiveTime:
      this.uiState() === 'success' ? this.linkEffectiveTime() : this.lastRefresh(),
    downstreamStatus:
      this.linkStatus() === 'Active'
        ? 'Payment link active · awaiting capture'
        : 'No pending action',
    owner: this.owner(),
    nextAction:
      this.linkStatus() === 'Active'
        ? 'Share the payment link with the donor'
        : 'Create the payment link when ready',
  }));

  protected readonly copiedField = signal<string | null>(null);
  protected copyValue(label: string, value: string): void {
    navigator.clipboard?.writeText(value).catch(() => undefined);
    this.copiedField.set(label);
    setTimeout(() => {
      if (this.copiedField() === label) {
        this.copiedField.set(null);
      }
    }, 1500);
  }

  // ================= Formatting helpers =================
  protected formatAmount(value: number | null): string {
    if (value === null) {
      return '—';
    }
    return (
      this.currency() +
      ' ' +
      value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    );
  }
  protected linkStatusClass(status: LinkStatus): string {
    switch (status) {
      case 'Active':
        return 'dit-badge-good';
      case 'Not Created':
        return 'dit-badge-muted';
      case 'Expired':
        return 'dit-badge-warn';
      case 'Cancelled':
        return 'dit-badge-danger';
    }
  }
  protected stateClass(state: IntentState): string {
    switch (state) {
      case 'Needs Payment':
        return 'dit-badge-blue';
      case 'Link Sent':
        return 'dit-badge-gold';
      case 'Paid':
        return 'dit-badge-good';
      case 'Cancelled':
        return 'dit-badge-danger';
      case 'Draft':
        return 'dit-badge-muted';
    }
  }

  // ================= Hand-off from the payment event queue =================
  /** The gateway event this screen was opened from, when it was opened from the queue. */
  protected readonly queueRecord = signal<PaymentEventRecord | null>(null);

  /**
   * Continue to payment - opens the donor's own payment link.
   *
   * IT OPENS THE LINK THE SERVER ISSUED, in a new tab, rather than navigating an operator into the
   * donor's checkout inside the admin shell. When no link exists yet, one is created first through
   * the API - which is what records the attempt.
   */
  protected continueToPayment(): void {
    const url = this.paymentUrl();

    if (url) {
      window.open(url, '_blank', 'noopener');
      return;
    }

    const reference = this.reference();
    if (!reference) {
      this.toast.show('No intent', 'This screen has no donation intent loaded.', 'warning');
      return;
    }

    this.paymentApi
      .createPaymentLink(reference, {
        expectedVersion: this.currentVersion(),
        preferredMethod: this.preferredMethod(),
      })
      .subscribe({
        next: (link) => {
          this.paymentUrl.set(link.paymentLinkUrl);
          this.linkStatus.set('Active');
          this.linkExpiresAt.set(formatMoment(link.expiresAtUtc));
          window.open(link.paymentLinkUrl, '_blank', 'noopener');
          this.toast.show(
            'Payment link opened',
            `A payment page was opened for ${reference}.`,
            'success',
          );
          this.loadIntents();
        },
        error: (error) => this.reportActionFailure(error, 'A payment page could not be opened.'),
      });
  }

  // ================= Safe retry =================
  protected readonly retryState = signal<'idle' | 'processing' | 'success' | 'failed'>('idle');

  protected readonly formattedAmount = computed(() => {
    const amount = this.requestedAmount();
    if (!Number.isFinite(amount)) return '—';
    return `${this.currency()} ${amount.toLocaleString('en-IN', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })}`;
  });

  /**
   * Safely retries the payment.
   *
   * NOT A PLAIN RETRY, AND THE DIFFERENCE IS THE ENTIRE POINT. The server verifies the previous
   * attempt with the gateway FIRST and refuses if it actually succeeded, so a donor whose card was
   * declined gets a fresh link and a donor who has already paid gets told so rather than charged
   * twice. Its four outcomes - Retried, AlreadyPaid, StillPending, Refused - are distinguished
   * here, because "already paid" reads as a failure if it is not.
   *
   * WHAT THIS REPLACES. A client-side checkout opened with a Razorpay test key hard-coded into
   * this file, with no order id and no server involvement: the donor could be charged and the
   * platform would never learn of it, because nothing on the way in or out passed through the API.
   */
  protected safeRetry(): void {
    const id = this.intentId();

    if (!id) {
      this.toast.show('No intent', 'This screen has no donation intent loaded.', 'warning');
      return;
    }

    if (!this.tokens.hasAnyPermission('pay.payments.safe-retry')) {
      this.toast.show(
        'Not permitted',
        'Retrying a payment needs the pay.payments.safe-retry permission.',
        'warning',
      );
      return;
    }

    this.retryState.set('processing');

    this.paymentApi
      .safeRetry(id, {
        expectedVersion: this.currentVersion(),
        reason: 'Retried from the donation intent detail after a failed attempt.',
      })
      .subscribe({
        next: (outcome) => {
          const result = outcome.outcome.trim().toLowerCase();

          if (result === 'alreadypaid') {
            this.retryState.set('success');
            this.state.set('Paid');
            this.toast.show('Already paid', outcome.message, 'warning');
            this.loadIntents();
            return;
          }

          if (result === 'retried' && outcome.paymentLinkUrl) {
            this.retryState.set('success');
            this.paymentUrl.set(outcome.paymentLinkUrl);
            this.linkStatus.set('Active');
            window.open(outcome.paymentLinkUrl, '_blank', 'noopener');
            this.toast.show('Retry started', outcome.message, 'success');
            this.loadIntents();
            return;
          }

          this.retryState.set('idle');
          this.toast.show('No action taken', outcome.message, 'warning');
          this.loadIntents();
        },
        error: (error) => {
          this.retryState.set('failed');
          this.reportActionFailure(error, 'The payment could not be retried.');
        },
      });
  }

  constructor() {
    if (!this.permissions().view) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    this.loadIntents();
  }

  private loadIntents(): void {
    this.loading.set(true);
    this.loadError.set(false);

    // A record handed over from the payment event queue decides which intent opens.
    const pending = this.dataService.getPendingDonationForPayment();
    if (pending) {
      this.queueRecord.set(pending);
      if (pending.mappedIntentOrPayment) {
        this.reference.set(pending.mappedIntentOrPayment);
      }
      this.dataService.clearPendingDonationForPayment();
    }

    this.dataService.getDonationIntentsData().subscribe({
      next: (res) => {
        this.intents.set(res.intents ?? []);
        this.loading.set(false);
        this.lastRefresh.set(formatMoment(new Date().toISOString()));

        // Named intent first; otherwise the newest one in the register. Falling back rather than
        // showing an empty screen matters because the menu link carries no reference at all.
        const wanted = this.reference();
        const current =
          (wanted ? this.intents().find((i) => i.reference === wanted) : null) ??
          this.intents()[0] ??
          null;

        if (!current) {
          this.uiState.set('empty');
          return;
        }

        this.reference.set(current.reference);
        this.intentId.set(current.id);
        this.state.set(current.state);
        this.hasDownstreamReference.set(current.hasDownstreamReference);
        this.linkStatus.set(current.linkStatus);
        this.preferredMethod.set(current.preferredMethod ?? 'Card');
        this.paymentUrl.set(current.paymentUrl);
        this.linkExpiresAt.set(current.linkExpiresAt);
        this.attemptsCount.set(current.attemptsCount);
        this.lastAttempt.set(current.lastAttempt);
        this.capturedAmount.set(current.capturedAmount);
        this.capturedTime.set(current.capturedTime);
        this.settlementStatus.set(current.settlementStatus);
        this.reconciliationStatus.set(current.reconciliationStatus);
        this.receiptStatus.set(current.receiptStatus);
        this.refundableBalance.set(current.refundableBalance);
        this.lifecycleHistory.set([...(current.lifecycleHistory ?? [])]);

        this.campaign.set(current.campaign);
        this.donor.set(current.donor);
        this.requestedAmount.set(current.requestedAmount);
        this.currency.set(current.currency);
        this.attribution.set(current.attribution);

        this.linkedRecords.set(current.linkedRecords ?? []);
        this.documentRows.set(current.documentRows ?? []);
        this.activityRows.set(current.activityRows ?? []);
        this.integrationRows.set(current.integrationRows ?? []);
        this.supportRows.set(current.supportRows ?? []);
        this.auditRows.set(current.auditRows ?? []);

        if (this.uiState() !== 'success' && this.uiState() !== 'no-access') {
          this.uiState.set('ready');
        }

        // THE HAND-OFF NEVER OVERWRITES THE RECORD. The previous version took the donor name,
        // the campaign and the amount from the gateway event and wrote them over the intent's
        // own - inventing `DON-` and `CMP-` references out of the intent reference on the way.
        // The event is what the operator arrived on; the intent is what is true.
        if (pending && !this.intents().some((i) => i.reference === pending.mappedIntentOrPayment)) {
          this.toast.show(
            'Intent not in this register',
            `${pending.mappedIntentOrPayment || 'That event'} did not correlate to a donation intent you can see.`,
            'info',
          );
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
          apiErrorMessage(error, 'The donation intent could not be loaded.'),
          'error',
        );
      },
    });
  }
}

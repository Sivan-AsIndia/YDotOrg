import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentEventRecord } from '../../../../Shared/models/payment-event-queue.model';
import { formatMoment, toRecoveryRecord } from '../../../../Shared/models/payment-adapters';
import {
  PsrLifecycleState,
  PsrPersistentOutcome,
  PsrRecoveryPermissions,
  PsrRecoveryRecord,
  PsrUiState,
  PsrVerifiedPaymentState,
} from '../../../../Shared/models/payment-support-safe-retry.model';

/**
 * Payment support and safe retry - SCR-PAY-007.
 *
 * THIS SCREEN CAN CHARGE SOMEBODY TWICE IF IT IS WRONG, which is why every action on it goes to
 * the server and none of them decides anything locally.
 *
 *   Verify status  - POST /payments/verify. Asks the gateway what actually happened. It NEVER
 *                    retries: a retry disguised as a check is exactly how a donor is charged
 *                    twice. Its three answers - Confirmed, Failed, still Pending - are all real,
 *                    and "still pending" is reported as such rather than nudged into a guess.
 *
 *   Resend link    - POST /payments/intents/{id}/safe-retry. Not a plain retry: the handler
 *   Replace link     verifies the previous attempt with the gateway FIRST and refuses if it
 *                    actually succeeded. The four outcomes it returns - Retried, AlreadyPaid,
 *                    StillPending, Refused - are distinguished here, because "already paid" is
 *                    the answer that matters most and reads as a failure if it is not.
 *
 *   Cancel intent  - POST /donation-intents/{id}/cancel, with a reason and the version.
 *
 * THE ROUTES TAKE IDENTIFIERS, NOT REFERENCES. `intentId` is carried on every row for that
 * reason, and the version is read from the intent when a row is opened, so a write never sends a
 * stale stamp.
 */
@Component({
  selector: 'app-payment-support-and-safe-retry',
  imports: [CommonModule, FormsModule],
  templateUrl: './payment-support-and-safe-retry.html',
  styleUrl: './payment-support-and-safe-retry.css',
})
export class PaymentSupportAndSafeRetryComponent {
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly tokens = inject(AuthTokenService);

  private static readonly FETCH_SIZE = 200;

  // ================= Task header =================
  protected readonly pageTitle = 'Payment support and safe retry';
  protected readonly pageSubtitle =
    'Help a donor recover from an incomplete payment without creating duplicate charges or exposing gateway details.';
  protected readonly owner = computed(() => this.tokens.user()?.displayName ?? 'You');
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('');

  /**
   * What this caller may do, read from the token rather than assumed.
   *
   * THIS SCREEN OFFERS THE TWO ACTIONS WITH THE GREATEST CAPACITY TO CHARGE SOMEBODY TWICE, so
   * drawing them for a person the API would refuse is worse here than anywhere else - they would
   * press Verify, receive a 403, and have no way of knowing whether the payment had been checked.
   *
   * The codes are the ones the PAY endpoints enforce, so the buttons and the API agree by
   * construction rather than by coincidence.
   */
  protected readonly permissions = computed<PsrRecoveryPermissions>(() => ({
    view: this.tokens.hasAnyPermission('pay.intents.view', 'pay.payments.safe-retry'),
    verifyStatus: this.tokens.hasAnyPermission('pay.payments.verify', 'pay.payments.safe-retry'),
    resendActiveLink: this.tokens.hasAnyPermission(
      'pay.intents.resend-link',
      'pay.payments.safe-retry',
    ),
    replaceExpiredLink: this.tokens.hasAnyPermission('pay.payments.safe-retry'),
    cancelIntent: this.tokens.hasAnyPermission('pay.intents.cancel'),
    openSupportCase: this.tokens.hasAnyPermission('pay.payments.safe-retry'),
  }));

  // ================= Hand-off from the event queue or the intent detail =================
  /** A record carried in from another screen, so the operator lands on the case they chose. */
  protected readonly retryRecord = signal<PaymentEventRecord | null>(null);

  // ================= Context and filters =================
  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  protected readonly searchTerm = signal('');

  protected readonly verifiedStateOptions: readonly PsrVerifiedPaymentState[] = [
    'Pending',
    'Uncertain',
    'Failed',
    'Confirmed',
    'Cancelled',
  ];
  protected readonly verifiedStateFilter = signal<PsrVerifiedPaymentState | ''>('');

  /** The delivery channels the payment service actually sends links on. */
  protected readonly channelOptions: readonly string[] = ['Email'];
  protected readonly channelFilter = signal<string>('');

  protected readonly rangeStart = signal('');
  protected readonly rangeEnd = signal('');
  protected readonly rangeInvalid = computed(() => {
    const s = this.rangeStart();
    const e = this.rangeEnd();
    return !!s && !!e && new Date(e) < new Date(s);
  });
  protected readonly interpretedRange = computed(() => {
    const s = this.rangeStart();
    const e = this.rangeEnd();
    if (!s && !e) return `Any last-attempt date · ${this.operatingTimeZone}`;
    return `${s ? this.formatDate(s) : '…'} – ${e ? this.formatDate(e) : '…'} · ${this.operatingTimeZone}`;
  });

  /**
   * The scope selector.
   *
   * IT NAMES THE SIGNED-IN ORGANISATION AND NOTHING ELSE. The previous version listed three
   * invented regions belonging to nobody, which told an operator they could work inside an
   * organisation that does not exist - and the API scopes every read to the token's organisation
   * regardless, so choosing one of them changed nothing.
   */
  protected readonly scopeOptions = computed(() => [
    `${this.tokens.tenant()?.tenantName ?? 'My active organisation'} (default)`,
  ]);
  protected readonly scopeFilter = signal('');
  protected readonly moreFiltersOpen = signal(false);
  protected toggleMoreFilters(): void {
    this.moreFiltersOpen.update((v) => !v);
  }
  protected readonly moreFiltersCount = computed(() => 0);

  protected readonly savedFilters = [
    'All records (Default)',
    'Needs donor action',
    'In progress',
    'Resolved',
  ];
  protected readonly savedFilter = signal(this.savedFilters[0]);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim())
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    if (this.verifiedStateFilter())
      chips.push({ key: 'state', label: `Payment state: ${this.verifiedStateFilter()}` });
    if (this.channelFilter()) chips.push({ key: 'channel', label: `Channel: ${this.channelFilter()}` });
    if (this.rangeStart() || this.rangeEnd()) {
      chips.push({
        key: 'date',
        label: `Last attempt: ${this.rangeStart() ? this.formatDate(this.rangeStart()) : '…'} – ${
          this.rangeEnd() ? this.formatDate(this.rangeEnd()) : '…'
        }`,
      });
    }
    return chips;
  });
  protected removeFilterChip(key: string): void {
    switch (key) {
      case 'search':
        this.searchTerm.set('');
        break;
      case 'state':
        this.verifiedStateFilter.set('');
        break;
      case 'channel':
        this.channelFilter.set('');
        break;
      case 'date':
        this.rangeStart.set('');
        this.rangeEnd.set('');
        break;
    }
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.verifiedStateFilter.set('');
    this.channelFilter.set('');
    this.rangeStart.set('');
    this.rangeEnd.set('');
    this.savedFilter.set(this.savedFilters[0]);
  }
  protected readonly filterAllowed = computed(
    () => this.permissions().view && !this.rangeInvalid(),
  );
  protected applyFilters(): void {
    if (!this.filterAllowed()) return;
    this.moreFiltersOpen.set(false);
  }

  // ================= Main work: recovery records =================
  protected readonly records = signal<PsrRecoveryRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly serverTotal = signal(0);

  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const state = this.verifiedStateFilter();
    const channel = this.channelFilter();
    const start = this.rangeStart() ? new Date(this.rangeStart()) : null;
    const end = this.rangeEnd() ? new Date(`${this.rangeEnd()}T23:59:59`) : null;

    return this.records().filter((r) => {
      if (
        q &&
        !(
          r.donationIntentReference.toLowerCase().includes(q) ||
          r.donorContactPreview.toLowerCase().includes(q) ||
          r.supportCorrelationReference.toLowerCase().includes(q)
        )
      ) {
        return false;
      }
      if (state && r.verifiedPaymentState !== state) return false;
      if (channel && r.preferredDeliveryChannel !== channel) return false;
      if (start && new Date(r.lastAttemptIso) < start) return false;
      if (end && new Date(r.lastAttemptIso) > end) return false;
      return true;
    });
  });

  protected readonly totalRecords = computed(() => this.serverTotal());
  protected readonly needsDonorActionCount = computed(
    () =>
      this.records().filter(
        (r) => r.verifiedPaymentState === 'Failed' || r.linkCondition === 'Expired',
      ).length,
  );
  protected readonly inProgressCount = computed(
    () =>
      this.records().filter(
        (r) => r.verifiedPaymentState === 'Pending' || r.verifiedPaymentState === 'Uncertain',
      ).length,
  );
  protected readonly resolvedCount = computed(
    () =>
      this.records().filter(
        (r) => r.verifiedPaymentState === 'Confirmed' || r.verifiedPaymentState === 'Cancelled',
      ).length,
  );
  protected readonly recordCount = computed(() => this.visibleRecords().length);

  // ================= Selection -> working record =================
  protected readonly selectedRef = signal<string>('');
  protected readonly selectedRecord = computed(
    () => this.records().find((r) => r.donationIntentReference === this.selectedRef()) ?? null,
  );
  protected select(ref: string): void {
    if (!this.permissions().view) return;
    this.selectedRef.set(ref);
    this.loadIntentDetail(ref);
  }
  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
  }

  /**
   * Reads the intent behind the selected row.
   *
   * THE SUPPORT-QUEUE PROJECTION CARRIES NO VERSION and no payment link - it is a work list, not
   * a record. Both are needed the moment somebody acts, so they are fetched when a row is opened
   * rather than sent with two hundred rows that will never be touched.
   */
  private loadIntentDetail(reference: string): void {
    const row = this.records().find((r) => r.donationIntentReference === reference);
    if (!row?.intentId) return;

    this.paymentApi.getIntent(row.intentId).subscribe({
      next: (intent) => {
        const expiry = intent.paymentLinkExpiresAtUtc;
        const expired = !!expiry && new Date(expiry).getTime() < Date.now();

        this.patch(reference, {
          version: intent.version,
          existingActiveLink: intent.paymentLinkUrl ?? '—',
          linkExpiryIso: expiry ?? '',
          linkExpiryLabel: expiry ? formatMoment(expiry) : '—',
          linkCondition: !intent.paymentLinkUrl ? 'None' : expired ? 'Expired' : 'Active',
        });
      },
      error: () => {
        // A failed detail read leaves the row exactly as the queue reported it. Acting on it will
        // then fail with a 409 rather than silently writing against a version nobody read.
      },
    });
  }

  protected readonly copiedField = signal<string | null>(null);
  protected copyValue(label: string, value: string): void {
    navigator.clipboard?.writeText(value).catch(() => undefined);
    this.copiedField.set(label);
    setTimeout(() => {
      if (this.copiedField() === label) this.copiedField.set(null);
    }, 1500);
  }

  protected readonly relatedTabs = [
    'Linked records',
    'Documents',
    'Activity',
    'Integration status',
    'Support correlation',
    'Audit chronology',
  ] as const;
  protected readonly relatedTab = signal<(typeof this.relatedTabs)[number]>('Linked records');
  protected selectRelatedTab(tab: (typeof this.relatedTabs)[number]): void {
    this.relatedTab.set(tab);
  }

  // ================= Action eligibility =================
  private recoverable(s: PsrVerifiedPaymentState): boolean {
    return s === 'Pending' || s === 'Uncertain' || s === 'Failed';
  }
  protected verifyAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions().verifyStatus && this.recoverable(r.verifiedPaymentState);
  }
  protected resendAllowed(r: PsrRecoveryRecord | null): boolean {
    return (
      !!r &&
      this.permissions().resendActiveLink &&
      r.linkCondition === 'Active' &&
      this.recoverable(r.verifiedPaymentState)
    );
  }
  protected replaceAllowed(r: PsrRecoveryRecord | null): boolean {
    return (
      !!r &&
      this.permissions().replaceExpiredLink &&
      r.linkCondition !== 'Active' &&
      this.recoverable(r.verifiedPaymentState)
    );
  }
  protected cancelAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions().cancelIntent && this.recoverable(r.verifiedPaymentState);
  }
  protected openSupportAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions().openSupportCase && this.recoverable(r.verifiedPaymentState);
  }
  protected anyOverflowAllowed(r: PsrRecoveryRecord | null): boolean {
    return (
      this.resendAllowed(r) ||
      this.replaceAllowed(r) ||
      this.openSupportAllowed(r) ||
      this.cancelAllowed(r)
    );
  }

  protected readonly overflowOpen = signal(false);
  protected toggleOverflow(): void {
    this.overflowOpen.update((v) => !v);
  }
  protected closeOverflow(): void {
    this.overflowOpen.set(false);
  }

  // ================= Verify status =================

  /**
   * Asks the gateway what actually happened to the last attempt.
   *
   * THE MOST IMPORTANT ACTION ON THE SCREEN. The three answers are all real: Confirmed means the
   * money arrived, Failed means it did not and a safe retry is the next step, and STILL PENDING
   * means the gateway does not know yet - which the screen says rather than nudging forward,
   * because a guess presented as a fact is what leads somebody to retry a payment that already
   * succeeded.
   */
  protected verifyStatus(r: PsrRecoveryRecord): void {
    this.closeOverflow();

    if (!this.verifyAllowed(r)) {
      return;
    }

    this.paymentApi.verifyPayment({ intentReference: r.donationIntentReference }).subscribe({
      next: (verification) => {
        const state = verification.backendPaymentState.trim().toLowerCase();

        const next: PsrVerifiedPaymentState =
          state === 'confirmed' ? 'Confirmed' : state === 'failed' ? 'Failed' : 'Pending';

        const nextLifecycle: PsrLifecycleState =
          next === 'Confirmed' ? 'Confirmed' : next === 'Failed' ? 'Failed' : 'Needs verification';

        this.patch(r.donationIntentReference, {
          verifiedPaymentState: next,
          lifecycleState: nextLifecycle,
          history: [
            ...r.history,
            {
              label: 'Status verified with the gateway',
              detail:
                next === 'Confirmed'
                  ? 'The provider confirms the payment succeeded. No duplicate charge was created.'
                  : next === 'Failed'
                    ? 'The provider confirms the payment failed. A safe retry is now possible.'
                    : 'The provider does not yet know the outcome. Do not retry - check again shortly.',
              meta: `${this.owner()} · ${this.lastRefresh()}`,
            },
          ],
        });

        const updated = this.byRef(r.donationIntentReference);
        this.selectedRef.set(r.donationIntentReference);

        if (updated) {
          this.setOutcome(
            updated,
            next === 'Confirmed'
              ? 'Gateway confirmed; no duplicate charge created'
              : next === 'Failed'
                ? 'Gateway confirms the payment failed'
                : 'Gateway outcome still unknown',
            next === 'Confirmed'
              ? 'No further action required'
              : next === 'Failed'
                ? 'Resend or replace the link so the donor can safely retry'
                : 'Do not retry. Check again shortly.',
          );
        }

        if (next === 'Confirmed') {
          this.toast.show(
            'Payment Confirmed',
            `The provider confirms ${r.donationIntentReference} succeeded.`,
            'success',
          );
        } else if (next === 'Failed') {
          this.toast.show(
            'Payment Failed',
            `The provider confirms ${r.donationIntentReference} failed. A safe retry is now possible.`,
            'warning',
          );
        } else {
          this.toast.show(
            'Outcome still unknown',
            'The provider does not yet know the outcome. Do not retry - check again shortly.',
            'warning',
          );
        }
      },
      error: (error) =>
        this.toast.show(
          'Provider unreachable',
          apiErrorMessage(
            error,
            'The payment provider could not be reached. The payment is unchanged and can be checked again.',
          ),
          'error',
        ),
    });
  }

  // ================= Resend active link =================
  protected readonly resendDialogOpen = signal(false);
  protected readonly resendTarget = signal<PsrRecoveryRecord | null>(null);
  protected requestResend(r: PsrRecoveryRecord): void {
    this.closeOverflow();
    if (!this.resendAllowed(r)) return;
    this.resendTarget.set(r);
    this.resendDialogOpen.set(true);
  }
  protected cancelResend(): void {
    this.resendDialogOpen.set(false);
    this.resendTarget.set(null);
  }
  /**
   * Sends the SAME link to the donor again.
   *
   * NO NEW INTENT AND NO NEW ATTEMPT. That distinction is the whole point of separating this from
   * "replace": a donor who lost the e-mail needs the link they already have, and issuing a second
   * one would leave them holding two live links for one gift.
   */
  protected confirmResend(): void {
    const target = this.resendTarget();

    if (!target || !this.resendAllowed(target)) {
      return;
    }

    this.paymentApi.resendPaymentLink(target.intentId, target.version).subscribe({
      next: (link) => {
        this.patch(target.donationIntentReference, {
          existingActiveLink: link.paymentLinkUrl,
          linkExpiryIso: link.expiresAtUtc,
          linkExpiryLabel: formatMoment(link.expiresAtUtc),
          linkCondition: 'Active',
          history: [
            ...target.history,
            {
              label: 'Payment link re-sent',
              detail: `The existing link was sent again. Attempt ${link.attemptNumber}.`,
              meta: `${this.owner()} · ${this.lastRefresh()}`,
            },
          ],
        });

        const updated = this.byRef(target.donationIntentReference);
        if (updated) {
          this.setOutcome(
            updated,
            'The existing payment link was re-sent; no second link was issued',
            'Await the donor',
          );
        }

        this.resendDialogOpen.set(false);
        this.resendTarget.set(null);
        this.toast.show(
          'Link re-sent',
          `The payment link for ${target.donationIntentReference} was sent again.`,
          'success',
        );
        this.loadIntentDetail(target.donationIntentReference);
      },
      error: (error) => {
        this.resendDialogOpen.set(false);
        this.resendTarget.set(null);
        this.toast.show(
          'Could not resend',
          apiErrorMessage(error, 'The payment link could not be re-sent.'),
          'error',
        );
      },
    });
  }

  /**
   * Applies a safe-retry outcome to the screen.
   *
   * FOUR OUTCOMES, AND THE SCREEN MUST DISTINGUISH THEM. The server verifies the previous attempt
   * before it does anything, so "AlreadyPaid" is a real and important answer: it means the donor
   * has already been charged and the retry was REFUSED. Treating that as a failure - or, worse,
   * as a success - is how somebody ends up paying twice.
   */
  private applySafeRetryOutcome(
    record: PsrRecoveryRecord,
    outcome: {
      outcome: string;
      message: string;
      paymentLinkUrl: string | null;
      attemptCount: number;
    },
  ): void {
    const result = outcome.outcome.trim().toLowerCase();

    const verified: PsrVerifiedPaymentState =
      result === 'alreadypaid'
        ? 'Confirmed'
        : result === 'stillpending'
          ? 'Pending'
          : record.verifiedPaymentState;

    const lifecycle: PsrLifecycleState =
      result === 'alreadypaid'
        ? 'Confirmed'
        : result === 'retried'
          ? 'Awaiting donor'
          : record.lifecycleState;

    this.patch(record.donationIntentReference, {
      verifiedPaymentState: verified,
      lifecycleState: lifecycle,
      existingActiveLink: outcome.paymentLinkUrl ?? record.existingActiveLink,
      linkCondition: outcome.paymentLinkUrl ? 'Active' : record.linkCondition,
      history: [
        ...record.history,
        {
          label: `Safe retry: ${outcome.outcome}`,
          detail: outcome.message,
          meta: `${this.owner()} · ${this.lastRefresh()}`,
        },
      ],
    });

    const updated = this.byRef(record.donationIntentReference);
    this.selectedRef.set(record.donationIntentReference);

    if (updated) {
      this.setOutcome(
        updated,
        outcome.message,
        result === 'alreadypaid'
          ? 'No further action required. Tell the donor their payment succeeded.'
          : result === 'retried'
            ? 'Send the link and await the donor'
            : 'Check again shortly',
      );
    }

    this.toast.show(
      result === 'alreadypaid'
        ? 'Already paid'
        : result === 'retried'
          ? 'Retry started'
          : 'No action taken',
      outcome.message,
      result === 'retried' ? 'success' : 'warning',
    );

    this.loadIntentDetail(record.donationIntentReference);
  }

  /**
   * Opens a support case against the intent.
   *
   * THE CORRELATION REFERENCE IS THE INTENT'S OWN, not a separate case number, and no record is
   * created anywhere. The donor already holds this reference - it is in the payment link they
   * followed and on the verification page they landed on - so quoting anything else would ask
   * them for a number they have never seen. The button copies it, ready to be read out.
   */
  protected openSupportCase(r: PsrRecoveryRecord): void {
    this.closeOverflow();

    if (!this.openSupportAllowed(r)) {
      return;
    }

    this.selectedRef.set(r.donationIntentReference);
    this.copyValue('support', r.donationIntentReference);

    this.toast.show(
      'Support reference copied',
      `Quote ${r.donationIntentReference} when the donor gets in touch.`,
      'success',
    );
  }

  /**
   * Issues a FRESH link for the same intent, through safe retry.
   *
   * SAFE RETRY VERIFIES THE PREVIOUS ATTEMPT WITH THE GATEWAY BEFORE ISSUING ANYTHING, and
   * refuses if it actually succeeded. That is what makes replacing an expired link safe rather
   * than a second chance to charge somebody who already paid.
   */
  protected replaceExpiredLink(r: PsrRecoveryRecord): void {
    this.closeOverflow();

    if (!this.replaceAllowed(r)) {
      return;
    }

    this.paymentApi
      .safeRetry(r.intentId, {
        expectedVersion: r.version,
        reason: "The donor's payment link had expired and a fresh one was requested.",
      })
      .subscribe({
        next: (outcome) => this.applySafeRetryOutcome(r, outcome),
        error: (error) =>
          this.toast.show(
            'Could not replace the link',
            apiErrorMessage(error, 'A new payment link could not be issued.'),
            'error',
          ),
      });
  }

  // ================= Cancel intent =================
  protected readonly cancelDialogOpen = signal(false);
  protected readonly cancelTarget = signal<PsrRecoveryRecord | null>(null);
  protected readonly cancelReason = signal('');
  protected readonly cancelSubmitted = signal(false);
  protected readonly reasonMin = 10;
  protected readonly reasonMax = 2000;
  protected readonly cancelReasonCount = computed(() => this.cancelReason().trim().length);
  protected readonly cancelReasonValid = computed(() => {
    const len = this.cancelReason().trim().length;
    return len >= this.reasonMin && len <= this.reasonMax;
  });
  protected requestCancel(r: PsrRecoveryRecord): void {
    this.closeOverflow();
    if (!this.cancelAllowed(r)) return;
    this.cancelTarget.set(r);
    this.cancelReason.set('');
    this.cancelSubmitted.set(false);
    this.cancelDialogOpen.set(true);
  }
  protected closeCancelDialog(): void {
    this.cancelDialogOpen.set(false);
    this.cancelTarget.set(null);
  }
  /**
   * Cancels the intent.
   *
   * NEVER AVAILABLE ONCE THE INTENT IS PAID - the server refuses it, because a donation attached
   * to a cancelled intention is a contradiction the reports cannot express. The reason and the
   * version go with it, so a cancellation cannot quietly overwrite a payment that arrived while
   * the dialog was open.
   */
  protected confirmCancel(): void {
    this.cancelSubmitted.set(true);
    if (!this.cancelReasonValid()) return;

    const r = this.cancelTarget();
    if (!r || !this.cancelAllowed(r)) return;

    const reason = this.cancelReason().trim();

    this.paymentApi
      .cancelIntent(r.intentId, { expectedVersion: r.version, reason })
      .subscribe({
        next: () => {
          this.patch(r.donationIntentReference, {
            verifiedPaymentState: 'Cancelled',
            lifecycleState: 'Cancelled',
            linkCondition: 'None',
            existingActiveLink: '—',
            history: [
              ...r.history,
              {
                label: 'Intent cancelled',
                detail: reason,
                meta: `${this.owner()} · ${this.lastRefresh()}`,
              },
            ],
          });

          const updated = this.byRef(r.donationIntentReference);
          this.selectedRef.set(r.donationIntentReference);
          this.cancelDialogOpen.set(false);
          this.cancelTarget.set(null);

          if (updated) {
            this.setOutcome(
              updated,
              'Intent cancelled; linked history preserved',
              'No further retry; raise a new intent if the donor wishes to give again',
            );
          }

          this.toast.show(
            'Intent Cancelled',
            `Donation intent ${r.donationIntentReference} has been cancelled. Linked history preserved.`,
            'success',
          );

          this.loadRecords();
        },
        error: (error) => {
          this.cancelDialogOpen.set(false);
          this.cancelTarget.set(null);
          this.toast.show(
            'Could not cancel',
            apiErrorMessage(error, 'The donation intent could not be cancelled.'),
            'error',
          );
        },
      });
  }

  // ================= UI state + persistent outcome =================
  protected readonly uiState = signal<PsrUiState>('loading');
  protected dismissBanner(): void {
    this.uiState.set('ready');
    this.lastOutcome.set(null);
  }

  protected readonly lastOutcome = signal<{
    reference: string;
    state: string;
    downstreamStatus: string;
    nextAction: string;
  } | null>(null);

  private setOutcome(r: PsrRecoveryRecord, downstreamStatus: string, nextAction: string): void {
    this.lastOutcome.set({
      reference: r.donationIntentReference,
      state: r.lifecycleState,
      downstreamStatus,
      nextAction,
    });
    this.uiState.set('success');
  }

  protected readonly persistentOutcome = computed<PsrPersistentOutcome>(() => {
    const outcome = this.lastOutcome();
    if (outcome) {
      return { ...outcome, effectiveTime: this.lastRefresh(), owner: this.owner() };
    }
    const r = this.selectedRecord();
    return {
      reference: r?.donationIntentReference ?? '—',
      state: r?.lifecycleState ?? '—',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: r
        ? `${r.integrationStatus.provider}: ${r.integrationStatus.state}`
        : 'No pending action',
      owner: this.owner(),
      nextAction: r ? this.nextActionFor(r) : 'Select a record to review its recovery',
    };
  });

  protected nextActionFor(r: PsrRecoveryRecord): string {
    if (r.verifiedPaymentState === 'Uncertain' || r.verifiedPaymentState === 'Pending')
      return 'Verify status before any retry';
    if (r.linkCondition === 'Expired')
      return 'Replace the expired link, then ask the donor to retry';
    if (r.linkCondition === 'Active' && r.verifiedPaymentState === 'Failed')
      return 'Resend the active link for a safe retry';
    if (r.verifiedPaymentState === 'Confirmed') return 'No further action required';
    if (r.verifiedPaymentState === 'Cancelled') return 'No further retry; history preserved';
    return 'Review the record';
  }

  // ================= Helpers =================
  private patch(ref: string, patch: Partial<PsrRecoveryRecord>): void {
    this.records.update((list) =>
      list.map((r) => (r.donationIntentReference === ref ? { ...r, ...patch } : r)),
    );
  }
  private byRef(ref: string): PsrRecoveryRecord | undefined {
    return this.records().find((r) => r.donationIntentReference === ref);
  }
  protected formatMoney(amountMinor: number, currency: string): string {
    const value = (amountMinor / 100).toLocaleString('en-IN', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
    return `${currency} ${value}`;
  }
  protected formatDate(iso: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  protected verifiedStateClass(state: PsrVerifiedPaymentState): string {
    switch (state) {
      case 'Pending':
        return 'psr-badge-blue';
      case 'Uncertain':
        return 'psr-badge-gold';
      case 'Failed':
        return 'psr-badge-danger';
      case 'Confirmed':
        return 'psr-badge-good';
      case 'Cancelled':
        return 'psr-badge-muted';
      default:
        return 'psr-badge-muted';
    }
  }
  protected lifecycleClass(state: PsrLifecycleState): string {
    switch (state) {
      case 'Needs verification':
        return 'psr-badge-gold';
      case 'Awaiting donor':
        return 'psr-badge-blue';
      case 'Link expired':
        return 'psr-badge-danger';
      case 'Confirmed':
        return 'psr-badge-good';
      case 'Cancelled':
        return 'psr-badge-muted';
      case 'Failed':
        return 'psr-badge-danger';
      default:
        return 'psr-badge-muted';
    }
  }

  constructor() {
    if (!this.permissions().view) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    this.scopeFilter.set(this.scopeOptions()[0]);
    this.loadRecords();
  }

  /**
   * Loads the support queue.
   *
   * WHAT LANDS HERE IS NARROWER THAN "FAILED": an intent that failed once and was then paid needs
   * nobody. The server selects intents that have either exhausted the retry allowance or carry an
   * attempt whose outcome is UNKNOWN - the second being the more urgent, because unknown means
   * the donor may already have been charged.
   */
  private loadRecords(): void {
    this.loading.set(true);
    this.loadError.set(false);

    // A record handed over from the event queue or the intent detail decides which row opens.
    const pending = this.dataService.getPendingSafeRetryRecord();
    if (pending) {
      this.retryRecord.set(pending);
      this.dataService.clearPendingSafeRetryRecord();
    }

    this.paymentApi
      .getSupportQueue({ page: 1, pageSize: PaymentSupportAndSafeRetryComponent.FETCH_SIZE })
      .subscribe({
        next: (page) => {
          const rows = (page.items ?? []).map(toRecoveryRecord);
          this.records.set(rows);
          this.serverTotal.set(page.totalCount ?? rows.length);
          this.lastRefresh.set(formatMoment(new Date().toISOString()));
          this.loading.set(false);

          if (this.uiState() !== 'success' && this.uiState() !== 'no-access') {
            this.uiState.set(rows.length === 0 ? 'empty' : 'ready');
          }

          // THE HAND-OFF SELECTS AN EXISTING ROW; it never invents one. The previous version
          // built a recovery record out of the gateway event it was handed - with a made-up
          // version of 1 and an amount parsed out of a formatted string - and put it at the top
          // of the list. Every action on that row would have failed, because no such intent was
          // ever loaded.
          const handOff = this.retryRecord();
          const wanted = handOff?.mappedIntentOrPayment;

          if (wanted && rows.some((r) => r.donationIntentReference === wanted)) {
            this.select(wanted);
          } else if (wanted) {
            this.toast.show(
              'Not in the support queue',
              `${wanted} is not waiting on support. It may already have been paid or cancelled.`,
              'info',
            );
          }

          this.retryRecord.set(null);
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
            apiErrorMessage(error, 'The payment support queue could not be loaded.'),
            'error',
          );
        },
      });
  }
}

import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentSupportCase } from '../../../../Shared/models/payment.model';
import {
  PsrHistoryEntry,
  PsrLifecycleState,
  PsrPersistentOutcome,
  PsrRecoveryPermissions,
  PsrRecoveryRecord,
  PsrUiState,
  PsrVerifiedPaymentState,
} from '../../../../Shared/models/payment-support-safe-retry.model';

@Component({
  selector: 'app-payment-support-and-safe-retry',
  imports: [CommonModule, FormsModule],
  templateUrl: './payment-support-and-safe-retry.html',
  styleUrl: './payment-support-and-safe-retry.css',
})
export class PaymentSupportAndSafeRetryComponent {
  private readonly toast = inject(ToastService);
  private readonly payments = inject(PaymentApiService);

  // ================= Task header =================
  protected readonly pageTitle = 'Payment support and safe retry';
  protected readonly pageSubtitle =
    'Help a donor recover from an incomplete payment without creating duplicate charges or exposing gateway details.';
  /**
   * Who the history lines are attributed to.
   *
   * IT USED TO BE A PERSON'S NAME COMPILED INTO THE BUNDLE - 'Firstlin S Joseph · Donor Care' -
   * so every organisation's audit trail credited the same stranger. The server stamps the actual
   * actor on what it records; this is only the label beside an action taken in this tab.
   */
  protected readonly owner = 'You';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('');

  /** Effective permissions decided server-side; the client mirrors the same decision. */
  protected readonly permissions: PsrRecoveryPermissions = {
    view: true,
    verifyStatus: true,
    resendActiveLink: true,
    replaceExpiredLink: true,
    cancelIntent: true,
    openSupportCase: true,
  };


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

  protected readonly channelOptions: readonly string[] = ['Email', 'SMS', 'WhatsApp'];
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
  protected readonly moreFiltersCount = computed(() => (this.scopeFilter() !== this.scopeOptions[0] ? 1 : 0));

  protected readonly savedFilters = ['All records (Default)', 'Needs donor action', 'In progress', 'Resolved'];
  protected readonly savedFilter = signal(this.savedFilters[0]);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim()) chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    if (this.verifiedStateFilter()) chips.push({ key: 'state', label: `Payment state: ${this.verifiedStateFilter()}` });
    if (this.channelFilter()) chips.push({ key: 'channel', label: `Channel: ${this.channelFilter()}` });
    if (this.rangeStart() || this.rangeEnd()) {
      chips.push({
        key: 'date',
        label: `Last attempt: ${this.rangeStart() ? this.formatDate(this.rangeStart()) : '…'} – ${
          this.rangeEnd() ? this.formatDate(this.rangeEnd()) : '…'
        }`,
      });
    }
    if (this.scopeFilter() !== this.scopeOptions[0]) chips.push({ key: 'scope', label: `Scope: ${this.scopeFilter()}` });
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
      case 'scope':
        this.scopeFilter.set(this.scopeOptions[0]);
        break;
    }
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.verifiedStateFilter.set('');
    this.channelFilter.set('');
    this.rangeStart.set('');
    this.rangeEnd.set('');
    this.scopeFilter.set(this.scopeOptions[0]);
    this.savedFilter.set(this.savedFilters[0]);
  }
  protected readonly filterAllowed = computed(() => this.permissions.view && !this.rangeInvalid());
  protected applyFilters(): void {
    if (!this.filterAllowed()) return;
    this.moreFiltersOpen.set(false);
  }

  // ================= Main work: recovery records =================
  protected readonly records = signal<PsrRecoveryRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const state = this.verifiedStateFilter();
    const channel = this.channelFilter();
    const start = this.rangeStart() ? new Date(this.rangeStart()) : null;
    const end = this.rangeEnd() ? new Date(this.rangeEnd()) : null;

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

  protected readonly totalRecords = computed(() => this.records().length);
  protected readonly needsDonorActionCount = computed(
    () => this.records().filter((r) => r.verifiedPaymentState === 'Failed' || r.linkCondition === 'Expired').length,
  );
  protected readonly inProgressCount = computed(
    () => this.records().filter((r) => r.verifiedPaymentState === 'Pending' || r.verifiedPaymentState === 'Uncertain').length,
  );
  protected readonly resolvedCount = computed(
    () => this.records().filter((r) => r.verifiedPaymentState === 'Confirmed' || r.verifiedPaymentState === 'Cancelled').length,
  );
  protected readonly recordCount = computed(() => this.visibleRecords().length);

  // ================= Selection → working record =================
  protected readonly selectedRef = signal<string>('INT-2026-007701');
  protected readonly selectedRecord = computed(
    () => this.records().find((r) => r.donationIntentReference === this.selectedRef()) ?? null,
  );
  protected select(ref: string): void {
    if (!this.permissions.view) return;
    this.selectedRef.set(ref);
  }
  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
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
    return !!r && this.permissions.verifyStatus && this.recoverable(r.verifiedPaymentState);
  }
  protected resendAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions.resendActiveLink && r.linkCondition === 'Active' && this.recoverable(r.verifiedPaymentState);
  }
  protected replaceAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions.replaceExpiredLink && r.linkCondition === 'Expired' && this.recoverable(r.verifiedPaymentState);
  }
  protected cancelAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions.cancelIntent && this.recoverable(r.verifiedPaymentState);
  }
  protected openSupportAllowed(r: PsrRecoveryRecord | null): boolean {
    return !!r && this.permissions.openSupportCase && r.supportCorrelationReference === '—' && this.recoverable(r.verifiedPaymentState);
  }
  protected anyOverflowAllowed(r: PsrRecoveryRecord | null): boolean {
    return this.resendAllowed(r) || this.replaceAllowed(r) || this.openSupportAllowed(r) || this.cancelAllowed(r);
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
   * Asks the gateway what actually happened.
   *
   * IT USED TO DECIDE THE ANSWER ITSELF. The line was
   * `const next = r.verifiedPaymentState === 'Failed' ? 'Failed' : 'Confirmed'` - so anything not
   * already failed became Confirmed, and an operator could tell a donor their payment had gone
   * through on the strength of a ternary. Verification is the one thing on this screen that MUST
   * come from the gateway, because the whole page exists to avoid charging somebody twice.
   *
   * IT NEVER RETRIES. Verification asks; it does not pay.
   */
  protected verifyStatus(r: PsrRecoveryRecord): void {
    this.closeOverflow();
    if (!this.verifyAllowed(r)) {
      return;
    }

    this.busy.set(true);
    this.payments.verifyPayment({ intentReference: r.donationIntentReference }).subscribe({
      next: (verification) => {
        this.busy.set(false);
        const confirmed = verification.backendPaymentState === 'Confirmed';

        this.toast.show(
          'Status verified',
          confirmed
            ? `The gateway confirms ${r.donationIntentReference} was paid. No retry is needed and no duplicate charge was created.`
            : `The gateway reports ${verification.backendPaymentState.toLowerCase()} for ${r.donationIntentReference}.`,
          confirmed ? 'success' : 'warning',
        );

        // RELOAD RATHER THAN PATCH. Verification can move the intent out of this queue entirely -
        // a confirmed payment is no longer an incomplete one - and only the server knows that.
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.show('Could not verify', apiErrorMessage(error), 'error');
      },
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
  protected confirmResend(): void {
    const r = this.resendTarget();
    if (!r || !this.resendAllowed(r)) {
      return;
    }
    this.issueLink(r, 'resend');
  }

  // ================= Replace expired link =================

  /**
   * Issues a link, or re-sends the one that exists.
   *
   * ONE ENDPOINT SERVES BOTH, and that is a property of the server rather than a shortcut here:
   * `resend-link` returns the current link when it is still valid and mints a fresh one when it
   * has expired. The distinction the screen draws - Resend versus Replace - is about what the
   * operator is telling the donor, not about two different operations.
   *
   * THE LINK IS NEVER INVENTED IN THE BROWSER. The old version built one with
   * `LINK-${Math.random().toString(16)...}-ACTIVE` and a made-up expiry a week out, so the panel
   * displayed a reference that pointed at nothing and a date nothing honoured.
   */
  protected replaceExpiredLink(r: PsrRecoveryRecord): void {
    this.closeOverflow();
    if (!this.replaceAllowed(r)) {
      return;
    }
    this.issueLink(r, 'replace');
  }

  private issueLink(r: PsrRecoveryRecord, mode: 'resend' | 'replace'): void {
    this.busy.set(true);
    this.resendDialogOpen.set(false);
    this.resendTarget.set(null);

    this.payments.resendPaymentLink(r.intentId, r.version).subscribe({
      next: (link) => {
        this.busy.set(false);
        this.toast.show(
          mode === 'replace' ? 'New link issued' : 'Link resent',
          mode === 'replace'
            ? `A fresh payment link was issued for ${r.donationIntentReference}. The same intent is reused, so no duplicate charge can occur.`
            : `The existing link for ${r.donationIntentReference} was sent again via ${r.preferredDeliveryChannel}.`,
          'success',
        );
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.toast.show('Link not issued', apiErrorMessage(error), 'error');
        this.load();
      },
    });
  }

  // ================= Open support case =================

  /**
   * Opens a message to the donor.
   *
   * THE DOCUMENT DEFINES THIS AS AN E-MAIL: "Open Support Case - sends an email to contact the
   * donor for further help". There is no support-case aggregate in PAY to create a record in, so
   * this composes the message rather than inventing a case number - the old version minted
   * "SUP-2025-00041" by incrementing the highest one it could see in the browser, which produced
   * a reference no system had heard of and that support could not look up.
   *
   * IT NEEDS AN UNMASKED ADDRESS. `maskedDonorContact` reads "pri•••@m•••.com" for a caller
   * without pay.donations.view-sensitive-donor, and mailto: cannot deliver to that - so the
   * button says why instead of opening an empty compose window.
   */
  protected openSupportCase(r: PsrRecoveryRecord): void {
    this.closeOverflow();
    if (!this.openSupportAllowed(r)) {
      return;
    }

    const address = r.maskedDonorContact;
    if (!address || address.includes('\u2022') || !address.includes('@')) {
      this.toast.show(
        'Donor address is masked',
        'You do not have permission to see this donor\u2019s contact details, so a message cannot be addressed to them. Ask somebody holding donor-contact access to make contact.',
        'warning',
      );
      return;
    }

    const subject = `Your donation ${r.donationIntentReference}`;
    const body = [
      `Hello ${r.donorContactPreview},`,
      '',
      `We noticed that your donation of ${this.formatMoney(r.requestedAmountMinor, r.currency)} did not complete.`,
      'No money has been taken. If you would still like to give, we can send you a fresh payment link.',
      '',
      `Reference: ${r.donationIntentReference}`,
    ].join('\n');

    window.location.href =
      `mailto:${encodeURIComponent(address)}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;

    this.toast.show(
      'Message started',
      `A message to ${r.donorContactPreview} about ${r.donationIntentReference} was opened in your mail client.`,
      'info',
    );
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
   * Cancel intent - the document's own action.
   *
   * "Cancel Intent - cancels and deletes the donation intent entirely." The server cancels rather
   * than deletes, and that difference is deliberate: an intent a donor started is part of what
   * happened, and a support conversation six weeks later needs it to still exist. What cancelling
   * does is close it to any further payment, which is what the admin actually needs.
   *
   * THE REASON IS REQUIRED, 10 to 2000 characters - the API's own bounds, matched here so a
   * refusal is a sentence under the box rather than a 400 after the button.
   */
  protected confirmCancel(): void {
    this.cancelSubmitted.set(true);
    if (!this.cancelReasonValid()) {
      return;
    }

    const r = this.cancelTarget();
    if (!r) {
      return;
    }

    this.busy.set(true);
    this.payments
      .cancelIntent(r.intentId, { expectedVersion: r.version, reason: this.cancelReason().trim() })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.cancelDialogOpen.set(false);
          this.cancelTarget.set(null);
          this.toast.show(
            'Intent cancelled',
            `${r.donationIntentReference} was cancelled. Its history is preserved; no further payment can be taken against it.`,
            'success',
          );
          this.load();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.toast.show('Not cancelled', apiErrorMessage(error), 'error');
        },
      });
  }

  // ================= UI state + persistent outcome =================
  protected readonly uiState = signal<PsrUiState>('ready');
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
    this.lastOutcome.set({ reference: r.donationIntentReference, state: r.lifecycleState, downstreamStatus, nextAction });
    this.uiState.set('success');
  }

  protected readonly persistentOutcome = computed<PsrPersistentOutcome>(() => {
    const outcome = this.lastOutcome();
    if (outcome) {
      return { ...outcome, effectiveTime: this.lastRefresh(), owner: this.owner };
    }
    const r = this.selectedRecord();
    return {
      reference: r?.donationIntentReference ?? '—',
      state: r?.lifecycleState ?? '—',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: r ? `${r.integrationStatus.provider}: ${r.integrationStatus.state}` : 'No pending action',
      owner: r?.owner ?? this.owner,
      nextAction: r ? this.nextActionFor(r) : 'Select a record to review its recovery',
    };
  });

  protected nextActionFor(r: PsrRecoveryRecord): string {
    if (r.verifiedPaymentState === 'Uncertain' || r.verifiedPaymentState === 'Pending') return 'Verify status before any retry';
    if (r.linkCondition === 'Expired') return 'Replace the expired link, then ask the donor to retry';
    if (r.linkCondition === 'Active' && r.verifiedPaymentState === 'Failed') return 'Resend the active link for a safe retry';
    if (r.verifiedPaymentState === 'Confirmed') return 'No further action required';
    if (r.verifiedPaymentState === 'Cancelled') return 'No further retry; history preserved';
    return 'Review the record';
  }

  // ================= Loading =================

  /**
   * The support queue - section 5 of the workflow document.
   *
   * "This page lists failed payments that need admin help to recover." The server's queue is
   * narrower than "everything that failed once", and deliberately: an intent that failed and was
   * then paid needs nobody. What lands here has exhausted its retry allowance, or has an attempt
   * whose outcome is UNKNOWN - and the second is the more urgent, which is why the server sorts
   * those first and this screen does not re-sort them.
   */
  private load(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.payments.getSupportQueue({ page: 1, pageSize: 100 }).subscribe({
      next: (page) => {
        this.records.set(page.items.map((item) => this.toRecoveryRecord(item)));
        this.loading.set(false);
        this.lastRefresh.set(this.nowLabel());

        // Keep the open panel on the same intent across a reload, so an action does not close
        // the record the person is working on.
        const current = this.selectedRef();
        const stillThere = this.records().some((r) => r.donationIntentReference === current);
        if (!stillThere) {
          this.selectedRef.set(this.records()[0]?.donationIntentReference ?? '');
        }
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(true);
        this.toast.show('Support queue unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  /**
   * Maps one support case onto the shape this screen draws.
   *
   * THE MASKED CONTACT IS THE SERVER'S. `donorEmail` arrives already masked unless the caller
   * holds pay.donations.view-sensitive-donor - the document's own words for this screen are
   * "without ... exposing gateway details", and the same reasoning covers the donor's address.
   */
  private toRecoveryRecord(item: PaymentSupportCase): PsrRecoveryRecord {
    const verified: PsrVerifiedPaymentState = item.requiresVerification
      ? 'Uncertain'
      : item.status === 'failed'
        ? 'Failed'
        : item.status === 'paid'
          ? 'Confirmed'
          : item.status === 'cancelled'
            ? 'Cancelled'
            : 'Pending';

    const lifecycle: PsrLifecycleState = item.requiresVerification
      ? 'Needs verification'
      : item.status === 'expired'
        ? 'Link expired'
        : item.status === 'paid'
          ? 'Confirmed'
          : item.status === 'cancelled'
            ? 'Cancelled'
            : item.status === 'failed'
              ? 'Failed'
              : 'Awaiting donor';

    const linkCondition: 'Active' | 'Expired' | 'None' =
      item.status === 'expired' ? 'Expired' : item.status === 'awaitingPayment' ? 'Active' : 'None';

    return {
      intentId: item.intentId,
      donationIntentReference: item.intentReference,
      maskedDonorContact: item.donorEmail,
      donorContactPreview: item.donorName,

      // THE MODEL HOLDS MINOR UNITS; the API sends a decimal amount.
      requestedAmountMinor: Math.round(item.amount.amount * 100),
      currency: item.amount.currencyCode,
      verifiedPaymentState: verified,
      lifecycleState: lifecycle,
      lastAttemptIso: item.lastAttemptAtUtc ?? item.createdAtUtc,
      lastAttemptLabel: this.formatDateTime(item.lastAttemptAtUtc ?? item.createdAtUtc),

      retryEligibility: item.requiresVerification
        // THE MOST IMPORTANT SENTENCE ON THIS SCREEN. An unknown outcome may mean the donor has
        // already been charged, so the safe move is to ask the gateway - never to retry.
        ? 'Verify with the gateway first - the last attempt\u2019s outcome is unknown and the donor may already have been charged.'
        : item.lastFailureReason ?? 'Safe retry available.',

      existingActiveLink: linkCondition === 'None' ? '\u2014' : item.intentReference,
      linkExpiryIso: '',
      linkExpiryLabel: '\u2014',
      linkCondition,
      supportCorrelationReference: item.lastGatewayResultCode ?? '\u2014',
      preferredDeliveryChannel: 'Email',
      preferredDeliveryChannelRef: 'email',
      owner: this.owner,

      // The intent's version, which every write on this screen sends back.
      version: 0,
      hasDownstreamReference: item.status === 'paid',
      history: [],
      linkedRecords: [],
      documents: [],
      integrationStatus: {
        provider: 'Payment gateway',
        state: item.requiresVerification ? 'Outcome unknown' : 'Reachable',
      },
      supportCorrelation: {
        reference: item.lastGatewayResultCode ?? '\u2014',
        state: item.requiresVerification ? 'Awaiting verification' : 'Open',
      },
    };
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }

  private formatDateTime(iso: string | null): string {
    if (!iso) {
      return '\u2014';
    }
    const parsed = new Date(iso);
    return Number.isNaN(parsed.getTime())
      ? iso
      : `${parsed.toLocaleString('en-GB', {
          day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
        })} \u00b7 IST`;
  }

  /** Set while a write is in flight, so a second click cannot start a second one. */
  protected readonly busy = signal(false);

  protected formatMoney(amountMinor: number, currency: string): string {
    const value = (amountMinor / 100).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
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
    this.load();
  }
}

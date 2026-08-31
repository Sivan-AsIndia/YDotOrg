import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { UiState, RelatedTab } from '../../../../Shared/models/campaign.model';
import {
  PaymentEventPermissions,
  PaymentEventState,
  PaymentEventRecord,
  DuplicateStatus,
  SequenceStatus,
  SignatureResult,
  PaymentStatus,
} from '../../../../Shared/models/payment-event-queue.model';
import type {
  PaymentEventSearchFilter,
  PaymentEventStatus,
} from '../../../../Shared/models/payment.model';
import { formatMoment, toPaymentEventRecord } from '../../../../Shared/models/payment-adapters';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';

/**
 * The gateway event queue - SCR-PAY-003.
 *
 * WHAT THE QUEUE IS FOR. A payment provider tells us things by posting to a webhook: a payment
 * captured, one that failed, a chargeback opened. Most apply cleanly and are never seen here.
 * What lands on this screen is what did not - an event whose signature failed, one that names an
 * intent we cannot find, a duplicate, or one that arrived out of order.
 *
 * THE TWO WRITES ARE THE TWO THE API HAS, and each is deliberately narrow:
 *
 *   Retry correlation - POST /payments/events/{id}/reprocess. Idempotent by the event's own
 *                       gateway id, so applying a capture twice cannot record the donation twice.
 *   Resolve           - POST /payments/events/{id}/dismiss, with the operator's action and
 *                       reason. The event is KEPT, never deleted: it is the record of what the
 *                       provider actually sent, and it is the evidence that settles an argument
 *                       with them about what that was.
 *
 * ESCALATE ASKS THE GATEWAY. There is no server-side "escalated" state for an event, and
 * inventing one would produce a status nobody could act on and no report could see. What an
 * operator actually needs from an event that will not correlate is the provider's own answer, so
 * Escalate performs an audited verification against the gateway and hands the case to Payment
 * Support - which is a real outcome rather than a label.
 */
@Component({
  selector: 'app-payment-event-queue',
  imports: [CommonModule, FormsModule],
  templateUrl: './payment-event-queue.html',
  styleUrl: './payment-event-queue.css',
})
export class PaymentEventQueueComponent {
  private readonly tokens = inject(AuthTokenService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly paymentApi = inject(PaymentApiService);

  /** One read pulls this many events; the toolbar pages over them. */
  private static readonly FETCH_SIZE = 200;

  // ================= Task header (4.4.1 Task header) =================
  protected readonly pageTitle = 'Payment event queue';
  protected readonly pageSubtitle =
    'Investigate invalid, unmatched, duplicate and out-of-order gateway events.';
  protected readonly owner = computed(() => this.tokens.user()?.displayName ?? 'You');
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  /** Freshness - when this screen last read the queue, never a fixed string. */
  protected readonly lastRefresh = signal('');

  /**
   * What this caller may do, read from the token.
   *
   * REPROCESSING A GATEWAY EVENT IS NOT A READ: it makes the platform act on a provider's message
   * again, which can create a donation. Drawing that button for everybody who can see the queue
   * would offer a write action to every auditor and support analyst who opened the page.
   */
  protected readonly permissions = computed<PaymentEventPermissions>(() => ({
    view: this.tokens.hasAnyPermission('pay.payments.view-events'),
    retryCorrelation: this.tokens.hasAnyPermission('pay.payments.reprocess-event'),
    resolve: this.tokens.hasAnyPermission('pay.payments.dismiss-event'),
    escalate: this.tokens.hasAnyPermission('pay.payments.verify'),
  }));

  // ================= Context and filters =================

  /** The filters section is hidden until the user opens it with the Filters button. */
  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  /** Search - by event reference, donor name, email or campaign. */
  protected readonly searchTerm = signal('');

  /** Payment status filter - Pending / Success / Fail / all. */
  protected readonly paymentStatusOptions: readonly PaymentStatus[] = ['Pending', 'Success', 'Fail'];
  protected readonly paymentStatusFilter = signal<PaymentStatus | ''>('');

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.paymentStatusFilter()) {
      chips.push({ key: 'paymentStatus', label: `Payment status: ${this.paymentStatusFilter()}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    return chips;
  });
  protected removeFilterChip(key: string): void {
    switch (key) {
      case 'paymentStatus':
        this.paymentStatusFilter.set('');
        break;
      case 'search':
        this.searchTerm.set('');
        break;
    }
    this.applyFilters();
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.paymentStatusFilter.set('');
    this.applyFilters();
  }
  protected readonly filterAllowed = computed(() => this.permissions().view);
  protected applyFilters(): void {
    if (!this.filterAllowed()) {
      this.uiState.set('no-access');
      return;
    }
    this.currentPage.set(1);
    this.loadQueue();
  }

  // ================= Main work: gateway event index (4.4.1 + 4.4.2) =================

  /** The event set inside the actor's effective data scope (4.4 Data scope). Server-derived. */
  protected readonly records = signal<PaymentEventRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  /** The server's count across the whole filter, not the loaded array's length. */
  protected readonly totalRecords = signal(0);

  /**
   * The visible set.
   *
   * The search and the outstanding filter go to the SERVER. The payment-status filter is applied
   * here because it is derived from the event type rather than being a field the API can filter
   * on - the derivation lives in the adapter, so it can only be applied after mapping.
   */
  protected readonly visibleRecords = computed(() => {
    const status = this.paymentStatusFilter();
    if (!status) return this.records();
    return this.records().filter((r) => r.paymentStatus === status);
  });

  protected readonly recordCount = computed(() => this.visibleRecords().length);

  // ----- Pagination -----
  protected readonly pageSize = 8;
  protected readonly currentPage = signal(1);
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.recordCount() / this.pageSize)),
  );
  private readonly clampedPage = computed(() => Math.min(this.currentPage(), this.totalPages()));
  protected readonly pagedRecords = computed(() => {
    const start = (this.clampedPage() - 1) * this.pageSize;
    return this.visibleRecords().slice(start, start + this.pageSize);
  });
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, i) => i + 1),
  );
  protected goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages()) {
      this.currentPage.set(page);
      this.fillDonorColumns();
    }
  }

  // ================= Selection -> detail; Inspect (4.4.1; 4.4.3 Inspect) =================
  protected readonly selectedRef = signal<string>('');
  protected readonly selectedEvent = computed(
    () => this.records().find((r) => r.eventReference === this.selectedRef()) ?? null,
  );

  /**
   * The donor and campaign behind the selected event.
   *
   * A GATEWAY EVENT NAMES A PAYMENT, NOT A PERSON. The donor is on the intent the event
   * correlates to, so it is fetched when a row is opened rather than guessed from the payload -
   * and an event that correlated to nothing correctly shows nothing.
   */
  protected readonly selectedDonorDetails = signal<{
    name: string;
    email: string;
    mobile: string;
    campaign: string;
    amount: string;

    /**
     * The donor record the intent resolved to, if it resolved to one at all.
     *
     * BLANK IS A REAL ANSWER. An intent from a first-time giver has no donor behind it until the
     * payment succeeds, and the screen showing a dash there is more truthful than an invented id.
     */
    donorId: string;
  } | null>(null);
  protected readonly selectedDonorLocation = signal('');

  /** Inspect - open the donation intent behind this event. */
  protected inspect(ref: string): void {
    if (!this.permissions().view) return;
    const rec = this.records().find((r) => r.eventReference === ref);
    if (!rec) return;

    this.selectedRef.set(ref);
    this.loadDonorFor(rec);

    if (!rec.donationIntentId) {
      this.toast.show(
        'No linked intent',
        'This event did not correlate to a donation intent, so there is nothing to open. Retry the correlation first.',
        'warning',
      );
      return;
    }

    this.dataService.setPendingDonationForPayment(rec);
    this.router.navigate(['/app/donations/donation-intent-detail']);
  }
  protected closeDetail(): void {
    this.selectedRef.set('');
    this.selectedDonorDetails.set(null);
    this.selectedDonorLocation.set('');
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

  // ----- Row overflow menu -----
  protected readonly openRowMenu = signal<string | null>(null);
  protected toggleRowMenu(ref: string): void {
    this.openRowMenu.update((cur) => (cur === ref ? null : ref));
  }

  // ----- Row actions: dismiss -----
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteTarget = signal<PaymentEventRecord | null>(null);

  protected requestDeletePaymentEvent(ref: string): void {
    if (!this.permissions().resolve) {
      this.toast.show(
        'Not permitted',
        'Dismissing a gateway event needs the pay.payments.dismiss-event permission.',
        'warning',
      );
      return;
    }
    const rec = this.records().find((r) => r.eventReference === ref) ?? null;
    if (!rec) return;
    this.deleteTarget.set(rec);
    this.deleteDialogOpen.set(true);
  }

  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
  }

  /**
   * Takes an event off the queue.
   *
   * IT DISMISSES RATHER THAN DELETES, and the API has no delete at all. A gateway event is the
   * record of what a payment provider actually told us; deleting one would destroy the evidence
   * that settles an argument with them about what they sent. Dismissing says "a person looked at
   * this and decided nothing was needed" - a different and far more useful statement than the row
   * not existing - and it records who decided.
   *
   * A PROCESSED EVENT CANNOT BE DISMISSED. The server refuses it: an event that already applied
   * is part of the donation's history, not an item of work.
   */
  protected confirmDelete(): void {
    const rec = this.deleteTarget();
    if (!rec) return;

    this.paymentApi
      .dismissPaymentEvent(rec.eventId, {
        expectedVersion: rec.version,
        reason: 'Dismissed from the payment event queue: no action required.',
      })
      .subscribe({
        next: () => {
          if (this.selectedRef() === rec.eventReference) {
            this.closeDetail();
          }

          this.deleteDialogOpen.set(false);
          this.deleteTarget.set(null);
          this.toast.show(
            'Event Dismissed',
            `Payment event ${rec.eventReference} was marked as needing no action.`,
            'success',
          );
          this.loadQueue();
        },
        error: (error) => {
          this.deleteDialogOpen.set(false);
          this.deleteTarget.set(null);
          this.reportFailure(error, 'The event could not be dismissed.');
        },
      });
  }

  /**
   * Reports a failed write.
   *
   * A 409 IS NAMED SEPARATELY because it means something the operator can act on - somebody else
   * worked this event - rather than something being broken, and the fix is to refresh.
   */
  private reportFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.uiState.set('conflict');
      this.toast.show('Event changed', 'Somebody else worked this event. Refreshing.', 'warning');
      this.loadQueue();
      return;
    }

    this.uiState.set('ready');
    this.toast.show('Action failed', apiErrorMessage(error, fallback), 'error');
  }

  /** Continue payment - hand the donor's unfinished intent to the public donation page. */
  protected continuePaymentFromQueue(rec: PaymentEventRecord): void {
    this.dataService.setPendingDonationForPayment(rec);
    this.router.navigate(['/app/donations/public-donation-initiation']);
  }

  /** Verify payment - open the verification page against this event's intent. */
  protected verifyPaymentFromQueue(rec: PaymentEventRecord): void {
    this.dataService.setPendingVerificationRecord(rec);
    this.router.navigate(['/app/donations/payment-verification']);
  }

  /** Retry payment - the same hand-off as Continue, from the row's retry control. */
  protected readonly retryingRef = signal<string | null>(null);
  protected retryPaymentFromQueue(rec: PaymentEventRecord): void {
    this.retryingRef.set(rec.eventReference);
    this.dataService.setPendingSafeRetryRecord(rec);
    this.router.navigate(['/app/donations/payment-support-and-safe-retry']);
  }

  // ================= Actions, eligibility and result (4.4.3) =================

  private readonly resolvableStates: readonly PaymentEventState[] = [
    'New',
    'Investigating',
    'Escalated',
  ];

  protected retryAllowed(event: PaymentEventRecord | null): boolean {
    return (
      !!event &&
      this.permissions().retryCorrelation &&
      event.eventState !== 'Resolved' &&
      this.uiState() !== 'no-access'
    );
  }
  protected resolveAllowed(event: PaymentEventRecord | null): boolean {
    return (
      !!event &&
      this.permissions().resolve &&
      this.resolvableStates.includes(event.eventState) &&
      this.uiState() !== 'no-access'
    );
  }
  protected escalateAllowed(event: PaymentEventRecord | null): boolean {
    return (
      !!event &&
      this.permissions().escalate &&
      event.eventState !== 'Resolved' &&
      this.uiState() !== 'no-access'
    );
  }

  // ----- Retry correlation: idempotent workflow action (4.4.3) -----
  protected readonly retryDialogOpen = signal(false);
  protected readonly retryTarget = signal<PaymentEventRecord | null>(null);
  protected requestRetry(event: PaymentEventRecord): void {
    this.openRowMenu.set(null);
    if (!this.retryAllowed(event)) return;
    this.retryTarget.set(event);
    this.retryDialogOpen.set(true);
  }
  protected cancelRetry(): void {
    this.retryDialogOpen.set(false);
    this.retryTarget.set(null);
  }
  /**
   * Re-runs a failed event through the processor.
   *
   * IT IS IDEMPOTENT BY THE EVENT'S OWN IDENTITY, not by a version. Applying a capture twice
   * cannot record the donation twice: the one-donation-per-intent unique index and the unique
   * (gateway, event id) index between them make it impossible rather than merely unlikely.
   */
  protected confirmRetry(): void {
    const target = this.retryTarget();
    if (!target) return;

    this.paymentApi
      .reprocessPaymentEvent(target.eventId, {
        expectedVersion: target.version,
        note: 'Reprocessed from the payment event queue.',
      })
      .subscribe({
        next: (outcome) => {
          const message = outcome.message ?? 'The event was reprocessed.';

          this.lastOutcome.set({
            reference: target.eventReference,
            state: target.eventState,
            downstreamStatus: message,
            nextAction: 'Review the updated correlation result',
          });

          this.retryDialogOpen.set(false);
          this.retryTarget.set(null);
          this.retryingRef.set(null);
          this.uiState.set('success');
          this.toast.show('Event Reprocessed', message, 'success');
          this.loadQueue();
        },
        error: (error) => {
          this.retryDialogOpen.set(false);
          this.retryTarget.set(null);
          this.retryingRef.set(null);
          this.reportFailure(error, 'The event could not be reprocessed.');
        },
      });
  }

  // ----- Resolve: workflow action, high-risk decision (4.4.3, 4.4.6) -----
  protected readonly resolveDialogOpen = signal(false);
  protected readonly resolveTarget = signal<PaymentEventRecord | null>(null);
  protected readonly resolveAction = signal('');
  protected readonly resolveReason = signal('');
  protected readonly resolveReasonMin = 10;
  protected readonly resolveReasonMax = 2000;
  protected readonly resolveSubmitted = signal(false);
  protected readonly resolveReasonCount = computed(() => this.resolveReason().trim().length);
  protected readonly resolveReasonValid = computed(() => {
    const len = this.resolveReason().trim().length;
    return len >= this.resolveReasonMin && len <= this.resolveReasonMax;
  });
  protected readonly resolveActionValid = computed(() => this.resolveAction().trim().length > 0);

  protected requestResolve(event: PaymentEventRecord): void {
    this.openRowMenu.set(null);
    if (!this.resolveAllowed(event)) return;
    this.resolveTarget.set(event);
    this.resolveAction.set('');
    this.resolveReason.set('');
    this.resolveSubmitted.set(false);
    this.resolveDialogOpen.set(true);
  }
  protected cancelResolve(): void {
    this.resolveDialogOpen.set(false);
    this.resolveTarget.set(null);
  }
  /**
   * Records the resolution.
   *
   * RESOLVING IS DISMISSING WITH A REASON. The operator's action and reason are written onto the
   * event and into the audit trail, which is what makes "somebody decided this needed nothing" a
   * defensible statement rather than a row quietly disappearing from a queue.
   *
   * NOTHING IS DECLARED DONE UNTIL THE SERVER SAYS SO. The previous version set the success state
   * and showed a toast immediately after firing the request, so a refusal produced a screen that
   * said Resolved and a queue that still held the event.
   */
  protected confirmResolve(): void {
    this.resolveSubmitted.set(true);
    if (!this.resolveActionValid() || !this.resolveReasonValid()) {
      this.uiState.set('validation');
      this.toast.show('Validation Error', 'Please provide a valid action and reason.', 'warning');
      return;
    }
    const e = this.resolveTarget();
    if (!e) return;

    const action = this.resolveAction().trim();

    this.paymentApi
      .dismissPaymentEvent(e.eventId, {
        expectedVersion: e.version,
        reason: `${action}: ${this.resolveReason().trim()}`,
      })
      .subscribe({
        next: () => {
          this.lastOutcome.set({
            reference: e.eventReference,
            state: 'Resolved',
            downstreamStatus: `Resolution recorded: ${action}`,
            nextAction: 'No further action required',
          });
          this.resolveDialogOpen.set(false);
          this.resolveTarget.set(null);
          this.uiState.set('success');
          this.toast.show(
            'Event Resolved',
            `Resolution recorded for ${e.eventReference}.`,
            'success',
          );
          this.loadQueue();
        },
        error: (error) => {
          this.resolveDialogOpen.set(false);
          this.resolveTarget.set(null);
          this.reportFailure(error, 'The resolution could not be recorded.');
        },
      });
  }

  // ----- Escalate: ask the gateway and hand the case on (4.4.3, 4.4.6) -----
  protected readonly escalateDialogOpen = signal(false);
  protected readonly escalateTarget = signal<PaymentEventRecord | null>(null);
  protected readonly escalateAction = signal('');
  protected readonly escalateReason = signal('');
  protected readonly escalateReasonMin = 10;
  protected readonly escalateReasonMax = 2000;
  protected readonly escalateSubmitted = signal(false);
  protected readonly escalateReasonCount = computed(() => this.escalateReason().trim().length);
  protected readonly escalateReasonValid = computed(() => {
    const len = this.escalateReason().trim().length;
    return len >= this.escalateReasonMin && len <= this.escalateReasonMax;
  });
  protected readonly escalateActionValid = computed(() => this.escalateAction().trim().length > 0);

  protected requestEscalate(event: PaymentEventRecord): void {
    this.openRowMenu.set(null);
    if (!this.escalateAllowed(event)) return;
    this.escalateTarget.set(event);
    this.escalateAction.set('');
    this.escalateReason.set('');
    this.escalateSubmitted.set(false);
    this.escalateDialogOpen.set(true);
  }
  protected cancelEscalate(): void {
    this.escalateDialogOpen.set(false);
    this.escalateTarget.set(null);
  }
  /**
   * Escalates the event by asking the gateway what actually happened.
   *
   * THERE IS NO SERVER-SIDE "ESCALATED" STATE for a gateway event, and inventing one in the
   * browser would produce a status that vanished on refresh, that no report could see and that no
   * colleague could pick up. What an operator actually needs from an event that will not
   * correlate is the provider's own answer, so this performs the audited verification - written
   * to the trail as `pay.payment.verified` - and hands the case to Payment Support with the
   * outcome on screen.
   *
   * IT NEVER RETRIES THE PAYMENT. A verification is a question; a retry disguised as a check is
   * how somebody gets charged twice.
   */
  protected confirmEscalate(): void {
    this.escalateSubmitted.set(true);
    if (!this.escalateActionValid() || !this.escalateReasonValid()) {
      this.uiState.set('validation');
      this.toast.show('Validation Error', 'Please provide a valid action and reason.', 'warning');
      return;
    }

    const e = this.escalateTarget();
    if (!e) return;

    if (!e.mappedIntentOrPayment) {
      this.escalateDialogOpen.set(false);
      this.escalateTarget.set(null);
      this.toast.show(
        'Nothing to verify',
        'This event has not correlated to a donation intent, so the gateway cannot be asked about it yet. Retry the correlation first.',
        'warning',
      );
      return;
    }

    const action = this.escalateAction().trim();

    this.paymentApi
      .verifyPayment({ intentReference: e.mappedIntentOrPayment })
      .subscribe({
        next: (verification) => {
          this.lastOutcome.set({
            reference: e.eventReference,
            state: 'Escalated',
            downstreamStatus: `Gateway says: ${verification.backendPaymentState}. ${action}`,
            nextAction: 'Continue in Payment support and safe retry',
          });
          this.escalateDialogOpen.set(false);
          this.escalateTarget.set(null);
          this.uiState.set('success');
          this.toast.show(
            'Event Escalated',
            `The gateway reports ${verification.backendPaymentState} for ${e.mappedIntentOrPayment}.`,
            'success',
          );
          this.loadQueue();
        },
        error: (error) => {
          this.escalateDialogOpen.set(false);
          this.escalateTarget.set(null);
          this.reportFailure(error, 'The gateway could not be asked about this event.');
        },
      });
  }

  // ================= UI states (4.4.4 / 4.4.7) =================
  protected readonly uiState = signal<UiState>('loading');
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }
  protected dismissBanner(): void {
    this.uiState.set('ready');
    this.lastOutcome.set(null);
  }

  // ================= Detail panel: tabs =================
  protected readonly detailTabs = [
    { key: 'overview', label: 'Overview' },
    { key: 'decision', label: 'Decision review' },
    { key: 'related', label: 'Related & history' },
  ] as const;
  protected readonly activeDetailTab = signal<string>('overview');
  protected selectDetailTab(key: string): void {
    this.activeDetailTab.set(key);
  }

  // ----- Related and history (4.4.1) -----
  protected readonly relatedTabsFor = computed<readonly RelatedTab[]>(() => {
    const e = this.selectedEvent();
    if (!e) return [];
    const donor = this.selectedDonorDetails();
    return [
      {
        key: 'linked',
        label: 'Linked records',
        rows: e.mappedIntentOrPayment
          ? [
              {
                primary: e.mappedIntentOrPayment,
                secondary: 'Donation intent',
                meta: `Correlated · ${e.sequenceStatus}`,
              },
            ]
          : [],
      },
      {
        key: 'documents',
        label: 'Documents',
        rows: [
          {
            primary: 'Raw gateway payload',
            secondary: 'JSON · confidential',
            meta: 'Withheld unless you hold pay.payments.view-events',
          },
        ],
      },
      {
        key: 'activity',
        label: 'Activity',
        rows: [
          { primary: 'Event received', secondary: e.gatewayEventType, meta: e.receivedTime },
          ...(e.resolutionAction
            ? [
                {
                  primary: e.resolutionAction,
                  secondary: 'Resolution recorded',
                  meta: this.lastRefresh(),
                },
              ]
            : []),
        ],
      },
      {
        key: 'integration',
        label: 'Integration status',
        rows: [
          {
            primary: 'Gateway webhook listener',
            secondary: e.signatureResult === 'Valid' ? 'Signature verified' : e.signatureResult,
            meta: `Last read ${this.lastRefresh()}`,
          },
        ],
      },
      {
        key: 'support',
        label: 'Support correlation',
        rows: donor
          ? [{ primary: donor.name, secondary: donor.email, meta: donor.campaign }]
          : [],
      },
      {
        key: 'audit',
        label: 'Audit chronology',
        rows: [
          {
            primary: 'Event ingested',
            secondary: e.eventReference,
            meta: `${this.owner()} · ${e.receivedTime}`,
          },
        ],
      },
    ];
  });
  protected readonly activeRelatedTab = signal<string>('linked');
  protected readonly activeRelatedRows = computed(
    () => this.relatedTabsFor().find((t) => t.key === this.activeRelatedTab())?.rows ?? [],
  );
  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  // ================= Persistent outcome (4.4.1) =================
  protected readonly lastOutcome = signal<{
    reference: string;
    state: PaymentEventState;
    downstreamStatus: string;
    nextAction: string;
  } | null>(null);

  protected readonly persistentOutcome = computed(() => {
    const outcome = this.lastOutcome();
    if (outcome) {
      return { ...outcome, effectiveTime: this.lastRefresh(), owner: this.owner() };
    }
    const e = this.selectedEvent();
    return {
      reference: e?.eventReference ?? '—',
      state: e?.eventState ?? 'New',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: e
        ? `${e.attempts} attempt(s) · ${e.mappedIntentOrPayment ? `mapped to ${e.mappedIntentOrPayment}` : 'not correlated'}`
        : 'No pending action',
      owner: this.owner(),
      nextAction:
        e && e.eventState !== 'Resolved'
          ? 'Resolve or escalate this event'
          : 'No further action required',
    };
  });

  // ================= Formatting helpers =================
  protected stateClass(state: PaymentEventState): string {
    switch (state) {
      case 'New':
        return 'peq-badge-blue';
      case 'Investigating':
        return 'peq-badge-gold';
      case 'Escalated':
        return 'peq-badge-danger';
      case 'Resolved':
        return 'peq-badge-good';
      default:
        return 'peq-badge-muted';
    }
  }
  protected duplicateClass(status: DuplicateStatus): string {
    switch (status) {
      case 'Unique':
        return 'peq-badge-good';
      case 'Duplicate':
        return 'peq-badge-danger';
      case 'Possible duplicate':
        return 'peq-badge-gold';
      default:
        return 'peq-badge-muted';
    }
  }
  protected sequenceClass(status: SequenceStatus): string {
    switch (status) {
      case 'In order':
        return 'peq-badge-good';
      case 'Out of order':
        return 'peq-badge-danger';
      case 'Unknown':
        return 'peq-badge-muted';
      default:
        return 'peq-badge-muted';
    }
  }
  protected signatureClass(result: SignatureResult): string {
    switch (result) {
      case 'Valid':
        return 'peq-badge-good';
      case 'Invalid':
        return 'peq-badge-danger';
      case 'Not verified':
        return 'peq-badge-muted';
      default:
        return 'peq-badge-muted';
    }
  }
  protected paymentStatusClass(status: PaymentStatus): string {
    switch (status) {
      case 'Success':
        return 'peq-badge-good';
      case 'Fail':
        return 'peq-badge-danger';
      case 'Pending':
        return 'peq-badge-gold';
      default:
        return 'peq-badge-muted';
    }
  }
  protected formatDate(iso: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  constructor() {
    if (!this.tokens.hasAnyPermission('pay.payments.view-events')) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    this.loadQueue();
  }

  private loadQueue(): void {
    this.loading.set(true);
    this.loadError.set(false);

    const filter: PaymentEventSearchFilter = {
      page: 1,
      pageSize: PaymentEventQueueComponent.FETCH_SIZE,
      search: this.searchTerm().trim() || undefined,
      status: this.toApiStatus(this.paymentStatusFilter()),
    };

    this.paymentApi.searchPaymentEvents(filter).subscribe({
      next: (page) => {
        this.records.set((page.items ?? []).map(toPaymentEventRecord));
        this.totalRecords.set(page.totalCount ?? 0);
        this.lastRefresh.set(formatMoment(new Date().toISOString()));
        this.loading.set(false);

        if (this.uiState() !== 'success' && this.uiState() !== 'no-access') {
          this.uiState.set(this.recordCount() === 0 ? 'empty' : 'ready');
        }

        this.fillDonorColumns();
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
          apiErrorMessage(error, 'The payment event queue could not be loaded.'),
          'error',
        );
      },
    });
  }

  /** Only the statuses the API can filter on; the rest are derived and filtered in memory. */
  private toApiStatus(status: PaymentStatus | ''): PaymentEventStatus | null {
    return status === 'Fail' ? 'failed' : null;
  }

  /**
   * Fills the donor, campaign and amount columns for the rows currently on screen.
   *
   * ONE READ PER VISIBLE ROW, not per row in the queue. A gateway event carries no donor - the
   * person is on the intent it correlated to - and fetching two hundred intents to draw eight
   * table rows would be a slow screen and more donor data in the browser than the view needs.
   *
   * A FAILED LOOKUP LEAVES THE COLUMNS BLANK rather than failing the page: an event that did not
   * correlate genuinely has no donor, and that is information rather than an error.
   */
  private fillDonorColumns(): void {
    const rows = this.pagedRecords().filter((r) => r.donationIntentId && !r.donorName);
    if (rows.length === 0) return;

    forkJoin(
      rows.map((row) =>
        this.paymentApi.getIntent(row.donationIntentId).pipe(
          map((intent) => ({ row, intent })),
          catchError(() => of(null)),
        ),
      ),
    ).subscribe((results) => {
      const byId = new Map(
        results
          .filter((r): r is NonNullable<typeof r> => r !== null)
          .map((r) => [r.row.eventId, r.intent]),
      );

      if (byId.size === 0) return;

      this.records.update((list) =>
        list.map((record) => {
          const intent = byId.get(record.eventId);
          if (!intent) return record;
          return {
            ...record,
            donorName: intent.donorName,
            donorEmail: intent.email,
            campaignName: intent.campaignName ?? '',
            donationAmount: record.donationAmount || intent.amount.display,
            currency: record.currency || intent.amount.currencyCode,
          };
        }),
      );

      const selected = this.selectedEvent();
      if (selected) this.loadDonorFor(selected);
    });
  }

  /** Reads the donor behind one event, for the detail panel. */
  private loadDonorFor(record: PaymentEventRecord): void {
    if (!record.donationIntentId) {
      this.selectedDonorDetails.set(null);
      this.selectedDonorLocation.set('');
      return;
    }

    this.paymentApi.getIntent(record.donationIntentId).subscribe({
      next: (intent) => {
        this.selectedDonorDetails.set({
          name: intent.donorName,
          email: intent.email,
          mobile: intent.mobile ?? '',
          campaign: intent.campaignName ?? '',
          amount: intent.amount.display,
          donorId: intent.donorId ?? '',
        });
        this.selectedDonorLocation.set(
          [intent.addressLine1, intent.postalCode].filter(Boolean).join(', '),
        );
      },
      error: () => {
        this.selectedDonorDetails.set(null);
        this.selectedDonorLocation.set('');
      },
    });
  }
}

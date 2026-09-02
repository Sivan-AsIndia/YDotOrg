import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentEventListItem } from '../../../../Shared/models/payment.model';

/** The outcome the document names on a row: Fail or Pending. Success never reaches this queue. */
type QueueOutcome = 'Fail' | 'Pending' | 'Success';

/**
 * The states the template can draw.
 *
 * The last four are reachable from the server's error envelope rather than from anything this
 * screen decides: a 400 is 'validation', a 409 is 'conflict', and a gateway that could not be
 * reached is 'dependency-failure'.
 */
type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'error'
  | 'success'
  | 'no-access'
  | 'validation'
  | 'duplicate'
  | 'conflict'
  | 'dependency-failure';

/**
 * One row of the queue, in the shape the template binds to.
 *
 * `eventReference` IS THE GATEWAY'S OWN EVENT ID. There is no separate EVT- column in the
 * database, and inventing one in the browser would produce a reference that means nothing to
 * anybody looking at the gateway dashboard - which is exactly who is looked at next when a
 * payment queue row needs explaining.
 */
interface PaymentQueueRow {
  readonly id: string;
  readonly eventReference: string;
  readonly mappedIntentOrPayment: string;
  readonly donationIntentId: string | null;
  readonly donorName: string;
  readonly donorEmail: string;
  readonly campaignName: string;
  readonly donationAmount: string;
  readonly currency: string;
  readonly paymentStatus: QueueOutcome;
  readonly receivedTime: string;
  readonly attempts: number;
  readonly version: number;
  readonly processingError: string | null;
}

/**
 * SCR-PAY-003 - Payment Queue. Section 4 of the YDot Donation Flow document.
 *
 * WHAT THE DOCUMENT SAYS THIS SCREEN IS. "Success -> the payment does NOT appear in the Payment
 * Event Queue. It goes straight to the Payment Receipt page. Fail -> appears with status Fail.
 * Pending -> appears with status Pending (this also happens if the donor cancels mid-way)." The
 * only actions it describes are the eye icon, which opens the detail panel, and Retry on a failed
 * row - and a retry that fails again sends the record to Payment Support and Safe Retry.
 *
 * WHAT THIS REPLACES, AND WHY IT MATTERED MOST HERE OF ANYWHERE. The screen opened Razorpay
 * Checkout directly from the browser with `key: 'rzp_test_TCwSZidEO9q88a'` compiled into the
 * bundle and `order_id: ''` - a comment beside it read "A real integration would create an order
 * server-side". Four things followed:
 *
 *   - THE KEY WAS PUBLIC, readable by anyone who opened dev-tools on the page.
 *   - THE AMOUNT CAME FROM THE ROW IN MEMORY. `Math.round(amount * 100)` was computed here, so
 *     what the donor was charged was decided by the browser.
 *   - NOTHING WAS VERIFIED. `handler` marked the event Success because a client-side callback
 *     said so - no signature check and no confirmation from the gateway.
 *   - RETRY COULD DOUBLE-CHARGE. It re-opened checkout against an attempt whose outcome nobody
 *     had asked about, which is precisely how a donor pays twice.
 *
 * IT NOW CALLS `POST /api/v1/payments/intents/{id}/safe-retry`, which VERIFIES WITH THE GATEWAY
 * BEFORE RETRYING and refuses when the original attempt actually succeeded. The four outcomes it
 * can answer - Retried, AlreadyPaid, StillPending, Refused - are reported to the operator as they
 * come back, because each one means something different to the donor on the phone.
 */
@Component({
  selector: 'app-payment-event-queue',
  imports: [CommonModule, FormsModule],
  templateUrl: './payment-event-queue.html',
  styleUrl: './payment-event-queue.css',
})
export class PaymentEventQueueComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly payments = inject(PaymentApiService);

  protected readonly pageTitle = 'Payment queue';
  protected readonly pageSubtitle =
    'Donations that did not complete. Fail and Pending only - a successful payment goes straight to its receipt.';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('');

  protected readonly uiState = signal<UiState>('loading');
  protected readonly errorMessage = signal('');

  // ===========================================================================================
  // Filters
  // ===========================================================================================

  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  protected readonly searchTerm = signal('');

  /** Only the two the document names. Success is not an option because it is never in the queue. */
  protected readonly paymentStatusOptions: readonly QueueOutcome[] = ['Pending', 'Fail'];
  protected readonly paymentStatusFilter = signal<QueueOutcome | ''>('');

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
    if (key === 'paymentStatus') {
      this.paymentStatusFilter.set('');
    }
    if (key === 'search') {
      this.searchTerm.set('');
    }
    this.currentPage.set(1);
    this.load();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.paymentStatusFilter.set('');
    this.currentPage.set(1);
    this.load();
  }

  protected applyFilters(): void {
    this.currentPage.set(1);
    this.load();
  }

  /**
   * Typing in the search box.
   *
   * DEBOUNCED, BECAUSE THE SERVER DOES THE SEARCHING NOW. The old screen filtered an array it
   * already held, so every keystroke was free; each keystroke is now a request, and firing one
   * per character would put eight requests in flight for "Priya" and render whichever happened
   * to come back last.
   */
  protected onSearchChange(value: string): void {
    this.searchTerm.set(value);
    this.currentPage.set(1);

    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 300);
  }
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  /** Changing the status filter. One deliberate choice, so it reloads at once. */
  protected onStatusChange(value: QueueOutcome | ''): void {
    this.paymentStatusFilter.set(value);
    this.currentPage.set(1);
    this.load();
  }

  // ===========================================================================================
  // Rows and paging
  // ===========================================================================================

  protected readonly records = signal<readonly PaymentQueueRow[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly pageSize = 8;
  protected readonly currentPage = signal(1);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalRecords() / this.pageSize)),
  );
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, i) => i + 1),
  );

  /**
   * THE SERVER PAGES, so this is the page rather than a slice of everything.
   *
   * The previous version fetched the lot and sliced in memory, which meant "Showing 1-8 of 8" was
   * true of the file it was reading and false of the organisation.
   */
  protected readonly pagedRecords = computed(() => this.records());
  protected readonly recordCount = computed(() => this.totalRecords());

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) {
      return;
    }
    this.currentPage.set(page);
    this.load();
  }

  constructor() {
    this.load();
  }

  private load(): void {
    this.uiState.set('loading');
    this.errorMessage.set('');

    this.payments
      .searchPaymentEvents({
        page: this.currentPage(),
        pageSize: this.pageSize,
        search: this.searchTerm().trim() || undefined,

        // NULL STILL EXCLUDES SUCCESS. The server treats an unset outcome as "Fail and Pending",
        // which is the queue the document describes.
        paymentOutcome: this.paymentStatusFilter() || null,
      })
      .subscribe({
        next: (page) => {
          this.records.set(page.items.map((item) => this.toRow(item)));
          this.totalRecords.set(page.totalCount);
          this.lastRefresh.set(this.nowLabel());
          this.uiState.set(page.items.length === 0 ? 'empty' : 'ready');
        },
        error: (error: unknown) => {
          this.errorMessage.set(apiErrorMessage(error));
          this.uiState.set('error');
          this.toast.show('Payment queue unavailable', this.errorMessage(), 'error');
        },
      });
  }

  private toRow(item: PaymentEventListItem): PaymentQueueRow {
    return {
      id: item.id,
      eventReference: item.gatewayEventId,
      mappedIntentOrPayment: item.intentReference ?? '—',
      donationIntentId: item.donationIntentId,
      donorName: item.donorName ?? '—',

      // ALREADY MASKED, OR ALREADY NOT. Whether this is the real address is the server's
      // decision, taken from pay.donations.view-sensitive-donor.
      donorEmail: item.donorEmail ?? '—',
      campaignName: item.campaignName ?? '—',
      donationAmount: item.amount ? this.formatAmount(item.amount.amount) : '—',
      currency: item.amount?.currencyCode ?? '',
      paymentStatus: item.paymentOutcome,
      receivedTime: this.formatDateTime(item.receivedAtUtc),
      attempts: item.processingAttempts,
      version: item.version,
      processingError: item.processingError,
    };
  }

  // ===========================================================================================
  // Detail panel - the document's eye icon
  // ===========================================================================================

  protected readonly selectedRef = signal<string>('');
  protected readonly selectedEvent = computed(
    () => this.records().find((r) => r.eventReference === this.selectedRef()) ?? null,
  );

  protected inspect(ref: string): void {
    this.selectedRef.set(ref);
  }

  protected closeDetail(): void {
    this.selectedRef.set('');
  }

  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
  }

  // ===========================================================================================
  // Retry - the one write this screen owns
  // ===========================================================================================

  /** The row currently being retried, so its button can say so and refuse a second click. */
  protected readonly retryingRef = signal<string | null>(null);

  /**
   * Retry, as the document describes it, and safely.
   *
   * TWO CALLS, NOT ONE, AND THE FIRST IS THE IMPORTANT ONE. `safe-retry` needs the INTENT's
   * version - the row carries the event's - so the intent is read first. That read is also what
   * makes the concurrency check meaningful: retrying against a version somebody else has since
   * changed is refused rather than applied.
   */
  protected retryPaymentFromQueue(row: PaymentQueueRow): void {
    if (!row.donationIntentId) {
      this.toast.show(
        'Nothing to retry',
        'This event was never matched to a donation, so there is no payment to retry. Open Payment Support and Safe Retry to investigate it.',
        'warning',
      );
      return;
    }
    if (this.retryingRef() !== null) {
      return;
    }

    const intentId = row.donationIntentId;
    this.retryingRef.set(row.eventReference);

    this.payments
      .getIntent(intentId)
      .pipe(
        switchMap((intent) =>
          this.payments.safeRetry(intentId, {
            expectedVersion: intent.version,
            reason: `Retried from the payment queue for event ${row.eventReference}.`,
          }),
        ),
      )
      .subscribe({
        next: (result) => {
          this.retryingRef.set(null);
          this.reportRetryOutcome(row, result.outcome, result.message, result.paymentLinkUrl);
          this.load();
        },
        error: (error: unknown) => {
          this.retryingRef.set(null);

          // A REFUSED RETRY IS NOT A BUG. The server refuses when the previous attempt's outcome
          // is unknown, and the document's own answer to that is this screen's neighbour.
          this.toast.show('Retry not completed', apiErrorMessage(error), 'error');
        },
      });
  }

  /**
   * Says what actually happened, in the donor's terms.
   *
   * THE FOUR OUTCOMES MEAN FOUR DIFFERENT THINGS TO THE PERSON ON THE PHONE, so they are not
   * collapsed into "retried". AlreadyPaid in particular must never read as a failure: the donor
   * has been charged and is owed a receipt, not another payment link.
   */
  private reportRetryOutcome(
    row: PaymentQueueRow,
    outcome: string,
    message: string,
    paymentLinkUrl: string | null,
  ): void {
    switch (outcome) {
      case 'Retried':
        this.toast.show(
          'Retry started',
          paymentLinkUrl
            ? `A fresh payment link was issued for ${row.mappedIntentOrPayment}.`
            : message,
          'success',
        );
        break;

      case 'AlreadyPaid':
        this.toast.show(
          'Already paid',
          `${row.donorName} has already been charged for ${row.mappedIntentOrPayment}. No second payment was taken; the receipt is in the Receipt Register.`,
          'info',
        );
        break;

      case 'StillPending':
        this.toast.show(
          'Still pending',
          'The gateway has not settled this payment yet. Nothing was charged again - check back rather than retrying.',
          'warning',
        );
        break;

      default:
        // "If the retry also fails, the payment record moves to the Payment Support and Safe
        // Retry page for the admin to handle." The server has already moved it; this is how the
        // operator finds out where it went.
        this.toast.show(
          'Sent to Payment Support',
          message || 'This payment could not be retried safely. It is now on the Payment Support and Safe Retry page.',
          'warning',
        );
        break;
    }
  }

  /**
   * Continue payment, for a Pending row.
   *
   * IT HANDS OVER THE REFERENCE AND NOTHING ELSE. The old version copied donor name, e-mail,
   * amount and currency into a shared `DataService` field for the next screen to read, so the
   * figures on the donation form were whatever this screen chose to put there - and a refresh
   * lost them. The public form now loads the intent from the API using this reference.
   */
  protected continuePaymentFromQueue(row: PaymentQueueRow): void {
    if (row.mappedIntentOrPayment === '—') {
      this.toast.show('No donation to continue', 'This event was never matched to a donation.', 'warning');
      return;
    }
    this.router.navigate(['/app/donations/public-donation-initiation'], {
      queryParams: { intent: row.mappedIntentOrPayment },
    });
  }

  // ===========================================================================================
  // Presentation
  // ===========================================================================================

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

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set(this.records().length === 0 ? 'empty' : 'ready');
  }

  protected paymentStatusClass(status: QueueOutcome): string {
    switch (status) {
      case 'Success': return 'peq-badge-good';
      case 'Fail': return 'peq-badge-danger';
      case 'Pending': return 'peq-badge-gold';
      default: return 'peq-badge-muted';
    }
  }

  private formatAmount(amount: number): string {
    return amount.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  protected formatDate(iso: string): string {
    if (!iso) {
      return '—';
    }
    const parsed = new Date(iso);
    return Number.isNaN(parsed.getTime())
      ? iso
      : parsed.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private formatDateTime(iso: string): string {
    if (!iso) {
      return '—';
    }
    const parsed = new Date(iso);
    return Number.isNaN(parsed.getTime())
      ? iso
      : parsed.toLocaleString('en-GB', {
          day: '2-digit', month: 'short', year: 'numeric',
          hour: '2-digit', minute: '2-digit',
        });
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric',
      hour: '2-digit', minute: '2-digit',
    });
  }
}

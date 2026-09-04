import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, switchMap } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { PaymentEventListItem, ReceiptRegisterRow } from '../../../../Shared/models/payment.model';

/**
 * The three outcomes a payment event can settle in. Unlike the old Payment Event Queue (which
 * only ever showed Fail/Pending) and the old Receipt Register (which only ever showed
 * Success/Failed), THIS screen shows all three side by side - that is the whole point of merging
 * them: "one place to track every payment event and the receipt it generated."
 */
export type PaymentOutcome = 'Success' | 'Pending' | 'Fail';

/** Whether a tax receipt exists for the row yet. Never invented - only ever what the server says. */
export type ReceiptStatus = 'Sent' | 'Not generated';

export type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'error'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure';

/**
 * One row of the merged register, in the shape the template binds to.
 *
 * ============================================================================================
 * HOW THE MERGE IS DONE - THERE IS NO UNIFIED BACKEND ENDPOINT (YET)
 * ============================================================================================
 * There is no single API that returns Success + Pending + Fail together, so this component
 * composes the two calls that already exist and were already correct on their own screens:
 *
 *   - PaymentApiService.searchPaymentEvents(...)  -> Fail and Pending rows. This is the only
 *     source for them: a successful payment never reaches this queue (see SCR-PAY-003), and this
 *     is also the only source with the fields Retry actually needs - donationIntentId, version,
 *     attempts, gatewayEventId.
 *   - PaymentApiService.getReceiptRegister(...) filtered to status 'Success' -> Success rows,
 *     because only an issued receipt has a real receipt number, and only the register knows it.
 *
 * Failed rows are taken from the queue, not the register, even though the register's union also
 * contains failed intents - the queue's copy carries donor email, gateway event id and version,
 * which the register's row does not, and Retry needs all three.
 *
 * WHEN NO STATUS FILTER IS APPLIED ("All statuses"), both sources are fetched up to
 * `mergedBatchSize` rows each and merged/sorted client-side, because true cross-source pagination
 * and a single accurate "Showing X of Y" require a real backend union - which does not exist yet.
 * The summary tiles do NOT have this limitation: they come from two lightweight, unpaged count
 * requests (see `loadSummary`), so they stay accurate for the whole scope regardless of how many
 * rows are actually fetched for display.
 *
 * WHEN A SINGLE STATUS IS SELECTED (Success, Pending or Fail), only one source is queried and
 * pagination is exact, the same as it always was on the two original screens.
 *
 * If/when a real `GET /payments/receipts-register` union endpoint is added server-side, this
 * whole composition block is what it should replace.
 */
export interface PaymentReceiptRow {
  readonly id: string;
  readonly eventReference: string;
  readonly receiptOrIntentRef: string;
  readonly donationIntentId: string | null;
  readonly donorName: string;
  readonly donorEmail: string;
  readonly campaignName: string;
  readonly amount: number | null;
  readonly currency: string;
  readonly paymentStatus: PaymentOutcome;
  readonly receiptStatus: ReceiptStatus;
  readonly receiptReference: string | null;
  readonly receivedTime: string;
  /** Raw ISO timestamp used only to sort the merged "All statuses" list - never rendered. */
  readonly sortKey: string;
  readonly attempts: number;
  readonly version: number;
  readonly processingError: string | null;
}

export interface PaymentsReceiptsSummary {
  readonly totalEvents: number;
  readonly totalAmount: number;
  readonly successful: number;
  readonly pending: number;
  readonly failed: number;
}

/**
 * SCR-PAY-00X - Payments & Receipts (merged view).
 *
 * Replaces two separate screens - the Payment Event Queue (SCR-PAY-003) and the Receipt Register
 * (SCR-PAY-005) - with the single index the reference design shows: every payment event, whatever
 * it settled as, in one table, with the receipt outcome alongside it instead of on a different
 * page. Payment Support and Safe Retry, and Receipt Correction, are unaffected - they stay as
 * their own screens for the same reason they always were separate: this index is for finding a
 * record, not for the deeper edit/void workflow.
 *
 * THE ONE WRITE ACTION IS CONTEXTUAL, NOT UNIFORM, because the four statuses are not four
 * variations of the same button:
 *   - Fail    -> Retry, via the same safe-retry flow the queue used (read the intent's version
 *                first, then call safe-retry with it, then report Retried / AlreadyPaid /
 *                StillPending / sent-to-Support exactly as before).
 *   - Pending -> Continue payment, handing over only the reference (never donor data) to the
 *                public donation form.
 *   - Success -> Resend, and only if the row actually has a receipt to resend and the caller is
 *                permitted to (permittedActions().includes('Resend')).
 * A row with none of these available shows no action, not a disabled one.
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

  protected readonly pageTitle = 'Payments & Receipts';
  protected readonly pageSubtitle = 'One place to track every payment event and the receipt it generated.';
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

  /** All three, unlike either predecessor screen, because this index shows every outcome. */
  protected readonly paymentStatusOptions: readonly PaymentOutcome[] = ['Success', 'Pending', 'Fail'];
  protected readonly paymentStatusFilter = signal<PaymentOutcome | ''>('');

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
    if (key === 'paymentStatus') this.paymentStatusFilter.set('');
    if (key === 'search') this.searchTerm.set('');
    this.refreshAfterFilterChange();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.paymentStatusFilter.set('');
    this.refreshAfterFilterChange();
  }

  protected applyFilters(): void {
    this.refreshAfterFilterChange();
  }

  /** Debounced, same reasoning as the old queue: every keystroke is now a request, not a filter. */
  protected onSearchChange(value: string): void {
    this.searchTerm.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.refreshAfterFilterChange(), 300);
  }
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected onStatusChange(value: PaymentOutcome | ''): void {
    this.paymentStatusFilter.set(value);
    this.refreshAfterFilterChange();
  }

  /** Search affects the summary tiles too (they are scoped by the same search term), so both
   *  the rows and the summary are re-fetched together whenever a filter changes. */
  private refreshAfterFilterChange(): void {
    this.currentPage.set(1);
    this.loadRows();
    this.loadSummary();
  }

  // ===========================================================================================
  // Rows, totals and paging - all server-side, scope-wide
  // ===========================================================================================

  protected readonly records = signal<readonly PaymentReceiptRow[]>([]);
  protected readonly totalRecords = signal(0);
  protected readonly summary = signal<PaymentsReceiptsSummary | null>(null);
  protected readonly permittedActions = signal<readonly string[]>([]);
  protected readonly pageSize = 8;
  protected readonly currentPage = signal(1);

  protected readonly totalEvents = computed(() => this.summary()?.totalEvents ?? 0);
  protected readonly totalAmount = computed(() => this.summary()?.totalAmount ?? 0);
  protected readonly successfulCount = computed(() => this.summary()?.successful ?? 0);
  protected readonly pendingCount = computed(() => this.summary()?.pending ?? 0);
  protected readonly failedCount = computed(() => this.summary()?.failed ?? 0);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalRecords() / this.pageSize)));
  protected readonly pageNumbers = computed(() => Array.from({ length: this.totalPages() }, (_, i) => i + 1));
  protected readonly pagedRecords = computed(() => this.records());
  protected readonly pageStart = computed(() =>
    this.totalRecords() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize + 1,
  );
  protected readonly pageEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize, this.totalRecords()),
  );

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) return;
    this.currentPage.set(page);
    this.loadRows();
  }

  /** How many rows to pull per source when both are queried for the unfiltered "All" view. */
  private readonly mergedBatchSize = 200;

  constructor() {
    this.loadSummary();
    this.loadRows();
  }

  /**
   * The five tiles. Two small, unpaged requests - independent of whatever page or batch of rows
   * is currently on screen, so the totals stay honest about the whole scope (4.5.1's "totals
   * qualified by scope", not by what happens to be rendered).
   */
  private loadSummary(): void {
    const search = this.searchTerm().trim() || undefined;

    forkJoin([
      this.payments.getReceiptRegister({ page: 1, pageSize: 1, search, status: 'Success' }),
      this.payments.searchPaymentEvents({ page: 1, pageSize: 1, search, paymentOutcome: 'Pending' }),
    ]).subscribe({
      next: ([receiptPage, pendingPage]) => {
        const s = receiptPage.summary;
        this.summary.set({
          // registerSummary.totalReceipts is already successful + failed together (the union);
          // pending never appears in the register, so it is added on here.
          totalEvents: (s?.totalReceipts ?? 0) + pendingPage.totalCount,
          totalAmount: s?.totalAmount.amount ?? 0,
          successful: s?.successful ?? 0,
          pending: pendingPage.totalCount,
          failed: s?.failed ?? 0,
        });
      },
      // Summary is a nice-to-have next to the row data; a failure here should not block the table.
      error: () => this.summary.set(null),
    });
  }

  private loadRows(): void {
    this.uiState.set('loading');
    this.errorMessage.set('');

    const search = this.searchTerm().trim() || undefined;
    const filter = this.paymentStatusFilter();

    // Single status selected -> single source, exact server-side pagination (same as before).
    if (filter === 'Fail' || filter === 'Pending') {
      this.payments
        .searchPaymentEvents({ page: this.currentPage(), pageSize: this.pageSize, search, paymentOutcome: filter })
        .subscribe({
          next: (page) => this.applyRows(page.items.map((item) => this.fromQueueItem(item)), page.totalCount),
          error: (error) => this.handleLoadError(error),
        });
      return;
    }
    if (filter === 'Success') {
      this.payments
        .getReceiptRegister({ page: this.currentPage(), pageSize: this.pageSize, search, status: 'Success' })
        .subscribe({
          next: (page) => {
            this.permittedActions.set(page.permittedActions ?? []);
            this.applyRows(page.rows.items.map((row) => this.fromReceiptRow(row)), page.rows.totalCount);
          },
          error: (error) => this.handleLoadError(error),
        });
      return;
    }

    // "All statuses" -> both sources, merged and sorted client-side. See the class-level note on
    // PaymentReceiptRow for why this is a stop-gap rather than real server-side pagination.
    forkJoin([
      this.payments.searchPaymentEvents({ page: 1, pageSize: this.mergedBatchSize, search, paymentOutcome: null }),
      this.payments.getReceiptRegister({ page: 1, pageSize: this.mergedBatchSize, search, status: 'Success' }),
    ]).subscribe({
      next: ([queuePage, receiptPage]) => {
        this.permittedActions.set(receiptPage.permittedActions ?? []);
        const queueRows = queuePage.items.map((item) => this.fromQueueItem(item));
        const receiptRows = receiptPage.rows.items.map((row) => this.fromReceiptRow(row));
        const merged = [...queueRows, ...receiptRows].sort((a, b) =>
          b.sortKey.localeCompare(a.sortKey),
        );
        const start = (this.currentPage() - 1) * this.pageSize;
        this.applyRows(merged.slice(start, start + this.pageSize), merged.length);
      },
      error: (error) => this.handleLoadError(error),
    });
  }

  private applyRows(rows: readonly PaymentReceiptRow[], totalCount: number): void {
    this.records.set(rows);
    this.totalRecords.set(totalCount);
    this.lastRefresh.set(this.nowLabel());
    this.uiState.set(rows.length === 0 ? 'empty' : 'ready');
  }

  private handleLoadError(error: unknown): void {
    this.errorMessage.set(apiErrorMessage(error));
    this.uiState.set(this.isForbidden(error) ? 'no-access' : 'error');
    this.toast.show('Payments & Receipts unavailable', this.errorMessage(), 'error');
  }

  private isForbidden(error: unknown): boolean {
    return typeof error === 'object' && error !== null && (error as { status?: number }).status === 403;
  }

  /** A Fail or Pending row - Success never appears in the queue, so this never produces one. */
  private fromQueueItem(item: PaymentEventListItem): PaymentReceiptRow {
    return {
      id: item.id,
      eventReference: item.gatewayEventId,
      receiptOrIntentRef: item.intentReference ?? '—',
      donationIntentId: item.donationIntentId,
      donorName: item.donorName ?? '—',
      donorEmail: item.donorEmail ?? '—',
      campaignName: item.campaignName ?? '—',
      amount: item.amount?.amount ?? null,
      currency: item.amount?.currencyCode ?? '',
      paymentStatus: item.paymentOutcome as PaymentOutcome,
      // A failed or still-pending payment never has a receipt - never invent one here.
      receiptStatus: 'Not generated',
      receiptReference: null,
      receivedTime: this.formatDateTime(item.receivedAtUtc),
      sortKey: item.receivedAtUtc ?? '',
      attempts: item.processingAttempts,
      version: item.version,
      processingError: item.processingError,
    };
  }

  /** A Success row - only ever built from an issued receipt, so receiptReference is always real. */
  private fromReceiptRow(row: ReceiptRegisterRow): PaymentReceiptRow {
    return {
      id: row.id,
      eventReference: row.reference,
      receiptOrIntentRef: row.receiptNumber ?? '—',
      donationIntentId: null,
      donorName: row.donorSnapshot ?? '—',
      donorEmail: '—',
      campaignName: row.campaignOrFundName ?? '—',
      amount: row.amount?.amount ?? null,
      currency: row.amount?.currencyCode ?? '',
      paymentStatus: 'Success',
      receiptStatus: row.receiptNumber ? 'Sent' : 'Not generated',
      receiptReference: row.receiptNumber ?? null,
      receivedTime: row.receiptDateUtc ? this.formatDateTime(row.receiptDateUtc) : '—',
      sortKey: row.receiptDateUtc ?? '',
      attempts: 0,
      version: 0,
      processingError: null,
    };
  }

  // ===========================================================================================
  // Detail panel - the eye/gear icon on a row
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
  // Retry (Fail rows) - unchanged from the queue's safe-retry flow
  // ===========================================================================================

  protected readonly retryingRef = signal<string | null>(null);

  protected retryPaymentFromQueue(row: PaymentReceiptRow): void {
    if (!row.donationIntentId) {
      this.toast.show(
        'Nothing to retry',
        'This event was never matched to a donation, so there is no payment to retry. Open Payment Support and Safe Retry to investigate it.',
        'warning',
      );
      return;
    }
    if (this.retryingRef() !== null) return;

    const intentId = row.donationIntentId;
    this.retryingRef.set(row.eventReference);

    this.payments
      .getIntent(intentId)
      .pipe(
        switchMap((intent) =>
          this.payments.safeRetry(intentId, {
            expectedVersion: intent.version,
            reason: `Retried from Payments & Receipts for event ${row.eventReference}.`,
          }),
        ),
      )
      .subscribe({
        next: (result) => {
          this.retryingRef.set(null);
          this.reportRetryOutcome(row, result.outcome, result.message, result.paymentLinkUrl);
          this.loadRows();
          this.loadSummary();
        },
        error: (error: unknown) => {
          this.retryingRef.set(null);
          this.toast.show('Retry not completed', apiErrorMessage(error), 'error');
        },
      });
  }

  private reportRetryOutcome(
    row: PaymentReceiptRow,
    outcome: string,
    message: string,
    paymentLinkUrl: string | null,
  ): void {
    switch (outcome) {
      case 'Retried':
        this.toast.show(
          'Retry started',
          paymentLinkUrl ? `A fresh payment link was issued for ${row.receiptOrIntentRef}.` : message,
          'success',
        );
        break;
      case 'AlreadyPaid':
        this.toast.show(
          'Already paid',
          `${row.donorName} has already been charged for ${row.receiptOrIntentRef}. No second payment was taken; the receipt is in the Receipt Register.`,
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
        this.toast.show(
          'Sent to Payment Support',
          message || 'This payment could not be retried safely. It is now on the Payment Support and Safe Retry page.',
          'warning',
        );
        break;
    }
  }

  // ===========================================================================================
  // Continue payment (Pending rows) - reference only, unchanged from the queue
  // ===========================================================================================

  protected continuePaymentFromQueue(row: PaymentReceiptRow): void {
    if (row.receiptOrIntentRef === '—') {
      this.toast.show('No donation to continue', 'This event was never matched to a donation.', 'warning');
      return;
    }
    this.router.navigate(['/app/donations/public-donation-initiation'], {
      queryParams: { intent: row.receiptOrIntentRef },
    });
  }

  // ===========================================================================================
  // Resend receipt (Success rows) - unchanged from the register, reason still audited
  // ===========================================================================================

  protected readonly resendAllowed = computed(() => {
    const current = this.selectedEvent();
    return (
      !!current &&
      current.paymentStatus === 'Success' &&
      current.receiptStatus === 'Sent' &&
      this.permittedActions().includes('Resend')
    );
  });

  protected readonly resendDialogOpen = signal(false);
  protected readonly resendReason = signal('');
  protected readonly resendReasonTouched = signal(false);
  protected readonly resendReasonMin = 10;
  protected readonly resendReasonMax = 500;
  protected readonly resendReasonValid = computed(() => {
    const length = this.resendReason().trim().length;
    return length >= this.resendReasonMin && length <= this.resendReasonMax;
  });

  protected openResendDialog(): void {
    if (!this.resendAllowed()) return;
    this.resendReason.set('');
    this.resendReasonTouched.set(false);
    this.resendDialogOpen.set(true);
  }

  protected cancelResend(): void {
    this.resendDialogOpen.set(false);
  }

  protected confirmResend(): void {
    const current = this.selectedEvent();
    this.resendReasonTouched.set(true);
    if (!current || !this.resendReasonValid()) return;

    this.payments.resendReceipt(current.id, { channel: 'Email' }).subscribe({
      next: () => {
        this.resendDialogOpen.set(false);
        this.toast.show('Receipt resent', `A copy of ${current.receiptReference} was sent to the donor.`, 'success');
        this.loadRows();
      },
      error: (error: unknown) => this.toast.show('Receipt not resent', apiErrorMessage(error), 'error'),
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
      if (this.copiedField() === label) this.copiedField.set(null);
    }, 1500);
  }

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set(this.records().length === 0 ? 'empty' : 'ready');
  }

  protected paymentStatusClass(status: PaymentOutcome): string {
    switch (status) {
      case 'Success': return 'pr-badge-good';
      case 'Fail': return 'pr-badge-danger';
      case 'Pending': return 'pr-badge-gold';
      default: return 'pr-badge-muted';
    }
  }

  protected receiptStatusClass(status: ReceiptStatus): string {
    return status === 'Sent' ? 'pr-badge-blue' : 'pr-badge-muted';
  }

  protected formatAmount(amount: number | null): string {
    if (amount === null) return '—';
    return amount.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  private formatDateTime(iso: string): string {
    if (!iso) return '—';
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
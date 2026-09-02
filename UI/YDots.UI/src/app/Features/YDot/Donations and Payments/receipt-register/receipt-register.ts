import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { ReceiptRegisterRow, ReceiptRegisterSummary } from '../../../../Shared/models/payment.model';

/**
 * The document's own two words for the Status column.
 *
 * NOT THE RECEIPT'S LIFECYCLE. Draft, Submitted, Pending review, Issued, Correction and Voided
 * describe a tax document's progress through approval; the register's Status column answers a
 * different question - did the donor's payment succeed - and the workflow document's Fig 5 shows
 * exactly two values in it.
 */
export type IssueState = 'Success' | 'Failed';

export type DeliveryState = 'Not sent' | 'Pending' | 'Delivered' | 'Failed' | string;

export type UiState =
  | 'ready'
  | 'loading'
  | 'success'
  | 'no-access'
  | 'empty'
  | 'duplicate'
  | 'conflict'
  | 'dependency-failure';

/** One row, in the shape the template binds to. */
export interface ReceiptRecord {
  readonly key: string;
  readonly donationReference: string;
  readonly issueState: IssueState;
  readonly deliveryState: DeliveryState;
  readonly receiptReference: string;
  readonly donorSnapshot: string;
  readonly amount: number;
  readonly currency: string;
  readonly campaignOrFund: string;
  readonly issuedTime: string | null;
  readonly documentUrl: string | null;
}

/**
 * SCR-PAY-005 - Receipt Register. Section 6 of the YDot Donation Flow document.
 *
 * WHY THIS SCREEN NEEDED A NEW ENDPOINT. The document says "whether a payment ends in Success or
 * Fail, the result is recorded and shown on the Payment Receipt page", and its Fig 5 shows failed
 * rows sitting beside successful ones with a Status column reading Success or Failed.
 *
 * A TAX RECEIPT IS NEVER ISSUED FOR A FAILED PAYMENT, and that is not a detail. A numbered receipt
 * carrying a tax-exemption reference, against money that was refused, is a document a donor could
 * claim on and an auditor would treat as fraud. So the register is a UNION built server-side -
 * issued receipts supply the Success lines, failed donation intents supply the Failed ones - and a
 * failed row has no receipt number and no document to open, because there is genuinely neither.
 *
 * WHAT THIS REPLACES. The component held a `catalogue` signal filled from `DataService`, filtered
 * and paged it in memory, and computed the four header totals by counting that array. The totals
 * were therefore totals of the page, and "Total Amount" summed successes and failures together -
 * overstating what the charity had received, on the one number somebody might copy into a report.
 * Both now come from the server, counted over the whole scope.
 */
@Component({
  selector: 'app-receipt-register',
  imports: [CommonModule, FormsModule],
  templateUrl: './receipt-register.html',
  styleUrl: './receipt-register.css',
})
export class ReceiptRegisterComponent {
  private readonly toast = inject(ToastService);
  private readonly payments = inject(PaymentApiService);

  protected readonly owner = 'You · Finance';
  protected readonly lastRefresh = signal('');
  protected readonly operatingTimeZone = 'IST';

  /**
   * When a confirmation would take effect.
   *
   * IT IS "NOW", RENDERED, rather than a fixed string. The old value was the literal
   * '12 May 2025, 02:20 PM · IST' compiled into the component, so every confirmation dialog in
   * the application claimed the same afternoon in 2025 no matter when it was opened.
   */
  protected readonly effectiveTime = computed(() => this.lastRefresh() || this.nowLabel());
  protected readonly scope = signal('Your organisation · All campaigns');

  protected readonly uiState = signal<UiState>('loading');
  protected readonly loading = computed(() => this.uiState() === 'loading');
  protected readonly loadError = signal(false);
  protected readonly errorMessage = signal('');

  // ===========================================================================================
  // Rows, totals and paging - all server-side
  // ===========================================================================================

  protected readonly catalogue = signal<readonly ReceiptRecord[]>([]);
  protected readonly summary = signal<ReceiptRegisterSummary | null>(null);
  protected readonly permittedActions = signal<readonly string[]>([]);

  protected readonly totalCount = signal(0);
  protected readonly pageSize = 8;
  protected readonly currentPage = signal(1);

  /**
   * The four cards.
   *
   * SCOPE-WIDE, NOT PAGE-WIDE. Counting the eight rows on screen would report "Total Receipts 8"
   * for an organisation with four hundred.
   */
  protected readonly totalReceipts = computed(() => this.summary()?.totalReceipts ?? 0);
  protected readonly successfulCount = computed(() => this.summary()?.successful ?? 0);
  protected readonly failedCount = computed(() => this.summary()?.failed ?? 0);
  protected readonly totalAmount = computed(() => this.summary()?.totalAmount.amount ?? 0);
  protected readonly totalAmountCurrency = computed(() => this.summary()?.totalAmount.currencyCode ?? 'INR');

  /** The server pages, so the page IS the rows. */
  protected readonly pagedRecords = computed(() => this.catalogue());
  protected readonly filteredCatalogue = computed(() => ({ length: this.totalCount() }));

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize)),
  );
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, i) => i + 1),
  );
  protected readonly pageStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize + 1,
  );
  protected readonly pageEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize, this.totalCount()),
  );

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) {
      return;
    }
    this.currentPage.set(page);
    this.load();
  }

  // ===========================================================================================
  // Filters
  // ===========================================================================================

  protected readonly searchTerm = signal('');
  protected readonly issueStateFilter = signal<IssueState | ''>('');
  protected readonly deliveryStateFilter = signal<DeliveryState | ''>('');
  protected readonly issueStateCatalogue: readonly IssueState[] = ['Success', 'Failed'];
  protected readonly deliveryStateCatalogue: readonly DeliveryState[] = [
    'Not sent', 'Pending', 'Delivered', 'Failed',
  ];

  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.issueStateFilter()) {
      chips.push({ key: 'issueState', label: `Status: ${this.issueStateFilter()}` });
    }
    if (this.deliveryStateFilter()) {
      chips.push({ key: 'deliveryState', label: `Delivery: ${this.deliveryStateFilter()}` });
    }
    return chips;
  });

  protected removeFilterChip(key: string): void {
    if (key === 'search') this.searchTerm.set('');
    if (key === 'issueState') this.issueStateFilter.set('');
    if (key === 'deliveryState') this.deliveryStateFilter.set('');
    this.applyFilters();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.issueStateFilter.set('');
    this.deliveryStateFilter.set('');
    this.applyFilters();
  }

  protected applyFilters(): void {
    this.currentPage.set(1);
    this.load();
  }

  constructor() {
    this.load();
  }

  private load(): void {
    this.uiState.set('loading');
    this.loadError.set(false);
    this.errorMessage.set('');

    this.payments
      .getReceiptRegister({
        page: this.currentPage(),
        pageSize: this.pageSize,
        search: this.searchTerm().trim() || undefined,
        status: this.issueStateFilter() || null,
      })
      .subscribe({
        next: (response) => {
          this.catalogue.set(response.rows.items.map((row) => this.toRecord(row)));
          this.totalCount.set(response.rows.totalCount);
          this.summary.set(response.summary);
          this.permittedActions.set(response.permittedActions ?? []);
          this.lastRefresh.set(this.nowLabel());
          this.uiState.set(response.rows.items.length === 0 ? 'empty' : 'ready');
        },
        error: (error: unknown) => {
          this.loadError.set(true);
          this.errorMessage.set(apiErrorMessage(error));

          // A 403 IS NOT AN EMPTY REGISTER. Rendering a blank grid for somebody who simply lacks
          // pay.receipts.view tells them this charity has issued no receipts, which is false.
          this.uiState.set(this.isForbidden(error) ? 'no-access' : 'ready');
          this.toast.show('Receipt register unavailable', this.errorMessage(), 'error');
        },
      });
  }

  private isForbidden(error: unknown): boolean {
    return typeof error === 'object' && error !== null && (error as { status?: number }).status === 403;
  }

  private toRecord(row: ReceiptRegisterRow): ReceiptRecord {
    return {
      key: row.id,
      donationReference: row.reference,

      // AN EM-DASH, NOT A FABRICATED NUMBER. A failed payment has no receipt number, and inventing
      // one here would put a reference on screen that exists nowhere else.
      receiptReference: row.receiptNumber ?? '—',
      issueState: row.status,
      deliveryState: row.deliveryState,
      donorSnapshot: row.donorSnapshot,
      amount: row.amount.amount,
      currency: row.amount.currencyCode,
      campaignOrFund: row.campaignOrFundName ?? '—',
      issuedTime: row.receiptDateUtc ? this.formatDateTime(row.receiptDateUtc) : null,
      documentUrl: row.documentUrl,
    };
  }

  // ===========================================================================================
  // Selection and the detail panel
  // ===========================================================================================

  protected readonly selectedKey = signal('');
  protected readonly record = computed<ReceiptRecord | null>(
    () => this.catalogue().find((r) => r.key === this.selectedKey()) ?? null,
  );
  protected readonly detailOpen = signal(false);

  protected selectRecord(key: string): void {
    this.selectedKey.set(key);
    this.detailOpen.set(true);
  }

  protected closeDetailPanel(): void {
    this.selectedKey.set('');
    this.detailOpen.set(false);
  }

  protected readonly deliveryHistoryOpen = signal(false);
  protected toggleDeliveryHistory(): void {
    this.deliveryHistoryOpen.update((v) => !v);
  }

  // ===========================================================================================
  // Resend - the one write this screen keeps
  // ===========================================================================================

  protected readonly resendAllowed = computed(() => {
    const current = this.record();
    return (
      !!current &&
      current.issueState === 'Success' &&
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
    if (!this.resendAllowed()) {
      return;
    }
    this.resendReason.set('');
    this.resendReasonTouched.set(false);
    this.resendDialogOpen.set(true);
  }

  protected cancelResend(): void {
    this.resendDialogOpen.set(false);
  }

  /**
   * Sends the donor another copy.
   *
   * ONLY FOR A SUCCESSFUL ROW, enforced above by `resendAllowed`. There is no document to resend
   * for a failed payment, and the endpoint would refuse it - but a button that cannot work should
   * not be offered in the first place.
   */
  protected confirmResend(): void {
    const current = this.record();
    this.resendReasonTouched.set(true);

    if (!current || !this.resendReasonValid()) {
      return;
    }

    // THE REASON IS AUDITED, NOT ROUTED. `ResendReceiptRequest` carries the channel and an
    // optional destination override; the typed reason is what the audit trail records against
    // the resend, which is why the dialog insists on one.
    this.payments
      .resendReceipt(current.key, { channel: 'Email' })
      .subscribe({
      next: () => {
        this.resendDialogOpen.set(false);
        this.toast.show(
          'Receipt resent',
          `A copy of ${current.receiptReference} was sent to the donor.`,
          'success',
        );
        this.load();
      },
      error: (error: unknown) =>
        this.toast.show('Receipt not resent', apiErrorMessage(error), 'error'),
    });
  }

  // ===========================================================================================
  // Presentation
  // ===========================================================================================

  protected readonly copiedField = signal<string | null>(null);
  protected copyToClipboard(label: string, value: string): void {
    navigator.clipboard?.writeText(value).catch(() => undefined);
    this.copiedField.set(label);
    setTimeout(() => {
      if (this.copiedField() === label) {
        this.copiedField.set(null);
      }
    }, 1500);
  }

  protected issueStateClass(state: IssueState): string {
    return state === 'Success' ? 'rr-badge-good' : 'rr-badge-danger';
  }

  protected deliveryStateClass(state: DeliveryState): string {
    switch (state) {
      case 'Delivered': return 'rr-badge-good';
      case 'Failed': return 'rr-badge-danger';
      case 'Pending': return 'rr-badge-gold';
      default: return 'rr-badge-muted';
    }
  }

  protected formatAmount(amount: number, currency: string): string {
    const symbol = currency === 'INR' ? '₹' : '';
    return `${symbol}${amount.toLocaleString('en-IN', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    })}`;
  }

  private formatDateTime(iso: string): string {
    const parsed = new Date(iso);
    return Number.isNaN(parsed.getTime())
      ? iso
      : `${parsed.toLocaleString('en-GB', {
          day: '2-digit', month: 'short', year: 'numeric',
          hour: '2-digit', minute: '2-digit',
        })} · IST`;
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric',
      hour: '2-digit', minute: '2-digit',
    });
  }
}

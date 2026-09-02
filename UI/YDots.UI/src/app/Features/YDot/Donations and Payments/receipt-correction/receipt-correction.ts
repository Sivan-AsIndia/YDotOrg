import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  ReceiptDetail,
  ReceiptListItem,
  ReceiptStatus,
} from '../../../../Shared/models/payment.model';

export type UiState = 'ready' | 'loading' | 'no-access' | 'empty';

/**
 * One row of the correction work list, in the shape the template binds to.
 *
 * IT CARRIES `version` AS WELL AS `versionNumber`, and they are not the same number. `version`
 * is the optimistic-concurrency token the correct and void endpoints demand; `versionNumber` is
 * how many times this receipt has been reissued, which is what a person reads. Conflating them
 * is how a screen sends "2" to mean a row it read at revision 7.
 */
export interface CorrectionRecord {
  readonly key: string;
  readonly receiptNumber: string;
  readonly hasReceiptNumber: boolean;
  readonly donationReference: string;
  readonly donorSnapshot: string;
  readonly amount: number;
  readonly currency: string;
  readonly campaignOrFund: string;
  readonly issuedTime: string | null;
  readonly financialYear: string;
  readonly status: ReceiptStatus;
  readonly statusLabel: string;
  readonly versionNumber: number;
  readonly version: number;
  readonly supersedesReceiptId: string | null;
  readonly documentUrl: string | null;
}

/**
 * SCR-PAY-005c - Receipt Correction. Section 7 of the YDot Donation Flow document, and the
 * fourth item under Donations & Payments in every one of the document's screenshots.
 *
 * WHY IT IS A SEPARATE SCREEN FROM THE REGISTER. They answer different questions and they list
 * different things. The register reports WHAT HAPPENED TO EVERY PAYMENT - so it deliberately
 * includes failed ones, which have no receipt at all - and its single write is Resend. This
 * screen lists ONLY DOCUMENTS THAT EXIST AND CAN STILL BE CHANGED, because everything on it is
 * an amendment to a tax document. Merging the two would put a Void button on a row representing
 * a payment that never succeeded.
 *
 * A CORRECTION IS A NEW VERSION, NEVER AN EDIT, and the whole shape of this screen follows from
 * that. The original receipt stays exactly as issued, because a donor who claimed tax relief on
 * version 1 must still be able to produce version 1 if an assessor asks. So the correct action
 * does not "save changes" - it supersedes, and the superseded row stays visible with its status
 * reading Corrected.
 *
 * WHAT IS DELIBERATELY NOT HERE: any way to change the AMOUNT. `CorrectReceiptRequest` accepts
 * a donor name, a postal address and a tax identifier and nothing else, which is the correct
 * boundary - a receipt whose amount was wrong is a receipt that should be voided and the
 * donation re-examined, not one that should be quietly reissued for a different figure.
 *
 * EVERY WRITE IS VERSION-CHECKED. Two people working the correction queue at once would
 * otherwise both read revision 4, both submit, and the second would silently overwrite the
 * first's amendment - on a document somebody files with a tax authority.
 */
@Component({
  selector: 'app-receipt-correction',
  imports: [CommonModule, FormsModule],
  templateUrl: './receipt-correction.html',
  styleUrl: './receipt-correction.css',
})
export class ReceiptCorrectionComponent {
  private readonly toast = inject(ToastService);
  private readonly payments = inject(PaymentApiService);

  protected readonly uiState = signal<UiState>('loading');
  protected readonly loading = computed(() => this.uiState() === 'loading');
  protected readonly errorMessage = signal('');
  protected readonly lastRefresh = signal('');
  protected readonly effectiveTime = computed(() => this.lastRefresh() || this.nowLabel());

  // ===========================================================================================
  // The work list
  // ===========================================================================================

  protected readonly catalogue = signal<readonly CorrectionRecord[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly pageSize = 8;
  protected readonly currentPage = signal(1);

  protected readonly pagedRecords = computed(() => this.catalogue());
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

  /**
   * The three states a receipt document can be in on this screen.
   *
   * DRAFT, SUBMITTED AND PENDING REVIEW ARE ABSENT ON PURPOSE. A receipt that has not been
   * issued has never reached a donor, so there is nothing to correct and nothing to void - it
   * is still being written. This screen begins at Issued.
   */
  protected readonly issueStateCatalogue: readonly { value: ReceiptStatus; label: string }[] = [
    { value: 'issued', label: 'Issued' },
    { value: 'corrected', label: 'Corrected' },
    { value: 'voided', label: 'Voided' },
  ];

  protected readonly searchTerm = signal('');
  protected readonly issueStateFilter = signal<ReceiptStatus | ''>('issued');
  protected readonly financialYearFilter = signal('');

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
      const match = this.issueStateCatalogue.find((s) => s.value === this.issueStateFilter());
      chips.push({ key: 'issueState', label: `State: ${match?.label ?? this.issueStateFilter()}` });
    }
    if (this.financialYearFilter().trim()) {
      chips.push({ key: 'financialYear', label: `FY: ${this.financialYearFilter().trim()}` });
    }
    return chips;
  });

  protected removeFilterChip(key: string): void {
    if (key === 'search') this.searchTerm.set('');
    if (key === 'issueState') this.issueStateFilter.set('');
    if (key === 'financialYear') this.financialYearFilter.set('');
    this.applyFilters();
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.issueStateFilter.set('');
    this.financialYearFilter.set('');
    this.applyFilters();
  }

  protected applyFilters(): void {
    this.currentPage.set(1);
    this.load();
  }

  constructor() {
    this.load();
  }

  // ===========================================================================================
  // Loading
  // ===========================================================================================

  private load(): void {
    this.uiState.set('loading');
    this.errorMessage.set('');
    this.closeDetailPanel();

    this.payments
      .searchReceipts({
        page: this.currentPage(),
        pageSize: this.pageSize,
        search: this.searchTerm().trim() || undefined,
        issueState: this.issueStateFilter() || null,
        financialYear: this.financialYearFilter().trim() || null,
      })
      .subscribe({
        next: (response) => {
          this.catalogue.set(response.items.map((row) => this.toRecord(row)));
          this.totalCount.set(response.totalCount);
          this.lastRefresh.set(this.nowLabel());
          this.uiState.set(response.items.length === 0 ? 'empty' : 'ready');
        },
        error: (error: unknown) => {
          this.errorMessage.set(apiErrorMessage(error));

          // A 403 IS NOT AN EMPTY QUEUE. Showing a blank list to somebody who simply lacks
          // pay.receipts.view tells them this charity has issued no receipts, which is false.
          this.uiState.set(this.isForbidden(error) ? 'no-access' : 'empty');
          this.toast.show('Receipts unavailable', this.errorMessage(), 'error');
        },
      });
  }

  private isForbidden(error: unknown): boolean {
    return (
      typeof error === 'object' && error !== null && (error as { status?: number }).status === 403
    );
  }

  private toRecord(row: ReceiptListItem): CorrectionRecord {
    return {
      key: row.id,

      // AN EM-DASH, NOT A FABRICATED NUMBER. A receipt that carries no number has none, and
      // inventing one here would put a reference on screen that exists nowhere else.
      receiptNumber: row.receiptNumber ?? '—',
      hasReceiptNumber: !!row.receiptNumber,
      donationReference: row.donationReference,
      donorSnapshot: row.donorSnapshot,
      amount: row.amount.amount,
      currency: row.amount.currencyCode,
      campaignOrFund: row.campaignOrFundName ?? '—',
      issuedTime: row.issuedAtUtc ? this.formatDateTime(row.issuedAtUtc) : null,
      financialYear: row.financialYear,
      status: row.issueState,
      statusLabel: row.issueStateDescription || this.statusLabel(row.issueState),
      versionNumber: row.versionNumber,
      version: row.version,
      supersedesReceiptId: row.supersedesReceiptId,
      documentUrl: row.documentUrl,
    };
  }

  // ===========================================================================================
  // Selection and the detail panel
  // ===========================================================================================

  protected readonly selectedKey = signal('');
  protected readonly detailOpen = signal(false);
  protected readonly detail = signal<ReceiptDetail | null>(null);
  protected readonly detailLoading = signal(false);

  protected readonly record = computed<CorrectionRecord | null>(
    () => this.catalogue().find((r) => r.key === this.selectedKey()) ?? null,
  );

  /**
   * Opens the panel and fetches the FULL record.
   *
   * THE LIST ROW IS NOT ENOUGH, and this is not a detail worth economising on. Correcting a
   * receipt means editing the donor name, postal address and tax identifier PRINTED ON IT -
   * three fields the list projection does not carry. Pre-filling the form from anything less
   * would leave a blank address box beside a receipt that has an address, and a person who
   * submits that form has silently erased it.
   */
  protected selectRecord(key: string): void {
    this.selectedKey.set(key);
    this.detailOpen.set(true);
    this.detail.set(null);
    this.detailLoading.set(true);

    this.payments.getReceipt(key).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.detailLoading.set(false);
      },
      error: (error: unknown) => {
        this.detailLoading.set(false);
        this.toast.show('Receipt unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  protected closeDetailPanel(): void {
    this.selectedKey.set('');
    this.detailOpen.set(false);
    this.detail.set(null);
    this.correctDialogOpen.set(false);
    this.voidDialogOpen.set(false);
    this.resendDialogOpen.set(false);
  }

  /**
   * What the server says this caller may do with this receipt.
   *
   * THE SERVER'S LIST, NOT A ROLE CHECK IN THE BROWSER. `permittedActions` is computed against
   * the caller's permissions AND the record's state, so it already knows that a voided receipt
   * cannot be corrected and that an APPROVER may not run a destructive operation. Re-deriving
   * either rule here would give two sources of truth that drift.
   */
  private permits(action: string): boolean {
    return this.detail()?.permittedActions?.includes(action) ?? false;
  }

  // NO STATE CONDITION IS REPEATED HERE. `PermittedActionsFor` returns early on anything that is
  // not Issued, so a superseded or voided receipt already comes back with View and Export and
  // nothing else. Adding `status === 'issued'` beside each of these would look like belt and
  // braces and behave like a second rule to keep in step with the first.
  protected readonly correctAllowed = computed(() => this.permits('Correct'));
  protected readonly voidAllowed = computed(() => this.permits('Void'));
  protected readonly resendAllowed = computed(() => this.permits('Resend'));
  protected readonly noActionsAvailable = computed(
    () =>
      !this.detailLoading() &&
      !!this.detail() &&
      !this.correctAllowed() &&
      !this.voidAllowed() &&
      !this.resendAllowed(),
  );

  // ===========================================================================================
  // Correct - the reissue
  // ===========================================================================================

  protected readonly correctDialogOpen = signal(false);
  protected readonly correctDonorName = signal('');
  protected readonly correctDonorAddress = signal('');
  protected readonly correctTaxIdentifier = signal('');
  protected readonly correctReason = signal('');
  protected readonly correctDeliver = signal(true);
  protected readonly correctTouched = signal(false);
  protected readonly reasonMin = 10;
  protected readonly reasonMax = 500;

  protected readonly correctReasonValid = computed(() => {
    const length = this.correctReason().trim().length;
    return length >= this.reasonMin && length <= this.reasonMax;
  });

  /**
   * PAN, when one is typed. The same expression the initiation form uses.
   *
   * OPTIONAL, BUT VALIDATED WHEN PRESENT. A blank tax identifier is a legitimate correction -
   * removing one that was entered by mistake - so emptiness is not an error; a malformed one is,
   * because it prints on a document a donor gives to an assessor.
   */
  protected readonly correctTaxIdentifierInvalid = computed(() => {
    const value = this.correctTaxIdentifier().trim().toUpperCase();
    return value.length > 0 && !/^[A-Z]{5}[0-9]{4}[A-Z]$/.test(value);
  });

  /**
   * Whether anything is actually being changed.
   *
   * A CORRECTION THAT CHANGES NOTHING IS NOT A CORRECTION. Without this the screen would happily
   * burn a receipt number, supersede the original and e-mail the donor a second identical
   * document - all recorded in the audit trail as an amendment, which is a lie about what
   * happened.
   */
  protected readonly correctHasChanges = computed(() => {
    const current = this.detail();
    if (!current) {
      return false;
    }
    return (
      this.correctDonorName().trim() !== (current.donorName ?? '').trim() ||
      this.correctDonorAddress().trim() !== (current.donorAddress ?? '').trim() ||
      this.correctTaxIdentifier().trim().toUpperCase() !==
        (current.donorTaxIdentifier ?? '').trim().toUpperCase()
    );
  });

  protected readonly correctSubmittable = computed(
    () =>
      this.correctReasonValid() &&
      this.correctHasChanges() &&
      !this.correctTaxIdentifierInvalid() &&
      this.correctDonorName().trim().length > 0,
  );

  protected openCorrectDialog(): void {
    const current = this.detail();
    if (!current || !this.correctAllowed()) {
      return;
    }

    // PRE-FILLED FROM THE RECEIPT AS PRINTED, not left blank. A correction form that opens empty
    // invites somebody to fix the one wrong field and submit three blanks over the two that were
    // right.
    this.correctDonorName.set(current.donorName ?? '');
    this.correctDonorAddress.set(current.donorAddress ?? '');
    this.correctTaxIdentifier.set(current.donorTaxIdentifier ?? '');
    this.correctReason.set('');
    this.correctDeliver.set(true);
    this.correctTouched.set(false);
    this.correctDialogOpen.set(true);
  }

  protected cancelCorrect(): void {
    this.correctDialogOpen.set(false);
  }

  protected confirmCorrect(): void {
    const current = this.detail();
    this.correctTouched.set(true);

    if (!current || !this.correctSubmittable()) {
      return;
    }

    this.payments
      .correctReceipt(current.id, {
        // THE RECORD'S OWN VERSION, read when the panel opened. A stale one is refused with a
        // 409 rather than overwriting whatever changed in between.
        expectedVersion: current.version,
        correctionReason: this.correctReason().trim(),
        donorName: this.correctDonorName().trim(),
        donorAddress: this.correctDonorAddress().trim() || null,
        donorTaxIdentifier: this.correctTaxIdentifier().trim().toUpperCase() || null,
        deliverImmediately: this.correctDeliver(),
      })
      .subscribe({
        next: (reissued) => {
          this.correctDialogOpen.set(false);
          this.toast.show(
            'Receipt corrected',
            `${current.receiptNumber ?? current.donationReference} was superseded by ` +
              `${reissued.receiptNumber ?? 'a new version'}` +
              (this.correctDeliver() ? ', and a copy was e-mailed to the donor.' : '.'),
            'success',
          );
          this.load();
        },
        error: (error: unknown) =>
          this.toast.show('Receipt not corrected', apiErrorMessage(error), 'error'),
      });
  }

  // ===========================================================================================
  // Void
  // ===========================================================================================

  protected readonly voidDialogOpen = signal(false);
  protected readonly voidReason = signal('');
  protected readonly voidTouched = signal(false);
  protected readonly voidReasonValid = computed(() => {
    const length = this.voidReason().trim().length;
    return length >= this.reasonMin && length <= this.reasonMax;
  });

  protected openVoidDialog(): void {
    if (!this.voidAllowed()) {
      return;
    }
    this.voidReason.set('');
    this.voidTouched.set(false);
    this.voidDialogOpen.set(true);
  }

  protected cancelVoid(): void {
    this.voidDialogOpen.set(false);
  }

  protected confirmVoid(): void {
    const current = this.detail();
    this.voidTouched.set(true);

    if (!current || !this.voidReasonValid()) {
      return;
    }

    this.payments
      .voidReceipt(current.id, {
        expectedVersion: current.version,
        reason: this.voidReason().trim(),
      })
      .subscribe({
        next: () => {
          this.voidDialogOpen.set(false);
          this.toast.show(
            'Receipt voided',
            `${current.receiptNumber ?? current.donationReference} is void. The record remains, ` +
              'marked as voided, with the reason recorded against it.',
            'success',
          );
          this.load();
        },
        error: (error: unknown) =>
          this.toast.show('Receipt not voided', apiErrorMessage(error), 'error'),
      });
  }

  // ===========================================================================================
  // Resend
  // ===========================================================================================

  protected readonly resendDialogOpen = signal(false);
  protected readonly resendDestination = signal('');
  protected readonly resendDestinationInvalid = computed(() => {
    const value = this.resendDestination().trim();
    return value.length > 0 && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
  });

  protected openResendDialog(): void {
    if (!this.resendAllowed()) {
      return;
    }
    this.resendDestination.set('');
    this.resendDialogOpen.set(true);
  }

  protected cancelResend(): void {
    this.resendDialogOpen.set(false);
  }

  protected confirmResend(): void {
    const current = this.detail();
    if (!current || this.resendDestinationInvalid()) {
      return;
    }

    this.payments
      .resendReceipt(current.id, {
        channel: 'Email',

        // NULL MEANS "THE ADDRESS ON THE RECEIPT". An override is audited, which is why it is a
        // deliberate entry rather than a pre-filled box somebody edits by accident.
        destination: this.resendDestination().trim() || null,
      })
      .subscribe({
        next: () => {
          this.resendDialogOpen.set(false);
          this.toast.show(
            'Receipt resent',
            `A copy of ${current.receiptNumber ?? current.donationReference} was sent.`,
            'success',
          );
          this.selectRecord(current.id);
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

  protected statusLabel(status: ReceiptStatus): string {
    switch (status) {
      case 'issued': return 'Issued';
      case 'corrected': return 'Corrected';
      case 'voided': return 'Voided';
      case 'draft': return 'Draft';
      case 'submitted': return 'Submitted';
      case 'pendingReview': return 'Pending review';
      default: return status;
    }
  }

  protected statusClass(status: ReceiptStatus): string {
    switch (status) {
      case 'issued': return 'rc-badge-good';
      case 'voided': return 'rc-badge-danger';
      case 'corrected': return 'rc-badge-gold';
      default: return 'rc-badge-muted';
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

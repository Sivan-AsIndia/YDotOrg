import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  DonationListItem,
  DonationSearchFilter,
  DonationStatistics,
  DonationStatus,
  MoneyResponse,
  ReconciliationStatus,
  SettlementStatus,
} from '../../../../Shared/models/payment.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { ToastService } from '../../../../Shared/services/toast.service';

/** The states this screen can be in, each reachable and each rendered differently. */
type UiState = 'loading' | 'ready' | 'empty' | 'no-access' | 'error';

/**
 * The donation register: every gift the organisation has received.
 *
 * WHY IT EXISTS. The menu carried a Donation Register node pointing at a route with no component
 * behind it, so anybody with `pay.donations.view` who followed it reached a blank page. It is also
 * the screen the module most needs: the receipt register answers "what have we issued?", the event
 * queue answers "what did the gateway tell us?", and neither answers "what have we actually
 * received?".
 *
 * EVERY FIGURE IS THE SERVER'S. The totals come from the statistics endpoint rather than being
 * summed over the loaded page - a page total presented as an organisation total is the most
 * plausible wrong number a screen like this can show, because it looks right until somebody
 * changes the page size.
 *
 * REFUNDS ARE NETTED, NOT LISTED SEPARATELY. A donation of 10,000 with 2,000 refunded is 8,000 to
 * this organisation, and a register that showed the gross figure against a target would overstate
 * income by exactly the amount that went back to donors.
 */
@Component({
  selector: 'app-donation-register',
  imports: [CommonModule, FormsModule],
  templateUrl: './donation-register.html',
  styleUrl: './donation-register.css',
})
export class DonationRegisterComponent {
  private readonly paymentApi = inject(PaymentApiService);
  private readonly toast = inject(ToastService);
  private readonly tokens = inject(AuthTokenService);

  // ================= Task header =================

  protected readonly pageTitle = 'Donation register';
  protected readonly pageSubtitle =
    'Every donation received, with its settlement, reconciliation and receipt state.';

  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** When the loaded set was actually read. Blank until the first response, never a fixed time. */
  protected readonly lastRefresh = signal('');

  // ================= State =================

  protected readonly uiState = signal<UiState>('loading');
  protected readonly rows = signal<readonly DonationListItem[]>([]);
  protected readonly statistics = signal<DonationStatistics | null>(null);
  protected readonly errorMessage = signal('');

  /** The server's total across the whole filter, not the loaded page's length. */
  protected readonly totalCount = signal(0);

  /**
   * What this caller may do.
   *
   * READ FROM THE TOKEN, and the export in particular is separate from the view: the register shows
   * donor names on screen, but an export puts them in a file that outlives the session.
   */
  protected readonly permissions = computed(() => ({
    view: this.tokens.hasAnyPermission('pay.donations.view'),
    export: this.tokens.hasAnyPermission('pay.donations.export'),
    reconcile: this.tokens.hasAnyPermission('pay.donations.reconcile'),
    viewSensitive: this.tokens.hasAnyPermission('pay.donations.view-sensitive-donor'),
  }));

  // ================= Filters =================

  protected readonly filtersOpen = signal(false);
  protected readonly search = signal('');
  protected readonly statusFilter = signal<DonationStatus | ''>('');
  protected readonly settlementFilter = signal<SettlementStatus | ''>('');
  protected readonly reconciliationFilter = signal<ReconciliationStatus | ''>('');
  protected readonly fromDate = signal('');
  protected readonly toDate = signal('');
  protected readonly awaitingReceiptOnly = signal(false);
  protected readonly openCasesOnly = signal(false);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);

  protected readonly statusOptions: readonly { value: DonationStatus; label: string }[] = [
    { value: 'recorded', label: 'Recorded' },
    { value: 'settled', label: 'Settled' },
    { value: 'partiallyRefunded', label: 'Partially refunded' },
    { value: 'refunded', label: 'Refunded' },
    { value: 'chargedBack', label: 'Charged back' },
    { value: 'voided', label: 'Voided' },
  ];

  protected readonly settlementOptions: readonly { value: SettlementStatus; label: string }[] = [
    { value: 'pending', label: 'Pending' },
    { value: 'settled', label: 'Settled' },

    // ON HOLD AND REVERSED ARE NOT THE SAME AS FAILED. Money on hold is still expected; money
    // reversed has gone back out of the account, and a finance officer needs to tell them apart.
    { value: 'onHold', label: 'On hold' },
    { value: 'reversed', label: 'Reversed' },
  ];

  protected readonly reconciliationOptions:
    readonly { value: ReconciliationStatus; label: string }[] = [
    { value: 'unreconciled', label: 'Unreconciled' },
    { value: 'matched', label: 'Matched to the bank' },
    { value: 'discrepancy', label: 'Discrepancy' },

    // A discrepancy somebody has settled by hand is a different state from one that matched
    // automatically: it is resolved, but a person decided it, and an auditor will want to know.
    { value: 'manuallyResolved', label: 'Manually resolved' },
  ];

  /** What the filters currently say, for the chip row above the table. */
  protected readonly activeFilterSummary = computed<readonly string[]>(() => {
    const chips: string[] = [];

    if (this.search().trim()) {
      chips.push(`Search: ${this.search().trim()}`);
    }

    if (this.statusFilter()) {
      chips.push(`Status: ${this.labelFor(this.statusOptions, this.statusFilter())}`);
    }

    if (this.settlementFilter()) {
      chips.push(`Settlement: ${this.labelFor(this.settlementOptions, this.settlementFilter())}`);
    }

    if (this.reconciliationFilter()) {
      chips.push(
        `Reconciliation: ${this.labelFor(this.reconciliationOptions, this.reconciliationFilter())}`,
      );
    }

    if (this.fromDate()) {
      chips.push(`From ${this.fromDate()}`);
    }

    if (this.toDate()) {
      chips.push(`To ${this.toDate()}`);
    }

    if (this.awaitingReceiptOnly()) {
      chips.push('Awaiting a receipt');
    }

    if (this.openCasesOnly()) {
      chips.push('With an open case');
    }

    return chips;
  });

  // ================= Selection =================

  protected readonly selectedId = signal('');

  protected readonly selected = computed<DonationListItem | null>(
    () => this.rows().find((row) => row.id === this.selectedId()) ?? null,
  );

  protected readonly detailOpen = signal(false);

  constructor() {
    if (!this.permissions().view) {
      this.uiState.set('no-access');
      return;
    }

    this.load();
  }

  // ================= Loading =================

  /**
   * Loads a page of donations and the organisation's totals.
   *
   * TWO REQUESTS, DELIBERATELY. The totals describe every donation in the filter, not the twenty-five
   * on screen, and computing them from the loaded rows would produce a number that shrank when
   * somebody turned the page.
   */
  protected load(): void {
    if (!this.permissions().view) {
      this.uiState.set('no-access');
      return;
    }

    this.uiState.set('loading');

    const filter: DonationSearchFilter = {
      page: this.page(),
      pageSize: this.pageSize(),
      search: this.search().trim() || undefined,
      status: this.statusFilter() || null,
      settlementStatus: this.settlementFilter() || null,
      reconciliationStatus: this.reconciliationFilter() || null,
      donatedFromUtc: this.fromDate() ? new Date(`${this.fromDate()}T00:00:00Z`).toISOString() : null,
      donatedToUtc: this.toDate() ? new Date(`${this.toDate()}T23:59:59Z`).toISOString() : null,
      awaitingReceipt: this.awaitingReceiptOnly() ? true : null,
      hasOpenCase: this.openCasesOnly() ? true : null,
    };

    this.paymentApi.searchDonations(filter).subscribe({
      next: (page) => {
        this.rows.set(page.items ?? []);
        this.totalCount.set(page.totalCount ?? 0);
        this.lastRefresh.set(this.nowLabel());
        this.uiState.set((page.items ?? []).length === 0 ? 'empty' : 'ready');
      },
      error: (error) => {
        this.rows.set([]);
        this.totalCount.set(0);
        this.errorMessage.set(apiErrorMessage(error, 'The donations could not be loaded.'));
        this.uiState.set('error');
      },
    });

    this.paymentApi.getDonationStatistics().subscribe({
      next: (statistics) => this.statistics.set(statistics),

      // The totals failing is not a reason to fail the page: the rows are what somebody came for,
      // and a missing tile is visibly missing.
      error: () => this.statistics.set(null),
    });
  }

  protected applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  protected resetFilters(): void {
    this.search.set('');
    this.statusFilter.set('');
    this.settlementFilter.set('');
    this.reconciliationFilter.set('');
    this.fromDate.set('');
    this.toDate.set('');
    this.awaitingReceiptOnly.set(false);
    this.openCasesOnly.set(false);
    this.applyFilters();
  }

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }

    this.page.set(page);
    this.load();
  }

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize())),
  );

  // ================= Row selection =================

  protected selectRow(row: DonationListItem): void {
    this.selectedId.set(row.id);
    this.detailOpen.set(true);
  }

  protected closeDetail(): void {
    this.selectedId.set('');
    this.detailOpen.set(false);
  }

  // ================= Actions =================

  /**
   * Marks a donation as reconciled against the bank.
   *
   * IT IS THE ONE WRITE ON THIS SCREEN, and it is a finance action rather than an operational one:
   * saying a donation matches the bank statement is a statement about money that has arrived. The
   * server refuses it on anything that is not settled, so a donation reconciled here is one whose
   * money is genuinely in the account.
   */
  protected reconcile(row: DonationListItem): void {
    if (!this.permissions().reconcile) {
      return;
    }

    this.paymentApi
      .reconcileDonation(row.id, {
        expectedVersion: row.version,

        // MATCHED, not "reconciled". The state means this donation was matched against the bank
        // statement, which is a stronger and more specific claim than "somebody looked at it".
        status: 'matched',
        note: 'Matched to the bank statement from the donation register.',
      })
      .subscribe({
        next: () => {
          this.toast.show(
            'Reconciled',
            `${row.donationReference} is now marked as reconciled.`,
            'success',
          );

          this.load();
        },
        error: (error) => {
          const code =
            typeof error === 'object' && error !== null && 'errorCode' in error
              ? (error as { errorCode?: string }).errorCode
              : undefined;

          if (code === 'CONCURRENCY_CONFLICT') {
            this.toast.show(
              'Donation changed',
              'Somebody else changed this donation. Refreshing.',
              'warning',
            );

            this.load();
            return;
          }

          this.toast.show(
            'Could not reconcile',
            apiErrorMessage(error, 'The donation could not be reconciled.'),
            'error',
          );
        },
      });
  }

  /**
   * Exports the filtered register.
   *
   * IT EXPORTS WHAT THE FILTER SAYS, not everything. An export that quietly widened the filter would
   * hand somebody more donor data than the screen in front of them showed - and this file carries
   * names and amounts together.
   */
  protected exportRegister(): void {
    if (!this.permissions().export) {
      return;
    }

    this.paymentApi
      .exportDonations({
        search: this.search().trim() || undefined,
        status: this.statusFilter() || null,
        settlementStatus: this.settlementFilter() || null,
        reconciliationStatus: this.reconciliationFilter() || null,
        donatedFromUtc: this.fromDate()
          ? new Date(`${this.fromDate()}T00:00:00Z`).toISOString()
          : null,
        donatedToUtc: this.toDate() ? new Date(`${this.toDate()}T23:59:59Z`).toISOString() : null,
        awaitingReceipt: this.awaitingReceiptOnly() ? true : null,
        hasOpenCase: this.openCasesOnly() ? true : null,
      })
      .subscribe({
        next: (download) => {
          this.paymentApi.saveBlob(download.blob, download.fileName);

          this.toast.show(
            'Export ready',
            `${download.fileName} has been downloaded.`,
            'success',
          );
        },
        error: (error) =>
          this.toast.show(
            'Export failed',
            apiErrorMessage(error, 'The register could not be exported.'),
            'error',
          ),
      });
  }

  // ================= Formatting =================

  protected money(value: MoneyResponse | null | undefined): string {
    if (!value) {
      return '—';
    }

    return `${value.amount.toLocaleString('en-IN', { minimumFractionDigits: 2 })} ${value.currencyCode}`;
  }

  protected when(value: string | null | undefined): string {
    if (!value) {
      return '—';
    }

    return new Date(value).toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /**
   * The colour a status is drawn in.
   *
   * CHARGED BACK IS AN ERROR TONE AND REFUNDED IS NOT. A refund is a normal, deliberate act; a
   * chargeback is a donor's bank reversing a payment against the organisation's wishes, and the two
   * needing different attention is the whole reason they are separate states.
   */
  protected statusTone(status: DonationStatus): 'good' | 'warn' | 'error' | 'neutral' {
    switch (status) {
      case 'settled':
        return 'good';
      case 'recorded':
        return 'neutral';
      case 'partiallyRefunded':
      case 'refunded':
        return 'warn';
      case 'chargedBack':
      case 'voided':
        return 'error';
      default:
        return 'neutral';
    }
  }

  protected settlementTone(status: SettlementStatus): 'good' | 'warn' | 'error' | 'neutral' {
    switch (status) {
      case 'settled':
        return 'good';
      case 'reversed':
        return 'error';
      default:
        return 'warn';
    }
  }

  protected reconciliationTone(status: ReconciliationStatus): 'good' | 'warn' | 'error' | 'neutral' {
    switch (status) {
      case 'matched':
      case 'manuallyResolved':
        return 'good';
      case 'discrepancy':
        return 'error';
      default:
        return 'warn';
    }
  }

  /**
   * Whether a donation can be reconciled from this row.
   *
   * DERIVED FROM THE ROW'S STATE, not from a server action list - the LIST endpoint does not carry
   * one, only the detail does, and fetching a detail per row to decide whether to draw a button
   * would be twenty-five requests to render one page.
   *
   * THE CONDITIONS MIRROR WHAT THE SERVER ENFORCES: only a settled donation can be matched to a
   * bank statement, and one already matched has nothing to do. If the server refuses anyway - a
   * stale version, a state that changed underneath - the failure is reported and the page reloads,
   * which is the same outcome an absent button would have produced more slowly.
   */
  protected canReconcile(row: DonationListItem): boolean {
    return (
      this.permissions().reconcile
      && row.settlementStatus === 'settled'
      && row.reconciliationStatus === 'unreconciled'
    );
  }

  private labelFor<T extends string>(
    options: readonly { value: T; label: string }[],
    value: T | '',
  ): string {
    return options.find((option) => option.value === value)?.label ?? String(value);
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }
}

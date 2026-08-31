import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { canPerform } from '../../../../Shared/models/payment.model';
import type {
  ReceiptDeliveryStatus,
  ReceiptListItem,
  ReceiptSearchFilter,
  ReceiptStatus,
} from '../../../../Shared/models/payment.model';
import { formatMoment, toReceiptRecord } from '../../../../Shared/models/payment-adapters';

import {
  UiState,
  IssueState,
  DeliveryState,
  ReceiptRegisterPermissions,
  ReceiptRecord,
  HistoryRow,
  PersistentOutcome,
} from '../../../../Shared/models/receipt-register.model';

/**
 * The receipt register - SCR-PAY-005.
 *
 * EVERY ROW COMES FROM THE API. The register used to read a bundled JSON file, which meant a
 * receipt issued a minute ago was invisible and a receipt voided by a colleague still looked
 * live. The screen now searches `/api/v1/receipts` with the filters the toolbar already
 * expresses, so what is drawn is what the organisation actually holds.
 *
 * THE FILTERS GO TO THE SERVER, not to a local `Array.filter`. A client-side filter over one
 * loaded page silently hides matching rows that happen to be on page two - the worst kind of
 * wrong, because the screen looks like it answered.
 *
 * BUTTONS COME FROM `permittedActions`, which the server computes from the record's state AND
 * the caller's permissions together. A local check can only see the first half, so it would draw
 * a Void button on a receipt this person may read but not void.
 */
@Component({
  selector: 'app-receipt-register',
  imports: [CommonModule, FormsModule],
  templateUrl: './receipt-register.html',
  styleUrl: './receipt-register.css',
})
export class ReceiptRegisterComponent {
  private readonly toast = inject(ToastService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly tokens = inject(AuthTokenService);

  /** How many rows one read pulls back. The toolbar pages over the loaded set. */
  private static readonly FETCH_SIZE = 200;

  // ================= Data scope (4.5 Data scope) =================
  protected readonly catalogue = signal<ReceiptRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly selectedKey = signal('');
  protected readonly record = computed<ReceiptRecord | null>(
    () => this.catalogue().find((r) => r.key === this.selectedKey()) ?? null,
  );
  /** Whether the Receipt Details panel is open. */
  protected readonly detailOpen = signal(false);
  /** Close the detail panel so the table takes full width. */
  protected closeDetailPanel(): void {
    this.selectedKey.set('');
    this.detailOpen.set(false);
  }

  /** Owner / freshness header meta (4.5.1 Task header). */
  protected readonly owner = computed(() => this.tokens.user()?.displayName ?? 'You');
  protected readonly lastRefresh = signal('');
  /**
   * When the screen last had an answer from the server.
   *
   * A GETTER rather than a signal because the template reads it as a plain property, and a signal
   * rendered without its parentheses interpolates the function itself.
   */
  protected get effectiveTime(): string {
    return this.lastRefresh();
  }
  protected readonly operatingTimeZone = 'IST';

  /**
   * What this caller may do with the OPEN receipt, decided by the server.
   *
   * The section permission gates the screen; `permittedActions` gates each button, because an
   * issued receipt and a voided one offer different actions to the very same person.
   */
  protected readonly permissions = computed<ReceiptRegisterPermissions>(() => {
    const actions = this.selectedActions();

    return {
      view: this.tokens.hasAnyPermission('pay.receipts.view'),
      generate: canPerform(actions, 'Issue') && this.tokens.hasAnyPermission('pay.receipts.issue'),
      resend: canPerform(actions, 'Resend') && this.tokens.hasAnyPermission('pay.receipts.resend'),
      voidReissueThroughApproval:
        (canPerform(actions, 'Void') && this.tokens.hasAnyPermission('pay.receipts.void')) ||
        (canPerform(actions, 'Correct') && this.tokens.hasAnyPermission('pay.receipts.correct')),
    };
  });

  /** The permitted-action list for whichever receipt is open, refreshed with the detail. */
  private readonly selectedActions = signal<readonly string[]>([]);

  /** The concurrency stamp of the open receipt, fetched with its permitted actions. */
  private readonly selectedVersion = signal(0);

  /** The donation the open receipt belongs to. Issuing works from the donation, not the draft. */
  private readonly selectedDonationId = signal('');

  // ================= Context and filters (4.5.1 Context and filters) =================
  protected readonly scope = computed(
    () => `${this.tokens.tenant()?.tenantName ?? 'Your organisation'} · All campaigns`,
  );
  protected readonly searchTerm = signal('');
  protected readonly issueStateFilter = signal<IssueState | ''>('');
  protected readonly deliveryStateFilter = signal<DeliveryState | ''>('');
  protected readonly savedFilter = signal('All receipts (Default)');
  protected readonly issueStateCatalogue: readonly IssueState[] = [
    'Draft',
    'Submitted',
    'Pending review',
    'Issued',
    'Correction',
    'Voided',
  ];
  protected readonly deliveryStateCatalogue: readonly DeliveryState[] = [
    'Not sent',
    'Pending',
    'Delivered',
    'Failed',
  ];

  protected readonly filtersOpen = signal(false);
  protected toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  /**
   * The loaded set, unfiltered.
   *
   * The server has already applied the filters, so this is a pass-through rather than a second
   * filter - keeping the name means the template and the pagination below are untouched.
   */
  protected readonly filteredCatalogue = computed(() => this.catalogue());

  // ---- Pagination ----
  protected readonly pageSize = 10;
  protected readonly currentPage = signal(1);
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredCatalogue().length / this.pageSize)),
  );
  protected readonly pagedRecords = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    return this.filteredCatalogue().slice(start, start + this.pageSize);
  });
  protected readonly pageStart = computed(() =>
    this.filteredCatalogue().length === 0 ? 0 : (this.currentPage() - 1) * this.pageSize + 1,
  );
  protected readonly pageEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize, this.filteredCatalogue().length),
  );
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, i) => i + 1),
  );
  protected goToPage(page: number): void {
    this.currentPage.set(Math.min(Math.max(1, page), this.totalPages()));
  }

  /** Loading and no-access override the plain empty-filter check (4.5.4). */
  protected readonly effectiveUiState = computed<UiState>(() => {
    if (this.uiState() === 'loading' || this.uiState() === 'no-access') return this.uiState();
    if (this.filteredCatalogue().length === 0) return 'empty';
    return this.uiState();
  });

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim())
      chips.push({ key: 'search', label: `Receipt number: ${this.searchTerm().trim()}` });
    if (this.issueStateFilter())
      chips.push({ key: 'issue', label: `Issue state: ${this.issueStateFilter()}` });
    if (this.deliveryStateFilter())
      chips.push({ key: 'delivery', label: `Delivery state: ${this.deliveryStateFilter()}` });
    return chips;
  });
  protected removeFilterChip(key: string): void {
    if (key === 'search') this.searchTerm.set('');
    else if (key === 'issue') this.issueStateFilter.set('');
    else if (key === 'delivery') this.deliveryStateFilter.set('');
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
    this.loadReceipts();
  }

  /**
   * Totals across the loaded scope.
   *
   * `totalReceipts` is the SERVER's count for the current filter, not the loaded array's length,
   * so it stays right when the filter matches more rows than one read returns.
   */
  protected readonly serverTotal = signal(0);
  protected readonly totalReceipts = computed(() => this.serverTotal());
  protected readonly totalAmount = computed(() =>
    this.catalogue()
      .filter((r) => r.receiptReference)
      .reduce((sum, r) => sum + r.amount, 0),
  );
  protected readonly successfulCount = computed(
    () =>
      this.catalogue().filter((r) => r.issueState === 'Issued' || r.issueState === 'Correction')
        .length,
  );
  /** A receipt that never reached the donor, or was voided: the ones needing a person. */
  protected readonly failedCount = computed(
    () =>
      this.catalogue().filter((r) => r.issueState === 'Voided' || r.deliveryState === 'Failed')
        .length,
  );
  protected readonly voidedCount = computed(
    () => this.catalogue().filter((r) => r.issueState === 'Voided').length,
  );

  /** Opening a row fetches its detail: that is where the version and the buttons come from. */
  protected selectRecord(key: string): void {
    const target = this.catalogue().find((r) => r.key === key);
    if (!target) return;
    if (!target.inScope) {
      this.uiState.set('no-access');
      return;
    }
    this.selectedKey.set(key);
    this.detailOpen.set(true);
    this.deliveryHistoryOpen.set(false);
    this.lastActionKind.set(null);
    this.selectedActions.set([]);
    this.selectedVersion.set(0);
    this.selectedDonationId.set('');

    if (this.uiState() !== 'no-access') this.uiState.set('loading');

    // THE DETAIL CALL IS WHAT DECIDES THE BUTTONS. The register's rows carry no permitted-action
    // list - it depends on the caller as well as the record, and computing it for two hundred
    // rows to draw four buttons would be wasteful. It is fetched when a row is opened.
    this.paymentApi.getReceipt(key).subscribe({
      next: (receipt) => {
        this.selectedActions.set(receipt.permittedActions);
        this.selectedVersion.set(receipt.version);
        this.selectedDonationId.set(receipt.donationId);
        if (this.uiState() === 'loading') this.uiState.set('ready');
      },
      error: (error) => {
        this.uiState.set('ready');
        this.toast.show('Error', apiErrorMessage(error, 'That receipt could not be opened.'), 'error');
      },
    });
  }
  protected returnToRegister(): void {
    this.uiState.set('ready');
  }

  // ================= Actions, eligibility and result (4.5.3) =================
  protected readonly generateAllowed = computed(
    () =>
      this.permissions().generate &&
      this.uiState() !== 'no-access' &&
      this.record()?.issueState === 'Draft',
  );
  protected readonly resendAllowed = computed(
    () =>
      this.permissions().resend &&
      this.uiState() !== 'no-access' &&
      !!this.record() &&
      ['Issued', 'Correction'].includes(this.record()!.issueState),
  );
  protected readonly voidReissueAllowed = computed(
    () =>
      this.permissions().voidReissueThroughApproval &&
      this.uiState() !== 'no-access' &&
      !!this.record() &&
      ['Issued', 'Correction'].includes(this.record()!.issueState),
  );

  protected readonly lastActionKind = signal<'generate' | 'resend' | 'void' | 'reissue' | null>(null);
  protected readonly deliveryHistoryOpen = signal(false);
  protected toggleDeliveryHistory(): void {
    this.deliveryHistoryOpen.update((v) => !v);
  }

  // ----- Generate: primary action on a draft (4.5.3) -----
  protected readonly generateDialogOpen = signal(false);
  protected openGenerateDialog(): void {
    if (!this.generateAllowed()) return;
    this.generateDialogOpen.set(true);
  }
  protected cancelGenerate(): void {
    this.generateDialogOpen.set(false);
  }
  /**
   * Issues the receipt.
   *
   * THE NUMBER IS ALLOCATED BY THE SERVER. A number built in the browser from the row count is
   * wrong three ways over: two operators produce the same one, the series gaps on every refresh,
   * and a tax authority reads a gap in a receipt series as a destroyed receipt.
   *
   * IT IS ISSUED AGAINST THE DONATION ID, not the donation reference. The reference is what a
   * person quotes; the route takes the identifier, and sending the wrong one is a 404 that looks
   * like a missing donation.
   */
  protected confirmGenerate(): void {
    const current = this.record();
    const donationId = this.selectedDonationId();
    if (!current || !donationId) return;

    this.generateDialogOpen.set(false);
    this.uiState.set('loading');

    this.paymentApi.issueReceipt(donationId, { deliverImmediately: true }).subscribe({
      next: (receipt) => {
        this.lastActionKind.set('generate');
        this.uiState.set('success');
        this.toast.show(
          'Receipt Generated',
          `Receipt ${receipt.receiptNumber ?? ''} has been issued.`,
          'success',
        );
        this.loadReceipts(receipt.id);
      },
      error: (error) => this.reportFailure(error, 'The receipt could not be issued.'),
    });
  }

  // ----- Duplicate state (4.5.4 Duplicate) -----
  protected readonly duplicateCandidate = signal<ReceiptRecord | null>(null);
  protected compareDuplicate(): void {
    const candidate = this.duplicateCandidate();
    if (candidate) this.selectedKey.set(candidate.key);
    this.uiState.set('ready');
  }
  protected cancelDuplicate(): void {
    this.uiState.set('ready');
  }

  // ----- Resend: workflow action, high-risk confirm (4.5.3, 4.5.6) -----
  protected readonly resendDialogOpen = signal(false);
  protected readonly resendChannelDate = signal('');
  protected readonly resendChannelTouched = signal(false);
  protected readonly resendReason = signal('');
  protected readonly resendReasonTouched = signal(false);
  protected readonly resendReasonMin = 10;
  protected readonly resendReasonMax = 2000;
  protected readonly resendReasonCount = computed(() => this.resendReason().trim().length);
  protected readonly resendReasonValid = computed(() => {
    const len = this.resendReason().trim().length;
    return len >= this.resendReasonMin && len <= this.resendReasonMax;
  });
  protected readonly resendChannelValid = computed(() => {
    const v = this.resendChannelDate();
    if (!v) return false;
    return !Number.isNaN(new Date(v).getTime());
  });
  protected readonly interpretedResendChannel = computed(() => {
    const v = this.resendChannelDate();
    if (!v || Number.isNaN(new Date(v).getTime())) return '';
    return (
      new Date(v).toLocaleString('en-IN', { dateStyle: 'medium', timeStyle: 'short' }) +
      ` · ${this.operatingTimeZone}`
    );
  });

  protected openResendDialog(): void {
    if (!this.resendAllowed()) return;
    this.resendChannelDate.set('');
    this.resendChannelTouched.set(false);
    this.resendReason.set('');
    this.resendReasonTouched.set(false);
    this.resendDialogOpen.set(true);
  }
  protected cancelResend(): void {
    this.resendDialogOpen.set(false);
  }
  protected confirmResend(): void {
    this.resendChannelTouched.set(true);
    this.resendReasonTouched.set(true);
    if (!this.resendChannelValid() || !this.resendReasonValid()) {
      this.toast.show('Validation Error', 'Please provide a valid channel and reason.', 'warning');
      return;
    }
    const current = this.record();
    if (!current) return;

    this.resendDialogOpen.set(false);
    this.uiState.set('loading');

    // NO DESTINATION OVERRIDE FROM THIS SCREEN. Omitting it sends to the address ON THE RECEIPT,
    // which is what "resend" means. Sending a donor's tax document somewhere else is a separate,
    // audited action and does not belong behind a button labelled Resend.
    this.paymentApi.resendReceipt(current.key, { channel: 'Email' }).subscribe({
      next: () => {
        this.lastActionKind.set('resend');
        this.uiState.set('success');
        this.toast.show(
          'Receipt Resent',
          `Receipt ${current.receiptReference ?? current.donationReference} has been re-sent.`,
          'success',
        );
        this.loadReceipts(current.key);
      },
      error: (error) => {
        // A DELIVERY FAILURE IS NOT AN ERROR STATE, it is a dependency state - the receipt is
        // still perfectly valid, the message simply did not go. The screen renders that
        // differently, and offers a retry rather than an apology.
        this.lastActionKind.set('resend');
        this.uiState.set('dependency-failure');
        this.toast.show(
          'Delivery failed',
          apiErrorMessage(
            error,
            'The receipt could not be delivered. It remains valid and can be re-sent.',
          ),
          'error',
        );
      },
    });
  }
  protected retryDependency(): void {
    const current = this.record();
    if (!current) return;
    this.uiState.set('ready');
    this.openResendDialog();
  }

  // ----- Void / reissue: primary decision (4.5.3) -----
  protected readonly voidDialogOpen = signal(false);
  protected readonly voidMode = signal<'void' | 'reissue'>('void');
  protected readonly correctionReason = signal('');
  protected readonly correctionReasonTouched = signal(false);
  protected readonly correctionReasonMin = 10;
  protected readonly correctionReasonMax = 2000;
  protected readonly correctionReasonCount = computed(() => this.correctionReason().trim().length);
  protected readonly correctionReasonValid = computed(() => {
    const len = this.correctionReason().trim().length;
    return len >= this.correctionReasonMin && len <= this.correctionReasonMax;
  });
  protected readonly proposedIssueState = computed<IssueState>(() =>
    this.voidMode() === 'void' ? 'Voided' : 'Correction',
  );

  protected openVoidDialog(): void {
    if (!this.voidReissueAllowed()) return;
    const current = this.record();
    if (current?.hasConflict) {
      this.uiState.set('conflict');
      return;
    }
    this.voidMode.set('void');
    this.correctionReason.set('');
    this.correctionReasonTouched.set(false);
    this.voidDialogOpen.set(true);
  }
  protected cancelVoid(): void {
    this.voidDialogOpen.set(false);
  }
  protected reviewLatestVersion(): void {
    this.uiState.set('ready');
    this.loadReceipts(this.selectedKey());
  }
  /**
   * Voids the receipt, or reissues a corrected version of it.
   *
   * A CORRECTION IS A NEW VERSION AND THE ORIGINAL SURVIVES. That is the API's rule and it is
   * not negotiable: a donor who claimed tax relief on version 1 must still be able to show what
   * version 1 said. Neither path edits the original in place.
   *
   * THE EXPECTED VERSION GOES WITH BOTH. Voiding a receipt somebody else has already corrected
   * would otherwise silently overwrite their correction.
   */
  protected confirmVoid(): void {
    this.correctionReasonTouched.set(true);

    if (!this.correctionReasonValid()) {
      this.toast.show('Validation Error', 'Please provide a valid reason.', 'warning');
      return;
    }

    const current = this.record();
    if (!current) return;

    const isReissue = this.voidMode() === 'reissue';
    const reason = this.correctionReason().trim();
    const expectedVersion = this.selectedVersion();

    this.voidDialogOpen.set(false);
    this.uiState.set('loading');

    if (isReissue) {
      this.paymentApi
        .correctReceipt(current.key, {
          expectedVersion,
          correctionReason: reason,
          deliverImmediately: true,
        })
        .subscribe({
          next: (corrected) => {
            this.lastActionKind.set('reissue');
            this.uiState.set('success');
            this.toast.show(
              'Receipt Reissued',
              `Receipt ${corrected.receiptNumber ?? ''} supersedes ${current.receiptReference ?? ''}.`,
              'success',
            );
            this.loadReceipts(corrected.id);
          },
          error: (error) => this.reportFailure(error, 'The receipt could not be corrected.'),
        });

      return;
    }

    this.paymentApi.voidReceipt(current.key, { expectedVersion, reason }).subscribe({
      next: () => {
        this.lastActionKind.set('void');
        this.uiState.set('success');
        this.toast.show(
          'Receipt Voided',
          `Receipt ${current.receiptReference ?? ''} has been voided. Its number is retained.`,
          'success',
        );
        this.loadReceipts(current.key);
      },
      error: (error) => this.reportFailure(error, 'The receipt could not be voided.'),
    });
  }

  /**
   * Reports a failed write.
   *
   * THREE OUTCOMES ARE DISTINGUISHED because the operator's next step differs for each: a
   * conflict means refresh and look again, a duplicate means the thing they wanted has already
   * happened, and anything else is a genuine failure.
   */
  private reportFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.uiState.set('conflict');
      this.toast.show('Record changed', apiErrorMessage(error, fallback), 'warning');
      return;
    }

    if (code === 'RECEIPT_ALREADY_ISSUED') {
      this.uiState.set('duplicate');
      this.toast.show('Duplicate', apiErrorMessage(error, fallback), 'warning');
      return;
    }

    this.uiState.set('ready');
    this.toast.show('Action failed', apiErrorMessage(error, fallback), 'error');
  }

  // ================= Related and history (4.5.1 Related and history) =================
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
  protected readonly linkedRecords = computed<readonly HistoryRow[]>(() => [
    {
      primary: this.record()?.donationReference ?? '—',
      secondary: 'Donation',
      meta: this.record()?.issueState ?? '—',
    },
    { primary: this.record()?.campaignOrFund || '—', secondary: 'Campaign or fund', meta: '' },
  ]);
  protected readonly documents = computed<readonly HistoryRow[]>(() => [
    {
      primary: this.record()?.receiptReference ?? 'Not yet issued',
      secondary: 'Receipt document',
      meta: 'Confidential · record scope',
    },
  ]);
  protected readonly activityRows = computed<readonly HistoryRow[]>(() =>
    (this.record()?.deliveryHistory ?? []).map((entry) => ({
      primary: `Delivery · ${entry.channel}`,
      secondary: entry.status,
      meta: entry.time,
    })),
  );
  protected readonly integrationRows = computed<readonly HistoryRow[]>(() => [
    {
      primary: 'Receipt delivery',
      secondary: this.record()?.deliveryState ?? '—',
      meta: this.lastRefresh(),
    },
  ]);
  protected readonly supportRows = computed<readonly HistoryRow[]>(() => [
    {
      primary: this.record()?.donationReference ?? '—',
      secondary: 'Support correlation reference',
      meta: this.record()?.issueState ?? '—',
    },
  ]);
  protected readonly auditRows = computed<readonly HistoryRow[]>(() => [
    {
      primary: 'Receipt opened',
      secondary: this.record()?.receiptReference ?? '—',
      meta: `${this.owner()} · ${this.lastRefresh()}`,
    },
  ]);

  // ================= UI states (4.5.4 / 4.5.7) =================
  protected readonly uiState = signal<UiState>('loading');

  constructor() {
    if (!this.tokens.hasAnyPermission('pay.receipts.view')) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    this.loadReceipts();
  }

  /** The API's status vocabulary, from the register's display vocabulary. */
  private toApiIssueState(state: IssueState | ''): ReceiptStatus | null {
    switch (state) {
      case 'Draft':
        return 'draft';
      case 'Submitted':
        return 'submitted';
      case 'Pending review':
        return 'pendingReview';
      case 'Issued':
        return 'issued';
      case 'Correction':
        return 'corrected';
      case 'Voided':
        return 'voided';
      default:
        return null;
    }
  }

  private toApiDeliveryState(state: DeliveryState | ''): ReceiptDeliveryStatus | null {
    switch (state) {
      case 'Not sent':
        return 'notSent';
      case 'Pending':
        return 'pending';
      case 'Delivered':
        return 'delivered';
      case 'Failed':
        return 'failed';
      default:
        return null;
    }
  }

  private loadReceipts(keepSelected?: string): void {
    this.loading.set(true);
    this.loadError.set(false);

    const filter: ReceiptSearchFilter = {
      page: 1,
      pageSize: ReceiptRegisterComponent.FETCH_SIZE,
      search: this.searchTerm().trim() || undefined,
      issueState: this.toApiIssueState(this.issueStateFilter()),
      deliveryState: this.toApiDeliveryState(this.deliveryStateFilter()),
    };

    this.paymentApi.searchReceipts(filter).subscribe({
      next: (page) => {
        const rows = (page.items ?? []).map((item: ReceiptListItem) => toReceiptRecord(item));
        this.catalogue.set(rows);
        this.serverTotal.set(page.totalCount ?? rows.length);
        this.lastRefresh.set(formatMoment(new Date().toISOString()));
        this.loading.set(false);

        // A refresh after a write keeps the operator on the record they were working on; a plain
        // load starts closed, because a register that opens a row for you is a register that
        // chose one.
        const stillThere = keepSelected && rows.some((r) => r.key === keepSelected);
        if (stillThere) {
          this.selectedKey.set(keepSelected!);
          this.detailOpen.set(true);
          this.refreshSelectedDetail(keepSelected!);
        } else if (!keepSelected) {
          this.selectedKey.set('');
          this.detailOpen.set(false);
        }

        if (this.uiState() !== 'success' && this.uiState() !== 'no-access') {
          this.uiState.set('ready');
        }
      },
      error: (error) => {
        this.loading.set(false);
        this.loadError.set(true);

        // A 403 is not a failure to be retried, it is an answer: this person may not read the
        // register. The screen says so rather than offering a Retry that will fail identically.
        if (typeof error === 'object' && error !== null && 'status' in error && (error as { status?: number }).status === 403) {
          this.uiState.set('no-access');
          return;
        }

        this.uiState.set('ready');
        this.toast.show('Error', apiErrorMessage(error, 'The receipt register could not be loaded.'), 'error');
      },
    });
  }

  /** Re-reads the open receipt's version and permitted actions after a write. */
  private refreshSelectedDetail(id: string): void {
    this.paymentApi.getReceipt(id).subscribe({
      next: (receipt) => {
        this.selectedActions.set(receipt.permittedActions);
        this.selectedVersion.set(receipt.version);
        this.selectedDonationId.set(receipt.donationId);
      },
      error: () => {
        this.selectedActions.set([]);
        this.selectedVersion.set(0);
      },
    });
  }

  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  // ================= Persistent outcome (4.5.1 Persistent outcome) =================
  protected readonly persistentOutcome = computed<PersistentOutcome>(() => {
    const r = this.record();
    const kind = this.lastActionKind();
    const success = this.uiState() === 'success';
    return {
      reference: r?.receiptReference ?? r?.donationReference ?? '—',
      state: r?.issueState ?? '—',
      effectiveTime: success ? this.effectiveTime : (r?.issuedTime ?? '—'),
      downstreamStatus: success
        ? kind === 'generate'
          ? 'Receipt delivered to donor · no pending dependency'
          : kind === 'resend'
            ? 'Delivery re-attempted · no pending dependency'
            : kind === 'reissue'
              ? 'Correction issued · original linked and preserved'
              : 'Void recorded · linked history preserved'
        : 'No pending action',
      owner: this.owner(),
      nextAction: success
        ? kind === 'generate'
          ? 'Resend if the donor requests another copy'
          : kind === 'resend'
            ? 'No further action required'
            : 'Review the linked receipt in Related and history'
        : 'Generate, resend or void/reissue when eligible',
    };
  });

  // ================= Formatting helpers =================
  protected formatAmount(value: number, currency: string): string {
    const symbol = currency === 'INR' ? '₹' : currency + ' ';
    return symbol + value.toLocaleString('en-IN');
  }
  /** Display label for the receipt status. */
  protected statusLabel(state: IssueState): string {
    return state;
  }
  protected issueStateClass(state: IssueState): string {
    switch (state) {
      case 'Issued':
      case 'Correction':
        return 'rr-badge-confirmed';
      case 'Voided':
        return 'rr-badge-failed';
      case 'Draft':
        return 'rr-badge-muted';
      default:
        return 'rr-badge-pending';
    }
  }
  protected deliveryStateClass(state: DeliveryState): string {
    switch (state) {
      case 'Delivered':
        return 'rr-badge-confirmed';
      case 'Failed':
        return 'rr-badge-failed';
      case 'Not sent':
        return 'rr-badge-muted';
      default:
        return 'rr-badge-pending';
    }
  }
  protected readonly copiedField = signal<string | null>(null);
  protected copyToClipboard(value: string): void {
    navigator.clipboard?.writeText(value).catch(() => undefined);
    this.copiedField.set(value);
    setTimeout(() => {
      if (this.copiedField() === value) this.copiedField.set(null);
    }, 1500);
  }
}

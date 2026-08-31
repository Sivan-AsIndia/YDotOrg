import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { ToastService } from '../../../../Shared/services/toast.service';
import { DataService } from '../../../../Service/data.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { RefundReason, canPerform } from '../../../../Shared/models/payment.model';
import { formatMoment } from '../../../../Shared/models/payment-adapters';
import {
  RccCaseState,
  RccCaseType,
  RccEvidenceItem,
  RccOutcomeStatus,
  RccPersistentOutcome,
  RccProviderStatus,
  RccReconciliationStatus,
  RccRefundCasePermissions,
  RccRefundCaseRecord,
  RccUiState,
} from '../../../../Shared/models/refund-chargeback-case.model';

/**
 * Refund and chargeback cases - SCR-PAY-008.
 *
 * ONE REGISTER, TWO KINDS OF CASE. A refund is money the organisation chooses to send back; a
 * chargeback is money a donor's bank takes back whether the organisation agrees or not. They read
 * the same way to an operator - a case, an amount, a state, a deadline - so they share a register,
 * and `caseType` is what routes each action to the right endpoint.
 *
 * APPROVING IS THE MOST CONSEQUENTIAL BUTTON IN THE MODULE: it submits the refund to the
 * organisation's gateway and money leaves. The server refuses it to the person who RAISED the
 * case, whatever permissions they hold, and no local rule could reproduce that - which is why
 * every button here is drawn from the server's own `permittedActions` for the open case rather
 * than from a state check.
 *
 * ROUTES TAKE IDENTIFIERS. `caseId` and `donationId` ride on every row for that reason; the
 * references are for people.
 */
@Component({
  selector: 'app-refund-and-chargeback-case',
  imports: [CommonModule, FormsModule],
  templateUrl: './refund-and-chargeback-case.html',
  styleUrl: './refund-and-chargeback-case.css',
})
export class RefundAndChargebackCaseComponent {
  private readonly toast = inject(ToastService);
  private readonly dataService = inject(DataService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly tokens = inject(AuthTokenService);

  protected readonly pageTitle = 'Refund and chargeback case';
  protected readonly pageSubtitle =
    'Control request, eligibility, approval, provider action and outcome.';
  protected readonly owner = computed(() => this.tokens.user()?.displayName ?? 'You');
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('');

  /**
   * What this caller may do with the OPEN case, decided by the server.
   *
   * `approve` IS THE ONE THAT MATTERS. Money leaving the organisation needs two people, and the
   * requester cannot be one of them twice - a rule that depends on WHO RAISED THE CASE, which is
   * invisible from the browser. So the value comes from `permittedActions` on the case's detail,
   * where the server has already folded in the state, the caller's permissions and the
   * segregation of duties together.
   *
   * `request` is the exception: raising a case is not an action on any existing record, so it is
   * gated on the caller's own permission.
   */
  private readonly permissionsState = computed<RccRefundCasePermissions>(() => {
    const actions = this.selectedActions();

    return {
      view: this.tokens.hasAnyPermission('pay.refunds.view', 'pay.chargebacks.view'),
      request: this.tokens.hasAnyPermission('pay.refunds.request'),
      submit: canPerform(actions, 'RequestRefund'),
      approve: canPerform(actions, 'Approve') || canPerform(actions, 'Reject'),
      reconcile: this.tokens.hasAnyPermission('pay.donations.reconcile'),
      deleteDraft: false,
    };
  });

  /**
   * The same values, reachable as a plain property.
   *
   * THE TEMPLATE READS `permissions.request`, without parentheses. A signal accessed that way
   * yields the function rather than its value, so the reactive source stays private and this
   * getter is what the template sees - it still reads the signal, so it still tracks.
   */
  protected get permissions(): RccRefundCasePermissions {
    return this.permissionsState();
  }

  /** The permitted-action list for whichever case is open, refreshed with its detail. */
  private readonly selectedActions = signal<readonly string[]>([]);

  /** The concurrency stamp of the open case, sent back with the decision. */
  private readonly selectedVersion = signal(0);

  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  protected readonly searchTerm = signal('');

  protected readonly caseTypeOptions: readonly RccCaseType[] = ['Refund request', 'Chargeback'];
  protected readonly caseTypeFilter = signal<RccCaseType | ''>('');

  protected readonly caseStateOptions: readonly RccCaseState[] = [
    'Draft',
    'Submitted',
    'Approved',
    'Refunded',
    'Charged back',
    'Reconciled',
    'Declined',
    'Cancelled',
  ];
  protected readonly caseStateFilter = signal<RccCaseState | ''>('');

  protected readonly providerStatusOptions: readonly RccProviderStatus[] = [
    'Not sent',
    'Requested',
    'Settled',
    'Declined',
    'Charged back',
  ];
  protected readonly providerStatusFilter = signal<RccProviderStatus | ''>('');

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
    if (!s && !e) return `Any created date · ${this.operatingTimeZone}`;
    return `${s ? this.formatDate(s) : '…'} – ${e ? this.formatDate(e) : '…'} · ${this.operatingTimeZone}`;
  });

  /**
   * The scope selector.
   *
   * IT NAMES THE SIGNED-IN ORGANISATION AND NOTHING ELSE. Three invented regions used to sit
   * beneath it, belonging to no one; the API scopes every read to the token's organisation, so
   * choosing one changed nothing except what the operator believed they were looking at.
   */
  protected readonly scopeOptions: readonly string[] = [
    `${this.tokens.tenant()?.tenantName ?? 'My active organisation'} (default)`,
  ];
  protected readonly scopeFilter = signal(this.scopeOptions[0]);
  protected readonly moreFiltersOpen = signal(false);
  protected toggleMoreFilters(): void {
    this.moreFiltersOpen.update((v) => !v);
  }
  protected readonly moreFiltersCount = computed(() => 0);

  protected readonly savedFilters = [
    'All cases (Default)',
    'Awaiting review',
    'Chargebacks only',
    'Unreconciled',
  ];
  protected readonly savedFilter = signal(this.savedFilters[0]);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim())
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    if (this.caseTypeFilter()) chips.push({ key: 'caseType', label: `Type: ${this.caseTypeFilter()}` });
    if (this.caseStateFilter())
      chips.push({ key: 'caseState', label: `Status: ${this.caseStateFilter()}` });
    if (this.providerStatusFilter())
      chips.push({ key: 'providerStatus', label: `Provider: ${this.providerStatusFilter()}` });
    if (this.rangeStart() || this.rangeEnd()) {
      chips.push({
        key: 'date',
        label: `Created: ${this.rangeStart() ? this.formatDate(this.rangeStart()) : '…'} – ${
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
      case 'caseType':
        this.caseTypeFilter.set('');
        break;
      case 'caseState':
        this.caseStateFilter.set('');
        break;
      case 'providerStatus':
        this.providerStatusFilter.set('');
        break;
      case 'date':
        this.rangeStart.set('');
        this.rangeEnd.set('');
        break;
    }
    this.currentPage.set(1);
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.caseTypeFilter.set('');
    this.caseStateFilter.set('');
    this.providerStatusFilter.set('');
    this.rangeStart.set('');
    this.rangeEnd.set('');
    this.savedFilter.set(this.savedFilters[0]);
    this.currentPage.set(1);
  }

  protected readonly filterAllowed = computed(
    () => this.permissions.view && !this.rangeInvalid(),
  );
  protected applyFilters(): void {
    if (!this.filterAllowed()) return;
    this.moreFiltersOpen.set(false);
    this.currentPage.set(1);
  }

  protected readonly records = signal<RccRefundCaseRecord[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);

  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const type = this.caseTypeFilter();
    const state = this.caseStateFilter();
    const provider = this.providerStatusFilter();
    const start = this.rangeStart() ? new Date(this.rangeStart()) : null;
    const end = this.rangeEnd() ? new Date(`${this.rangeEnd()}T23:59:59`) : null;

    return this.records().filter((r) => {
      if (
        q &&
        !(
          r.caseReference.toLowerCase().includes(q) ||
          r.paymentReference.toLowerCase().includes(q) ||
          r.requester.toLowerCase().includes(q)
        )
      ) {
        return false;
      }
      if (type && r.caseType !== type) return false;
      if (state && r.caseState !== state) return false;
      if (provider && r.providerStatus !== provider) return false;
      if (start && new Date(r.createdIso) < start) return false;
      if (end && new Date(r.createdIso) > end) return false;
      return true;
    });
  });

  protected readonly totalCases = computed(() => this.records().length);
  protected readonly refundRequestCount = computed(
    () => this.records().filter((r) => r.caseType === 'Refund request').length,
  );
  protected readonly chargebackCount = computed(
    () => this.records().filter((r) => r.caseType === 'Chargeback').length,
  );
  protected readonly awaitingReviewCount = computed(
    () => this.records().filter((r) => r.caseState === 'Submitted').length,
  );
  protected readonly recordCount = computed(() => this.visibleRecords().length);

  protected readonly pageSize = 10;
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
    if (page >= 1 && page <= this.totalPages()) this.currentPage.set(page);
  }

  /** Case reference of the currently selected row. */
  protected readonly selectedRef = signal<string>('');
  /** Whether the Case Details side panel is open. */
  protected readonly detailOpen = signal(false);
  protected readonly selectedCase = computed(
    () => this.records().find((r) => r.caseReference === this.selectedRef()) ?? null,
  );
  /**
   * Opens one case.
   *
   * IT FETCHES THE DETAIL, which is what decides the buttons. The register's rows carry no
   * permitted-action list - it depends on the caller AND on who raised the case, and computing it
   * for two hundred rows to draw three buttons would be wasteful - so it comes with the record the
   * operator actually opened.
   *
   * A CHARGEBACK IS FETCHED FROM A DIFFERENT ENDPOINT. The screen shows one combined register, but
   * the two case types are separately permissioned and separately routed, so `caseType` picks the
   * call - and both are addressed by identifier, never by the reference in the first column.
   */
  protected review(ref: string): void {
    this.selectedRef.set(ref);
    this.detailOpen.set(true);
    this.openRowMenu.set(null);
    this.selectedActions.set([]);
    this.selectedVersion.set(0);

    const record = this.records().find((candidate) => candidate.caseReference === ref);

    if (!record) {
      return;
    }

    // Typed as the common shape the screen actually needs. The two detail responses differ in
    // most of their fields and agree on exactly the two this uses, so narrowing here is both
    // honest and enough - and it avoids a union whose call signatures do not overlap.
    const detail$: Observable<{ permittedActions: string[]; version: number }> =
      record.caseType === 'Chargeback'
        ? this.paymentApi.getChargeback(record.caseId)
        : this.paymentApi.getRefund(record.caseId);

    detail$.subscribe({
      next: (detail) => {
        this.selectedActions.set(detail.permittedActions);
        this.selectedVersion.set(detail.version);
      },
      error: (error: unknown) =>
        this.toast.show(
          'Could not open',
          apiErrorMessage(error, 'That case could not be opened.'),
          'error',
        ),
    });
  }
  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
  }
  protected closeDetailPanel(): void {
    this.detailOpen.set(false);
    this.selectedRef.set('');
    this.selectedActions.set([]);
    this.selectedVersion.set(0);
  }

  protected readonly copiedField = signal<string | null>(null);
  protected copyValue(label: string, value: string): void {
    navigator.clipboard?.writeText(value).catch(() => undefined);
    this.copiedField.set(label);
    this.toast.show('Copied', `${value} copied to clipboard.`, 'success');
    setTimeout(() => {
      if (this.copiedField() === label) this.copiedField.set(null);
    }, 1500);
  }

  protected readonly openRowMenu = signal<string | null>(null);
  protected toggleRowMenu(ref: string): void {
    this.openRowMenu.update((cur) => (cur === ref ? null : ref));
  }

  protected submitAllowed(c: RccRefundCaseRecord | null): boolean {
    return !!c && this.permissions.submit && c.caseState === 'Draft';
  }
  protected approveAllowed(c: RccRefundCaseRecord | null): boolean {
    return (
      !!c &&
      c.caseReference === this.selectedRef() &&
      this.permissions.approve &&
      c.caseState === 'Submitted'
    );
  }
  protected reconcileAllowed(c: RccRefundCaseRecord | null): boolean {
    return (
      !!c &&
      this.permissions.reconcile &&
      (c.caseState === 'Refunded' || c.caseState === 'Charged back') &&
      c.reconciliationStatus !== 'Reconciled'
    );
  }
  protected deleteDraftAllowed(c: RccRefundCaseRecord | null): boolean {
    return (
      !!c && this.permissions.deleteDraft && c.caseState === 'Draft' && !c.hasDownstreamReference
    );
  }
  protected anyRowActionAllowed(c: RccRefundCaseRecord): boolean {
    return (
      this.submitAllowed(c) ||
      this.approveAllowed(c) ||
      this.reconcileAllowed(c) ||
      this.deleteDraftAllowed(c)
    );
  }

  protected readonly requestReasonMin = 10;
  protected readonly reasonMax = 2000;

  protected readonly requestDialogOpen = signal(false);
  protected readonly requestSubmitted = signal(false);

  /**
   * The payments a refund can be raised against.
   *
   * REAL DONATIONS, filtered to the ones a refund is actually possible on: settled money, not
   * already fully refunded, and inside the caller's data scope. Every amount below is in MAJOR
   * units - rupees, not paise - because that is what the API returns and what the API expects
   * back; the previous version multiplied by a hundred on the way in and compared paise against
   * rupees to decide whether the amount was allowable.
   */
  private readonly eligiblePaymentsState = signal<
    readonly {
      donationId: string;
      paymentReference: string;
      receiptNumber: string;
      currency: string;
      capturedAmount: number;
      previouslyRefundedAmount: number;
      refundableBalance: number;
    }[]
  >([]);

  /**
   * The same list, reachable as a plain property.
   *
   * The dialog iterates `@for (p of eligiblePayments; …)`, which needs something iterable rather
   * than a signal. Reading the signal inside the getter keeps the list reactive.
   */
  protected get eligiblePayments(): readonly {
    donationId: string;
    paymentReference: string;
    receiptNumber: string;
    currency: string;
    capturedAmount: number;
    previouslyRefundedAmount: number;
    refundableBalance: number;
  }[] {
    return this.eligiblePaymentsState();
  }

  private loadEligiblePayments(): void {
    if (!this.tokens.hasAnyPermission('pay.refunds.request')) return;

    this.paymentApi.searchDonations({ pageSize: 200, settlementStatus: 'settled' }).subscribe({
      next: (page) =>
        this.eligiblePaymentsState.set(
          (page.items ?? [])
            // A VOIDED OR FULLY REFUNDED DONATION HAS NOTHING LEFT TO REFUND, and offering one
            // invites a request the server will refuse.
            .filter((donation) => donation.status !== 'refunded' && donation.status !== 'voided')
            .map((donation) => {
              const captured = donation.amount?.amount ?? 0;
              const net = donation.netAmount?.amount ?? captured;

              return {
                donationId: donation.id,
                paymentReference: donation.donationReference,
                receiptNumber: donation.receiptNumber ?? '',
                currency: donation.amount?.currencyCode ?? '',
                capturedAmount: captured,

                // What has already gone back: the gap between what was captured and what the
                // organisation still holds.
                previouslyRefundedAmount: Math.max(0, captured - net),
                refundableBalance: Math.max(0, net),
              };
            }),
        ),
      error: () => this.eligiblePaymentsState.set([]),
    });
  }

  protected readonly reqPaymentReference = signal('');
  /** The requested refund, in MAJOR units - the same units the API takes. */
  protected readonly reqRequestedAmount = signal<number | null>(null);
  protected readonly reqReasonCategory = signal('');
  protected readonly reqDetailedReason = signal('');
  protected readonly reqEvidence = signal<RccEvidenceItem[]>([]);

  protected readonly selectedPayment = computed(
    () =>
      this.eligiblePayments.find((p) => p.paymentReference === this.reqPaymentReference()) ?? null,
  );
  protected readonly reqAmountDisplay = computed(() => this.reqRequestedAmount());
  protected onRequestedAmountChange(value: number | string | null): void {
    if (value === null || value === '' || value === undefined) {
      this.reqRequestedAmount.set(null);
      return;
    }
    const major = typeof value === 'string' ? parseFloat(value) : value;
    this.reqRequestedAmount.set(Number.isNaN(major) ? null : major);
  }
  protected readonly reqReasonCategoryCount = computed(() => this.reqReasonCategory().trim().length);
  protected readonly reqDetailedReasonCount = computed(() => this.reqDetailedReason().trim().length);
  protected readonly reqPaymentValid = computed(() => !!this.selectedPayment());
  protected readonly reqAmountValid = computed(() => {
    const p = this.selectedPayment();
    const a = this.reqRequestedAmount();
    return !!p && a !== null && a > 0 && a <= p.refundableBalance;
  });
  protected readonly reqReasonCategoryValid = computed(() => {
    const len = this.reqReasonCategory().trim().length;
    return len >= this.requestReasonMin && len <= this.reasonMax;
  });
  protected readonly reqDetailedReasonValid = computed(() => {
    const len = this.reqDetailedReason().trim().length;
    return len >= this.requestReasonMin && len <= this.reasonMax;
  });
  protected readonly requestDuplicate = computed(() => {
    const ref = this.reqPaymentReference();
    if (!ref) return null;
    return (
      this.records().find(
        (r) =>
          r.paymentReference === ref && ['Draft', 'Submitted', 'Approved'].includes(r.caseState),
      ) ?? null
    );
  });

  protected openRequest(): void {
    if (!this.permissions.request) return;
    this.reqPaymentReference.set('');
    this.reqRequestedAmount.set(null);
    this.reqReasonCategory.set('');
    this.reqDetailedReason.set('');
    this.reqEvidence.set([]);
    this.requestSubmitted.set(false);
    this.requestDialogOpen.set(true);
  }
  protected cancelRequest(): void {
    this.requestDialogOpen.set(false);
  }

  protected onEvidenceFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = input.files;
    if (!files || files.length === 0) return;
    Array.from(files).forEach((file) => this.attachEvidence(file.name));
    this.toast.show(
      files.length > 1 ? 'Files Uploaded' : 'File Uploaded',
      files.length > 1
        ? `${files.length} files noted against this request.`
        : `${files[0].name} noted against this request.`,
      'success',
    );
    input.value = '';
  }
  protected attachEvidence(fileName: string): void {
    const name = (fileName || '').trim();
    if (!name) return;
    this.reqEvidence.update((list) => [
      ...list,
      {
        name,
        classification: 'Confidential',
        uploadStatus: 'Noted',
        scanStatus: 'Not scanned',
        linkStatus: 'Named in the reason',
      },
    ]);
  }
  protected removeEvidence(name: string): void {
    this.reqEvidence.update((list) => list.filter((e) => e.name !== name));
    this.toast.show('Evidence Removed', `${name} has been unlinked from this request.`, 'success');
  }
  protected readonly requestValid = computed(
    () =>
      this.reqPaymentValid() &&
      this.reqAmountValid() &&
      this.reqReasonCategoryValid() &&
      this.reqDetailedReasonValid(),
  );

  /**
   * Raises a refund against a donation.
   *
   * THE SERVER CHECKS THE BALANCE. A request for more than is left, or a second request while one
   * is already undecided, is refused there - the second by a filtered unique index, so it is
   * impossible rather than merely unlikely.
   *
   * RAISING IS NOT APPROVING. The case opens as Requested and a DIFFERENT person decides it.
   */
  protected confirmRequest(): void {
    this.requestSubmitted.set(true);

    if (!this.requestValid()) {
      this.toast.show(
        'Validation Error',
        'Please complete all required fields correctly.',
        'warning',
      );
      return;
    }

    const payment = this.selectedPayment();
    if (!payment) return;

    const evidenceNote = this.reqEvidence().length
      ? ` Evidence held: ${this.reqEvidence()
          .map((e) => e.name)
          .join(', ')}.`
      : '';

    this.paymentApi
      .requestRefund(payment.donationId, {
        amount: this.reqRequestedAmount()!,
        reason: this.toRefundReason(this.reqReasonCategory()),
        reasonDetail: `${this.reqDetailedReason().trim()}${evidenceNote}`,
      })
      .subscribe({
        next: (refundCase) => {
          this.selectedRef.set(refundCase.caseReference);
          this.detailOpen.set(true);
          this.currentPage.set(1);
          this.requestDialogOpen.set(false);
          this.selectedActions.set(refundCase.permittedActions);
          this.selectedVersion.set(refundCase.version);

          this.toast.show(
            'Refund Requested',
            `Case ${refundCase.caseReference} is awaiting an independent decision.`,
            'success',
          );

          this.loadCases(refundCase.caseReference);
        },
        error: (error) => {
          this.requestDialogOpen.set(false);
          this.reportFailure(error, 'The refund could not be requested.');
        },
      });
  }

  /**
   * The screen's reason label to the API's code.
   *
   * A WHITELIST WITH A FALLBACK. The reason is a controlled catalogue on the server, and sending a
   * label it has never heard of would fail validation on a form the person has already completed -
   * so anything unrecognised becomes 'other' with the detail carrying the specifics.
   */
  private toRefundReason(label: string): RefundReason {
    switch (label.trim().toLowerCase()) {
      case 'donor requested':
        return 'donorRequested';
      case 'duplicate charge':
      case 'duplicate':
        return 'duplicateCharge';
      case 'incorrect amount':
        return 'incorrectAmount';
      case 'fraudulent':
      case 'fraud':
        return 'fraudulent';
      case 'campaign cancelled':
        return 'campaignCancelled';
      case 'test transaction':
        return 'testTransaction';
      default:
        return 'other';
    }
  }

  /**
   * Reports a failed write.
   *
   * A SEGREGATION-OF-DUTIES REFUSAL HAS TO SAY SO PLAINLY: "you cannot approve a case you raised"
   * is something the person can act on by finding a colleague, and burying it in a generic failure
   * would leave them retrying.
   */
  private reportFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'SEGREGATION_OF_DUTIES') {
      this.toast.show(
        'Second person required',
        apiErrorMessage(error, 'A refund cannot be decided by the person who raised it.'),
        'warning',
      );
      return;
    }

    if (code === 'CONCURRENCY_CONFLICT') {
      this.toast.show('Case changed', 'Somebody else worked this case. Refreshing.', 'warning');
      this.loadCases(this.selectedRef());
      return;
    }

    this.toast.show('Action failed', apiErrorMessage(error, fallback), 'error');
  }

  /**
   * THERE IS NO SEPARATE SUBMIT STEP any more, and its absence is the design.
   *
   * The screen used to create a Draft and then submit it for review - two acts by the same person
   * before anybody else was involved. On the API a refund is RAISED straight into Requested, which
   * is the state an approver works from: there is nothing a draft could usefully be except a delay
   * before the second person sees it. The method opens the case rather than pretending to move it.
   */
  protected submitCase(c: RccRefundCaseRecord): void {
    this.openRowMenu.set(null);
    this.review(c.caseReference);
  }

  protected readonly approveDialogOpen = signal(false);
  protected readonly approveTarget = signal<RccRefundCaseRecord | null>(null);
  protected readonly approveDecision = signal<'Approve' | 'Decline'>('Approve');
  protected readonly approveReason = signal('');
  protected readonly approveSubmitted = signal(false);
  protected readonly approveReasonCount = computed(() => this.approveReason().trim().length);
  protected readonly approveReasonValid = computed(() => {
    const len = this.approveReason().trim().length;
    return len >= this.requestReasonMin && len <= this.reasonMax;
  });
  protected requestApprove(c: RccRefundCaseRecord): void {
    this.openRowMenu.set(null);
    if (!this.approveAllowed(c)) return;
    this.approveTarget.set(c);
    this.approveDecision.set('Approve');
    this.approveReason.set('');
    this.approveSubmitted.set(false);
    this.approveDialogOpen.set(true);
  }
  protected cancelApprove(): void {
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
  }
  /**
   * Decides a refund.
   *
   * APPROVING IS WHAT ACTUALLY SENDS MONEY BACK. It submits the refund to the organisation's
   * gateway, so it is the single most consequential button on this screen - and the one the server
   * refuses to the person who raised the case, whatever permissions they hold.
   *
   * A REJECTION MUST SAY WHY. Somebody asked for this and deserves an answer; "rejected" with
   * nothing beside it is the kind of record that produces a second identical request a week later.
   */
  protected confirmApprove(): void {
    this.approveSubmitted.set(true);

    if (!this.approveReasonValid()) {
      this.toast.show('Validation Error', 'Please provide a valid reason.', 'warning');
      return;
    }

    const target = this.approveTarget();
    if (!target) return;

    const reason = this.approveReason().trim();
    const isApproval = this.approveDecision() === 'Approve';
    const expectedVersion = this.selectedVersion() || target.version;

    const call = isApproval
      ? this.paymentApi.approveRefund(target.caseId, { expectedVersion, note: reason })
      : this.paymentApi.rejectRefund(target.caseId, { expectedVersion, reason });

    call.subscribe({
      next: () => {
        this.approveDialogOpen.set(false);
        this.approveTarget.set(null);

        if (isApproval) {
          this.toast.show(
            'Refund Approved',
            `Case ${target.caseReference} was approved and submitted to the payment provider.`,
            'success',
          );
        } else {
          this.toast.show('Refund Rejected', `Case ${target.caseReference} was rejected.`, 'warning');
        }

        this.loadCases(target.caseReference);
      },
      error: (error) => {
        this.approveDialogOpen.set(false);
        this.approveTarget.set(null);
        this.reportFailure(
          error,
          isApproval ? 'The refund could not be approved.' : 'The refund could not be rejected.',
        );
      },
    });
  }

  /**
   * Who is authorised to decide.
   *
   * IT NAMES THE SIGNED-IN PERSON, because they are the one the server will record. The previous
   * version printed a fixed name from another organisation beside the word "Independent
   * authority", which told an approver that somebody else had signed off on money leaving.
   */
  protected readonly approverIdentity = `${this.tokens.user()?.displayName ?? 'You'} (deciding as an independent approver)`;

  /**
   * Reconciles the case's donation against the bank.
   *
   * IT WRITES TO THE DONATION, not to the case. On this platform reconciliation is an assertion
   * about MONEY - somebody looked at a statement line and this donation and said they are the
   * same - and that assertion lives on the donation with the person's identity beside it. The
   * previous version flipped a label in the browser, which survived until the next refresh and
   * was recorded nowhere.
   */
  protected reconcileCase(c: RccRefundCaseRecord): void {
    this.openRowMenu.set(null);
    if (!this.reconcileAllowed(c)) return;

    this.paymentApi.getDonation(c.donationId).subscribe({
      next: (donation) => {
        this.paymentApi
          .reconcileDonation(c.donationId, {
            expectedVersion: donation.version,
            status: 'manuallyResolved',
            note: `Reconciled to the provider settlement for case ${c.caseReference}.`,
          })
          .subscribe({
            next: () => {
              this.selectedRef.set(c.caseReference);
              this.detailOpen.set(true);
              this.lastOutcome.set({
                reference: c.caseReference,
                state: 'Reconciled',
                downstreamStatus: 'Reconciled to the provider settlement',
                nextAction: 'No further action required',
              });
              this.uiState.set('success');
              this.toast.show(
                'Case Reconciled',
                `Donation ${c.paymentReference} was marked reconciled.`,
                'success',
              );
              this.loadCases(c.caseReference);
            },
            error: (error) => this.reportFailure(error, 'The case could not be reconciled.'),
          });
      },
      error: (error) => this.reportFailure(error, 'The donation behind this case could not be read.'),
    });
  }

  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteTarget = signal<RccRefundCaseRecord | null>(null);
  protected readonly deleteReason = signal('');
  protected readonly deleteSubmitted = signal(false);
  protected readonly deleteReasonCount = computed(() => this.deleteReason().trim().length);
  protected readonly deleteReasonValid = computed(() => {
    const len = this.deleteReason().trim().length;
    return len >= this.requestReasonMin && len <= this.reasonMax;
  });
  protected requestDelete(c: RccRefundCaseRecord): void {
    this.openRowMenu.set(null);
    if (!this.deleteDraftAllowed(c)) return;
    this.deleteTarget.set(c);
    this.deleteReason.set('');
    this.deleteSubmitted.set(false);
    this.deleteDialogOpen.set(true);
  }
  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
  }
  /**
   * UNREACHABLE, AND DELIBERATELY SO.
   *
   * A refund case cannot be deleted: it is the record of somebody having asked for money back, and
   * a request that was refused is more useful in the trail than a row that never existed.
   * `deleteDraft` is permanently false, so the button that reaches this is never drawn; the method
   * survives only because the row menu still names it.
   */
  protected confirmDelete(): void {
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
    this.toast.show(
      'Cases are not deleted',
      'A refund case is the record of a request. Reject it with a reason instead - the case stays, and so does the reason.',
      'warning',
    );
  }

  protected readonly uiState = signal<RccUiState>('loading');
  protected dismissBanner(): void {
    this.uiState.set('ready');
    this.lastOutcome.set(null);
  }

  protected readonly lastOutcome = signal<{
    reference: string;
    state: RccCaseState;
    downstreamStatus: string;
    nextAction: string;
  } | null>(null);

  protected readonly persistentOutcome = computed<RccPersistentOutcome>(() => {
    const outcome = this.lastOutcome();
    if (outcome) {
      return { ...outcome, effectiveTime: this.lastRefresh(), owner: this.owner() };
    }
    const c = this.selectedCase();
    return {
      reference: c?.caseReference ?? '—',
      state: c?.caseState ?? 'Draft',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: c
        ? `Provider ${c.providerStatus} · ${c.reconciliationStatus}`
        : 'No pending action',
      owner: this.owner(),
      nextAction: c ? this.nextActionFor(c) : 'Use Request to initiate a new case',
    };
  });

  protected nextActionFor(c: RccRefundCaseRecord): string {
    if (this.submitAllowed(c)) return 'Submit the draft for independent review';
    if (c.caseState === 'Submitted') return 'Awaiting an independent approval';
    if (this.reconcileAllowed(c)) return 'Reconcile to the provider settlement';
    if (c.caseState === 'Approved') return 'Await the provider outcome';
    return 'No further action required';
  }

  /** Amounts are held in MAJOR units, exactly as the API reports and accepts them. */
  protected formatMoney(amount: number, currency: string): string {
    const value = (amount ?? 0).toLocaleString('en-IN', {
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

  constructor() {
    if (!this.permissions.view) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    this.loadEligiblePayments();
    this.loadCases();
  }

  private loadCases(keepSelected?: string): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.dataService.getRefundChargebackData().subscribe({
      next: (res) => {
        this.records.set(res);
        this.lastRefresh.set(formatMoment(new Date().toISOString()));
        this.loading.set(false);

        if (this.uiState() !== 'success' && this.uiState() !== 'no-access') {
          this.uiState.set(res.length === 0 ? 'empty' : 'ready');
        }

        if (keepSelected && res.some((r) => r.caseReference === keepSelected)) {
          this.review(keepSelected);
        } else if (!keepSelected) {
          this.selectedRef.set('');
          this.detailOpen.set(false);
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
          apiErrorMessage(error, 'Refund and chargeback cases could not be loaded.'),
          'error',
        );
      },
    });
  }

  protected caseStateClass(state: RccCaseState): string {
    switch (state) {
      case 'Draft':
        return 'rcc-badge-muted';
      case 'Submitted':
        return 'rcc-badge-blue';
      case 'Approved':
        return 'rcc-badge-gold';
      case 'Refunded':
        return 'rcc-badge-good';
      case 'Charged back':
        return 'rcc-badge-danger';
      case 'Reconciled':
        return 'rcc-badge-good';
      case 'Declined':
        return 'rcc-badge-danger';
      case 'Cancelled':
        return 'rcc-badge-muted';
      default:
        return 'rcc-badge-muted';
    }
  }
  protected providerStatusClass(status: RccProviderStatus): string {
    switch (status) {
      case 'Not sent':
        return 'rcc-badge-muted';
      case 'Requested':
        return 'rcc-badge-gold';
      case 'Settled':
        return 'rcc-badge-good';
      case 'Declined':
        return 'rcc-badge-danger';
      case 'Charged back':
        return 'rcc-badge-danger';
      default:
        return 'rcc-badge-muted';
    }
  }
  protected reconciliationClass(status: RccReconciliationStatus): string {
    switch (status) {
      case 'Unreconciled':
        return 'rcc-badge-gold';
      case 'Reconciled':
        return 'rcc-badge-good';
      case 'Not applicable':
        return 'rcc-badge-muted';
      default:
        return 'rcc-badge-muted';
    }
  }
  protected outcomeClass(status: RccOutcomeStatus): string {
    switch (status) {
      case 'Pending':
        return 'rcc-badge-gold';
      case 'Refunded':
        return 'rcc-badge-good';
      case 'Charged back':
        return 'rcc-badge-danger';
      case 'Declined':
        return 'rcc-badge-danger';
      case 'Cancelled':
        return 'rcc-badge-muted';
      default:
        return 'rcc-badge-muted';
    }
  }
}

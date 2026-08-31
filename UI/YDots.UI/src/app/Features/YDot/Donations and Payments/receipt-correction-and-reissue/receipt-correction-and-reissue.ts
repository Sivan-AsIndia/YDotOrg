import { CommonModule } from '@angular/common';
import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../../../Shared/services/toast.service';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import type {
  ReceiptListItem,
  ReceiptSearchFilter,
  ReceiptStatus,
} from '../../../../Shared/models/payment.model';
import { formatMoment } from '../../../../Shared/models/payment-adapters';
import {
  RcrCorrectionCategory,
  RcrCorrectionPermissions,
  RcrCorrectionRequest,
  RcrCorrectionStatus,
  RcrPersistentOutcome,
  RcrUiState,
} from '../../../../Shared/models/receipt-correction-and-reissue.model';

/**
 * Receipt correction and reissue - SCR-PAY-006.
 *
 * WHAT A "REQUEST" IS HERE. The API holds no separate correction-request entity, and inventing
 * one in the browser would mean a request that vanished on refresh and an approval nobody could
 * audit. A row on this screen is therefore a RECEIPT, and its status is that receipt's own
 * position in the correction lifecycle: issued and standing, superseded by a correction, or
 * voided. Everything the screen shows can be read back from the server tomorrow.
 *
 * THE FOUR ROW ACTIONS ARE THE FOUR API CALLS, named as the screen names them:
 *
 *   Review difference          - opens the receipt beside the one it supersedes
 *   Approve reissue            - POST /receipts/{id}/correct   (issues the corrected version)
 *   Deliver corrected receipt  - POST /receipts/{id}/resend
 *   Reject request             - POST /receipts/{id}/void
 *
 * ELIGIBILITY MIRRORS THE SERVER'S OWN RULE, which is `status == Issued && hasPermission(...)`.
 * It is reproduced here rather than guessed, so a button is drawn only where the API would
 * accept the call - and the write still sends the version it read, so a race is a 409 rather
 * than a silent overwrite.
 */
@Component({
  selector: 'app-receipt-correction-and-reissue',
  imports: [CommonModule, FormsModule],
  templateUrl: './receipt-correction-and-reissue.html',
  styleUrl: './receipt-correction-and-reissue.css',
})
export class ReceiptCorrectionAndReissueComponent {
  private readonly toast = inject(ToastService);
  private readonly paymentApi = inject(PaymentApiService);
  private readonly tokens = inject(AuthTokenService);

  /** One read pulls this many rows; the toolbar pages over them. */
  private static readonly FETCH_SIZE = 200;

  // ================= Task header (§4.8.1 Task header) =================
  protected readonly pageTitle = 'Receipt Correction & Reissue';
  protected readonly pageSubtitle = 'Correct receipt details or reissue a receipt when required.';
  protected readonly owner = computed(
    () => this.tokens.user()?.displayName ?? 'You',
  );
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  /** Freshness - when this screen last read the register, never a fixed string. */
  protected readonly lastRefresh = signal('');

  /**
   * What this caller may do, read from the token rather than assumed.
   *
   * CORRECTING A RECEIPT IS NOT THE SAME PERMISSION AS ISSUING ONE. A correction supersedes a tax
   * document a donor may already have claimed relief on, so it sits with finance rather than with
   * whoever issued the original - and the buttons say so before somebody presses one.
   */
  protected readonly permissions = computed<RcrCorrectionPermissions>(() => ({
    view: this.tokens.hasAnyPermission('pay.receipts.view'),
    requestCorrection: this.tokens.hasAnyPermission('pay.receipts.correct'),
    reviewDifference: this.tokens.hasAnyPermission('pay.receipts.view'),
    approveReissue: this.tokens.hasAnyPermission('pay.receipts.correct'),
    deliverCorrectedReceipt: this.tokens.hasAnyPermission('pay.receipts.resend'),
    rejectRequest: this.tokens.hasAnyPermission('pay.receipts.void'),
  }));

  // ================= Approved catalogues (§4.8.2) =================
  protected readonly categoryOptions: readonly RcrCorrectionCategory[] = [
    'Amount Correction',
    'Donor Name Correction',
    'Receipt Date Correction',
    'Reissue (Duplicate)',
    'Reissue (Lost Receipt)',
  ];
  /** Delivery channel - the channels the receipt service actually supports. */
  protected readonly channelOptions: readonly string[] = ['Email'];
  /**
   * Who may approve.
   *
   * IT IS THE SIGNED-IN PERSON, and only them. The previous version offered three invented names,
   * which told an operator they were routing a tax-document correction to a colleague who does
   * not exist. The API records the caller as the approver, so that is what the field says.
   */
  protected readonly approverOptions = computed<readonly string[]>(() => [this.owner()]);
  protected readonly statusOptions: readonly RcrCorrectionStatus[] = [
    'Draft',
    'Pending Approval',
    'Approved',
    'Completed',
    'Rejected',
  ];

  // ================= Context and filters (§4.8.1 Context and filters) =================
  protected readonly filtersVisible = signal(false);
  protected toggleFiltersVisible(): void {
    this.filtersVisible.update((v) => !v);
  }

  protected readonly searchTerm = signal('');
  protected readonly categoryFilter = signal<RcrCorrectionCategory | ''>('');
  protected readonly statusFilter = signal<RcrCorrectionStatus | ''>('');
  protected readonly receiptFilter = signal('');
  protected readonly donorFilter = signal('');
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
    if (!s && !e) return `Any requested date · ${this.operatingTimeZone}`;
    return `${s ? this.formatDate(s) : '…'} – ${e ? this.formatDate(e) : '…'} · ${this.operatingTimeZone}`;
  });

  protected readonly savedFilters = [
    'All requests (Default)',
    'Pending approval',
    'Completed',
    'Rejected',
  ];
  protected readonly savedFilter = signal(this.savedFilters[0]);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.searchTerm().trim())
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    if (this.categoryFilter())
      chips.push({ key: 'category', label: `Correction type: ${this.categoryFilter()}` });
    if (this.statusFilter()) chips.push({ key: 'status', label: `Status: ${this.statusFilter()}` });
    if (this.receiptFilter().trim())
      chips.push({ key: 'receipt', label: `Receipt no.: ${this.receiptFilter().trim()}` });
    if (this.donorFilter().trim())
      chips.push({ key: 'donor', label: `Donor: ${this.donorFilter().trim()}` });
    if (this.rangeStart() || this.rangeEnd()) {
      chips.push({
        key: 'date',
        label: `Requested: ${this.rangeStart() ? this.formatDate(this.rangeStart()) : '…'} – ${
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
      case 'category':
        this.categoryFilter.set('');
        break;
      case 'status':
        this.statusFilter.set('');
        break;
      case 'receipt':
        this.receiptFilter.set('');
        break;
      case 'donor':
        this.donorFilter.set('');
        break;
      case 'date':
        this.rangeStart.set('');
        this.rangeEnd.set('');
        break;
    }
    this.applyFilters();
  }
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.categoryFilter.set('');
    this.statusFilter.set('');
    this.receiptFilter.set('');
    this.donorFilter.set('');
    this.rangeStart.set('');
    this.rangeEnd.set('');
    this.savedFilter.set(this.savedFilters[0]);
    this.applyFilters();
  }
  protected readonly filterAllowed = computed(
    () => this.permissions().view && !this.rangeInvalid(),
  );
  protected applyFilters(): void {
    if (this.rangeInvalid()) return;
    this.currentPage.set(1);
    this.loadRequests();
  }

  // ================= Records (§4.8.1 Main work + §4.8.2) =================
  protected readonly records = signal<RcrCorrectionRequest[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly serverTotal = signal(0);

  // ================= Sorting (Requested at) =================
  protected readonly sortDir = signal<'asc' | 'desc'>('desc');
  protected toggleSort(): void {
    this.sortDir.update((d) => (d === 'desc' ? 'asc' : 'desc'));
  }

  /**
   * The loaded set.
   *
   * The status, the date range and the free-text search are applied by the SERVER; the two
   * remaining boxes - correction type and donor - have no counterpart in the receipt search, so
   * they are narrowed here over what came back.
   */
  protected readonly filteredRecords = computed(() => {
    const category = this.categoryFilter();
    const donor = this.donorFilter().trim().toLowerCase();

    const rows = this.records().filter((r) => {
      if (category && r.correctionCategory !== category) return false;
      if (donor && !r.donorName.toLowerCase().includes(donor)) return false;
      return true;
    });

    const dir = this.sortDir() === 'desc' ? -1 : 1;
    return [...rows].sort(
      (a, b) => dir * (new Date(a.requestedAtIso).getTime() - new Date(b.requestedAtIso).getTime()),
    );
  });

  // ================= Pagination =================
  protected readonly pageSize = 10;
  protected readonly currentPage = signal(1);
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredRecords().length / this.pageSize)),
  );
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, i) => i + 1),
  );
  protected readonly pagedRecords = computed(() => {
    const page = Math.min(this.currentPage(), this.totalPages());
    const start = (page - 1) * this.pageSize;
    return this.filteredRecords().slice(start, start + this.pageSize);
  });
  protected readonly showingFrom = computed(() =>
    this.filteredRecords().length === 0
      ? 0
      : (Math.min(this.currentPage(), this.totalPages()) - 1) * this.pageSize + 1,
  );
  protected readonly showingTo = computed(() =>
    Math.min(
      this.filteredRecords().length,
      Math.min(this.currentPage(), this.totalPages()) * this.pageSize,
    ),
  );
  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.currentPage.set(page);
  }
  protected prevPage(): void {
    this.goToPage(this.currentPage() - 1);
  }
  protected nextPage(): void {
    this.goToPage(this.currentPage() + 1);
  }

  // ================= Totals qualified by scope (§4.8.1) =================
  protected readonly totalRequests = computed(() => this.serverTotal());
  protected readonly pendingCount = computed(
    () => this.records().filter((r) => r.status === 'Pending Approval').length,
  );
  protected readonly completedCount = computed(
    () => this.records().filter((r) => r.status === 'Completed').length,
  );
  protected readonly rejectedCount = computed(
    () => this.records().filter((r) => r.status === 'Rejected').length,
  );

  // ================= Row selection (checkbox column) =================
  protected readonly selectedIds = signal<Set<string>>(new Set());
  protected isChecked(ref: string): boolean {
    return this.selectedIds().has(ref);
  }
  protected toggleRow(ref: string): void {
    this.selectedIds.update((set) => {
      const next = new Set(set);
      if (next.has(ref)) next.delete(ref);
      else next.add(ref);
      return next;
    });
    this.select(ref);
  }
  protected readonly allVisibleSelected = computed(() => {
    const rows = this.pagedRecords();
    return rows.length > 0 && rows.every((r) => this.selectedIds().has(r.requestReference));
  });
  protected toggleAllVisible(): void {
    const rows = this.pagedRecords();
    const all = this.allVisibleSelected();
    this.selectedIds.update((set) => {
      const next = new Set(set);
      for (const r of rows) {
        if (all) next.delete(r.requestReference);
        else next.add(r.requestReference);
      }
      return next;
    });
  }

  // ================= Selection -> working record (§4.8.5) =================
  protected readonly selectedRef = signal<string>('');
  protected readonly selectedRecord = computed(
    () => this.records().find((r) => r.requestReference === this.selectedRef()) ?? null,
  );
  protected select(ref: string): void {
    if (!this.permissions().view) return;
    this.selectedRef.set(ref);
    this.detailOpen.set(true);
  }
  protected isSelected(ref: string): boolean {
    return this.selectedRef() === ref;
  }
  protected readonly detailOpen = signal(false);
  protected closeSelectedRow(): void {
    this.selectedRef.set('');
    this.detailOpen.set(false);
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
  protected readonly relatedTab = signal<(typeof this.relatedTabs)[number]>('Activity');
  protected selectRelatedTab(tab: (typeof this.relatedTabs)[number]): void {
    this.relatedTab.set(tab);
  }

  // ================= Action eligibility (§4.8.3) =================

  /**
   * Whether the caller may act on this receipt at all.
   *
   * THE SERVER'S RULE, REPRODUCED: only an ISSUED receipt can be corrected, voided or re-sent. A
   * superseded or voided receipt is history - it is read, and nothing else.
   */
  private actionable(r: RcrCorrectionRequest | null): boolean {
    return !!r && r.status === 'Approved';
  }

  protected requestCorrectionAllowed(): boolean {
    return this.permissions().requestCorrection;
  }
  protected reviewAllowed(r: RcrCorrectionRequest | null): boolean {
    return !!r && this.permissions().reviewDifference;
  }
  protected approveAllowed(r: RcrCorrectionRequest | null): boolean {
    return this.actionable(r) && this.permissions().approveReissue;
  }
  protected deliverAllowed(r: RcrCorrectionRequest | null): boolean {
    return this.actionable(r) && this.permissions().deliverCorrectedReceipt;
  }
  protected rejectAllowed(r: RcrCorrectionRequest | null): boolean {
    return this.actionable(r) && this.permissions().rejectRequest;
  }
  protected anyRowActionAllowed(r: RcrCorrectionRequest | null): boolean {
    return (
      this.reviewAllowed(r) ||
      this.approveAllowed(r) ||
      this.deliverAllowed(r) ||
      this.rejectAllowed(r)
    );
  }

  // ================= Header overflow menu (§4.8.3) =================
  protected readonly overflowOpen = signal(false);
  protected toggleOverflow(): void {
    this.overflowOpen.update((v) => !v);
  }
  protected closeOverflow(): void {
    this.overflowOpen.set(false);
  }

  // ================= Per-row actions menu =================
  protected readonly rowMenuFor = signal<string | null>(null);
  protected toggleRowMenu(ref: string): void {
    this.rowMenuFor.update((cur) => (cur === ref ? null : ref));
    this.select(ref);
  }
  protected closeRowMenu(): void {
    this.rowMenuFor.set(null);
  }

  protected readonly helpOpen = signal(false);
  protected toggleHelp(): void {
    this.helpOpen.update((v) => !v);
  }

  // ================= Request correction - primary (§4.8.3) =================
  protected readonly requestDialogOpen = signal(false);
  protected readonly requestSubmitted = signal(false);
  protected readonly reasonMin = 10;
  protected readonly reasonMax = 2000;

  protected readonly formReceipt = signal('');
  protected readonly formCategory = signal<RcrCorrectionCategory | ''>('');
  protected readonly formCurrentValue = signal('');
  protected readonly formProposedValue = signal('');
  protected readonly formReason = signal('');
  protected readonly formEvidence = signal('');
  protected readonly formEvidenceSize = signal('');
  protected readonly formApprover = signal('');
  protected readonly formChannel = signal('');

  protected readonly formReasonCount = computed(() => this.formReason().trim().length);
  protected readonly receiptValid = computed(() => this.formReceipt().trim().length > 0);
  protected readonly categoryValid = computed(() => this.formCategory() !== '');
  protected readonly proposedValid = computed(() => this.formProposedValue().trim().length > 0);
  protected readonly reasonValid = computed(() => {
    const len = this.formReason().trim().length;
    return len >= this.reasonMin && len <= this.reasonMax;
  });
  protected readonly approverValid = computed(() => this.formApprover() !== '');
  protected readonly channelValid = computed(() => this.formChannel() !== '');
  protected readonly formValid = computed(
    () =>
      this.receiptValid() &&
      this.categoryValid() &&
      this.proposedValid() &&
      this.reasonValid() &&
      this.approverValid() &&
      this.channelValid(),
  );

  // ---- Supporting evidence ----
  private readonly evidenceInput = viewChild<ElementRef<HTMLInputElement>>('evidenceFileInput');
  // The optional `event` on each of these is what the template passes: the controls are keyboard
  // activatable as well as clickable, so the handler is reached from click, Enter and Space alike
  // and has to stop the event reaching the surrounding row.
  protected triggerEvidenceUpload(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.evidenceInput()?.nativeElement.click();
  }
  protected onEvidenceSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.formEvidence.set(file.name);
    this.formEvidenceSize.set(`${(file.size / 1024).toFixed(0)} KB`);
  }
  protected clearEvidence(event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    this.formEvidence.set('');
    this.formEvidenceSize.set('');
    const input = this.evidenceInput()?.nativeElement;
    if (input) input.value = '';
  }
  /**
   * Opens the document held against a correction.
   *
   * IT SAYS WHERE THE FILE IS RATHER THAN PRETENDING TO FETCH IT. The receipts API returns a
   * document URL on the receipt itself and holds no separate evidence store, so a download button
   * here has nothing of its own to serve - and a button that silently does nothing is worse than
   * one that explains itself.
   */
  protected downloadEvidence(item: { name: string } | string, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    const name = typeof item === 'string' ? item : item.name;
    this.toast.show(
      'Evidence',
      `${name} is held against this correction. Open it from the receipt's document link.`,
      'info',
    );
  }

  protected openRequestDialog(): void {
    this.closeOverflow();
    if (!this.requestCorrectionAllowed()) return;
    const r = this.selectedRecord();
    this.requestSubmitted.set(false);
    this.formReceipt.set(r?.receiptReference ?? '');
    this.formCategory.set('');
    this.formCurrentValue.set(r?.currentValue ?? '');
    this.formProposedValue.set('');
    this.formReason.set('');
    this.clearEvidence();
    this.formApprover.set(this.owner());
    this.formChannel.set(this.channelOptions[0]);
    this.requestDialogOpen.set(true);
  }
  protected closeRequestDialog(): void {
    this.requestDialogOpen.set(false);
  }

  /**
   * Submits the correction.
   *
   * IT GOES STRAIGHT TO THE SERVER RATHER THAN INTO A LOCAL QUEUE. The previous version pushed an
   * invented `COR-2025-…` row onto an array: it looked like a submitted request, survived nothing,
   * and no approver anywhere could ever have seen it. A correction here issues the corrected
   * receipt, with the reason recorded and the original retained exactly as issued.
   *
   * WHAT THE PROPOSED VALUE MEANS depends on the category, and only the donor-detail categories
   * can be carried: the API deliberately refuses to take an amount, because a receipt is for what
   * was actually given and letting a caller choose the figure on a tax document is the hole a
   * receipt exists to close. An amount correction is therefore a void and a fresh receipt, which
   * the screen says rather than silently ignoring the field.
   */
  protected submitRequest(): void {
    this.requestSubmitted.set(true);
    if (!this.formValid()) return;

    const target = this.selectedRecord() ?? this.byReceiptNumber(this.formReceipt().trim());

    if (!target) {
      this.toast.show(
        'Receipt not found',
        `No receipt numbered ${this.formReceipt().trim()} is in view. Search for it first, then request the correction.`,
        'warning',
      );
      return;
    }

    if (!this.actionable(target)) {
      this.toast.show(
        'Not correctable',
        'Only a receipt that is currently issued can be corrected. This one has been superseded or voided.',
        'warning',
      );
      return;
    }

    const category = this.formCategory() as RcrCorrectionCategory;

    if (category === 'Amount Correction') {
      this.toast.show(
        'Amount cannot be corrected',
        'A receipt is for the amount actually given. Void this receipt and issue a fresh one against the corrected donation instead.',
        'warning',
      );
      return;
    }

    const proposed = this.formProposedValue().trim();
    const reason = `${category}: ${this.formReason().trim()}`;

    this.requestDialogOpen.set(false);
    this.uiState.set('loading');

    this.paymentApi
      .correctReceipt(target.receiptId, {
        expectedVersion: target.version,
        correctionReason: reason,
        donorName: category === 'Donor Name Correction' ? proposed : null,
        deliverImmediately: this.formChannel() === 'Email',
      })
      .subscribe({
        next: (corrected) => {
          this.toast.show(
            'Correction Issued',
            `Receipt ${corrected.receiptNumber ?? ''} supersedes ${target.receiptReference}.`,
            'success',
          );
          this.lastOutcome.set({
            reference: corrected.receiptNumber ?? target.receiptReference,
            state: 'Completed',
            downstreamStatus: `Corrected receipt issued; ${target.receiptReference} retained as issued`,
            nextAction: 'No further action. The original receipt remains in the audit trail.',
          });
          this.uiState.set('success');
          this.loadRequests(corrected.receiptNumber ?? '');
        },
        error: (error) => this.reportFailure(error, 'The receipt could not be corrected.'),
      });
  }

  // ================= Review difference (§4.8.3) =================
  protected readonly reviewDialogOpen = signal(false);
  protected readonly reviewTarget = signal<RcrCorrectionRequest | null>(null);
  protected openReview(r: RcrCorrectionRequest): void {
    this.closeOverflow();
    this.closeRowMenu();
    if (!this.reviewAllowed(r)) return;
    this.select(r.requestReference);
    this.reviewTarget.set(r);
  }
  protected closeReview(): void {
    this.reviewDialogOpen.set(false);
    this.reviewTarget.set(null);
  }
  protected openReview1(r: RcrCorrectionRequest): void {
    this.closeOverflow();
    this.closeRowMenu();
    if (!this.reviewAllowed(r)) return;
    this.select(r.requestReference);
    this.reviewTarget.set(r);
    this.reviewDialogOpen.set(true);
  }

  // ================= Approve reissue - high-risk confirm (§4.8.3, §4.8.6) =================
  protected readonly approveDialogOpen = signal(false);
  protected readonly approveTarget = signal<RcrCorrectionRequest | null>(null);
  protected requestApprove(r: RcrCorrectionRequest): void {
    this.closeOverflow();
    this.closeRowMenu();
    if (!this.approveAllowed(r)) return;
    this.select(r.requestReference);
    this.approveTarget.set(r);
    this.approveDialogOpen.set(true);
  }
  protected closeApprove(): void {
    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
  }
  /**
   * Issues the corrected receipt.
   *
   * A CORRECTION IS A NEW VERSION AND TAKES THE NEXT NUMBER IN THE SERIES, allocated by the
   * server. The original stays exactly as issued, because a donor who claimed relief on version 1
   * must still be able to show what version 1 said, and the new one points back at what it
   * supersedes.
   *
   * THE VERSION GOES WITH IT: correcting a receipt somebody else has already corrected would
   * otherwise silently overwrite their correction.
   */
  protected confirmApprove(): void {
    const target = this.approveTarget();

    if (!target || !this.approveAllowed(target)) {
      return;
    }

    this.approveDialogOpen.set(false);
    this.approveTarget.set(null);
    this.uiState.set('loading');

    this.paymentApi
      .correctReceipt(target.receiptId, {
        expectedVersion: target.version,
        correctionReason:
          target.reason || 'Receipt corrected following a review of the donor details.',
        deliverImmediately: true,
      })
      .subscribe({
        next: (corrected) => {
          this.lastOutcome.set({
            reference: corrected.receiptNumber ?? target.receiptReference,
            state: 'Completed',
            downstreamStatus: `Corrected receipt ${corrected.receiptNumber ?? ''} issued and delivered`,
            nextAction: 'No further action. The original receipt remains in the audit trail.',
          });
          this.uiState.set('success');

          this.toast.show(
            'Receipt Corrected',
            `Receipt ${corrected.receiptNumber ?? ''} supersedes ${target.receiptReference}.`,
            'success',
          );

          this.loadRequests(corrected.receiptNumber ?? '');
        },
        error: (error) => this.reportFailure(error, 'The receipt could not be corrected.'),
      });
  }

  // ================= Deliver corrected receipt (§4.8.3) =================
  protected readonly deliverDialogOpen = signal(false);
  protected readonly deliverTarget = signal<RcrCorrectionRequest | null>(null);
  protected requestDeliver(r: RcrCorrectionRequest): void {
    this.closeOverflow();
    this.closeRowMenu();
    if (!this.deliverAllowed(r)) return;
    this.select(r.requestReference);
    this.deliverTarget.set(r);
    this.deliverDialogOpen.set(true);
  }
  protected closeDeliver(): void {
    this.deliverDialogOpen.set(false);
    this.deliverTarget.set(null);
  }
  /**
   * Sends the receipt to the donor again.
   *
   * SEPARATE FROM ISSUING, because they fail differently. The receipt is valid the moment it is
   * numbered and recorded; getting it into an inbox is a later step that can fail without making
   * it any less valid. Collapsing the two would make a bounced e-mail look like an unissued
   * receipt.
   */
  protected confirmDeliver(): void {
    const target = this.deliverTarget();

    if (!target || !this.deliverAllowed(target)) {
      return;
    }

    this.deliverDialogOpen.set(false);
    this.deliverTarget.set(null);

    this.paymentApi
      .resendReceipt(target.receiptId, { channel: target.deliveryChannel || 'Email' })
      .subscribe({
        next: () => {
          this.lastOutcome.set({
            reference: target.receiptReference,
            state: 'Completed',
            downstreamStatus: `Delivered via ${target.deliveryChannel || 'Email'}`,
            nextAction: 'No further action; the original receipt remains in the audit trail',
          });
          this.uiState.set('success');

          this.toast.show(
            'Receipt Delivered',
            `Receipt ${target.receiptReference} was sent to the donor.`,
            'success',
          );

          this.loadRequests(target.requestReference);
        },
        error: (error) => {
          // A delivery failure is a DEPENDENCY state, not an error state: the receipt is still
          // perfectly valid and can be sent again.
          this.uiState.set('dependency-failure');
          this.toast.show(
            'Delivery failed',
            apiErrorMessage(
              error,
              'The receipt could not be delivered. It remains valid and can be sent again.',
            ),
            'error',
          );
        },
      });
  }

  // ================= Reject request - danger (§4.8.3) =================
  protected readonly rejectDialogOpen = signal(false);
  protected readonly rejectTarget = signal<RcrCorrectionRequest | null>(null);
  protected readonly rejectReason = signal('');
  protected readonly rejectSubmitted = signal(false);
  protected readonly rejectReasonCount = computed(() => this.rejectReason().trim().length);
  protected readonly rejectReasonValid = computed(() => {
    const len = this.rejectReason().trim().length;
    return len >= this.reasonMin && len <= this.reasonMax;
  });
  protected requestReject(r: RcrCorrectionRequest): void {
    this.closeOverflow();
    this.closeRowMenu();
    if (!this.rejectAllowed(r)) return;
    this.select(r.requestReference);
    this.rejectTarget.set(r);
    this.rejectReason.set('');
    this.rejectSubmitted.set(false);
    this.rejectDialogOpen.set(true);
  }
  protected closeReject(): void {
    this.rejectDialogOpen.set(false);
    this.rejectTarget.set(null);
  }
  /**
   * Voids the receipt.
   *
   * THE NUMBER IS RETAINED, NOT REUSED. A gap in a receipt series reads to a tax authority as a
   * destroyed receipt, so a voided receipt keeps its number and its row and simply stops being
   * valid - which is also why this needs a named reason.
   */
  protected confirmReject(): void {
    this.rejectSubmitted.set(true);
    if (!this.rejectReasonValid()) return;

    const target = this.rejectTarget();
    if (!target || !this.rejectAllowed(target)) return;

    const reason = this.rejectReason().trim();

    this.rejectDialogOpen.set(false);
    this.rejectTarget.set(null);
    this.uiState.set('loading');

    this.paymentApi
      .voidReceipt(target.receiptId, { expectedVersion: target.version, reason })
      .subscribe({
        next: () => {
          this.lastOutcome.set({
            reference: target.receiptReference,
            state: 'Rejected',
            downstreamStatus: 'Receipt voided; its number is retained in the series',
            nextAction: 'Issue a fresh receipt against the donation if one is still due',
          });
          this.uiState.set('success');

          this.toast.show(
            'Receipt Voided',
            `Receipt ${target.receiptReference} has been voided. Its number is retained.`,
            'warning',
          );

          this.loadRequests(target.requestReference);
        },
        error: (error) => this.reportFailure(error, 'The receipt could not be voided.'),
      });
  }

  /**
   * Reports a failed write.
   *
   * A RECEIPT THAT CANNOT BE CORRECTED IS NAMED SEPARATELY: a voided receipt, or one whose
   * donation was charged back, is not correctable at all, and telling somebody that plainly is
   * more use than a generic failure they will retry.
   */
  private reportFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    this.uiState.set('ready');

    if (code === 'RECEIPT_NOT_CORRECTABLE') {
      this.toast.show(
        'Not correctable',
        apiErrorMessage(error, 'This receipt can no longer be corrected.'),
        'warning',
      );
      return;
    }

    if (code === 'CONCURRENCY_CONFLICT') {
      this.toast.show(
        'Receipt changed',
        'Somebody else changed this receipt. Refreshing so you can look again.',
        'warning',
      );
      this.loadRequests();
      return;
    }

    this.toast.show('Action failed', apiErrorMessage(error, fallback), 'error');
  }

  // ================= UI state + persistent outcome (§4.8.1 / §4.8.4) =================
  protected readonly uiState = signal<RcrUiState>('loading');
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

  protected readonly persistentOutcome = computed<RcrPersistentOutcome>(() => {
    const outcome = this.lastOutcome();
    if (outcome) {
      return { ...outcome, effectiveTime: this.lastRefresh(), owner: this.owner() };
    }
    const r = this.selectedRecord();
    return {
      reference: r?.requestReference ?? '—',
      state: r?.status ?? '—',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: r?.downstreamStatus ?? 'No pending action',
      owner: r?.requestedBy ?? this.owner(),
      nextAction: r ? this.nextActionFor(r) : 'Select a request to review its correction',
    };
  });

  protected nextActionFor(r: RcrCorrectionRequest): string {
    switch (r.status) {
      case 'Pending Approval':
        return 'Review the difference, then approve the reissue or reject the request';
      case 'Approved':
        return 'Correct, re-send or void this receipt';
      case 'Completed':
        return 'No further action; the original receipt remains in the audit trail';
      case 'Rejected':
        return 'The receipt is void; issue a fresh one if a receipt is still due';
      case 'Draft':
        return 'Issue the receipt from the donation before it can be corrected';
      default:
        return 'Review the request';
    }
  }

  // ================= Helpers =================
  private byReceiptNumber(number: string): RcrCorrectionRequest | undefined {
    const wanted = number.trim().toLowerCase();
    return this.records().find((r) => r.receiptReference.toLowerCase() === wanted);
  }

  protected formatDate(iso: string): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  protected statusClass(status: RcrCorrectionStatus): string {
    switch (status) {
      case 'Pending Approval':
        return 'rcr-badge-gold';
      case 'Approved':
        return 'rcr-badge-blue';
      case 'Completed':
        return 'rcr-badge-good';
      case 'Rejected':
        return 'rcr-badge-danger';
      case 'Draft':
        return 'rcr-badge-muted';
      default:
        return 'rcr-badge-muted';
    }
  }

  constructor() {
    if (!this.tokens.hasAnyPermission('pay.receipts.view')) {
      this.uiState.set('no-access');
      this.loading.set(false);
      return;
    }

    this.loadRequests();
  }

  /** The screen's correction vocabulary, from the receipt's own status. */
  private toCorrectionStatus(status: ReceiptStatus): RcrCorrectionStatus {
    switch (status) {
      case 'draft':
        return 'Draft';
      case 'submitted':
      case 'pendingReview':
        return 'Pending Approval';
      case 'issued':
        return 'Approved';
      case 'corrected':
        return 'Completed';
      case 'voided':
        return 'Rejected';
      default:
        return 'Draft';
    }
  }

  /** Which filter status, if any, narrows the server-side search. */
  private toApiStatus(status: RcrCorrectionStatus | ''): ReceiptStatus | null {
    switch (status) {
      case 'Draft':
        return 'draft';
      case 'Pending Approval':
        return 'pendingReview';
      case 'Approved':
        return 'issued';
      case 'Completed':
        return 'corrected';
      case 'Rejected':
        return 'voided';
      default:
        return null;
    }
  }

  /**
   * One receipt, as this screen reads it.
   *
   * A receipt that SUPERSEDES another is itself the product of a correction, so the category
   * says so; anything else is an original with no correction against it yet.
   */
  private toRequest(item: ReceiptListItem): RcrCorrectionRequest {
    const isCorrection = !!item.supersedesReceiptId;
    const status = this.toCorrectionStatus(item.issueState);
    const reference = item.receiptNumber ?? item.donationReference;
    const lastDelivery = item.deliveryHistory[item.deliveryHistory.length - 1] ?? null;

    return {
      receiptId: item.id,
      requestReference: reference,
      correctionCategory: isCorrection ? 'Reissue (Duplicate)' : 'Donor Name Correction',
      receiptReference: item.receiptNumber ?? '—',
      newReceiptReference: '',
      donationReference: item.donationReference,
      donorName: item.donorSnapshot,
      currentValue: `${item.donorSnapshot} · ${item.amount.display}`,
      proposedValue: isCorrection ? `${item.donorSnapshot} · ${item.amount.display}` : '—',
      currentVersion: item.versionNumber,
      status,
      requestedAtIso: item.issuedAtUtc ?? '',
      requestedAtLabel: item.issuedAtUtc ? formatMoment(item.issuedAtUtc) : 'Not yet issued',
      requestedBy: this.owner(),
      reason: '',
      supportingEvidence: item.documentUrl
        ? [
            {
              name: `${item.receiptNumber ?? item.donationReference}.pdf`,
              classification: 'Confidential',
              status: 'Available',
            },
          ]
        : [],
      approver: this.owner(),
      deliveryChannel: lastDelivery?.channel ?? 'Email',
      version: item.version,
      hasDownstreamReference: isCorrection,
      downstreamStatus: isCorrection
        ? 'This receipt supersedes an earlier one, which is retained as issued'
        : status === 'Completed'
          ? 'Superseded by a corrected receipt'
          : status === 'Rejected'
            ? 'Voided; its number is retained in the series'
            : 'No correction outstanding',
      history: item.deliveryHistory.map((delivery) => ({
        label: `Delivery · ${delivery.channel}`,
        detail: delivery.statusDescription,
        meta: formatMoment(delivery.attemptedAtUtc),
      })),
      linkedRecords: [
        { reference: item.donationReference, kind: 'Donation' },
        ...(item.campaignOrFundName
          ? [{ reference: item.campaignOrFundName, kind: 'Campaign or fund' }]
          : []),
      ],
      documents: item.documentUrl
        ? [
            {
              name: `${item.receiptNumber ?? item.donationReference}.pdf`,
              classification: 'Confidential',
            },
          ]
        : [],
      integrationStatus: {
        provider: 'Receipt delivery',
        state: lastDelivery?.statusDescription ?? 'Not sent',
      },
      supportCorrelation: {
        reference: item.donationReference,
        state: status === 'Rejected' ? 'Voided' : 'No open case',
      },
    };
  }

  private loadRequests(keepSelected?: string): void {
    this.loading.set(true);
    this.loadError.set(false);

    const search = this.searchTerm().trim() || this.receiptFilter().trim();

    const filter: ReceiptSearchFilter = {
      page: 1,
      pageSize: ReceiptCorrectionAndReissueComponent.FETCH_SIZE,
      search: search || undefined,
      issueState: this.toApiStatus(this.statusFilter()),
      issuedFromUtc: this.rangeStart() ? new Date(this.rangeStart()).toISOString() : null,
      issuedToUtc: this.rangeEnd()
        ? new Date(`${this.rangeEnd()}T23:59:59`).toISOString()
        : null,
    };

    this.paymentApi.searchReceipts(filter).subscribe({
      next: (page) => {
        const rows = (page.items ?? []).map((item) => this.toRequest(item));
        this.records.set(rows);
        this.serverTotal.set(page.totalCount ?? rows.length);
        this.lastRefresh.set(formatMoment(new Date().toISOString()));
        this.loading.set(false);

        const stillThere = keepSelected && rows.some((r) => r.requestReference === keepSelected);
        if (stillThere) {
          this.selectedRef.set(keepSelected!);
          this.detailOpen.set(true);
        } else if (!keepSelected) {
          this.selectedRef.set('');
          this.detailOpen.set(false);
        }

        if (this.uiState() !== 'success' && this.uiState() !== 'no-access') {
          this.uiState.set('ready');
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
          apiErrorMessage(error, 'The correction register could not be loaded.'),
          'error',
        );
      },
    });
  }
}

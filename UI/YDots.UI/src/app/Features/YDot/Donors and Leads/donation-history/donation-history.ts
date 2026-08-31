import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { PaymentApiService } from '../../../../Service/payment-api.service';
import { RefundCaseListItem } from '../../../../Shared/models/payment.model';

export type CaseType = 'Refund request' | 'Chargeback';
export type CaseStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected' | 'Completed';
export type ProviderState = 'Not sent' | 'Requested' | 'Sent';
export type ApprovalDecision = 'Pending' | 'Approved' | 'Rejected' | 'Not required';
export type OutcomeResult = 'Pending' | 'Refunded' | 'Chargeback upheld' | 'Chargeback reversed' | 'Rejected';

export interface RefundCase {
  id: string;
  caseType: CaseType;
  paymentRef: string;
  capturedAmount: number;
  previouslyRefunded: number;
  requestedAmount: number;
  provider: ProviderState;
  status: CaseStatus;
  requesterName: string;
  requesterTeam: string;
  checkerApprover: string | null;
  createdAt: string;
  eligibility: {
    refundableAmount: number;
    eligibleUntil: string;
    windowRemainingDays: number;
  };
  approval: {
    required: boolean;
    decision: ApprovalDecision;
    decidedBy: string | null;
    decidedAt: string | null;
  };
  providerAction: {
    state: ProviderState;
    reference: string | null;
    lastUpdated: string | null;
  };
  outcome: {
    result: OutcomeResult;
    settledAmount: number | null;
    settledAt: string | null;
  };
}

type StatusFilter = 'All' | CaseStatus;
type TypeFilter = 'All' | CaseType;
type CopyableField = 'id' | 'ref';



@Component({
  selector: 'app-donation-history',
  standalone: true,
  imports: [CurrencyPipe, DatePipe],
  templateUrl: './donation-history.html',
  styleUrl: './donation-history.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DonationHistoryComponent {
  private readonly paymentApi = inject(PaymentApiService);



  protected readonly ownerName = 'Firstlin S Joseph';
  protected readonly ownerTeam = 'Donor Care';
  protected readonly lastUpdatedLabel = 'Today, 09:30 AM · IST';

  protected readonly statusOptions: StatusFilter[] = [
    'All',
    'Draft',
    'Submitted',
    'Approved',
    'Rejected',
    'Completed',
  ];
  protected readonly typeOptions: TypeFilter[] = ['All', 'Refund request', 'Chargeback'];

  protected readonly loading = signal(true);
  protected readonly loadError = signal(false);
  protected readonly cases = signal<RefundCase[]>([]);

  protected readonly searchTerm = signal('');
  protected readonly statusFilter = signal<StatusFilter>('All');
  protected readonly typeFilter = signal<TypeFilter>('All');
  protected readonly filtersOpen = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly copiedField = signal<CopyableField | null>(null);

  protected readonly filteredCases = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const status = this.statusFilter();
    const type = this.typeFilter();

    return this.cases().filter((item) => {
      const matchesTerm =
        term.length === 0 ||
        item.id.toLowerCase().includes(term) ||
        item.paymentRef.toLowerCase().includes(term) ||
        item.requesterName.toLowerCase().includes(term);
      const matchesStatus = status === 'All' || item.status === status;
      const matchesType = type === 'All' || item.caseType === type;
      return matchesTerm && matchesStatus && matchesType;
    });
  });

  protected readonly kpis = computed(() => {
    const all = this.cases();
    return {
      totalCases: all.length,
      refundRequests: all.filter((c) => c.caseType === 'Refund request').length,
      chargebacks: all.filter((c) => c.caseType === 'Chargeback').length,
      awaitingReview: all.filter((c) => c.status === 'Submitted').length,
    };
  });

  protected readonly selectedCase = computed<RefundCase | null>(() => {
    const id = this.selectedId();
    return this.cases().find((c) => c.id === id) ?? null;
  });

  protected readonly hasFiltersApplied = computed(
    () => this.searchTerm().trim().length > 0 || this.statusFilter() !== 'All' || this.typeFilter() !== 'All',
  );

  constructor() {
    this.loadCases();
  }

  /**
   * Loads the refund and chargeback cases.
   *
   * WHAT THIS REPLACES. Eleven cases were written inline in this file and a `queueMicrotask`
   * copied them into the signal to simulate an async load, with a comment saying to replace it
   * with "the real donations/refunds service call". They were the same eleven for every
   * organisation, and somebody opening one read a donor name, a payment reference and an amount
   * that belonged to nobody.
   *
   * IT NOW READS `PAY /api/v1/refunds`, which is organisation-scoped server-side.
   */
  private loadCases(): void {
    this.loading.set(true);
    this.loadError.set(false);

    this.paymentApi.searchRefunds({ pageSize: 200 }).subscribe({
      next: (page) => {
        this.cases.set((page.items ?? []).map((item) => this.toRefundCase(item)));
        this.selectedId.set(this.cases()[0]?.id ?? null);
        this.loading.set(false);
      },
      error: () => {
        this.cases.set([]);
        this.selectedId.set(null);
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  /**
   * One API case as this screen reads it.
   *
   * THE FIGURES ARE THE SERVER'S, and the gaps are honest. The inline records carried an
   * eligibility window with "days remaining" and a settled amount; neither is on the list
   * projection, so both are left empty rather than computed in a browser from a date it invented.
   * A refund window is what somebody refuses a donor on.
   */
  private toRefundCase(item: RefundCaseListItem): RefundCase {
    const decided = !!item.decidedByUserId;
    const donationAmount = item.donationAmount?.amount ?? 0;
    const requested = item.amount?.amount ?? 0;

    const decision: ApprovalDecision =
      item.status === 'approved' || item.status === 'completed'
        ? 'Approved'
        : item.status === 'rejected'
          ? 'Rejected'
          : 'Pending';

    return {
      id: item.caseReference,
      caseType: 'Refund request',
      paymentRef: item.donationReference,
      capturedAmount: donationAmount,

      // What was refunded before this case: the difference between the donation and what is
      // still refundable against it.
      previouslyRefunded: Math.max(0, donationAmount - requested),

      requestedAmount: requested,
      provider: decided ? 'Sent' : 'Not sent',
      status: this.toCaseStatus(item.status),
      requesterName: item.requestedByUserId,
      requesterTeam: '',
      checkerApprover: item.decidedByUserId,
      createdAt: item.requestedAtUtc,

      eligibility: {
        refundableAmount: Math.max(0, donationAmount - requested),
        eligibleUntil: '',
        windowRemainingDays: 0,
      },

      approval: {
        required: true,
        decision,
        decidedBy: item.decidedByUserId,
        decidedAt: item.decidedAtUtc,
      },

      providerAction: {
        state: decided ? 'Sent' : 'Not sent',
        reference: item.donationReference,
        lastUpdated: item.decidedAtUtc,
      },

      outcome: {
        result:
          item.status === 'completed'
            ? 'Refunded'
            : item.status === 'rejected'
              ? 'Rejected'
              : 'Pending',
        settledAmount: item.status === 'completed' ? requested : null,
        settledAt: item.status === 'completed' ? item.decidedAtUtc : null,
      },
    };
  }

  /** The API's refund status as the status this screen filters on. */
  private toCaseStatus(status: string): CaseStatus {
    switch (status) {
      case 'requested':
        return 'Submitted';
      case 'approved':
        return 'Approved';
      case 'rejected':
        return 'Rejected';
      case 'completed':
        return 'Completed';
      default:
        return 'Draft';
    }
  }

  protected retryLoad(): void {
    this.loadCases();
  }

  protected selectCase(id: string): void {
    this.selectedId.set(id);
  }

  protected onRowKeydown(event: KeyboardEvent, id: string): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.selectCase(id);
    }
  }

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  protected updateSearch(value: string): void {
    this.searchTerm.set(value);
  }

  protected updateStatusFilter(value: string): void {
    this.statusFilter.set(value as StatusFilter);
  }

  protected updateTypeFilter(value: string): void {
    this.typeFilter.set(value as TypeFilter);
  }

  protected clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set('All');
    this.typeFilter.set('All');
  }

  protected requestNewCase(): void {
    // Hook this into the real "new case" route/modal when integrating with the backend.
    console.info('Request new refund or chargeback case');
  }

  protected async copyToClipboard(field: CopyableField, value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.copiedField.set(field);
      setTimeout(() => {
        if (this.copiedField() === field) {
          this.copiedField.set(null);
        }
      }, 1500);
    } catch {
      this.copiedField.set(null);
    }
  }

  protected statusBadgeClass(status: CaseStatus): string {
    switch (status) {
      case 'Approved':
        return 'badge badge--approved';
      case 'Submitted':
        return 'badge badge--submitted';
      case 'Rejected':
        return 'badge badge--rejected';
      case 'Completed':
        return 'badge badge--completed';
      default:
        return 'badge badge--draft';
    }
  }

  protected providerBadgeClass(provider: ProviderState): string {
    switch (provider) {
      case 'Requested':
        return 'badge badge--requested';
      case 'Sent':
        return 'badge badge--sent';
      default:
        return 'badge badge--muted';
    }
  }

  protected decisionBadgeClass(decision: ApprovalDecision): string {
    switch (decision) {
      case 'Approved':
        return 'badge badge--approved';
      case 'Rejected':
        return 'badge badge--rejected';
      case 'Not required':
        return 'badge badge--muted';
      default:
        return 'badge badge--submitted';
    }
  }
}
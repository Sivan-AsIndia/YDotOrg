import {
  Component,
  HostListener,
  computed,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { DonorListItem } from '../../../../Shared/models/donor-contract.model';

/** Donor record as surfaced from the Donation & Payments module. */
export interface Donor {
  donorId: string;
  name: string;
  mobile: string;
  email: string;
  location: string;
  region: string;
  campaign: string;
  owner: string;
  ownerInitials: string;
  ownerColor: string;
  reference: string;
  lastDonationAmount: number;
  lastDonationDate: string;
  lifetimeGiving: number;
  followUpStatus: FollowUpStatus;
  consentStatus: ConsentStatus;
  verificationStatus: VerificationStatus;
  engagementTag: EngagementTag;
  consentReviewRequired: boolean;
  createdDate: string;
}

export type FollowUpStatus = 'Due Today' | 'Tomorrow' | 'Overdue' | 'None';
export type ConsentStatus = 'Full Consent' | 'Partial' | 'Do Not Contact';
export type VerificationStatus = 'Verified' | 'Pending' | 'Failed' | 'Expired';
export type EngagementTag =
  | 'High Potential'
  | 'Follow-Up Due'
  | 'No Contact'
  | 'Dormant'
  | 'Recently Active';

type SortableColumn =
  | 'name'
  | 'lastDonationDate'
  | 'lifetimeGiving'
  | 'campaign'
  | 'owner';
type SortDirection = 'asc' | 'desc';
type ExportFormat = 'excel' | 'csv' | 'pdf';

interface Kpi {
  key: string;
  label: string;
  value: number;
  hint: string;
}

const ENGAGEMENT_TAGS: EngagementTag[] = [
  'High Potential',
  'Follow-Up Due',
  'No Contact',
  'Dormant',
  'Recently Active',
];
const VERIFICATION_TAGS: VerificationStatus[] = [
  'Verified',
  'Pending',
  'Failed',
  'Expired',
];
const CONSENT_TAGS: ConsentStatus[] = [
  'Full Consent',
  'Partial',
  'Do Not Contact',
];

@Component({
  selector: 'app-donor-list',
  imports: [],
  templateUrl: './donor-list.html',
  styleUrl: './donor-list.css',
})
export class DonorListComponent {
  /** ----- Raw data + async state ----- */
  /**
   * The donor records.
   *
   * FROM `GET /api/v1/donors`. The document describes this list as the destination of a
   * conversion: "the lead becomes a donor, is removed from the Lead Work Queue and is added to
   * the Donor List". That only works if the list reads the same store the conversion writes to.
   */
  protected readonly donors = signal<Donor[]>([]);
  protected readonly loading = signal<boolean>(true);
  protected readonly error = signal<string | null>(null);
  protected readonly lastRefreshed = signal<Date>(new Date());

  /** ----- Search + filters ----- */
  protected readonly searchTerm = signal<string>('');
  protected readonly filterPanelOpen = signal<boolean>(false);
  protected readonly ownerFilter = signal<string>('all');
  protected readonly campaignFilter = signal<string>('all');
  protected readonly regionFilter = signal<string>('all');
  protected readonly engagementFilter = signal<EngagementTag | 'all'>('all');
  protected readonly verificationFilter = signal<VerificationStatus | 'all'>(
    'all',
  );
  protected readonly consentFilter = signal<ConsentStatus | 'all' | 'review'>(
    'all',
  );

  /** ----- Sorting ----- */
  protected readonly sortColumn = signal<SortableColumn>('lastDonationDate');
  protected readonly sortDirection = signal<SortDirection>('desc');

  /** ----- Selection + drawer + menus ----- */
  protected readonly selectedIds = signal<Set<string>>(new Set());
  protected readonly previewDonorId = signal<string | null>(null);
  protected readonly openMoreMenuId = signal<string | null>(null);
  protected readonly exportMenuOpen = signal<boolean>(false);

  /** ----- Pagination ----- */
  protected readonly currentPage = signal<number>(1);
  protected readonly pageSize = signal<number>(5);
  protected readonly pageSizeOptions = [5, 10, 25, 50];

  protected readonly engagementTags = ENGAGEMENT_TAGS;
  protected readonly verificationTags = VERIFICATION_TAGS;
  protected readonly consentTags = CONSENT_TAGS;

  constructor(
    private readonly router: Router,
    private readonly api: DonorApiService,
  ) {
    this.loadDonors();
  }

  /**
   * Loads the donors.
   *
   * IT USED TO `fetch('/assets/data/donors.json')` - a static file served from the bundle, with a
   * comment saying to swap the URL "once the real endpoint is available". Three things followed:
   * every organisation saw the same donors, the contact columns were never masked because a file
   * cannot check a permission, and a donor created by a donation never appeared here at all.
   */
  private loadDonors(): void {
    this.loading.set(true);
    this.error.set(null);

    this.api.searchDonors({ page: 1, pageSize: 200 }).subscribe({
      next: (page) => {
        this.donors.set(page.items.map((row) => this.toDonor(row)));
        this.lastRefreshed.set(new Date());
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.error.set(apiErrorMessage(error));
        this.loading.set(false);
      },
    });
  }

  private toDonor(row: DonorListItem): Donor {
    const owner = row.relationshipOwnerName ?? 'Unassigned';

    return {
      donorId: row.id,
      reference: row.displayCode,
      name: row.displayName,

      // ALREADY MASKED, OR ALREADY NOT. `isContactMasked` is the server's decision.
      mobile: row.mobileNumber ?? '',
      email: row.emailAddress ?? '',
      location: '',
      region: '',
      campaign: row.campaignName ?? '',
      owner,
      ownerInitials: this.initialsFor(owner),
      ownerColor: this.colourFor(owner),
      lastDonationAmount: row.lastDonationAmount ?? 0,
      lastDonationDate: row.lastDonationAtUtc ?? '',
      lifetimeGiving: row.lifetimeGiving,
      followUpStatus: row.followUpStatus as FollowUpStatus,
      consentStatus: this.toConsentStatus(row.consentStatus),
      verificationStatus: row.verificationStatus as VerificationStatus,
      engagementTag: this.toEngagementTag(row),
      consentReviewRequired: row.consentReviewRequired,
      createdDate: row.updatedAtUtc,
    };
  }

  /** The screen's three consent words, from the server's four. */
  private toConsentStatus(state: string): ConsentStatus {
    switch (state) {
      case 'Granted': return 'Full Consent';
      case 'Partial': return 'Partial';
      default: return 'Do Not Contact';
    }
  }

  /**
   * The engagement badge.
   *
   * DERIVED FROM FACTS THE SERVER SENT, in a fixed order so the same donor always gets the same
   * badge. It was a stored string in the JSON file, which meant it could disagree with every
   * other column on the row.
   */
  private toEngagementTag(row: DonorListItem): EngagementTag {
    if (row.followUpStatus === 'Overdue' || row.followUpStatus === 'Due Today') {
      return 'Follow-Up Due';
    }
    if (row.lifetimeGiving >= 100000) {
      return 'High Potential';
    }
    if (row.lastDonationAtUtc === null) {
      return 'No Contact';
    }
    return 'Dormant';
  }

  private initialsFor(name: string): string {
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return '??';
    if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  }

  /** Deterministic per owner, so the same person is the same colour on every row. */
  private colourFor(name: string): string {
    const palette = ['#2d6a4f', '#3b82c4', '#b45309', '#6d28d9', '#0f766e', '#c53030'];
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    return palette[Math.abs(hash) % palette.length];
  }

  /** ----- Derived filter option lists ----- */
  protected readonly ownerOptions = computed(() =>
    this.uniqueSorted(this.donors().map((d) => d.owner)),
  );
  protected readonly campaignOptions = computed(() =>
    this.uniqueSorted(this.donors().map((d) => d.campaign)),
  );
  protected readonly regionOptions = computed(() =>
    this.uniqueSorted(this.donors().map((d) => d.region)),
  );

  /** ----- KPI cards ----- */
  protected readonly kpis = computed<Kpi[]>(() => {
    const list = this.donors();
    const now = new Date();
    const thirtyDaysAgo = new Date(now);
    thirtyDaysAgo.setDate(now.getDate() - 30);

    const newDonors = list.filter(
      (d) => new Date(d.createdDate) >= thirtyDaysAgo,
    ).length;
    const activeDonors = list.filter(
      (d) => d.engagementTag === 'Recently Active',
    ).length;
    const followUpsDue = list.filter((d) =>
      ['Due Today', 'Tomorrow', 'Overdue'].includes(d.followUpStatus),
    ).length;
    const verificationPending = list.filter(
      (d) => d.verificationStatus === 'Pending',
    ).length;
    const consentReviewDue = list.filter((d) => d.consentReviewRequired).length;

    return [
      {
        key: 'total',
        label: 'Total Donors',
        value: list.length,
        hint: 'Total donor records',
      },
      {
        key: 'new',
        label: 'New Donors',
        value: newDonors,
        hint: 'Created within selected period',
      },
      {
        key: 'active',
        label: 'Active Donors',
        value: activeDonors,
        hint: 'Recent donation activity',
      },
      {
        key: 'followups',
        label: 'Follow-Ups Due',
        value: followUpsDue,
        hint: 'Donors needing engagement',
      },
      {
        key: 'verification',
        label: 'Verification Pending',
        value: verificationPending,
        hint: 'Donor identity still pending',
      },
      {
        key: 'consent',
        label: 'Consent Review Due',
        value: consentReviewDue,
        hint: 'Consent requires attention',
      },
    ];
  });

  /** ----- Search + filter + sort pipeline ----- */
  protected readonly filteredDonors = computed<Donor[]>(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const owner = this.ownerFilter();
    const campaign = this.campaignFilter();
    const region = this.regionFilter();
    const engagement = this.engagementFilter();
    const verification = this.verificationFilter();
    const consent = this.consentFilter();

    let result = this.donors().filter((donor) => {
      const matchesSearch =
        term.length === 0 ||
        [
          donor.donorId,
          donor.name,
          donor.mobile,
          donor.email,
          donor.campaign,
          donor.reference,
        ].some((field) => field.toLowerCase().includes(term));

      const matchesOwner = owner === 'all' || donor.owner === owner;
      const matchesCampaign = campaign === 'all' || donor.campaign === campaign;
      const matchesRegion = region === 'all' || donor.region === region;
      const matchesEngagement =
        engagement === 'all' || donor.engagementTag === engagement;
      const matchesVerification =
        verification === 'all' || donor.verificationStatus === verification;
      const matchesConsent =
        consent === 'all' ||
        (consent === 'review'
          ? donor.consentReviewRequired
          : donor.consentStatus === consent);

      return (
        matchesSearch &&
        matchesOwner &&
        matchesCampaign &&
        matchesRegion &&
        matchesEngagement &&
        matchesVerification &&
        matchesConsent
      );
    });

    const col = this.sortColumn();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    result = [...result].sort((a, b) => {
      switch (col) {
        case 'name':
          return a.name.localeCompare(b.name) * dir;
        case 'campaign':
          return a.campaign.localeCompare(b.campaign) * dir;
        case 'owner':
          return a.owner.localeCompare(b.owner) * dir;
        case 'lifetimeGiving':
          return (a.lifetimeGiving - b.lifetimeGiving) * dir;
        case 'lastDonationDate':
        default:
          return (
            (new Date(a.lastDonationDate).getTime() -
              new Date(b.lastDonationDate).getTime()) *
            dir
          );
      }
    });

    return result;
  });

  protected readonly totalRecords = computed(() => this.filteredDonors().length);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalRecords() / this.pageSize())),
  );

  protected readonly paginatedDonors = computed<Donor[]>(() => {
    const page = Math.min(this.currentPage(), this.totalPages());
    const size = this.pageSize();
    const start = (page - 1) * size;
    return this.filteredDonors().slice(start, start + size);
  });

  protected readonly rangeStart = computed(() =>
    this.totalRecords() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize() + 1,
  );
  protected readonly rangeEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize(), this.totalRecords()),
  );

  protected readonly hasActiveFilters = computed(
    () =>
      this.ownerFilter() !== 'all' ||
      this.campaignFilter() !== 'all' ||
      this.regionFilter() !== 'all' ||
      this.engagementFilter() !== 'all' ||
      this.verificationFilter() !== 'all' ||
      this.consentFilter() !== 'all',
  );

  protected readonly previewDonor = computed<Donor | null>(() => {
    const id = this.previewDonorId();
    if (!id) return null;
    return this.donors().find((d) => d.donorId === id) ?? null;
  });

  protected readonly allOnPageSelected = computed(() => {
    const page = this.paginatedDonors();
    if (page.length === 0) return false;
    const selected = this.selectedIds();
    return page.every((d) => selected.has(d.donorId));
  });

  /** ----- Search + filter actions ----- */
  protected onSearchInput(value: string): void {
    this.searchTerm.set(value);
    this.currentPage.set(1);
  }

  protected resetSearch(): void {
    this.searchTerm.set('');
    this.currentPage.set(1);
  }

  protected toggleFilterPanel(): void {
    this.filterPanelOpen.update((open) => !open);
  }

  protected setOwnerFilter(value: string): void {
    this.ownerFilter.set(value);
    this.currentPage.set(1);
  }

  protected setCampaignFilter(value: string): void {
    this.campaignFilter.set(value);
    this.currentPage.set(1);
  }

  protected setRegionFilter(value: string): void {
    this.regionFilter.set(value);
    this.currentPage.set(1);
  }

  protected toggleEngagementFilter(tag: EngagementTag): void {
    this.engagementFilter.set(this.engagementFilter() === tag ? 'all' : tag);
    this.currentPage.set(1);
  }

  protected toggleVerificationFilter(tag: VerificationStatus): void {
    this.verificationFilter.set(
      this.verificationFilter() === tag ? 'all' : tag,
    );
    this.currentPage.set(1);
  }

  protected toggleConsentFilter(tag: ConsentStatus | 'review'): void {
    this.consentFilter.set(this.consentFilter() === tag ? 'all' : tag);
    this.currentPage.set(1);
  }

  protected clearFilters(): void {
    this.ownerFilter.set('all');
    this.campaignFilter.set('all');
    this.regionFilter.set('all');
    this.engagementFilter.set('all');
    this.verificationFilter.set('all');
    this.consentFilter.set('all');
    this.currentPage.set(1);
  }

  protected clearAll(): void {
    this.clearFilters();
    this.resetSearch();
  }

  /** ----- Sorting ----- */
  protected toggleSort(column: SortableColumn): void {
    if (this.sortColumn() === column) {
      this.sortDirection.update((dir) => (dir === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('asc');
    }
  }

  protected sortIndicator(column: SortableColumn): 'asc' | 'desc' | 'none' {
    return this.sortColumn() === column ? this.sortDirection() : 'none';
  }

  /** ----- Selection ----- */
  protected toggleRowSelection(donorId: string, event: Event): void {
    event.stopPropagation();
    this.selectedIds.update((current) => {
      const next = new Set(current);
      if (next.has(donorId)) {
        next.delete(donorId);
      } else {
        next.add(donorId);
      }
      return next;
    });
    this.previewDonorId.set(donorId);
  }

  protected toggleSelectAllOnPage(): void {
    const page = this.paginatedDonors();
    const allSelected = this.allOnPageSelected();
    this.selectedIds.update((current) => {
      const next = new Set(current);
      for (const donor of page) {
        if (allSelected) {
          next.delete(donor.donorId);
        } else {
          next.add(donor.donorId);
        }
      }
      return next;
    });
  }

  protected isSelected(donorId: string): boolean {
    return this.selectedIds().has(donorId);
  }

  /** ----- Preview drawer ----- */
  protected closePreview(): void {
    this.previewDonorId.set(null);
  }

  /** ----- Navigation / row actions ----- */
  protected openDonor360(donor: Donor, event?: Event): void {
    event?.stopPropagation();
    this.router.navigate(['/app/fundraising/relationships/donor-360'], {
      queryParams: { donorId: donor.donorId, tab: 'overview' },
    });
  }

  protected openDonations(donor: Donor, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/app/fundraising/relationships/donor-360'], {
      queryParams: { donorId: donor.donorId, tab: 'donations' },
    });
  }

  protected openCommunicationTimeline(donor: Donor, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], {
      queryParams: { donorId: donor.donorId },
    });
  }

  protected openFollowUpPlanner(donor: Donor, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/app/don/follow-up-planner'], {
      queryParams: { donorId: donor.donorId, mode: 'create' },
    });
  }

  protected openConsentCentre(donor: Donor, event?: Event): void {
    event?.stopPropagation();
    this.router.navigate(['/app/fundraising/relationships/consent-and-preference-centre'], {
      queryParams: { donorId: donor.donorId },
    });
  }

  protected openIdentityVerification(donor: Donor, event: Event): void {
    event.stopPropagation();
    this.router.navigate(['/app/don/donor-identity-verification'], {
      queryParams: { donorId: donor.donorId },
    });
  }

  protected exportDonorRecord(donor: Donor, event: Event): void {
    event.stopPropagation();
    this.downloadCsv([donor], `${donor.donorId}.csv`);
    this.openMoreMenuId.set(null);
  }

  protected onRowClick(donor: Donor): void {
    this.openDonor360(donor);
  }

  protected toggleMoreMenu(donorId: string, event: Event): void {
    event.stopPropagation();
    this.openMoreMenuId.set(this.openMoreMenuId() === donorId ? null : donorId);
  }

  /** ----- Header actions ----- */
  protected refresh(): void {
    this.loadDonors();
  }

  protected toggleExportMenu(): void {
    this.exportMenuOpen.update((open) => !open);
  }

  protected exportData(format: ExportFormat): void {
    const rows =
      this.selectedIds().size > 0
        ? this.donors().filter((d) => this.selectedIds().has(d.donorId))
        : this.filteredDonors();

    if (format === 'csv') {
      this.downloadCsv(rows, 'donor-list.csv');
    } else if (format === 'excel') {
      this.downloadExcel(rows, 'donor-list.xls');
    } else {
      this.printAsPdf(rows);
    }
    this.exportMenuOpen.set(false);
  }

  /** ----- Pagination ----- */
  protected goToPage(page: number): void {
    this.currentPage.set(Math.min(Math.max(1, page), this.totalPages()));
  }

  protected nextPage(): void {
    this.goToPage(this.currentPage() + 1);
  }

  protected previousPage(): void {
    this.goToPage(this.currentPage() - 1);
  }

  protected setPageSize(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
  }

  /** ----- Global escape handler for drawer / menus ----- */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.previewDonorId.set(null);
    this.openMoreMenuId.set(null);
    this.exportMenuOpen.set(false);
  }

  @HostListener('document:click')
  protected onDocumentClick(): void {
    this.openMoreMenuId.set(null);
    this.exportMenuOpen.set(false);
  }

  /** ----- Formatting helpers ----- */
  protected formatCurrency(value: number): string {
    return '\u20b9' + new Intl.NumberFormat('en-IN').format(value);
  }

  protected formatDate(value: string): string {
    return new Intl.DateTimeFormat('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    }).format(new Date(value));
  }

  protected formatDateTime(value: Date): string {
    return new Intl.DateTimeFormat('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(value);
  }

  private uniqueSorted(values: string[]): string[] {
    return Array.from(new Set(values)).sort((a, b) => a.localeCompare(b));
  }

  private downloadCsv(rows: Donor[], filename: string): void {
    const headers = [
      'Donor ID',
      'Donor Name',
      'Mobile',
      'Email',
      'Location',
      'Campaign',
      'Owner',
      'Last Donation Amount',
      'Last Donation Date',
      'Lifetime Giving',
      'Follow-Up Status',
      'Consent Status',
      'Verification Status',
    ];
    const lines = rows.map((d) =>
      [
        d.donorId,
        d.name,
        d.mobile,
        d.email,
        d.location,
        d.campaign,
        d.owner,
        d.lastDonationAmount,
        d.lastDonationDate,
        d.lifetimeGiving,
        d.followUpStatus,
        d.consentStatus,
        d.verificationStatus,
      ]
        .map((field) => `"${String(field).replace(/"/g, '""')}"`)
        .join(','),
    );
    const csvContent = [headers.join(','), ...lines].join('\r\n');
    this.triggerDownload(csvContent, filename, 'text/csv;charset=utf-8;');
  }

  private downloadExcel(rows: Donor[], filename: string): void {
    const headerRow =
      '<tr><th>Donor ID</th><th>Donor Name</th><th>Mobile</th><th>Email</th>' +
      '<th>Location</th><th>Campaign</th><th>Owner</th><th>Last Donation</th>' +
      '<th>Lifetime Giving</th><th>Follow-Up</th><th>Consent</th><th>Verification</th></tr>';
    const bodyRows = rows
      .map(
        (d) =>
          `<tr><td>${d.donorId}</td><td>${d.name}</td><td>${d.mobile}</td><td>${d.email}</td>` +
          `<td>${d.location}</td><td>${d.campaign}</td><td>${d.owner}</td><td>${d.lastDonationAmount}</td>` +
          `<td>${d.lifetimeGiving}</td><td>${d.followUpStatus}</td><td>${d.consentStatus}</td><td>${d.verificationStatus}</td></tr>`,
      )
      .join('');
    const table = `<table>${headerRow}${bodyRows}</table>`;
    this.triggerDownload(table, filename, 'application/vnd.ms-excel');
  }

  private printAsPdf(rows: Donor[]): void {
    const printWindow = window.open('', '_blank');
    if (!printWindow) return;
    const rowsHtml = rows
      .map(
        (d) =>
          `<tr><td>${d.donorId}</td><td>${d.name}</td><td>${d.campaign}</td>` +
          `<td>${this.formatCurrency(d.lifetimeGiving)}</td><td>${d.verificationStatus}</td></tr>`,
      )
      .join('');
    printWindow.document.write(`
      <html>
        <head>
          <title>Donor List</title>
          <style>
            body { font-family: Arial, sans-serif; padding: 24px; }
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ddd; padding: 8px; text-align: left; font-size: 12px; }
            th { background: #f8f9fa; }
          </style>
        </head>
        <body>
          <h2>Donor List</h2>
          <table>
            <thead><tr><th>Donor ID</th><th>Name</th><th>Campaign</th><th>Lifetime Giving</th><th>Verification</th></tr></thead>
            <tbody>${rowsHtml}</tbody>
          </table>
        </body>
      </html>
    `);
    printWindow.document.close();
    printWindow.focus();
    printWindow.print();
  }

  private triggerDownload(content: string, filename: string, mimeType: string): void {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  }
}
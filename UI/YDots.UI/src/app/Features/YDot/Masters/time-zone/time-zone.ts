import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage, apiFieldErrors } from '../../../../Shared/models/api-response.model';
import {
  MasterDataStatus,
  TimeZoneDetail,
  TimeZoneListItem,
  canPerform,
} from '../../../../Shared/models/global-master.model';
import { MasterService } from '../master.service';

/* ============================================================
   Models
   ============================================================ */

/**
 * One time zone, in the shape this screen's template binds to.
 *
 * THE COUNTRY FIELDS ARE GONE. A time zone has no country on this platform: Asia/Kolkata is one
 * zone whoever uses it, and the United States spans six. The relationship runs the other way -
 * a STATE names its default zone - so `countryId` and `countryName` existed only because the mock
 * data invented them, and every control bound to them showed a value no record supported.
 *
 * `stateUsageCount` REPLACES THEM as the real association, and it is the one that matters: it is
 * how many states default to this zone, and a non-zero count is why Delete is refused.
 */
export interface TimeZoneModel {
  id: string;
  tenantId?: string | null;
  /** The IANA identifier as written: `Asia/Kolkata`. */
  timeZoneKey: string;
  displayName: string;
  shortName?: string | null;
  standardUtcOffsetMinutes?: number | null;
  supportsDST: boolean;
  dstRuleNote?: string | null;
  sortOrder: number;
  isDefaultRecommended: boolean;
  status: string | null;
  isActive: boolean;
  notes?: string | null;
  createdAt: Date;
  createdBy?: string | null;
  updatedAt?: Date | null;
  updatedBy?: string | null;

  /** States defaulting to this zone. Non-zero is why Delete is refused. */
  stateUsageCount: number;

  /** A shared platform row. Read-only to an Organisation; only SuperAdmin may change it. */
  isPlatformRow: boolean;

  /** Sent back on the next write. A stale one answers 409 rather than overwriting somebody. */
  version: number;

  /** What the SERVER says this caller may do. */
  permittedActions: string[];
}

interface Toast {
  id: number;
  type: 'success' | 'error' | 'warning' | 'info';
  title: string;
  message: string;
}

type ViewMode = 'list' | 'form';

export const TimeZoneStatus = {
  Draft: 'Draft',
  Active: 'Active',
  Inactive: 'Inactive',
  All: ['Draft', 'Active', 'Inactive'] as const,
};

/** The display label the template shows, and the code the API takes. */
const STATUS_CODES: Record<string, MasterDataStatus> = {
  Draft: 'draft',
  Active: 'active',
  Inactive: 'inactive',
};

const STATUS_LABELS: Record<MasterDataStatus, string> = {
  draft: 'Draft',
  active: 'Active',
  inactive: 'Inactive',
};

/* ============================================================
   Component
   ============================================================ */

/**
 * The Time Zone master.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. Every row came from a `seedData()` method compiled into the
 * bundle, and every write mutated the resulting array: activating set a field, deleting spliced,
 * creating pushed a row with a browser-generated GUID. None of it survived a refresh, and every
 * Organisation saw the same fabricated list.
 *
 * It now reads and writes `IAM /api/v1/masters/timezones`, where the catalogue moved when the
 * GlobalMaster service was merged into IAM.
 *
 * THE SERVER DECIDES WHAT MAY BE DONE. `canDeactivate` and `canDelete` used to be
 * `!tz.isDefaultRecommended` - a local guess at a rule the server actually enforces differently:
 * what blocks a delete is STATES STILL USING THE ZONE, which the browser has no way of knowing.
 * Both now come from `permittedActions` and the usage count on the detail response.
 *
 * THE ARTIFICIAL DELAYS ARE GONE. `await this.delay(150)` before every action existed to make
 * an in-memory mutation feel like a network call. There is a real network call now, so the
 * spinner covers something.
 */
@Component({
  selector: 'app-time-zone',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './time-zone.html',
  styleUrl: './time-zone.css',
})
export class TimeZoneComponent implements OnInit {
  private readonly masters = inject(MasterService);
  private readonly destroyRef = inject(DestroyRef);

  /* ---------------- Page state ---------------- */
  isInitialized = false;
  isLoading = false;
  view: ViewMode = 'list';

  timeZones: TimeZoneModel[] = [];
  filteredTimeZones: TimeZoneModel[] = [];

  searchText = '';
  selectedStatus = '';
  selectedDST = '';

  currentPage = 1;
  pageSize = 10;
  pageWindowSize = 3;

  /** The server's totals. A page of ten cannot tell you how many zones the catalogue holds. */
  private totalCountFromServer = 0;
  private activeCountFromServer = 0;
  private inactiveCountFromServer = 0;
  private totalPagesFromServer = 1;

  selectedTimeZone: TimeZoneModel | null = null;

  /**
   * Whether the open zone may be deactivated or deleted.
   *
   * FROM THE SERVER, not guessed. The previous rule - `!isDefaultRecommended` - was wrong in both
   * directions: a recommended zone with no states using it CAN be deleted once the flag is
   * cleared, and a non-recommended zone with fifty states using it cannot.
   */
  canDeactivate = true;
  canDelete = true;

  showViewPanel = false;
  showActivateModal = false;
  showDeactivateModal = false;
  showDeleteModal = false;

  /* ---------------- Mobile "quick view" row expander ---------------- */
  rowDetailsTz: TimeZoneModel | null = null;
  showRowDetailsModal = false;

  toasts: Toast[] = [];
  private toastSeq = 1;

  readonly statusList = TimeZoneStatus.All;

  /* ---------------- Form state ---------------- */
  editingId: string | null = null;
  isEdit = false;
  formModel: TimeZoneModel = this.createNewTimeZone();
  statusValidationError: string | null = null;
  submitted = false;
  fieldErrors: { [key: string]: string } = {};

  get pageTitle(): string {
    return this.isEdit ? 'Edit Time Zone' : 'Create Time Zone';
  }

  get pageSubTitle(): string {
    return this.isEdit
      ? 'Update and manage time zone details and settings.'
      : 'Create a new time zone and configure its settings.';
  }

  /* ---------------- Derived / computed ---------------- */

  get totalPages(): number {
    return Math.max(1, this.totalPagesFromServer);
  }

  /** The server already paged the result, so this is the page it returned. */
  get pagedTimeZones(): TimeZoneModel[] {
    return this.filteredTimeZones;
  }

  get startPage(): number {
    const half = Math.floor(this.pageWindowSize / 2);
    let start = this.currentPage - half;

    if (start < 1) {
      start = 1;
    }

    if (start + this.pageWindowSize - 1 > this.totalPages) {
      start = Math.max(1, this.totalPages - this.pageWindowSize + 1);
    }

    return start;
  }

  get endPage(): number {
    return Math.min(this.startPage + this.pageWindowSize - 1, this.totalPages);
  }

  get pageNumbers(): number[] {
    const pages: number[] = [];

    for (let index = this.startPage; index <= this.endPage; index++) {
      pages.push(index);
    }

    return pages;
  }

  get totalCount(): number {
    return this.totalCountFromServer;
  }

  get activeCount(): number {
    return this.activeCountFromServer;
  }

  get inactiveCount(): number {
    return this.inactiveCountFromServer;
  }

  get rangeStart(): number {
    return this.totalCountFromServer === 0 ? 0 : (this.currentPage - 1) * this.pageSize + 1;
  }

  get rangeEnd(): number {
    return Math.min(this.currentPage * this.pageSize, this.totalCountFromServer);
  }

  /* ============================================================
     Lifecycle
     ============================================================ */

  constructor(private cdr: ChangeDetectorRef) {}

  ngOnInit(): void {
    this.loadData();
    this.isInitialized = true;
  }

  /**
   * Forces a render after state changed outside an Angular-bound event.
   *
   * Still needed: an HTTP callback is not a DOM event, so under zoneless change detection
   * nothing tells the view to re-check itself and the loading overlay - which sits over the whole
   * page and captures clicks - would never disappear.
   */
  private renderNow(): void {
    if (!(this.cdr as unknown as { destroyed?: boolean }).destroyed) {
      this.cdr.detectChanges();
    }
  }

  private createNewTimeZone(): TimeZoneModel {
    return {
      id: '',
      timeZoneKey: '',
      displayName: '',
      shortName: '',
      standardUtcOffsetMinutes: null,
      supportsDST: false,
      dstRuleNote: '',
      sortOrder: 0,
      isDefaultRecommended: false,
      status: null,
      isActive: false,
      notes: '',
      createdAt: new Date(),
      stateUsageCount: 0,
      isPlatformRow: false,
      version: 0,
      permittedActions: [],
    };
  }

  /* ============================================================
     Data loading / filtering
     ============================================================ */

  /**
   * Fetches one page from the API.
   *
   * THE FILTERS GO TO THE SERVER. Filtering in the browser can only filter what has been
   * downloaded - the current page - so a search for a zone on page three would have come back
   * empty and looked like a missing record.
   */
  private loadData(): void {
    this.isLoading = true;

    this.masters
      .searchTimeZones({
        page: this.currentPage,
        pageSize: this.pageSize,
        search: this.searchText.trim() || undefined,
        status: this.selectedStatus ? STATUS_CODES[this.selectedStatus] : undefined,
        supportsDaylightSaving: this.selectedDST ? this.selectedDST === 'yes' : undefined,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.timeZones = page.items.map((item) => this.toViewModel(item));
          this.filteredTimeZones = this.timeZones;
          this.totalCountFromServer = page.totalCount;
          this.totalPagesFromServer = page.totalPages;
          this.currentPage = page.page;
          this.isLoading = false;
          this.renderNow();
        },
        error: (error) => {
          this.timeZones = [];
          this.filteredTimeZones = [];
          this.isLoading = false;
          this.showToast(
            'error',
            'Could not load',
            apiErrorMessage(error, 'The time zone catalogue could not be loaded.'),
          );
          this.renderNow();
        },
      });

    this.masters
      .searchTimeZones({ pageSize: 1, status: 'active' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.activeCountFromServer = page.totalCount;
          this.renderNow();
        },
      });

    this.masters
      .searchTimeZones({ pageSize: 1, status: 'inactive' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.inactiveCountFromServer = page.totalCount;
          this.renderNow();
        },
      });
  }

  /** Every filter change restarts at page one - staying on page three of a narrower result is blank. */
  applyFilters(): void {
    this.currentPage = 1;
    this.loadData();
  }

  onSearchInput(value: string): void {
    this.searchText = value;
    this.applyFilters();
  }

  onStatusFilterChange(): void {
    this.applyFilters();
  }

  onDstFilterChange(): void {
    this.applyFilters();
  }

  onRefresh(): void {
    this.searchText = '';
    this.selectedStatus = '';
    this.selectedDST = '';
    this.currentPage = 1;

    // The reference-data cache goes too: a zone added or retired here appears in the State
    // form's dropdown, and a cached list would keep offering the old one.
    this.masters.invalidateReferenceData();
    this.loadData();
    this.showToast('info', 'Refresh', 'Data refreshed');
  }

  /* ============================================================
     Pagination
     ============================================================ */

  onPageSizeChange(value: string): void {
    this.pageSize = Number(value);
    this.currentPage = 1;
    this.loadData();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) {
      return;
    }

    this.currentPage = page;
    this.loadData();
  }

  previousPage(): void {
    if (this.currentPage > 1) {
      this.goToPage(this.currentPage - 1);
    }
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.goToPage(this.currentPage + 1);
    }
  }

  /* ============================================================
     Row helpers
     ============================================================ */

  formatOffset(minutes?: number | null): string {
    if (minutes === null || minutes === undefined) {
      return '—';
    }

    const hours = Math.trunc(minutes / 60);
    const remainder = Math.abs(minutes % 60);
    const sign = minutes >= 0 ? '+' : '-';

    return `UTC${sign}${String(Math.abs(hours)).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
  }

  statusBadgeClass(status: string | null): string {
    switch (status) {
      case 'Active':
        return 'badge-active';
      case 'Inactive':
        return 'badge-inactive';
      case 'Draft':
        return 'badge-draft';
      default:
        return 'badge-neutral';
    }
  }

  getPlainText(html?: string | null): string {
    if (!html) {
      return '—';
    }

    return html.replace(/<[^>]*>/g, '');
  }

  /* ============================================================
     View details (full side panel)
     ============================================================ */

  /**
   * Opens the detail panel.
   *
   * IT FETCHES THE DETAIL rather than showing the grid row. The row carries no notes, no DST rule
   * note, no usage count and no permitted actions - and the usage count is what decides whether
   * Delete may be offered.
   */
  viewTimeZone(tz: TimeZoneModel): void {
    this.isLoading = true;
    this.selectedTimeZone = tz;
    this.showViewPanel = true;

    this.masters
      .getTimeZone(tz.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.selectedTimeZone = this.toViewModelFromDetail(detail);
          this.canDeactivate = canPerform(detail, 'Deactivate');

          // Both halves matter: the permission AND the fact that no state still points at it.
          this.canDelete = canPerform(detail, 'Delete') && detail.stateUsageCount === 0;

          this.isLoading = false;
          this.renderNow();
        },
        error: (error) => {
          this.isLoading = false;
          this.showToast('error', 'Could not open', apiErrorMessage(error, 'That time zone could not be opened.'));
          this.renderNow();
        },
      });
  }

  closeViewPanel(): void {
    this.showViewPanel = false;
    this.selectedTimeZone = null;
    this.renderNow();
  }

  /* ============================================================
     Row quick-view (mobile-only compact popup)
     ============================================================ */

  openRowDetails(tz: TimeZoneModel): void {
    this.rowDetailsTz = tz;
    this.showRowDetailsModal = true;
  }

  closeRowDetails(): void {
    this.showRowDetailsModal = false;
    this.rowDetailsTz = null;
  }

  /* ============================================================
     Activate / Deactivate / Delete
     ============================================================ */

  confirmActivate(tz: TimeZoneModel): void {
    this.viewTimeZone(tz);
    this.showViewPanel = false;
    this.showActivateModal = true;
  }

  activateConfirmed(): void {
    const current = this.selectedTimeZone;
    if (!current) return;

    this.masters
      .activateTimeZone(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('success', 'Activated', `Time Zone '${current.displayName}' activated successfully`);
          this.showActivateModal = false;
          this.selectedTimeZone = null;
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => this.reportWriteFailure(error, 'The time zone could not be activated.'),
      });
  }

  confirmDeactivate(tz: TimeZoneModel): void {
    this.viewTimeZone(tz);
    this.showViewPanel = false;
    this.showDeactivateModal = true;
  }

  deactivateConfirmed(): void {
    const current = this.selectedTimeZone;
    if (!current || !this.canDeactivate) return;

    this.masters
      .deactivateTimeZone(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('warning', 'Deactivated', `Time Zone '${current.displayName}' deactivated successfully`);
          this.showDeactivateModal = false;
          this.selectedTimeZone = null;
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => this.reportWriteFailure(error, 'The time zone could not be deactivated.'),
      });
  }

  confirmDelete(tz: TimeZoneModel): void {
    this.viewTimeZone(tz);
    this.showViewPanel = false;
    this.showDeleteModal = true;
  }

  deleteConfirmed(): void {
    const current = this.selectedTimeZone;
    if (!current || !this.canDelete) return;

    this.masters
      .deleteTimeZone(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('error', 'Deleted', `Time Zone '${current.displayName}' deleted successfully`);
          this.showDeleteModal = false;
          this.selectedTimeZone = null;
          this.currentPage = 1;
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => this.reportWriteFailure(error, 'The time zone could not be deleted.'),
      });
  }

  closeModals(): void {
    this.showActivateModal = false;
    this.showDeactivateModal = false;
    this.showDeleteModal = false;

    if (!this.showViewPanel) {
      this.selectedTimeZone = null;
    }
  }

  /* ============================================================
     Create / Edit form
     ============================================================ */

  openCreateForm(): void {
    this.editingId = null;
    this.isEdit = false;
    this.formModel = this.createNewTimeZone();
    this.statusValidationError = null;
    this.fieldErrors = {};
    this.submitted = false;
    this.view = 'form';
  }

  /**
   * Opens the edit form.
   *
   * IT LOADS THE DETAIL FIRST. The grid row has no notes and no DST rule note, so editing from the
   * row would silently blank both the moment somebody saved.
   */
  openEditForm(tz: TimeZoneModel): void {
    this.editingId = tz.id;
    this.isEdit = true;
    this.statusValidationError = null;
    this.fieldErrors = {};
    this.submitted = false;
    this.view = 'form';

    this.masters
      .getTimeZone(tz.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.formModel = this.toViewModelFromDetail(detail);
          this.renderNow();
        },
        error: (error) => {
          this.view = 'list';
          this.showToast('error', 'Could not open', apiErrorMessage(error, 'That time zone could not be opened for editing.'));
          this.renderNow();
        },
      });
  }

  cancelForm(): void {
    this.view = 'list';
    this.editingId = null;
  }

  onKeyChange(): void {
    this.formModel.timeZoneKey = (this.formModel.timeZoneKey ?? '').trim();
  }

  onDisplayNameChange(): void {
    this.formModel.displayName = (this.formModel.displayName ?? '').trim();
  }

  onShortNameChange(): void {
    this.formModel.shortName = (this.formModel.shortName ?? '').trim();
  }

  onStatusChanged(): void {
    this.statusValidationError = null;
  }

  private validateForm(): boolean {
    this.fieldErrors = {};
    let valid = true;

    if (!this.formModel.timeZoneKey?.trim()) {
      this.fieldErrors['timeZoneKey'] = 'Time Zone ID is required';
      valid = false;
    }

    if (!this.formModel.displayName?.trim()) {
      this.fieldErrors['displayName'] = 'Display Name is required';
      valid = false;
    }

    // THE OFFSET IS REQUIRED BY THE API and was not checked here at all. Without it the create
    // call fails on a field the form never asked about, which is the worst kind of validation
    // failure: correct, and impossible for the person to act on.
    if (
      this.formModel.standardUtcOffsetMinutes === null ||
      this.formModel.standardUtcOffsetMinutes === undefined
    ) {
      this.fieldErrors['standardUtcOffsetMinutes'] = 'UTC offset is required';
      valid = false;
    }

    this.statusValidationError = null;

    if (!this.formModel.status?.trim()) {
      this.statusValidationError = 'Status is required';
      valid = false;
    }

    return valid;
  }

  handleSubmit(): void {
    this.submitted = true;

    if (!this.validateForm()) {
      return;
    }

    this.save();
  }

  /**
   * Saves the form.
   *
   * THE IANA KEY IS NOT SENT ON AN UPDATE, and the API has no field for it. The key identifies the
   * zone: every timestamp ever stamped in it points at that string, so repointing it changes what
   * historical data means rather than correcting a typo.
   *
   * THE DUPLICATE CHECK IS THE SERVER'S. A local scan could not see a zone created by a colleague
   * a moment ago, nor one on a page this screen has not fetched.
   */
  private save(): void {
    const model = this.formModel;
    const key = (model.timeZoneKey ?? '').trim();
    const displayName = (model.displayName ?? '').trim();

    if (!key.includes('/')) {
      this.showToast('error', 'Validation Error', 'Time Zone ID must be in IANA format (e.g., Asia/Kolkata)');
      return;
    }

    const sortOrder = model.sortOrder < 0 ? 0 : model.sortOrder;

    if (this.isEdit && this.editingId) {
      this.masters
        .updateTimeZone(this.editingId, {
          expectedVersion: model.version,
          displayName,
          shortName: (model.shortName ?? '').trim() || null,
          standardUtcOffsetMinutes: model.standardUtcOffsetMinutes,
          supportsDaylightSaving: model.supportsDST,
          daylightSavingRuleNote: (model.dstRuleNote ?? '').trim() || null,
          isDefaultRecommended: model.isDefaultRecommended,
          sortOrder,
          notes: (model.notes ?? '').trim() || null,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => {
            this.showToast('success', 'Updated', `Time Zone '${displayName}' updated successfully`);
            this.masters.invalidateReferenceData();
            this.view = 'list';
            this.editingId = null;
            this.loadData();
          },
          error: (error) => this.reportSaveFailure(error, 'The time zone could not be updated.'),
        });

      return;
    }

    this.masters
      .createTimeZone({
        timeZoneKey: key,
        displayName,
        standardUtcOffsetMinutes: model.standardUtcOffsetMinutes ?? 0,
        shortName: (model.shortName ?? '').trim() || null,
        supportsDaylightSaving: model.supportsDST,
        daylightSavingRuleNote: (model.dstRuleNote ?? '').trim() || null,
        isDefaultRecommended: model.isDefaultRecommended,
        status: model.status ? STATUS_CODES[model.status] : 'draft',
        sortOrder,
        notes: (model.notes ?? '').trim() || null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.showToast('success', 'Added', `Time Zone '${created.displayName}' added successfully`);
          this.masters.invalidateReferenceData();
          this.view = 'list';
          this.currentPage = 1;
          this.loadData();
        },
        error: (error) => this.reportSaveFailure(error, 'The time zone could not be created.'),
      });
  }

  /* ============================================================
     Mapping
     ============================================================ */

  private toViewModel(item: TimeZoneListItem): TimeZoneModel {
    return {
      id: item.id,
      tenantId: item.tenantId,
      timeZoneKey: item.timeZoneKey,
      displayName: item.displayName,
      shortName: item.shortName,
      standardUtcOffsetMinutes: item.standardUtcOffsetMinutes,
      supportsDST: item.supportsDaylightSaving,
      dstRuleNote: null,
      sortOrder: item.sortOrder,
      isDefaultRecommended: item.isDefaultRecommended,
      status: STATUS_LABELS[item.status] ?? item.statusDescription,
      isActive: item.isActive,
      notes: null,
      createdAt: item.updatedAtUtc ? new Date(item.updatedAtUtc) : new Date(),
      updatedAt: item.updatedAtUtc ? new Date(item.updatedAtUtc) : null,
      stateUsageCount: 0,
      isPlatformRow: item.isPlatformRow,
      version: item.version,
      permittedActions: [],
    };
  }

  private toViewModelFromDetail(detail: TimeZoneDetail): TimeZoneModel {
    return {
      id: detail.id,
      tenantId: detail.tenantId,
      timeZoneKey: detail.timeZoneKey,
      displayName: detail.displayName,
      shortName: detail.shortName,
      standardUtcOffsetMinutes: detail.standardUtcOffsetMinutes,
      supportsDST: detail.supportsDaylightSaving,
      dstRuleNote: detail.daylightSavingRuleNote,
      sortOrder: detail.sortOrder,
      isDefaultRecommended: detail.isDefaultRecommended,
      status: STATUS_LABELS[detail.status] ?? detail.statusDescription,
      isActive: detail.isActive,
      notes: detail.notes,
      createdAt: new Date(detail.createdAtUtc),
      createdBy: detail.createdByUserId,
      updatedAt: detail.updatedAtUtc ? new Date(detail.updatedAtUtc) : null,
      updatedBy: detail.updatedByUserId,
      stateUsageCount: detail.stateUsageCount,
      isPlatformRow: detail.isPlatformRow,
      version: detail.version,
      permittedActions: detail.permittedActions,
    };
  }

  /* ============================================================
     Failure reporting
     ============================================================ */

  private reportSaveFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'DUPLICATE_CODE') {
      this.fieldErrors['timeZoneKey'] = apiErrorMessage(error, 'Time Zone ID already exists');
      this.showToast('error', 'Validation Error', this.fieldErrors['timeZoneKey']);
      this.renderNow();
      return;
    }

    this.fieldErrors = { ...this.fieldErrors, ...apiFieldErrors(error) };
    this.showToast('error', 'Save failed', apiErrorMessage(error, fallback));
    this.renderNow();
  }

  private reportWriteFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.showToast('warning', 'Record changed', 'Somebody else changed this time zone. Refreshing.');
      this.loadData();
      return;
    }

    this.showToast('error', 'Action failed', apiErrorMessage(error, fallback));
    this.renderNow();
  }

  /* ============================================================
     Toasts
     ============================================================ */

  showToast(type: Toast['type'], title: string, message: string): void {
    const toast: Toast = { id: this.toastSeq++, type, title, message };
    this.toasts.push(toast);
    setTimeout(() => this.dismissToast(toast.id), 4000);
  }

  dismissToast(id: number): void {
    this.toasts = this.toasts.filter((toast) => toast.id !== id);
    this.renderNow();
  }

  trackByToastId(_index: number, toast: Toast): number {
    return toast.id;
  }

  trackByTzId(_index: number, tz: TimeZoneModel): string {
    return tz.id;
  }
}

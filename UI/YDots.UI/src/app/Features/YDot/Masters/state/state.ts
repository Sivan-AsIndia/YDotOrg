import {
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { enumLabel } from '../../../../Shared/models/enum-option.model';
import {
  EnumOption,
  JurisdictionType,
  MasterDataStatus,
  StateProvinceDetail,
  StateProvinceListItem,
  canPerform,
} from '../../../../Shared/models/global-master.model';
import { MasterService } from '../master.service';
import { GeoMasterService } from '../../../../Shared/services/geo-master.service';

// ---------------------------------------------------------------------------
// Models
// ---------------------------------------------------------------------------
export interface CountryModel {
  id: string;
  countryName: string;
  countryCode: string;
  isActive: boolean;
}

export interface TimeZoneModel {
  id: string;
  displayName: string;
  isActive: boolean;
}

export interface StateProvinceModel {
  id: string;
  stateProvinceCode: string;
  stateProvinceName: string;
  displayName?: string | null;
  countryId?: string | null;
  countryName?: string | null;

  /** The API's code - 'state', 'unionTerritory'. The label comes from the server's option list. */
  jurisdictionType?: JurisdictionType | null;

  /** How the server words it - the typed description for "Other", rather than the word itself. */
  jurisdictionDescription?: string | null;

  isFederalJurisdiction: boolean;
  gstStateCode?: string | null;
  stateTaxJurisdictionCode?: string | null;
  defaultTimeZoneId?: string | null;
  postalCodePattern?: string | null;
  addressFormatHint?: string | null;

  /** The API's code - 'draft', 'active', 'inactive'. */
  status?: MasterDataStatus | null;

  isActive: boolean;
  isDeleted: boolean;
  sortOrder: number;
  notes?: string | null;
  createdAt: Date;

  /** Names, resolved by the server - never the user GUID it used to print. */
  createdBy?: string | null;
  updatedAt?: Date | null;
  updatedBy?: string | null;

  /** Cities beneath this state. Non-zero is why Delete is refused. */
  cityCount: number;

  /** A shared platform row. Read-only to an Organisation; only SuperAdmin may change it. */
  isPlatformRow: boolean;

  /** Sent back on the next write. A stale one answers 409 rather than overwriting somebody. */
  version: number;

  /** What the SERVER says this caller may do. */
  permittedActions: string[];

  /** The free-text description behind a jurisdiction type of "Other". */
  otherJurisdictionType?: string | null;

  defaultTimeZoneName?: string | null;
}

// THE JURISDICTION TYPES AND STATUSES ARE THE SERVER'S. They used to be two literal lists in this
// file, paired with label/code maps; they now come from `GET /masters/reference-data`, already in
// the camelCase the records carry (see MasterService), so a value the domain adds appears here
// without an Angular change and an existing record's value always has an option to select.

type ToastKind = 'success' | 'warning' | 'error' | 'info';

interface ToastMessage {
  id: number;
  kind: ToastKind;
  title: string;
  message: string;
}

type ViewMode = 'list' | 'form';

type AccordionSection =
  | 'stateIdentity'
  | 'countryLinkage'
  | 'compliance'
  | 'addressRules'
  | 'statusGovernance';

let idCounter = 1000;
function newId(): string {
  idCounter += 1;
  return `id-${idCounter}-${Math.random().toString(36).slice(2, 8)}`;
}

@Component({
  selector: 'app-state',
  imports: [CommonModule, FormsModule],
  templateUrl: './state.html',
  styleUrl: './state.css',
})
export class StateComponent implements OnInit {
  // -------------------------------------------------------------------
  // Shared / bootstrap
  // -------------------------------------------------------------------
  isInitialized = false;
  isLoading = false;
  viewMode: ViewMode = 'list';

  countries: CountryModel[] = [];
  timeZones: TimeZoneModel[] = [];

  /** The server's jurisdiction types and statuses, values in API case. */
  jurisdictionOptions: EnumOption[] = [];
  statusOptions: EnumOption[] = [];

  toasts: ToastMessage[] = [];
  private toastCounter = 0;

  private lastFocusedElement: HTMLElement | null = null;

  private readonly masters = inject(MasterService);
  private readonly geoMasters = inject(GeoMasterService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly cdr = inject(ChangeDetectorRef);

  constructor(private hostRef: ElementRef<HTMLElement>) {}

  ngOnInit(): void {
    this.loadReferenceData();
    this.loadData();
    this.isInitialized = true;
  }

  /**
   * Asks for a render after state changed outside an Angular-bound event.
   *
   * THIS IS WHY THE SCREEN SAT ON ITS LOADING OVERLAY. The application is zoneless, and this
   * component keeps its state in plain fields rather than signals, so an HTTP callback setting
   * `isLoading = false` changed nothing on screen: no DOM event, no signal, no render. Arriving from
   * the sidebar left the whirly-loader over a grid whose three requests had all succeeded. Every
   * asynchronous callback in this file ends here - the same arrangement the Time Zone screen uses.
   */
  private renderNow(): void {
    this.cdr.markForCheck();
  }

  /**
   * The country and time-zone dropdowns.
   *
   * ONE CALL FOR BOTH, cached by the service for the life of the application: every Masters
   * screen opens by asking for the same lists, and none of them change between two page views.
   * `invalidateReferenceData` after a write is what stops the cache going stale.
   */
  private loadReferenceData(): void {
    this.masters
      .getReferenceData()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (reference) => {
          this.countries = reference.countries.map((country) => ({
            id: country.id,
            countryName: country.name,
            countryCode: country.code,
            isActive: country.status === 'active',
          }));

          this.timeZones = reference.timeZones.map((zone) => ({
            id: zone.id,
            displayName: zone.name,
            isActive: zone.status === 'active',
          }));

          this.jurisdictionOptions = reference.jurisdictionTypes;
          this.statusOptions = reference.statuses;
          this.renderNow();
        },
        error: () =>
          this.showToast(
            'warning',
            'Reference data',
            'Countries, jurisdictions and time zones could not be loaded. The dropdowns will be empty.',
          ),
      });
  }

  /** A status code as the server labels it. */
  statusLabel(status: string | null | undefined): string {
    return enumLabel(this.statusOptions, status);
  }

  /** A jurisdiction as the server words it, preferring the row's own description. */
  jurisdictionLabel(state: Pick<StateProvinceModel, 'jurisdictionType' | 'jurisdictionDescription'>): string {
    return state.jurisdictionDescription || enumLabel(this.jurisdictionOptions, state.jurisdictionType);
  }

  private captureFocus(): void {
    this.lastFocusedElement = document.activeElement as HTMLElement | null;
  }

  private restoreFocus(): void {
    if (this.lastFocusedElement && typeof this.lastFocusedElement.focus === 'function') {
      this.lastFocusedElement.focus();
    }
    this.lastFocusedElement = null;
  }

  // =====================================================================
  // LIST VIEW
  // =====================================================================
  states: StateProvinceModel[] = [];
  filteredStates: StateProvinceModel[] = [];
  selectedState: StateProvinceModel | null = null;
  selectedCompany: StateProvinceModel | null = null;

  /**
   * What the server allows on the open record. FALSE until its detail arrives: they used to start
   * `true`, so a modal opened before the detail loaded offered a button the server then refused.
   */
  canDeactivate = false;
  canDelete = false;

  /** True while the detail behind an open confirmation is still being fetched. */
  actionsLoading = false;

  searchText = '';
  selectedStatus = '';
  selectedCountryId = '';
  selectedJurisdictionType = '';

  currentPage = 1;
  pageSize = 10;
  pageWindowSize = 2;
  startPage = 1;

  /** Reactive state for the inline details pane. */
  readonly detailsOpen = signal(false);

  // Kept for compatibility with existing callers; the UI no longer renders these as overlays.
  showViewOffcanvas = false;
  showRowDetailsModal = false;
  showActivateModal = false;
  showDeactivateModal = false;
  showDeleteModal = false;

  /** The server's totals. A page of ten cannot tell you how many states the catalogue holds. */
  private totalCountFromServer = 0;
  private activeCountFromServer = 0;
  private inactiveCountFromServer = 0;
  private totalPagesFromServer = 1;

  get totalPages(): number {
    return Math.max(1, this.totalPagesFromServer);
  }

  get endPage(): number {
    return Math.min(this.startPage + this.pageWindowSize - 1, this.totalPages);
  }

  get pageEndItem(): number {
    return Math.min(this.currentPage * this.pageSize, this.totalCountFromServer);
  }

  /** The server already paged the result, so this is the page it returned. */
  get pagedStates(): StateProvinceModel[] {
    return this.filteredStates;
  }

  /**
   * The countries offered in the FILTER.
   *
   * EVERY ACTIVE COUNTRY, not only those with a state on the current page. Narrowing to the
   * loaded rows made the filter useless the moment paging moved to the server: the country you
   * wanted to filter by was, by definition, the one whose states were not on screen.
   */
  get tableCountries(): CountryModel[] {
    return this.countries
      .filter((country) => country.isActive)
      .sort((left, right) => left.countryName.localeCompare(right.countryName));
  }

  /** The full catalogue, for the same reason as the countries above. */
  get tableJurisdictionTypes(): EnumOption[] {
    return this.jurisdictionOptions;
  }

  get scopedCount(): number {
    return this.totalCountFromServer;
  }

  get activeCount(): number {
    return this.activeCountFromServer;
  }

  get inactiveCount(): number {
    return this.inactiveCountFromServer;
  }

  /**
   * Fetches one page from the API.
   *
   * THE FILTERS GO TO THE SERVER. Filtering in the browser can only filter what has been
   * downloaded - the current page - so a search for a state on page four came back empty and
   * looked like a missing record. More to the point, only the server can apply the Organisation
   * filter: a static array has no idea who is asking.
   */
  private loadData(): void {
    this.isLoading = true;

    this.masters
      .searchStates({
        page: this.currentPage,
        pageSize: this.pageSize,
        search: this.searchText.trim() || undefined,
        countryId: this.selectedCountryId || undefined,
        jurisdictionType: (this.selectedJurisdictionType as JurisdictionType) || undefined,
        status: (this.selectedStatus as MasterDataStatus) || undefined,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.states = page.items.map((item) => this.toViewModel(item));
          this.filteredStates = this.states;
          this.totalCountFromServer = page.totalCount;
          this.totalPagesFromServer = page.totalPages;
          this.currentPage = page.page;
          this.isLoading = false;
          this.renderNow();
        },
        error: (error) => {
          this.states = [];
          this.filteredStates = [];
          this.isLoading = false;
          this.showToast(
            'error',
            'Could not load',
            apiErrorMessage(error, 'The state catalogue could not be loaded.'),
          );
        },
      });

    this.masters
      .searchStates({ pageSize: 1, status: 'active' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.activeCountFromServer = page.totalCount;
          this.renderNow();
        },
      });

    this.masters
      .searchStates({ pageSize: 1, status: 'inactive' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.inactiveCountFromServer = page.totalCount;
          this.renderNow();
        },
      });
  }

  private toViewModel(item: StateProvinceListItem): StateProvinceModel {
    return {
      id: item.id,
      stateProvinceCode: item.stateProvinceCode,
      stateProvinceName: item.stateProvinceName,
      displayName: item.displayName,
      countryId: item.countryId,
      countryName: item.countryName,
      jurisdictionType: item.jurisdictionType,
      jurisdictionDescription: item.jurisdictionDescription,
      isFederalJurisdiction: item.isFederalJurisdiction,
      gstStateCode: item.gstStateCode,
      stateTaxJurisdictionCode: null,
      defaultTimeZoneId: null,
      postalCodePattern: null,
      addressFormatHint: null,
      status: item.status,
      isActive: item.isActive,
      isDeleted: false,
      sortOrder: item.sortOrder,
      notes: null,
      createdAt: item.updatedAtUtc ? new Date(item.updatedAtUtc) : new Date(),
      updatedAt: item.updatedAtUtc ? new Date(item.updatedAtUtc) : null,
      cityCount: item.cityCount,
      isPlatformRow: item.isPlatformRow,
      version: item.version,
      permittedActions: [],
    };
  }

  private toViewModelFromDetail(detail: StateProvinceDetail): StateProvinceModel {
    return {
      id: detail.id,
      stateProvinceCode: detail.stateProvinceCode,
      stateProvinceName: detail.stateProvinceName,
      displayName: detail.displayName,
      countryId: detail.countryId,
      countryName: detail.countryName,
      jurisdictionType: detail.jurisdictionType,
      jurisdictionDescription: detail.jurisdictionDescription,
      otherJurisdictionType: detail.otherJurisdictionType,
      isFederalJurisdiction: detail.isFederalJurisdiction,
      gstStateCode: detail.gstStateCode,
      stateTaxJurisdictionCode: detail.stateTaxJurisdictionCode,
      defaultTimeZoneId: detail.defaultTimeZoneId,
      defaultTimeZoneName: detail.defaultTimeZoneName,
      postalCodePattern: detail.postalCodePattern,
      addressFormatHint: detail.addressFormatHint,
      status: detail.status,
      isActive: detail.isActive,
      isDeleted: false,
      sortOrder: detail.sortOrder,
      notes: detail.notes,
      createdAt: new Date(detail.createdAtUtc),
      createdBy: detail.createdByName ?? null,
      updatedAt: detail.updatedAtUtc ? new Date(detail.updatedAtUtc) : null,
      updatedBy: detail.updatedByName ?? null,
      cityCount: detail.cityCount,
      isPlatformRow: detail.isPlatformRow,
      version: detail.version,
      permittedActions: detail.permittedActions,
    };
  }

  onSearch(value: string): void {
    this.searchText = value;
    this.applyFilters();
  }

  onStatusFilterChange(value: string): void {
    this.selectedStatus = value;
    this.applyFilters();
  }

  onCountryFilterChange(value: string): void {
    this.selectedCountryId = value;
    this.applyFilters();
  }

  onJurisdictionFilterChange(value: string): void {
    this.selectedJurisdictionType = value;
    this.applyFilters();
  }

  applyFilters(): void {
    this.currentPage = 1;
    this.startPage = 1;
    this.loadData();
  }

  getStatusBadge(status?: string | null): string {
    switch (status) {
      case 'active':
        return 'bg-success-transparent text-success';
      case 'inactive':
        return 'bg-danger-transparent text-danger';
      case 'draft':
        return 'bg-warning-transparent text-warning';
      default:
        return 'bg-secondary-transparent text-secondary';
    }
  }

  getStatusDotBadge(status?: string | null): string {
    switch (status) {
      case 'active':
        return 'bg-success text-success';
      case 'inactive':
        return 'bg-danger text-danger';
      case 'draft':
        return 'bg-warning text-warning';
      default:
        return 'bg-secondary text-secondary';
    }
  }

  getTimeZoneName(timeZoneId?: string | null): string | null {
    if (!timeZoneId) return null;
    return this.timeZones.find((t) => t.id === timeZoneId)?.displayName ?? null;
  }

  onPageSizeChange(value: string): void {
    this.pageSize = parseInt(value, 10) || 10;
    this.currentPage = 1;
    this.startPage = 1;
    this.loadData();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.currentPage = page;
    if (page < this.startPage) this.startPage = page;
    if (page > this.endPage) this.startPage = page - this.pageWindowSize + 1;
    this.loadData();
  }

  previousPage(): void {
    if (this.currentPage > 1) this.goToPage(this.currentPage - 1);
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) this.goToPage(this.currentPage + 1);
  }

  pageNumbers(): number[] {
    const arr: number[] = [];
    for (let i = this.startPage; i <= this.endPage; i++) arr.push(i);
    return arr;
  }

  /**
   * Opens the detail pane.
   *
   * IT FETCHES THE DETAIL rather than showing the grid row. The row carries no notes, no postal
   * pattern, no default time zone and no permitted actions - and the last of those is what
   * decides which buttons may be drawn.
   */
  viewState(state: StateProvinceModel): void {
    this.selectedState = state;
    this.selectedCompany = state;
    this.detailsOpen.set(true);
    this.showViewOffcanvas = true;
    this.showRowDetailsModal = false;
    this.canDeactivate = false;
    this.canDelete = false;
    this.actionsLoading = true;

    this.masters
      .getState(state.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          const loaded = this.toViewModelFromDetail(detail);
          this.selectedState = loaded;
          this.selectedCompany = loaded;
          this.canDeactivate = canPerform(detail, 'Deactivate');

          // Both halves matter: the permission AND the fact that no city sits beneath it.
          this.canDelete = canPerform(detail, 'Delete') && detail.cityCount === 0;
          this.actionsLoading = false;
          this.renderNow();
        },
        error: (error) => {
          this.actionsLoading = false;
          this.showToast(
            'error',
            'Could not open',
            apiErrorMessage(error, 'That state could not be opened.'),
          );
        },
      });
  }

  /**
   * Why Deactivate is not on offer, in terms the person can act on.
   *
   * The modal used to say "deactivate all cities under this state first" - but the server never
   * ties deactivation to cities. It withholds it for a shared platform row the caller may not
   * change, or for a row that is not active.
   */
  get deactivateBlockedReason(): string {
    const state = this.selectedState;

    if (state?.isPlatformRow) {
      return 'This is a shared platform state. Only the platform administrator can change it.';
    }

    return 'This state is not active, so there is nothing to deactivate.';
  }

  /** Why Delete is not on offer: the cities beneath it, or the same platform-row rule. */
  get deleteBlockedReason(): string {
    const state = this.selectedState;
    const cities = state?.cityCount ?? 0;

    if (cities > 0) {
      return `${cities} ${cities === 1 ? 'city sits' : 'cities sit'} under this state. `
        + 'Remove or move them first, or deactivate the state instead.';
    }

    return 'This is a shared platform state. Only the platform administrator can delete it.';
  }

  closeViewOffcanvas(): void {
    this.detailsOpen.set(false);
    this.showViewOffcanvas = false;
    this.selectedState = null;
    this.selectedCompany = null;
    this.restoreFocus();
  }

  openRowDetails(company: StateProvinceModel): void {
    this.viewState(company);
  }

  closeRowDetailsModal(): void {
    this.closeViewOffcanvas();
  }

  confirmActivate(state: StateProvinceModel): void {
    this.viewState(state);
    this.showViewOffcanvas = false;
    this.detailsOpen.set(false);
    this.captureFocus();
    this.showActivateModal = true;
  }

  activateConfirmed(): void {
    const current = this.selectedState;

    if (!current) {
      this.showActivateModal = false;
      this.restoreFocus();
      return;
    }

    this.masters
      .activateState(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast(
            'success',
            'Activated',
            `State/Province '${current.stateProvinceName}' activated successfully`,
          );
          this.selectedState = null;
          this.selectedCompany = null;
          this.detailsOpen.set(false);
          this.showActivateModal = false;
          this.restoreFocus();
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => {
          this.showActivateModal = false;
          this.restoreFocus();
          this.reportWriteFailure(error, 'The state could not be activated.');
        },
      });
  }

  confirmDeactivate(state: StateProvinceModel): void {
    // viewState fetches the detail, which is what sets canDeactivate from the server's own
    // answer. The pane is closed again immediately: the modal is what the operator sees.
    this.viewState(state);
    this.showViewOffcanvas = false;
    this.detailsOpen.set(false);
    this.captureFocus();
    this.showDeactivateModal = true;
  }

  deactivateConfirmed(): void {
    const current = this.selectedState;

    if (!current || !this.canDeactivate) {
      this.showDeactivateModal = false;
      this.restoreFocus();
      return;
    }

    this.masters
      .deactivateState(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast(
            'warning',
            'Deactivated',
            `State/Province '${current.stateProvinceName}' deactivated successfully`,
          );
          this.selectedState = null;
          this.selectedCompany = null;
          this.detailsOpen.set(false);
          this.showDeactivateModal = false;
          this.restoreFocus();
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => {
          this.showDeactivateModal = false;
          this.restoreFocus();
          this.reportWriteFailure(error, 'The state could not be deactivated.');
        },
      });
  }

  confirmDelete(state: StateProvinceModel): void {
    this.viewState(state);
    this.showViewOffcanvas = false;
    this.detailsOpen.set(false);
    this.captureFocus();
    this.showDeleteModal = true;
  }

  deleteConfirmed(): void {
    const current = this.selectedState;

    if (!current || !this.canDelete) {
      this.showDeleteModal = false;
      this.restoreFocus();
      return;
    }

    this.masters
      .deleteState(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast(
            'error',
            'Deleted',
            `State/Province '${current.stateProvinceName}' deleted successfully`,
          );
          this.selectedState = null;
          this.selectedCompany = null;
          this.detailsOpen.set(false);
          this.currentPage = 1;
          this.showDeleteModal = false;
          this.restoreFocus();
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => {
          this.showDeleteModal = false;
          this.restoreFocus();
          this.reportWriteFailure(error, 'The state could not be deleted.');
        },
      });
  }

  closeModals(): void {
    this.showActivateModal = false;
    this.showDeactivateModal = false;
    this.showDeleteModal = false;
    this.restoreFocus();
  }

  /**
   * Reports a failed activate, deactivate or delete.
   *
   * A 409 IS NAMED SEPARATELY because it means something the operator can act on - somebody else
   * changed this row - rather than something being broken, and the fix is to refresh rather than
   * to try again.
   *
   * A DEPENDENCY REFUSAL likewise: "this state still has cities" is a fact the person can do
   * something about, and burying it in a generic failure message would leave them retrying.
   */
  private reportWriteFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.showToast('warning', 'Record changed', 'Somebody else changed this state. Refreshing.');
      this.loadData();
      return;
    }

    if (code === 'RECORD_IN_USE' || code === 'DEPENDENCY_EXISTS') {
      this.showToast('warning', 'Still in use', apiErrorMessage(error, fallback));
      return;
    }

    this.showToast('error', 'Action failed', apiErrorMessage(error, fallback));
  }

  // =====================================================================
  // ADD / EDIT FORM
  // =====================================================================
  stateProvince: StateProvinceModel = this.createNewStateProvince();
  isEdit = false;
  editingId: string | null = null;

  get pageTitle(): string {
    return this.isEdit ? 'Edit State' : 'Create State';
  }

  get pageSubTitle(): string {
    return this.isEdit ? 'Update state details' : 'Create new state';
  }

  stateTouched = false;
  addressTouched = false;
  contactTouched = false;
  financeTouched = false;
  reportTouched = false;

  showIdentity = true;
  showCountry = false;
  showCompliance = false;
  showAddressRules = false;
  showStatus = false;

  stateProvinceCodeValidationError: string | null = null;
  stateProvinceNameValidationError: string | null = null;
  countryValidationError: string | null = null;
  jurisdictionTypeValidationError: string | null = null;
  otherJurisdictionTypeValidationError: string | null = null;
  statusValidationError: string | null = null;
  gstStateCodeValidationError: string | null = null;

  /** True when "Other" is chosen, which is when the server requires a description of it. */
  get isOtherJurisdiction(): boolean {
    return this.stateProvince.jurisdictionType === 'other';
  }

  get isIndiaSelected(): boolean {
    return (
      this.countries.find((c) => c.id === this.stateProvince.countryId)?.countryCode === 'IN'
    );
  }

  get formCountries(): CountryModel[] {
    return this.countries.filter((c) => c.isActive);
  }

  /**
   * The zones offered as this state's default, NARROWED TO ITS COUNTRY.
   *
   * A state belongs to exactly one country, so offering the whole catalogue here invited the
   * error the brief is about: a Tamil Nadu row defaulting to America/Denver, which nothing
   * downstream would ever flag. Narrowing it makes that unselectable.
   *
   * ALL of the country's zones are offered, not just one - the United States has seven and a
   * state genuinely has to pick among them. When the country has none mapped, or none is chosen
   * yet, this falls back to the full catalogue rather than an empty list, so the field is always
   * answerable.
   */
  get formTimeZones(): TimeZoneModel[] {
    return this.countryTimeZones.length
      ? this.countryTimeZones
      : this.timeZones.filter((t) => t.isActive);
  }

  /** True when the list above really is the country's own, for labelling the field honestly. */
  timeZonesAreCountryFiltered = false;

  /** The selected country's zones. Empty until a country is chosen. */
  countryTimeZones: TimeZoneModel[] = [];

  /**
   * Loads the zones for a country and drops a now-invalid selection.
   *
   * THE SELECTION IS CLEARED WHEN IT NO LONGER FITS. Editing a state from India to Australia
   * while leaving DefaultTimeZone on Asia/Kolkata is exactly the inconsistency this screen is
   * meant to prevent, and it is invisible because the box still looks filled in.
   */
  private loadCountryTimeZones(countryId: string | null | undefined): void {
    if (!countryId) {
      this.countryTimeZones = [];
      this.timeZonesAreCountryFiltered = false;
      return;
    }

    this.geoMasters
      .getTimeZones(countryId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        this.renderNow();
        this.timeZonesAreCountryFiltered = result.isCountryFiltered;

        // isActive is unconditionally true: the lookup endpoints return only Active rows, both
        // on the country-filtered path and the fallback. Comparing the status here would be
        // testing something the server has already guaranteed.
        this.countryTimeZones = result.timeZones.map((zone) => ({
          id: zone.id,
          displayName: zone.name,
          isActive: true,
        }));

        const selected = this.stateProvince.defaultTimeZoneId;

        if (
          result.isCountryFiltered &&
          selected &&
          !this.countryTimeZones.some((zone) => zone.id === selected)
        ) {
          this.stateProvince.defaultTimeZoneId = null;
        }
      });
  }

  private createNewStateProvince(): StateProvinceModel {
    return {
      id: '',
      stateProvinceCode: '',
      stateProvinceName: '',
      displayName: '',
      countryId: null,
      countryName: null,
      jurisdictionType: null,
      isFederalJurisdiction: false,
      gstStateCode: '',
      stateTaxJurisdictionCode: '',
      defaultTimeZoneId: null,
      postalCodePattern: '',
      addressFormatHint: '',
      status: null,
      isActive: false,
      isDeleted: false,
      sortOrder: 0,
      notes: '',
      createdAt: new Date(),

      // A new record has no server-side history yet: no cities beneath it, no version to send
      // back, and no permitted actions until it has been saved and read again.
      cityCount: 0,
      isPlatformRow: false,
      version: 0,
      permittedActions: [],
    };
  }

  openCreateForm(): void {
    this.closeViewOffcanvas();
    this.isEdit = false;
    this.editingId = null;
    this.stateProvince = this.createNewStateProvince();
    this.resetFormUiState();
    this.viewMode = 'form';
  }

  /**
   * Opens the edit form.
   *
   * IT LOADS THE DETAIL FIRST. The grid row has no notes, no postal pattern, no address hint and
   * no default time zone, so editing from the row would silently blank four fields the moment
   * somebody saved.
   */
  openEditForm(state: StateProvinceModel): void {
    this.closeViewOffcanvas();
    this.isEdit = true;
    this.editingId = state.id;
    this.stateProvince = { ...state };
    this.resetFormUiState();
    this.viewMode = 'form';

    this.masters
      .getState(state.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.stateProvince = this.toViewModelFromDetail(detail);

          // Narrow the zone picker to this state's country as the form opens, so the saved
          // value is shown in the list it belongs to rather than in the whole catalogue.
          this.loadCountryTimeZones(this.stateProvince.countryId);
          this.renderNow();
        },
        error: (error) => {
          this.viewMode = 'list';
          this.showToast(
            'error',
            'Could not open',
            apiErrorMessage(error, 'That state could not be opened for editing.'),
          );
        },
      });
  }

  private resetFormUiState(): void {
    this.stateTouched = false;
    this.addressTouched = false;
    this.contactTouched = false;
    this.financeTouched = false;
    this.reportTouched = false;
    this.showIdentity = true;
    this.showCountry = false;
    this.showCompliance = false;
    this.showAddressRules = false;
    this.showStatus = false;
    this.stateProvinceCodeValidationError = null;
    this.stateProvinceNameValidationError = null;
    this.countryValidationError = null;
    this.jurisdictionTypeValidationError = null;
    this.otherJurisdictionTypeValidationError = null;
    this.statusValidationError = null;
    this.gstStateCodeValidationError = null;
  }

  backToList(): void {
    this.viewMode = 'list';
    this.detailsOpen.set(false);
    this.selectedState = null;
    this.selectedCompany = null;
  }

  toggleAccordion(section: AccordionSection): void {
    switch (section) {
      case 'stateIdentity':
        this.showIdentity = !this.showIdentity;
        break;
      case 'countryLinkage':
        this.showCountry = !this.showCountry;
        break;
      case 'compliance':
        this.showCompliance = !this.showCompliance;
        break;
      case 'addressRules':
        this.showAddressRules = !this.showAddressRules;
        break;
      case 'statusGovernance':
        this.showStatus = !this.showStatus;
        break;
    }
  }

  openAccordion(section: AccordionSection): void {
    switch (section) {
      case 'stateIdentity':
        this.showIdentity = true;
        break;
      case 'countryLinkage':
        this.showCountry = true;
        break;
      case 'compliance':
        this.showCompliance = true;
        break;
      case 'addressRules':
        this.showAddressRules = true;
        break;
      case 'statusGovernance':
        this.showStatus = true;
        break;
    }
  }

  touchState(): void {
    this.stateTouched = true;
  }

  touchAddress(): void {
    this.addressTouched = true;
  }

  touchContact(): void {
    this.contactTouched = true;
  }

  touchFinance(): void {
    this.financeTouched = true;
  }

  touchReport(): void {
    this.reportTouched = true;
  }

  onStateProvinceCodeChanged(): void {
    this.stateTouched = true;
    this.stateProvinceCodeValidationError = null;
    this.stateProvince.stateProvinceCode = (this.stateProvince.stateProvinceCode ?? '').trim();
  }

  onStateProvinceNameChanged(): void {
    this.stateProvince.stateProvinceName = (this.stateProvince.stateProvinceName ?? '').replace(
      /[0-9]/g,
      ''
    );
    this.stateTouched = true;
    this.stateProvinceNameValidationError = null;
  }

  onStateNameChangedTrim(): void {
    this.stateProvince.stateProvinceName = (this.stateProvince.stateProvinceName ?? '').trim();
  }

  onDisplayNameChanged(): void {
    this.stateProvince.displayName = (this.stateProvince.displayName ?? '').trim();
  }

  onCountryChanged(): void {
    this.addressTouched = true;
    this.countryValidationError = null;
    this.gstStateCodeValidationError = null;
    this.loadCountryTimeZones(this.stateProvince.countryId);
  }

  onJurisdictionTypeChanged(): void {
    this.addressTouched = true;
    this.jurisdictionTypeValidationError = null;
    this.otherJurisdictionTypeValidationError = null;
  }

  onStatusChanged(): void {
    this.reportTouched = true;
    this.statusValidationError = null;
  }

  onGstInput(): void {
    this.contactTouched = true;
    this.gstStateCodeValidationError = null;
  }

  hasIdentityErrors(): boolean {
    return (
      !this.stateProvince.stateProvinceCode?.trim() ||
      !this.stateProvince.stateProvinceName?.trim()
    );
  }

  hasCountryErrors(): boolean {
    return (
      !this.stateProvince.countryId
      || !this.stateProvince.jurisdictionType?.trim()
      || (this.isOtherJurisdiction && !this.stateProvince.otherJurisdictionType?.trim())
    );
  }

  hasStatusErrors(): boolean {
    return !this.stateProvince.status?.trim();
  }

  hasComplianceErrors(): boolean {
    const country = this.countries.find((c) => c.id === this.stateProvince.countryId);
    return country?.countryCode === 'IN' && !this.stateProvince.gstStateCode?.trim();
  }

  private validateAllFields(): boolean {
    let isValid = true;

    this.stateProvinceCodeValidationError = null;
    this.stateProvinceNameValidationError = null;
    this.countryValidationError = null;
    this.jurisdictionTypeValidationError = null;
    this.otherJurisdictionTypeValidationError = null;
    this.statusValidationError = null;
    this.gstStateCodeValidationError = null;

    if (!this.stateProvince.stateProvinceCode?.trim()) {
      this.stateProvinceCodeValidationError = 'State/Province Code is required';
      isValid = false;
    } else if (!/^[A-Za-z0-9_-]+$/.test(this.stateProvince.stateProvinceCode.trim())) {
      this.stateProvinceCodeValidationError =
        'Only letters, numbers, underscore (_) and hyphen (-) are allowed';
      isValid = false;
    }

    if (!this.stateProvince.stateProvinceName?.trim()) {
      this.stateProvinceNameValidationError = 'State/Province Name is required';
      isValid = false;
    }

    if (!this.stateProvince.countryId) {
      this.countryValidationError = 'Country is required';
      isValid = false;
    }

    if (!this.stateProvince.jurisdictionType?.trim()) {
      this.jurisdictionTypeValidationError = 'Jurisdiction Type is required';
      isValid = false;
    } else if (this.isOtherJurisdiction && !this.stateProvince.otherJurisdictionType?.trim()) {
      // The server requires it for "Other" and the form had no box for it, so choosing Other
      // could never be saved.
      this.otherJurisdictionTypeValidationError = 'Describe the jurisdiction type';
      isValid = false;
    }

    if (!this.stateProvince.status?.trim()) {
      this.statusValidationError = 'Status is required';
      isValid = false;
    }

    if (this.isIndiaSelected && !this.stateProvince.gstStateCode?.trim()) {
      this.gstStateCodeValidationError = 'GST State Code is required for India';
      isValid = false;
    }

    return isValid;
  }

  handleSubmit(): void {
    const isValid = this.validateAllFields();
    if (isValid) {
      this.save();
      return;
    }
    if (this.hasIdentityErrors()) this.openAccordion('stateIdentity');
    if (this.hasCountryErrors()) this.openAccordion('countryLinkage');
    if (this.hasComplianceErrors()) this.openAccordion('compliance');
    if (this.hasStatusErrors()) this.openAccordion('statusGovernance');
  }

  private save(): void {
    if (!this.validateAllFields()) {
      if (this.hasIdentityErrors()) this.openAccordion('stateIdentity');
      if (this.hasCountryErrors()) this.openAccordion('countryLinkage');
      if (this.hasComplianceErrors()) this.openAccordion('compliance');
      if (this.hasStatusErrors()) this.openAccordion('statusGovernance');
      return;
    }

    this.stateProvince.stateProvinceCode = (this.stateProvince.stateProvinceCode ?? '')
      .toUpperCase()
      .trim();
    this.stateProvince.stateProvinceName = (this.stateProvince.stateProvinceName ?? '').trim();
    this.stateProvince.displayName = this.stateProvince.displayName?.trim() ?? '';
    this.stateProvince.notes = this.stateProvince.notes?.trim() ?? '';
    this.stateProvince.gstStateCode = this.stateProvince.gstStateCode?.trim() ?? '';

    if (this.stateProvince.sortOrder < 0) this.stateProvince.sortOrder = 0;

    const country = this.countries.find((c) => c.id === this.stateProvince.countryId);
    this.stateProvince.countryName = country?.countryName ?? null;
    this.stateProvince.isActive = this.stateProvince.status === 'active';

    if (this.isEdit && this.editingId) {
      this.masters
        .updateState(this.editingId, {
          expectedVersion: this.stateProvince.version,
          stateProvinceName: this.stateProvince.stateProvinceName,
          displayName: this.stateProvince.displayName || null,
          jurisdictionType: this.stateProvince.jurisdictionType ?? null,
          otherJurisdictionType: this.stateProvince.otherJurisdictionType?.trim() || null,
          isFederalJurisdiction: this.stateProvince.isFederalJurisdiction,
          gstStateCode: this.stateProvince.gstStateCode || null,
          stateTaxJurisdictionCode: this.stateProvince.stateTaxJurisdictionCode || null,
          defaultTimeZoneId: this.stateProvince.defaultTimeZoneId || null,
          postalCodePattern: this.stateProvince.postalCodePattern || null,
          addressFormatHint: this.stateProvince.addressFormatHint || null,
          sortOrder: this.stateProvince.sortOrder,
          notes: this.stateProvince.notes || null,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => {
            this.showToast(
              'success',
              'Updated',
              `State/Province '${this.stateProvince.stateProvinceName}' updated successfully`,
            );
            this.masters.invalidateReferenceData();
            this.geoMasters.invalidate();
            this.viewMode = 'list';
            this.editingId = null;
            this.loadData();
          },
          error: (error) => this.reportSaveFailure(error, 'The state could not be updated.'),
        });

      return;
    }

    this.masters
      .createState({
        stateProvinceCode: this.stateProvince.stateProvinceCode,
        stateProvinceName: this.stateProvince.stateProvinceName,
        countryId: this.stateProvince.countryId!,
        displayName: this.stateProvince.displayName || null,
        jurisdictionType: this.stateProvince.jurisdictionType ?? 'state',
        otherJurisdictionType: this.stateProvince.otherJurisdictionType?.trim() || null,
        isFederalJurisdiction: this.stateProvince.isFederalJurisdiction,
        gstStateCode: this.stateProvince.gstStateCode || null,
        stateTaxJurisdictionCode: this.stateProvince.stateTaxJurisdictionCode || null,
        defaultTimeZoneId: this.stateProvince.defaultTimeZoneId || null,
        postalCodePattern: this.stateProvince.postalCodePattern || null,
        addressFormatHint: this.stateProvince.addressFormatHint || null,
        status: this.stateProvince.status ?? 'draft',
        sortOrder: this.stateProvince.sortOrder,
        notes: this.stateProvince.notes || null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.showToast(
            'success',
            'Added',
            `State/Province '${created.stateProvinceName}' added successfully`,
          );
          this.masters.invalidateReferenceData();
          this.geoMasters.invalidate();
          this.viewMode = 'list';
          this.currentPage = 1;
          this.loadData();
        },
        error: (error) => this.reportSaveFailure(error, 'The state could not be created.'),
      });
  }

  /**
   * Reports a failed create or update.
   *
   * THE DUPLICATE GOES ON THE FIELD AND OPENS ITS ACCORDION. The form is five collapsed
   * sections; a message about a code the person cannot see is a message they will not find.
   *
   * THE CHECK ITSELF IS THE SERVER'S. A local scan of the loaded page could not see a state
   * created by a colleague a moment ago, nor one on a page this screen has not fetched - and
   * the uniqueness rule is per COUNTRY, which the browser has no way to evaluate across pages.
   */
  private reportSaveFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'DUPLICATE_CODE') {
      this.stateProvinceCodeValidationError = apiErrorMessage(
        error,
        'A state/province with this code already exists for the selected country',
      );
      this.openAccordion('stateIdentity');
      this.renderNow();
      return;
    }

    if (code === 'CONCURRENCY_CONFLICT') {
      this.showToast('warning', 'Record changed', 'Somebody else changed this state. Reload and try again.');
      return;
    }

    this.showToast('error', 'Save failed', apiErrorMessage(error, fallback));
  }

  cancelForm(): void {
    this.viewMode = 'list';
  }

  // =====================================================================
  // Toasts
  // =====================================================================
  private showToast(kind: ToastKind, title: string, message: string): void {
    this.toastCounter += 1;
    const toast: ToastMessage = { id: this.toastCounter, kind, title, message };
    this.toasts.push(toast);
    setTimeout(() => this.dismissToast(toast.id), 3500);

    // Toasts are raised from HTTP callbacks, so the render has to be asked for - and asking once
    // here also paints whatever else the same callback changed.
    this.renderNow();
  }

  dismissToast(id: number): void {
    this.toasts = this.toasts.filter((t) => t.id !== id);
    this.renderNow();
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}

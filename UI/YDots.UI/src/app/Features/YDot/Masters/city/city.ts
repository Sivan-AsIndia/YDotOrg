// city.ts
import {
  Component,
  OnInit,
  inject,
  signal,
  computed,
  ChangeDetectionStrategy,
  DestroyRef,
  HostListener,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  FormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  CityDetail,
  CityListItem,
  MasterDataStatus,
  canPerform,
} from '../../../../Shared/models/global-master.model';
import { MasterService } from '../master.service';

/* ───────────────────── Models ───────────────────── */
export interface CityModel {
  id: string;
  cityCode: string;
  cityName: string;
  displayName?: string | null;
  countryId: string;
  countryName?: string | null;
  stateProvinceId: string;
  stateProvinceName?: string | null;
  defaultPostalCodePattern?: string | null;
  isMetro: boolean;
  latitude?: number | null;
  longitude?: number | null;
  status: string | null;
  isActive: boolean;
  notes?: string | null;
  createdAt: Date | string;
  createdBy?: string | null;
  updatedAt?: Date | string | null;
  updatedBy?: string | null;

  /** A shared platform row. Read-only to an Organisation; only SuperAdmin may change it. */
  isPlatformRow: boolean;

  /** Sent back on the next write. A stale one answers 409 rather than overwriting somebody. */
  version: number;

  /** What the SERVER says this caller may do. */
  permittedActions: string[];
}

/** The display label this screen shows, and the code the API takes. */
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

export interface CountryModel {
  id: string;
  countryName: string;
  countryCode: string;
  isActive: boolean;
}

export interface StateProvinceModel {
  id: string;
  stateProvinceName: string;
  stateProvinceCode: string;
  countryId: string;
  isActive: boolean;
}

export const CITY_STATUS = {
  Draft: 'Draft',
  Active: 'Active',
  Inactive: 'Inactive',
} as const;
export type CityStatus = (typeof CITY_STATUS)[keyof typeof CITY_STATUS];
export const CITY_STATUS_ALL: CityStatus[] = Object.values(CITY_STATUS);

export interface ToastMessage {
  id: number;
  type: 'success' | 'error' | 'warning' | 'info';
  title: string;
  message: string;
}

/* ───────────────────── Component ───────────────────── */
@Component({
  selector: 'app-city',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  templateUrl: './city.html',
  styleUrl: './city.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CityComponent implements OnInit {
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  private masters = inject(MasterService);

  /* ---------- View mode ---------- */
  mode = signal<'list' | 'form'>('list');
  isEdit = signal(false);
  pageTitle = signal('City List');
  pageSubTitle = signal('Manage your cities');

  /* ---------- Shared state ---------- */
  isInitialized = signal(false);
  isLoading = signal(false);
  isSubmitting = signal(false);

  /* ---------- Toasts ---------- */
  toasts = signal<ToastMessage[]>([]);
  private toastCounter = 0;

  /* ---------- List data ---------- */
  cities = signal<CityModel[]>([]);
  filteredCities = signal<CityModel[]>([]);
  countries = signal<CountryModel[]>([]);
  states = signal<StateProvinceModel[]>([]);

  searchText = '';
  selectedStatus = '';
  selectedCountryId = '';
  selectedStateId = '';

  currentPage = signal(1);
  pageSize = signal(10);
  pageWindowSize = 2;

  selectedCity = signal<CityModel | null>(null);
  canDeactivate = signal(true);
  canDelete = signal(true);

  showView = signal(false);
  showActivateModal = signal(false);
  showDeactivateModal = signal(false);
  showDeleteModal = signal(false);

  /* ---------- Row action menu (⋮) + mobile filters drawer ---------- */
  openMenuId = signal<string | null>(null);
  filtersOpen = signal(false);

  /* ---------- Form ---------- */
  form!: FormGroup;
  filteredStates = signal<StateProvinceModel[]>([]);
  countryError = signal<string | null>(null);
  stateError = signal<string | null>(null);
  statusError = signal<string | null>(null);
  private existingId: string | null = null;
  private isInitializing = false;
  cityStatusList = CITY_STATUS_ALL;

  /* ---------- Computed ---------- */
  /** The server's totals. A page of ten cannot say how many cities the catalogue holds. */
  totalCountFromServer = signal(0);
  activeCountFromServer = signal(0);
  inactiveCountFromServer = signal(0);
  totalPagesFromServer = signal(1);

  totalPages = computed(() => Math.max(1, this.totalPagesFromServer()));

  /** The server already paged the result, so this IS the page it returned. */
  pagedCities = computed(() => this.filteredCities());

  startPage = computed(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    let start = Math.max(1, current - Math.floor(this.pageWindowSize / 2));
    if (start + this.pageWindowSize - 1 > total) {
      start = Math.max(1, total - this.pageWindowSize + 1);
    }
    return start;
  });

  endPage = computed(() =>
    Math.min(this.startPage() + this.pageWindowSize - 1, this.totalPages())
  );

  /** Last row index (1-based) shown on the current page, for the
   *  "Showing X–Y of Z" pagination summary. */
  pageRangeEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize(), this.totalCountFromServer())
  );

  pageNumbers = computed(() => {
    const pages: number[] = [];
    for (let i = this.startPage(); i <= this.endPage(); i++) {
      pages.push(i);
    }
    return pages;
  });

  /**
   * The countries offered in the FILTER.
   *
   * EVERY ACTIVE COUNTRY, not only those with a city on the current page. Narrowing to the loaded
   * rows made the filter useless the moment paging moved to the server: the country you wanted to
   * filter by was, by definition, the one whose cities were not on screen.
   */
  tableCountries = computed(() =>
    this.countries()
      .filter((country) => country.isActive)
      .sort((left, right) => left.countryName.localeCompare(right.countryName))
  );

  /** Every state in the chosen country, for the same reason. */
  tableStatesForDropdown = computed(() => {
    const list = this.selectedCountryId
      ? this.states().filter((state) => state.countryId === this.selectedCountryId)
      : this.states();

    return [...list].sort((left, right) =>
      left.stateProvinceName.localeCompare(right.stateProvinceName)
    );
  });

  activeCount = computed(() => this.activeCountFromServer());

  inactiveCount = computed(() => this.inactiveCountFromServer());

  /* ───────────────────── Lifecycle ───────────────────── */
  ngOnInit(): void {
    this.loadReferenceData();
    this.form = this.fb.group({
      cityCode: ['', [Validators.required, Validators.maxLength(15)]],
      cityName: ['', [Validators.required, Validators.maxLength(150)]],
      displayName: ['', Validators.maxLength(200)],
      countryId: ['', Validators.required],
      stateProvinceId: ['', Validators.required],
      defaultPostalCodePattern: ['', Validators.maxLength(100)],
      latitude: [null as number | null, [Validators.min(-90), Validators.max(90)]],
      longitude: [null as number | null, [Validators.min(-180), Validators.max(180)]],
      isMetro: [false],
      status: ['', Validators.required],
      notes: [''],
    });

    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        const id = params.get('id');
        const url = this.router.url;
        if (url.includes('/create') || url.endsWith('/cities/create')) {
          this.openCreate();
        } else if (id && id !== 'create') {
          this.openEdit(id);
        } else if (!id) {
          this.openList();
        }
      });

    this.isInitialized.set(true);
  }

  /* ─────────────── Explicit navigation (buttons call these directly) ───────────────
     These flip the view immediately via the signals, then sync the URL.
     They don't depend on the paramMap subscription firing, so the buttons
     work even if app-level routing isn't fully wired up yet. */
  goToList(): void {
    this.openList();
    this.router.navigateByUrl('/cities');
  }

  goToCreate(): void {
    this.openCreate();
    this.router.navigateByUrl('/cities/create');
  }

  goToEdit(id: string): void {
    this.openEdit(id);
    this.router.navigateByUrl(`/cities/${id}`);
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.openMenuId()) {
      this.openMenuId.set(null);
    } else if (this.showView()) {
      this.closeView();
    } else if (this.showActivateModal()) {
      this.showActivateModal.set(false);
    } else if (this.showDeactivateModal()) {
      this.showDeactivateModal.set(false);
    } else if (this.showDeleteModal()) {
      this.showDeleteModal.set(false);
    }
  }

  /** Closes the open row action-menu when clicking anywhere outside it. */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.openMenuId()) return;
    const target = event.target as HTMLElement;
    if (!target.closest('.action-menu-wrap')) {
      this.openMenuId.set(null);
    }
  }

  toggleRowMenu(id: string, event: Event): void {
    event.stopPropagation();
    this.openMenuId.set(this.openMenuId() === id ? null : id);
  }

  closeRowMenu(): void {
    this.openMenuId.set(null);
  }

  toggleFiltersDrawer(): void {
    this.filtersOpen.update((v) => !v);
  }

  /* ───────────────────── Mode helpers ───────────────────── */
  openList(): void {
    this.mode.set('list');
    this.isEdit.set(false);
    this.pageTitle.set('City List');
    this.pageSubTitle.set('Manage your cities');
    this.loadListData();
  }

  openCreate(): void {
    this.mode.set('form');
    this.isEdit.set(false);
    this.existingId = null;
    this.pageTitle.set('Create City');
    this.pageSubTitle.set('Create new city');
    this.form.reset({
      cityCode: '',
      cityName: '',
      displayName: '',
      countryId: '',
      stateProvinceId: '',
      defaultPostalCodePattern: '',
      latitude: null,
      longitude: null,
      isMetro: false,
      status: '',
      notes: '',
    });
    this.form.get('cityCode')?.enable();
    this.form.get('countryId')?.enable();
    this.form.get('stateProvinceId')?.enable();
    this.filteredStates.set([]);
    this.countryError.set(null);
    this.stateError.set(null);
    this.statusError.set(null);
  }

  /**
   * Opens the edit form.
   *
   * IT FETCHES THE RECORD rather than looking in the loaded page. A deep link, a refresh, or a
   * row on a page this screen has not loaded would all have failed the old lookup and bounced
   * the person back to the list with "not found" for a city that exists.
   *
   * THREE FIELDS ARE DISABLED, and they are the three the API has no update for: the code, the
   * country and the state. A city's code identifies it within its state; re-parenting it would
   * silently rewrite the geography of every address beneath it. Delete and recreate is the honest
   * operation, which is why the form does not pretend otherwise.
   */
  openEdit(id: string): void {
    this.mode.set('form');
    this.isEdit.set(true);
    this.existingId = id;
    this.pageTitle.set('Edit City');
    this.pageSubTitle.set('Update city details');
    this.isLoading.set(true);

    this.masters
      .getCity(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          const existing = this.toViewModelFromDetail(detail);

          this.isInitializing = true;
          this.form.patchValue({
            cityCode: existing.cityCode,
            cityName: existing.cityName,
            displayName: existing.displayName,
            countryId: existing.countryId,
            stateProvinceId: existing.stateProvinceId,
            defaultPostalCodePattern: existing.defaultPostalCodePattern,
            latitude: existing.latitude,
            longitude: existing.longitude,
            isMetro: existing.isMetro,
            status: existing.status,
            notes: existing.notes,
          });

          // The one state that matters here: the city's own. The picker is disabled on edit, so
          // there is nothing to choose between and no reason to fetch the country's whole list.
          this.filteredStates.set([
            {
              id: existing.stateProvinceId,
              stateProvinceName: existing.stateProvinceName ?? '',
              stateProvinceCode: '',
              countryId: existing.countryId,
              isActive: true,
            },
          ]);

          this.editingVersion = existing.version;
          this.form.get('cityCode')?.disable();
          this.form.get('countryId')?.disable();
          this.form.get('stateProvinceId')?.disable();
          this.isInitializing = false;
          this.isLoading.set(false);
        },
        error: (error) => {
          this.isLoading.set(false);
          this.showToast(
            'error',
            'Not found',
            apiErrorMessage(error, 'The requested city could not be found.')
          );
          this.openList();
          this.router.navigateByUrl('/cities');
        },
      });
  }

  /** The concurrency stamp of the record being edited, sent back with the update. */
  private editingVersion = 0;

  /* ───────────────────── Reference data and loading ───────────────────── */

  /**
   * The country and state dropdowns.
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
          this.countries.set(
            reference.countries.map((country) => ({
              id: country.id,
              countryName: country.name,
              countryCode: country.code,
              isActive: country.status === 'active',
            }))
          );

          // THE STATE LIST CARRIES NO COUNTRY on the unfiltered reference call, so the country
          // is resolved per state when a country is chosen - see onCountryChange, which asks the
          // server for that country's states rather than filtering a list it cannot filter.
          this.states.set([]);
        },
        error: () =>
          this.showToast(
            'warning',
            'Reference data',
            'Countries could not be loaded. The form dropdowns will be empty.'
          ),
      });
  }

  /**
   * Fetches one page from the API.
   *
   * THE FILTERS GO TO THE SERVER. Filtering in the browser can only filter what has been
   * downloaded - the current page - so a search for a city on page four came back empty and
   * looked like a missing record. More to the point, only the server can apply the Organisation
   * filter: a static array has no idea who is asking.
   */
  private loadListData(): void {
    this.isLoading.set(true);

    this.masters
      .searchCities({
        page: this.currentPage(),
        pageSize: this.pageSize(),
        search: this.searchText.trim() || undefined,
        countryId: this.selectedCountryId || undefined,
        stateProvinceId: this.selectedStateId || undefined,
        status: this.selectedStatus ? STATUS_CODES[this.selectedStatus] : undefined,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          const rows = page.items.map((item) => this.toViewModel(item));
          this.cities.set(rows);
          this.filteredCities.set(rows);
          this.totalCountFromServer.set(page.totalCount);
          this.totalPagesFromServer.set(page.totalPages);
          this.currentPage.set(page.page);
          this.isLoading.set(false);
          this.openMenuId.set(null);
        },
        error: (error) => {
          this.cities.set([]);
          this.filteredCities.set([]);
          this.isLoading.set(false);
          this.showToast(
            'error',
            'Could not load',
            apiErrorMessage(error, 'The city catalogue could not be loaded.')
          );
        },
      });

    this.masters
      .searchCities({ pageSize: 1, status: 'active' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => this.activeCountFromServer.set(page.totalCount) });

    this.masters
      .searchCities({ pageSize: 1, status: 'inactive' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => this.inactiveCountFromServer.set(page.totalCount) });
  }

  private toViewModel(item: CityListItem): CityModel {
    return {
      id: item.id,
      cityCode: item.cityCode,
      cityName: item.cityName,
      displayName: item.displayName,
      countryId: item.countryId,
      countryName: item.countryName,
      stateProvinceId: item.stateProvinceId,
      stateProvinceName: item.stateProvinceName,
      defaultPostalCodePattern: null,
      isMetro: item.isMetro,
      latitude: item.latitude,
      longitude: item.longitude,
      status: STATUS_LABELS[item.status] ?? item.statusDescription,
      isActive: item.isActive,
      notes: null,
      createdAt: item.updatedAtUtc ?? new Date(),
      updatedAt: item.updatedAtUtc ?? null,
      isPlatformRow: item.isPlatformRow,
      version: item.version,
      permittedActions: [],
    };
  }

  private toViewModelFromDetail(detail: CityDetail): CityModel {
    return {
      id: detail.id,
      cityCode: detail.cityCode,
      cityName: detail.cityName,
      displayName: detail.displayName,
      countryId: detail.countryId,
      countryName: detail.countryName,
      stateProvinceId: detail.stateProvinceId,
      stateProvinceName: detail.stateProvinceName,
      defaultPostalCodePattern: detail.defaultPostalCodePattern,
      isMetro: detail.isMetro,
      latitude: detail.latitude,
      longitude: detail.longitude,
      status: STATUS_LABELS[detail.status] ?? detail.statusDescription,
      isActive: detail.isActive,
      notes: detail.notes,
      createdAt: detail.createdAtUtc,
      createdBy: detail.createdByUserId,
      updatedAt: detail.updatedAtUtc,
      updatedBy: detail.updatedByUserId,
      isPlatformRow: detail.isPlatformRow,
      version: detail.version,
      permittedActions: detail.permittedActions,
    };
  }

  /* ───────────────────── List methods ───────────────────── */
  onSearch(value: string): void {
    this.searchText = value;
    this.applyFilters();
  }

  onCountryChangeFilter(value: string): void {
    this.selectedCountryId = value;
    this.selectedStateId = '';
    this.applyFilters();
  }

  onStateChangeFilter(value: string): void {
    this.selectedStateId = value;
    this.applyFilters();
  }

  onStatusChangeFilter(value: string): void {
    this.selectedStatus = value;
    this.applyFilters();
  }

  clearFilters(): void {
    this.searchText = '';
    this.selectedStatus = '';
    this.selectedCountryId = '';
    this.selectedStateId = '';
    this.applyFilters();
  }

  applyFilters(): void {
    this.currentPage.set(1);
    this.loadListData();
  }

  onPageSizeChange(value: string): void {
    if (this.isLoading()) return;
    this.pageSize.set(+value || 10);
    this.currentPage.set(1);
    this.loadListData();
  }

  goToPage(page: number): void {
    if (
      this.isLoading() ||
      page < 1 ||
      page > this.totalPages() ||
      page === this.currentPage()
    ) {
      return;
    }

    this.currentPage.set(page);
    this.loadListData();
  }

  previousPage(): void {
    if (this.currentPage() > 1) this.goToPage(this.currentPage() - 1);
  }

  nextPage(): void {
    if (this.currentPage() < this.totalPages()) this.goToPage(this.currentPage() + 1);
  }

  onRefresh(): void {
    if (this.isLoading()) return;

    this.searchText = '';
    this.selectedStatus = '';
    this.selectedCountryId = '';
    this.selectedStateId = '';
    this.currentPage.set(1);

    // The cached reference data goes too: a country or state added elsewhere should appear in
    // this screen's dropdowns without a full reload.
    this.masters.invalidateReferenceData();
    this.loadReferenceData();
    this.loadListData();
    this.showToast('info', 'Refresh', 'Data refreshed');
  }

  /**
   * Opens the detail pane.
   *
   * IT FETCHES THE DETAIL rather than showing the grid row. The row carries no notes, no postal
   * pattern and no permitted actions - and the last of those decides which buttons may be drawn.
   */
  viewCity(city: CityModel): void {
    if (this.isLoading()) return;

    this.selectedCity.set(city);
    this.showView.set(true);

    this.masters
      .getCity(city.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.selectedCity.set(this.toViewModelFromDetail(detail));
          this.canDeactivate.set(canPerform(detail, 'Deactivate'));
          this.canDelete.set(canPerform(detail, 'Delete'));
        },
        error: (error) =>
          this.showToast(
            'error',
            'Could not open',
            apiErrorMessage(error, 'That city could not be opened.')
          ),
      });
  }

  closeView(): void {
    this.showView.set(false);
    this.selectedCity.set(null);
  }

  /** Opens the same inline details view used by desktop and mobile layouts. */
  openRowDetails(city: CityModel): void {
    this.viewCity(city);
  }

  confirmActivate(city: CityModel): void {
    // viewCity fetches the detail, which is what sets canDeactivate and canDelete from the
    // server's own answer. The pane is closed again: the modal is what the operator sees.
    this.viewCity(city);
    this.showView.set(false);
    this.showActivateModal.set(true);
  }

  activateConfirmed(): void {
    const city = this.selectedCity();
    if (!city) return;

    this.masters
      .activateCity(city.id, { expectedVersion: city.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('success', 'Activated', `City '${city.cityName}' activated successfully`);
          this.showActivateModal.set(false);
          this.selectedCity.set(null);
          this.masters.invalidateReferenceData();
          this.loadListData();
        },
        error: (error) => {
          this.showActivateModal.set(false);
          this.reportWriteFailure(error, 'The city could not be activated.');
        },
      });
  }

  confirmDeactivate(city: CityModel): void {
    // viewCity fetches the detail, which is what sets canDeactivate and canDelete from the
    // server's own answer. The pane is closed again: the modal is what the operator sees.
    this.viewCity(city);
    this.showView.set(false);
    this.showDeactivateModal.set(true);
  }

  deactivateConfirmed(): void {
    const city = this.selectedCity();
    if (!city || !this.canDeactivate()) return;

    this.masters
      .deactivateCity(city.id, { expectedVersion: city.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast(
            'warning',
            'Deactivated',
            `City '${city.cityName}' deactivated successfully`
          );
          this.showDeactivateModal.set(false);
          this.selectedCity.set(null);
          this.masters.invalidateReferenceData();
          this.loadListData();
        },
        error: (error) => {
          this.showDeactivateModal.set(false);
          this.reportWriteFailure(error, 'The city could not be deactivated.');
        },
      });
  }

  confirmDelete(city: CityModel): void {
    // viewCity fetches the detail, which is what sets canDeactivate and canDelete from the
    // server's own answer. The pane is closed again: the modal is what the operator sees.
    this.viewCity(city);
    this.showView.set(false);
    this.showDeleteModal.set(true);
  }

  deleteConfirmed(): void {
    const city = this.selectedCity();
    if (!city || !this.canDelete()) return;

    this.masters
      .deleteCity(city.id, { expectedVersion: city.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('error', 'Deleted', `City '${city.cityName}' deleted successfully`);
          this.showDeleteModal.set(false);
          this.selectedCity.set(null);
          this.currentPage.set(1);
          this.masters.invalidateReferenceData();
          this.loadListData();
        },
        error: (error) => {
          this.showDeleteModal.set(false);
          this.reportWriteFailure(error, 'The city could not be deleted.');
        },
      });
  }

  /* ───────────────────── Form methods ───────────────────── */
  /**
   * Loads the chosen country's states.
   *
   * IT ASKS THE SERVER rather than filtering a list. The unfiltered reference call returns states
   * without their country, so a local filter had nothing to filter on - and downloading every
   * state on the platform to pick one country's is the wrong shape of call anyway.
   */
  onCountryChange(): void {
    this.countryError.set(null);
    this.stateError.set(null);

    const countryId = this.form.get('countryId')?.value;

    if (!this.isInitializing) {
      this.form.patchValue({ stateProvinceId: '' });
    }

    if (!countryId) {
      this.filteredStates.set([]);
      return;
    }

    this.masters
      .lookupStates(countryId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lookup) =>
          this.filteredStates.set(
            lookup.map((state) => ({
              id: state.id,
              stateProvinceName: state.name,
              stateProvinceCode: state.code,
              countryId,
              isActive: state.status === 'active',
            }))
          ),
        error: () => {
          this.filteredStates.set([]);
          this.stateError.set('The states for that country could not be loaded.');
        },
      });
  }

  onStateChange(): void {
    this.stateError.set(null);
  }

  onStatusChange(): void {
    this.statusError.set(null);
  }

  onCityNameInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const cleaned = input.value.replace(/[0-9]/g, '');
    if (cleaned === input.value) return;

    const cursorPos = input.selectionStart ?? cleaned.length;
    const removedBeforeCursor = (input.value.slice(0, cursorPos).match(/[0-9]/g) || []).length;
    const newPos = Math.max(0, cursorPos - removedBeforeCursor);

    this.form.get('cityName')?.setValue(cleaned, { emitEvent: false });
    queueMicrotask(() => input.setSelectionRange(newPos, newPos));
  }

  getValidationClass(controlName: string): string {
    const ctrl = this.form.get(controlName);
    if (!ctrl) return '';
    if (ctrl.invalid && (ctrl.dirty || ctrl.touched)) return 'is-invalid';
    if (ctrl.valid && (ctrl.dirty || ctrl.touched)) return 'is-valid';
    return '';
  }

  getStatusPillClass(status: string | null | undefined): string {
    switch (status) {
      case 'Active': return 'status-active';
      case 'Inactive': return 'status-inactive';
      case 'Draft': return 'status-draft';
      default: return 'status-default';
    }
  }

  onSubmit(): void {
    if (this.isSubmitting()) return;
    this.form.markAllAsTouched();

    this.countryError.set(null);
    this.stateError.set(null);
    this.statusError.set(null);
    let customValid = true;

    const raw = this.form.getRawValue();
    if (!raw.countryId) {
      this.countryError.set('Country is required');
      customValid = false;
    }
    if (!raw.stateProvinceId) {
      this.stateError.set('State/Province is required');
      customValid = false;
    }
    if (!raw.status) {
      this.statusError.set('Status is required');
      customValid = false;
    }

    if (this.form.invalid || !customValid) return;

    // isSubmitting is cleared by save() when the call completes, not here: the write is
    // asynchronous now, and clearing it immediately would re-enable the button mid-flight.
    this.save();
  }

  /**
   * Saves the form.
   *
   * THE COUNTRY IS NOT SENT, and the API has no field for it: it takes the country from the
   * chosen STATE, which is the only way its denormalised country column can be guaranteed to
   * agree with the state above it. The old code sent both and could have written a city whose
   * country and state disagreed.
   *
   * THE DUPLICATE CHECK IS THE SERVER'S. A local scan could not see a city created by a colleague
   * a moment ago, nor one on a page this screen has not fetched - and the rule is per STATE,
   * which the browser cannot evaluate across pages.
   */
  private save(): void {
    const raw = this.form.getRawValue();
    const cityCode = (raw.cityCode || '').toUpperCase().trim();
    const cityName = (raw.cityName || '').trim();
    const displayName = (raw.displayName || '').trim() || null;
    const notes = (raw.notes || '').trim() || null;

    this.isSubmitting.set(true);

    if (this.isEdit() && this.existingId) {
      this.masters
        .updateCity(this.existingId, {
          expectedVersion: this.editingVersion,
          cityName,
          displayName,
          defaultPostalCodePattern: raw.defaultPostalCodePattern || null,
          isMetro: !!raw.isMetro,
          latitude: raw.latitude,
          longitude: raw.longitude,
          notes,

          // Explicit, because a null latitude already means "unchanged" in a partial update -
          // so there would otherwise be no way to un-geocode a city that was geocoded wrongly.
          clearCoordinates: raw.latitude === null && raw.longitude === null,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => {
            this.isSubmitting.set(false);
            this.showToast('success', 'Updated', `City '${cityName}' updated successfully`);
            this.masters.invalidateReferenceData();
            this.goToList();
          },
          error: (error) => {
            this.isSubmitting.set(false);
            this.reportSaveFailure(error, 'The city could not be updated.');
          },
        });

      return;
    }

    this.masters
      .createCity({
        cityCode,
        cityName,
        stateProvinceId: raw.stateProvinceId,
        displayName,
        defaultPostalCodePattern: raw.defaultPostalCodePattern || null,
        isMetro: !!raw.isMetro,
        latitude: raw.latitude,
        longitude: raw.longitude,
        status: raw.status ? STATUS_CODES[raw.status] : 'draft',
        notes,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.showToast('success', 'Added', `City '${created.cityName}' added successfully`);
          this.masters.invalidateReferenceData();
          this.goToList();
        },
        error: (error) => {
          this.isSubmitting.set(false);
          this.reportSaveFailure(error, 'The city could not be created.');
        },
      });
  }

  /**
   * Reports a failed create or update.
   *
   * A DUPLICATE GOES ON THE CODE FIELD, where the person can act on it. A 409 is named separately
   * because it means somebody else changed the row, and the fix is to reload rather than retry.
   */
  private reportSaveFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'DUPLICATE_CODE') {
      this.showToast(
        'error',
        'Validation error',
        apiErrorMessage(error, 'City code already exists in this state/province')
      );
      return;
    }

    if (code === 'CONCURRENCY_CONFLICT') {
      this.showToast(
        'warning',
        'Record changed',
        'Somebody else changed this city. Reload and try again.'
      );
      return;
    }

    this.showToast('error', 'Save failed', apiErrorMessage(error, fallback));
  }

  /**
   * Reports a failed activate, deactivate or delete.
   *
   * A DEPENDENCY REFUSAL is named because it is a fact the person can do something about, and
   * burying it in a generic failure would leave them retrying an operation that cannot succeed.
   */
  private reportWriteFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.showToast('warning', 'Record changed', 'Somebody else changed this city. Refreshing.');
      this.loadListData();
      return;
    }

    if (code === 'RECORD_IN_USE' || code === 'DEPENDENCY_EXISTS') {
      this.showToast('warning', 'Still in use', apiErrorMessage(error, fallback));
      return;
    }

    this.showToast('error', 'Action failed', apiErrorMessage(error, fallback));
  }

  cancel(): void {
    this.goToList();
  }

  /** Reverts the form to its last-loaded values (empty defaults on create,
   *  the original record's values on edit) without leaving the form view. */
  resetForm(): void {
    if (this.isSubmitting()) return;
    this.countryError.set(null);
    this.stateError.set(null);
    this.statusError.set(null);
    if (this.isEdit() && this.existingId) {
      this.openEdit(this.existingId);
    } else {
      this.openCreate();
    }
  }

  /* ───────────────────── Helpers ───────────────────── */
  getStatusDotClass(status: string | null | undefined): string {
    switch (status) {
      case 'Active': return 'dot-success';
      case 'Inactive': return 'dot-danger';
      case 'Draft': return 'dot-warning';
      default: return 'dot-secondary';
    }
  }

  getStatusBadgeClass(status: string | null | undefined): string {
    switch (status) {
      case 'Active': return 'badge-success';
      case 'Inactive': return 'badge-danger';
      case 'Draft': return 'badge-warning';
      default: return 'badge-secondary';
    }
  }

  getPlainText(html: string | null | undefined): string {
    if (!html) return '—';
    return html.replace(/<[^>]*>/g, '');
  }

  private delay(ms: number): Promise<void> {
    return new Promise((r) => setTimeout(r, ms));
  }

  /* ---------- Toasts ---------- */
  private showToast(
    type: ToastMessage['type'],
    title: string,
    message: string
  ): void {
    const id = ++this.toastCounter;
    this.toasts.update((list) => [...list, { id, type, title, message }]);
    setTimeout(() => this.dismissToast(id), 4000);
  }

  dismissToast(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}
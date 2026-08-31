import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MasterService } from '../master.service';
import { apiErrorMessage, apiFieldErrors } from '../../../../Shared/models/api-response.model';
import {
  CountryDetail,
  CountryListItem,
  CreateCountryRequest,
  EnumOption,
  GeographicRegion,
  UpdateCountryRequest,
  canPerform,
} from '../../../../Shared/models/global-master.model';

/**
 * The row shape the country template binds to.
 *
 * IT IS THE SERVER'S LIST ITEM PLUS THE FIELDS THE FORM NEEDS. The grid gets a narrow
 * projection from `GET /masters/countries`, while the form needs `numericCode`, `postalCodePattern`
 * and `notes`, which only the detail endpoint returns — so opening the editor fetches the full
 * record and merges it in. That keeps the list query cheap without the form silently blanking
 * fields it never loaded, which is what a single shared model would have done.
 */
export interface CountryModel extends Partial<CountryDetail> {
  id: string;
  countryCode: string;
  countryName: string;
  iso2: string;
  isActive: boolean;
  sortOrder: number;
  version: number;
}

type ToastType = 'success' | 'error' | 'warning' | 'info';
type ViewMode = 'list' | 'form';
type StatusFilter = 'all' | 'active' | 'inactive';

interface Toast {
  id: number;
  type: ToastType;
  title: string;
  message: string;
}

/**
 * The Country master.
 *
 * WHAT CHANGED. Every row on this screen used to live in a signal and nowhere else: saving
 * updated an array, deleting spliced one out, and a refresh lost the lot. It now reads from and
 * writes to `IAM /api/v1/masters/countries`, which is where the catalogue moved when the
 * GlobalMaster service was merged into IAM.
 *
 * THE SERVER DECIDES WHAT MAY BE DONE, NOT THIS COMPONENT. `permittedActions` on a detail
 * response already accounts for the caller's permissions AND the record's state — a shared
 * platform row is read-only to an Organisation, and a country with states beneath it cannot be
 * deleted. `canPerform` reads that answer instead of this file re-deriving a rule that would
 * eventually disagree with the API.
 *
 * FILTERING IS SERVER-SIDE, PAGING IS CLIENT-SIDE, and the split is deliberate rather than
 * accidental. Search, region and status go to the API so the work happens where the index is;
 * the returned set is then paged in the browser because the existing template computes its own
 * pager from `filteredCountries()`, and rewriting a working, themed pager was not part of this
 * change. `FetchLimit` is the guard that keeps that honest.
 */
@Component({
  selector: 'app-country',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './country.html',
  styleUrl: './country.css',
})
export class Country implements OnInit {
  private readonly masterService = inject(MasterService);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * The most rows fetched for one filter.
   *
   * The catalogue is a few hundred rows at most, so this is generous rather than tight. It
   * exists so that a mistaken filter cannot pull an unbounded set into the browser, and
   * `isTruncated` tells the person when they have hit it instead of silently showing a
   * partial list.
   */
  private static readonly FetchLimit = 500;

  /**
   * Region options, served by the API rather than hard-coded.
   *
   * They used to be a literal array in this file, which meant a region added on the server was
   * invisible here until somebody remembered to add it in two places. `value` is what the API
   * expects back; `label` is what the person reads.
   */
  readonly regions = signal<EnumOption[]>([]);

  // ---------- view state ----------
  readonly view = signal<ViewMode>('list');
  readonly isEdit = signal(false);
  readonly statusSectionOpen = signal(false);
  readonly isLoading = signal(false);
  readonly isSaving = signal(false);
  readonly isTruncated = signal(false);

  // ---------- list state ----------
  readonly countries = signal<CountryModel[]>([]);
  readonly searchText = signal('');
  readonly selectedRegion = signal('');
  readonly selectedStatus = signal('');
  readonly statusFilter = signal<StatusFilter>('all');
  readonly currentPage = signal(1);
  readonly pageSize = signal(10);
  readonly startPage = signal(1);
  private readonly pageWindowSize = 2;

  readonly selectedCountry = signal<CountryModel | null>(null);
  readonly selectedCompany = signal<CountryModel | null>(null);
  readonly canDeactivate = signal(true);
  readonly canDelete = signal(true);
  readonly showActivateModal = signal(false);
  readonly showDeactivateModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly showViewPanel = signal(false);
  readonly showRowDetailsModal = signal(false);

  // ---------- form state ----------
  country: CountryModel = this.createNewCountry();
  formErrors: Record<string, string> = {};
  touched: Record<string, boolean> = {};
  private editSnapshot: CountryModel | null = null;

  // ---------- toasts ----------
  readonly toasts = signal<Toast[]>([]);
  private toastIdCounter = 0;

  /**
   * ngOnInit RATHER THAN ngAfterViewInit, which is what this used to use.
   *
   * A data fetch has nothing to do with the view being ready, and starting one in
   * `ngAfterViewInit` writes to signals the template has already rendered — the source of
   * NG0100 "expression changed after it was checked" in development.
   */
  ngOnInit(): void {
    this.loadReferenceData();
    this.loadCountries();
  }

  // ================= loading =================

  /**
   * The region dropdown, and nothing else from the shared reference-data call.
   *
   * `MasterService` caches that call for the life of the application, so the four sibling
   * Masters screens share the one request rather than each issuing their own.
   */
  private loadReferenceData(): void {
    this.masterService
      .getReferenceData()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => this.regions.set(data.regions),
        // A failed lookup leaves the region filter empty rather than blocking the grid. The
        // countries themselves are the point of the screen; the dropdown is a convenience.
        error: () => this.regions.set([]),
      });
  }

  protected loadCountries(): void {
    this.isLoading.set(true);

    this.masterService
      .searchCountries({
        search: this.searchText().trim() || undefined,
        region: (this.selectedRegion() as GeographicRegion) || undefined,
        status: this.resolveStatusFilter(),
        page: 1,
        pageSize: Country.FetchLimit,
        sort: 'sortOrder',
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          // REPLACED, NOT APPENDED. The previous version did
          // `countries.update(list => [...list, response.value.data])`, which appended the whole
          // payload as a single element on every load - so a second refresh produced a list of
          // arrays and the grid rendered nothing.
          this.countries.set(page.items.map((item) => this.toModel(item)));
          this.isTruncated.set(page.totalCount > page.items.length);
          this.isLoading.set(false);
        },
        error: (error) => {
          this.isLoading.set(false);
          this.showToast('error', 'Could not load countries', apiErrorMessage(error));
        },
      });
  }

  /**
   * Turns the two status controls into the single value the API takes.
   *
   * The quick pills win over the dropdown, which is the precedence the template already
   * implied by clearing one when the other is set.
   */
  private resolveStatusFilter(): 'active' | 'inactive' | undefined {
    const pill = this.statusFilter();
    if (pill === 'active' || pill === 'inactive') {
      return pill;
    }

    const dropdown = this.selectedStatus();
    if (dropdown === 'Active') return 'active';
    if (dropdown === 'Inactive') return 'inactive';

    return undefined;
  }

  private toModel(item: CountryListItem): CountryModel {
    return { ...item };
  }

  // ================= helpers =================
  private createNewCountry(): CountryModel {
    return {
      id: '',
      countryCode: '',
      countryName: '',
      officialName: '',
      region: null,
      iso2: '',
      iso3: '',
      numericCode: '',
      defaultCurrencyCode: '',
      hasStates: true,
      postalCodePattern: '',
      phoneCountryCode: '',
      isActive: true,
      sortOrder: 0,
      version: 0,
    };
  }

  /**
   * The flag for a row.
   *
   * The server sends `flagEmoji` already rendered, so every client shows the same glyph. The
   * local computation is kept only as the fallback for a row being typed into the form, which
   * has no server answer yet.
   */
  flagEmoji(iso2: string | undefined): string {
    const code = (iso2 || '').trim().toUpperCase();
    if (code.length !== 2) return '🏳️';
    const points = [...code].map((ch) => 127397 + ch.charCodeAt(0));
    return String.fromCodePoint(...points);
  }

  get pageTitle(): string {
    return this.isEdit() ? 'Edit Country' : 'Create Country';
  }

  get pageSubTitle(): string {
    return this.isEdit()
      ? 'Update and manage country information'
      : 'Add a new country to your master data';
  }

  // ================= list computed =================

  /**
   * The regions actually present in the fetched rows, for the grid's own filter chips.
   *
   * Labels come from the server's option list where one matches, so the chip reads "North
   * America" rather than "northAmerica".
   */
  readonly tableRegions = computed<string[]>(() =>
    Array.from(
      new Set(this.countries().filter((c) => !!c.region).map((c) => c.region as string)),
    ).sort(),
  );

  /** The label for a region value, falling back to the raw value when the list has not loaded. */
  regionLabel(value: string | null | undefined): string {
    if (!value) return '';
    return this.regions().find((option) => option.value === value)?.label ?? value;
  }

  /**
   * The rows to page over.
   *
   * The server has already applied search, region and status, so this is a straight sort
   * rather than a second filter — filtering twice is how a screen ends up showing fewer rows
   * than its own count claims.
   */
  readonly filteredCountries = computed<CountryModel[]>(() =>
    [...this.countries()].sort((a, b) => {
      const byOrder = (a.sortOrder ?? 0) - (b.sortOrder ?? 0);
      return byOrder !== 0 ? byOrder : a.countryName.localeCompare(b.countryName);
    }),
  );

  readonly activeCount = computed(() => this.filteredCountries().filter((c) => c.isActive).length);
  readonly inactiveCount = computed(() => this.filteredCountries().filter((c) => !c.isActive).length);

  readonly totalPages = computed(() =>
    this.filteredCountries().length === 0
      ? 1
      : Math.ceil(this.filteredCountries().length / this.pageSize()),
  );

  readonly pagedCountries = computed<CountryModel[]>(() => {
    const start = (this.currentPage() - 1) * this.pageSize();
    return this.filteredCountries().slice(start, start + this.pageSize());
  });

  readonly endPage = computed(() =>
    Math.min(this.startPage() + this.pageWindowSize - 1, this.totalPages()),
  );

  readonly pageNumbers = computed<number[]>(() => {
    const arr: number[] = [];
    for (let i = this.startPage(); i <= this.endPage(); i++) arr.push(i);
    return arr;
  });

  // ================= list behaviour =================
  private resetPaging(): void {
    this.currentPage.set(1);
    this.startPage.set(1);
  }

  onSearchChange(value: string): void {
    this.searchText.set(value);
    this.resetPaging();
    this.loadCountries();
  }

  onRegionChange(value: string): void {
    this.selectedRegion.set(value);
    this.resetPaging();
    this.loadCountries();
  }

  onStatusChange(value: string): void {
    this.selectedStatus.set(value);
    this.resetPaging();
    this.loadCountries();
  }

  setStatusFilter(filter: StatusFilter): void {
    this.statusFilter.set(filter);
    if (filter !== 'all') {
      this.selectedStatus.set('');
    }
    this.resetPaging();
    this.loadCountries();
  }

  onPageSizeChange(value: string): void {
    this.pageSize.set(parseInt(value, 10) || 10);
    this.resetPaging();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.currentPage.set(page);
    if (page < this.startPage()) this.startPage.set(page);
    if (page > this.endPage()) this.startPage.set(Math.max(1, page - this.pageWindowSize + 1));
  }

  previousPage(): void {
    this.goToPage(this.currentPage() - 1);
  }

  nextPage(): void {
    this.goToPage(this.currentPage() + 1);
  }

  refresh(): void {
    this.searchText.set('');
    this.selectedRegion.set('');
    this.selectedStatus.set('');
    this.statusFilter.set('all');
    this.resetPaging();
    this.loadCountries();
    this.showToast('success', 'Reload successful', 'Country data has been refreshed');
  }

  // ================= navigation =================
  openCreate(): void {
    this.isEdit.set(false);
    this.country = this.createNewCountry();
    this.editSnapshot = null;
    this.formErrors = {};
    this.touched = {};
    this.statusSectionOpen.set(false);
    this.view.set('form');
  }

  /**
   * Opens the editor.
   *
   * The FULL record is fetched rather than reusing the grid row, because the form edits fields
   * the list projection does not carry - the numeric code, the postal pattern, the notes. Editing
   * from the row alone would send those back as blank and quietly erase them.
   */
  openEdit(c: CountryModel): void {
    this.isEdit.set(true);
    this.country = { ...c };
    this.editSnapshot = { ...c };
    this.formErrors = {};
    this.touched = {};
    this.statusSectionOpen.set(false);
    this.view.set('form');

    this.masterService
      .getCountry(c.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.country = { ...detail };
          this.editSnapshot = { ...detail };
        },
        error: (error) =>
          this.showToast('error', 'Could not load the country', apiErrorMessage(error)),
      });
  }

  cancelForm(): void {
    this.view.set('list');
  }

  resetFormFields(): void {
    this.country =
      this.isEdit() && this.editSnapshot ? { ...this.editSnapshot } : this.createNewCountry();
    this.formErrors = {};
    this.touched = {};
  }

  toggleStatusSection(): void {
    this.statusSectionOpen.update((open) => !open);
  }

  // ================= form field handlers =================
  onCodeChange(): void {
    this.country.countryCode = (this.country.countryCode || '').toUpperCase().trim();
  }

  onCountryNameChange(): void {
    this.country.countryName = (this.country.countryName || '').trim();
  }

  onOfficialNameChange(): void {
    this.country.officialName = (this.country.officialName || '').trim();
  }

  blockDigits(event: Event): void {
    const el = event.target as HTMLInputElement;
    el.value = el.value.replace(/[0-9]/g, '');
  }

  blockNonPhone(event: Event): void {
    const el = event.target as HTMLInputElement;
    el.value = el.value.replace(/[^0-9+]/g, '');
  }

  markTouched(field: string): void {
    this.touched[field] = true;
  }

  fieldClass(field: string): string {
    if (this.formErrors[field] && this.touched[field]) return 'is-invalid';
    if (this.touched[field] && !this.formErrors[field]) return 'is-valid';
    return '';
  }

  hasError(field: string): boolean {
    return !!this.formErrors[field] && !!this.touched[field];
  }

  /**
   * The client-side checks.
   *
   * They exist to save a round trip on the obvious mistakes, NOT to be the validation. The API
   * runs FluentValidation over the same fields and its field errors are merged back onto
   * `formErrors` when a save is refused, so a rule that only exists on the server still lands
   * on the right control.
   */
  private validate(): boolean {
    this.formErrors = {};
    if (!this.country.countryCode?.trim()) {
      this.formErrors['countryCode'] = 'Country code is required';
    }
    if (!this.country.countryName?.trim()) {
      this.formErrors['countryName'] = 'Country name is required';
    }
    if (!this.country.iso2?.trim()) {
      this.formErrors['iso2'] = 'ISO 2-letter code is required';
    } else if (this.country.iso2.trim().length !== 2) {
      this.formErrors['iso2'] = 'ISO 2-letter code must be exactly 2 letters';
    }
    return Object.keys(this.formErrors).length === 0;
  }

  save(): void {
    ['countryCode', 'countryName', 'iso2'].forEach((f) => (this.touched[f] = true));

    this.country.countryCode = (this.country.countryCode || '').toUpperCase().trim();
    this.country.countryName = (this.country.countryName || '').trim();
    this.country.officialName = (this.country.officialName || '').trim();
    this.country.iso2 = (this.country.iso2 || '').toUpperCase().trim();
    this.country.iso3 = (this.country.iso3 || '').toUpperCase().trim();
    this.country.defaultCurrencyCode = (this.country.defaultCurrencyCode || '').toUpperCase().trim();

    if (!this.validate() || this.isSaving()) {
      return;
    }

    this.isSaving.set(true);

    // THE DUPLICATE CHECKS ARE GONE FROM HERE. They used to scan the in-memory array, which
    // could only ever see the rows this browser had loaded - so two administrators adding the
    // same country both passed. The unique index and the create handler answer 409, and that
    // answer is authoritative.
    if (this.isEdit()) {
      this.saveEdit();
    } else {
      this.saveNew();
    }
  }

  private saveNew(): void {
    const request: CreateCountryRequest = {
      countryCode: this.country.countryCode,
      countryName: this.country.countryName,
      iso2: this.country.iso2,
      officialName: this.country.officialName || null,
      region: (this.country.region as GeographicRegion) || null,
      iso3: this.country.iso3 || null,
      numericCode: this.country.numericCode || null,
      defaultCurrencyCode: this.country.defaultCurrencyCode || null,
      hasStates: this.country.hasStates ?? true,
      postalCodePattern: this.country.postalCodePattern || null,
      phoneCountryCode: this.country.phoneCountryCode || null,
      status: this.country.isActive ? 'active' : 'inactive',
      sortOrder: this.country.sortOrder ?? 0,
      notes: this.country.notes || null,
    };

    this.masterService
      .createCountry(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.isSaving.set(false);
          // The picker lists elsewhere in the application now have one more country in them.
          this.masterService.invalidateReferenceData();
          this.showToast('success', 'Added', `Country '${created.countryName}' added successfully`);
          this.view.set('list');
          this.loadCountries();
        },
        error: (error) => this.handleSaveError(error),
      });
  }

  private saveEdit(): void {
    const request: UpdateCountryRequest = {
      // The version the form was opened with. The server refuses the save if the record moved
      // underneath it, which is what stops two administrators overwriting one another.
      expectedVersion: this.country.version,
      countryName: this.country.countryName,
      officialName: this.country.officialName || null,
      region: (this.country.region as GeographicRegion) || null,
      iso2: this.country.iso2,
      iso3: this.country.iso3 || null,
      numericCode: this.country.numericCode || null,
      defaultCurrencyCode: this.country.defaultCurrencyCode || null,
      hasStates: this.country.hasStates ?? null,
      postalCodePattern: this.country.postalCodePattern || null,
      phoneCountryCode: this.country.phoneCountryCode || null,
      sortOrder: this.country.sortOrder ?? null,
      notes: this.country.notes || null,
    };

    this.masterService
      .updateCountry(this.country.id, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSaving.set(false);
          this.masterService.invalidateReferenceData();
          this.showToast(
            'success',
            'Updated',
            `Country '${this.country.countryName}' updated successfully`,
          );
          this.view.set('list');
          this.loadCountries();
        },
        error: (error) => this.handleSaveError(error),
      });
  }

  /**
   * Puts a refused save back on the form.
   *
   * Field errors from the API are merged into `formErrors` and their controls marked touched,
   * so a server-only rule - a malformed postal pattern, a duplicate ISO code - shows against
   * the field it belongs to rather than only in a toast the person has to interpret.
   */
  private handleSaveError(error: unknown): void {
    this.isSaving.set(false);

    const fieldErrors = apiFieldErrors(error);

    for (const [field, message] of Object.entries(fieldErrors)) {
      // The API names fields as they appear on the request; the form uses the same names.
      const control = field.charAt(0).toLowerCase() + field.slice(1);
      this.formErrors[control] = message;
      this.touched[control] = true;
    }

    this.showToast('error', 'Could not save', apiErrorMessage(error));
  }

  // ================= view / details =================
  viewCountry(c: CountryModel): void {
    this.selectedCountry.set(c);
    this.showViewPanel.set(true);
    this.showRowDetailsModal.set(false);

    // The panel shows the counts and notes, which only the detail endpoint carries.
    this.masterService
      .getCountry(c.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => this.selectedCountry.set({ ...detail }),
        error: () => {
          /* The row is already on screen; a failed enrichment is not worth a toast. */
        },
      });
  }

  closeViewPanel(): void {
    this.showViewPanel.set(false);
    this.selectedCountry.set(null);
  }

  openRowDetails(c: CountryModel): void {
    this.viewCountry(c);
  }

  closeRowDetails(): void {
    this.closeViewPanel();
  }

  // ================= activate / deactivate / delete =================
  confirmActivate(c: CountryModel): void {
    this.selectedCountry.set(c);
    this.showActivateModal.set(true);
  }

  activateConfirmed(): void {
    const selected = this.selectedCountry();
    this.showActivateModal.set(false);

    if (!selected) {
      return;
    }

    this.masterService
      .activateCountry(selected.id, { expectedVersion: selected.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.masterService.invalidateReferenceData();
          this.showToast(
            'success',
            'Activated',
            `Country '${selected.countryName}' activated successfully`,
          );
          this.selectedCountry.set(null);
          this.loadCountries();
        },
        error: (error) => this.showToast('error', 'Could not activate', apiErrorMessage(error)),
      });
  }

  /**
   * Opens the deactivate confirmation.
   *
   * The detail record is fetched first so `canDeactivate` reflects the SERVER's answer rather
   * than an assumption - the old version returned a hard-coded `true` with a comment saying a
   * real application would check.
   */
  confirmDeactivate(c: CountryModel): void {
    this.selectedCountry.set(c);
    this.canDeactivate.set(false);
    this.showDeactivateModal.set(true);

    this.masterService
      .getCountry(c.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.selectedCountry.set({ ...detail });
          this.canDeactivate.set(canPerform(detail, 'Deactivate'));
        },
        error: () => this.canDeactivate.set(false),
      });
  }

  deactivateConfirmed(): void {
    const selected = this.selectedCountry();
    this.showDeactivateModal.set(false);

    if (!selected || !this.canDeactivate()) {
      return;
    }

    this.masterService
      .deactivateCountry(selected.id, { expectedVersion: selected.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.masterService.invalidateReferenceData();
          this.showToast(
            'warning',
            'Deactivated',
            `Country '${selected.countryName}' deactivated successfully`,
          );
          this.selectedCountry.set(null);
          this.loadCountries();
        },
        error: (error) => this.showToast('error', 'Could not deactivate', apiErrorMessage(error)),
      });
  }

  confirmDelete(c: CountryModel): void {
    this.selectedCountry.set(c);
    this.canDelete.set(false);
    this.showDeleteModal.set(true);

    this.masterService
      .getCountry(c.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.selectedCountry.set({ ...detail });
          // Absent from permittedActions whenever a state or city still sits beneath the
          // country, so the confirm button is disabled rather than answering 409.
          this.canDelete.set(canPerform(detail, 'Delete'));
        },
        error: () => this.canDelete.set(false),
      });
  }

  deleteConfirmed(): void {
    const selected = this.selectedCountry();
    this.showDeleteModal.set(false);

    if (!selected || !this.canDelete()) {
      return;
    }

    this.masterService
      .deleteCountry(selected.id, { expectedVersion: selected.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.masterService.invalidateReferenceData();
          this.showToast(
            'error',
            'Deleted',
            `Country '${selected.countryName}' deleted successfully`,
          );
          this.selectedCountry.set(null);
          this.resetPaging();
          this.loadCountries();
        },
        error: (error) => this.showToast('error', 'Could not delete', apiErrorMessage(error)),
      });
  }

  closeModals(): void {
    this.showActivateModal.set(false);
    this.showDeactivateModal.set(false);
    this.showDeleteModal.set(false);
  }

  // ================= toasts =================
  showToast(type: ToastType, title: string, message: string): void {
    const id = ++this.toastIdCounter;
    this.toasts.update((list) => [...list, { id, type, title, message }]);
    setTimeout(() => this.dismissToast(id), 4000);
  }

  dismissToast(id: number): void {
    this.toasts.update((list) => list.filter((t) => t.id !== id));
  }
}

/**
 * Backward-compatible alias. `country.spec.ts` (generated by the current
 * Angular CLI schematics) imports `Country`, while existing routing files
 * may still reference the older `CountryComponent` name. Both resolve to
 * the same class so neither needs to change.
 */
export { Country as CountryComponent };

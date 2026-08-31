import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule, NgForm } from '@angular/forms';
import { apiErrorMessage, apiFieldErrors } from '../../../../Shared/models/api-response.model';
import {
  CurrencyDetail,
  CurrencyListItem,
  CurrencyType,
  RoundingMode,
  SymbolPosition,
  canPerform,
} from '../../../../Shared/models/global-master.model';
import { MasterService } from '../master.service';

/**
 * One currency, in the shape this screen's template binds to.
 *
 * IT KEEPS DISPLAY LABELS WHERE THE API KEEPS CODES - "Crypto" rather than "crypto", "Round Half
 * Up" rather than "halfUp". The template compares `currencyType === 'Crypto'` to pick a badge
 * colour and prints the value straight into a cell, so translating at the edge keeps every one of
 * those bindings working unchanged. The two mappings below are the only places the vocabularies
 * meet.
 *
 * THE THREE FIELDS AT THE END ARE NEW and are what make the screen honest. `version` is the
 * optimistic-concurrency stamp every write has to send back; `isPlatformRow` says whether this is
 * a shared seeded row that an Organisation may read but not change; `permittedActions` is the
 * server's own answer to what this caller may do next.
 */
export interface Currency {
  id: string;
  currencyCode: string;
  currencyName: string;
  numericCode: number | null;
  currencyType: string;
  symbol: string | null;
  symbolPosition: string;
  displayFormat: string | null;
  decimalPlaces: number | null;
  minorUnitName: string | null;
  roundingMode: string;
  roundingStep: number | null;
  isActive: boolean;
  notes: string | null;
  createdAt: Date;
  updatedAt?: Date | null;

  /** Sent back on the next write. A stale one answers 409 rather than overwriting somebody. */
  version: number;

  /** A shared platform row. Read-only to an Organisation; only SuperAdmin may change it. */
  isPlatformRow: boolean;

  /** What the SERVER says this caller may do. Buttons are drawn from it, never from a local rule. */
  permittedActions: string[];
}

export type ToastType = 'success' | 'error' | 'warning' | 'info';

export interface ToastMessage {
  id: number;
  type: ToastType;
  title: string;
  message: string;
}

/**
 * The display label for each API code, and back again.
 *
 * PAIRS RATHER THAN TWO LISTS, so a label and its code cannot drift apart. The previous version
 * had a bare `['Fiat', 'Crypto', 'Other']` and sent the label to a server that had never heard of
 * it - which, once the screen was talking to a real API, would have been a validation failure on
 * every save.
 */
const CURRENCY_TYPES: readonly { code: CurrencyType; label: string }[] = [
  { code: 'fiat', label: 'Fiat' },
  { code: 'crypto', label: 'Crypto' },
  { code: 'other', label: 'Other' },
];

const SYMBOL_POSITIONS: readonly { code: SymbolPosition; label: string }[] = [
  { code: 'prefix', label: 'Prefix' },
  { code: 'suffix', label: 'Suffix' },
];

/**
 * THE SERVER OFFERS THREE ROUNDING MODES, not five.
 *
 * The old list included "Round Up" and "Round Down", which the domain does not have - money
 * rounding on this platform is half-up, half-down or bankers'. Offering a fourth would have let
 * somebody choose a mode the API rejects, and there is no honest way to render a choice that
 * cannot be saved.
 */
const ROUNDING_MODES: readonly { code: RoundingMode; label: string }[] = [
  { code: 'halfUp', label: 'Round Half Up' },
  { code: 'halfDown', label: 'Round Half Down' },
  { code: 'bankers', label: 'Round Half Even' },
];

const ROUNDING_STEPS = ['0.01', '0.05', '0.10', '1.00'];

const DECIMAL_PLACES_OPTIONS = [
  { value: 0, label: '0 (e.g., JPY)' },
  { value: 1, label: '1' },
  { value: 2, label: '2 (Default)' },
  { value: 3, label: '3' },
  { value: 4, label: '4' },
];

function labelForType(code: CurrencyType | undefined): string {
  return CURRENCY_TYPES.find((entry) => entry.code === code)?.label ?? '';
}

function codeForType(label: string): CurrencyType {
  return CURRENCY_TYPES.find((entry) => entry.label === label)?.code ?? 'fiat';
}

function labelForPosition(code: SymbolPosition | undefined): string {
  return SYMBOL_POSITIONS.find((entry) => entry.code === code)?.label ?? '';
}

function codeForPosition(label: string): SymbolPosition {
  return SYMBOL_POSITIONS.find((entry) => entry.label === label)?.code ?? 'prefix';
}

function labelForRounding(code: RoundingMode | undefined): string {
  return ROUNDING_MODES.find((entry) => entry.code === code)?.label ?? '';
}

function codeForRounding(label: string): RoundingMode {
  return ROUNDING_MODES.find((entry) => entry.label === label)?.code ?? 'halfUp';
}

function blankCurrency(): Currency {
  return {
    id: '',
    currencyCode: '',
    currencyName: '',
    numericCode: null,
    currencyType: '',
    symbol: null,
    symbolPosition: '',
    displayFormat: null,
    decimalPlaces: null,
    minorUnitName: null,
    roundingMode: '',
    roundingStep: null,
    isActive: true,
    notes: null,
    createdAt: new Date(),
    updatedAt: null,
    version: 0,
    isPlatformRow: false,
    permittedActions: [],
  };
}

/**
 * The Currency master.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. Every row on this screen came from an eight-item
 * `seedData` array compiled into the bundle. Saving mutated that array, deleting spliced from it,
 * and a refresh restored the original eight - so nothing anybody did here survived, and every
 * Organisation saw the same eight currencies whatever their own catalogue said.
 *
 * It now reads and writes `IAM /api/v1/masters/currencies`, which is where the catalogue moved
 * when the GlobalMaster service was merged into IAM.
 *
 * THE SERVER DECIDES WHAT MAY BE DONE. `permittedActions` on each detail response already
 * accounts for the caller's permissions AND the record's state - a shared platform row is
 * read-only to an Organisation, and a currency that countries still name as their default cannot
 * be deleted. `canPerform` reads that answer rather than this file re-deriving a rule that would
 * eventually disagree with the API.
 *
 * FILTERING AND PAGING MOVED TO THE SERVER. They used to run over the whole in-memory array,
 * which works for eight rows and not for a real catalogue - and, more to the point, cannot apply
 * the Organisation filter at all.
 */
@Component({
  selector: 'app-currency',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './currency.html',
  styleUrl: './currency.css',
})
export class CurrencyComponent implements OnInit {
  private readonly masters = inject(MasterService);
  private readonly destroyRef = inject(DestroyRef);

  // ---------- View switch: 'list' | 'form' (replaces routing) ----------
  view: 'list' | 'form' = 'list';

  // ---------- Dropdown option sources ----------
  //
  // The template prints these straight into <option> elements, so they stay as the display
  // labels they always were. The codes travel separately - see the mapping helpers above.
  currencyTypes = CURRENCY_TYPES.map((entry) => entry.label);
  symbolPositions = SYMBOL_POSITIONS.map((entry) => entry.label);
  roundingModes = ROUNDING_MODES.map((entry) => entry.label);
  roundingSteps = ROUNDING_STEPS;
  decimalPlacesOptions = DECIMAL_PLACES_OPTIONS;

  // ---------- List state ----------
  currencies: Currency[] = [];
  filteredCurrencies: Currency[] = [];
  pagedCurrencies: Currency[] = [];
  searchText = '';
  selectedType = '';
  selectedStatus = '';
  currentPage = 1;
  pageSize = 10;
  totalPages = 1;

  /** The server's total, not the loaded page's length. The pager reads it. */
  private totalCountFromServer = 0;

  /** Counts for the summary tiles, fetched alongside the page rather than derived from it. */
  private activeCountFromServer = 0;
  private inactiveCountFromServer = 0;

  private readonly selectedCurrencyState = signal<Currency | null>(null);

  get selectedCurrency(): Currency | null {
    return this.selectedCurrencyState();
  }

  set selectedCurrency(value: Currency | null) {
    this.selectedCurrencyState.set(value);
  }

  /**
   * Whether the open record may be deactivated or deleted.
   *
   * BOTH COME FROM THE SERVER now. They used to be hard-coded `true`, so the buttons were always
   * enabled and the refusal - a currency still in use, a platform row an Organisation may not
   * touch - arrived as a 409 nobody could have anticipated.
   */
  canDeactivate = true;
  canDelete = true;

  isLoading = false;
  showActivateModal = false;
  showDeactivateModal = false;
  showDeleteModal = false;

  // ---------- Form state ----------
  formCurrency: Currency = blankCurrency();
  isEdit = false;
  submitted = false;
  duplicateCodeError = false;

  /** Server-side field errors, keyed by control name, for the form to show inline. */
  fieldErrors: Record<string, string> = {};

  // ---------- Toasts ----------
  toasts: ToastMessage[] = [];
  private nextToastId = 1;

  ngOnInit(): void {
    this.loadData();
  }

  // ================= LIST: derived counts =================
  //
  // Read from the server's answer rather than counted over the loaded page: a page of ten cannot
  // tell you how many active currencies the catalogue holds.

  get totalCount(): number {
    return this.totalCountFromServer;
  }

  get activeCount(): number {
    return this.activeCountFromServer;
  }

  get inactiveCount(): number {
    return this.inactiveCountFromServer;
  }

  get uniqueTypes(): string[] {
    return this.currencyTypes;
  }

  // ================= LIST: data load / filter / paginate =================

  /**
   * Fetches one page from the API.
   *
   * THE FILTERS GO TO THE SERVER. Searching in the browser can only search what has been
   * downloaded, which on a paged endpoint is the current page - so a search for a currency on
   * page four would have come back empty and looked like a missing record.
   */
  loadData(): void {
    this.isLoading = true;

    this.masters
      .searchCurrencies({
        page: this.currentPage,
        pageSize: this.pageSize,
        search: this.searchText.trim() || undefined,
        currencyType: this.selectedType ? codeForType(this.selectedType) : undefined,
        status: this.selectedStatus
          ? this.selectedStatus === 'active'
            ? 'active'
            : 'inactive'
          : undefined,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.currencies = page.items.map((item) => this.toViewModel(item));
          this.filteredCurrencies = this.currencies;
          this.pagedCurrencies = this.currencies;
          this.totalCountFromServer = page.totalCount;
          this.totalPages = Math.max(1, page.totalPages);
          this.currentPage = page.page;
          this.isLoading = false;
        },
        error: (error) => {
          this.isLoading = false;
          this.showToast('error', 'Could not load', apiErrorMessage(error, 'The currency catalogue could not be loaded.'));
        },
      });

    // The two status counts are a second, cheap call: the paged response reports one total, and
    // showing "12 active" derived from a page of ten would simply be wrong.
    this.masters
      .searchCurrencies({ pageSize: 1, status: 'active' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => (this.activeCountFromServer = page.totalCount) });

    this.masters
      .searchCurrencies({ pageSize: 1, status: 'inactive' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (page) => (this.inactiveCountFromServer = page.totalCount) });
  }

  /** Every filter change restarts at page one - staying on page four of a narrower result is a blank screen. */
  applyFilters(): void {
    this.currentPage = 1;
    this.loadData();
  }

  updatePagination(): void {
    this.loadData();
  }

  onSearch(): void {
    this.applyFilters();
  }

  onTypeChange(): void {
    this.applyFilters();
  }

  onStatusChange(): void {
    this.applyFilters();
  }

  onPageSizeChange(): void {
    this.currentPage = 1;
    this.loadData();
  }

  goToPage(page: number): void {
    if (page >= 1 && page <= this.totalPages) {
      this.currentPage = page;
      this.loadData();
    }
  }

  previousPage(): void {
    this.goToPage(this.currentPage - 1);
  }

  nextPage(): void {
    this.goToPage(this.currentPage + 1);
  }

  get pageNumbers(): number[] {
    const pages: number[] = [];
    const windowSize = 2;
    let start = Math.max(1, this.currentPage - windowSize);
    const end = Math.min(this.totalPages, start + windowSize * 2);
    start = Math.max(1, end - windowSize * 2);

    for (let index = start; index <= end; index++) {
      pages.push(index);
    }

    return pages;
  }

  onRefresh(): void {
    this.searchText = '';
    this.selectedType = '';
    this.selectedStatus = '';
    this.currentPage = 1;

    // The reference-data cache is invalidated too: a currency added or retired here appears in
    // the country form's dropdown, and a cached list would keep offering the old one.
    this.masters.invalidateReferenceData();
    this.loadData();
    this.showToast('info', 'Refresh', 'Data refreshed');
  }

  trackById(_: number, item: Currency): string {
    return item.id;
  }

  // ================= LIST: view / activate / deactivate / delete modals =================

  /**
   * Opens the detail panel.
   *
   * IT FETCHES THE DETAIL rather than showing the row it already has. The row carries no
   * `permittedActions`, no notes and no usage count - and the usage count is what decides whether
   * Delete may be offered at all.
   */
  openView(currency: Currency): void {
    this.selectedCurrency = currency;

    this.masters
      .getCurrency(currency.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          this.selectedCurrency = this.toViewModelFromDetail(detail);
          this.canDeactivate = canPerform(detail, 'Deactivate');

          // A currency any country still names as its default cannot be removed. The server
          // refuses it; this stops the button being offered in the first place.
          this.canDelete =
            canPerform(detail, 'Delete') && detail.countryUsageCount === 0;
        },
        error: (error) =>
          this.showToast('error', 'Could not open', apiErrorMessage(error, 'That currency could not be opened.')),
      });
  }

  closeView(): void {
    this.selectedCurrency = null;
  }

  openActivate(currency: Currency): void {
    this.openView(currency);
    this.showActivateModal = true;
  }

  closeActivate(): void {
    this.showActivateModal = false;
    this.selectedCurrency = null;
  }

  confirmActivate(): void {
    const current = this.selectedCurrency;
    if (!current) return;

    this.masters
      .activateCurrency(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('success', 'Activated', `Currency '${current.currencyName}' activated successfully`);
          this.closeActivate();
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => this.reportWriteFailure(error, 'The currency could not be activated.'),
      });
  }

  openDeactivate(currency: Currency): void {
    this.openView(currency);
    this.showDeactivateModal = true;
  }

  closeDeactivate(): void {
    this.showDeactivateModal = false;
    this.selectedCurrency = null;
  }

  confirmDeactivate(): void {
    const current = this.selectedCurrency;
    if (!current || !this.canDeactivate) return;

    this.masters
      .deactivateCurrency(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('warning', 'Deactivated', `Currency '${current.currencyName}' deactivated successfully`);
          this.closeDeactivate();
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => this.reportWriteFailure(error, 'The currency could not be deactivated.'),
      });
  }

  openDelete(currency: Currency): void {
    this.openView(currency);
    this.showDeleteModal = true;
  }

  closeDelete(): void {
    this.showDeleteModal = false;
    this.selectedCurrency = null;
  }

  confirmDelete(): void {
    const current = this.selectedCurrency;
    if (!current || !this.canDelete) return;

    this.masters
      .deleteCurrency(current.id, { expectedVersion: current.version })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.showToast('error', 'Deleted', `Currency '${current.currencyName}' deleted successfully`);
          this.closeDelete();
          this.currentPage = 1;
          this.masters.invalidateReferenceData();
          this.loadData();
        },
        error: (error) => this.reportWriteFailure(error, 'The currency could not be deleted.'),
      });
  }

  // ================= FORM: create / edit =================

  get pageTitle(): string {
    return this.isEdit ? 'Edit Currency' : 'Create Currency';
  }

  get pageSubTitle(): string {
    return this.isEdit ? 'Update currency details' : 'Create new currency';
  }

  get roundingStepString(): string {
    return this.formCurrency.roundingStep != null ? this.formCurrency.roundingStep.toFixed(2) : '';
  }

  set roundingStepString(value: string) {
    this.formCurrency.roundingStep = value === '' ? null : parseFloat(value);
  }

  openCreate(): void {
    this.formCurrency = blankCurrency();
    this.isEdit = false;
    this.submitted = false;
    this.duplicateCodeError = false;
    this.fieldErrors = {};
    this.view = 'form';
  }

  /**
   * Opens the edit form.
   *
   * IT LOADS THE DETAIL FIRST. The grid row has no notes, no display format and no minor-unit
   * name, so editing from the row would silently blank three fields the moment somebody saved.
   */
  openEdit(currency: Currency): void {
    this.isEdit = true;
    this.submitted = false;
    this.duplicateCodeError = false;
    this.fieldErrors = {};
    this.view = 'form';
    this.closeView();

    this.masters
      .getCurrency(currency.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => (this.formCurrency = this.toViewModelFromDetail(detail)),
        error: (error) => {
          this.view = 'list';
          this.showToast('error', 'Could not open', apiErrorMessage(error, 'That currency could not be opened for editing.'));
        },
      });
  }

  backToList(): void {
    this.view = 'list';
  }

  onCurrencyNameInput(): void {
    this.formCurrency.currencyName = (this.formCurrency.currencyName ?? '').replace(/[0-9]/g, '');
  }

  onCurrencyCodeInput(): void {
    this.formCurrency.currencyCode = (this.formCurrency.currencyCode ?? '').toUpperCase();
    this.duplicateCodeError = false;
  }

  /**
   * Saves the form.
   *
   * THE DUPLICATE CHECK IS THE SERVER'S. A local scan of the loaded page could not see a currency
   * created by a colleague a moment ago, nor one on a page this screen has not fetched. The API
   * answers DUPLICATE_CODE, which is surfaced on the field rather than as a generic failure.
   *
   * THE CODE IS NOT SENT ON AN UPDATE, and the API has no field for it. The code IS the currency -
   * every donation, receipt and refund that ever named it points at that three-letter string -
   * so repointing it would redenominate history rather than correct a typo.
   */
  save(form: NgForm): void {
    this.submitted = true;
    this.duplicateCodeError = false;
    this.fieldErrors = {};

    if (form.invalid) {
      return;
    }

    const code = (this.formCurrency.currencyCode ?? '').toUpperCase().trim();
    const name = (this.formCurrency.currencyName ?? '').trim();

    this.formCurrency.currencyCode = code;
    this.formCurrency.currencyName = name;

    if (this.isEdit) {
      this.masters
        .updateCurrency(this.formCurrency.id, {
          expectedVersion: this.formCurrency.version,
          currencyName: name,
          numericCode: this.formCurrency.numericCode,
          currencyType: codeForType(this.formCurrency.currencyType),
          symbol: this.formCurrency.symbol,
          symbolPosition: codeForPosition(this.formCurrency.symbolPosition),
          displayFormat: this.formCurrency.displayFormat,
          decimalPlaces: this.formCurrency.decimalPlaces,
          minorUnitName: this.formCurrency.minorUnitName,
          roundingMode: codeForRounding(this.formCurrency.roundingMode),
          roundingStep: this.formCurrency.roundingStep,
          notes: this.formCurrency.notes,

          // Explicit, because a null roundingStep is indistinguishable from "leave it alone" in a
          // partial update - and a cash-rounding step somebody cleared has to actually clear.
          clearRoundingStep: this.formCurrency.roundingStep === null,
        })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: () => {
            this.showToast('success', 'Updated', `Currency '${code}' updated successfully`);
            this.masters.invalidateReferenceData();
            this.view = 'list';
            this.loadData();
          },
          error: (error) => this.reportSaveFailure(error, 'The currency could not be updated.'),
        });

      return;
    }

    this.masters
      .createCurrency({
        currencyCode: code,
        currencyName: name,
        numericCode: this.formCurrency.numericCode,
        currencyType: codeForType(this.formCurrency.currencyType),
        symbol: this.formCurrency.symbol,
        symbolPosition: codeForPosition(this.formCurrency.symbolPosition),
        displayFormat: this.formCurrency.displayFormat,
        decimalPlaces: this.formCurrency.decimalPlaces ?? 2,
        minorUnitName: this.formCurrency.minorUnitName,
        roundingMode: codeForRounding(this.formCurrency.roundingMode),
        roundingStep: this.formCurrency.roundingStep,
        status: this.formCurrency.isActive ? 'active' : 'draft',
        notes: this.formCurrency.notes,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (created) => {
          this.showToast('success', 'Added', `Currency '${created.currencyCode}' added successfully`);
          this.masters.invalidateReferenceData();
          this.view = 'list';
          this.currentPage = 1;
          this.loadData();
        },
        error: (error) => this.reportSaveFailure(error, 'The currency could not be created.'),
      });
  }

  // ================= Mapping =================

  private toViewModel(item: CurrencyListItem): Currency {
    return {
      id: item.id,
      currencyCode: item.currencyCode,
      currencyName: item.currencyName,
      numericCode: item.numericCode,
      currencyType: labelForType(item.currencyType),
      symbol: item.symbol,
      symbolPosition: labelForPosition(item.symbolPosition),
      displayFormat: null,
      decimalPlaces: item.decimalPlaces,
      minorUnitName: null,
      roundingMode: '',
      roundingStep: null,
      isActive: item.isActive,
      notes: null,

      // The grid has no created date; the updated one stands in so the default sort still reads
      // newest-first. The detail call fills both in properly when a row is opened.
      createdAt: item.updatedAtUtc ? new Date(item.updatedAtUtc) : new Date(),
      updatedAt: item.updatedAtUtc ? new Date(item.updatedAtUtc) : null,

      version: item.version,
      isPlatformRow: item.isPlatformRow,
      permittedActions: [],
    };
  }

  private toViewModelFromDetail(detail: CurrencyDetail): Currency {
    return {
      id: detail.id,
      currencyCode: detail.currencyCode,
      currencyName: detail.currencyName,
      numericCode: detail.numericCode,
      currencyType: labelForType(detail.currencyType),
      symbol: detail.symbol,
      symbolPosition: labelForPosition(detail.symbolPosition),
      displayFormat: detail.displayFormat,
      decimalPlaces: detail.decimalPlaces,
      minorUnitName: detail.minorUnitName,
      roundingMode: labelForRounding(detail.roundingMode),
      roundingStep: detail.roundingStep,
      isActive: detail.isActive,
      notes: detail.notes,
      createdAt: new Date(detail.createdAtUtc),
      updatedAt: detail.updatedAtUtc ? new Date(detail.updatedAtUtc) : null,
      version: detail.version,
      isPlatformRow: detail.isPlatformRow,
      permittedActions: detail.permittedActions,
    };
  }

  // ================= Failure reporting =================

  /**
   * A failed create or update.
   *
   * A DUPLICATE CODE GOES ON THE FIELD, not into a toast that disappears. It is the one failure
   * the person can fix without leaving the form, and pointing at the control is what tells them
   * where to look.
   */
  private reportSaveFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'DUPLICATE_CODE') {
      this.duplicateCodeError = true;
      this.showToast('error', 'Validation Error', apiErrorMessage(error, 'Currency code already exists'));
      return;
    }

    this.fieldErrors = apiFieldErrors(error);
    this.showToast('error', 'Save failed', apiErrorMessage(error, fallback));
  }

  /**
   * A failed activate, deactivate or delete.
   *
   * A 409 IS NAMED SEPARATELY because it means something the operator can act on - somebody else
   * changed this row - rather than something being broken, and the fix is to refresh rather than
   * to try again.
   */
  private reportWriteFailure(error: unknown, fallback: string): void {
    const code =
      typeof error === 'object' && error !== null && 'errorCode' in error
        ? (error as { errorCode?: string }).errorCode
        : undefined;

    if (code === 'CONCURRENCY_CONFLICT') {
      this.showToast('warning', 'Record changed', 'Somebody else changed this currency. Refreshing.');
      this.loadData();
      return;
    }

    this.showToast('error', 'Action failed', apiErrorMessage(error, fallback));
  }

  // ================= Toasts =================

  private showToast(type: ToastType, title: string, message: string): void {
    const toast: ToastMessage = { id: this.nextToastId++, type, title, message };
    this.toasts = [...this.toasts, toast];
    setTimeout(() => this.dismissToast(toast.id), 3500);
  }

  dismissToast(id: number): void {
    this.toasts = this.toasts.filter((toast) => toast.id !== id);
  }

  toastIconPath(type: ToastType): string {
    switch (type) {
      case 'success':
        return 'M20 6 9 17l-5-5';
      case 'error':
        return 'M18 6 6 18 M6 6l12 12';
      case 'warning':
        return 'M12 9v4 M12 17h.01';
      default:
        return 'M12 16v-4 M12 8h.01';
    }
  }
}

import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

/**
 * SCR-UX-004 — Standard list page.
 *
 * Faithful implementation of section 4.4 of the YDot Practical UI/UX Generation
 * Specification. Every region (4.4.1), field (4.4.2), action (4.4.3), UI state
 * (4.4.4) and validation/confirmation pattern (4.4.6) below maps directly to the
 * controlled contract. No content or behaviour outside that contract is added.
 *
 *  Route            : /workspace/standard-list-page
 *  Purpose          : Manage filtered record sets with stable columns and saved views.
 *  Primary users    : Module users
 *  View permission  : ux.standard-list-page.view
 *  Primary action   : Sort
 *  History rule     : Delete is available only for an unused draft with no downstream
 *                     reference; otherwise use the domain lifecycle action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.4.4, plus the settled "ready" surface. */
type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** Effective permission set for the acting Module user (4.4.3). */
interface EffectivePermissions {
  readonly view: boolean; // ux.standard-list-page.view
  readonly sort: boolean; // ux.standard-list-page.sort
  readonly bulkAction: boolean; // ux.standard-list-page.bulk-action
  readonly export: boolean; // ux.standard-list-page.export
}

/** A scope-aware selector option with a stable reference and disambiguating context (4.4.2 Owner). */
interface OwnerOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

/** A single row in the managed record set. All read-only, server-derived values (4.4.2). */
interface ListRecord {
  readonly reference: string; // Record reference (read-only)
  readonly type: string; // Donation type
  readonly name: string; // Primary name (read-only)
  readonly amount: number; // ₹
  readonly updated: string; // Updated time (read-only, ISO)
  readonly updatedLabel: string; // Updated time display
  readonly status: RecordStatus; // Status (catalogue value)
  readonly campaign: string; // Campaign
  readonly paymentMode: string; // Payment mode
  readonly ownerReference: string; // Owner reference for scope filtering
}

type RecordStatus = 'Completed' | 'In Progress' | 'Received' | 'Failed';

/** A sortable, stable column (4.4.1 Main work — stable columns; 4.4.3 Sort). */
interface SortableColumn {
  readonly key: keyof ListRecord;
  readonly label: string;
}

type SortDirection = 'asc' | 'desc';

@Component({
  selector: 'app-standard-list-page',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './standard-list-page.html',
  styleUrl: './standard-list-page.css',
})
export class StandardListPageComponent {
  // ----- Task header (4.4.1) -----
  protected readonly pageTitle = 'Donations';
  protected readonly pageSubtitle = 'View, filter and manage all donation records in your permitted scope.';
  protected readonly stableReference = 'LIST-DONATION-2026-0001';
  protected readonly lifecycleState = 'Active';
  protected readonly owner = 'Sophie Bennett · Programme Manager';
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';

  /** Last refresh time — server-derived, read-only freshness evidence (4.4.1). */
  protected readonly lastRefresh = signal('Today, 09:30 AM · IST');

  /** Effective permissions decided server-side; the client mirrors the same decision (4.4.3, 4.4.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    sort: true,
    bulkAction: true,
    export: true,
  };

  /** Lifecycle states in which workflow actions (Sort/Bulk/Export) are permitted (4.4.3). */
  private readonly workflowPermittedStates = ['Active'];

  // ================= Context and filters (4.4.1 + 4.4.2) =================

  /** Saved view — search / filter control (4.4.2). */
  protected readonly savedViews = ['All Donations (Default)', 'Board pack — monthly', 'Exceptions focus'];
  protected readonly savedView = signal(this.savedViews[0]);

  /** Records-per-page control (list toolbar). */
  protected readonly pageSizes = [10, 25, 50, 100];
  protected readonly pageSize = signal(10);

  /** Free-text search across the record scope (4.4.2 Search). */
  protected readonly searchTerm = signal('');

  /** Status — search-select using only current catalogue values (4.4.2 Status). */
  protected readonly statusCatalogue: readonly RecordStatus[] = ['Completed', 'In Progress', 'Received', 'Failed'];
  protected readonly statusFilter = signal<RecordStatus | ''>('');

  /** Owner — scope-aware searchable selector with identity preview (4.4.2 Owner). */
  protected readonly ownerOptions: readonly OwnerOption[] = [
    { reference: 'USR-0001', name: 'All owners in scope', context: 'YDot Foundation · National' },
    { reference: 'USR-0114', name: 'Sophie Bennett', context: 'Programme Manager · Maharashtra' },
    { reference: 'USR-0142', name: 'Arun Verma', context: 'Fundraising Lead · Tamil Nadu' },
    { reference: 'USR-0177', name: 'Divya Rao', context: 'Regional Coordinator · Karnataka' },
  ];
  protected readonly ownerQuery = signal('');
  protected readonly activeOwner = signal<OwnerOption>(this.ownerOptions[0]);
  protected readonly ownerPickerOpen = signal(false);

  /** Eligible owner options filtered by the search query (server-side in production; 4.4.2). */
  protected readonly ownerResults = computed(() => {
    const q = this.ownerQuery().trim().toLowerCase();
    if (!q) {
      return this.ownerOptions;
    }
    return this.ownerOptions.filter(
      (o) =>
        o.name.toLowerCase().includes(q) ||
        o.reference.toLowerCase().includes(q) ||
        o.context.toLowerCase().includes(q),
    );
  });

  /** Date range — operating time zone; impossible ranges rejected before submit (4.4.2 Date range). */
  protected readonly rangeStart = signal('2026-07-12');
  protected readonly rangeEnd = signal('2026-07-22');

  /** Campaign — catalogue filter (4.4.1 Context and filters; not a contract field, drawn from records). */
  protected readonly campaignCatalogue = [
    'All campaigns',
    'Blind Stick Distribution Drive Jul 2026',
    'Education Support Program',
    'Community Development Fund',
    'Healthcare Aid Initiative',
    'Infrastructure Improvement',
  ];
  protected readonly campaignFilter = signal(this.campaignCatalogue[0]);

  /** Payment mode — catalogue filter drawn from records (4.4.1 Context and filters). */
  protected readonly paymentModeCatalogue = ['All modes', 'NEFT', 'UPI'];
  protected readonly paymentModeFilter = signal(this.paymentModeCatalogue[0]);

  /** Donation type — catalogue filter drawn from records (4.4.1 Context and filters). */
  protected readonly typeCatalogue = ['Individual Donation', 'Corporate Donation', 'Organisation Donation'];
  protected readonly typeFilter = signal<Record<string, boolean>>({
    'Individual Donation': true,
    'Corporate Donation': true,
    'Organisation Donation': false,
  });

  /** Amount range (₹) — numeric bounds (4.4.1 Context and filters). */
  protected readonly amountMin = signal<number | null>(null);
  protected readonly amountMax = signal<number | null>(null);

  /** Whether the filters panel (secondary region) is shown (4.4.1 — clearing/toggling is explicit).
   *  Hidden by default; revealed only when the user chooses "Show Filters". */
  protected readonly filtersPanelOpen = signal(false);

  /** Human-readable, interpreted date shown before submit (4.4.2 Date range). */
  protected readonly interpretedRange = computed(
    () => `${this.formatDate(this.rangeStart())} – ${this.formatDate(this.rangeEnd())} · ${this.operatingTimeZone}`,
  );

  /** True when the range is impossible (end before start); blocks submit (4.4.2, 4.4.6 invalid value). */
  protected readonly rangeInvalid = computed(() => new Date(this.rangeEnd()) < new Date(this.rangeStart()));

  /** True when Min amount exceeds Max amount; blocks submit (4.4.6 invalid value). */
  protected readonly amountRangeInvalid = computed(() => {
    const lo = this.amountMin();
    const hi = this.amountMax();
    return lo !== null && hi !== null && lo > hi;
  });

  /** Active-filter summary chips, qualified by scope (4.4.1 Context and filters). */
  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [
      { key: 'date', label: `Date: ${this.formatDate(this.rangeStart())} - ${this.formatDate(this.rangeEnd())}` },
      { key: 'status', label: `Status: ${this.statusFilter() || 'All'}` },
      {
        key: 'owner',
        label: `Organisation: ${this.activeOwner().reference === 'USR-0001' ? 'All' : this.activeOwner().name}`,
      },
    ];
    return chips;
  });

  // ================= Main work: filtered record set with stable columns (4.4.1 + 4.4.2) =================

  /** The stable columns of the list; each is sortable via the Sort action (4.4.1 Main work / 4.4.3 Sort). */
  protected readonly columns: readonly SortableColumn[] = [
    { key: 'reference', label: 'Donation ID' },
    { key: 'type', label: 'Donation Type' },
    { key: 'name', label: 'Donor / Organisation' },
    { key: 'amount', label: 'Amount (₹)' },
    { key: 'updated', label: 'Date' },
    { key: 'status', label: 'Status' },
    { key: 'campaign', label: 'Campaign' },
    { key: 'paymentMode', label: 'Payment Mode' },
  ];

  /** Columns offered in the Sort primary-action menu (4.4.3 Sort). */
  protected readonly sortableColumns = this.columns;

  /** The full record set inside the actor's effective data scope (4.4 Data scope). Read-only, server-derived. */
  protected readonly records: readonly ListRecord[] = [
    { reference: 'DONATION-2026-0921', type: 'Corporate Donation', name: 'GreenSol India Pvt Ltd', amount: 1236500, updated: '2026-07-22T09:15', updatedLabel: '22 Jul 2026 · 09:15 AM', status: 'Completed', campaign: 'Blind Stick Distribution Drive Jul 2026', paymentMode: 'NEFT', ownerReference: 'USR-0114' },
    { reference: 'DONATION-2026-0918', type: 'Individual Donation', name: 'Rahul Sharma', amount: 5600, updated: '2026-07-22T08:47', updatedLabel: '22 Jul 2026 · 08:47 AM', status: 'In Progress', campaign: 'Education Support Program', paymentMode: 'UPI', ownerReference: 'USR-0142' },
    { reference: 'DONATION-2026-0917', type: 'Corporate Donation', name: 'BuildTech Solutions', amount: 345000, updated: '2026-07-21T18:30', updatedLabel: '21 Jul 2026 · 06:30 PM', status: 'Received', campaign: 'Education Support Program', paymentMode: 'NEFT', ownerReference: 'USR-0114' },
    { reference: 'DONATION-2026-0916', type: 'Individual Donation', name: 'Meera Nair', amount: 2200, updated: '2026-07-21T16:12', updatedLabel: '21 Jul 2026 · 04:12 PM', status: 'Completed', campaign: 'Community Development Fund', paymentMode: 'UPI', ownerReference: 'USR-0177' },
    { reference: 'DONATION-2026-0915', type: 'Corporate Donation', name: 'BlueRiver Foundation', amount: 875000, updated: '2026-07-21T14:05', updatedLabel: '21 Jul 2026 · 02:05 PM', status: 'In Progress', campaign: 'Healthcare Aid Initiative', paymentMode: 'NEFT', ownerReference: 'USR-0142' },
    { reference: 'DONATION-2026-0914', type: 'Individual Donation', name: 'Arjun Patel', amount: 1000, updated: '2026-07-20T11:20', updatedLabel: '20 Jul 2026 · 11:20 AM', status: 'Completed', campaign: 'Blind Stick Distribution Drive Jul 2026', paymentMode: 'UPI', ownerReference: 'USR-0114' },
    { reference: 'DONATION-2026-0913', type: 'Corporate Donation', name: 'Nexus Technologies', amount: 1500000, updated: '2026-07-20T10:05', updatedLabel: '20 Jul 2026 · 10:05 AM', status: 'Completed', campaign: 'Education Support Program', paymentMode: 'NEFT', ownerReference: 'USR-0177' },
    { reference: 'DONATION-2026-0912', type: 'Individual Donation', name: 'Sanjay Krishnan', amount: 750, updated: '2026-07-19T19:45', updatedLabel: '19 Jul 2026 · 07:45 PM', status: 'Failed', campaign: 'Community Development Fund', paymentMode: 'UPI', ownerReference: 'USR-0142' },
    { reference: 'DONATION-2026-0911', type: 'Corporate Donation', name: 'Pyramid Constructions', amount: 650000, updated: '2026-07-19T17:30', updatedLabel: '19 Jul 2026 · 05:30 PM', status: 'In Progress', campaign: 'Infrastructure Improvement', paymentMode: 'NEFT', ownerReference: 'USR-0114' },
    { reference: 'DONATION-2026-0910', type: 'Individual Donation', name: 'Priya Menon', amount: 500, updated: '2026-07-19T15:22', updatedLabel: '19 Jul 2026 · 03:22 PM', status: 'Completed', campaign: 'Blind Stick Distribution Drive Jul 2026', paymentMode: 'UPI', ownerReference: 'USR-0177' },
  ];

  /** Committed sort — column + direction. Primary action Sort (4.4.3). */
  protected readonly sortColumn = signal<keyof ListRecord>('updated');
  protected readonly sortDirection = signal<SortDirection>('desc');

  /** The filtered, sorted record set for the current scope and filters (server-side in production; 4.4.1). */
  protected readonly visibleRecords = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
    const status = this.statusFilter();
    const owner = this.activeOwner();
    const campaign = this.campaignFilter();
    const mode = this.paymentModeFilter();
    const types = this.typeFilter();
    const lo = this.amountMin();
    const hi = this.amountMax();
    const start = new Date(this.rangeStart());
    const end = new Date(this.rangeEnd());
    end.setHours(23, 59, 59, 999);

    const rows = this.records.filter((r) => {
      if (q && !(r.reference.toLowerCase().includes(q) || r.name.toLowerCase().includes(q) || r.campaign.toLowerCase().includes(q))) {
        return false;
      }
      if (status && r.status !== status) {
        return false;
      }
      if (owner.reference !== 'USR-0001' && r.ownerReference !== owner.reference) {
        return false;
      }
      if (campaign !== 'All campaigns' && r.campaign !== campaign) {
        return false;
      }
      if (mode !== 'All modes' && r.paymentMode !== mode) {
        return false;
      }
      if (!types[r.type]) {
        return false;
      }
      if (lo !== null && r.amount < lo) {
        return false;
      }
      if (hi !== null && r.amount > hi) {
        return false;
      }
      const d = new Date(r.updated);
      if (d < start || d > end) {
        return false;
      }
      return true;
    });

    const col = this.sortColumn();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = a[col];
      const bv = b[col];
      if (typeof av === 'number' && typeof bv === 'number') {
        return (av - bv) * dir;
      }
      return String(av).localeCompare(String(bv)) * dir;
    });
  });

  /** Totals qualified by scope and last refresh (4.4.1 Context and filters). */
  protected readonly recordCount = computed(() => this.visibleRecords().length);
  protected readonly totalRecords = this.records.length;

  // ----- Row selection → Selected-row count (read-only, 4.4.2) -----
  protected readonly selectedRefs = signal<ReadonlySet<string>>(new Set());
  protected readonly selectedCount = computed(() => this.selectedRefs().size);
  protected readonly allVisibleSelected = computed(() => {
    const visible = this.visibleRecords();
    const sel = this.selectedRefs();
    return visible.length > 0 && visible.every((r) => sel.has(r.reference));
  });

  // ================= Actions, eligibility and result (4.4.3) =================

  /** Whether workflow actions are allowed (permission + permitted lifecycle state; 4.4.3). */
  private readonly inWorkflowState = () => this.workflowPermittedStates.includes(this.lifecycleState);

  /** Primary action — Sort. Offered only when role, permission, scope, state and dependencies allow (4.4.1). */
  protected readonly sortAllowed = computed(
    () => this.permissions.sort && this.inWorkflowState() && this.uiState() !== 'no-access',
  );
  /** Filter — any authorised state (4.4.3). */
  protected readonly filterAllowed = computed(
    () => this.permissions.view && !this.rangeInvalid() && !this.amountRangeInvalid() && this.uiState() !== 'no-access',
  );
  /** Bulk action — permitted state and at least one selected row (conditional visibility, 4.4.2 + 4.4.3). */
  protected readonly bulkAllowed = computed(
    () => this.permissions.bulkAction && this.inWorkflowState() && this.selectedCount() > 0,
  );
  /** Export — permitted lifecycle state (4.4.3). */
  protected readonly exportAllowed = computed(() => this.permissions.export && this.inWorkflowState());

  // ----- Sort primary action: draft + high-risk confirmation (4.4.3 + 4.4.6 high-risk) -----
  protected readonly sortMenuOpen = signal(false);
  protected readonly sortDialogOpen = signal(false);
  protected readonly pendingSort = signal<{ column: keyof ListRecord; direction: SortDirection } | null>(null);

  protected toggleSortMenu(): void {
    if (!this.sortAllowed()) {
      return;
    }
    this.sortMenuOpen.update((v) => !v);
  }

  /** Choose a column/direction and open the required Confirm Sort review (4.4.6 high-risk). */
  protected requestSort(column: keyof ListRecord, direction?: SortDirection): void {
    if (!this.sortAllowed()) {
      return;
    }
    const dir: SortDirection =
      direction ?? (this.sortColumn() === column && this.sortDirection() === 'asc' ? 'desc' : 'asc');
    this.pendingSort.set({ column, direction: dir });
    this.sortMenuOpen.set(false);
    this.sortDialogOpen.set(true);
  }

  protected cancelSort(): void {
    this.sortDialogOpen.set(false);
    this.pendingSort.set(null);
  }

  /** Explicit confirmation before the committed reorder (4.4.3 Sort → 4.4.6 high-risk). */
  protected confirmSort(): void {
    const pending = this.pendingSort();
    if (!pending) {
      return;
    }
    this.sortColumn.set(pending.column);
    this.sortDirection.set(pending.direction);
    this.sortDialogOpen.set(false);
    this.pendingSort.set(null);
    this.uiState.set('success');
  }

  protected sortLabel(key: keyof ListRecord): string {
    return this.columns.find((c) => c.key === key)?.label ?? String(key);
  }

  protected pendingSortLabel(): string {
    const p = this.pendingSort();
    if (!p) {
      return '';
    }
    return `${this.sortLabel(p.column)} · ${p.direction === 'asc' ? 'Ascending' : 'Descending'}`;
  }

  // ----- Filter action (4.4.3) -----
  protected applyFilter(): void {
    if (!this.filterAllowed()) {
      // Impossible ranges surface the validation state (4.4.4 / 4.4.6 invalid value).
      this.uiState.set('validation');
      return;
    }
    // Server-side filtering; the confirmed result is the refreshed record set (4.4.3).
    this.uiState.set(this.visibleRecords().length === 0 ? 'empty' : 'ready');
  }

  /** Clearing filters is explicit and returns focus predictably (4.4.1 Context and filters). */
  protected clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set('');
    this.activeOwner.set(this.ownerOptions[0]);
    this.campaignFilter.set(this.campaignCatalogue[0]);
    this.paymentModeFilter.set(this.paymentModeCatalogue[0]);
    this.typeFilter.set({ 'Individual Donation': true, 'Corporate Donation': true, 'Organisation Donation': false });
    this.amountMin.set(null);
    this.amountMax.set(null);
    this.rangeStart.set('2026-07-12');
    this.rangeEnd.set('2026-07-22');
    this.savedView.set(this.savedViews[0]);
    this.uiState.set('ready');
  }

  protected removeFilterChip(key: string): void {
    if (key === 'status') {
      this.statusFilter.set('');
    } else if (key === 'owner') {
      this.activeOwner.set(this.ownerOptions[0]);
    } else if (key === 'date') {
      this.rangeStart.set('2026-07-12');
      this.rangeEnd.set('2026-07-22');
    }
  }

  protected toggleFiltersPanel(): void {
    this.filtersPanelOpen.update((v) => !v);
  }

  // ----- Owner selector (4.4.2) -----
  protected selectOwner(option: OwnerOption): void {
    this.activeOwner.set(option);
    this.ownerPickerOpen.set(false);
    this.ownerQuery.set('');
  }
  protected toggleOwnerPicker(): void {
    this.ownerPickerOpen.update((v) => !v);
  }

  protected toggleType(type: string): void {
    this.typeFilter.update((cur) => ({ ...cur, [type]: !cur[type] }));
  }

  // ----- Row selection -----
  protected toggleRow(reference: string): void {
    this.selectedRefs.update((cur) => {
      const next = new Set(cur);
      if (next.has(reference)) {
        next.delete(reference);
      } else {
        next.add(reference);
      }
      return next;
    });
  }
  protected toggleAllVisible(): void {
    const visible = this.visibleRecords();
    this.selectedRefs.update((cur) => {
      const allSelected = visible.length > 0 && visible.every((r) => cur.has(r.reference));
      const next = new Set(cur);
      if (allSelected) {
        visible.forEach((r) => next.delete(r.reference));
      } else {
        visible.forEach((r) => next.add(r.reference));
      }
      return next;
    });
  }
  protected isSelected(reference: string): boolean {
    return this.selectedRefs().has(reference);
  }

  // ----- Save view action (4.4.3) -----
  protected readonly saveViewDialogOpen = signal(false);
  protected readonly saveViewName = signal('');
  protected readonly saveViewReference = signal('');

  protected openSaveView(): void {
    this.saveViewName.set('');
    this.saveViewReference.set('');
    this.saveViewDialogOpen.set(true);
  }
  protected cancelSaveView(): void {
    this.saveViewDialogOpen.set(false);
  }
  /** Save one draft, preserve values, show a stable reference (4.4.3 Save view). */
  protected confirmSaveView(): void {
    const name = this.saveViewName().trim();
    if (!name) {
      // Required field (4.4.6): keep the dialog open and surface the message.
      return;
    }
    // A similar view already exists → duplicate handling (4.4.4 duplicate).
    if (this.savedViews.some((v) => v.toLowerCase() === name.toLowerCase())) {
      this.uiState.set('duplicate');
      this.saveViewDialogOpen.set(false);
      return;
    }
    this.saveViewReference.set(`VIEW-2026-${String(1000 + this.savedViews.length)}`);
    this.uiState.set('success');
    this.saveViewDialogOpen.set(false);
  }

  // ----- Bulk action (4.4.3) -----
  protected readonly bulkOperations = ['Assign owner', 'Change campaign', 'Mark for review'];
  protected readonly bulkMenuOpen = signal(false);
  protected readonly bulkDialogOpen = signal(false);
  protected readonly pendingBulkOperation = signal('');

  protected toggleBulkMenu(): void {
    if (!this.bulkAllowed()) {
      return;
    }
    this.bulkMenuOpen.update((v) => !v);
  }
  protected requestBulk(operation: string): void {
    if (!this.bulkAllowed()) {
      return;
    }
    this.pendingBulkOperation.set(operation);
    this.bulkMenuOpen.set(false);
    this.bulkDialogOpen.set(true);
  }
  protected cancelBulk(): void {
    this.bulkDialogOpen.set(false);
    this.pendingBulkOperation.set('');
  }
  /** Refresh only authorised records in scope; show the confirmed result (4.4.3 Bulk action). */
  protected confirmBulk(): void {
    this.bulkDialogOpen.set(false);
    this.pendingBulkOperation.set('');
    this.selectedRefs.set(new Set());
    this.uiState.set('success');
  }

  // ----- Export action + Export reason (Confidential, 4.4.2 + 4.4.3) -----
  protected readonly exportDialogOpen = signal(false);
  protected readonly exportReason = signal('');
  protected readonly exportReasonMin = 10;
  protected readonly exportReasonMax = 2000;
  protected readonly exportConfirmation = computed(() => ({
    classification: 'Official — Sensitive',
    purpose: 'Donation record export',
    scope: `${this.activeOwner().name} · ${this.formatDate(this.rangeStart())} – ${this.formatDate(this.rangeEnd())}`,
    rowFileCount: `${this.recordCount()} rows · 1 file (CSV)`,
    expiry: 'Link expires in 24 hours',
    auditReference: 'AUD-EXP-2026-0518',
  }));

  /** Export reason validity — 10–2,000 characters of meaningful text (4.4.2 Export reason). */
  protected readonly exportReasonValid = computed(() => {
    const len = this.exportReason().trim().length;
    return len >= this.exportReasonMin && len <= this.exportReasonMax;
  });
  protected readonly exportReasonCount = computed(() => this.exportReason().trim().length);

  protected openExportDialog(): void {
    if (!this.exportAllowed()) {
      return;
    }
    this.exportReason.set('');
    this.exportDialogOpen.set(true);
  }
  protected cancelExport(): void {
    this.exportDialogOpen.set(false);
  }
  /** Confirm classification, purpose, scope, count, expiry and audit reference before release (4.4.3 Export). */
  protected confirmExport(): void {
    if (!this.exportReasonValid()) {
      return;
    }
    this.exportDialogOpen.set(false);
    this.uiState.set('success');
  }

  // ================= UI state demonstrability (4.4.4 / 4.4.7) =================
  protected readonly uiState = signal<UiState>('ready');
  protected readonly uiStates: readonly UiState[] = [
    'ready',
    'loading',
    'empty',
    'validation',
    'duplicate',
    'no-access',
    'conflict',
    'dependency-failure',
    'success',
  ];
  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  // ================= Related and history + Persistent outcome (4.4.1) =================
  protected readonly relatedTabs = [
    { key: 'linked', label: 'Linked records', permitted: true, rows: [
      { primary: 'CAMP-2026-0178', secondary: 'Blind Stick Distribution Drive', meta: 'Campaign · Active' },
      { primary: 'RECON-2026-0442', secondary: 'July settlement batch', meta: 'Reconciliation · Open' },
    ] },
    { key: 'documents', label: 'Documents', permitted: true, rows: [
      { primary: 'Donation register — July 2026', secondary: 'CSV · 128 KB', meta: 'Generated 29 Jul 2026' },
    ] },
    { key: 'activity', label: 'Activity', permitted: true, rows: [
      { primary: 'Filter applied', secondary: 'Range narrowed to 12–22 Jul', meta: 'S. Bennett · 09:32 IST' },
      { primary: 'Data refreshed', secondary: 'Record set recomputed', meta: 'System · 09:30 IST' },
    ] },
    { key: 'integration', label: 'Integration status', permitted: true, rows: [
      { primary: 'Settlement feed', secondary: 'Healthy', meta: 'Last sync 09:28 IST' },
    ] },
    { key: 'support', label: 'Support correlation', permitted: true, rows: [
      { primary: 'INT-77213', secondary: 'Export job retry', meta: 'Open · linked to dependency failure' },
    ] },
    { key: 'audit', label: 'Audit chronology', permitted: true, rows: [
      { primary: 'View opened', secondary: '', meta: 'S. Bennett · 09:31 IST' },
    ] },
  ] as const;
  protected readonly visibleRelatedTabs = computed(() => this.relatedTabs.filter((t) => t.permitted));
  protected readonly activeRelatedTab = signal<string>('linked');
  protected readonly activeRelatedRows = computed(
    () => this.visibleRelatedTabs().find((t) => t.key === this.activeRelatedTab())?.rows ?? [],
  );
  protected selectRelatedTab(key: string): void {
    this.activeRelatedTab.set(key);
  }

  /** Persistent outcome (4.4.1). A toast may support but never replace this. */
  protected readonly persistentOutcome = computed(() => ({
    reference: this.stableReference,
    state: this.lifecycleState,
    effectiveTime: this.lastRefresh(),
    downstreamStatus: this.selectedCount() > 0 ? `${this.selectedCount()} record(s) selected` : 'No pending action',
    owner: this.owner,
    nextAction: 'Review filtered record set',
  }));

  // ================= Formatting helpers =================
  protected formatAmount(value: number): string {
    return value.toLocaleString('en-IN');
  }
  protected statusClass(status: RecordStatus): string {
    switch (status) {
      case 'Completed':
        return 'badge-good';
      case 'In Progress':
        return 'badge-info';
      case 'Received':
        return 'badge-received';
      case 'Failed':
        return 'badge-error';
    }
  }
  private formatDate(iso: string): string {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return iso;
    }
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
}

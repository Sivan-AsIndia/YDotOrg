import { CommonModule } from '@angular/common';
import { Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

/**
 * UX-UI-08 — Saved view builder.
 *
 * Faithful implementation of section 4.8 of the YDot Practical UI/UX Generation
 * Specification. Every region (4.8.1), field (4.8.2), action (4.8.3), UI state
 * (4.8.4), responsive/accessibility/privacy rule (4.8.5) and validation /
 * confirmation pattern (4.8.6) below maps directly to the controlled contract.
 * Nothing outside that contract is added.
 *
 *  Route            : /ux/saved-view-builder
 *  Purpose          : Allow a person to preserve a private or shared filter,
 *                     column and sort arrangement without changing the
 *                     underlying records.
 *  Primary users    : Operational users; Executive Sponsor; All users; Module users
 *  View permission  : ux.saved-view-builder.view
 *  Data scope       : Only records inside the actor's active organisation,
 *                     programme, campaign, geography, warehouse, queue,
 *                     assignment or explicit record scope.
 *  Primary action   : Preview
 *  History rule     : Only an unused draft without downstream references may be
 *                     permanently deleted; otherwise use a controlled lifecycle
 *                     action.
 *  Theme            : Dark Meadow task surface; warm-paper data rows; calm-blue
 *                     information; antique-gold focus/progress.
 */

/** The eight required UI states from 4.8.4, plus the settled "ready" surface. */
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

/** The guided task steps from the task header (4.8.1). Preview is the primary gate. */
type BuilderStep = 'configure' | 'preview' | 'save';

/** Effective permission set decided server-side; the client mirrors it (4.8.3, 4.8.7). */
interface EffectivePermissions {
  readonly view: boolean; // ux.saved-view-builder.view
  readonly preview: boolean; // ux.saved-view-builder.preview
  readonly savePrivateView: boolean; // ux.saved-view-builder.save-private-view
  readonly requestSharedView: boolean; // ux.saved-view-builder.request-shared-view
  readonly setDefault: boolean; // ux.saved-view-builder.set-default
  readonly deleteUnusedView: boolean; // ux.saved-view-builder.delete-unused-view
}

/** A source module the person may build a view over — Source screen field (4.8.2). */
interface ModuleOption {
  readonly value: string;
  readonly label: string;
}

/** Kinds of filter value editor rendered for the Filter definition field (4.8.2). */
type FilterKind = 'chips' | 'date-range' | 'text';

/** A single row in the Filter definition (4.8.2 / 4.8.1 Context and filters). */
interface FilterRow {
  readonly id: number;
  field: string;
  operator: string;
  kind: FilterKind;
  values: string[]; // for chips
  text: string; // for text
  from: string; // for date-range
  to: string; // for date-range
}

/** A group of filter rows (4.8.1 Context and filters — grouped conditions). */
interface FilterGroup {
  readonly id: number;
  filters: FilterRow[];
}

/** A column in the Column order field; ordering is the contract's "order" (4.8.2). */
interface ColumnItem {
  readonly key: string;
  readonly label: string;
  selected: boolean;
  visible: boolean;
}

/** A single sort level in the Sort order field (4.8.2). */
interface SortLevel {
  readonly id: number;
  field: string;
  direction: 'Ascending' | 'Descending';
}

/** Per-module content — base views, filter fields, columns (4.8.2 Source screen). */
interface ModuleConfig {
  readonly baseViews: readonly string[];
  readonly filterFields: readonly string[];
  readonly columns: ReadonlyArray<Omit<ColumnItem, 'selected' | 'visible'> & { selected: boolean }>;
  readonly extraColumns: readonly string[];
}

/** A saved view surfaced in Related and history (4.8.1). */
interface RecentView {
  readonly reference: string;
  readonly name: string;
  readonly visibility: 'Private' | 'Shared';
  readonly shareLabel: string;
  readonly updated: string;
  readonly lastUsedTime: string;
  readonly hasDownstreamReferences: boolean; // gates permanent deletion (History rule)
}

@Component({
  selector: 'app-saved-view-builder',
  imports: [CommonModule, FormsModule],
  templateUrl: './saved-view-builder.html',
  styleUrl: './saved-view-builder.css',
})
export class SavedViewBuilderComponent {
  // ===================================================================
  // Task header (4.8.1) — title, stable reference, lifecycle state,
  // owner, freshness and one primary action.
  // ===================================================================
  protected readonly pageTitle = 'Saved View Builder';
  protected readonly pageSubtitle =
    'Create and personalise your view by selecting filters, columns, sorting and display preferences.';
  protected readonly stableReference = signal('SV-2026-0042 (Draft)');
  protected readonly lifecycleState = signal('Draft');
  protected readonly owner = signal('Priya Nair · Programme Manager');
  protected readonly operatingTimeZone = 'Asia/Kolkata · IST (UTC+05:30)';
  protected readonly lastRefresh = signal('29 Jul 2026, 09:30 IST');

  /** Effective permissions; every action is gated by these (4.8.3, 4.8.7). */
  protected readonly permissions: EffectivePermissions = {
    view: true,
    preview: true,
    savePrivateView: true,
    requestSharedView: true,
    setDefault: true,
    deleteUnusedView: true,
  };

  // ===================================================================
  // Guided task steps (4.8.1 task header) — Configure, Preview, Save.
  // ===================================================================
  protected readonly steps: ReadonlyArray<{ key: BuilderStep; index: number; title: string; caption: string }> = [
    { key: 'configure', index: 1, title: 'Configure', caption: 'Define filters, columns and sorting' },
    { key: 'preview', index: 2, title: 'Preview', caption: 'Review your results' },
    { key: 'save', index: 3, title: 'Save', caption: 'Name, share and finalise' },
  ];
  protected readonly currentStep = signal<BuilderStep>('configure');

  // ===================================================================
  // Per-module content (4.8.2) — base views, filter fields, columns.
  // Selecting a module rebuilds the base view, filters and columns so the
  // builder always shows content related to the chosen module.
  // ===================================================================
  private readonly moduleConfigs: Record<string, ModuleConfig> = {
    Donations: {
      baseViews: ['All Donations (Default)', 'Corporate Donations', 'High Value Donations', 'My Team Donations'],
      filterFields: ['Donation Type', 'Status', 'Donation Date', 'Organisation', 'Amount (₹)', 'Campaign', 'Payment Mode'],
      columns: [
        { key: 'donation-id', label: 'Donation ID', selected: true },
        { key: 'donor-org', label: 'Donor / Organisation', selected: true },
        { key: 'donation-type', label: 'Donation Type', selected: true },
        { key: 'amount', label: 'Amount (₹)', selected: true },
        { key: 'status', label: 'Status', selected: true },
        { key: 'donation-date', label: 'Donation Date', selected: true },
        { key: 'campaign', label: 'Campaign', selected: false },
        { key: 'payment-mode', label: 'Payment Mode', selected: false },
      ],
      extraColumns: ['Receipt Number', 'Tax Exemption', 'Donor Email', 'Acknowledgement Status'],
    },
    Beneficiaries: {
      baseViews: ['All Beneficiaries (Default)', 'Active Beneficiaries', 'Pending Verification', 'My Region Beneficiaries'],
      filterFields: ['Beneficiary Type', 'Status', 'Registration Date', 'Region', 'Age Group', 'Programme', 'Verification State'],
      columns: [
        { key: 'beneficiary-id', label: 'Beneficiary ID', selected: true },
        { key: 'beneficiary-name', label: 'Name', selected: true },
        { key: 'beneficiary-type', label: 'Beneficiary Type', selected: true },
        { key: 'region', label: 'Region', selected: true },
        { key: 'status', label: 'Status', selected: true },
        { key: 'registration-date', label: 'Registration Date', selected: true },
        { key: 'programme', label: 'Programme', selected: false },
        { key: 'verification-state', label: 'Verification State', selected: false },
      ],
      extraColumns: ['Guardian Name', 'Contact Number', 'Distribution Count', 'Last Assessment'],
    },
    Campaigns: {
      baseViews: ['All Campaigns (Default)', 'Active Campaigns', 'Planned Campaigns', 'My Team Campaigns'],
      filterFields: ['Campaign Status', 'Target Amount', 'Launch Date', 'Owner', 'Raised Amount', 'Channel', 'Programme'],
      columns: [
        { key: 'campaign-code', label: 'Campaign Code', selected: true },
        { key: 'campaign-name', label: 'Campaign Name', selected: true },
        { key: 'campaign-status', label: 'Status', selected: true },
        { key: 'target-amount', label: 'Target Amount (₹)', selected: true },
        { key: 'raised-amount', label: 'Raised Amount (₹)', selected: true },
        { key: 'launch-date', label: 'Launch Date', selected: true },
        { key: 'owner', label: 'Owner', selected: false },
        { key: 'channel', label: 'Channel', selected: false },
      ],
      extraColumns: ['End Date', 'Progress %', 'Donations Count', 'Programme'],
    },
    Stock: {
      baseViews: ['All Stock (Default)', 'Low Stock', 'In Warehouse', 'My Warehouse Stock'],
      filterFields: ['Item Type', 'Stock Status', 'Received Date', 'Warehouse', 'Quantity', 'Supplier', 'Batch'],
      columns: [
        { key: 'item-code', label: 'Item Code', selected: true },
        { key: 'item-name', label: 'Item Name', selected: true },
        { key: 'item-type', label: 'Item Type', selected: true },
        { key: 'warehouse', label: 'Warehouse', selected: true },
        { key: 'quantity', label: 'Quantity', selected: true },
        { key: 'stock-status', label: 'Stock Status', selected: true },
        { key: 'received-date', label: 'Received Date', selected: false },
        { key: 'supplier', label: 'Supplier', selected: false },
      ],
      extraColumns: ['Batch', 'Expiry Date', 'Reorder Level', 'Unit Cost (₹)'],
    },
  };

  protected readonly modules: readonly ModuleOption[] = [
    { value: 'Donations', label: 'Donations' },
    { value: 'Beneficiaries', label: 'Beneficiaries' },
    { value: 'Campaigns', label: 'Campaigns' },
    { value: 'Stock', label: 'Stock' },
  ];
  protected readonly selectedModule = signal('Donations');

  protected readonly baseViews = signal<readonly string[]>(this.moduleConfigs['Donations'].baseViews);
  protected readonly selectedBaseView = signal(this.moduleConfigs['Donations'].baseViews[0]);

  // ===================================================================
  // Filter definition (4.8.2) / Context and filters region (4.8.1).
  // Filters are organised into groups; each group can hold many filters.
  // ===================================================================
  /** Fields available for the active module (server-supplied in production). */
  protected readonly filterFields = signal<readonly string[]>(this.moduleConfigs['Donations'].filterFields);
  protected readonly operators: readonly string[] = ['is', 'is not', 'in', 'contains', 'between', 'greater than', 'less than'];

  /**
   * Catalogue of selectable values per chips field (4.8.2 — controlled choice values).
   * When a filter field is chosen, its related options are offered on the right so the
   * person selects from valid catalogue values instead of typing free text.
   */
  private readonly fieldValueCatalogue: Record<string, readonly string[]> = {
    // Donations
    'Donation Type': ['Corporate Donation', 'Individual Donation', 'In-Kind Donation', 'Recurring Donation', 'Grant'],
    'Status': ['Completed', 'In Progress', 'Received', 'Pending', 'Failed', 'Refunded'],
    'Campaign': ['Educate a Child 2025', 'Clean Water Initiative', 'Health Camp Rural Drive', 'Food for All'],
    'Payment Mode': ['UPI', 'Card', 'Net Banking', 'Cash', 'Cheque'],
    // Beneficiaries
    'Beneficiary Type': ['Child', 'Adult', 'Senior', 'Family', 'Group'],
    'Region': ['North', 'South', 'East', 'West', 'Central'],
    'Age Group': ['0-5', '6-12', '13-18', '19-45', '45+'],
    'Programme': ['Education', 'Health', 'Nutrition', 'Water', 'Livelihood'],
    'Verification State': ['Verified', 'Pending', 'Rejected'],
    // Campaigns
    'Campaign Status': ['Active', 'Planned', 'Closed', 'On Hold', 'Draft'],
    'Owner': ['Priya Nair', 'Arun Kumar', 'Neha Patel', 'Vikram Nair'],
    'Channel': ['Email', 'SMS', 'Social', 'Web', 'Event'],
    // Stock
    'Item Type': ['Blind Stick', 'Kit', 'Book', 'Medicine', 'Food'],
    'Stock Status': ['In Stock', 'Low Stock', 'Out of Stock', 'Reserved'],
    'Warehouse': ['Chennai', 'Mumbai', 'Delhi', 'Kolkata'],
    'Supplier': ['Supplier A', 'Supplier B', 'Supplier C'],
    'Batch': ['Batch 2025-A', 'Batch 2025-B', 'Batch 2026-A'],
  };

  /** Catalogue options for a chips filter, excluding already-selected values. */
  protected chipOptionsFor(f: FilterRow): readonly string[] {
    return (this.fieldValueCatalogue[f.field] ?? []).filter((o) => !f.values.includes(o));
  }

  /** Add a selected catalogue value to a chips filter (deduplicated). */
  protected addFilterValue(groupId: number, id: number, value: string): void {
    if (!value) {
      return;
    }
    this.updateFilter(groupId, id, (r) =>
      r.values.includes(value) ? r : { ...r, values: [...r.values, value] },
    );
  }

  private filterSeq = 4;
  private groupSeq = 1;
  protected readonly groups = signal<FilterGroup[]>([
    {
      id: 1,
      filters: [
        { id: 1, field: 'Donation Type', operator: 'is', kind: 'chips', values: ['Corporate Donation'], text: '', from: '', to: '' },
        { id: 2, field: 'Status', operator: 'in', kind: 'chips', values: ['Completed', 'In Progress', 'Received'], text: '', from: '', to: '' },
        { id: 3, field: 'Donation Date', operator: 'between', kind: 'date-range', values: [], text: '', from: '01 Jul 2026', to: '31 Jul 2026' },
        { id: 4, field: 'Organisation', operator: 'contains', kind: 'text', values: [], text: 'GreenSol', from: '', to: '' },
      ],
    },
  ]);

  /** Flat list of every filter across all groups — used by the View Summary. */
  protected readonly allFilters = computed(() => this.groups().flatMap((g) => g.filters));

  // ===================================================================
  // Column order (4.8.2) — selection, visibility and order.
  // ===================================================================
  protected readonly columns = signal<ColumnItem[]>(
    this.moduleConfigs['Donations'].columns.map((c) => ({ ...c, visible: true })),
  );

  /** Default column arrangement, used by "Reset to Default". */
  private defaultColumns: ReadonlyArray<ColumnItem> = this.columns().map((c) => ({ ...c }));

  // ===================================================================
  // Sort order (4.8.2) — one or more ordered sort levels.
  // ===================================================================
  private sortSeq = 2;
  protected readonly sortLevels = signal<SortLevel[]>([
    { id: 1, field: 'Donation Date', direction: 'Descending' },
    { id: 2, field: 'Amount (₹)', direction: 'Descending' },
  ]);
  protected readonly sortDirections: readonly SortLevel['direction'][] = ['Ascending', 'Descending'];

  // ===================================================================
  // Default view (4.8.2) + display preferences (part of the arrangement).
  // ===================================================================
  protected readonly showGroupHeaders = signal(true);
  protected readonly compactRowHeight = signal(false);
  protected readonly enableRowSelection = signal(true);
  protected readonly rememberColumnWidths = signal(true);
  protected readonly rowsPerPageOptions: readonly number[] = [10, 25, 50, 100];
  protected readonly rowsPerPage = signal(25);
  protected readonly defaultView = signal<'Table' | 'Card'>('Table');

  // ===================================================================
  // Save step fields (4.8.2) — View name, Owner, Visibility, Shared role,
  // Last used time. Presented in the Save step of the guided task.
  // ===================================================================
  protected readonly viewName = signal('');
  protected readonly owners: readonly string[] = [
    'Priya Nair · Programme Manager',
    'Anita Rao · Access Administrator',
    'Ravi Kumar · Finance Manager',
  ];
  protected readonly viewOwner = signal('Priya Nair · Programme Manager');
  protected readonly visibility = signal<'Private' | 'Shared'>('Private');
  protected readonly sharedRoles: readonly string[] = [
    'Programme Manager',
    'Executive Sponsor',
    'Operations Lead',
    'Programme Officer',
  ];
  protected readonly sharedRole = signal('');
  protected readonly lastUsedTime = signal('—');

  // ===================================================================
  // Related and history (4.8.1) — recent saved views.
  // ===================================================================
  protected readonly recentViews: readonly RecentView[] = [
    { reference: 'SV-2026-0031', name: 'Corporate Donations - July', visibility: 'Shared', shareLabel: 'Shared with 8 users', updated: 'Updated 2 days ago', lastUsedTime: '27 Jul 2026, 16:10 IST', hasDownstreamReferences: true },
    { reference: 'SV-2026-0028', name: 'High Value Donations', visibility: 'Private', shareLabel: 'Private', updated: 'Updated 5 days ago', lastUsedTime: '24 Jul 2026, 11:02 IST', hasDownstreamReferences: false },
    { reference: 'SV-2026-0021', name: 'Donations - My Team', visibility: 'Shared', shareLabel: 'Shared with 12 users', updated: 'Updated 1 week ago', lastUsedTime: '22 Jul 2026, 09:44 IST', hasDownstreamReferences: true },
    { reference: 'SV-2026-0015', name: 'Fundraising Progress', visibility: 'Shared', shareLabel: 'Shared with 6 users', updated: 'Updated 2 weeks ago', lastUsedTime: '15 Jul 2026, 14:30 IST', hasDownstreamReferences: false },
  ];

  // ===================================================================
  // UI state demonstrability (4.8.4 / 4.8.7).
  // ===================================================================
  protected readonly uiState = signal<UiState>('ready');

  // ===================================================================
  // Actions surface (4.8.3) — overflow menu and danger confirmation.
  // ===================================================================
  protected readonly overflowOpen = signal(false);
  protected readonly deleteDialogOpen = signal(false);
  protected readonly deleteTarget = signal<RecentView | null>(null);
  protected readonly deleteReason = signal('');

  /** Persistent confirmation of the last committed action (4.8.1 persistent outcome). */
  protected readonly persistentOutcome = signal<{
    reference: string;
    state: string;
    effectiveTime: string;
    downstreamStatus: string;
    nextAction: string;
  } | null>(null);

  /** Validation summary entries shown in the Validation state (4.8.4 / 4.8.6). */
  protected readonly validationErrors = signal<Array<{ field: string; message: string }>>([]);

  // ===================================================================
  // Derived summary — Decision / review region (4.8.1).
  // ===================================================================
  protected readonly selectedColumns = computed(() => this.columns().filter((c) => c.selected));
  protected readonly filtersCount = computed(() => this.allFilters().length);
  protected readonly columnsCount = computed(() => this.selectedColumns().length);
  protected readonly sortCount = computed(() => this.sortLevels().length);

  /** Human summary of one filter for the View Summary panel. */
  protected filterSummary(f: FilterRow): string {
    if (f.kind === 'chips') {
      return f.values.length ? f.values.join(', ') : '(any)';
    }
    if (f.kind === 'date-range') {
      return `${f.from || '…'} - ${f.to || '…'}`;
    }
    return f.text || '(empty)';
  }

  protected readonly displaySummary = computed(() => {
    const parts = [`${this.defaultView()} view`, `${this.rowsPerPage()} rows per page`];
    parts.push(this.showGroupHeaders() ? 'Group headers on' : 'Group headers off');
    parts.push(this.compactRowHeight() ? 'Compact rows' : 'Comfortable rows');
    if (this.rememberColumnWidths()) {
      parts.push('Remember column widths');
    }
    return parts.join(' · ');
  });

  /** The view is valid when a compatible arrangement exists within scope (4.8.3). */
  protected readonly isValid = computed(
    () => this.selectedModule().length > 0 && this.columnsCount() > 0 && this.uiState() !== 'no-access',
  );

  /** Whether the primary action (Preview) is currently allowed (4.8.1, 4.8.3). */
  protected readonly previewAllowed = computed(
    () => this.permissions.preview && this.isValid() && this.uiState() !== 'no-access',
  );

  // ===================================================================
  // Behaviour
  // ===================================================================

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected goToStep(step: BuilderStep): void {
    this.currentStep.set(step);
  }

  // ----- Source screen: switching module rebuilds related content -----
  protected onModuleChange(value: string): void {
    this.selectedModule.set(value);
    const config = this.moduleConfigs[value];
    if (!config) {
      return;
    }
    // Base view — module-related default and options.
    this.baseViews.set(config.baseViews);
    this.selectedBaseView.set(config.baseViews[0]);
    // Filter fields — module-related.
    this.filterFields.set(config.filterFields);
    // Columns — module-related default arrangement.
    const cols = config.columns.map((c) => ({ ...c, visible: true }));
    this.columns.set(cols);
    this.defaultColumns = cols.map((c) => ({ ...c }));
    // Reset filters to one starter group using this module's first field.
    const seedField = config.filterFields[0];
    const kind = this.kindForField(seedField);
    this.groupSeq = 1;
    this.filterSeq = 1;
    this.groups.set([
      {
        id: 1,
        filters: [
          { id: 1, field: seedField, operator: this.defaultOperatorForKind(kind), kind, values: [], text: '', from: '', to: '' },
        ],
      },
    ]);
    // Reset sorting to this module's first field.
    this.sortSeq = 1;
    this.sortLevels.set([{ id: 1, field: seedField, direction: 'Descending' }]);
  }

  /** Choose the value editor that fits the selected field (4.8.2 Filter definition). */
  private kindForField(field: string): FilterKind {
    if (/date/i.test(field)) {
      return 'date-range';
    }
    if (/amount|quantity|value|age|target|raised|count|level|cost|batch|number/i.test(field)) {
      return 'text';
    }
    return 'chips';
  }

  private defaultOperatorForKind(kind: FilterKind): string {
    if (kind === 'date-range') {
      return 'between';
    }
    if (kind === 'text') {
      return 'contains';
    }
    return 'is';
  }

  // ----- Filter definition (4.8.1: clearing a filter is explicit) -----

  /** Add a fresh filter row inside an existing group. */
  protected addFilterToGroup(groupId: number): void {
    const id = ++this.filterSeq;
    const seedField = this.filterFields()[0];
    const kind = this.kindForField(seedField);
    this.groups.update((groups) =>
      groups.map((g) =>
        g.id === groupId
          ? { ...g, filters: [...g.filters, { id, field: seedField, operator: this.defaultOperatorForKind(kind), kind, values: [], text: '', from: '', to: '' }] }
          : g,
      ),
    );
  }

  /** Add a new filter group, seeded with one filter row. */
  protected addFilterGroup(): void {
    const groupId = ++this.groupSeq;
    const id = ++this.filterSeq;
    const seedField = this.filterFields()[0];
    const kind = this.kindForField(seedField);
    this.groups.update((groups) => [
      ...groups,
      { id: groupId, filters: [{ id, field: seedField, operator: this.defaultOperatorForKind(kind), kind, values: [], text: '', from: '', to: '' }] },
    ]);
  }

  protected removeFilterGroup(groupId: number): void {
    this.groups.update((groups) => groups.filter((g) => g.id !== groupId));
  }

  protected removeFilter(groupId: number, id: number): void {
    this.groups.update((groups) =>
      groups
        .map((g) => (g.id === groupId ? { ...g, filters: g.filters.filter((r) => r.id !== id) } : g))
        // Drop a group once its last filter is removed.
        .filter((g) => g.filters.length > 0),
    );
  }

  protected removeFilterValue(groupId: number, id: number, value: string): void {
    this.updateFilter(groupId, id, (r) => ({ ...r, values: r.values.filter((v) => v !== value) }));
  }

  /** Switching the field also switches the value editor to the matching kind. */
  protected updateFilterField(groupId: number, id: number, field: string): void {
    this.updateFilter(groupId, id, (r) => {
      const kind = this.kindForField(field);
      const changedKind = kind !== r.kind;
      return {
        ...r,
        field,
        kind,
        operator: changedKind ? this.defaultOperatorForKind(kind) : r.operator,
        values: kind === 'chips' ? r.values : [],
        text: kind === 'text' ? r.text : '',
        from: kind === 'date-range' ? r.from : '',
        to: kind === 'date-range' ? r.to : '',
      };
    });
  }

  protected updateFilterOperator(groupId: number, id: number, operator: string): void {
    this.updateFilter(groupId, id, (r) => ({ ...r, operator }));
  }

  protected updateFilterText(groupId: number, id: number, text: string): void {
    this.updateFilter(groupId, id, (r) => ({ ...r, text }));
  }

  protected updateFilterFrom(groupId: number, id: number, from: string): void {
    this.updateFilter(groupId, id, (r) => ({ ...r, from }));
  }

  protected updateFilterTo(groupId: number, id: number, to: string): void {
    this.updateFilter(groupId, id, (r) => ({ ...r, to }));
  }

  private updateFilter(groupId: number, id: number, fn: (r: FilterRow) => FilterRow): void {
    this.groups.update((groups) =>
      groups.map((g) => (g.id === groupId ? { ...g, filters: g.filters.map((r) => (r.id === id ? fn(r) : r)) } : g)),
    );
  }

  /** Clearing every filter is an explicit, focus-predictable action (4.8.1). */
  protected clearAllFilters(): void {
    this.groups.set([]);
  }

  // ----- Column order -----
  protected toggleColumn(key: string): void {
    this.columns.update((cols) => cols.map((c) => (c.key === key ? { ...c, selected: !c.selected } : c)));
  }

  protected toggleColumnVisibility(key: string): void {
    this.columns.update((cols) => cols.map((c) => (c.key === key ? { ...c, visible: !c.visible } : c)));
  }

  protected removeColumn(key: string): void {
    this.columns.update((cols) => cols.map((c) => (c.key === key ? { ...c, selected: false } : c)));
  }

  /** Add a new column to the arrangement (4.8.2 Column order). */
  protected addColumn(): void {
    const existing = new Set(this.columns().map((c) => c.label));
    const pool = (this.moduleConfigs[this.selectedModule()]?.extraColumns ?? []).filter((l) => !existing.has(l));
    const label = pool.length ? pool[0] : `Custom Column ${this.columns().length + 1}`;
    const key = label.toLowerCase().replace(/[^a-z0-9]+/g, '-') + '-' + (this.columns().length + 1);
    this.columns.update((cols) => [...cols, { key, label, selected: true, visible: true }]);
  }

  protected moveColumn(key: string, direction: -1 | 1): void {
    this.columns.update((cols) => {
      const idx = cols.findIndex((c) => c.key === key);
      const next = idx + direction;
      if (idx < 0 || next < 0 || next >= cols.length) {
        return cols;
      }
      const copy = [...cols];
      const [item] = copy.splice(idx, 1);
      copy.splice(next, 0, item);
      return copy;
    });
  }

  protected resetColumnsToDefault(): void {
    this.columns.set(this.defaultColumns.map((c) => ({ ...c })));
  }

  // ----- Sort order -----
  protected addSortLevel(): void {
    const id = ++this.sortSeq;
    this.sortLevels.update((levels) => [...levels, { id, field: this.filterFields()[0], direction: 'Ascending' }]);
  }

  protected removeSortLevel(id: number): void {
    this.sortLevels.update((levels) => levels.filter((l) => l.id !== id));
  }

  protected updateSortField(id: number, field: string): void {
    this.sortLevels.update((levels) => levels.map((l) => (l.id === id ? { ...l, field } : l)));
  }

  protected updateSortDirection(id: number, direction: SortLevel['direction']): void {
    this.sortLevels.update((levels) => levels.map((l) => (l.id === id ? { ...l, direction } : l)));
  }

  // ----- Display preferences / Default view (also the "Set default" control) -----
  protected setDefaultView(view: 'Table' | 'Card'): void {
    this.defaultView.set(view);
  }

  // ===================================================================
  // Actions, eligibility and result (4.8.3)
  // ===================================================================

  /** Preview — Primary action (4.8.3). Advances to the Preview step within scope. */
  protected preview(): void {
    if (!this.previewAllowed()) {
      return;
    }
    this.overflowOpen.set(false);
    this.currentStep.set('preview');
    this.persistentOutcome.set({
      reference: this.stableReference(),
      state: 'Previewed',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: 'No changes made to underlying records',
      nextAction: 'Review results, then Save',
    });
  }

  /** Save private view — Secondary / overflow (4.8.3). One draft; required name (4.8.6). */
  protected savePrivateView(): void {
    this.overflowOpen.set(false);
    if (!this.permissions.savePrivateView) {
      return;
    }
    this.currentStep.set('save');
    this.visibility.set('Private');
    if (!this.validateForSave()) {
      return;
    }
    this.lastUsedTime.set(this.lastRefresh());
    this.persistentOutcome.set({
      reference: this.stableReference(),
      state: 'Saved (Private)',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: 'Private to owner; no shared definition changed',
      nextAction: 'Set as default or request a shared view',
    });
    this.uiState.set('success');
  }

  /** Request shared view — Secondary / overflow (4.8.3). Requires a shared role (4.8.6). */
  protected requestSharedView(): void {
    this.overflowOpen.set(false);
    if (!this.permissions.requestSharedView) {
      return;
    }
    this.currentStep.set('save');
    this.visibility.set('Shared');
    if (!this.validateForSave()) {
      return;
    }
    this.persistentOutcome.set({
      reference: this.stableReference(),
      state: 'Shared view requested',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: `Pending approval · ${this.sharedRole()}`,
      nextAction: 'Await approval before the shared view is published',
    });
    this.uiState.set('success');
  }

  /** Set default — Secondary / overflow (4.8.3). Marks the current default view. */
  protected setDefault(): void {
    this.overflowOpen.set(false);
    if (!this.permissions.setDefault) {
      return;
    }
    this.persistentOutcome.set({
      reference: this.stableReference(),
      state: 'Default set',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: `Default arrangement: ${this.defaultView()} view`,
      nextAction: 'Preview or save the arrangement',
    });
    this.uiState.set('success');
  }

  /**
   * Delete unused view — Danger / confirmation (4.8.3). Only an unused draft
   * without downstream references may be permanently deleted (History rule); a
   * named reason and consequence preview are required (4.8.3 / 4.8.6).
   */
  protected openDeleteDialog(view: RecentView): void {
    this.overflowOpen.set(false);
    this.deleteTarget.set(view);
    this.deleteReason.set('');
    this.deleteDialogOpen.set(true);
  }

  protected cancelDelete(): void {
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
  }

  protected confirmDelete(): void {
    const target = this.deleteTarget();
    if (!target || !this.permissions.deleteUnusedView) {
      return;
    }
    if (target.hasDownstreamReferences) {
      // History rule: not permitted to permanently delete — keep linked history.
      return;
    }
    if (!this.deleteReason().trim()) {
      // Required named reason (4.8.6 high-risk action).
      return;
    }
    this.persistentOutcome.set({
      reference: target.reference,
      state: 'Deleted (unused draft)',
      effectiveTime: this.lastRefresh(),
      downstreamStatus: 'Linked history preserved',
      nextAction: 'Return to saved views',
    });
    this.deleteDialogOpen.set(false);
    this.deleteTarget.set(null);
    this.uiState.set('success');
  }

  protected toggleOverflow(): void {
    this.overflowOpen.update((v) => !v);
  }

  // ===================================================================
  // Validation and confirmation content (4.8.6)
  // ===================================================================

  /** Validate the required Save-step fields; preserves entered values (4.8.4 / 4.8.6). */
  private validateForSave(): boolean {
    const errors: Array<{ field: string; message: string }> = [];
    if (!this.viewName().trim()) {
      errors.push({ field: 'View name', message: 'Enter View name.' });
    }
    if (this.visibility() === 'Shared' && !this.sharedRole().trim()) {
      errors.push({ field: 'Shared role', message: 'Enter Shared role.' });
    }
    this.validationErrors.set(errors);
    if (errors.length > 0) {
      this.uiState.set('validation');
      return false;
    }
    return true;
  }

  /** Cancel the whole builder task and return to the permitted landing. */
  protected cancel(): void {
    this.overflowOpen.set(false);
    console.info('Saved view builder cancelled — no changes committed.');
  }
}

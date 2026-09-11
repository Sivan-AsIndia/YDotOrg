import { CommonModule, DOCUMENT } from '@angular/common';
import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, Subject, Subscription } from 'rxjs';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  DonLookupItem,
  LeadListItem,
  LeadWorkQueueFilter,
  LeadWorkQueueResponse,
} from '../../../../Shared/models/donor-contract.model';

export type UiState = 'ready' | 'loading' | 'success' | 'error' | 'empty';

/**
 * One row of the queue, as this screen draws it.
 *
 * IT IS A VIEW OF `LeadListItem`, NOT A SEPARATE TRUTH. Every field is copied straight from the
 * server's row; nothing is computed here that the server also computes. `masked` in particular
 * is the server's `isContactMasked` - whether the caller may read a donor's phone number is a
 * permission decision, and a browser that decided it for itself would be deciding it wrongly.
 */
export interface LeadItem {
  readonly id: string;
  readonly reference: string;
  readonly name: string;
  readonly mobile: string;
  readonly email: string;
  readonly source: string;
  readonly campaign: string;
  readonly stage: string;
  readonly temperature: string;
  readonly donationPotential: string;
  readonly owner: string;
  readonly ownerUserId: string | null;
  readonly lastActivity: string;
  readonly nextFollowUp: string;
  readonly healthScore: number;
  readonly lastContactOutcome: string;
  readonly language: string;
  readonly masked: boolean;
  readonly converted: boolean;
  readonly donorId: string | null;
  readonly version: number;
  readonly permittedActions: readonly string[];
}

export interface KpiCard {
  readonly id: string;
  readonly label: string;
  readonly value: number;
  readonly hint: string;
}

export interface PipelineStage {
  readonly key: string;
  readonly label: string;
  readonly count: number;
}

export interface LeadQueuePermissions {
  readonly view: boolean;
  readonly create: boolean;
  readonly assign: boolean;
  readonly communicate: boolean;
  readonly schedule: boolean;
  readonly export: boolean;
}

/** Nothing until the server answers. A screen that assumes permissions shows buttons that 403. */
const NO_PERMISSIONS: LeadQueuePermissions = {
  view: false,
  create: false,
  assign: false,
  communicate: false,
  schedule: false,
  export: false,
};

/**
 * The tabs across the top of the queue, exactly as the workflow document draws them.
 *
 * EACH ONE IS A SERVER FILTER, not a predicate over an array in this browser. The distinction
 * matters because the grid is paged: filtering the current page client-side would show "4
 * unassigned" when the organisation has ninety, and the count on the tab would disagree with the
 * rows underneath it.
 */
const SAVED_VIEWS = [
  'All Leads',
  'Unassigned Leads',
  'Assigned Leads',
  'Hot Leads',
  'High Donation Potential',
  'Recently Added',
  'Converted Leads',
] as const;
type SavedView = (typeof SAVED_VIEWS)[number];

/**
 * SCR-DON-001 - Lead Queue. The document's central list page.
 *
 * WHAT THIS REPLACES. The component imported two JSON files at build time -
 * `donors-leads/lead-work-queue.json` for the screen chrome and `my-leads.json` for the rows -
 * seeded a `WorkflowStateService` signal from them and worked over that array in memory. Four
 * things followed, and all four were real:
 *
 *   - NOTHING WAS EVER SAVED. A lead assigned or contacted here was back to its old state on
 *     refresh, because no request ever left the browser.
 *   - EVERY ORGANISATION SAW THE SAME ELEVEN LEADS. A file compiled into the bundle has no idea
 *     who is asking, so tenant isolation stopped at the API boundary.
 *   - THE MASKING RULES COULD NOT WORK. Whether a lead's mobile number is visible depends on
 *     `don.donors.view-sensitive-contact`, which the server checks; a static file has one answer
 *     for everybody and it was "show it".
 *   - THE COUNTS WERE FICTION. The KPI cards and the pipeline tabs counted the rows in the file.
 *
 * IT NOW READS `GET /api/v1/donors/lead-work-queue`, which answers the rows, the dropdowns, the
 * status counts, the summary cards and the caller's permitted actions in ONE call - so the tabs
 * and the grid can never disagree, because they came from the same answer.
 */
@Component({
  selector: 'app-lead-work-queue',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './lead-work-queue.html',
  styleUrl: './lead-work-queue.css',
})
export class LeadWorkQueueComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DonorApiService);

  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchChanges = new Subject<void>();
  private loadSubscription?: Subscription;
  protected readonly previewDialog = viewChild<ElementRef<HTMLDialogElement>>('leadDialog');
  protected readonly optionSearch = signal<Partial<Record<string, string>>>({});
  protected readonly primaryKpis = computed(() => this.kpis().filter((kpi) => kpi.id !== 'hot'));
  protected readonly extraKpis = computed(() => this.kpis().filter((kpi) => kpi.id === 'hot'));
  protected readonly primaryViews = computed(() => this.savedViews().slice(0, 3));
  protected readonly extraViews = computed(() => this.savedViews().slice(3));
  protected readonly filterFields = computed(() => {
    const options = this.filterOptions();
    const choices = (values: readonly string[]) => values.map((value) => ({ value, label: value }));
    return [
      { key: 'stage', label: 'Stage', value: this.stageFilter(), options: options.stages },
      {
        key: 'temperature',
        label: 'Temperature',
        value: this.temperatureFilter(),
        options: options.temperatures,
      },
      {
        key: 'potential',
        label: 'Donation potential',
        value: this.potentialFilter(),
        options: options.potentials,
      },
      {
        key: 'source',
        label: 'Lead source',
        value: this.sourceFilter(),
        options: choices(options.sources),
      },
      {
        key: 'owner',
        label: 'Owner',
        value: this.ownerFilter(),
        options: [
          { value: 'Unassigned', label: 'Unassigned' },
          ...this.ownerOptions().filter((owner) => owner.value !== 'Unassigned'),
        ],
      },
    ];
  });

  protected onSearchChange(value: string): void {
    this.searchTerm.set(value);
    this.searchChanges.next();
  }

  protected setOptionSearch(key: string, value: string): void {
    this.optionSearch.update((current) => ({ ...current, [key]: value }));
  }

  protected matchingOptions(
    key: string,
    options: readonly DonLookupItem[],
    selected: string,
  ): readonly DonLookupItem[] {
    const query = (this.optionSearch()[key] ?? '').trim().toLocaleLowerCase();
    return options.filter(
      (option) => option.value === selected || option.label.toLocaleLowerCase().includes(query),
    );
  }

  protected setFilterValue(key: string, value: string): void {
    // Explicit field choices supersede saved views that force the same field.
    if (
      (key === 'temperature' && this.savedView() === 'Hot Leads') ||
      (key === 'potential' && this.savedView() === 'High Donation Potential') ||
      (key === 'owner' && ['Assigned Leads', 'Unassigned Leads'].includes(this.savedView()))
    ) {
      this.savedView.set('All Leads');
    }
    switch (key) {
      case 'stage':
        this.stageFilter.set(value);
        break;
      case 'temperature':
        this.temperatureFilter.set(value);
        break;
      case 'potential':
        this.potentialFilter.set(value);
        break;
      case 'source':
        this.sourceFilter.set(value);
        break;
      case 'owner':
        this.ownerFilter.set(value);
        break;
    }
  }

  protected onPreviewBackdrop(event: MouseEvent): void {
    const dialog = this.previewDialog()?.nativeElement;
    if (!dialog || event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (
      event.clientX < bounds.left ||
      event.clientX > bounds.right ||
      event.clientY < bounds.top ||
      event.clientY > bounds.bottom
    ) {
      this.closePreview();
    }
  }

  protected readonly icons: Record<string, string> = {
    potential: 'm12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z',
    add: 'M12 5v14M5 12h14',
    close: 'm6 6 12 12M6 18 18 6',
    search: 'M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',
    filter: 'M3 4h18l-7 8v7l-4 2v-9Z',
    refresh: 'M20 7v5h-5M4 17v-5h5M6 6a8 8 0 0 1 13 2M5 16a8 8 0 0 0 13 2',
    export: 'M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5',
    grid: 'M3 3h6v6H3ZM15 3h6v6h-6ZM3 15h6v6H3ZM15 15h6v6h-6Z',
    user: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0M4 21v-2a6 6 0 0 1 6-6h4a6 6 0 0 1 6 6v2Z',
    users:
      'M14 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0M2 21v-2a6 6 0 0 1 6-6h4a6 6 0 0 1 6 6v2ZM18 4a4 4 0 0 1 0 7M21 21v-3a5 5 0 0 0-2-4',
    assign: 'M13 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0M2 21v-2a6 6 0 0 1 6-6h3M18 13v8m-4-4h8',
    eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12ZM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',
    chat: 'M21 11.5a8.5 8.5 0 0 1-12.3 7.6L3 21l1.9-5.7A8.5 8.5 0 1 1 21 11.5Z',
    list: 'M4 5h16M4 12h16M4 19h16',
    file: 'M6 2h8l5 5v15H6ZM14 2v6h5M9 12h7M9 16h7',
    copy: 'M9 9h12v12H9ZM5 15H3V3h12v2',
    check: 'm5 12 4 4L19 6',
    calendar: 'M4 5h16v16H4ZM16 3v4M8 3v4M4 11h16',
    clock: 'M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0M12 6v6l4 2',
    arrow: 'M5 12h14m-6-6 6 6-6 6',
    alert: 'm12 3 10 18H2ZM12 9v5M12 17v1',
  };

  // Palette used to derive a consistent, distinct avatar colour per lead name.
  private readonly avatarPalette: readonly string[] = [
    '#2d6a4f',
    '#3b82c4',
    '#b45309',
    '#6d28d9',
    '#0f766e',
    '#c53030',
    '#0e7490',
    '#4f46e5',
  ];

  // ===========================================================================================
  // Screen chrome. Signals rather than constants, because the server supplies them.
  // ===========================================================================================
  protected readonly screen = signal({
    viewId: 'SCR-DON-001',
    title: 'Lead Work Queue',
    route: '/app/fundraising/relationships/lead-work-queue',
    purpose: 'Manage and monitor all leads within the tenant.',
    scope: '',
    lastRefresh: '',
    breadcrumb: ['Fundraising', 'Relationships', 'Lead Queue'] as readonly string[],
  });

  protected readonly uiState = signal<UiState>('loading');
  protected readonly errorMessage = signal<string>('');

  protected readonly permissions = signal<LeadQueuePermissions>(NO_PERMISSIONS);
  protected readonly kpis = signal<readonly KpiCard[]>([]);
  protected readonly pipeline = signal<readonly PipelineStage[]>([]);
  protected readonly savedViews = signal<readonly string[]>(SAVED_VIEWS);
  protected readonly filterOptions = signal<{
    readonly stages: readonly DonLookupItem[];
    readonly temperatures: readonly DonLookupItem[];
    readonly potentials: readonly DonLookupItem[];
    readonly sources: readonly string[];
  }>({ stages: [], temperatures: [], potentials: [], sources: [] });
  protected readonly ownerOptions = signal<readonly DonLookupItem[]>([]);

  // ===========================================================================================
  // Filter state. Every one of these re-queries the server.
  // ===========================================================================================
  protected readonly savedView = signal<string>('All Leads');
  protected readonly searchTerm = signal('');
  protected readonly stageFilter = signal<string>('');
  protected readonly temperatureFilter = signal<string>('');
  protected readonly potentialFilter = signal<string>('');
  protected readonly sourceFilter = signal<string>('');
  protected readonly ownerFilter = signal<string>('');
  protected readonly showFilters = signal(false);

  protected readonly leads = signal<readonly LeadItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly selectedIds = signal<Set<string>>(new Set());
  protected readonly selectedLead = signal<LeadItem | null>(null);
  protected readonly copiedField = signal<string | null>(null);

  constructor() {
    this.searchChanges
      .pipe(debounceTime(300), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    this.destroyRef.onDestroy(() => this.loadSubscription?.unsubscribe());
    afterRenderEffect((onCleanup) => {
      const lead = this.selectedLead();
      const dialog = this.previewDialog()?.nativeElement;
      if (!lead || !dialog) return;
      const previousOverflow = this.document.body.style.overflow;
      this.document.body.style.overflow = 'hidden';
      if (!dialog.open) dialog.showModal();
      onCleanup(() => {
        if (dialog.open) dialog.close();
        this.document.body.style.overflow = previousOverflow;
      });
    });

    // Coming back from Lead Capture. The reference is the API's, not one this browser minted,
    // so the row it opens is the row that was actually saved.
    const createdLeadId = this.route.snapshot.queryParamMap.get('createdLeadId');
    if (createdLeadId) {
      this.openAfterLoad = createdLeadId;
    }
    this.load();
  }

  private openAfterLoad: string | null = null;

  // ===========================================================================================
  // Loading
  // ===========================================================================================

  /**
   * One call fills the whole screen.
   *
   * THE FILTERS GO TO THE SERVER, not to a `.filter()` over what is already here. The grid is
   * paged, so a client-side predicate would filter one page and label it as the whole set.
   */
  private load(): void {
    this.uiState.set('loading');
    this.errorMessage.set('');

    this.loadSubscription?.unsubscribe();
    this.loadSubscription = this.api
      .getLeadWorkQueue(this.buildFilter())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => this.applyResponse(response),
        error: (error: unknown) => {
          this.errorMessage.set(apiErrorMessage(error));
          this.uiState.set('error');
        },
      });
  }

  /** The saved view and the filter controls, translated into the API's query string. */
  private buildFilter(): LeadWorkQueueFilter {
    const filter: LeadWorkQueueFilter = {
      page: 1,
      pageSize: 100,
      search: this.searchTerm().trim() || undefined,
      status: this.stageFilter() || null,
      temperature: this.temperatureFilter() || null,
      donationPotential: this.potentialFilter() || null,
    };

    const owner = this.ownerFilter();
    if (owner === 'Unassigned') {
      filter.assignmentState = 'Unassigned';
    } else if (owner && owner !== 'All') {
      filter.ownerUserId = owner;
    }

    switch (this.savedView() as SavedView) {
      case 'Unassigned Leads':
        filter.assignmentState = 'Unassigned';
        break;
      case 'Assigned Leads':
        filter.assignmentState = 'Assigned';
        break;
      case 'Hot Leads':
        filter.temperature = 'Hot';
        break;
      case 'High Donation Potential':
        filter.donationPotential = 'High';
        break;
      case 'Recently Added':
        filter.newestFirst = true;
        break;

      // THE ONLY TAB THAT ASKS FOR CONVERTED ROWS. Everywhere else they are hidden, because the
      // document says a converted lead leaves this queue for the Donor List.
      case 'Converted Leads':
        filter.isConverted = true;
        break;
    }

    return filter;
  }

  private applyResponse(response: LeadWorkQueueResponse): void {
    this.leads.set(response.leads.items.map((row) => this.toRow(row)));
    this.totalCount.set(response.leads.totalCount);
    const visibleIds = new Set(this.filteredLeads().map((lead) => lead.id));
    this.selectedIds.update((ids) => new Set([...ids].filter((id) => visibleIds.has(id))));

    this.screen.update((current) => ({
      ...current,
      scope: response.activeScope,
      lastRefresh: this.formatDateTime(response.lastRefreshedAtUtc),
    }));

    // THE CARDS COME FROM THE SERVER'S SUMMARY, not from counting the page. The summary is
    // scope-wide; the page is a hundred rows at most, and the two are different numbers.
    const summary = response.summary;
    this.kpis.set([
      { id: 'total', label: 'Total Leads', value: summary.totalLeads, hint: 'In selected scope' },
      {
        id: 'unassigned',
        label: 'Unassigned Leads',
        value: summary.unassignedLeads,
        hint: 'In selected scope',
      },
      {
        id: 'assigned',
        label: 'Assigned Leads',
        value: summary.assignedLeads,
        hint: 'In selected scope',
      },
      { id: 'hot', label: 'Hot Leads', value: summary.hotLeads, hint: 'In selected scope' },
      {
        id: 'converted',
        label: 'Converted Leads',
        value: summary.convertedLeads,
        hint: 'Donation recorded',
      },
      {
        id: 'potential',
        label: 'High Donation Potential',
        value: summary.highDonationPotential,
        hint: 'In selected scope',
      },
    ]);

    this.pipeline.set(
      response.statusOptions.map((option) => ({
        key: option.value,
        label: option.label,
        count: response.statusCounts[option.value] ?? 0,
      })),
    );

    this.filterOptions.set({
      stages: response.statusOptions,
      temperatures: response.temperatureOptions,
      potentials: response.donationPotentialOptions,
      sources: this.distinct(response.leads.items.map((row) => row.source ?? '').filter(Boolean)),
    });
    this.ownerOptions.set(response.ownerOptions);

    this.permissions.set(this.toPermissions(response.permittedActions));

    this.uiState.set(this.leads().length === 0 ? 'empty' : 'ready');

    if (this.openAfterLoad) {
      const created = this.leads().find(
        (lead) => lead.id === this.openAfterLoad || lead.reference === this.openAfterLoad,
      );
      if (created) {
        this.selectedLead.set(created);
      }
      this.openAfterLoad = null;
    }
  }

  private toRow(row: LeadListItem): LeadItem {
    return {
      id: row.id,
      reference: row.leadReference,
      name: row.name,

      // ALREADY MASKED, OR ALREADY NOT. The server sends '+91 98•••••210' or the real number
      // depending on the caller's permission, so there is nothing left to decide here.
      mobile: row.mobileNumber ?? '',
      email: row.emailAddress ?? '',
      source: row.source ?? '',
      campaign: row.campaignName ?? '',
      stage: row.status,
      temperature: row.temperature,
      donationPotential: row.donationPotential,
      owner: row.ownerName ?? 'Unassigned',
      ownerUserId: row.ownerUserId,
      lastActivity: row.lastContactOutcome,
      nextFollowUp: this.formatDate(row.nextActionDueUtc),
      healthScore: row.healthScore,
      lastContactOutcome: row.lastContactOutcome,
      language: row.preferredLanguage,
      masked: row.isContactMasked,
      converted: row.isConverted,
      donorId: row.convertedDonorId,
      version: row.version,
      permittedActions: row.permittedActions ?? [],
    };
  }

  /**
   * The caller's permitted actions, as the server listed them.
   *
   * THIS IS THE WHOLE ROLE MODEL ON THIS SCREEN. TENANT_ADMIN, INITIATOR and APPROVER differ
   * only in which verbs appear in `permittedActions`, so nothing here names a role. The
   * endpoints re-check every one of these, so a hidden button is a courtesy rather than a
   * control.
   */
  private toPermissions(permitted: readonly string[]): LeadQueuePermissions {
    // THE SERVER ANSWERS IN VERBS, NOT PERMISSION CODES. This used to compare against strings
    // like 'don.lead-work-queue.assign', which match nothing the API returns - so every flag was
    // false and Create Lead, Export, Communicate and Assign were all hidden. The endpoint answers
    // ['Accept','Filter','Open','Create','Assign','Contact','Qualify','Close'].
    //
    // CREATE IS ITS OWN VERB, and it has to be. It was read off `Accept || Contact`, which are
    // rights over leads that ALREADY EXIST and say nothing about whether this caller may save a
    // new one - the form behind the button is Lead Capture, gated on `don.lead-capture.save`.
    // The queue endpoint now answers 'Create' for exactly that permission.
    const has = (verb: string) => permitted.includes(verb);
    return {
      view: has('Open') || has('Filter'),
      create: has('Create'),
      assign: has('Assign'),
      communicate: has('Contact'),
      schedule: has('Contact'),
      export: has('Filter') || has('Open'),
    };
  }

  private distinct(values: readonly string[]): readonly string[] {
    return [...new Set(values)].sort();
  }

  // ===========================================================================================
  // Derived view state
  // ===========================================================================================

  protected readonly activeFilterChips = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedView() !== 'All Leads') {
      chips.push({ key: 'view', label: `View: ${this.savedView()}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.stageFilter()) {
      chips.push({
        key: 'stage',
        label: `Stage: ${this.filterOptions().stages.find((option) => option.value === this.stageFilter())?.label ?? this.stageFilter()}`,
      });
    }
    if (this.temperatureFilter()) {
      chips.push({
        key: 'temperature',
        label: `Temperature: ${this.filterOptions().temperatures.find((option) => option.value === this.temperatureFilter())?.label ?? this.temperatureFilter()}`,
      });
    }
    if (this.potentialFilter()) {
      chips.push({
        key: 'potential',
        label: `Potential: ${this.filterOptions().potentials.find((option) => option.value === this.potentialFilter())?.label ?? this.potentialFilter()}`,
      });
    }
    if (this.sourceFilter()) {
      chips.push({ key: 'source', label: `Source: ${this.sourceFilter()}` });
    }
    if (this.ownerFilter()) {
      chips.push({
        key: 'owner',
        label: `Owner: ${this.ownerOptions().find((owner) => owner.value === this.ownerFilter())?.label ?? this.ownerFilter()}`,
      });
    }
    return chips;
  });

  /**
   * The rows on screen.
   *
   * SOURCE IS THE ONE FILTER STILL APPLIED HERE, because the API has no source parameter. It is
   * a narrowing of the page rather than of the set, and the chip says so.
   */
  protected readonly filteredLeads = computed(() => {
    const source = this.sourceFilter();
    const rows = this.leads();
    return source ? rows.filter((lead) => lead.source === source) : rows;
  });

  protected readonly hasResults = computed(() => this.filteredLeads().length > 0);
  protected readonly selectedRows = computed(() =>
    this.filteredLeads().filter((lead) => this.selectedIds().has(lead.id)),
  );
  protected readonly selectionCount = computed(() => this.selectedRows().length);
  protected readonly allSelected = computed(() => {
    const ids = this.filteredLeads().map((l) => l.id);
    return ids.length > 0 && ids.every((id) => this.selectedIds().has(id));
  });

  protected readonly statusPill = computed(() => {
    switch (this.uiState()) {
      case 'loading':
        return { label: 'Loading', cls: 'lq-badge-muted' };
      case 'error':
        return { label: 'Unavailable', cls: 'lq-badge-danger' };
      case 'success':
        return { label: 'Updated', cls: 'lq-badge-good' };
      default:
        return { label: 'Live queue', cls: 'lq-badge-good' };
    }
  });

  protected readonly healthSummary = computed(() => {
    const leads = this.filteredLeads();
    return {
      cold: leads.filter((l) => l.temperature === 'Cold').length,
      warm: leads.filter((l) => l.temperature === 'Warm').length,
      hot: leads.filter((l) => l.temperature === 'Hot').length,
      highPotential: leads.filter((l) => l.donationPotential === 'High').length,
      converted: leads.filter((l) => l.converted).length,
    };
  });

  // ===========================================================================================
  // Filter controls. Each one reloads, because each one is a server filter.
  // ===========================================================================================

  protected selectSavedView(view: string): void {
    this.savedView.set(view);
    this.clearAdvancedFilters();
    this.load();
  }

  protected selectPipelineStage(key: string): void {
    const stage = this.pipeline().find((p) => p.key === key);
    if (!stage) {
      return;
    }
    this.stageFilter.set(stage.key);
    this.savedView.set('All Leads');
    this.load();
  }

  protected removeFilterChip(key: string): void {
    switch (key) {
      case 'view':
        this.savedView.set('All Leads');
        break;
      case 'search':
        this.searchTerm.set('');
        break;
      case 'stage':
        this.stageFilter.set('');
        break;
      case 'temperature':
        this.temperatureFilter.set('');
        break;
      case 'potential':
        this.potentialFilter.set('');
        break;
      case 'source':
        this.sourceFilter.set('');
        break;
      case 'owner':
        this.ownerFilter.set('');
        break;
    }
    this.load();
  }

  protected clearAdvancedFilters(): void {
    this.optionSearch.set({});
    this.stageFilter.set('');
    this.temperatureFilter.set('');
    this.potentialFilter.set('');
    this.sourceFilter.set('');
    this.ownerFilter.set('');
  }

  protected clearAllFilters(): void {
    this.savedView.set('All Leads');
    this.searchTerm.set('');
    this.clearAdvancedFilters();
    this.load();
  }

  protected applyFilters(): void {
    this.showFilters.set(false);
    this.load();
  }

  protected toggleFilters(): void {
    this.showFilters.update((v) => !v);
  }

  // ===========================================================================================
  // Selection
  // ===========================================================================================

  protected toggleSelectAll(): void {
    const ids = this.filteredLeads().map((l) => l.id);
    this.selectedIds.set(this.allSelected() ? new Set() : new Set(ids));
  }

  protected toggleSelect(id: string): void {
    const next = new Set(this.selectedIds());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this.selectedIds.set(next);
  }

  protected isSelected(id: string): boolean {
    return this.selectedIds().has(id);
  }

  // ===========================================================================================
  // Row actions - the document's Action-to-Destination matrix
  // ===========================================================================================

  protected previewLead(lead: LeadItem): void {
    this.selectedLead.set(lead);
  }

  protected isPreviewed(lead: LeadItem): boolean {
    return this.selectedLead()?.id === lead.id;
  }

  protected closePreview(): void {
    this.selectedLead.set(null);
  }

  /** Assign - "available only for an unassigned lead; opens the Assignment Board". */
  protected onAssign(lead: LeadItem): void {
    this.router.navigate(['/app/fundraising/relationships/assignment-board'], {
      queryParams: { leadId: lead.id },
    });
  }

  /** Communicate - "opens the selected lead's Communication Timeline". */
  protected onCommunicate(lead: LeadItem): void {
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], {
      queryParams: { leadId: lead.id },
    });
  }

  /** Schedule Follow-Up - "opens the Follow-Up Planner". */
  protected onSchedule(lead: LeadItem): void {
    this.router.navigate(['/app/don/follow-up-planner'], {
      queryParams: { leadId: lead.id, mode: 'create' },
    });
  }

  /** Open Timeline - the same destination as Communicate, per the document. */
  protected onTimeline(lead: LeadItem): void {
    this.onCommunicate(lead);
  }

  protected bulkAssign(): void {
    if (this.selectionCount() === 0) {
      return;
    }
    this.router.navigate(['/app/fundraising/relationships/assignment-board'], {
      queryParams: {
        leadIds: this.selectedRows()
          .map((lead) => lead.id)
          .join(','),
      },
    });
  }

  protected createLead(): void {
    this.router.navigate(['/app/fundraising/relationships/lead-capture']);
  }

  // ===========================================================================================
  // Export
  // ===========================================================================================

  /**
   * Export - the document's shared function.
   *
   * IT EXPORTS WHAT IS ON SCREEN. The server has its own donor export endpoint for a full
   * extract; this is the filtered view the person is looking at, which is what the control
   * beside the grid means.
   */
  protected exportLeads(): void {
    this.exportRows(this.filteredLeads());
  }

  protected bulkExport(): void {
    if (this.selectionCount() === 0) {
      return;
    }
    this.exportRows(this.filteredLeads().filter((lead) => this.selectedIds().has(lead.id)));
  }

  private exportRows(rows: readonly LeadItem[]): void {
    const headers = [
      'Lead ID',
      'Name',
      'Mobile',
      'Email',
      'Source',
      'Campaign',
      'Stage',
      'Temperature',
      'Donation Potential',
      'Owner',
      'Next Follow-Up',
    ];
    const lines = rows.map((lead) =>
      [
        lead.reference,
        lead.name,
        lead.mobile,
        lead.email,
        lead.source,
        lead.campaign,
        lead.stage,
        lead.temperature,
        lead.donationPotential,
        lead.owner,
        lead.nextFollowUp,
      ]
        .map((value) => `"${String(value).replace(/"/g, '""')}"`)
        .join(','),
    );

    const blob = new Blob([[headers.join(','), ...lines].join('\n')], {
      type: 'text/csv;charset=utf-8;',
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'lead-work-queue.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  // ===========================================================================================
  // Bulk qualification - REMOVED
  //
  // The bulk bar used to carry "Update Temperature" and "Update Donation Potential", and neither
  // appears anywhere in the Donors and Leads workflow document: its Lead Work Queue offers
  // Preview, Communicate and Assign, and its only bulk operation is Bulk Assign on the Assignment
  // Board. Both buttons also had no endpoint behind them - `QualifyLeadRequest` carries
  // qualification notes and a next action, not a temperature - so they set a local `success`
  // banner and changed nothing. Selection now serves Assign and Export, which the document does
  // describe.
  // ===========================================================================================

  // ===========================================================================================
  // Presentation helpers
  // ===========================================================================================

  protected refresh(): void {
    this.load();
  }

  protected dismissBanner(): void {
    this.uiState.set(this.leads().length === 0 ? 'empty' : 'ready');
  }

  protected async copyValue(label: string, value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.copiedField.set(label);
      setTimeout(() => {
        if (this.copiedField() === label) {
          this.copiedField.set(null);
        }
      }, 1500);
    } catch {
      this.copiedField.set(null);
    }
  }

  protected stageClass(stage: string): string {
    switch (stage) {
      case 'New':
        return 'lq-badge-blue';
      case 'Assigned':
        return 'lq-badge-warn';
      case 'Nurture':
        return 'lq-badge-plum';
      case 'Contacted':
        return 'lq-badge-warn';
      case 'Engaged':
        return 'lq-badge-good';
      case 'Dormant':
        return 'lq-badge-muted';
      case 'Lost':
        return 'lq-badge-danger';
      case 'Converted':
        return 'lq-badge-good';
      default:
        return 'lq-badge-muted';
    }
  }

  protected temperatureClass(temp: string): string {
    switch (temp) {
      case 'Hot':
        return 'lq-badge-danger';
      case 'Warm':
        return 'lq-badge-warn';
      case 'Cold':
        return 'lq-badge-muted';
      default:
        return 'lq-badge-muted';
    }
  }

  protected potentialClass(pot: string): string {
    switch (pot) {
      case 'High':
        return 'lq-badge-good';
      case 'Medium':
        return 'lq-badge-warn';
      case 'Low':
        return 'lq-badge-muted';
      default:
        return 'lq-badge-muted';
    }
  }

  protected healthClass(score: number): string {
    if (score >= 80) return 'lq-health-high';
    if (score >= 55) return 'lq-health-mid';
    return 'lq-health-low';
  }

  protected getInitials(name: string): string {
    return name
      .split(' ')
      .map((p) => p.charAt(0))
      .slice(0, 2)
      .join('')
      .toUpperCase();
  }

  /** Deterministic avatar colour per name, so a lead always looks the same in the grid. */
  protected getAvatarColor(name: string): string {
    if (!name) {
      return this.avatarPalette[0];
    }
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    return this.avatarPalette[Math.abs(hash) % this.avatarPalette.length];
  }

  protected displayValue(value: string | null | undefined): string {
    return value && value.trim().length > 0 ? value : '-';
  }

  private formatDate(value: string | null): string {
    if (!value) {
      return '';
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
      ? ''
      : parsed.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private formatDateTime(value: string | null): string {
    if (!value) {
      return '';
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
      ? ''
      : parsed.toLocaleString('en-GB', {
          day: '2-digit',
          month: 'short',
          year: 'numeric',
          hour: '2-digit',
          minute: '2-digit',
        });
  }
}

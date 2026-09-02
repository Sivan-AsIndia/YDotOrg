import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import { ConfirmDialogConfig, UiState } from '../../../../Shared/models/donors-leads.model';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  AssignmentBoardResponse,
  AssignmentBoardRow,
  AssignmentHistoryItem,
  DonLookupItem,
  OwnerWorkload,
} from '../../../../Shared/models/donor-contract.model';

interface OwnerOption {
  readonly reference: string;
  readonly label: string;
  readonly context: string;
  readonly initials: string;
}

/** One board row, in the shape the template binds to. */
interface LeadRow {
  readonly leadId: string;
  readonly leadReference: string;
  readonly leadPreview: string;
  readonly campaign: string;
  readonly team: string;
  readonly language: string;
  readonly workloadBand: string;
  readonly slaState: string;
  readonly currentOwner: string;
  readonly currentOwnerUserId: string | null;
  readonly suggestedOwner: string;
  readonly suggestedOwnerUserId: string | null;
  readonly suggestionRationale: string;
  readonly openWorkCount: number;
  readonly nextActionDue: string;
  readonly status: string;
  readonly version: number;
}

type AssignMode = 'assign' | 'reassign' | 'bulkRoute';
type ScheduleMode = 'immediate' | 'scheduled';

interface AssignmentDraft {
  readonly mode: AssignMode;
  readonly rows: LeadRow[];
  readonly newOwnerRef: string | null;
  readonly scheduleMode: ScheduleMode;
  readonly effectiveTimeInput: string;
  readonly effectiveTimeError: string;
}

interface HistoryEntry {
  readonly event: string;
  readonly owner: string;
  readonly reason: string;
  readonly effectiveTime: string;
  readonly actor: string;
  readonly at: string;
}

interface BulkResultDetail {
  readonly leadReference: string;
  readonly leadPreview: string;
  readonly status: 'success' | 'ineligible';
  readonly note: string;
}

interface BulkResult {
  readonly selected: number;
  readonly eligible: number;
  readonly ineligible: number;
  readonly success: number;
  readonly newOwner: string;
  readonly details: BulkResultDetail[];
}

interface AssignResult {
  readonly leadReference: string;
  readonly leadPreview: string;
  readonly owner: string;
  readonly assignmentState: string;
  readonly effectiveTime: string;
  readonly nextAction: string;
}

/**
 * SCR-DON-006 - Assignment Board.
 *
 * THE DOCUMENT'S RULES, AND ALL OF THEM ARE THE SERVER'S NOW. "Unassigned lead - Preview, Inspect
 * History and Assign. Assigned lead - Preview, Inspect History and Reassign. Bulk Assign allows
 * multiple leads to be selected at the same time and assigned or reassigned to an owner selected
 * from the drop-down."
 *
 * WHAT THIS REPLACES, AND WHY EACH PIECE MATTERED.
 *
 *   - THE ROWS CAME FROM A JSON FILE merged with an in-memory `WorkflowStateService` array, so
 *     the board showed the same leads to every organisation and forgot every assignment on
 *     refresh. `onConfirm` ended in `window.setTimeout(..., 600)` with the comment "Simulated
 *     processing delay ... No backend call is invented here" - the assignment was never saved.
 *
 *   - THE OWNER LIST WAS DERIVED BY GUESSWORK. `matchesCampaignByStem` compared a lead's campaign
 *     name to a campaign record's name word by word, accepting a match when each word was a
 *     prefix of the other, so "Clean Water 2026" could resolve to "Clean Water Initiative" - or to
 *     the wrong campaign entirely. The board now uses the owners the API returns for the leads it
 *     returned, which needs no matching at all.
 *
 *   - THE HISTORY WAS A BROWSER FIELD. `sessionHistory` was a signal keyed by reference, so
 *     "Inspect History" showed only what this tab had done since it was opened - an audit trail
 *     that forgot everything on refresh and knew nothing anybody else had done.
 *
 * OWNERSHIP IS A CONTESTED WRITE, which is why every assign sends `expectedVersion`. Two
 * fundraisers claiming the same lead within a second of each other is the ordinary case on a
 * board like this, and the second one is refused rather than silently overwriting the first.
 */
@Component({
  selector: 'app-assignment-board',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './assignment-board.html',
  styleUrl: './assignment-board.css',
})
export class AssignmentBoardComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);

  protected readonly timezoneLabel = 'Asia/Kolkata (IST)';

  protected readonly uiState = signal<UiState>('loading');
  protected readonly errorMessage = signal('');

  // ===========================================================================================
  // Screen chrome and lookups, all from the API
  // ===========================================================================================

  protected readonly screen = signal({
    viewId: 'SCR-DON-006',
    title: 'Assignment board',
    route: '/app/fundraising/relationships/assignment-board',
    purpose: 'Balance ownership by team, language, workload and SLA.',
    scope: '',
    lastRefresh: '',
    timezone: 'Asia/Kolkata (IST)',
  });

  protected readonly filters = signal<{
    campaigns: readonly string[];
    teams: readonly string[];
    languages: readonly string[];
    workloadBands: readonly string[];
    slaStates: readonly string[];
  }>({ campaigns: ['All'], teams: ['All'], languages: ['All'], workloadBands: ['All'], slaStates: ['All'] });

  protected readonly savedFilters = signal<readonly string[]>(['All leads']);

  /**
   * What the caller may do, as the server listed it.
   *
   * THE THREE-ROLE MODEL LIVES HERE AND NOWHERE ELSE ON THIS SCREEN. An APPROVER holds no
   * `assign` code, so no Assign or Bulk Assign button is drawn for them; TENANT_ADMIN and
   * INITIATOR both hold it. Nothing in this file names a role.
   */
  protected readonly permissions = signal<Record<string, boolean>>({
    view: false,
    assign: false,
    reassign: false,
    bulkRoute: false,
  });

  protected readonly bulkRouteMaximumItems = signal(50);

  // ===========================================================================================
  // Filter state - every change re-queries
  // ===========================================================================================

  protected readonly savedFilter = signal('All leads');
  protected readonly campaignFilter = signal('All');
  protected readonly teamFilter = signal('All');
  protected readonly languageFilter = signal('All');
  protected readonly workloadFilter = signal('All');
  protected readonly slaFilter = signal('All');
  protected readonly filtersOpen = signal(false);

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  private campaignLookup: readonly DonLookupItem[] = [];
  private teamLookup: readonly DonLookupItem[] = [];

  // ===========================================================================================
  // Rows and owners
  // ===========================================================================================

  protected readonly rows = signal<readonly LeadRow[]>([]);
  protected readonly owners = signal<readonly OwnerWorkload[]>([]);
  protected readonly totalCountFromServer = signal(0);

  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly selectedRow = signal<LeadRow | null>(null);
  protected readonly activeActionId = signal('');
  protected readonly previewRow = signal<LeadRow | null>(null);

  private pendingLeadId: string | null = null;
  private pendingLeadIds: string[] = [];

  constructor() {
    const params = this.route.snapshot.queryParamMap;
    this.pendingLeadId = params.get('leadId');
    this.pendingLeadIds = (params.get('leadIds') ?? '').split(',').map((v) => v.trim()).filter(Boolean);

    this.load();
  }

  // ===========================================================================================
  // Loading
  // ===========================================================================================

  private load(): void {
    this.uiState.set('loading');
    this.errorMessage.set('');

    this.api.getAssignmentBoard(this.buildFilter()).subscribe({
      next: (response) => this.applyResponse(response),
      error: (error: unknown) => {
        this.errorMessage.set(apiErrorMessage(error));
        this.uiState.set('dependency-failure');
        this.toast.show('Assignment board unavailable', this.errorMessage(), 'error');
      },
    });
  }

  private buildFilter(): Record<string, unknown> {
    const filter: Record<string, unknown> = { page: this.currentPage(), pageSize: this.pageSize() };

    // THE ID, NOT THE LABEL. The dropdowns show names; the API filters on the lookup value that
    // came with each name, so nothing here has to match one string against another.
    const campaign = this.campaignLookup.find((c) => c.label === this.campaignFilter());
    if (campaign) {
      filter['campaignId'] = campaign.value;
    }

    const team = this.teamLookup.find((t) => t.label === this.teamFilter());
    if (team) {
      filter['teamCode'] = team.value;
    }

    if (this.languageFilter() !== 'All') {
      filter['preferredLanguage'] = this.languageFilter();
    }
    if (this.workloadFilter() !== 'All') {
      filter['workloadBand'] = this.workloadFilter();
    }
    if (this.slaFilter() !== 'All') {
      filter['slaState'] = this.slaFilter();
    }

    // The board's own saved view. Unassigned is the one the document names as the entry point.
    if (this.savedFilter() === 'Unassigned only') {
      filter['assignmentState'] = 'Unassigned';
    }

    return filter;
  }

  private applyResponse(response: AssignmentBoardResponse): void {
    this.rows.set(response.rows.items.map((row) => this.toRow(row, response.owners)));
    this.totalCountFromServer.set(response.rows.totalCount);
    this.owners.set(response.owners);
    this.bulkRouteMaximumItems.set(response.bulkRouteMaximumItems);

    this.campaignLookup = response.campaignOptions;
    this.teamLookup = response.teamOptions;

    this.filters.set({
      campaigns: ['All', ...response.campaignOptions.map((o) => o.label)],
      teams: ['All', ...response.teamOptions.map((o) => o.label)],
      languages: ['All', ...response.languageOptions.map((o) => o.label)],
      workloadBands: ['All', ...response.workloadBandOptions.map((o) => o.label)],
      slaStates: ['All', ...response.slaStateOptions.map((o) => o.label)],
    });
    this.savedFilters.set(['All leads', 'Unassigned only']);

    const permitted = response.permittedActions ?? [];
    // VERBS, AS THE API ANSWERS THEM: ['Assign','Inspect history','Reassign','Bulk route'].
    this.permissions.set({
      view: permitted.includes('Inspect history') || permitted.length > 0,
      assign: permitted.includes('Assign'),
      reassign: permitted.includes('Reassign'),
      bulkRoute: permitted.includes('Bulk route'),
    });

    this.screen.update((current) => ({
      ...current,
      scope: response.activeScope,
      lastRefresh: this.nowLabel(),
    }));

    this.uiState.set(this.rows().length === 0 ? 'empty' : 'ready');

    // Arriving from the Lead Queue's Assign action, or from its bulk selection.
    if (this.pendingLeadIds.length > 0) {
      const wanted = new Set(this.pendingLeadIds);
      const matches = this.rows().filter((r) => wanted.has(r.leadId) || wanted.has(r.leadReference));
      if (matches.length > 0) {
        this.selectionMode.set(true);
        this.selectedLeadRefs.set(new Set(matches.map((m) => m.leadReference)));
      }
      this.pendingLeadIds = [];
    }

    const requested = this.pendingLeadId
      ? this.rows().find((r) => r.leadId === this.pendingLeadId || r.leadReference === this.pendingLeadId)
      : null;
    this.pendingLeadId = null;
    this.previewRow.set(requested ?? this.previewRow() ?? this.rows()[0] ?? null);
  }

  private toRow(row: AssignmentBoardRow, owners: readonly OwnerWorkload[]): LeadRow {
    const owner = owners.find((o) => o.userId === row.currentOwnerUserId);
    return {
      leadId: row.leadId,
      leadReference: row.leadReference,
      leadPreview: row.leadPreview,
      campaign: row.campaignName ?? '—',
      team: row.teamCode ?? '—',
      language: row.preferredLanguage,

      // THE OWNER'S BAND, NOT THE LEAD'S. Workload is a property of the person holding the work,
      // which is exactly what makes it useful when deciding who to hand the next lead to.
      workloadBand: owner?.workloadBand ?? '—',
      slaState: row.slaState,
      currentOwner: row.currentOwnerName ?? 'Unassigned',
      currentOwnerUserId: row.currentOwnerUserId,
      suggestedOwner: row.suggestedOwnerName ?? '',
      suggestedOwnerUserId: row.suggestedOwnerUserId,
      suggestionRationale: row.suggestionRationale ?? '',
      openWorkCount: row.currentOwnerOpenWorkCount,
      nextActionDue: this.formatDate(row.nextActionDueUtc),
      status: row.status,
      version: row.version,
    };
  }

  // ===========================================================================================
  // Derived view state
  // ===========================================================================================

  protected readonly allRows = computed(() => this.rows());
  protected readonly filteredRows = computed(() => this.rows());
  protected readonly paginatedRows = computed(() => this.rows());

  protected readonly totalCount = computed(() => this.totalCountFromServer());
  protected readonly unassignedCount = computed(
    () => this.rows().filter((r) => r.currentOwner === 'Unassigned').length,
  );
  protected readonly dueTodayCount = computed(
    () => this.rows().filter((r) => r.slaState === 'Due today').length,
  );
  protected readonly overdueCount = computed(
    () => this.rows().filter((r) => r.slaState === 'Overdue').length,
  );

  // ----- Pagination, server-side -----
  protected readonly pageSizes = [10, 25, 50, 100];
  protected readonly pageSize = signal(10);
  protected readonly currentPage = signal(1);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCountFromServer() / this.pageSize())),
  );

  protected readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    const pages: number[] = [];
    for (let i = Math.max(1, current - 2); i <= Math.min(total, current + 2); i++) {
      pages.push(i);
    }
    return pages;
  });

  protected readonly pagedStart = computed(() =>
    this.totalCountFromServer() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize() + 1,
  );
  protected readonly pagedEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize(), this.totalCountFromServer()),
  );

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) {
      return;
    }
    this.currentPage.set(page);
    this.load();
  }

  protected onPageSizeChange(size: number): void {
    this.pageSize.set(Number(size));
    this.currentPage.set(1);
    this.load();
  }

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedFilter() !== 'All leads') chips.push({ key: 'saved', label: `View: ${this.savedFilter()}` });
    if (this.campaignFilter() !== 'All') chips.push({ key: 'campaign', label: `Campaign: ${this.campaignFilter()}` });
    if (this.teamFilter() !== 'All') chips.push({ key: 'team', label: `Team: ${this.teamFilter()}` });
    if (this.languageFilter() !== 'All') chips.push({ key: 'language', label: `Language: ${this.languageFilter()}` });
    if (this.workloadFilter() !== 'All') chips.push({ key: 'workload', label: `Workload: ${this.workloadFilter()}` });
    if (this.slaFilter() !== 'All') chips.push({ key: 'sla', label: `SLA: ${this.slaFilter()}` });
    return chips;
  });

  protected removeFilterChip(key: string): void {
    if (key === 'saved') this.savedFilter.set('All leads');
    if (key === 'campaign') this.campaignFilter.set('All');
    if (key === 'team') this.teamFilter.set('All');
    if (key === 'language') this.languageFilter.set('All');
    if (key === 'workload') this.workloadFilter.set('All');
    if (key === 'sla') this.slaFilter.set('All');
    this.currentPage.set(1);
    this.load();
  }

  protected clearFilters(): void {
    this.savedFilter.set('All leads');
    this.campaignFilter.set('All');
    this.teamFilter.set('All');
    this.languageFilter.set('All');
    this.workloadFilter.set('All');
    this.slaFilter.set('All');
    this.currentPage.set(1);
    this.load();
  }

  /** Any dropdown change re-queries; the template binds its selects to this. */
  protected onFilterChanged(): void {
    this.currentPage.set(1);
    this.load();
  }

  // ===========================================================================================
  // Preview
  // ===========================================================================================

  protected selectPreview(row: LeadRow): void {
    if (this.selectionMode()) {
      this.toggleRowSelection(row.leadReference);
      return;
    }
    this.previewRow.set(row);
    this.draft.set(null);
    this.historyPanelRow.set(null);
  }

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set(this.rows().length === 0 ? 'empty' : 'ready');
  }

  // ===========================================================================================
  // Presentation helpers
  // ===========================================================================================

  protected getInitials(name: string): string {
    return name.split(' ').map((p) => p.charAt(0)).join('').slice(0, 2).toUpperCase();
  }

  protected avatarTone(index: number): string {
    return `avatar-tone-${index % 5}`;
  }

  protected slaClass(sla: string): string {
    if (sla === 'Overdue') return 'ab-badge-danger';
    if (sla === 'Due today') return 'ab-badge-warn';
    return 'ab-badge-good';
  }

  protected workloadClass(band: string): string {
    if (band === 'High') return 'ab-badge-danger';
    if (band === 'Medium') return 'ab-badge-warn';
    return 'ab-badge-good';
  }

  protected trackByLead(_index: number, row: LeadRow): string {
    return row.leadReference;
  }

  /** The document: Assign for an unassigned lead, Reassign for an assigned one. */
  protected primaryActionId(row: LeadRow): 'assign' | 'reassign' {
    return row.currentOwner === 'Unassigned' ? 'assign' : 'reassign';
  }

  protected primaryActionLabel(row: LeadRow): string {
    return this.primaryActionId(row) === 'assign' ? 'Assign' : 'Reassign';
  }

  protected actionLabel(mode: AssignMode): string {
    if (mode === 'assign') return 'Assign';
    if (mode === 'reassign') return 'Reassign';
    return 'Bulk assign';
  }

  // ===========================================================================================
  // Bulk selection
  // ===========================================================================================

  protected readonly selectionMode = signal(false);
  protected readonly selectedLeadRefs = signal<Set<string>>(new Set());

  protected toggleSelectionMode(): void {
    if (!this.permissions()['bulkRoute']) {
      return;
    }
    this.selectionMode.update((v) => !v);
    if (!this.selectionMode()) {
      this.selectedLeadRefs.set(new Set());
    } else {
      this.draft.set(null);
      this.historyPanelRow.set(null);
    }
  }

  protected exitSelectionMode(): void {
    this.selectionMode.set(false);
    this.selectedLeadRefs.set(new Set());
  }

  protected isRowSelected(ref: string): boolean {
    return this.selectedLeadRefs().has(ref);
  }

  protected toggleRowSelection(ref: string): void {
    const next = new Set(this.selectedLeadRefs());
    if (next.has(ref)) {
      next.delete(ref);
    } else {
      next.add(ref);
    }
    this.selectedLeadRefs.set(next);
  }

  protected readonly selectedRowsForBulk = computed(() =>
    this.rows().filter((r) => this.selectedLeadRefs().has(r.leadReference)),
  );

  protected startBulkAssignment(): void {
    const rows = this.selectedRowsForBulk();
    if (rows.length === 0) {
      return;
    }

    // THE SERVER'S CAP, SHOWN BEFORE THE ATTEMPT. Sending more than it accepts would fail the
    // whole batch after the person had already chosen an owner and typed a reason.
    if (rows.length > this.bulkRouteMaximumItems()) {
      this.toast.show(
        'Too many leads selected',
        `Bulk assign takes at most ${this.bulkRouteMaximumItems()} leads at a time. ${rows.length} are selected.`,
        'warning',
      );
      return;
    }

    this.beginAssignment('bulkRoute', rows);
  }

  // ===========================================================================================
  // The assignment drawer
  // ===========================================================================================

  protected readonly draft = signal<AssignmentDraft | null>(null);
  protected readonly ownerSearchTerm = signal('');
  protected readonly ownerPickerOpen = signal(false);
  protected readonly processing = signal(false);
  protected readonly lastResult = signal<AssignResult | null>(null);
  protected readonly bulkResult = signal<BulkResult | null>(null);

  protected readonly minEffectiveTime = computed(() => this.toLocalDatetimeInput(new Date()));

  /**
   * The owners a lead may be handed to.
   *
   * THE SERVER'S LIST, WITH THE WORKLOAD IT REPORTED. The old version tried to derive it by
   * matching the lead's campaign name against campaign records word by word, and then looked the
   * result up in a constant exported from the campaign register screen. Both steps could be wrong
   * and neither was checked - a lead could be offered to somebody with no claim on its campaign.
   */
  protected readonly campaignScopedOwnerOptions = computed<readonly OwnerOption[]>(() =>
    this.owners().map((owner) => ({
      reference: owner.userId,
      label: owner.name,
      context: `${owner.teamCode ?? 'No team'} · ${owner.openWorkCount} open · ${owner.workloadBand}`,
      initials: this.getInitials(owner.name),
    })),
  );

  protected readonly hasCampaignOwners = computed(() => this.campaignScopedOwnerOptions().length > 0);

  protected readonly filteredOwnerOptions = computed(() => {
    const term = this.ownerSearchTerm().trim().toLowerCase();
    const pool = this.campaignScopedOwnerOptions();
    if (!term) {
      return pool;
    }
    return pool.filter(
      (o) =>
        o.label.toLowerCase().includes(term) ||
        o.reference.toLowerCase().includes(term) ||
        o.context.toLowerCase().includes(term),
    );
  });

  protected readonly selectedOwner = computed<OwnerOption | null>(() => {
    const draft = this.draft();
    if (!draft?.newOwnerRef) {
      return null;
    }
    return this.campaignScopedOwnerOptions().find((o) => o.reference === draft.newOwnerRef) ?? null;
  });

  /**
   * Which of a bulk selection would actually move.
   *
   * A LEAD ALREADY OWNED BY THE CHOSEN PERSON IS SKIPPED, and named as skipped rather than
   * quietly dropped - the document's bulk flow ends in a completion summary, and a summary that
   * says "12 routed" when 3 were no-ops is not a summary.
   */
  protected readonly eligibleRows = computed<LeadRow[]>(() => {
    const draft = this.draft();
    if (!draft) return [];
    if (draft.mode !== 'bulkRoute') return draft.rows;
    const owner = this.selectedOwner();
    if (!owner) return draft.rows;
    return draft.rows.filter((r) => r.currentOwnerUserId !== owner.reference);
  });

  protected readonly ineligibleRows = computed<LeadRow[]>(() => {
    const draft = this.draft();
    const owner = this.selectedOwner();
    if (!draft || draft.mode !== 'bulkRoute' || !owner) return [];
    return draft.rows.filter((r) => r.currentOwnerUserId === owner.reference);
  });

  protected readonly canContinue = computed(() => {
    const draft = this.draft();
    if (!draft || this.processing() || !draft.newOwnerRef) {
      return false;
    }
    if (draft.scheduleMode === 'scheduled' && (!draft.effectiveTimeInput || draft.effectiveTimeError)) {
      return false;
    }
    if (draft.mode === 'bulkRoute' && this.eligibleRows().length === 0) {
      return false;
    }
    return true;
  });

  protected draftTitle(draft: AssignmentDraft): string {
    if (draft.mode === 'bulkRoute') {
      return `Bulk assign · ${draft.rows.length} record${draft.rows.length === 1 ? '' : 's'}`;
    }
    return draft.mode === 'assign' ? 'Assign owner' : 'Reassign owner';
  }

  protected beginAssignment(mode: AssignMode, rows: LeadRow[]): void {
    if (rows.length === 0 || !this.permissions()[mode]) {
      return;
    }

    // The server's suggestion, preselected. It travelled with the row, so it needs no lookup.
    const suggestedRef = mode !== 'bulkRoute' ? rows[0].suggestedOwnerUserId : null;

    this.draft.set({
      mode,
      rows,
      newOwnerRef: suggestedRef,
      scheduleMode: 'immediate',
      effectiveTimeInput: '',
      effectiveTimeError: '',
    });
    this.ownerSearchTerm.set('');
    this.ownerPickerOpen.set(false);
    this.bulkResult.set(null);
    this.lastResult.set(null);
    this.confirmConfig.set(null);
    this.historyPanelRow.set(null);
  }

  protected handlePrimaryCta(): void {
    const row = this.previewRow();
    if (row) {
      this.beginAssignment(this.primaryActionId(row), [row]);
    }
  }

  protected cancelDraft(): void {
    this.draft.set(null);
    this.ownerSearchTerm.set('');
    this.ownerPickerOpen.set(false);
  }

  protected toggleOwnerPicker(): void {
    this.ownerPickerOpen.update((v) => !v);
  }

  protected selectOwner(ref: string): void {
    const draft = this.draft();
    if (!draft) return;
    this.draft.set({ ...draft, newOwnerRef: ref });
    this.ownerPickerOpen.set(false);
    this.ownerSearchTerm.set('');
  }

  protected useSuggestedOwner(): void {
    const draft = this.draft();
    if (!draft || draft.mode === 'bulkRoute') return;
    const ref = draft.rows[0].suggestedOwnerUserId;
    if (ref) {
      this.draft.set({ ...draft, newOwnerRef: ref });
    }
  }

  protected setScheduleMode(mode: ScheduleMode): void {
    const draft = this.draft();
    if (!draft) return;
    this.draft.set({
      ...draft,
      scheduleMode: mode,
      effectiveTimeInput: mode === 'immediate' ? '' : draft.effectiveTimeInput,
      effectiveTimeError: '',
    });
  }

  protected onEffectiveTimeInput(value: string): void {
    const draft = this.draft();
    if (!draft) return;
    let error = '';
    if (value) {
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) {
        error = 'Enter a valid date and time.';
      } else if (date.getTime() < Date.now() - 60000) {
        error = 'Effective time cannot be in the past.';
      }
    }
    this.draft.set({ ...draft, effectiveTimeInput: value, effectiveTimeError: error });
  }

  protected continueToConfirm(): void {
    const draft = this.draft();
    const owner = this.selectedOwner();
    if (!draft || !owner || !this.canContinue()) {
      return;
    }

    const isBulk = draft.mode === 'bulkRoute';
    const effectiveDisplay = this.formatEffectiveTimeDisplay(draft);

    const beforeAfter = isBulk
      ? [
          { label: 'Selected', before: `${draft.rows.length}`, after: `${this.eligibleRows().length} eligible` },
          { label: 'New owner', before: '—', after: owner.label },
        ]
      : [
          { label: 'Owner', before: draft.rows[0].currentOwner, after: owner.label },
          { label: 'Effective time', before: '—', after: effectiveDisplay },
        ];

    this.selectedRow.set(draft.rows[0]);
    this.activeActionId.set(draft.mode);
    this.confirmConfig.set({
      title: `Confirm ${this.actionLabel(draft.mode)}`,
      message: isBulk
        ? `This will update ownership for ${this.eligibleRows().length} of ${draft.rows.length} selected record(s). ${this.ineligibleRows().length} already belong to ${owner.label} and will be skipped.`
        : `Ownership moves to ${owner.label}. The reason is recorded on the lead's assignment history.`,
      confirmLabel: this.actionLabel(draft.mode),
      cancelLabel: 'Cancel',
      tone: 'primary',

      // THE API REQUIRES 10 TO 2000 CHARACTERS. Matching the bounds here means the refusal is a
      // sentence under the box rather than a 400 after the button.
      requireReason: true,
      reasonLabel: 'Assignment reason',
      reasonMin: 10,
      reasonMax: 2000,
      typedConfirm: false,
      affectedRecord: isBulk
        ? `${this.eligibleRows().length} eligible record(s)`
        : `${draft.rows[0].leadReference} · ${draft.rows[0].leadPreview}`,
      effectiveTime: effectiveDisplay,
      beforeAfter,
    });
  }

  /**
   * Commits the assignment.
   *
   * THE VERSION GOES WITH IT. Ownership is the most contested field on a lead - two fundraisers
   * claiming the same one within a second of each other is ordinary on this board - so the second
   * write is refused with a conflict rather than silently overwriting the first.
   */
  protected onConfirm(reason: string): void {
    const draft = this.draft();
    const owner = this.selectedOwner();
    if (!draft || !owner || this.processing()) {
      this.confirmConfig.set(null);
      return;
    }

    this.processing.set(true);
    const effectiveDisplay = this.formatEffectiveTimeDisplay(draft);
    const effectiveAtUtc =
      draft.scheduleMode === 'scheduled' && draft.effectiveTimeInput
        ? new Date(draft.effectiveTimeInput).toISOString()
        : null;

    if (draft.mode === 'bulkRoute') {
      this.commitBulk(draft, owner, reason, effectiveAtUtc);
      return;
    }

    const row = draft.rows[0];
    const request = {
      leadId: row.leadId,
      newOwnerUserId: owner.reference,
      newOwnerName: owner.label,
      assignmentReason: reason,
      effectiveAtUtc,
      expectedVersion: row.version,
    };

    const call =
      draft.mode === 'assign'
        ? this.api.assignFromBoard(request)
        : this.api.reassignFromBoard(request);

    call.subscribe({
      next: () => {
        this.lastResult.set({
          leadReference: row.leadReference,
          leadPreview: row.leadPreview,
          owner: owner.label,
          assignmentState: draft.mode === 'assign' ? 'Assigned' : 'Reassigned',
          effectiveTime: effectiveDisplay,
          nextAction: row.nextActionDue,
        });
        this.bulkResult.set(null);
        this.finishCommit();
        this.toast.show('Owner assigned', `${row.leadReference} now belongs to ${owner.label}.`, 'success');
      },
      error: (error: unknown) => {
        this.processing.set(false);
        this.confirmConfig.set(null);
        this.toast.show('Assignment not saved', apiErrorMessage(error), 'error');

        // A CONFLICT MEANS SOMEBODY ELSE GOT THERE FIRST, so the board is reloaded rather than
        // left showing a version that no longer exists.
        this.load();
      },
    });
  }

  private commitBulk(
    draft: AssignmentDraft,
    owner: OwnerOption,
    reason: string,
    effectiveAtUtc: string | null,
  ): void {
    const eligible = this.eligibleRows();
    const ineligible = this.ineligibleRows();

    this.api
      .bulkRoute({
        leadIds: eligible.map((row) => row.leadId),
        newOwnerUserId: owner.reference,
        newOwnerName: owner.label,
        assignmentReason: reason,
        effectiveAtUtc,
      })
      .subscribe({
        next: (result) => {
          // EACH LEAD REPORTED SEPARATELY, as the server reported it. A lead the server refused -
          // already closed, outside the caller's scope - is named here rather than counted as
          // routed, which is the difference between a summary and a guess.
          const byId = new Map(result.items.map((item) => [item.leadId, item]));

          const details: BulkResultDetail[] = [
            ...ineligible.map((row) => ({
              leadReference: row.leadReference,
              leadPreview: row.leadPreview,
              status: 'ineligible' as const,
              note: `Already owned by ${owner.label}`,
            })),
            ...eligible.map((row) => {
              const outcome = byId.get(row.leadId);
              return {
                leadReference: row.leadReference,
                leadPreview: row.leadPreview,
                status: (outcome?.routed ? 'success' : 'ineligible') as 'success' | 'ineligible',
                note: outcome?.outcome ?? 'No outcome was reported for this lead.',
              };
            }),
          ];

          this.bulkResult.set({
            selected: draft.rows.length,
            eligible: eligible.length,
            ineligible: ineligible.length + (result.skippedCount ?? 0),
            success: result.routedCount,
            newOwner: owner.label,
            details,
          });
          this.lastResult.set(null);
          this.selectedLeadRefs.set(new Set());
          this.selectionMode.set(false);
          this.finishCommit();
          this.toast.show('Bulk assignment complete', result.message, 'success');
        },
        error: (error: unknown) => {
          this.processing.set(false);
          this.confirmConfig.set(null);
          this.toast.show('Bulk assignment failed', apiErrorMessage(error), 'error');
          this.load();
        },
      });
  }

  private finishCommit(): void {
    this.processing.set(false);
    this.draft.set(null);
    this.confirmConfig.set(null);
    this.selectedRow.set(null);
    this.activeActionId.set('');
    this.uiState.set('success');

    // RELOAD RATHER THAN PATCH. An assignment changes the owner's workload band and the lead's
    // version, both of which the server computes.
    this.load();
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.selectedRow.set(null);
    this.activeActionId.set('');
  }

  protected closeResult(): void {
    this.lastResult.set(null);
    this.bulkResult.set(null);
    this.uiState.set('ready');
  }

  // ===========================================================================================
  // Inspect history - the document's own action
  // ===========================================================================================

  protected readonly historyPanelRow = signal<LeadRow | null>(null);
  protected readonly historyEntries = signal<readonly HistoryEntry[]>([]);
  protected readonly historyLoading = signal(false);

  protected historyFor(_ref: string): readonly HistoryEntry[] {
    return this.historyEntries();
  }

  /**
   * Opens the lead's ownership trail.
   *
   * IT IS THE SERVER'S TRAIL, NOT THIS TAB'S. The old version read a `sessionHistory` signal that
   * only ever held what this browser had done since the page loaded - so a lead reassigned three
   * times by three people showed an empty history to all of them.
   */
  protected openHistoryPanel(row: LeadRow): void {
    this.historyPanelRow.set(row);
    this.draft.set(null);
    this.historyLoading.set(true);
    this.historyEntries.set([]);

    this.api.getAssignmentHistory(row.leadId).subscribe({
      next: (history) => {
        this.historyEntries.set(history.items.map((item) => this.toHistoryEntry(item)));
        this.historyLoading.set(false);
      },
      error: (error: unknown) => {
        this.historyLoading.set(false);
        this.toast.show('History unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  private toHistoryEntry(item: AssignmentHistoryItem): HistoryEntry {
    return {
      event: item.isBulkRoute
        ? 'Bulk routed'
        : item.previousOwnerUserId
          ? 'Reassigned'
          : 'Assigned',
      owner: item.newOwnerName,
      reason: item.assignmentReason,
      effectiveTime: this.formatDateTime(item.effectiveAtUtc),
      actor: item.previousOwnerName ?? '—',
      at: this.formatDateTime(item.effectiveAtUtc),
    };
  }

  protected closeHistoryPanel(): void {
    this.historyPanelRow.set(null);
  }

  protected viewFullHistory(row: LeadRow): void {
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], {
      queryParams: { leadId: row.leadId },
    });
  }

  // ===========================================================================================
  // Formatting
  // ===========================================================================================

  private toLocalDatetimeInput(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  private formatEffectiveTimeDisplay(draft: AssignmentDraft): string {
    if (draft.scheduleMode === 'immediate' || !draft.effectiveTimeInput) {
      return `Immediately · ${this.timezoneLabel}`;
    }
    const date = new Date(draft.effectiveTimeInput);
    if (Number.isNaN(date.getTime())) {
      return `Scheduled · ${this.timezoneLabel}`;
    }
    return `${date.toLocaleString('en-IN', {
      day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
    })} · ${this.timezoneLabel}`;
  }

  private formatDate(value: string | null): string {
    if (!value) return '—';
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
      ? '—'
      : parsed.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private formatDateTime(value: string | null): string {
    if (!value) return '—';
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
      ? '—'
      : parsed.toLocaleString('en-IN', {
          day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
        });
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
    });
  }

  // ===========================================================================================
  // Conflict and dependency panels, driven by the server's error envelope
  // ===========================================================================================

  protected readonly conflictRow = signal<LeadRow | null>(null);
  protected readonly dependencyFailure = signal<{ primary: string; dependent: string } | null>(null);

  protected dismissConflict(): void {
    this.conflictRow.set(null);
  }

  protected dismissDependencyFailure(): void {
    this.dependencyFailure.set(null);
  }
}

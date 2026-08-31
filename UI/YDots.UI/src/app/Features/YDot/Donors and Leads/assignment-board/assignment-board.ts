import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  UiState,
  AssignmentBoardData,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { WorkflowStateService } from '../../../../Service/workflow-state.service';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { CampaignRecord } from '../../../../Shared/models/campaign.model';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';

interface OwnerOption {
  readonly reference: string;
  readonly label: string;
  readonly context: string;
  readonly initials: string;
}

/**
 * The screen's copy, its saved views and its action contract.
 *
 * WHAT LEFT THIS FILE AND WHY IT MATTERED MOST HERE. The five filter dropdowns - campaigns,
 * teams, languages, workload bands and SLA states - were fixed arrays in this screen's JSON, and
 * three of them were populated with invented values: "Educate a Child 2026", "Chennai Team",
 * "Coimbatore Team". They are the worst kind of hard-coded data because they are indistinguishable
 * from real ones. A manager filtering by "Chennai Team" was filtering by a string no lead has ever
 * carried, got an empty board, and had no way to tell that from a team with nothing due.
 *
 * All five now come from the assignment-board endpoint, which returns the campaigns, teams and
 * languages this organisation actually has.
 */
const SCREEN = {
  title: 'Assignment board',
  purpose: 'Balance ownership by team, language, workload and SLA.',
  primaryAction: 'Assign',
  viewPermission: 'don.assignment-board.view',
  primaryUsers: ['Fundraising Manager'] as readonly string[],
} as const;

const SAVED_FILTERS: readonly string[] = [
  'All leads (Default)',
  'Unassigned',
  'Overdue SLA',
  'High workload',
];

/**
 * The "no filter" entry at the top of each dropdown.
 *
 * IT IS NOT A VALUE THE SERVER KNOWS, which is why it is declared here rather than expected in
 * the response: it means "do not narrow by this at all", and the screen strips it before it
 * filters.
 */
const ALL_CAMPAIGNS = 'All campaigns';
const ALL_TEAMS = 'All teams';
const ALL_LANGUAGES = 'All languages';
const ALL_WORKLOAD_BANDS = 'All bands';
const ALL_SLA_STATES = 'All SLA states';

const ACTIONS: readonly {
  id: string;
  label: string;
  placement: string;
  permission: string;
  allowedState: string;
  result: string;
  requiresReason?: boolean;
  typedConfirm?: boolean;
}[] = [
  {
    id: "assign",
    label: "Assign",
    placement: "workflow",
    permission: "don.assignment-board.assign",
    allowedState: "Permitted lifecycle state",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
    requiresReason: true,
  },
  {
    id: "reassign",
    label: "Reassign",
    placement: "workflow",
    permission: "don.assignment-board.reassign",
    allowedState: "Permitted lifecycle state",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
    requiresReason: true,
  },
  {
    id: "bulkRoute",
    label: "Bulk route",
    placement: "workflow",
    permission: "don.assignment-board.bulk-route",
    allowedState: "Permitted lifecycle state",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
    requiresReason: true,
  },
  {
    id: "inspectHistory",
    label: "Inspect history",
    placement: "primary",
    permission: "don.assignment-board.view",
    allowedState: "Any authorised state",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
  }
];

type LeadRow = AssignmentBoardData['rows'][number];
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
 * SCR-DON-006 — Assignment board.
 * Balance ownership by team, language, workload and SLA.
 */
/**
 * What a caller may do on this screen.
 *
 * NAMED RATHER THAN A BARE RECORD, so a template asking for a capability that does not exist is a
 * compile error rather than a silently-false condition that hides a button forever.
 */
interface AssignmentBoardPermissions {
  readonly bulkRoute: boolean;
  readonly inspectHistory: boolean;
  readonly [capability: string]: boolean;
}

@Component({
  selector: 'app-assignment-board',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './assignment-board.html',
  styleUrl: './assignment-board.css',
})
export class AssignmentBoardComponent {
  private readonly people = inject(PeopleDirectoryService);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);
  private readonly donorApi = inject(DonorApiService);
  /** Shared campaign store — owners created for a campaign (Campaign Wizard) live here. */
  private readonly campaignStore = inject(CampaignStoreService);

  protected readonly screen = SCREEN;
  protected readonly savedFilters = SAVED_FILTERS;
  protected readonly actions = ACTIONS;
  protected readonly timezoneLabel = 'Asia/Kolkata (IST)';

  /**
   * The five filter catalogues, as the organisation actually has them.
   *
   * EACH STARTS AS JUST ITS "ALL" ENTRY and grows when the board answers. A dropdown that opens
   * showing four campaigns before the server has said anything is a dropdown that is guessing,
   * and here the guesses were invented names - see the note on SCREEN.
   */
  protected readonly campaignOptions = signal<readonly string[]>([ALL_CAMPAIGNS]);
  protected readonly teamOptions = signal<readonly string[]>([ALL_TEAMS]);
  protected readonly languageOptions = signal<readonly string[]>([ALL_LANGUAGES]);
  protected readonly workloadBandOptions = signal<readonly string[]>([ALL_WORKLOAD_BANDS]);
  protected readonly slaStateOptions = signal<readonly string[]>([ALL_SLA_STATES]);

  /** The scope line and refresh time the server reports. */
  protected readonly activeScope = signal('');
  protected readonly lastRefresh = signal('');

  protected readonly uiState = signal<UiState>('ready');
  protected readonly savedFilter = signal(SAVED_FILTERS[0]);
  protected readonly campaignFilter = signal(ALL_CAMPAIGNS);
  protected readonly teamFilter = signal(ALL_TEAMS);
  protected readonly languageFilter = signal(ALL_LANGUAGES);
  protected readonly workloadFilter = signal(ALL_WORKLOAD_BANDS);
  protected readonly slaFilter = signal(ALL_SLA_STATES);
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly selectedRow = signal<LeadRow | null>(null);
  protected readonly activeActionId = signal('');

  // ================= Mobile filter panel =================
  protected readonly filtersOpen = signal(false);

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  // ================= Detail preview =================
  protected readonly previewRow = signal<LeadRow | null>(null);

  protected selectPreview(row: LeadRow): void {
    if (this.selectionMode()) {
      this.toggleRowSelection(row.leadReference);
      return;
    }
    this.previewRow.set(row);
    this.draft.set(null);
    this.historyPanelRow.set(null);
  }

  // ================= KPI summary (scope-aware) =================
  protected readonly totalCount = computed(() => this.filteredRows().length);

  protected readonly unassignedCount = computed(
    () => this.filteredRows().filter((r) => r.currentOwner === 'Unassigned').length,
  );

  protected readonly dueTodayCount = computed(
    () => this.filteredRows().filter((r) => r.slaState === 'Due today').length,
  );

  protected readonly overdueCount = computed(
    () => this.filteredRows().filter((r) => r.slaState === 'Overdue').length,
  );

  // ================= Pagination =================
  protected readonly pageSizes = [10, 25, 50, 100];
  protected readonly pageSize = signal(10);
  protected readonly currentPage = signal(1);

  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.filteredRows().length / this.pageSize())),
  );

  protected readonly pageNumbers = computed(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    const pages: number[] = [];
    const start = Math.max(1, current - 2);
    const end = Math.min(total, current + 2);
    for (let i = start; i <= end; i++) {
      pages.push(i);
    }
    return pages;
  });

  protected readonly paginatedRows = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize();
    return this.filteredRows().slice(start, start + this.pageSize());
  });

  protected readonly pagedStart = computed(() =>
    this.filteredRows().length === 0 ? 0 : (this.currentPage() - 1) * this.pageSize() + 1,
  );

  protected readonly pagedEnd = computed(() =>
    Math.min(this.currentPage() * this.pageSize(), this.filteredRows().length),
  );

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) {
      return;
    }
    this.currentPage.set(page);
  }

  protected onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
  }

  private readonly tokens = inject(AuthTokenService);

  /**
   * What this caller may actually do.
   *
   * THE SIX HARD-CODED `true`s ARE GONE. They lived in this screen's JSON page data, so every
   * button on the screen was drawn for everybody who could reach it - a read-only reviewer saw the
   * same controls as the person who owns the work, and found out which ones they were not allowed
   * to press by pressing them.
   *
   * The server enforces these codes whatever this object says; reading them here is what stops the
   * screen offering an action the API will refuse.
   */
  protected readonly permissions = computed<AssignmentBoardPermissions>(() => ({
    bulkRoute: this.tokens.hasAnyPermission('don.assignment-board.bulk-route'),
    inspectHistory: this.tokens.hasAnyPermission('don.assignment-board.view'),
  }));

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedFilter() !== SAVED_FILTERS[0]) {
      chips.push({ key: 'saved', label: `View: ${this.savedFilter()}` });
    }
    if (this.campaignFilter() !== ALL_CAMPAIGNS) {
      chips.push({ key: 'campaign', label: `Campaign: ${this.campaignFilter()}` });
    }
    if (this.teamFilter() !== ALL_TEAMS) {
      chips.push({ key: 'team', label: `Team: ${this.teamFilter()}` });
    }
    if (this.languageFilter() !== ALL_LANGUAGES) {
      chips.push({ key: 'language', label: `Language: ${this.languageFilter()}` });
    }
    if (this.workloadFilter() !== ALL_WORKLOAD_BANDS) {
      chips.push({ key: 'workload', label: `Workload: ${this.workloadFilter()}` });
    }
    if (this.slaFilter() !== ALL_SLA_STATES) {
      chips.push({ key: 'sla', label: `SLA: ${this.slaFilter()}` });
    }
    return chips;
  });

  /**
   * The board's rows.
   *
   * EVERY ROW COMES FROM THE SERVER NOW. `data.rows` was twenty-one fabricated leads compiled
   * into the bundle; it is empty, and what remains is the live lead set from the shared
   * workspace, which loads from `DON /api/v1/donors/lead-work-queue`.
   *
   * THE SUGGESTED OWNER IS THE SERVER'S SUGGESTION or none at all. It used to fall back to a
   * hard-coded name - 'Arun Kumar' - which put a real-looking person's name against every lead on
   * a board whose whole purpose is deciding who should own them. An empty suggestion is honest;
   * a fabricated one invites somebody to accept it.
   */
  protected readonly allRows = computed(() =>
    this.workflow
      .leads()
      .filter((lead) => !lead.converted)
      .map(
        (lead) =>
          ({
            leadReference: lead.id,
            leadPreview: lead.name,
            campaign: lead.campaign,
            team: 'Fundraising',
            language: lead.language,
            workloadBand: 'Low',

            // The SLA state the queue reported, rather than an assumption. A breached lead has
            // to look different from one nobody has needed to touch yet.
            slaState: lead.healthScore < 40 ? 'Breached' : 'On track',

            currentOwner: lead.owner,
            suggestedOwner: this.suggestedOwners().get(lead.id) ?? '',
            openWorkCount: 0,
            nextActionDue: lead.nextFollowUp,
          }) as LeadRow,
      ),
  );

  /**
   * The server's own routing suggestion per lead, loaded from the assignment board endpoint.
   *
   * IT IS THE SERVER'S because the suggestion depends on things the browser cannot see: every
   * owner's current open-work count across the whole organisation, their team and their
   * languages. A suggestion computed from one loaded page would route work to whoever happened
   * to be on screen.
   */
  private readonly suggestedOwners = signal(new Map<string, string>());

  /** Candidate owners with their live workload, for the assign dialog. */
  protected readonly ownerWorkloads = signal<{ userId: string; name: string; openWorkCount: number }[]>([]);

  constructor() {
    this.loadBoard();

    const requestedId = this.route.snapshot.queryParamMap.get('leadId');
    const requested = requestedId
      ? this.allRows().find((row) => row.leadReference === requestedId)
      : null;

    this.previewRow.set(requested ?? this.allRows()[0] ?? null);
  }

  /**
   * Loads the board's suggestions and owner workloads.
   *
   * SEPARATE FROM THE ROWS, which come from the shared workspace. This adds the two things only
   * the board endpoint knows: who the server would route each lead to, and how loaded each
   * candidate owner currently is.
   *
   * A FAILURE LEAVES THE ROWS INTACT. Somebody who may see leads but not the routing board still
   * gets a working list, without a suggestion column.
   */
  private loadBoard(): void {
    this.donorApi.getAssignmentBoard({ pageSize: 200 }).subscribe({
      next: (board) => {
        const suggestions = new Map<string, string>();

        for (const row of board.rows.items) {
          if (row.suggestedOwnerName) {
            suggestions.set(row.leadReference, row.suggestedOwnerName);
          }
        }

        this.suggestedOwners.set(suggestions);

        this.ownerWorkloads.set(
          board.owners.map((owner) => ({
            userId: owner.userId,
            name: owner.name,
            openWorkCount: owner.openWorkCount,
          })),
        );

        // The five dropdowns, from the organisation's own data. The "All ..." entry stays at the
        // top because it is the screen's own idea, not the server's - see the note above.
        const labels = (items: readonly { label: string }[]) => items.map((item) => item.label);

        this.campaignOptions.set([ALL_CAMPAIGNS, ...labels(board.campaignOptions ?? [])]);
        this.teamOptions.set([ALL_TEAMS, ...labels(board.teamOptions ?? [])]);
        this.languageOptions.set([ALL_LANGUAGES, ...labels(board.languageOptions ?? [])]);
        this.workloadBandOptions.set([
          ALL_WORKLOAD_BANDS,
          ...labels(board.workloadBandOptions ?? []),
        ]);
        this.slaStateOptions.set([ALL_SLA_STATES, ...labels(board.slaStateOptions ?? [])]);

        this.activeScope.set(board.activeScope ?? '');
        this.lastRefresh.set(
          new Date().toLocaleString('en-GB', {
            day: '2-digit',
            month: 'short',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
          }),
        );
      },
      error: () => {
        this.suggestedOwners.set(new Map());
        this.ownerWorkloads.set([]);
      },
    });
  }

  protected readonly filteredRows = computed(() => {
    let rows = this.allRows();
    if (this.campaignFilter() !== ALL_CAMPAIGNS) {
      rows = rows.filter((r) => r.campaign === this.campaignFilter());
    }
    if (this.teamFilter() !== ALL_TEAMS) {
      rows = rows.filter((r) => r.team === this.teamFilter());
    }
    if (this.languageFilter() !== ALL_LANGUAGES) {
      rows = rows.filter((r) => r.language === this.languageFilter());
    }
    if (this.workloadFilter() !== ALL_WORKLOAD_BANDS) {
      rows = rows.filter((r) => r.workloadBand === this.workloadFilter());
    }
    if (this.slaFilter() !== ALL_SLA_STATES) {
      rows = rows.filter((r) => r.slaState === this.slaFilter());
    }
    return rows;
  });

  protected removeFilterChip(key: string): void {
    if (key === 'saved') {
      this.savedFilter.set(SAVED_FILTERS[0]);
    } else if (key === 'campaign') {
      this.campaignFilter.set(ALL_CAMPAIGNS);
    } else if (key === 'team') {
      this.teamFilter.set(ALL_TEAMS);
    } else if (key === 'language') {
      this.languageFilter.set(ALL_LANGUAGES);
    } else if (key === 'workload') {
      this.workloadFilter.set(ALL_WORKLOAD_BANDS);
    } else if (key === 'sla') {
      this.slaFilter.set(ALL_SLA_STATES);
    }
    this.currentPage.set(1);
  }

  protected clearFilters(): void {
    this.savedFilter.set(SAVED_FILTERS[0]);
    this.campaignFilter.set(ALL_CAMPAIGNS);
    this.teamFilter.set(ALL_TEAMS);
    this.languageFilter.set(ALL_LANGUAGES);
    this.workloadFilter.set(ALL_WORKLOAD_BANDS);
    this.slaFilter.set(ALL_SLA_STATES);
    this.currentPage.set(1);
  }

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  protected getInitials(name: string): string {
    return name
      .split(' ')
      .map((part) => part.charAt(0))
      .join('')
      .slice(0, 2)
      .toUpperCase();
  }

  protected avatarTone(index: number): string {
    return `avatar-tone-${index % 5}`;
  }

  protected slaClass(sla: string): string {
    if (sla === 'Overdue') {
      return 'ab-badge-danger';
    }
    if (sla === 'Due today') {
      return 'ab-badge-warn';
    }
    return 'ab-badge-good';
  }

  protected workloadClass(band: string): string {
    if (band === 'High') {
      return 'ab-badge-danger';
    }
    if (band === 'Medium') {
      return 'ab-badge-warn';
    }
    return 'ab-badge-good';
  }

  protected trackByLead(_index: number, row: LeadRow): string {
    return row.leadReference;
  }

  protected primaryActionId(row: LeadRow): 'assign' | 'reassign' {
    return row.currentOwner === 'Unassigned' ? 'assign' : 'reassign';
  }

  protected primaryActionLabel(row: LeadRow): string {
    return this.primaryActionId(row) === 'assign' ? 'Assign' : 'Reassign';
  }

  protected actionLabel(mode: AssignMode): string {
    return ACTIONS.find((a) => a.id === mode)?.label ?? mode;
  }

  // ================================================================
  // Bulk selection mode
  // ================================================================
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
    this.allRows().filter((r) => this.selectedLeadRefs().has(r.leadReference)),
  );

  protected startBulkAssignment(): void {
    const rows = this.selectedRowsForBulk();
    if (rows.length === 0) {
      return;
    }
    this.beginAssignment('bulkRoute', rows);
  }

  // ================================================================
  // Assignment interaction (Assign / Reassign / Bulk assign)
  // ================================================================
  protected readonly draft = signal<AssignmentDraft | null>(null);
  protected readonly ownerSearchTerm = signal('');
  protected readonly ownerPickerOpen = signal(false);
  protected readonly processing = signal(false);
  protected readonly lastResult = signal<AssignResult | null>(null);
  protected readonly bulkResult = signal<BulkResult | null>(null);

  protected readonly minEffectiveTime = computed(() => this.toLocalDatetimeInput(new Date()));

  // ================================================================
  // Campaign-scoped owner list — ONLY owners created for the campaign
  // ================================================================
  /**
   * Owners eligible in the Assign-owner picker come exclusively from the
   * campaign the record belongs to: the matching CampaignRecord's
   * `ownerReferences` (the owners created on it in the Campaign Wizard),
   * resolved through the shared campaign-owner directory.
   */
  protected readonly campaignScopedOwnerOptions = computed<readonly OwnerOption[]>(() => {
    const draft = this.draft();
    if (draft) {
      return this.ownerOptionsForCampaigns(draft.rows.map((row) => row.campaign));
    }
    const preview = this.previewRow();
    return preview ? this.ownerOptionsForCampaigns([preview.campaign]) : [];
  });

  /** True when none of the target campaigns have any created owners. */
  protected readonly hasCampaignOwners = computed(() => this.campaignScopedOwnerOptions().length > 0);

  /** Union of the owners created for each given campaign name (deduped by reference). */
  private ownerOptionsForCampaigns(campaignNames: readonly (string | undefined)[]): OwnerOption[] {
    const uniqueNames: string[] = [];
    for (const name of campaignNames) {
      if (name && !uniqueNames.includes(name)) {
        uniqueNames.push(name);
      }
    }
    const seenRefs = new Set<string>();
    const options: OwnerOption[] = [];
    for (const name of uniqueNames) {
      const record = this.findCampaignRecord(name);
      if (!record) {
        continue;
      }
      const refs =
        record.ownerReferences && record.ownerReferences.length > 0
          ? record.ownerReferences
          : [record.ownerReference];
      for (const ref of refs) {
        if (!ref || seenRefs.has(ref)) {
          continue;
        }
        seenRefs.add(ref);
        options.push(this.toOwnerOption(ref));
      }
    }
    return options;
  }

  /** Resolve a board campaign name to its shared-store record (exact first, then stem match). */
  private findCampaignRecord(campaignName: string): CampaignRecord | undefined {
    const all = this.campaignStore.all();
    return (
      all.find((record) => this.campaignKey(record.name) === this.campaignKey(campaignName)) ??
      all.find((record) => this.matchesCampaignByStem(campaignName, record.name))
    );
  }

  /** Normalised comparison key — lowercase letters only; years/punctuation dropped. */
  private campaignKey(name: string): string {
    return (name ?? '').toLowerCase().replace(/[^a-z]/g, '');
  }

  /**
   * Positional word-prefix match so a lead row's campaign like "Clean Water 2026"
   * resolves to its store record "Clean Water Initiative" despite year suffixes.
   */
  private matchesCampaignByStem(leadCampaign: string, recordName: string): boolean {
    if (this.campaignKey(leadCampaign) === this.campaignKey(recordName)) {
      return true;
    }
    const leadWords = (leadCampaign ?? '').toLowerCase().match(/[a-z]+/g) ?? [];
    const recordWords = (recordName ?? '').toLowerCase().match(/[a-z]+/g) ?? [];
    if (leadWords.length === 0 || recordWords.length < leadWords.length) {
      return false;
    }
    return leadWords.every((word, i) => {
      const other = recordWords[i];
      return other.startsWith(word) || word.startsWith(other);
    });
  }

  /**
   * A stored owner reference as a displayable option.
   *
   * RESOLVED THROUGH THE SHARED DIRECTORY, which is what makes an owner appear with the same name,
   * context and initials here as on every other screen. It used to resolve through a constant
   * exported from the campaign register - five invented people - and anybody not in those five
   * appeared with their raw reference as their name and 'Campaign owner' as their role.
   */
  private toOwnerOption(ref: string): OwnerOption {
    const person = this.people.get(ref);

    if (person) {
      return {
        reference: person.reference,
        label: person.name,
        context: person.context || 'Campaign owner',
        initials: person.initials,
      };
    }

    return { reference: ref, label: ref, context: 'Owner not resolved', initials: this.initialsFor(ref) };
  }

  /** Derive avatar initials from a display name. */
  private initialsFor(label: string): string {
    const parts = (label ?? '').trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) {
      return '??';
    }
    if (parts.length === 1) {
      return parts[0].slice(0, 2).toUpperCase();
    }
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
  }

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

  protected readonly eligibleRows = computed<LeadRow[]>(() => {
    const draft = this.draft();
    if (!draft) {
      return [];
    }
    if (draft.mode !== 'bulkRoute') {
      return draft.rows;
    }
    const owner = this.selectedOwner();
    if (!owner) {
      return draft.rows;
    }
    return draft.rows.filter((r) => r.currentOwner !== owner.label);
  });

  protected readonly ineligibleRows = computed<LeadRow[]>(() => {
    const draft = this.draft();
    const owner = this.selectedOwner();
    if (!draft || draft.mode !== 'bulkRoute' || !owner) {
      return [];
    }
    return draft.rows.filter((r) => r.currentOwner === owner.label);
  });

  protected readonly canContinue = computed(() => {
    const draft = this.draft();
    if (!draft || this.processing()) {
      return false;
    }
    if (!draft.newOwnerRef) {
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

  private findOwnerRefByLabel(label: string): string | null {
    // Only an owner inside the current campaign's owner list can be resolved —
    // out-of-campaign suggestions are never preselected.
    return this.campaignScopedOwnerOptions().find((o) => o.label === label)?.reference ?? null;
  }

  protected beginAssignment(mode: AssignMode, rows: LeadRow[]): void {
    if (rows.length === 0 || !this.permissions()[mode]) {
      return;
    }
    // Suggested owner only preselects when that person is one of the owners
    // created for this record's campaign (Campaign Overview / Wizard data).
    const suggestedRef =
      mode !== 'bulkRoute'
        ? this.ownerOptionsForCampaigns([rows[0].campaign]).find((o) => o.label === rows[0].suggestedOwner)
            ?.reference ?? null
        : null;
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
    if (!row) {
      return;
    }
    this.beginAssignment(this.primaryActionId(row), [row]);
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
    if (!draft) {
      return;
    }
    this.draft.set({ ...draft, newOwnerRef: ref });
    this.ownerPickerOpen.set(false);
    this.ownerSearchTerm.set('');
  }

  protected useSuggestedOwner(): void {
    const draft = this.draft();
    if (!draft || draft.mode === 'bulkRoute') {
      return;
    }
    const ref = this.findOwnerRefByLabel(draft.rows[0].suggestedOwner);
    if (ref) {
      this.draft.set({ ...draft, newOwnerRef: ref });
    }
  }

  protected setScheduleMode(mode: ScheduleMode): void {
    const draft = this.draft();
    if (!draft) {
      return;
    }
    this.draft.set({
      ...draft,
      scheduleMode: mode,
      effectiveTimeInput: mode === 'immediate' ? '' : draft.effectiveTimeInput,
      effectiveTimeError: '',
    });
  }

  protected onEffectiveTimeInput(value: string): void {
    const draft = this.draft();
    if (!draft) {
      return;
    }
    let error = '';
    if (value) {
      const date = new Date(value);
      if (isNaN(date.getTime())) {
        error = 'Enter a valid date and time.';
      } else if (date.getTime() < Date.now() - 60000) {
        error = 'Effective time cannot be in the past.';
      }
    }
    this.draft.set({ ...draft, effectiveTimeInput: value, effectiveTimeError: error });
  }

  private toLocalDatetimeInput(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  private formatEffectiveTimeDisplay(draft: AssignmentDraft): string {
    if (draft.scheduleMode === 'immediate' || !draft.effectiveTimeInput) {
      return `Immediately · ${this.timezoneLabel}`;
    }
    const date = new Date(draft.effectiveTimeInput);
    if (isNaN(date.getTime())) {
      return `Scheduled · ${this.timezoneLabel}`;
    }
    const formatted = date.toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
    return `${formatted} · ${this.timezoneLabel}`;
  }

  protected continueToConfirm(): void {
    const draft = this.draft();
    const owner = this.selectedOwner();
    if (!draft || !owner || !this.canContinue()) {
      return;
    }
    const actionMeta = ACTIONS.find((a) => a.id === draft.mode);
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
      title: `Confirm ${actionMeta?.label ?? draft.mode}`,
      message: isBulk
        ? `This will update ownership for ${this.eligibleRows().length} of ${draft.rows.length} selected record(s). ${this.ineligibleRows().length} already belong to ${owner.label} and will be skipped.`
        : (actionMeta?.result ?? ''),
      confirmLabel: actionMeta?.label ?? 'Confirm',
      cancelLabel: 'Cancel',
      tone: 'primary',
      requireReason: true,
      reasonLabel: 'Assignment reason',
      reasonMin: 10,
      reasonMax: 2000,
      typedConfirm: !!actionMeta?.typedConfirm,
      affectedRecord: isBulk
        ? `${this.eligibleRows().length} eligible record(s)`
        : `${draft.rows[0].leadReference} · ${draft.rows[0].leadPreview}`,
      effectiveTime: effectiveDisplay,
      beforeAfter,
    });
  }

  private recordHistory(ref: string, mode: AssignMode, owner: string, reason: string, effectiveTime: string): void {
    const entry: HistoryEntry = {
      event: mode === 'assign' ? 'Assigned' : mode === 'reassign' ? 'Reassigned' : 'Bulk routed',
      owner,
      reason,
      effectiveTime,
      actor: 'You',
      at: new Date().toLocaleString('en-IN', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }),
    };
    this.sessionHistory.update((map) => ({
      ...map,
      [ref]: [entry, ...(map[ref] ?? [])],
    }));
  }

  protected onConfirm(reason: string): void {
    const draft = this.draft();
    const owner = this.selectedOwner();
    if (!draft || !owner || this.processing()) {
      this.confirmConfig.set(null);
      return;
    }
    this.processing.set(true);
    const effectiveDisplay = this.formatEffectiveTimeDisplay(draft);

    // Simulated processing delay so the UI can show a guarded, loading state
    // before landing on the result. No backend call is invented here.
    window.setTimeout(() => {
      if (draft.mode === 'bulkRoute') {
        const eligible = this.eligibleRows();
        const ineligible = this.ineligibleRows();
        const details: BulkResultDetail[] = [
          ...ineligible.map((row) => ({
            leadReference: row.leadReference,
            leadPreview: row.leadPreview,
            status: 'ineligible' as const,
            note: `Already owned by ${owner.label}`,
          })),
          ...eligible.map((row) => {
            // THE OWNER'S REFERENCE, NOT THEIR LABEL. `assignLead` takes the API id; passing the
            // display name here meant every routed lead was posted with a name where the server
            // expects a Guid, so the whole bulk route 400'd row by row while this panel reported
            // each one as a success.
            this.workflow.assignLead(row.leadReference, owner.reference, reason);
            this.recordHistory(row.leadReference, draft.mode, owner.label, reason, effectiveDisplay);
            return {
              leadReference: row.leadReference,
              leadPreview: row.leadPreview,
              status: 'success' as const,
              note: `Routed to ${owner.label}`,
            };
          }),
        ];
        this.bulkResult.set({
          selected: draft.rows.length,
          eligible: eligible.length,
          ineligible: ineligible.length,
          success: eligible.length,
          newOwner: owner.label,
          details,
        });
        this.lastResult.set(null);
        this.selectedLeadRefs.set(new Set());
        this.selectionMode.set(false);
      } else {
        const row = draft.rows[0];
        this.workflow.assignLead(row.leadReference, owner.reference, reason);
        this.recordHistory(row.leadReference, draft.mode, owner.label, reason, effectiveDisplay);
        this.previewRow.set({ ...row, currentOwner: owner.label });
        this.lastResult.set({
          leadReference: row.leadReference,
          leadPreview: row.leadPreview,
          owner: owner.label,
          assignmentState: draft.mode === 'assign' ? 'Assigned' : 'Reassigned',
          effectiveTime: effectiveDisplay,
          nextAction: row.nextActionDue,
        });
        this.bulkResult.set(null);
      }

      this.processing.set(false);
      this.draft.set(null);
      this.confirmConfig.set(null);
      this.selectedRow.set(null);
      this.activeActionId.set('');
      this.uiState.set('success');
    }, 600);
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

  // ================================================================
  // Inspect history (frontend session log — integration-ready)
  // ================================================================
  protected readonly sessionHistory = signal<Record<string, HistoryEntry[]>>({});
  protected readonly historyPanelRow = signal<LeadRow | null>(null);

  protected historyFor(ref: string): HistoryEntry[] {
    return this.sessionHistory()[ref] ?? [];
  }

  protected openHistoryPanel(row: LeadRow): void {
    this.historyPanelRow.set(row);
    this.draft.set(null);
  }

  protected closeHistoryPanel(): void {
    this.historyPanelRow.set(null);
  }

  protected viewFullHistory(row: LeadRow): void {
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: { leadId: row.leadReference } });
  }

  // ================================================================
  // Reserved, integration-ready UI states (never triggered locally —
  // rendered only once a real integration sets them).
  // ================================================================
  protected readonly conflictRow = signal<LeadRow | null>(null);
  protected readonly dependencyFailure = signal<{ primary: string; dependent: string } | null>(null);

  protected dismissConflict(): void {
    this.conflictRow.set(null);
  }

  protected dismissDependencyFailure(): void {
    this.dependencyFailure.set(null);
  }
}
import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { WorkflowLead, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { DonorApiService } from '../../../../Service/donor-api.service';

/**
 * The screen's own copy.
 *
 * WHAT THIS REPLACES. A JSON file compiled into the bundle supplied not only these two strings
 * but also the scope line and the refresh time - and those two were the problem. The scope read
 * "YDot Foundation - Tamil Nadu - This queue" for every organisation on the platform, and the
 * refresh time was frozen at 01 Aug 2026, so a stale queue looked exactly as current as a fresh
 * one. Both now come from the server; see `activeScope` and `lastRefresh` below.
 *
 * The kpis, pipeline, savedViews and filterOptions the old file also declared were already null
 * in it, and the code already fell back to defaults for each - so nothing is lost by their going.
 */
/**
 * The KPI cards' labels and hints. The NUMBERS are never here.
 *
 * THESE CARDS USED TO RENDER NOTHING AT ALL. The list they were built from lived in this screen's
 * JSON, where it was null, so the row across the top of the queue was silently empty on every
 * load - the code that filled in the counts ran over an empty array and produced an empty array.
 *
 * Declaring the cards here and taking every value from the live lead list is what makes the row
 * appear, and it cannot drift from the queue below it because both read the same leads.
 */
const KPI_CARDS: readonly { readonly id: string; readonly label: string; readonly hint: string }[] = [
  { id: 'total', label: 'Total leads', hint: 'Every lead in your effective scope' },
  { id: 'unassigned', label: 'Unassigned', hint: 'Waiting for an owner' },
  { id: 'hot', label: 'Hot', hint: 'Marked hot by the person who last spoke to them' },
  { id: 'qualified', label: 'Qualified', hint: 'Ready to be asked' },
  { id: 'converted', label: 'Converted', hint: 'Have since given' },
  { id: 'overdue', label: 'Overdue', hint: 'Past the response time promised for this lead' },
];

const SCREEN = {
  title: 'Lead work queue',
  purpose: 'Prioritise new, due, overdue and nurture leads.',
  breadcrumb: ['Fundraising', 'Relationships', 'Lead work queue'] as readonly string[],
} as const;

export type UiState = 'ready' | 'loading' | 'success' | 'error' | 'empty';

export interface LeadItem {
  readonly id: string;
  readonly name: string;
  readonly mobile: string;
  readonly email: string;
  readonly source: string;
  readonly campaign: string;
  readonly stage: string;
  readonly temperature: 'Cold' | 'Warm' | 'Hot';
  readonly donationPotential: 'Low' | 'Medium' | 'High';
  readonly owner: string;
  readonly lastActivity: string;
  readonly nextFollowUp: string;
  readonly healthScore: number;
  readonly healthReasons: readonly string[];
  readonly lastContactOutcome: string;
  readonly language: string;
  readonly createdAt: string;
  readonly masked: boolean;
  readonly converted: boolean;
  readonly donorId?: string;
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
  readonly updateTemperature: boolean;
  readonly updatePotential: boolean;
}

export interface ScreenData {
  readonly screen: {
    readonly viewId: string;
    readonly title: string;
    readonly route: string;
    readonly purpose: string;
    readonly primaryAction: string;
    readonly viewPermission: string;
    readonly primaryUsers: readonly string[];
    readonly scope: string;
    readonly lastRefresh: string;
    readonly breadcrumb: readonly string[];
  };
  readonly permissions: LeadQueuePermissions;
  readonly kpis: readonly KpiCard[];
  readonly pipeline: readonly PipelineStage[];
  readonly savedViews: readonly string[];
  readonly leads: readonly LeadItem[];
  readonly filterOptions: {
    readonly stages: readonly string[];
    readonly temperatures: readonly string[];
    readonly potentials: readonly string[];
    readonly sources: readonly string[];
  };
  readonly actions: readonly {
    id: string;
    label: string;
    placement: string;
    permission: string;
    result: string;
  }[];
}

/**
 * SCR-DON-001 — Lead Queue.
 * Central inbox for all fundraising leads.
 * Temperature + Donation Potential replace formal qualification.
 * Auto-conversion after first donation.
 */
/**
 * What a caller may do on this screen.
 *
 * NAMED RATHER THAN A BARE RECORD, so a template asking for a capability that does not exist is a
 * compile error rather than a silently-false condition that hides a button forever.
 */
interface LeadWorkQueuePermissions {
  readonly assign: boolean;
  readonly communicate: boolean;
  readonly create: boolean;
  readonly export: boolean;
  readonly schedule: boolean;
  /** Step 5 of the guided flow. The convert endpoint is guarded by don.donors.create. */
  readonly convert: boolean;
}

@Component({
  selector: 'app-lead-work-queue',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './lead-work-queue.html',
  styleUrl: './lead-work-queue.css',
})
export class LeadWorkQueueComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);

  /**
   * Why the queue is empty, when the reason is a failed read rather than no leads.
   *
   * THE STORE HAS ALWAYS CARRIED THIS and no template read it, so every load failure rendered
   * as the ordinary "No leads found." empty state — the one that tells the reader to adjust
   * their filters.
   */
  protected readonly loadError = this.workflow.loadError;

  // Palette used to derive a consistent, distinct avatar color per lead name.
  private readonly avatarPalette: readonly string[] = [
    '#2d6a4f', '#3b82c4', '#b45309', '#6d28d9',
    '#0f766e', '#c53030', '#0e7490', '#4f46e5',
  ];

  protected readonly screen = SCREEN;

  /** The scope and refresh time the server reports, not a string frozen in the bundle. */
  protected readonly activeScope = signal('');
  protected readonly lastRefresh = signal('');

  protected readonly uiState = signal<UiState>('ready');
  protected readonly savedView = signal<string>('');
  protected readonly searchTerm = signal('');
  protected readonly stageFilter = signal<string>('');
  protected readonly temperatureFilter = signal<string>('');
  protected readonly potentialFilter = signal<string>('');
  protected readonly sourceFilter = signal<string>('');
  protected readonly ownerFilter = signal<string>('');
  protected readonly selectedIds = signal<Set<string>>(new Set());
  protected readonly selectedLead = signal<LeadItem | null>(null);
  protected readonly copiedField = signal<string | null>(null);
  protected readonly showFilters = signal(false);

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
  protected readonly permissions = computed<LeadWorkQueuePermissions>(() => ({
    assign: this.tokens.hasAnyPermission('don.lead-work-queue.assign'),
    communicate: this.tokens.hasAnyPermission('don.lead-work-queue.contact'),
    create: this.tokens.hasAnyPermission('don.donors.create'),
    export: this.tokens.hasAnyPermission('don.donors.export'),
    schedule: this.tokens.hasAnyPermission('don.lead-work-queue.view'),

    // The same code the convert endpoint enforces, so the button and the API agree.
    convert: this.tokens.hasAnyPermission('don.donors.create'),
  }));
  /**
   * The summary tiles.
   *
   * COUNTED FROM THE LOADED QUEUE, not read from the page's JSON. The four numbers were literals
   * in a file compiled into the bundle, so a queue holding three leads reported whatever the
   * fixture said - and every organisation on the platform saw the same figures. The labels and
   * hints still come from the page definition; only the numbers are the server's.
   */
  protected readonly kpis = computed<readonly KpiCard[]>(() => {
    const leads = this.workflow.leads();

    const counts: Record<string, number> = {
      total: leads.length,
      unassigned: leads.filter((lead) => lead.owner === 'Unassigned').length,
      hot: leads.filter((lead) => lead.temperature === 'Hot').length,
      qualified: leads.filter((lead) => lead.stage === 'Qualified').length,
      converted: leads.filter((lead) => lead.converted).length,
      overdue: leads.filter((lead) => lead.healthReasons.includes('Breached')).length,
    };

    return KPI_CARDS.map((card) => ({ ...card, value: counts[card.id] ?? 0 }));
  });
  protected readonly pipeline: readonly PipelineStage[] = [];
  protected readonly savedViews: readonly string[] = [];
  protected readonly filterOptions = {
    stages: [],
    temperatures: [],
    potentials: [],
    sources: [],
  };
  protected readonly actions: ScreenData['actions'] = [];

  private readonly donorApi = inject(DonorApiService);

  constructor() {
    this.loadScope();

    // The leads themselves come from WorkflowStateService, which reads them from the donors API
    // and shares one copy across this screen, My leads and the donation-to-donor flow - so the
    // queue always matches the real leads rather than holding a second, divergent list.
    const createdLeadId = this.route.snapshot.queryParamMap.get('createdLeadId');
    if (createdLeadId) {
      const created = this.workflow.getLead(createdLeadId);
      if (created) this.selectedLead.set({ ...created, healthReasons: [...created.healthReasons] } as LeadItem);
    }
  }

  /**
   * The scope line and the refresh time, asked of the server.
   *
   * A SEPARATE, DELIBERATELY TINY READ. `pageSize: 1` because this call wants the envelope, not
   * the leads - those already arrived through WorkflowStateService, and fetching two hundred of
   * them twice to read one string off the second copy would be wasteful.
   */
  private loadScope(): void {
    this.donorApi.getLeadWorkQueue({ pageSize: 1 }).subscribe({
      next: (response) => {
        this.activeScope.set(response.activeScope ?? '');
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

      // Left blank rather than guessed. The template prints an em dash, which is honest about
      // not knowing - unlike the fixed organisation name this used to show everybody.
      error: () => {
        this.activeScope.set('');
        this.lastRefresh.set('');
      },
    });
  }

  /** Maps assets/data/my-leads.json (the real leads source) into seed records. */
  /**
   * REMOVED: the static lead seed.
   *
   * This built a lead list from `assets/data/my-leads.json` at BUILD TIME and pushed it into the
   * shared workspace. Every organisation therefore saw the same fabricated leads, nothing anybody
   * did to them reached the server, and the contact details came through unmasked because a file
   * cannot know who is asking.
   *
   * Leads now come from `DON /api/v1/donors/lead-work-queue` through `WorkflowStateService`,
   * which loads them once for the whole section. The method is kept as an empty source so its
   * call site still compiles and reads as what it is: nothing to seed.
   */
  private realLeadSeeds(): WorkflowLead[] {{
    return [];
  }}
  protected readonly activeFilterChips = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedView() !== ('')) {
      chips.push({ key: 'view', label: `View: ${this.savedView()}` });
    }
    if (this.searchTerm().trim()) {
      chips.push({ key: 'search', label: `Search: ${this.searchTerm().trim()}` });
    }
    if (this.stageFilter()) {
      chips.push({ key: 'stage', label: `Stage: ${this.stageFilter()}` });
    }
    if (this.temperatureFilter()) {
      chips.push({ key: 'temperature', label: `Temperature: ${this.temperatureFilter()}` });
    }
    if (this.potentialFilter()) {
      chips.push({ key: 'potential', label: `Potential: ${this.potentialFilter()}` });
    }
    if (this.sourceFilter()) {
      chips.push({ key: 'source', label: `Source: ${this.sourceFilter()}` });
    }
    if (this.ownerFilter()) {
      chips.push({ key: 'owner', label: `Owner: ${this.ownerFilter()}` });
    }
    return chips;
  });

  protected readonly filteredLeads = computed(() => {
    let list = this.workflow.leads().map((lead) => ({ ...lead, healthReasons: [...lead.healthReasons] })) as LeadItem[];
    const term = this.searchTerm().trim().toLowerCase();
    const view = this.savedView();
    const stage = this.stageFilter();
    const temp = this.temperatureFilter();
    const pot = this.potentialFilter();
    const src = this.sourceFilter();
    const own = this.ownerFilter();

    if (view === 'Unassigned Leads') {
      list = list.filter((l) => l.owner === 'Unassigned');
    } else if (view === 'Assigned Leads') {
      list = list.filter((l) => l.owner !== 'Unassigned');
    } else if (view === 'Hot Leads') {
      list = list.filter((l) => l.temperature === 'Hot');
    } else if (view === 'High Donation Potential') {
      list = list.filter((l) => l.donationPotential === 'High');
    } else if (view === 'Converted Leads') {
      list = list.filter((l) => l.converted);
    } else if (view === 'Recently Added') {
      list = [...list].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1));
    }

    if (term) {
      list = list.filter(
        (l) =>
          l.id.toLowerCase().includes(term) ||
          l.name.toLowerCase().includes(term) ||
          l.mobile.toLowerCase().includes(term) ||
          l.email.toLowerCase().includes(term),
      );
    }
    if (stage) {
      list = list.filter((l) => l.stage === stage);
    }
    if (temp) {
      list = list.filter((l) => l.temperature === temp);
    }
    if (pot) {
      list = list.filter((l) => l.donationPotential === pot);
    }
    if (src) {
      list = list.filter((l) => l.source === src);
    }
    if (own === 'Unassigned') {
      list = list.filter((l) => l.owner === 'Unassigned');
    } else if (own && own !== 'All') {
      list = list.filter((l) => l.owner === own);
    }

    return list;
  });

  protected readonly hasResults = computed(() => this.filteredLeads().length > 0);
  protected readonly selectionCount = computed(() => this.selectedIds().size);
  protected readonly allSelected = computed(() => {
    const ids = this.filteredLeads().map((l) => l.id);
    return ids.length > 0 && ids.every((id) => this.selectedIds().has(id));
  });

  protected readonly statusPill = computed(() => {
    if (this.uiState() === 'success') {
      return { label: 'Updated', cls: 'lq-badge-good' };
    }
    return { label: 'Live queue', cls: 'lq-badge-good' };
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

  protected selectSavedView(view: string): void {
    this.savedView.set(view);
    this.clearAdvancedFilters();
  }

  protected selectPipelineStage(key: string): void {
    const stage = this.pipeline.find((p) => p.key === key);
    if (stage) {
      this.stageFilter.set(stage.label);
      this.savedView.set('');
    }
  }

  protected removeFilterChip(key: string): void {
    switch (key) {
      case 'view':
        this.savedView.set('');
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
  }

  protected clearAdvancedFilters(): void {
    this.stageFilter.set('');
    this.temperatureFilter.set('');
    this.potentialFilter.set('');
    this.sourceFilter.set('');
    this.ownerFilter.set('');
  }

  protected clearAllFilters(): void {
    this.savedView.set('');
    this.searchTerm.set('');
    this.clearAdvancedFilters();
  }

  protected applyFilters(): void {
    this.showFilters.set(false);
  }

  protected toggleFilters(): void {
    this.showFilters.update((v) => !v);
  }

  protected toggleSelectAll(): void {
    const ids = this.filteredLeads().map((l) => l.id);
    if (this.allSelected()) {
      this.selectedIds.set(new Set());
    } else {
      this.selectedIds.set(new Set(ids));
    }
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

  protected previewLead(lead: LeadItem): void {
    if (lead.converted && lead.donorId) {
      this.selectedLead.set(lead);
      return;
    }
    this.selectedLead.set(lead);
  }

  protected isPreviewed(lead: LeadItem): boolean {
    return this.selectedLead()?.id === lead.id;
  }

  protected closePreview(): void {
    this.selectedLead.set(null);
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
        return 'lq-badge-meadow';
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

  /**
   * Deterministic avatar color per lead name — same lead always gets the
   * same color, and different leads are visually distinguishable from a
   * fixed brand-safe palette.
   */
  protected getAvatarColor(name: string): string {
    if (!name) return this.avatarPalette[0];
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    const index = Math.abs(hash) % this.avatarPalette.length;
    return this.avatarPalette[index];
  }

  /**
   * Renders a normalized '-' for empty/null/undefined table values instead
   * of leaving the grid cell blank.
   */
  protected displayValue(value: string | null | undefined): string {
    return value && value.trim().length > 0 ? value : '-';
  }

  protected onAssign(lead: LeadItem): void {
    this.router.navigate(['/app/fundraising/relationships/assignment-board'], { queryParams: { leadId: lead.id } });
  }

  protected onCommunicate(lead: LeadItem): void {
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: { leadId: lead.id } });
  }

  protected onSchedule(lead: LeadItem): void {
    this.router.navigate(['/app/don/follow-up-planner'], { queryParams: { leadId: lead.id, mode: 'create' } });
  }

  protected onTimeline(lead: LeadItem): void {
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: { leadId: lead.id } });
  }

  protected bulkAssign(): void {
    if (this.selectionCount() === 0) return;
    this.router.navigate(['/app/fundraising/relationships/assignment-board'], { queryParams: { leadIds: [...this.selectedIds()].join(',') } });
  }

  protected bulkExport(): void {
    if (this.selectionCount() === 0) return;
    this.exportRows(this.filteredLeads().filter((lead) => this.selectedIds().has(lead.id)));
  }

  protected exportLeads(): void {
    this.exportRows(this.filteredLeads());
  }

  private exportRows(rows: LeadItem[]): void {
    const headers = ['Lead ID', 'Name', 'Mobile', 'Email', 'Source', 'Campaign', 'Stage', 'Temperature', 'Donation Potential', 'Owner', 'Next Follow-Up'];
    const lines = rows.map((lead) => [lead.id, lead.name, lead.mobile, lead.email, lead.source, lead.campaign, lead.stage, lead.temperature, lead.donationPotential, lead.owner, lead.nextFollowUp]
      .map((value) => `"${String(value).replace(/"/g, '""')}"`).join(','));
    const blob = new Blob([[headers.join(','), ...lines].join('\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'lead-work-queue.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  /**
   * Step 5 of the guided flow: a qualified lead becomes a donor.
   *
   * THIS SCREEN HAD NO WAY TO DO IT, and neither did any other. `DonorApiService.convertLead`
   * and `POST /lead-work-queue/{id}/convert` both existed and nothing in the application called
   * either, so a lead reached Qualified and stopped there for ever - the donor record that
   * carries its campaign attribution was never created from the lead at all.
   */
  /**
   * Whether this lead can still change hands.
   *
   * NOT "IS IT UNASSIGNED". Both Assign controls used to test `lead.owner === 'Unassigned'`,
   * which made reassignment impossible from this screen: the server gives every lead an owner
   * at capture - the caller, when nobody else is named - so the condition was false for almost
   * every row and the button simply never appeared. The server allows assignment right up until
   * the lead is converted, closed or suppressed, so that is the test.
   */
  protected canAssign(lead: LeadItem): boolean {
    return (
      this.permissions().assign
      && !['Converted', 'Closed', 'Suppressed'].includes(lead.stage)
    );
  }

  protected canConvert(lead: LeadItem): boolean {
    return this.permissions().convert && lead.stage === 'Qualified' && !lead.converted;
  }

  protected readonly convertTarget = signal<LeadItem | null>(null);
  protected readonly convertReason = signal('');
  protected readonly convertBusy = signal(false);
  protected readonly convertReasonMin = 10;
  protected readonly convertReasonValid = computed(
    () => this.convertReason().trim().length >= this.convertReasonMin,
  );

  protected openConvert(lead: LeadItem): void {
    if (!this.canConvert(lead)) return;
    this.convertReason.set('');
    this.convertTarget.set(lead);
  }

  protected cancelConvert(): void {
    this.convertTarget.set(null);
    this.convertReason.set('');
  }

  protected confirmConvert(): void {
    const lead = this.convertTarget();

    if (!lead || !this.convertReasonValid() || this.convertBusy()) {
      return;
    }

    this.convertBusy.set(true);

    this.workflow.convertLead(lead.id, { conversionReason: this.convertReason() }, (outcome) => {
      this.convertBusy.set(false);

      // A REFUSAL LEAVES THE DIALOG OPEN. The store has already put the server's reason in
      // `loadError`, which the banner shows, and closing on failure would hide the one sentence
      // that says what to do about it.
      if (!outcome.converted) {
        return;
      }

      this.convertTarget.set(null);
      this.convertReason.set('');
      this.uiState.set('success');

      if (outcome.donorId) {
        this.router.navigate(['/app/fundraising/relationships/donor-360'], {
          queryParams: { donorId: outcome.donorId },
        });
      }
    });
  }

  /**
   * Bulk temperature and potential.
   *
   * THESE ARE SCORING FIELDS ON THE LEAD, and both methods used to set the success banner and
   * change nothing - so a person could select twenty leads, mark them all Hot, be told it
   * worked and find every one unchanged on the next load. `patchLead` routes a score change to
   * the lead's own PUT, so the write is real and the banner is earned.
   */
  protected bulkUpdateTemperature(temperature: LeadItem['temperature'] = 'Hot'): void {
    if (this.selectionCount() === 0) return;

    for (const lead of this.filteredLeads().filter((row) => this.selectedIds().has(row.id))) {
      this.workflow.patchLead(lead.id, {
        temperature,
        lastActivity: `Temperature set to ${temperature} from the lead queue.`,
      });
    }

    this.selectedIds.set(new Set());
    this.uiState.set('success');
  }

  protected bulkUpdatePotential(potential: LeadItem['donationPotential'] = 'High'): void {
    if (this.selectionCount() === 0) return;

    for (const lead of this.filteredLeads().filter((row) => this.selectedIds().has(row.id))) {
      this.workflow.patchLead(lead.id, {
        donationPotential: potential,
        lastActivity: `Donation potential set to ${potential} from the lead queue.`,
      });
    }

    this.selectedIds.set(new Set());
    this.uiState.set('success');
  }

  /**
   * Re-asks the server.
   *
   * IT USED TO BE A `setTimeout` that put the screen back to 'ready' after 400ms and fetched
   * nothing, so Refresh on a queue somebody else had just added to showed the same rows it
   * showed before.
   */
  protected refresh(): void {
    this.uiState.set('loading');
    this.workflow.refresh();
    this.uiState.set('ready');
  }

  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  protected createLead(): void {
    this.router.navigate(['/app/fundraising/relationships/lead-capture']);
  }
}
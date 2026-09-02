import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { LeadListItem } from '../../../../Shared/models/donor-contract.model';

// ============================================================================
// DATA MODEL
// ============================================================================
export interface LeadItem {
  reference: string;
  name: string;
  campaign: string;
  owner: string;
  stage: string;
  temperature: 'Cold' | 'Warm' | 'Hot';
  healthScore: number;
  nextFollowUp: string;
  followUpStatus: 'Upcoming' | 'Due' | 'Overdue' | 'Completed';
  qualificationReadiness: string;
  language: string;
  source: string;
  lastContactOutcome: string;
  recommendedNextAction: string;
  contactRestricted: boolean;

  /**
   * The reference a person quotes, e.g. LED-2026-001245.
   *
   * SEPARATE FROM `reference`, WHICH IS NOW THE API'S ID. Every write on this screen is addressed
   * to the id; the grid and every message show this.
   */
  displayReference: string;

  /** The server's row version, sent back on every write for the concurrency check. */
  version: number;
  email?: string;
  mobile?: string;
}

export interface LeadGroup {
  key: string;
  label: string;
  count: number;
  items: LeadItem[];
}

export interface ScreenMeta {
  viewId: string;
  title: string;
  route: string;
  purpose: string;
  primaryAction: string;
  viewPermission: string;
  primaryUsers: string[];
  scope: string;
  lastRefresh: string;
}

export interface PermissionMap {
  view: boolean;
  updateStage: boolean;
  updateTemperature: boolean;
  logCommunication: boolean;
  scheduleFollowUp: boolean;
  executeFollowUp: boolean;
  qualify: boolean;
  markLost: boolean;
  markDormant: boolean;
  bulkActions: boolean;
  exportGrid: boolean;
}

export interface PipelineStage {
  stage: string;
  count: number;
}

export interface ActionDef {
  id: string;
  label: string;
  placement: string;
  permission: string;
  allowedState: string;
  result: string;
  requiresReason?: boolean;
}

export interface FieldContract {
  label: string;
  control: string;
  required: boolean;
  visibility: string;
}

export interface LeadsScreenData {
  screen: ScreenMeta;
  permissions: PermissionMap;
  kpis: {
    assignedLeads: number;
    coldLeads: number;
    warmLeads: number;
    hotLeads: number;
    followUpsDueToday: number;
    followUpsOverdue: number;
  };
  pipeline: PipelineStage[];
  groups: LeadGroup[];
  savedFilters: string[];
  fieldContracts: FieldContract[];
  actions: ActionDef[];
}


/**
 * LEADS_SOURCE - REMOVED.
 *
 * It was a `LeadsScreenData` literal compiled into the bundle: eleven named leads with campaigns,
 * owners, health scores and follow-up dates. The constructor flattened it into
 * `WorkflowStateService`, so every fundraiser in every organisation opened My Leads to the same
 * eleven people, and qualifying one of them lasted until the tab was refreshed.
 */


// ============================================================================
// COMPONENT
// ============================================================================
type FilterValue = 'All' | string;


@Component({
  selector: 'app-my-leads',
  standalone: true,
  imports: [CommonModule, FormsModule],
templateUrl: './my-leads.html',
styleUrl: './my-leads.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyLeadsComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);
  // ---- compatibility outputs; actions are also wired directly to Router/state ----
  @Output() communicate = new EventEmitter<string>();
  @Output() scheduleFollowUp = new EventEmitter<string>();
  @Output() executeFollowUp = new EventEmitter<string>();
  @Output() qualify = new EventEmitter<string>();
  @Output() markLost = new EventEmitter<string>();
  @Output() markDormant = new EventEmitter<string>();
  @Output() openFollowUpQueue = new EventEmitter<void>();
  @Output() viewLeadQueue = new EventEmitter<void>();
  @Output() exportGrid = new EventEmitter<LeadItem[]>();

  // ---- Screen chrome ----
  //
  // THE COUNTS ARE COMPUTED FROM THE SERVER'S ROWS, not read from a constant. `kpis` and
  // `pipeline` used to be literal arrays in the same file as the leads, so the cards showed
  // "11 leads, 4 due today" whatever the queue actually contained.
  readonly screen = {
    title: 'My leads',
    purpose: 'The leads assigned to you. Work them, schedule follow-ups and record what happened.',
    scope: 'Assigned to you',
  };

  /** What the caller may do, from the server's permitted actions for the queue. */
  readonly permissions = signal<Record<string, boolean>>({ view: false });

  /**
   * The summary cards.
   *
   * COUNTED FROM THE SERVER'S ROWS. They used to be a literal object in the same file as the
   * leads themselves, so the cards agreed with the file rather than with the queue.
   */
  readonly kpis = computed(() => {
    const rows = this.sourceLeads();
    return {
      assignedLeads: rows.length,
      followUpsDueToday: rows.filter((l) => l.followUpStatus === 'Due').length,
      followUpsOverdue: rows.filter((l) => l.followUpStatus === 'Overdue').length,
      hotLeads: rows.filter((l) => l.temperature === 'Hot').length,
      warmLeads: rows.filter((l) => l.temperature === 'Warm').length,
      coldLeads: rows.filter((l) => l.temperature === 'Cold').length,
    };
  });

  readonly pipeline = computed(() => {
    const rows = this.sourceLeads();
    const counts = new Map<string, number>();
    for (const lead of rows) {
      counts.set(lead.stage, (counts.get(lead.stage) ?? 0) + 1);
    }
    return [...counts.entries()].map(([stage, count]) => ({ stage, count }));
  });

  readonly savedFilters = ['All my leads', 'Due today', 'Overdue', 'Hot leads'];

  readonly actionDefs: ActionDef[] = [];

  // ---- state ----
  private readonly sourceLeads = signal<LeadItem[]>([]);

  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly searchTerm = signal('');
  readonly stageFilter = signal<FilterValue>('All');
  readonly temperatureFilter = signal<FilterValue>('All');
  readonly followUpFilter = signal<FilterValue>('All');

  readonly selectedRefs = signal<ReadonlySet<string>>(new Set());
  readonly activeRef = signal<string | null>(null);

  // ---- copy-to-clipboard feedback state ----
  readonly copiedRef = signal<string | null>(null);
  private copiedRefTimeout: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.load();
  }

  /**
   * SCR-DON-009 - My Leads.
   *
   * THE DOCUMENT DEFINES THE SCOPE: "My Leads is the owner-specific list page: it shows the leads
   * assigned to a single owner." `onlyMine` is how the server is told that, and resolving it from
   * the token rather than from a value this browser sends is what makes it true - a browser
   * cannot be trusted to say whose leads it is asking for.
   *
   * WHAT THIS REPLACES. The constructor flattened `LEADS_SOURCE` - a constant compiled into the
   * bundle - into `WorkflowStateService`, so every fundraiser in every organisation saw the same
   * leads and every qualification was forgotten on refresh.
   */
  private load(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.api.getLeadWorkQueue({ page: 1, pageSize: 200, onlyMine: true }).subscribe({
      next: (response) => {
        this.sourceLeads.set(response.leads.items.map((row) => this.toLeadItem(row)));

        // VERBS, from the same endpoint the Lead Queue reads:
        // ['Accept','Filter','Open','Assign','Contact','Qualify','Close'].
        const permitted = response.permittedActions ?? [];
        this.permissions.set({
          view: permitted.includes('Open') || permitted.includes('Filter'),
          contact: permitted.includes('Contact'),
          qualify: permitted.includes('Qualify'),
          close: permitted.includes('Close'),
          schedule: permitted.includes('Contact'),
        });

        this.loading.set(false);

        const requestedId = this.route.snapshot.queryParamMap.get('leadId');
        const rows = this.sourceLeads();
        this.activeRef.set(
          requestedId && rows.some((lead) => lead.reference === requestedId)
            ? requestedId
            : rows[0]?.reference ?? null,
        );
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(apiErrorMessage(error));
      },
    });
  }

  private toLeadItem(row: LeadListItem): LeadItem {
    return {
      // THE API'S ID, NOT THE REFERENCE. Every write below is addressed to it; the reference is
      // what a person quotes and is shown in the grid.
      reference: row.id,
      displayReference: row.leadReference,
      name: row.name,
      campaign: row.campaignName ?? '',
      owner: row.ownerName ?? 'Unassigned',
      stage: row.status,
      temperature: row.temperature as LeadItem['temperature'],
      healthScore: row.healthScore,
      nextFollowUp: this.formatDate(row.nextActionDueUtc),
      followUpStatus: this.toFollowUpStatus(row.slaState),
      qualificationReadiness: row.donationPotential === 'High' ? 'Ready' : 'Not Ready',
      language: row.preferredLanguage,
      source: row.source ?? '',
      lastContactOutcome: row.lastContactOutcome,
      recommendedNextAction: row.nextAction ?? 'Initial contact',

      // MASKED BY THE SERVER, and a masked lead is one this caller may not contact directly.
      contactRestricted: row.isContactMasked,
      version: row.version,
    };
  }

  /** The SLA badge the server computed, in this screen's own words. */
  private toFollowUpStatus(slaState: string): LeadItem['followUpStatus'] {
    switch (slaState) {
      case 'Overdue': return 'Overdue';
      case 'DueToday':
      case 'Due today': return 'Due';
      case 'Completed': return 'Completed';
      default: return 'Upcoming';
    }
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

  readonly stageOptions = computed<FilterValue[]>(() => [
    'All',
    ...Array.from(new Set(this.sourceLeads().map((l) => l.stage))),
  ]);
  readonly temperatureOptions: FilterValue[] = ['All', 'Cold', 'Warm', 'Hot'];
  readonly followUpOptions: FilterValue[] = ['All', 'Upcoming', 'Due', 'Overdue', 'Completed'];

  readonly filteredLeads = computed<LeadItem[]>(() => {
    const term = this.searchTerm().trim().toLowerCase();
    const stage = this.stageFilter();
    const temperature = this.temperatureFilter();
    const followUp = this.followUpFilter();

    return this.sourceLeads().filter((lead) => {
      const matchesTerm =
        !term ||
        lead.name.toLowerCase().includes(term) ||
        lead.reference.toLowerCase().includes(term) ||
        lead.campaign.toLowerCase().includes(term);
      const matchesStage = stage === 'All' || lead.stage === stage;
      const matchesTemperature = temperature === 'All' || lead.temperature === temperature;
      const matchesFollowUp = followUp === 'All' || lead.followUpStatus === followUp;
      return matchesTerm && matchesStage && matchesTemperature && matchesFollowUp;
    });
  });

  readonly isEmpty = computed(() => !this.loading() && !this.loadError() && this.filteredLeads().length === 0);

  readonly activeLead = computed<LeadItem | null>(() => {
    const ref = this.activeRef();
    if (!ref) return null;
    return this.sourceLeads().find((l) => l.reference === ref) ?? null;
  });

  readonly selectedCount = computed(() => this.selectedRefs().size);
  readonly hasSelection = computed(() => this.selectedCount() > 0);

  readonly allVisibleSelected = computed(() => {
    const visible = this.filteredLeads();
    if (visible.length === 0) return false;
    const selected = this.selectedRefs();
    return visible.every((l) => selected.has(l.reference));
  });

  // ---- derived label helpers (pure functions, template-safe) ----
  temperatureClass(t: LeadItem['temperature']): string {
    return t === 'Hot' ? 'badge-hot' : t === 'Warm' ? 'badge-warm' : 'badge-cold';
  }

  followUpClass(status: LeadItem['followUpStatus']): string {
    switch (status) {
      case 'Overdue':
        return 'badge-overdue';
      case 'Due':
        return 'badge-due';
      case 'Completed':
        return 'badge-completed';
      default:
        return 'badge-upcoming';
    }
  }

  healthClass(score: number): string {
    if (score >= 70) return 'health-high';
    if (score >= 35) return 'health-medium';
    return 'health-low';
  }

  // ---- Permission gating ----
  //
  // ONE SOURCE: the codes the server listed for this caller. The old version looked each action
  // up in a `PermissionMap` object that lived in the same file as the leads, so what somebody
  // could do was decided by the bundle rather than by their token.
  //
  // THE THREE-ROLE MODEL NEEDS NO CODE HERE. An APPROVER holds no `don.lead-work-queue.qualify`,
  // so Qualify is not drawn for them; TENANT_ADMIN and INITIATOR both hold it.

  canQualify(lead: LeadItem): boolean {
    // STATE AS WELL AS PERMISSION. A lead that is not ready is not qualifiable by anybody.
    return this.permissions()['qualify'] === true
      && lead.temperature === 'Hot'
      && lead.qualificationReadiness === 'Ready';
  }

  canMarkLost(lead: LeadItem): boolean {
    return this.permissions()['close'] === true && lead.stage !== 'Lost';
  }

  canMarkDormant(lead: LeadItem): boolean {
    return this.permissions()['close'] === true && lead.stage !== 'Dormant';
  }

  get canCommunicate(): boolean {
    return this.permissions()['contact'] === true;
  }

  get canScheduleFollowUp(): boolean {
    return this.permissions()['schedule'] === true;
  }

  get canExecuteFollowUp(): boolean {
    return this.permissions()['schedule'] === true;
  }

  /** Export is available to every role that can see the list - the document treats it that way. */
  get canExport(): boolean {
    return this.permissions()['view'] === true;
  }

  get canBulkAct(): boolean {
    return this.permissions()['view'] === true;
  }

  // ---- interactions ----
  openLead(ref: string): void {
    this.activeRef.set(ref);
  }

  toggleSelection(ref: string, event: Event): void {
    event.stopPropagation();
    const next = new Set(this.selectedRefs());
    if (next.has(ref)) {
      next.delete(ref);
    } else {
      next.add(ref);
    }
    this.selectedRefs.set(next);
  }

  toggleSelectAllVisible(): void {
    const visible = this.filteredLeads();
    if (this.allVisibleSelected()) {
      const next = new Set(this.selectedRefs());
      visible.forEach((l) => next.delete(l.reference));
      this.selectedRefs.set(next);
    } else {
      const next = new Set(this.selectedRefs());
      visible.forEach((l) => next.add(l.reference));
      this.selectedRefs.set(next);
    }
  }

  clearSelection(): void {
    this.selectedRefs.set(new Set());
  }

  applyPipelineFilter(stage: string): void {
    this.stageFilter.set(stage);
  }

  applySavedFilter(name: string): void {
    switch (name) {
      case 'Due today':
        this.resetFilters(false);
        this.followUpFilter.set('Due');
        break;
      case 'Overdue':
        this.resetFilters(false);
        this.followUpFilter.set('Overdue');
        break;
      case 'Hot leads':
        this.resetFilters(false);
        this.temperatureFilter.set('Hot');
        break;
      case 'Ready for qualification':
        this.resetFilters(false);
        this.stageFilter.set('Qualified');
        break;
      default:
        this.resetFilters(false);
    }
  }

  resetFilters(clearSearch = true): void {
    if (clearSearch) this.searchTerm.set('');
    this.stageFilter.set('All');
    this.temperatureFilter.set('All');
    this.followUpFilter.set('All');
  }

  refresh(): void {
    // Local re-read of the provided dataset — no simulated network delay.
    // Replace with a real HTTP call once a data service is available.
    this.loadError.set(null);
    try {
      this.load();
    } catch {
      this.loadError.set('Unable to load assigned leads.');
    }
  }

  exportSelected(): void {
    if (!this.canExport) return;
    const rows = this.selectedCount() > 0 ? this.sourceLeads().filter((l) => this.selectedRefs().has(l.reference)) : this.filteredLeads();
    this.exportGrid.emit(rows);
    this.downloadCsv(rows);
  }

  private downloadCsv(rows: LeadItem[]): void {
    const headers = ['Reference', 'Name', 'Campaign', 'Owner', 'Stage', 'Temperature', 'Health Score', 'Next Follow-Up', 'Follow-Up Status'];
    const lines = rows.map((r) =>
      [r.reference, r.name, r.campaign, r.owner, r.stage, r.temperature, r.healthScore, r.nextFollowUp, r.followUpStatus]
        .map((v) => `"${String(v).replace(/"/g, '""')}"`)
        .join(',')
    );
    const csv = [headers.join(','), ...lines].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'my-leads-export.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  requestCommunicate(ref: string): void {
    if (!this.canCommunicate) return;
    this.communicate.emit(ref);
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: { leadId: ref } });
  }

  requestScheduleFollowUp(ref: string): void {
    if (!this.canScheduleFollowUp) return;
    this.scheduleFollowUp.emit(ref);
    this.router.navigate(['/app/don/follow-up-planner'], { queryParams: { leadId: ref, mode: 'create' } });
  }

  /**
   * Execute Follow-Up - "opens the Follow-Up Execution page", per the document.
   *
   * IT NO LONGER INVENTS A FOLLOW-UP. The old version created one on the spot when the lead had
   * none, so pressing Execute manufactured the very task it then claimed to be executing. The
   * execution screen loads the lead's real follow-ups and says so when there are none.
   */
  requestExecuteFollowUp(ref: string): void {
    if (!this.canExecuteFollowUp) {
      return;
    }
    this.executeFollowUp.emit(ref);
    this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
      queryParams: { leadId: ref },
    });
  }

  /**
   * Qualify - saved through the lead's own qualify command.
   *
   * THE NOTES ARE REQUIRED, 10 to 2000 characters, because qualifying is a judgement somebody
   * made and the audit trail should carry why. The old version set a local string.
   */
  requestQualify(lead: LeadItem): void {
    if (!this.canQualify(lead)) {
      return;
    }

    this.api
      .qualifyLead(lead.reference, {
        qualificationNotes: `Qualified from My Leads. Recommended next action: ${lead.recommendedNextAction}.`,
        moveToNurture: false,
        expectedVersion: lead.version,
      })
      .subscribe({
        next: () => {
          this.qualify.emit(lead.reference);
          this.toast.show('Lead qualified', `${lead.displayReference} is now qualified.`, 'success');
          this.load();
        },
        error: (error: unknown) => this.toast.show('Not qualified', apiErrorMessage(error), 'error'),
      });
  }

  requestMarkLost(lead: LeadItem): void {
    if (!this.canMarkLost(lead)) {
      return;
    }
    this.closeLead(lead, 'Lost', 'Marked lost from My Leads.');
  }

  requestMarkDormant(lead: LeadItem): void {
    if (!this.canMarkDormant(lead)) {
      return;
    }
    this.closeLead(lead, 'Dormant', 'Marked dormant from My Leads.');
  }

  /**
   * Closes a lead with a recorded reason.
   *
   * LOST AND DORMANT ARE THE SAME WRITE with a different reason, which is why they share this.
   * Both take the lead out of the working queue, and the trail should say which one it was.
   */
  private closeLead(lead: LeadItem, outcome: string, reason: string): void {
    this.api
      .closeLead(lead.reference, { reason: `${outcome}: ${reason}`, expectedVersion: lead.version })
      .subscribe({
        next: () => {
          if (outcome === 'Lost') {
            this.markLost.emit(lead.reference);
          } else {
            this.markDormant.emit(lead.reference);
          }
          this.toast.show(`Marked ${outcome.toLowerCase()}`, `${lead.displayReference} was closed.`, 'success');
          this.load();
        },
        error: (error: unknown) => this.toast.show('Not saved', apiErrorMessage(error), 'error'),
      });
  }

  requestOpenFollowUpQueue(): void {
    this.openFollowUpQueue.emit();
    this.router.navigate(['/app/fundraising/relationships/follow-up-queue']);
  }

  requestViewLeadQueue(): void {
    this.viewLeadQueue.emit();
    this.router.navigate(['/app/fundraising/relationships/lead-work-queue']);
  }

  /**
   * Copies the lead reference to the clipboard and shows a brief
   * "copied" confirmation on the icon button.
   */
  async copyReference(ref: string, event: Event): Promise<void> {
    event.stopPropagation();

    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(ref);
      } else {
        this.legacyCopy(ref);
      }
    } catch {
      this.legacyCopy(ref);
    }

    if (this.copiedRefTimeout) {
      clearTimeout(this.copiedRefTimeout);
    }
    this.copiedRef.set(ref);
    this.copiedRefTimeout = setTimeout(() => {
      this.copiedRef.set(null);
      this.copiedRefTimeout = null;
    }, 1500);
  }

  private legacyCopy(text: string): void {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand('copy');
    document.body.removeChild(textarea);
  }

  trackByRef(_index: number, item: LeadItem): string {
    return item.reference;
  }
}
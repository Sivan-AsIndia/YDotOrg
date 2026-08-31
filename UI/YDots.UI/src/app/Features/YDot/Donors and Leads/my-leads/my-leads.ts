import { ChangeDetectionStrategy, Component, EventEmitter, Output, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { WorkflowStateService } from '../../../../Service/workflow-state.service';

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
  /** Contact identity — used for payment → lead matching (WorkflowStateService). */
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


export const LEADS_SOURCE: LeadsScreenData = {
  "screen": {
    "viewId": "SCR-DON-005",
    "title": "My leads",
    "route": "/fundraising/relationships/my-leads",
    "purpose": "Work assigned leads through to qualification: prioritise, engage and track follow-ups.",
    "primaryAction": "Communicate",
    "viewPermission": "don.my-leads.view",
    "primaryUsers": [
      "Fundraiser",
      "Team Lead"
    ],
    "scope": "YDot Foundation · Tamil Nadu · My assignment",
    "lastRefresh": "01 Aug 2026, 02:10 PM · IST"
  },
  "permissions": {
    "view": true,
    "updateStage": true,
    "updateTemperature": true,
    "logCommunication": true,
    "scheduleFollowUp": true,
    "executeFollowUp": true,
    "qualify": true,
    "markLost": true,
    "markDormant": true,
    "bulkActions": true,
    "exportGrid": true
  },
  "kpis": {
    "assignedLeads": 11,
    "coldLeads": 6,
    "warmLeads": 4,
    "hotLeads": 1,
    "followUpsDueToday": 3,
    "followUpsOverdue": 2
  },
  "pipeline": [
    {
      "stage": "Assigned",
      "count": 4
    },
    {
      "stage": "Contacted",
      "count": 2
    },
    {
      "stage": "Engaged",
      "count": 2
    },
    {
      "stage": "Warm",
      "count": 1
    },
    {
      "stage": "Hot",
      "count": 0
    },
    {
      "stage": "Qualified",
      "count": 1
    },
    {
      "stage": "Lost",
      "count": 0
    },
    {
      "stage": "Dormant",
      "count": 1
    }
  ],
  "groups": [
    {
      "key": "assigned",
      "label": "Assigned",
      "count": 4,
      "items": [
        {
          "reference": "LEAD-2026-0142",
          "name": "Ramesh Kumar",
          "campaign": "Educate a Child 2026",
          "owner": "Unassigned",
          "stage": "Assigned",
          "temperature": "Cold",
          "healthScore": 20,
          "nextFollowUp": "01 Aug 2026, 05:00 PM",
          "followUpStatus": "Upcoming",
          "qualificationReadiness": "Not Ready",
          "language": "Tamil",
          "source": "Website form",
          "lastContactOutcome": "No contact yet",
          "recommendedNextAction": "Initial contact",
          "contactRestricted": true
        },
        {
          "reference": "LEAD-2026-0143",
          "name": "Anitha S",
          "campaign": "Clean Water 2026",
          "owner": "Unassigned",
          "stage": "Assigned",
          "temperature": "Cold",
          "healthScore": 20,
          "nextFollowUp": "01 Aug 2026, 06:30 PM",
          "followUpStatus": "Upcoming",
          "qualificationReadiness": "Not Ready",
          "language": "English",
          "source": "Facebook",
          "lastContactOutcome": "No contact yet",
          "recommendedNextAction": "Initial contact",
          "contactRestricted": true
        },
        {
          "reference": "LEAD-2026-0144",
          "name": "Priya Venkatesh",
          "campaign": "Educate a Child 2026",
          "owner": "Unassigned",
          "stage": "Assigned",
          "temperature": "Cold",
          "healthScore": 20,
          "nextFollowUp": "02 Aug 2026, 10:00 AM",
          "followUpStatus": "Upcoming",
          "qualificationReadiness": "Not Ready",
          "language": "Tamil",
          "source": "Referral",
          "lastContactOutcome": "No contact yet",
          "recommendedNextAction": "Initial contact",
          "contactRestricted": true
        },
        {
          "reference": "LEAD-2026-0145",
          "name": "Mohammed Irfan",
          "campaign": "Health Camps 2026",
          "owner": "Unassigned",
          "stage": "Assigned",
          "temperature": "Cold",
          "healthScore": 20,
          "nextFollowUp": "02 Aug 2026, 11:30 AM",
          "followUpStatus": "Upcoming",
          "qualificationReadiness": "Not Ready",
          "language": "Urdu",
          "source": "Instagram",
          "lastContactOutcome": "No contact yet",
          "recommendedNextAction": "Initial contact",
          "contactRestricted": true
        }
      ]
    },
    {
      "key": "contacted",
      "label": "Contacted",
      "count": 2,
      "items": [
        {
          "reference": "LEAD-2026-0138",
          "name": "Sundar Rajan",
          "campaign": "Clean Water 2026",
          "owner": "Arun Kumar",
          "stage": "Contacted",
          "temperature": "Warm",
          "healthScore": 50,
          "nextFollowUp": "01 Aug 2026, 03:00 PM",
          "followUpStatus": "Due",
          "qualificationReadiness": "Partially Ready",
          "language": "Tamil",
          "source": "Website form",
          "lastContactOutcome": "Interested — call back",
          "recommendedNextAction": "Qualification call",
          "contactRestricted": true
        },
        {
          "reference": "LEAD-2026-0135",
          "name": "Lakshmi Narayanan",
          "campaign": "Clean Water 2026",
          "owner": "Neha Patel",
          "stage": "Contacted",
          "temperature": "Cold",
          "healthScore": 15,
          "nextFollowUp": "31 Jul 2026, 05:00 PM",
          "followUpStatus": "Overdue",
          "qualificationReadiness": "Not Ready",
          "language": "Tamil",
          "source": "Website form",
          "lastContactOutcome": "No answer",
          "recommendedNextAction": "Follow-up call",
          "contactRestricted": true
        }
      ]
    },
    {
      "key": "engaged",
      "label": "Engaged",
      "count": 2,
      "items": [
        {
          "reference": "LEAD-2026-0139",
          "name": "Divya Bharathi",
          "campaign": "Educate a Child 2026",
          "owner": "Neha Patel",
          "stage": "Engaged",
          "temperature": "Warm",
          "healthScore": 60,
          "nextFollowUp": "01 Aug 2026, 04:00 PM",
          "followUpStatus": "Due",
          "qualificationReadiness": "Partially Ready",
          "language": "English",
          "source": "Event",
          "lastContactOutcome": "Requested brochure",
          "recommendedNextAction": "Send information pack",
          "contactRestricted": true
        },
        {
          "reference": "LEAD-2026-0136",
          "name": "Suresh Babu",
          "campaign": "Educate a Child 2026",
          "owner": "Arun Kumar",
          "stage": "Engaged",
          "temperature": "Warm",
          "healthScore": 40,
          "nextFollowUp": "31 Jul 2026, 03:30 PM",
          "followUpStatus": "Overdue",
          "qualificationReadiness": "Partially Ready",
          "language": "Telugu",
          "source": "Facebook",
          "lastContactOutcome": "Asked to call later",
          "recommendedNextAction": "Qualification call",
          "contactRestricted": true
        }
      ]
    },
    {
      "key": "warm",
      "label": "Warm",
      "count": 1,
      "items": [
        {
          "reference": "LEAD-2026-0130",
          "name": "Ravi Shankar",
          "campaign": "Health Camps 2026",
          "owner": "Arun Kumar",
          "stage": "Warm",
          "temperature": "Warm",
          "healthScore": 65,
          "nextFollowUp": "06 Aug 2026, 11:00 AM",
          "followUpStatus": "Upcoming",
          "qualificationReadiness": "Partially Ready",
          "language": "Kannada",
          "source": "Referral",
          "lastContactOutcome": "Interested later",
          "recommendedNextAction": "Nurture call",
          "contactRestricted": true
        }
      ]
    },
    {
      "key": "qualified",
      "label": "Qualified",
      "count": 1,
      "items": [
        {
          "reference": "LEAD-2026-0140",
          "name": "Karthik Raja",
          "campaign": "Health Camps 2026",
          "owner": "Arun Kumar",
          "stage": "Qualified",
          "temperature": "Hot",
          "healthScore": 100,
          "nextFollowUp": "01 Aug 2026, 05:30 PM",
          "followUpStatus": "Due",
          "qualificationReadiness": "Ready",
          "language": "Tamil",
          "source": "Referral",
          "lastContactOutcome": "Qualified — high interest",
          "recommendedNextAction": "Consent confirmation",
          "contactRestricted": true
        }
      ]
    },
    {
      "key": "dormant",
      "label": "Dormant",
      "count": 1,
      "items": [
        {
          "reference": "LEAD-2026-0129",
          "name": "Meena Kumari",
          "campaign": "Educate a Child 2026",
          "owner": "Neha Patel",
          "stage": "Dormant",
          "temperature": "Cold",
          "healthScore": 5,
          "nextFollowUp": "05 Aug 2026, 10:00 AM",
          "followUpStatus": "Upcoming",
          "qualificationReadiness": "Not Ready",
          "language": "Tamil",
          "source": "Event",
          "lastContactOutcome": "Not ready — nurture",
          "recommendedNextAction": "Nurture email",
          "contactRestricted": true
        }
      ]
    }
  ],
  "savedFilters": [
    "All leads (Default)",
    "Due today",
    "Overdue",
    "Hot leads",
    "Ready for qualification",
    "Action required"
  ],
  "fieldContracts": [
    {
      "label": "Lead reference",
      "control": "readonly",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Lead name",
      "control": "readonly",
      "required": false,
      "visibility": "Restricted"
    },
    {
      "label": "Mobile / email",
      "control": "telephone",
      "required": false,
      "visibility": "Restricted"
    },
    {
      "label": "Campaign",
      "control": "searchable-select",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Lead source",
      "control": "select",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Stage",
      "control": "select",
      "required": true,
      "visibility": "Internal"
    },
    {
      "label": "Temperature",
      "control": "select",
      "required": true,
      "visibility": "Internal"
    },
    {
      "label": "Lead health score",
      "control": "readonly",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Next follow-up",
      "control": "datetime",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Follow-up status",
      "control": "badge",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Qualification readiness",
      "control": "badge",
      "required": false,
      "visibility": "Internal"
    },
    {
      "label": "Last contact outcome",
      "control": "readonly",
      "required": false,
      "visibility": "Restricted"
    }
  ],
  "actions": [
    {
      "id": "open",
      "label": "Open",
      "placement": "grid-row",
      "permission": "don.my-leads.view",
      "allowedState": "Any",
      "result": "Open the lead quick preview drawer without page navigation."
    },
    {
      "id": "preview",
      "label": "Preview",
      "placement": "grid-row",
      "permission": "don.my-leads.view",
      "allowedState": "Any",
      "result": "Open the right-side preview drawer without page navigation."
    },
    {
      "id": "communicate",
      "label": "Communicate",
      "placement": "grid-row",
      "permission": "don.my-leads.logCommunication",
      "allowedState": "Any",
      "result": "Navigate to Communication Timeline, passing the lead ID."
    },
    {
      "id": "scheduleFollowUp",
      "label": "Schedule Follow-Up",
      "placement": "grid-row",
      "permission": "don.my-leads.scheduleFollowUp",
      "allowedState": "Any",
      "result": "Open Follow-Up Planner, passing the lead ID."
    },
    {
      "id": "executeFollowUp",
      "label": "Execute Follow-Up",
      "placement": "grid-row",
      "permission": "don.my-leads.executeFollowUp",
      "allowedState": "Any",
      "result": "Open Follow-Up Execution, passing the lead ID and follow-up ID."
    },
    {
      "id": "qualify",
      "label": "Qualify",
      "placement": "grid-row",
      "permission": "don.my-leads.qualify",
      "allowedState": "Temperature = Hot AND Qualification readiness = Ready",
      "result": "Open Lead Qualification, passing the lead ID."
    },
    {
      "id": "markLost",
      "label": "Mark Lost",
      "placement": "danger",
      "permission": "don.my-leads.markLost",
      "allowedState": "Compatible current state only",
      "result": "Set stage to Lost and preserve linked history.",
      "requiresReason": false
    },
    {
      "id": "markDormant",
      "label": "Mark Dormant",
      "placement": "danger",
      "permission": "don.my-leads.markDormant",
      "allowedState": "Compatible current state only",
      "result": "Set stage to Dormant and preserve linked history.",
      "requiresReason": false
    }
  ]
};


// ============================================================================
// COMPONENT
// ============================================================================
type FilterValue = 'All' | string;

const DATA = LEADS_SOURCE;

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
  private readonly workflow = inject(WorkflowStateService);
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

  // ---- static screen metadata from the source data ----
  readonly screen = DATA.screen;
  readonly permissions = DATA.permissions;
  readonly kpis = DATA.kpis;
  readonly pipeline = DATA.pipeline;
  readonly savedFilters = DATA.savedFilters;
  readonly actionDefs: ActionDef[] = DATA.actions;

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
    const seed = DATA.groups.flatMap((group) => group.items).map((lead) => ({
      id: lead.reference,
      name: lead.name,
      mobile: lead.mobile ?? '',
      email: lead.email ?? '',
      source: lead.source,
      campaign: lead.campaign,
      stage: lead.stage,
      temperature: lead.temperature,
      donationPotential: lead.temperature === 'Hot' ? 'High' as const : lead.temperature === 'Warm' ? 'Medium' as const : 'Low' as const,
      owner: lead.owner,
      lastActivity: lead.lastContactOutcome,
      nextFollowUp: lead.nextFollowUp,
      healthScore: lead.healthScore,
      healthReasons: [],
      lastContactOutcome: lead.lastContactOutcome,
      language: lead.language,
      createdAt: '2026-08-01T00:00:00.000Z',
      masked: false,
      converted: false,
      followUpStatus: lead.followUpStatus,
      qualificationReadiness: lead.qualificationReadiness,
      recommendedNextAction: lead.recommendedNextAction,
      contactRestricted: lead.contactRestricted,
    }));
    this.syncFromWorkflow();
    const requestedId = this.route.snapshot.queryParamMap.get('leadId');
    this.activeRef.set(requestedId && this.sourceLeads().some((lead) => lead.reference === requestedId)
      ? requestedId
      : this.sourceLeads()[0]?.reference ?? null);
  }

  private syncFromWorkflow(): void {
    const leads = this.workflow.leads()
      .filter((lead) => lead.owner !== 'Unassigned' || ['Assigned', 'Contacted', 'Engaged', 'Warm', 'Hot', 'Qualified', 'Dormant', 'Lost'].includes(lead.stage))
      .map((lead) => ({
        reference: lead.id,
        name: lead.name,
        campaign: lead.campaign,
        owner: lead.owner,
        stage: lead.stage,
        temperature: lead.temperature,
        healthScore: lead.healthScore,
        nextFollowUp: lead.nextFollowUp,
        followUpStatus: (lead.followUpStatus ?? 'Upcoming') as LeadItem['followUpStatus'],
        qualificationReadiness: lead.qualificationReadiness ?? 'Not Ready',
        language: lead.language,
        source: lead.source,
        lastContactOutcome: lead.lastContactOutcome,
        recommendedNextAction: lead.recommendedNextAction ?? 'Initial contact',
        contactRestricted: lead.contactRestricted ?? false,
      }));
    this.sourceLeads.set(leads);
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

  // ---- permission gating (reads the real permissions map, no invented logic) ----
  private permissionFor(action: ActionDef): boolean {
    const key = action.permission.split('.').pop() as keyof PermissionMap | undefined;
    if (!key || !(key in this.permissions)) return false;
    return this.permissions[key];
  }

  canQualify(lead: LeadItem): boolean {
    const def = this.actionDefs.find((a) => a.id === 'qualify');
    if (!def || !this.permissionFor(def)) return false;
    return lead.temperature === 'Hot' && lead.qualificationReadiness === 'Ready';
  }

  canMarkLost(lead: LeadItem): boolean {
    const def = this.actionDefs.find((a) => a.id === 'markLost');
    if (!def || !this.permissionFor(def)) return false;
    return lead.stage !== 'Lost';
  }

  canMarkDormant(lead: LeadItem): boolean {
    const def = this.actionDefs.find((a) => a.id === 'markDormant');
    if (!def || !this.permissionFor(def)) return false;
    return lead.stage !== 'Dormant';
  }

  get canCommunicate(): boolean {
    return this.permissions.logCommunication;
  }
  get canScheduleFollowUp(): boolean {
    return this.permissions.scheduleFollowUp;
  }
  get canExecuteFollowUp(): boolean {
    return this.permissions.executeFollowUp;
  }
  get canExport(): boolean {
    return this.permissions.exportGrid;
  }
  get canBulkAct(): boolean {
    return this.permissions.bulkActions;
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
      this.syncFromWorkflow();
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

  requestExecuteFollowUp(ref: string): void {
    if (!this.canExecuteFollowUp) return;
    this.executeFollowUp.emit(ref);
    let followUp = this.workflow.followUpsFor(ref).find((item) => item.status === 'Pending' || item.status === 'Rescheduled') ?? this.workflow.followUpsFor(ref)[0];
    if (!followUp) {
      const lead = this.workflow.getLead(ref);
      followUp = this.workflow.addFollowUp({
        recordId: ref,
        recordName: lead?.name,
        assignedTo: lead?.owner,
        campaign: lead?.campaign,
        phone: lead?.mobile,
        email: lead?.email,
        purpose: lead?.recommendedNextAction ?? 'Relationship follow-up',
      });
    }
    this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
      queryParams: { leadId: ref, followUpId: followUp.id },
    });
  }

  requestQualify(lead: LeadItem): void {
    if (!this.canQualify(lead)) return;
    this.workflow.patchLead(lead.reference, { stage: 'Qualified', qualificationReadiness: 'Ready', lastActivity: 'Qualification completed' });
    this.syncFromWorkflow();
    this.qualify.emit(lead.reference);
    this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { leadId: lead.reference, conversion: 'pending' } });
  }

  requestMarkLost(lead: LeadItem): void {
    if (!this.canMarkLost(lead)) return;
    this.workflow.patchLead(lead.reference, { stage: 'Lost', lastActivity: 'Marked lost' });
    this.syncFromWorkflow();
    this.markLost.emit(lead.reference);
  }

  requestMarkDormant(lead: LeadItem): void {
    if (!this.canMarkDormant(lead)) return;
    this.workflow.patchLead(lead.reference, { stage: 'Dormant', lastActivity: 'Marked dormant' });
    this.syncFromWorkflow();
    this.markDormant.emit(lead.reference);
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
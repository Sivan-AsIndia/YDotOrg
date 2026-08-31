import {
  Component,
  ElementRef,
  HostListener,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { WorkflowLead, WorkflowStateService } from '../../../../Service/workflow-state.service';

const searchDataset = {
  meta: {
    breadcrumb: [
      "Fundraising",
      "Global Search"
    ],
    title: "Universal Search",
    subtitle: "Search across all fundraising records, documents and activities.",
    owner: "Firstlin S Joseph · Donor Care",
    lastRefresh: "21 Aug 2026, 05:10 PM · IST"
  },
  kpis: [
    {
      id: "total",
      label: "Total Leads",
      value: 11,
      hint: "In selected scope",
      icon: "bookmark",
      tone: "neutral"
    },
    {
      id: "unassigned",
      label: "Unassigned Leads",
      value: 4,
      hint: "In selected scope",
      icon: "refresh",
      tone: "info"
    },
    {
      id: "hot",
      label: "Hot Leads",
      value: 3,
      hint: "In selected scope",
      icon: "flame",
      tone: "danger"
    },
    {
      id: "highPotential",
      label: "High Donation Potential",
      value: 4,
      hint: "In selected scope",
      icon: "clock",
      tone: "warning"
    }
  ],
  quickFilters: [
    "All",
    "Leads",
    "Donors",
    "Donations",
    "Communications",
    "Follow-Ups",
    "Notes",
    "Attachments"
  ],
  savedSearches: [
    {
      name: "Hot Leads",
      query: "",
      filter: "temperature:Hot"
    },
    {
      name: "High Donation Potential",
      query: "",
      filter: "potential:High"
    },
    {
      name: "Overdue Follow-Ups",
      query: "",
      filter: "stage:Assigned,Contacted"
    },
    {
      name: "Recently Converted",
      query: "",
      filter: "stage:Converted"
    }
  ],
  recentSearches: [
    "Arun Kumar",
    "LED-2026-001238",
    "Clean Water 2026",
    "Ravi Shankar"
  ],
  suggestedSearches: [
    {
      title: "Recently Converted Leads",
      hint: "1 result"
    },
    {
      title: "High Potential Leads",
      hint: "4 results"
    },
    {
      title: "Upcoming Follow-Ups",
      hint: "Due today"
    },
    {
      title: "Unassigned Leads",
      hint: "4 results"
    }
  ],
  facets: {
    stages: [
      "New",
      "Assigned",
      "Contacted",
      "Engaged",
      "Dormant",
      "Lost",
      "Converted"
    ],
    temperatures: [
      "Cold",
      "Warm",
      "Hot"
    ],
    potentials: [
      "Low",
      "Medium",
      "High"
    ],
    campaigns: [
      "Educate a Child 2026",
      "Clean Water 2026",
      "Health Camps 2026"
    ]
  },
  records: [
    {
      id: "LED-2026-001245",
      type: "Lead",
      name: "Ramesh Kumar",
      mobile: "+91 98765 43210",
      email: "ramesh.k@example.com",
      source: "Website",
      campaign: "Educate a Child 2026",
      stage: "New",
      temperature: "Warm",
      potential: "Medium",
      owner: "Unassigned",
      lastActivity: "21 Aug 2026, 02:15 PM",
      nextFollowUp: "22 Aug 2026",
      healthScore: 68,
      healthReasons: [
        "Recent activity",
        "Source verified"
      ],
      lastContactOutcome: "No contact yet",
      language: "Tamil",
      createdAt: "21 Aug 2026, 01:40 PM",
      converted: false
    },
    {
      id: "LED-2026-001246",
      type: "Lead",
      name: "Anitha S",
      mobile: "+91 91234 56789",
      email: "anitha.s@example.com",
      source: "Event",
      campaign: "Clean Water 2026",
      stage: "New",
      temperature: "Hot",
      potential: "High",
      owner: "Unassigned",
      lastActivity: "21 Aug 2026, 11:20 AM",
      nextFollowUp: "21 Aug 2026",
      healthScore: 82,
      healthReasons: [
        "Communication exists",
        "Follow-ups completed",
        "Recent activity"
      ],
      lastContactOutcome: "No contact yet",
      language: "English",
      createdAt: "21 Aug 2026, 10:05 AM",
      converted: false
    },
    {
      id: "LED-2026-001247",
      type: "Lead",
      name: "Priya Venkatesh",
      mobile: "+91 99887 76655",
      email: "priya.v@example.com",
      source: "Referral",
      campaign: "Educate a Child 2026",
      stage: "New",
      temperature: "Cold",
      potential: "Low",
      owner: "Unassigned",
      lastActivity: "20 Aug 2026, 04:30 PM",
      nextFollowUp: "23 Aug 2026",
      healthScore: 45,
      healthReasons: [
        "Source verified"
      ],
      lastContactOutcome: "No contact yet",
      language: "Tamil",
      createdAt: "20 Aug 2026, 03:10 PM",
      converted: false
    },
    {
      id: "LED-2026-001248",
      type: "Lead",
      name: "Mohammed Irfan",
      mobile: "+91 97654 32109",
      email: "m.irfan@example.com",
      source: "Bulk Upload",
      campaign: "Health Camps 2026",
      stage: "New",
      temperature: "Warm",
      potential: "High",
      owner: "Unassigned",
      lastActivity: "20 Aug 2026, 09:45 AM",
      nextFollowUp: "22 Aug 2026",
      healthScore: 71,
      healthReasons: [
        "Recent activity",
        "Source verified"
      ],
      lastContactOutcome: "No contact yet",
      language: "Urdu",
      createdAt: "20 Aug 2026, 09:00 AM",
      converted: false
    },
    {
      id: "LED-2026-001238",
      type: "Lead",
      name: "Sundar Rajan",
      mobile: "+91 94444 12345",
      email: "sundar.r@example.com",
      source: "Website",
      campaign: "Clean Water 2026",
      stage: "Assigned",
      temperature: "Hot",
      potential: "High",
      owner: "Arun Kumar",
      lastActivity: "21 Aug 2026, 03:00 PM",
      nextFollowUp: "21 Aug 2026",
      healthScore: 88,
      healthReasons: [
        "Communication exists",
        "Follow-ups completed",
        "Recent activity"
      ],
      lastContactOutcome: "Interested — call back",
      language: "Tamil",
      createdAt: "18 Aug 2026, 11:20 AM",
      converted: false
    },
    {
      id: "LED-2026-001239",
      type: "Lead",
      name: "Divya Bharathi",
      mobile: "+91 93333 98765",
      email: "divya.b@example.com",
      source: "Event",
      campaign: "Educate a Child 2026",
      stage: "Contacted",
      temperature: "Warm",
      potential: "Medium",
      owner: "Neha Patel",
      lastActivity: "21 Aug 2026, 01:15 PM",
      nextFollowUp: "22 Aug 2026",
      healthScore: 76,
      healthReasons: [
        "Communication exists",
        "Recent activity"
      ],
      lastContactOutcome: "Requested brochure",
      language: "English",
      createdAt: "17 Aug 2026, 02:40 PM",
      converted: false
    },
    {
      id: "LED-2026-001240",
      type: "Lead",
      name: "Karthik Raja",
      mobile: "+91 92222 45678",
      email: "karthik.r@example.com",
      source: "Referral",
      campaign: "Health Camps 2026",
      stage: "Engaged",
      temperature: "Hot",
      potential: "High",
      owner: "Arun Kumar",
      lastActivity: "20 Aug 2026, 05:30 PM",
      nextFollowUp: "21 Aug 2026",
      healthScore: 91,
      healthReasons: [
        "Communication exists",
        "Follow-ups completed",
        "Recent activity"
      ],
      lastContactOutcome: "High interest expressed",
      language: "Tamil",
      createdAt: "15 Aug 2026, 10:00 AM",
      converted: false
    },
    {
      id: "LED-2026-001235",
      type: "Lead",
      name: "Lakshmi Narayanan",
      mobile: "+91 91111 23456",
      email: "lakshmi.n@example.com",
      source: "Website",
      campaign: "Clean Water 2026",
      stage: "Assigned",
      temperature: "Cold",
      potential: "Low",
      owner: "Neha Patel",
      lastActivity: "19 Aug 2026, 04:00 PM",
      nextFollowUp: "24 Aug 2026",
      healthScore: 52,
      healthReasons: [
        "Source verified"
      ],
      lastContactOutcome: "No answer",
      language: "Tamil",
      createdAt: "14 Aug 2026, 03:25 PM",
      converted: false
    },
    {
      id: "LED-2026-001236",
      type: "Lead",
      name: "Suresh Babu",
      mobile: "+91 90000 87654",
      email: "suresh.b@example.com",
      source: "Partner NGO",
      campaign: "Educate a Child 2026",
      stage: "Contacted",
      temperature: "Warm",
      potential: "Medium",
      owner: "Arun Kumar",
      lastActivity: "18 Aug 2026, 11:45 AM",
      nextFollowUp: "25 Aug 2026",
      healthScore: 64,
      healthReasons: [
        "Communication exists"
      ],
      lastContactOutcome: "Asked to call later",
      language: "Telugu",
      createdAt: "12 Aug 2026, 09:30 AM",
      converted: false
    },
    {
      id: "LED-2026-001220",
      type: "Lead",
      name: "Meena Kumari",
      mobile: "+91 98888 11223",
      email: "meena.k@example.com",
      source: "Walk-In",
      campaign: "Educate a Child 2026",
      stage: "Lost",
      temperature: "Cold",
      potential: "Low",
      owner: "Neha Patel",
      lastActivity: "10 Aug 2026, 02:00 PM",
      nextFollowUp: "—",
      healthScore: 28,
      healthReasons: [],
      lastContactOutcome: "Not interested",
      language: "Tamil",
      createdAt: "05 Aug 2026, 11:00 AM",
      converted: false
    },
    {
      id: "LED-2026-001210",
      type: "Lead",
      name: "Ravi Shankar",
      mobile: "+91 97777 33445",
      email: "ravi.s@example.com",
      source: "Campaign",
      campaign: "Health Camps 2026",
      stage: "Converted",
      temperature: "Hot",
      potential: "High",
      owner: "Arun Kumar",
      lastActivity: "19 Aug 2026, 06:10 PM",
      nextFollowUp: "—",
      healthScore: 95,
      healthReasons: [
        "Communication exists",
        "Follow-ups completed",
        "Recent activity",
        "Donation recorded"
      ],
      lastContactOutcome: "First donation received",
      language: "Kannada",
      createdAt: "01 Aug 2026, 10:30 AM",
      converted: true,
      donorId: "DON-2026-004890"
    }
  ]
};

export type RecordType =
  | 'Lead'
  | 'Donor'
  | 'Donation'
  | 'Communication'
  | 'Follow-Up'
  | 'Note'
  | 'Attachment';

export interface UniversalRecord {
  id: string;
  type: RecordType;
  name: string;
  mobile: string;
  email: string;
  source: string;
  campaign: string;
  stage: string;
  temperature: 'Cold' | 'Warm' | 'Hot';
  potential: 'Low' | 'Medium' | 'High';
  owner: string;
  lastActivity: string;
  nextFollowUp: string;
  healthScore: number;
  healthReasons: string[];
  lastContactOutcome: string;
  language: string;
  createdAt: string;
  converted: boolean;
  donorId?: string;
}

export interface KpiCard {
  id: string;
  label: string;
  value: number;
  hint: string;
  icon: 'bookmark' | 'refresh' | 'flame' | 'clock';
  tone: 'neutral' | 'info' | 'danger' | 'warning';
}

export interface SavedSearch {
  name: string;
  query: string;
  filter: string;
}

export interface SuggestedSearch {
  title: string;
  hint: string;
}

interface FacetSelection {
  stage: string | null;
  temperature: string | null;
  potential: string | null;
  campaign: string | null;
}

@Component({
  selector: 'app-global-search',
  standalone: true,
  imports: [],
  templateUrl: './global-search.html',
  styleUrl: './global-search.css',
})
export class GlobalSearchComponent {
  private readonly router = inject(Router);
  private readonly workflow = inject(WorkflowStateService);

  @ViewChild('searchInput') private searchInputRef?: ElementRef<HTMLInputElement>;

  // ---- static / seed data --------------------------------------------
  protected readonly meta = searchDataset.meta;
  protected readonly kpis: KpiCard[] = searchDataset.kpis as KpiCard[];
  protected readonly quickFilters: string[] = searchDataset.quickFilters;
  protected readonly facetOptions = searchDataset.facets;
  protected readonly suggestedSearches: SuggestedSearch[] = searchDataset.suggestedSearches;

  private allRecords: UniversalRecord[] = searchDataset.records as UniversalRecord[];

  /** Real CRM leads from my-leads.json — the SAME source every other page
   *  (My Leads, Lead Work Queue, Follow-up Queue) seeds. Never seed the inline
   *  LED-2026 demo records into the shared state: they carry different ids and
   *  emails, so they used to appear as duplicate "dummy" leads in Lead Work
   *  Queue and never matched the donation→donor removal flow. */
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
  constructor() {
    // Seed the REAL leads — never the inline LED-2026 demo records — so this
    // page no longer injects dummy/duplicate leads into the shared state that
    // My Leads / Lead Work Queue render.

    const leadRecords: UniversalRecord[] = this.workflow.leads().map((lead) => ({
      id: lead.id,
      type: lead.converted ? 'Donor' : 'Lead',
      name: lead.name,
      mobile: lead.mobile,
      email: lead.email,
      source: lead.source,
      campaign: lead.campaign,
      stage: lead.converted ? 'Converted' : lead.stage,
      temperature: lead.temperature,
      potential: lead.donationPotential,
      owner: lead.owner,
      lastActivity: lead.lastActivity,
      nextFollowUp: lead.nextFollowUp,
      healthScore: lead.healthScore,
      healthReasons: [...lead.healthReasons],
      lastContactOutcome: lead.lastContactOutcome,
      language: lead.language,
      createdAt: lead.createdAt,
      converted: lead.converted,
      donorId: lead.donorId,
    }));
    const followUpRecords: UniversalRecord[] = this.workflow.followUps().map((followUp) => ({
      id: followUp.id,
      type: 'Follow-Up',
      name: followUp.recordName,
      mobile: followUp.phone,
      email: followUp.email,
      source: followUp.followUpType,
      campaign: followUp.campaign,
      stage: followUp.status,
      temperature: 'Cold',
      potential: 'Low',
      owner: followUp.assignedTo,
      lastActivity: followUp.history.at(-1)?.label ?? 'Follow-up created',
      nextFollowUp: `${followUp.scheduledDate} ${followUp.scheduledTime}`,
      healthScore: 50,
      healthReasons: [followUp.slaStatus],
      lastContactOutcome: followUp.lastCommunicationOutcome ?? 'Pending',
      language: this.workflow.getLead(followUp.recordId)?.language ?? '',
      createdAt: followUp.history[0]?.date ?? '',
      converted: followUp.recordType === 'Donor',
      donorId: followUp.recordType === 'Donor' ? followUp.recordId : undefined,
    }));
    const communicationRecords: UniversalRecord[] = this.workflow.communications().map((communication) => {
      const lead = this.workflow.getLead(communication.recordId);
      return {
        id: communication.id,
        type: 'Communication',
        name: lead?.name ?? communication.recordId,
        mobile: lead?.mobile ?? '',
        email: lead?.email ?? '',
        source: communication.type,
        campaign: lead?.campaign ?? '',
        stage: communication.outcome,
        temperature: lead?.temperature ?? 'Cold',
        potential: lead?.donationPotential ?? 'Low',
        owner: communication.createdBy,
        lastActivity: communication.summary,
        nextFollowUp: communication.followUpDate ?? lead?.nextFollowUp ?? '',
        healthScore: lead?.healthScore ?? 50,
        healthReasons: lead?.healthReasons ?? [],
        lastContactOutcome: communication.outcome,
        language: lead?.language ?? '',
        createdAt: communication.date,
        converted: lead?.converted ?? false,
        donorId: lead?.donorId,
      };
    });
    this.allRecords = [...followUpRecords, ...communicationRecords, ...leadRecords];
  }

  // ---- reactive state ---------------------------------------------------
  protected readonly query = signal('');
  protected readonly activeQuickFilter = signal<string>('All');
  protected readonly facets = signal<FacetSelection>({
    stage: null,
    temperature: null,
    potential: null,
    campaign: null,
  });
  protected readonly selectedRecordId = signal<string | null>(this.allRecords[0]?.id ?? null);
  protected readonly savedSearches = signal<SavedSearch[]>(searchDataset.savedSearches as SavedSearch[]);
  protected readonly recentSearches = signal<string[]>(searchDataset.recentSearches as string[]);
  protected readonly isFacetPanelOpen = signal(true);
  protected readonly isAdvancedSearchOpen = signal(false);
  protected readonly copiedField = signal<string | null>(null);

  // ---- derived state ------------------------------------------------
  protected readonly filteredRecords = computed<UniversalRecord[]>(() => {
    const q = this.query().trim().toLowerCase();
    const filter = this.activeQuickFilter();
    const f = this.facets();

    return this.allRecords
      .filter((r) => {
        if (filter === 'All') return true;
        const map: Record<string, RecordType> = { Leads: 'Lead', Donors: 'Donor', Donations: 'Donation', Communications: 'Communication', 'Follow-Ups': 'Follow-Up', Notes: 'Note', Attachments: 'Attachment' };
        return r.type === map[filter];
      })
      .filter((r) => (f.stage ? r.stage === f.stage : true))
      .filter((r) => (f.temperature ? r.temperature === f.temperature : true))
      .filter((r) => (f.potential ? r.potential === f.potential : true))
      .filter((r) => (f.campaign ? r.campaign === f.campaign : true))
      .filter((r) => {
        if (!q) return true;
        return (
          r.name.toLowerCase().includes(q) ||
          r.id.toLowerCase().includes(q) ||
          r.mobile.toLowerCase().includes(q) ||
          r.email.toLowerCase().includes(q) ||
          r.campaign.toLowerCase().includes(q) ||
          r.owner.toLowerCase().includes(q)
        );
      })
      .map((r) => ({ ...r, relevance: this.computeRelevance(r, q) }))
      .sort((a: any, b: any) => b.relevance - a.relevance);
  });

  protected readonly resultCount = computed(() => this.filteredRecords().length);

  protected readonly selectedRecord = computed<UniversalRecord | null>(() => {
    const id = this.selectedRecordId();
    return (
      this.filteredRecords().find((r) => r.id === id) ??
      this.filteredRecords()[0] ??
      null
    );
  });

  protected readonly insights = computed(() => {
    const results = this.filteredRecords();
    return {
      total: results.length,
      hot: results.filter((r) => r.temperature === 'Hot').length,
      highPotential: results.filter((r) => r.potential === 'High').length,
      unassigned: results.filter((r) => r.owner === 'Unassigned').length,
    };
  });

  protected readonly hasActiveFacets = computed(() => {
    const f = this.facets();
    return Boolean(f.stage || f.temperature || f.potential || f.campaign);
  });

  // ---- keyboard shortcut (Cmd/Ctrl + K) ---------------------------------
  @HostListener('window:keydown', ['$event'])
  protected handleGlobalKeydown(event: KeyboardEvent): void {
    const isShortcut = (event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k';
    if (isShortcut) {
      event.preventDefault();
      this.searchInputRef?.nativeElement.focus();
    }
    if (event.key === 'Escape' && document.activeElement === this.searchInputRef?.nativeElement) {
      this.clearQuery();
    }
  }

  // ---- interaction handlers ---------------------------------------------
  protected onQueryChange(value: string): void {
    this.query.set(value);
  }

  protected clearQuery(): void {
    this.query.set('');
  }

  protected setQuickFilter(name: string): void {
    this.activeQuickFilter.set(name);
  }

  protected toggleFacet(group: keyof FacetSelection, value: string): void {
    this.facets.update((current) => ({
      ...current,
      [group]: current[group] === value ? null : value,
    }));
  }

  protected clearFacets(): void {
    this.facets.set({ stage: null, temperature: null, potential: null, campaign: null });
  }

  protected toggleFacetPanel(): void {
    this.isFacetPanelOpen.update((open) => !open);
  }

  protected toggleAdvancedSearch(): void {
    this.isAdvancedSearchOpen.update((open) => !open);
  }

  protected selectRecord(id: string): void {
    this.selectedRecordId.set(id);
  }

  protected openRecord(record: UniversalRecord): void {
    if (record.type === 'Follow-Up') {
      const followUp = this.workflow.getFollowUp(record.id);
      this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], { queryParams: { followUpId: record.id, leadId: followUp?.recordType === 'Lead' ? followUp.recordId : null, donorId: followUp?.recordType === 'Donor' ? followUp.recordId : null } });
      return;
    }
    if (record.type === 'Communication') {
      const communication = this.workflow.communications().find((item) => item.id === record.id);
      const recordId = communication?.recordId ?? null;
      const isDonor = Boolean(recordId && this.workflow.getDonor(recordId));
      this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: { leadId: isDonor ? null : recordId, donorId: isDonor ? recordId : null, communicationId: record.id } });
      return;
    }
    const lead = this.workflow.getLead(record.id) ?? this.workflow.leads().find((item) => item.donorId === record.id);
    if (record.type === 'Donor' || record.converted) {
      this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { donorId: record.donorId ?? record.id } });
      return;
    }
    this.router.navigate(['/app/fundraising/relationships/my-leads'], { queryParams: { leadId: record.id } });
  }

  private recordContext(record: UniversalRecord): { leadId: string | null; donorId: string | null } {
    if (record.type === 'Follow-Up') {
      const followUp = this.workflow.getFollowUp(record.id);
      return {
        leadId: followUp?.recordType === 'Lead' ? followUp.recordId : null,
        donorId: followUp?.recordType === 'Donor' ? followUp.recordId : null,
      };
    }
    if (record.type === 'Communication') {
      const communication = this.workflow.communications().find((item) => item.id === record.id);
      const recordId = communication?.recordId ?? null;
      const isDonor = Boolean(recordId && this.workflow.getDonor(recordId));
      return { leadId: isDonor ? null : recordId, donorId: isDonor ? recordId : null };
    }
    if (record.type === 'Donor' || record.converted) {
      return { leadId: null, donorId: record.donorId ?? record.id };
    }
    return { leadId: record.id, donorId: null };
  }

  protected communicateSelected(): void {
    const record = this.selectedRecord();
    if (!record) return;
    const context = this.recordContext(record);
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], { queryParams: context });
  }

  protected scheduleSelected(): void {
    const record = this.selectedRecord();
    if (!record) return;
    const context = this.recordContext(record);
    this.router.navigate(['/app/don/follow-up-planner'], { queryParams: { ...context, mode: 'create' } });
  }

  protected openTimelineSelected(): void {
    this.communicateSelected();
  }

  protected runSavedSearch(search: SavedSearch): void {
    this.query.set(search.query);
    this.activeQuickFilter.set('All');
    this.recordRecentSearch(search.name);
  }

  protected runSuggestedSearch(item: SuggestedSearch): void {
    this.query.set(item.title.replace(/^(Recently|Upcoming)\s/, ''));
    this.recordRecentSearch(item.title);
  }

  protected runRecentSearch(term: string): void {
    this.query.set(term);
  }

  protected clearSearchHistory(): void {
    this.recentSearches.set([]);
  }

  protected saveCurrentSearch(): void {
    const q = this.query().trim();
    if (!q) return;
    this.savedSearches.update((list) => [{ name: q, query: q, filter: '' }, ...list]);
  }

  protected async copyToClipboard(value: string, field: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
      this.copiedField.set(field);
      setTimeout(() => this.copiedField.set(null), 1500);
    } catch {
      this.copiedField.set(null);
    }
  }

  private recordRecentSearch(term: string): void {
    this.recentSearches.update((list) => [term, ...list.filter((t) => t !== term)].slice(0, 10));
  }

  private computeRelevance(record: UniversalRecord, query: string): number {
    if (!query) return record.healthScore;
    let score = 0;
    const q = query.toLowerCase();
    if (record.name.toLowerCase() === q) score += 100;
    else if (record.name.toLowerCase().startsWith(q)) score += 70;
    else if (record.name.toLowerCase().includes(q)) score += 40;
    if (record.id.toLowerCase().includes(q)) score += 50;
    score += record.healthScore / 10;
    return Math.min(99, Math.round(score));
  }

  protected relevanceOf(record: UniversalRecord): number {
    return this.computeRelevance(record, this.query().trim().toLowerCase());
  }

  protected trackByRecordId(_index: number, record: UniversalRecord): string {
    return record.id;
  }
}
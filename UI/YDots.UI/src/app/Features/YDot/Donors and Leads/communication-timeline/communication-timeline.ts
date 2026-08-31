import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { WorkflowDonor, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';

type CommunicationType =
  | 'Call'
  | 'Email'
  | 'SMS'
  | 'WhatsApp'
  | 'Meeting'
  | 'Visit'
  | 'Event'
  | 'Internal Note';

type Temperature = 'Cold' | 'Warm' | 'Hot';
type DonationPotential = 'Low' | 'Medium' | 'High';
type CommunicationQuality = 'Poor' | 'Average' | 'Good' | 'Excellent';
type EngagementLevel = 'Low' | 'Medium' | 'High';
type FollowUpPriority = 'Low' | 'Medium' | 'High' | 'Critical';
type RelationshipHealth = 'Healthy' | 'Needs Attention' | 'At Risk';
type EngagementTrend = 'Improving' | 'Stable' | 'Declining';
type CommunicationTrend = 'High Frequency' | 'Moderate Frequency' | 'Low Frequency';

type Outcome =
  | 'Connected'
  | 'No Answer'
  | 'Interested'
  | 'Requested Callback'
  | 'Meeting Scheduled'
  | 'Meeting Completed'
  | 'Requested Information'
  | 'Donation Discussion'
  | 'Not Interested'
  | 'Wrong Contact';

interface CommunicationRecord {
  id: string;
  type: CommunicationType;
  date: string;
  time: string;
  createdBy: string;
  direction: 'Incoming' | 'Outgoing';
  outcome: Outcome;
  summary: string;
  notes?: string;
  engagement: EngagementLevel;
  quality?: CommunicationQuality;
  important: boolean;
  attachment?: string;
  followUpDate?: string;
  followUpTime?: string;
  followUpPriority?: FollowUpPriority;
  followUpPurpose?: string;
  followUpStatus?: 'Pending' | 'Completed' | 'Overdue';
}

interface CommunicationForm {
  type: CommunicationType;
  date: string;
  time: string;
  direction: 'Incoming' | 'Outgoing';
  outcome: Outcome;
  engagement: EngagementLevel;
  quality: CommunicationQuality;
  summary: string;
  notes: string;
  attachmentName: string;
  important: boolean;
}

interface SuggestedAction {
  label: string;
  detail: string;
}

@Component({
  selector: 'app-communication-timeline',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './communication-timeline.html',
  styleUrl: './communication-timeline.css',
})
export class CommunicationTimelineComponent {
  /** Who is actually logging this - the signed-in person, not a constant. */
  protected actorName(): string {
    return this.tokens.displayName() || 'Signed-in user';
  }

  private readonly people = inject(PeopleDirectoryService);
  private readonly tokens = inject(AuthTokenService);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);
  readonly donorId = signal(this.route.snapshot.queryParamMap.get('donorId'));
  readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
  readonly recordId = computed(() => this.donorId() ?? this.leadId() ?? 'LEAD-2026-0142');

  @Output() navigateToLeads = new EventEmitter<void>();

  readonly activeTab = signal<'All' | CommunicationType | 'Important'>('All');
  readonly isEntryDrawerOpen = signal(false);
  readonly isDetailDrawerOpen = signal(false);
  readonly isTemperatureModalOpen = signal(false);
  readonly isDonationPotentialModalOpen = signal(false);
  readonly isExportModalOpen = signal(false);
  readonly isFilterOpen = signal(false);
  readonly isActionMenuOpen = signal(false);
  readonly isRefreshing = signal(false);
  readonly menuRecordId = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly selectedCommunication = signal<CommunicationRecord | null>(null);
  readonly formErrors = signal<string[]>([]);

  readonly currentTemperature = signal<Temperature>('Warm');
  readonly newTemperature = signal<Temperature>('Warm');
  readonly temperatureReason = signal('');

  readonly donationPotential = signal<DonationPotential>('Medium');
  readonly newDonationPotential = signal<DonationPotential>('Medium');
  readonly donationPotentialReason = signal('');

  readonly typeFilter = signal<string>('All');
  readonly directionFilter = signal<string>('All');
  readonly outcomeFilter = signal<string>('All');
  readonly importantOnly = signal(false);
  readonly dateFromFilter = signal('');
  readonly dateToFilter = signal('');

  readonly relationship = computed(() => {
    const lead = this.workflow.getLead(this.leadId());
    const donor = this.workflow.getDonor(this.donorId());
    return {
      reference: lead?.id ?? donor?.donorId ?? this.recordId(),
      name: lead?.name ?? donor?.name ?? '',
      mobile: lead?.mobile ?? donor?.mobile ?? '+91 98765 43210',
      email: lead?.email ?? donor?.email ?? 'ramesh.kumar@example.com',
      campaign: lead?.campaign ?? donor?.campaign ?? 'Educate a Child 2026',
      source: lead?.source ?? (donor ? 'Donation & Payments' : 'Website form'),
      language: lead?.language ?? 'Tamil',
      owner: lead?.owner ?? donor?.owner ?? 'Unassigned',
      preferredContactMethod: 'WhatsApp',
      preferredLanguage: lead?.language ?? 'Tamil',
      bestContactTime: 'Evenings',
      stage: lead?.stage ?? 'Engaged',
      qualificationReadiness: lead?.qualificationReadiness ?? 'Partially Ready',
      readinessScore: lead?.healthScore ?? 68,
    };
  });

  readonly communicationTypes: CommunicationType[] = [
    'Call',
    'Email',
    'SMS',
    'WhatsApp',
    'Meeting',
    'Visit',
    'Event',
    'Internal Note',
  ];

  readonly outcomes: Outcome[] = [
    'Connected',
    'No Answer',
    'Interested',
    'Requested Callback',
    'Meeting Scheduled',
    'Meeting Completed',
    'Requested Information',
    'Donation Discussion',
    'Not Interested',
    'Wrong Contact',
  ];

  readonly temperatures: Temperature[] = ['Cold', 'Warm', 'Hot'];
  readonly donationPotentials: DonationPotential[] = ['Low', 'Medium', 'High'];
  readonly qualities: CommunicationQuality[] = ['Poor', 'Average', 'Good', 'Excellent'];
  readonly engagementLevels: EngagementLevel[] = ['Low', 'Medium', 'High'];
  readonly priorities: FollowUpPriority[] = ['Low', 'Medium', 'High', 'Critical'];

  private readonly monthMap: Record<string, number> = {
    Jan: 0, Feb: 1, Mar: 2, Apr: 3, May: 4, Jun: 5,
    Jul: 6, Aug: 7, Sep: 8, Oct: 9, Nov: 10, Dec: 11,
  };

  private readonly monthNames = [
    'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
    'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
  ];

  readonly records = signal<CommunicationRecord[]>([
    {
      id: 'COM-2026-00871',
      type: 'Call',
      date: '20 Aug 2026',
      time: '10:30 AM',
      createdBy: this.actorName(),
      direction: 'Outgoing',
      outcome: 'Interested',
      summary:
        'Ramesh confirmed interest in the Educate a Child campaign and requested a detailed explanation of the donation options.',
      engagement: 'High',
      quality: 'Excellent',
      important: true,
      attachment: 'campaign-brochure.pdf',
      followUpDate: '25 Aug 2026',
      followUpPriority: 'High',
      followUpPurpose: 'Walk through recurring donation options',
      followUpStatus: 'Pending',
    },
    {
      id: 'COM-2026-00863',
      type: 'WhatsApp',
      date: '19 Aug 2026',
      time: '06:15 PM',
      createdBy: this.actorName(),
      direction: 'Outgoing',
      outcome: 'Requested Information',
      summary:
        'Campaign overview and impact information were shared. The lead requested additional information about recurring donations.',
      engagement: 'Medium',
      quality: 'Good',
      important: false,
      attachment: 'educate-a-child-overview.pdf',
    },
    {
      id: 'COM-2026-00841',
      type: 'Meeting',
      date: '16 Aug 2026',
      time: '04:00 PM',
      createdBy: this.actorName(),
      direction: 'Outgoing',
      outcome: 'Meeting Completed',
      summary:
        'Completed relationship discussion. The lead showed strong interest and agreed to consider a monthly contribution.',
      engagement: 'High',
      quality: 'Excellent',
      important: true,
      followUpDate: '18 Aug 2026',
      followUpStatus: 'Completed',
    },
    {
      id: 'COM-2026-00827',
      type: 'Email',
      date: '14 Aug 2026',
      time: '11:20 AM',
      createdBy: this.actorName(),
      direction: 'Outgoing',
      outcome: 'Requested Information',
      summary:
        'Sent detailed information about campaign objectives, donor impact and available contribution methods.',
      engagement: 'Medium',
      quality: 'Average',
      important: false,
    },
  ]);

  constructor() {
    /*
     * IT NO LONGER CREATES A LEAD.
     *
     * Opening this screen with a record id that had not loaded CREATED ONE - 'Ramesh Kumar', with
     * a phone number, an e-mail address and a campaign, all invented - and, now that addLead posts
     * to the server, that would have written a real lead into the organisation's data every time
     * somebody followed a stale link.
     *
     * A record that is not there is not there. The screen shows its empty state and says so.
     */
    this.syncRecords();
    const lead = this.workflow.getLead(this.leadId());
    if (lead) {
      this.currentTemperature.set(lead.temperature);
      this.newTemperature.set(lead.temperature);
      this.donationPotential.set(lead.donationPotential);
      this.newDonationPotential.set(lead.donationPotential);
    }
  }

  private syncRecords(): void {
    const records = this.workflow.communicationsFor(this.recordId()).map((record) => ({
      id: record.id,
      type: record.type as CommunicationType,
      date: record.date,
      time: record.time,
      createdBy: record.createdBy,
      direction: record.direction as 'Incoming' | 'Outgoing',
      outcome: record.outcome as Outcome,
      summary: record.summary,
      notes: record.notes,
      engagement: (record.engagement ?? 'Medium') as EngagementLevel,
      quality: record.quality as CommunicationQuality | undefined,
      important: record.important ?? false,
      attachment: record.attachment,
      followUpDate: record.followUpDate,
      followUpTime: record.followUpTime,
      followUpPriority: record.followUpPriority as FollowUpPriority | undefined,
      followUpPurpose: record.followUpPurpose,
      followUpStatus: record.followUpStatus as 'Pending' | 'Completed' | 'Overdue' | undefined,
    }));
    this.records.set(records);
  }

  readonly form = signal<CommunicationForm>(this.createEmptyForm('Call'));


  readonly filteredRecords = computed(() => {
    const from = this.dateFromFilter() ? this.parseDisplayDate(this.toDisplayDate(this.dateFromFilter())) : null;
    const to = this.dateToFilter() ? this.parseDisplayDate(this.toDisplayDate(this.dateToFilter())) : null;

    return this.records().filter((record) => {
      const tabMatch =
        this.activeTab() === 'All'
          ? true
          : this.activeTab() === 'Important'
            ? record.important
            : record.type === this.activeTab();

      const typeMatch =
        this.typeFilter() === 'All' || record.type === this.typeFilter();

      const directionMatch =
        this.directionFilter() === 'All' ||
        record.direction === this.directionFilter();

      const outcomeMatch =
        this.outcomeFilter() === 'All' || record.outcome === this.outcomeFilter();

      const importantMatch = !this.importantOnly() || record.important;

      const recordDate = this.parseDisplayDate(record.date);
      const fromMatch = !from || !recordDate || recordDate.getTime() >= from.getTime();
      const toMatch = !to || !recordDate || recordDate.getTime() <= to.getTime();

      return (
        tabMatch &&
        typeMatch &&
        directionMatch &&
        outcomeMatch &&
        importantMatch &&
        fromMatch &&
        toMatch
      );
    });
  });

  readonly totalCommunications = computed(() => this.records().length);

  readonly callsCount = computed(
    () => this.records().filter((item) => item.type === 'Call').length,
  );

  readonly meetingsCount = computed(
    () => this.records().filter((item) => item.type === 'Meeting').length,
  );

  readonly emailsCount = computed(
    () => this.records().filter((item) => item.type === 'Email').length,
  );

  readonly interestedCount = computed(
    () => this.records().filter((item) => item.outcome === 'Interested').length,
  );

  readonly lastContactDisplay = computed(() => {
    const list = this.records();
    if (!list.length) return 'No contact yet';
    return this.formatRelativeDate(list[0].date);
  });

  readonly nextFollowUpRecord = computed(() => {
    const pending = this.records()
      .filter((record) => record.followUpDate && record.followUpStatus === 'Pending')
      .map((record) => ({ record, parsed: this.parseDisplayDate(record.followUpDate as string) }))
      .filter((entry): entry is { record: CommunicationRecord; parsed: Date } => entry.parsed !== null)
      .sort((a, b) => a.parsed.getTime() - b.parsed.getTime());

    return pending[0]?.record ?? null;
  });

  readonly nextFollowUpDisplay = computed(() => {
    const next = this.nextFollowUpRecord();
    if (!next || !next.followUpDate) return 'None scheduled';
    return this.formatRelativeDate(next.followUpDate);
  });

  readonly relationshipHealthStatus = computed<RelationshipHealth>(() => {
    const list = this.records();
    if (!list.length) return 'At Risk';
    const last = this.parseDisplayDate(list[0].date);
    if (!last) return 'Needs Attention';
    const days = this.daysBetween(last, this.today());
    if (days <= 7) return 'Healthy';
    if (days <= 14) return 'Needs Attention';
    return 'At Risk';
  });

  readonly relationshipHealthReason = computed(() => {
    const list = this.records();
    if (!list.length) return 'No communication logged yet.';
    const last = this.parseDisplayDate(list[0].date);
    if (!last) return '';
    const days = this.daysBetween(last, this.today());
    if (days <= 0) return 'Contacted today.';
    if (days === 1) return 'Last contacted yesterday.';
    return `No contact for ${days} days.`;
  });

  readonly leadHealthScore = computed(() => {
    const list = this.records();
    if (!list.length) return 0;

    const engagementValue = (level: EngagementLevel) =>
      level === 'High' ? 3 : level === 'Medium' ? 2 : 1;

    const avgEngagement =
      list.reduce((sum, r) => sum + engagementValue(r.engagement), 0) / list.length;

    const positiveOutcomes = list.filter(
      (r) => r.outcome === 'Interested' || r.outcome === 'Donation Discussion' || r.outcome === 'Meeting Completed',
    ).length / list.length;

    const importantRatio = list.filter((r) => r.important).length / list.length;

    const last = this.parseDisplayDate(list[0].date);
    const daysSince = last ? Math.max(0, this.daysBetween(last, this.today())) : 30;

    const score = (avgEngagement / 3) * 50 + positiveOutcomes * 30 + importantRatio * 10 - Math.min(daysSince, 20);
    return Math.max(0, Math.min(100, Math.round(score)));
  });

  readonly engagementTrend = computed<EngagementTrend>(() => {
    const scores = this.records().map((r) =>
      r.engagement === 'High' ? 3 : r.engagement === 'Medium' ? 2 : 1,
    );
    if (scores.length < 2) return 'Stable';

    const midpoint = Math.ceil(scores.length / 2);
    const recentAvg = scores.slice(0, midpoint).reduce((a, b) => a + b, 0) / midpoint;
    const olderAvg =
      scores.slice(midpoint).reduce((a, b) => a + b, 0) / (scores.length - midpoint || 1);

    if (recentAvg - olderAvg > 0.3) return 'Improving';
    if (olderAvg - recentAvg > 0.3) return 'Declining';
    return 'Stable';
  });

  readonly communicationTrend = computed<CommunicationTrend>(() => {
    const list = this.records();
    if (list.length < 2) return 'Low Frequency';

    const earliest = this.parseDisplayDate(list[list.length - 1].date);
    const latest = this.parseDisplayDate(list[0].date);
    if (!earliest || !latest) return 'Moderate Frequency';

    const span = Math.max(1, this.daysBetween(earliest, latest));
    const avgGap = span / (list.length - 1);

    if (avgGap <= 3) return 'High Frequency';
    if (avgGap <= 7) return 'Moderate Frequency';
    return 'Low Frequency';
  });

  readonly followUpCompletionRate = computed(() => {
    const withFollowUp = this.records().filter((r) => r.followUpStatus);
    if (!withFollowUp.length) return 0;
    const completed = withFollowUp.filter((r) => r.followUpStatus === 'Completed').length;
    return Math.round((completed / withFollowUp.length) * 100);
  });

  readonly suggestedAction = computed<SuggestedAction>(() => {
    const latest = this.records()[0];
    if (!latest) {
      return { label: 'Log First Communication', detail: 'No communication history yet.' };
    }

    switch (latest.outcome) {
      case 'Interested':
        return { label: 'Schedule Meeting', detail: 'Lead confirmed interest during the last communication.' };
      case 'Requested Information':
        return { label: 'Send Proposal', detail: 'Lead asked for more information.' };
      case 'Donation Discussion':
        return { label: 'Follow-Up In 3 Days', detail: 'A donation discussion is in progress.' };
      case 'Meeting Scheduled':
        return { label: 'Prepare Meeting Brief', detail: 'A meeting has been scheduled with the lead.' };
      case 'Not Interested':
      case 'Wrong Contact':
        return { label: 'Review Lead Status', detail: 'Recent outcome suggests low engagement.' };
      default:
        return { label: 'Log Next Communication', detail: 'Keep the conversation moving.' };
    }
  });

  openEntryDrawer(type: CommunicationType = 'Call'): void {
    this.editingId.set(null);
    this.formErrors.set([]);
    this.form.set(this.createEmptyForm(type));
    this.isEntryDrawerOpen.set(true);
  }

  editCommunication(record: CommunicationRecord): void {
    this.editingId.set(record.id);
    this.formErrors.set([]);

    this.form.set({
      type: record.type,
      date: this.toIsoDate(record.date),
      time: record.time,
      direction: record.direction,
      outcome: record.outcome,
      engagement: record.engagement,
      quality: record.quality ?? 'Good',
      summary: record.summary,
      notes: record.notes ?? '',
      attachmentName: record.attachment ?? '',
      important: record.important,
    });

    this.closeActionMenu();
    this.isDetailDrawerOpen.set(false);
    this.isEntryDrawerOpen.set(true);
  }

  closeEntryDrawer(): void {
    this.isEntryDrawerOpen.set(false);
    this.editingId.set(null);
    this.formErrors.set([]);
  }

  openDetails(record: CommunicationRecord): void {
    this.selectedCommunication.set(record);
    this.isDetailDrawerOpen.set(true);
    this.closeActionMenu();
  }

  closeDetails(): void {
    this.isDetailDrawerOpen.set(false);
    this.selectedCommunication.set(null);
  }

  onAttachmentSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files && input.files.length ? input.files[0] : null;
    this.updateForm('attachmentName', file ? file.name : '');
  }

  saveCommunication(): void {
    const value = this.form();
    const errors = this.validateForm(value);
    this.formErrors.set(errors);

    if (errors.length) {
      return;
    }

    const displayDate = this.toDisplayDate(value.date);
    const editingId = this.editingId();

    if (editingId) {
      this.records.update((records) =>
        records.map((record) =>
          record.id === editingId
            ? {
                ...record,
                type: value.type,
                date: displayDate,
                time: value.time,
                direction: value.direction,
                outcome: value.outcome,
                engagement: value.engagement,
                quality: value.quality,
                summary: value.summary.trim(),
                notes: value.notes.trim() || undefined,
                attachment: value.attachmentName || record.attachment,
                important: value.important,
              }
            : record,
        ),
      );
    } else {
      const newRecord: CommunicationRecord = {
        id: `COM-${Date.now()}`,
        type: value.type,
        date: displayDate,
        time: value.time,
        createdBy: this.relationship().owner,
        direction: value.direction,
        outcome: value.outcome,
        engagement: value.engagement,
        quality: value.quality,
        summary: value.summary.trim(),
        notes: value.notes.trim() || undefined,
        important: value.important,
        attachment: value.attachmentName || undefined,
      };

      this.records.update((records) => [newRecord, ...records]);
    }

    const storedRecord = this.records().find((record) => record.id === (editingId ?? this.records()[0]?.id));
    if (storedRecord) {
      if (editingId) {
        this.workflow.replaceCommunication(this.recordId(), { ...storedRecord, recordId: this.recordId() });
      } else {
        this.workflow.addCommunication({ ...storedRecord, recordId: this.recordId(), type: storedRecord.type, outcome: storedRecord.outcome, summary: storedRecord.summary });
      }
    }
    this.syncRecords();
    this.closeEntryDrawer();
  }

  toggleImportant(record: CommunicationRecord): void {
    this.records.update((records) =>
      records.map((item) =>
        item.id === record.id ? { ...item, important: !item.important } : item,
      ),
    );
    const updated = this.records().find((item) => item.id === record.id);
    if (updated) this.workflow.replaceCommunication(this.recordId(), { ...updated, recordId: this.recordId() });
    this.closeActionMenu();
  }

  openTemperatureModal(): void {
    this.newTemperature.set(this.currentTemperature());
    this.temperatureReason.set('');
    this.isTemperatureModalOpen.set(true);
    this.closeActionMenu();
  }

  closeTemperatureModal(): void {
    this.isTemperatureModalOpen.set(false);
  }

  saveTemperature(): void {
    if (!this.temperatureReason().trim()) {
      return;
    }

    this.currentTemperature.set(this.newTemperature());
    if (this.leadId()) {
      this.workflow.patchLead(this.leadId()!, { temperature: this.newTemperature(), lastActivity: `Temperature updated: ${this.newTemperature()}` });
    }
    this.isTemperatureModalOpen.set(false);
  }

  openDonationPotentialModal(): void {
    this.newDonationPotential.set(this.donationPotential());
    this.donationPotentialReason.set('');
    this.isDonationPotentialModalOpen.set(true);
    this.closeActionMenu();
  }

  closeDonationPotentialModal(): void {
    this.isDonationPotentialModalOpen.set(false);
  }

  saveDonationPotential(): void {
    if (!this.donationPotentialReason().trim()) {
      return;
    }

    this.donationPotential.set(this.newDonationPotential());
    if (this.leadId()) {
      this.workflow.patchLead(this.leadId()!, { donationPotential: this.newDonationPotential(), lastActivity: `Donation potential updated: ${this.newDonationPotential()}` });
    }
    this.isDonationPotentialModalOpen.set(false);
  }


  openFollowUpPlanner(): void {
    this.router.navigate(['/app/don/follow-up-planner'], {
      queryParams: this.donorId()
        ? { donorId: this.donorId(), mode: 'create' }
        : { leadId: this.leadId(), mode: 'create' },
    });
  }

  openExportModal(): void {
    this.isExportModalOpen.set(true);
  }

  closeExportModal(): void {
    this.isExportModalOpen.set(false);
  }

  exportAsExcel(): void {
    this.downloadCsv();
    this.isExportModalOpen.set(false);
  }

  exportAsPdf(): void {
    this.isExportModalOpen.set(false);
    window.print();
  }

  refreshTimeline(): void {
    this.isRefreshing.set(true);
    this.isFilterOpen.set(false);
    this.closeActionMenu();
    window.setTimeout(() => { this.syncRecords(); this.isRefreshing.set(false); }, 150);
  }

  handleOpenMyLeads(): void {
    this.navigateToLeads.emit();
    if (this.donorId()) {
      this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { donorId: this.donorId(), tab: 'communications' } });
      return;
    }
    this.router.navigate(['/app/fundraising/relationships/my-leads'], { queryParams: { leadId: this.leadId() } });
  }

  toggleFilterPanel(): void {
    this.isFilterOpen.update((open) => !open);
  }

  toggleActionMenu(record: CommunicationRecord): void {
    if (this.isActionMenuOpen() && this.menuRecordId() === record.id) {
      this.closeActionMenu();
      return;
    }

    this.isActionMenuOpen.set(true);
    this.menuRecordId.set(record.id);
  }

  closeActionMenu(): void {
    this.isActionMenuOpen.set(false);
    this.menuRecordId.set(null);
  }

  setTab(tab: 'All' | CommunicationType | 'Important'): void {
    this.activeTab.set(tab);
  }

  resetFilters(): void {
    this.typeFilter.set('All');
    this.directionFilter.set('All');
    this.outcomeFilter.set('All');
    this.importantOnly.set(false);
    this.dateFromFilter.set('');
    this.dateToFilter.set('');
  }

  communicationIcon(type: CommunicationType): string {
    const icons: Record<CommunicationType, string> = {
      Call: '☎',
      Email: '✉',
      SMS: '◌',
      WhatsApp: '◉',
      Meeting: '◫',
      Visit: '⌂',
      Event: '◆',
      'Internal Note': '✎',
    };

    return icons[type];
  }

  outcomeClass(outcome: Outcome): string {
    if (
      outcome === 'Interested' ||
      outcome === 'Meeting Completed' ||
      outcome === 'Donation Discussion'
    ) {
      return 'status-success';
    }

    if (
      outcome === 'No Answer' ||
      outcome === 'Requested Callback' ||
      outcome === 'Requested Information'
    ) {
      return 'status-warning';
    }

    if (outcome === 'Not Interested' || outcome === 'Wrong Contact') {
      return 'status-danger';
    }

    return 'status-neutral';
  }

  temperatureClass(temperature: Temperature): string {
    return `temperature-${temperature.toLowerCase()}`;
  }

  donationPotentialClass(value: DonationPotential): string {
    return `potential-${value.toLowerCase()}`;
  }

  relationshipHealthClass(status: RelationshipHealth): string {
    if (status === 'Healthy') return 'health-healthy';
    if (status === 'Needs Attention') return 'health-attention';
    return 'health-risk';
  }

  followUpStatusClass(status?: CommunicationRecord['followUpStatus']): string {
    if (status === 'Completed') return 'status-success';
    if (status === 'Overdue') return 'status-danger';
    return 'status-warning';
  }

  getTodayIso(): string {
    return this.formatIso(this.today());
  }

  updateForm<K extends keyof CommunicationForm>(
    key: K,
    value: CommunicationForm[K],
  ): void {
    this.form.update((current) => ({
      ...current,
      [key]: value,
    }));
  }

  private downloadCsv(): void {
    const header = ['Type', 'Date', 'Time', 'Direction', 'Outcome', 'Engagement', 'Recorded By', 'Summary'];
    const rows = this.filteredRecords().map((record) => [
      record.type,
      record.date,
      record.time,
      record.direction,
      record.outcome,
      record.engagement,
      record.createdBy,
      record.summary.replace(/"/g, '""'),
    ]);

    const csv = [header, ...rows]
      .map((row) => row.map((cell) => `"${cell}"`).join(','))
      .join('\n');

    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `communication-timeline-${this.relationship().reference}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  private validateForm(value: CommunicationForm): string[] {
    const errors: string[] = [];

    if (!value.date) {
      errors.push('Communication date is required.');
    } else if (value.date > this.getTodayIso()) {
      errors.push('Communication date cannot be in the future.');
    }

    if (!value.time) {
      errors.push('Communication time is required.');
    }

    const summaryLength = value.summary.trim().length;
    if (summaryLength < 10) {
      errors.push('Summary must be at least 10 characters.');
    } else if (value.summary.length > 2000) {
      errors.push('Summary cannot exceed 2000 characters.');
    }

    if (value.notes.length > 3000) {
      errors.push('Internal notes cannot exceed 3000 characters.');
    }

    return errors;
  }

  private createEmptyForm(type: CommunicationType): CommunicationForm {
    return {
      type,
      date: this.getTodayIso(),
      time: '',
      direction: 'Outgoing',
      outcome: 'Connected',
      engagement: 'Medium',
      quality: 'Good',
      summary: '',
      notes: '',
      attachmentName: '',
      important: false,
    };
  }

  private today(): Date {
    const now = new Date();
    now.setHours(0, 0, 0, 0);
    return now;
  }

  private daysBetween(earlier: Date, later: Date): number {
    return Math.round((later.getTime() - earlier.getTime()) / 86400000);
  }

  private parseDisplayDate(value: string): Date | null {
    const match = value.trim().match(/^(\d{1,2})\s+([A-Za-z]{3})\s+(\d{4})$/);
    if (!match) return null;

    const [, day, mon, year] = match;
    const month = this.monthMap[mon];
    if (month === undefined) return null;

    return new Date(Number(year), month, Number(day));
  }

  private formatRelativeDate(displayDate: string): string {
    const date = this.parseDisplayDate(displayDate);
    if (!date) return displayDate;

    const diff = this.daysBetween(date, this.today());

    if (diff === 0) return 'Today';
    if (diff === 1) return 'Yesterday';
    if (diff === -1) return 'Tomorrow';
    if (diff > 1) return `${diff} Days Ago`;
    if (diff < -1) return `In ${Math.abs(diff)} Days`;
    return displayDate;
  }

  private formatIso(date: Date): string {
    const y = date.getFullYear();
    const m = `${date.getMonth() + 1}`.padStart(2, '0');
    const d = `${date.getDate()}`.padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private toDisplayDate(iso: string): string {
    const [y, m, d] = iso.split('-').map(Number);
    if (!y || !m || !d) return iso;
    return `${d} ${this.monthNames[m - 1]} ${y}`;
  }

  private toIsoDate(display: string): string {
    const parsed = this.parseDisplayDate(display);
    if (!parsed) return '';
    return this.formatIso(parsed);
  }
}
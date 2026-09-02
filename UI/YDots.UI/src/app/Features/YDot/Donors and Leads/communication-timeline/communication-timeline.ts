import { CommonModule } from '@angular/common';
import {
  Component,
  EventEmitter,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { CommunicationTimelineResponse } from '../../../../Shared/models/donor-contract.model';

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
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);

  /**
   * The server's answer for this lead or donor.
   *
   * ONE CALL FILLS BOTH HALVES OF THE SCREEN - the profile card and the timeline beneath it -
   * so the header can never describe one person while the conversations belong to another.
   */
  readonly timeline = signal<CommunicationTimelineResponse | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly donorId = signal(this.route.snapshot.queryParamMap.get('donorId'));
  readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
  /**
   * The record this timeline belongs to.
   *
   * NO FALLBACK CONSTANT. It used to default to the literal 'LEAD-2026-0142', so opening the
   * screen without a query string showed one particular fabricated lead - and any note recorded
   * there was attached to it.
   */
  readonly recordId = computed(() => this.donorId() ?? this.leadId() ?? '');

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

  /**
   * The profile beside the timeline.
   *
   * EVERY FIELD IS THE SERVER'S. The old version fell back to a fabricated person - "Ramesh
   * Kumar", "+91 98765 43210", "ramesh.kumar@example.com", "Tamil", "Evenings" - whenever the
   * in-memory store had nothing, which is to say on every fresh page load. Somebody could ring
   * that number.
   */
  readonly relationship = computed(() => {
    const data = this.timeline();
    return {
      reference: data?.leadReference ?? data?.donorReference ?? '',
      name: data?.displayName ?? '',

      // ALREADY MASKED, OR ALREADY NOT - `isContactMasked` says which, and the screen shows it
      // rather than deciding.
      mobile: data?.mobileNumber ?? '',
      email: data?.emailAddress ?? '',
      campaign: data?.campaignName ?? '',
      source: data?.source ?? '',
      language: data?.preferredLanguage ?? '',
      owner: data?.ownerName ?? 'Unassigned',
      preferredContactMethod: '',
      preferredLanguage: data?.preferredLanguage ?? '',
      bestContactTime: '',
      stage: data?.status ?? '',
      qualificationReadiness: '',
      readinessScore: data?.healthScore ?? 0,
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

  /**
   * The conversations, newest first.
   *
   * IT WAS A LITERAL ARRAY of four invented exchanges - "Ramesh confirmed interest in the Educate
   * a Child campaign", "campaign-brochure.pdf" - seeded into `WorkflowStateService` on every
   * construction, so every lead in every organisation had had the same four conversations.
   */
  readonly records = signal<CommunicationRecord[]>([]);

  constructor() {
    this.load();
  }

  /**
   * Loads the profile and the conversations together.
   *
   * IT SENDS WHICHEVER ID IT HAS. The screen is reached with a lead id from the queues and a
   * donor id from Donor 360; the server resolves one to the other, so a lead that has since
   * converted still shows everything said before the conversion.
   */
  private load(): void {
    if (!this.recordId()) {
      this.loadError.set('No lead or donor was named, so there is no timeline to show.');
      return;
    }

    this.loading.set(true);
    this.loadError.set('');

    this.api.getCommunicationTimeline(this.leadId(), this.donorId()).subscribe({
      next: (response) => {
        this.timeline.set(response);
        this.records.set(response.entries.map((entry) => this.toRecord(entry)));

        this.currentTemperature.set(response.temperature as Temperature);
        this.newTemperature.set(response.temperature as Temperature);
        this.donationPotential.set(response.donationPotential as DonationPotential);
        this.newDonationPotential.set(response.donationPotential as DonationPotential);

        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(apiErrorMessage(error));
        this.toast.show('Timeline unavailable', this.loadError(), 'error');
      },
    });
  }

  /**
   * Maps one server entry onto the row this screen draws.
   *
   * THE DATE IS SPLIT FOR DISPLAY ONLY. The API stores one UTC instant; the timeline groups by
   * day and shows a time beside each line, so both are derived here rather than stored twice.
   */
  private toRecord(entry: import('../../../../Shared/models/donor-contract.model').CommunicationTimelineEntry): CommunicationRecord {
    const occurred = new Date(entry.occurredAtUtc);

    return {
      id: entry.id,
      type: this.toCommunicationType(entry.interactionType, entry.channel),
      date: this.toDisplayDateFrom(occurred),
      time: occurred.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' }),
      createdBy: entry.performedByName ?? '',
      direction: entry.direction === 'Incoming' ? 'Incoming' : 'Outgoing',
      outcome: entry.outcome as Outcome,
      summary: entry.summary,

      // WITHHELD RATHER THAN BLANK. `isNotesMasked` is why there is nothing here, and the screen
      // says so instead of implying the conversation had no notes.
      notes: entry.notes ?? undefined,
      engagement: 'Medium',
      quality: undefined,
      important: false,
    };
  }

  private toCommunicationType(interactionType: string, channel: string | null): CommunicationType {
    switch (channel ?? interactionType) {
      case 'Call': return 'Call';
      case 'Email': return 'Email';
      case 'Sms':
      case 'SMS': return 'SMS';
      case 'WhatsApp': return 'WhatsApp';
      case 'Meeting': return 'Meeting';
      case 'Visit': return 'Visit';
      default: return 'Internal Note';
    }
  }

  private toDisplayDateFrom(value: Date): string {
    const day = String(value.getDate()).padStart(2, '0');
    return `${day} ${this.monthNames[value.getMonth()]} ${value.getFullYear()}`;
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

    // RECORDED THROUGH THE LEAD'S CONTACT COMMAND, which is the same write the Lead Queue uses.
    // That endpoint applies the consent rules - it refuses a channel the lead has withdrawn -
    // and writes the audit entry, neither of which a local array could do.
    const leadId = this.leadId();
    if (!leadId) {
      this.toast.show(
        'Cannot record this',
        'A conversation is recorded against a lead. Open this timeline from the Lead Queue to add one.',
        'warning',
      );
      this.closeEntryDrawer();
      return;
    }

    this.api
      .contactLead(leadId, {
        channel: this.toConsentChannel(value.type),
        outcome: value.outcome,

        // THE SUMMARY LEADS THE NOTE. `ContactLeadRequest` carries one free-text field, and the
        // server uses the interaction's Name for the summary line, so both are sent together
        // rather than dropping what the person typed in the summary box.
        notes: [value.summary.trim(), value.notes.trim()].filter(Boolean).join(' — ') || null,
      })
      .subscribe({
        next: () => {
          this.closeEntryDrawer();
          this.toast.show('Conversation recorded', 'The timeline has been updated.', 'success');
          this.load();
        },
        error: (error: unknown) => {
          this.toast.show('Not recorded', apiErrorMessage(error), 'error');
          this.load();
        },
      });
  }

  /** The screen's own type names, mapped onto the consent channel the API records against. */
  private toConsentChannel(type: CommunicationType): string {
    switch (type) {
      case 'Call': return 'PhoneCall';
      case 'Email': return 'Email';
      case 'SMS': return 'Sms';
      case 'WhatsApp': return 'WhatsApp';
      default: return 'Email';
    }
  }

  /**
   * Flagging a line as important.
   *
   * IT IS THIS BROWSER'S FLAG ONLY, and now says so. The API has no "important" field on an
   * interaction, and the old version wrote it into an in-memory store that made it look shared -
   * a colleague opening the same timeline saw nothing flagged.
   */
  toggleImportant(record: CommunicationRecord): void {
    this.records.update((records) =>
      records.map((item) =>
        item.id === record.id ? { ...item, important: !item.important } : item,
      ),
    );
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

  /**
   * Temperature and donation potential, saved through the lead's qualify command.
   *
   * THE REASON IS THE AUDIT ENTRY, which is why the dialog insists on one and why this is a
   * server call rather than a signal update: "warm to hot" is a judgement somebody made, and the
   * trail should record who and why.
   */
  saveTemperature(): void {
    const reason = this.temperatureReason().trim();
    const leadId = this.leadId();
    if (!reason || !leadId) {
      return;
    }

    this.api
      .qualifyLead(leadId, {
        qualificationNotes: `Temperature set to ${this.newTemperature()}. ${reason}`,
        moveToNurture: false,
      })
      .subscribe({
        next: () => {
          this.isTemperatureModalOpen.set(false);
          this.toast.show('Temperature updated', `Set to ${this.newTemperature()}.`, 'success');
          this.load();
        },
        error: (error: unknown) => this.toast.show('Not updated', apiErrorMessage(error), 'error'),
      });
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
    const reason = this.donationPotentialReason().trim();
    const leadId = this.leadId();
    if (!reason || !leadId) {
      return;
    }

    this.api
      .qualifyLead(leadId, {
        qualificationNotes: `Donation potential set to ${this.newDonationPotential()}. ${reason}`,
        moveToNurture: false,
      })
      .subscribe({
        next: () => {
          this.isDonationPotentialModalOpen.set(false);
          this.toast.show('Donation potential updated', `Set to ${this.newDonationPotential()}.`, 'success');
          this.load();
        },
        error: (error: unknown) => this.toast.show('Not updated', apiErrorMessage(error), 'error'),
      });
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
    window.setTimeout(() => { this.load(); this.isRefreshing.set(false); }, 150);
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
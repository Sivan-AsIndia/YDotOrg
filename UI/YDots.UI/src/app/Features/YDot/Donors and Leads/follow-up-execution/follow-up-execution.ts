/**
 * Screen 9 – Follow-Up Execution
 * DON Module | Fundraising CRM
 *
 * Single-file TypeScript module containing the domain models, the data
 * access service, and the standalone Angular component for this screen.
 * Template and styles remain in follow-up-execution.html / .css per the
 * component's templateUrl / styleUrl.
 */

import {
  ChangeDetectionStrategy,
  Component,
  Injectable,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { Observable, delay, finalize, of, throwError } from 'rxjs';
import { WorkflowStateService } from '../../../../Service/workflow-state.service';

// ---------------------------------------------------------------------------
// Domain models
// ---------------------------------------------------------------------------

/**
 * Domain models for Screen 9 – Follow-Up Execution
 * DON Module | Fundraising CRM
 */

export type Temperature = 'Cold' | 'Warm' | 'Hot';

export type LeadStage =
  | 'Assigned'
  | 'Contacted'
  | 'Engaged'
  | 'Qualified'
  | 'Lost'
  | 'Dormant';

export type QualificationReadiness = 'Not Ready' | 'Partially Ready' | 'Ready';

export type ExecutionStatus =
  | 'Completed'
  | 'Partially Completed'
  | 'No Response'
  | 'Cancelled';

export type CompletionReason =
  | 'Successfully Completed'
  | 'Partially Completed'
  | 'Lead Unavailable'
  | 'Cancelled By Lead'
  | 'Wrong Contact'
  | 'Escalated'
  | 'Converted'
  | 'No Response';

export type FollowUpOutcome =
  | 'Interested'
  | 'Very Interested'
  | 'Requested Proposal'
  | 'Requested Meeting'
  | 'Meeting Scheduled'
  | 'Donation Discussion'
  | 'Recurring Donation Interest'
  | 'Qualification Ready'
  | 'Call Back Later'
  | 'Not Interested'
  | 'Wrong Contact'
  | 'Do Not Contact'
  | 'No Response';

export type EngagementLevel = 'Low' | 'Medium' | 'High';

export type CommunicationQuality = 'Poor' | 'Average' | 'Good' | 'Excellent';

export type Disposition =
  | 'Interested'
  | 'Nurture Later'
  | 'Not Interested'
  | 'Wrong Contact'
  | 'Converted'
  | 'Escalated'
  | 'Dormant';

export type RiskLevel = 'Healthy' | 'Needs Attention' | 'At Risk';

export type FollowUpType = 'Call' | 'Email' | 'SMS' | 'WhatsApp' | 'Meeting' | 'Event';

export type FollowUpPriority = 'Low' | 'Medium' | 'High' | 'Critical';

export interface LeadSummary {
  leadId: string;
  fullName: string;
  phone: string;
  email: string;
  campaign: string;
  leadSource: string;
  currentOwner: string;
  currentStage: LeadStage;
  currentTemperature: Temperature;
  qualificationReadiness: QualificationReadiness;
  followUpStats: {
    open: number;
    completed: number;
    overdue: number;
  };
}

export interface FollowUpSummary {
  followUpId: string;
  type: FollowUpType;
  subject: string;
  priority: FollowUpPriority;
  scheduledDate: string;
  scheduledTime: string;
  assignedUser: string;
  originalPurpose: string;
  expectedOutcome: string;
}

export interface Attachment {
  id: string;
  name: string;
  type: 'PDF' | 'DOCX' | 'PNG' | 'JPG';
  sizeLabel: string;
}

export interface ExecutionHistoryEntry {
  id: string;
  date: string;
  type: 'Call' | 'Meeting' | 'Follow-Up' | 'Email' | 'SMS' | 'WhatsApp' | 'Event';
  outcome: string;
  detail?: string;
}

export interface QualificationCheck {
  label: string;
  complete: boolean;
}

export interface RiskIndicator {
  level: RiskLevel;
  reason: string;
}

export interface OutcomeRecommendation {
  suggestions: string[];
}

/** Shape of the primary Execution Form (Sections 1-9). */
export interface ExecutionFormValue {
  actualContactDate: string;
  actualContactTime: string;
  executionStatus: ExecutionStatus | null;
  completionReason: CompletionReason | null;
  outcome: FollowUpOutcome | null;
  engagementLevel: EngagementLevel | null;
  communicationQuality: CommunicationQuality | null;
  completionNotes: string;
  internalNotes: string;
}

export interface TemperatureUpdateValue {
  newTemperature: Temperature | null;
  reasonForChange: string;
}

export interface StageProgressionValue {
  newStage: LeadStage | null;
}

export interface NextFollowUpValue {
  enabled: boolean;
  type: FollowUpType | null;
  date: string;
  time: string;
  priority: FollowUpPriority | null;
  purpose: string;
  owner: string;
}

export interface EscalationValue {
  escalateTo: string;
  reason: string;
  notes: string;
}

export const EXECUTION_STATUS_OPTIONS: ExecutionStatus[] = [
  'Completed',
  'Partially Completed',
  'No Response',
  'Cancelled',
];

export const COMPLETION_REASON_OPTIONS: CompletionReason[] = [
  'Successfully Completed',
  'Partially Completed',
  'Lead Unavailable',
  'Cancelled By Lead',
  'Wrong Contact',
  'Escalated',
  'Converted',
  'No Response',
];

export const OUTCOME_OPTIONS: FollowUpOutcome[] = [
  'Interested',
  'Very Interested',
  'Requested Proposal',
  'Requested Meeting',
  'Meeting Scheduled',
  'Donation Discussion',
  'Recurring Donation Interest',
  'Qualification Ready',
  'Call Back Later',
  'Not Interested',
  'Wrong Contact',
  'Do Not Contact',
  'No Response',
];

export const ENGAGEMENT_LEVEL_OPTIONS: EngagementLevel[] = ['Low', 'Medium', 'High'];

export const COMMUNICATION_QUALITY_OPTIONS: CommunicationQuality[] = [
  'Poor',
  'Average',
  'Good',
  'Excellent',
];

export const TEMPERATURE_OPTIONS: Temperature[] = ['Cold', 'Warm', 'Hot'];

export const STAGE_OPTIONS: LeadStage[] = [
  'Assigned',
  'Contacted',
  'Engaged',
  'Qualified',
  'Lost',
  'Dormant',
];

export const DISPOSITION_OPTIONS: Disposition[] = [
  'Interested',
  'Nurture Later',
  'Not Interested',
  'Wrong Contact',
  'Converted',
  'Escalated',
  'Dormant',
];

export const FOLLOW_UP_TYPE_OPTIONS: FollowUpType[] = [
  'Call',
  'Email',
  'SMS',
  'WhatsApp',
  'Meeting',
  'Event',
];

export const PRIORITY_OPTIONS: FollowUpPriority[] = ['Low', 'Medium', 'High', 'Critical'];

/** Maps a selected outcome to the system-suggested next actions (Outcome Recommendation Panel). */
export const OUTCOME_RECOMMENDATIONS: Partial<Record<FollowUpOutcome, string[]>> = {
  'Interested': ['Schedule Meeting', 'Send Proposal', 'Create Follow-Up'],
  'Very Interested': ['Schedule Meeting', 'Send Proposal', 'Create Follow-Up'],
  'Requested Proposal': ['Send Proposal', 'Create Follow-Up'],
  'Requested Meeting': ['Schedule Meeting'],
  'Meeting Scheduled': ['Create Follow-Up'],
  'Donation Discussion': ['Send Proposal', 'Create Follow-Up'],
  'Recurring Donation Interest': ['Send Proposal', 'Create Follow-Up'],
  'Qualification Ready': ['Start Qualification'],
  'Call Back Later': ['Retry In 3 Days'],
  'No Response': ['Retry In 3 Days'],
  'Wrong Contact': ['Mark Lost'],
  'Not Interested': ['Mark Lost'],
  'Do Not Contact': ['Mark Lost'],
};

// ---------------------------------------------------------------------------
// Data access
// ---------------------------------------------------------------------------

export interface FollowUpExecutionSnapshot {
  lead: LeadSummary;
  followUp: FollowUpSummary;
  executionHistory: ExecutionHistoryEntry[];
  riskIndicator: RiskIndicator;
  readinessScore: number;
  qualificationChecks: QualificationCheck[];
}

export interface CompleteFollowUpPayload {
  followUpId: string;
  execution: ExecutionFormValue;
  temperature: TemperatureUpdateValue;
  stage: StageProgressionValue;
  disposition: Disposition | null;
  attachments: Attachment[];
  nextFollowUp: NextFollowUpValue;
  asDraft: boolean;
}

/**
 * Data access for Screen 9 – Follow-Up Execution.
 *
 * This is an in-memory mock so the component can be exercised end-to-end
 * without a live backend. Swap the method bodies for real HTTP calls
 * (HttpClient) against the DON module API once the endpoints are available,
 * keeping the method signatures and return types intact.
 */
@Injectable({ providedIn: 'root' })
export class FollowUpExecutionService {
  private readonly simulatedLatencyMs = 100;
  private readonly workflow = inject(WorkflowStateService);

  loadSnapshot(leadId: string, followUpId: string): Observable<FollowUpExecutionSnapshot> {
    const lead = this.workflow.getLead(leadId);
    const donor = this.workflow.getDonor(leadId);
    const followUp = this.workflow.getFollowUp(followUpId) ?? this.workflow.followUpsFor(leadId)[0];
    const stage = (lead?.stage === 'New' || !lead?.stage ? 'Assigned' : lead.stage === 'Converted' ? 'Qualified' : lead.stage) as LeadStage;
    const communications = this.workflow.communicationsFor(leadId);
    const relatedFollowUps = this.workflow.followUpsFor(leadId);
    const snapshot: FollowUpExecutionSnapshot = {
      lead: {
        leadId: lead?.id ?? leadId,
        fullName: lead?.name ?? donor?.name ?? 'Unknown record',
        phone: lead?.mobile ?? donor?.mobile ?? '',
        email: lead?.email ?? donor?.email ?? '',
        campaign: lead?.campaign ?? donor?.campaign ?? '',
        leadSource: lead?.source ?? (donor ? 'Donation & Payments' : ''),
        currentOwner: lead?.owner ?? donor?.owner ?? 'Unassigned',
        currentStage: stage,
        currentTemperature: lead?.temperature ?? 'Cold',
        qualificationReadiness: (lead?.qualificationReadiness ?? 'Not Ready') as QualificationReadiness,
        followUpStats: {
          open: relatedFollowUps.filter((item) => item.status === 'Pending' || item.status === 'Rescheduled').length,
          completed: relatedFollowUps.filter((item) => item.status === 'Completed').length,
          overdue: relatedFollowUps.filter((item) => item.slaStatus === 'Breached' && item.status !== 'Completed').length,
        },
      },
      followUp: {
        followUpId: followUp?.id ?? followUpId,
        type: (['Call', 'Email', 'SMS', 'WhatsApp', 'Meeting', 'Event'].includes(followUp?.followUpType ?? '') ? followUp?.followUpType : 'Call') as FollowUpType,
        subject: followUp?.purpose ?? 'Relationship follow-up',
        priority: (followUp?.priority === 'Urgent' ? 'Critical' : followUp?.priority ?? 'Medium') as FollowUpPriority,
        scheduledDate: followUp?.scheduledDate ?? new Date().toISOString().slice(0, 10),
        scheduledTime: followUp?.scheduledTime ?? '',
        assignedUser: followUp?.assignedTo ?? lead?.owner ?? donor?.owner ?? 'Unassigned',
        originalPurpose: followUp?.purpose ?? 'Relationship follow-up',
        expectedOutcome: followUp?.expectedOutcome ?? 'Progress relationship',
      },
      executionHistory: [
        ...communications.slice(0, 5).map((item) => ({ id: item.id, date: item.date, type: item.type as ExecutionHistoryEntry['type'], outcome: item.outcome, detail: item.summary })),
        ...relatedFollowUps.filter((item) => item.status === 'Completed').slice(0, 3).map((item) => ({ id: item.id, date: item.scheduledDate, type: 'Follow-Up' as const, outcome: 'Completed', detail: item.purpose })),
      ],
      riskIndicator: lead && lead.healthScore >= 70 ? { level: 'Healthy', reason: 'Recent engagement is healthy.' } : lead && lead.healthScore < 35 ? { level: 'At Risk', reason: 'Low relationship health score.' } : { level: 'Needs Attention', reason: 'Relationship needs continued follow-up.' },
      readinessScore: lead?.healthScore ?? 20,
      qualificationChecks: [
        { label: 'Communication Recorded', complete: communications.length > 0 },
        { label: 'Follow-Up Completed', complete: relatedFollowUps.some((item) => item.status === 'Completed') },
        { label: 'Engagement High', complete: (lead?.healthScore ?? 0) >= 70 },
        { label: 'Temperature Hot', complete: lead?.temperature === 'Hot' },
        { label: 'Positive Outcome', complete: !!lead && !['No contact yet', 'No answer', 'Not interested'].includes(lead.lastContactOutcome) },
      ],
    };
    return of(snapshot).pipe(delay(this.simulatedLatencyMs));
  }

  saveDraft(payload: CompleteFollowUpPayload): Observable<{ savedAt: string }> {
    return of({ savedAt: new Date().toISOString() }).pipe(delay(this.simulatedLatencyMs));
  }

  completeFollowUp(
    payload: CompleteFollowUpPayload,
  ): Observable<{ completedAt: string; nextFollowUpId: string | null }> {
    if (!payload.execution.outcome) return throwError(() => new Error('Outcome is required.'));
    if (!payload.execution.completionNotes || payload.execution.completionNotes.trim().length < 20) {
      return throwError(() => new Error('Completion notes are required.'));
    }

    const followUp = this.workflow.getFollowUp(payload.followUpId);
    const recordId = followUp?.recordId;
    if (recordId) {
      this.workflow.patchFollowUp(payload.followUpId, {
        status: payload.execution.executionStatus === 'Cancelled' ? 'Cancelled' : 'Completed',
        history: [...(followUp?.history ?? []), { date: new Date().toLocaleDateString('en-GB'), label: `Executed: ${payload.execution.outcome}` }],
      });
      const current = this.workflow.getLead(recordId);
      if (current) {
        this.workflow.patchLead(recordId, {
          temperature: (payload.temperature.newTemperature ?? current.temperature ?? 'Cold') as 'Cold' | 'Warm' | 'Hot',
          stage: payload.stage.newStage ?? current.stage ?? 'Contacted',
          lastContactOutcome: payload.execution.outcome,
          lastActivity: `Follow-up completed: ${payload.execution.outcome}`,
          healthScore: Math.min(100, (current.healthScore ?? 20) + (payload.execution.engagementLevel === 'High' ? 20 : 10)),
          qualificationReadiness: payload.execution.outcome === 'Qualification Ready' ? 'Ready' : current.qualificationReadiness,
        });
      }
      this.workflow.addCommunication({
        recordId,
        type: followUp?.followUpType ?? 'Call',
        date: payload.execution.actualContactDate,
        time: payload.execution.actualContactTime,
        direction: 'Outgoing',
        outcome: payload.execution.outcome,
        summary: payload.execution.completionNotes,
        engagement: payload.execution.engagementLevel ?? undefined,
        quality: payload.execution.communicationQuality ?? undefined,
        notes: payload.execution.internalNotes || undefined,
        attachment: payload.attachments[0]?.name,
      });
    }

    let nextFollowUpId: string | null = null;
    if (recordId && payload.nextFollowUp.enabled) {
      nextFollowUpId = this.workflow.addFollowUp({
        recordId,
        followUpType: payload.nextFollowUp.type ?? 'Call',
        scheduledDate: payload.nextFollowUp.date,
        scheduledTime: payload.nextFollowUp.time,
        priority: payload.nextFollowUp.priority ?? 'Medium',
        purpose: payload.nextFollowUp.purpose,
        assignedTo: payload.nextFollowUp.owner,
      }).id;
    }

    return of({ completedAt: new Date().toISOString(), nextFollowUpId }).pipe(delay(this.simulatedLatencyMs));
  }

  escalate(leadId: string, escalation: EscalationValue): Observable<{ escalated: true }> {
    const target = this.workflow.followUpsFor(leadId).find((item) => item.status === 'Pending' || item.status === 'Rescheduled');
    if (target) this.workflow.patchFollowUp(target.id, { status: 'Escalated', history: [...target.history, { date: new Date().toLocaleDateString('en-GB'), label: `Escalated to ${escalation.escalateTo}: ${escalation.reason}` }] });
    return of({ escalated: true as const }).pipe(delay(this.simulatedLatencyMs));
  }

  /** Client-side guard mirroring the "Assigned → Qualified without engagement" rule. */
  isStageTransitionAllowed(current: LeadStage, next: LeadStage, engagementLevelSet: boolean): boolean {
    if (current === 'Assigned' && next === 'Qualified' && !engagementLevelSet) {
      return false;
    }
    return true;
  }

  computeTemperatureFromOutcome(temperature: Temperature | null): Temperature | null {
    return temperature;
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const ACCEPTED_ATTACHMENT_EXTENSIONS = ['.pdf', '.docx', '.png', '.jpg', '.jpeg'];
const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024;
const QUALIFICATION_READY_THRESHOLD = 75;

/** Disallow any date later than today. */
function noFutureDateValidator(): ValidatorFn {
  return (control): ValidationErrors | null => {
    if (!control.value) return null;
    const selected = new Date(control.value);
    const today = new Date();
    today.setHours(23, 59, 59, 999);
    return selected.getTime() > today.getTime() ? { futureDate: true } : null;
  };
}

@Component({
  selector: 'app-follow-up-execution',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './follow-up-execution.html',
  styleUrl: './follow-up-execution.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FollowUpExecutionComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly executionService = inject(FollowUpExecutionService);

  private readonly params = toSignal(this.route.queryParamMap, { initialValue: null });
  private readonly workflow = inject(WorkflowStateService);

  private readonly requestedLeadId = computed(() => this.params()?.get('leadId'));
  private readonly requestedDonorId = computed(() => this.params()?.get('donorId'));
  readonly followUpId = computed(() => {
    const requested = this.params()?.get('followUpId');
    if (requested) return requested;
    const recordId = this.requestedLeadId() ?? this.requestedDonorId();
    return (recordId ? this.workflow.followUpsFor(recordId)[0]?.id : undefined) ?? 'FUP-2026-00421';
  });
  private readonly selectedFollowUp = computed(() => this.workflow.getFollowUp(this.followUpId()));
  readonly donorId = computed(() =>
    this.requestedDonorId() ?? (this.selectedFollowUp()?.recordType === 'Donor' ? this.selectedFollowUp()!.recordId : null),
  );
  readonly leadId = computed(() =>
    this.requestedLeadId()
      ?? (this.selectedFollowUp()?.recordType === 'Lead' ? this.selectedFollowUp()!.recordId : null)
      ?? this.donorId()
      ?? this.workflow.leads()[0]?.id
      ?? 'LEAD-2026-0142',
  );

  // ---- Async state -------------------------------------------------------
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly formError = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  readonly snapshot = signal<FollowUpExecutionSnapshot | null>(null);
  readonly attachments = signal<Attachment[]>([]);
  readonly attachmentError = signal<string | null>(null);
  readonly showEscalationModal = signal(false);
  readonly expandedHistoryId = signal<string | null>(null);

  // ---- Options for template ----------------------------------------------
  readonly executionStatusOptions = EXECUTION_STATUS_OPTIONS;
  readonly completionReasonOptions = COMPLETION_REASON_OPTIONS;
  readonly outcomeOptions = OUTCOME_OPTIONS;
  readonly engagementLevelOptions = ENGAGEMENT_LEVEL_OPTIONS;
  readonly communicationQualityOptions = COMMUNICATION_QUALITY_OPTIONS;
  readonly temperatureOptions = TEMPERATURE_OPTIONS;
  readonly stageOptions = STAGE_OPTIONS;
  readonly dispositionOptions = DISPOSITION_OPTIONS;
  readonly followUpTypeOptions = FOLLOW_UP_TYPE_OPTIONS;
  readonly priorityOptions = PRIORITY_OPTIONS;
  readonly acceptedAttachmentTypes = ACCEPTED_ATTACHMENT_EXTENSIONS.join(',');

  // ---- Forms ---------------------------------------------------------------
  readonly executionForm = this.fb.nonNullable.group({
    actualContactDate: [this.today(), [Validators.required, noFutureDateValidator()]],
    actualContactTime: [this.nowTime(), Validators.required],
    executionStatus: [null as ExecutionFormValue['executionStatus'], Validators.required],
    completionReason: [null as ExecutionFormValue['completionReason'], Validators.required],
    outcome: [null as FollowUpOutcome | null, Validators.required],
    engagementLevel: [null as ExecutionFormValue['engagementLevel'], Validators.required],
    communicationQuality: [
      null as ExecutionFormValue['communicationQuality'],
      Validators.required,
    ],
    completionNotes: ['', [Validators.required, Validators.minLength(20), Validators.maxLength(3000)]],
    internalNotes: ['', Validators.maxLength(3000)],
  });

  readonly temperatureForm = this.fb.nonNullable.group({
    newTemperature: [null as Temperature | null],
    reasonForChange: [''],
  });

  readonly stageForm = this.fb.nonNullable.group({
    newStage: [null as LeadStage | null],
  });

  readonly dispositionForm = this.fb.nonNullable.group({
    disposition: [null as Disposition | null, Validators.required],
  });

  readonly nextFollowUpForm = this.fb.nonNullable.group({
    enabled: [false],
    type: [null as (typeof FOLLOW_UP_TYPE_OPTIONS)[number] | null],
    date: [''],
    time: [''],
    priority: [null as (typeof PRIORITY_OPTIONS)[number] | null],
    purpose: ['', Validators.maxLength(500)],
    owner: ['', Validators.required],
  });

  readonly escalationForm = this.fb.nonNullable.group({
    escalateTo: ['', Validators.required],
    reason: ['', Validators.required],
    notes: [''],
  });

  // ---- Derived / computed state -------------------------------------------
  readonly selectedOutcome = toSignal(this.executionForm.controls['outcome'].valueChanges, {
    initialValue: null as FollowUpOutcome | null,
  });

  readonly outcomeRecommendations = computed(() => {
    const outcome = this.selectedOutcome();
    if (!outcome) return [];
    return OUTCOME_RECOMMENDATIONS[outcome] ?? [];
  });

  readonly selectedTemperature = toSignal(
    this.temperatureForm.controls['newTemperature'].valueChanges,
    { initialValue: null as Temperature | null },
  );

  readonly temperatureChanged = computed(() => {
    const current = this.snapshot()?.lead.currentTemperature;
    const next = this.selectedTemperature();
    return !!next && !!current && next !== current;
  });

  readonly selectedStage = toSignal(this.stageForm.controls['newStage'].valueChanges, {
    initialValue: null as LeadStage | null,
  });

  readonly stageTransitionBlocked = computed(() => {
    const current = this.snapshot()?.lead.currentStage;
    const next = this.selectedStage();
    if (!current || !next) return false;
    const engagementSet = !!this.executionForm.controls['engagementLevel'].value;
    return !this.executionService.isStageTransitionAllowed(current, next, engagementSet);
  });

  readonly nextFollowUpEnabled = toSignal(this.nextFollowUpForm.controls['enabled'].valueChanges, {
    initialValue: false,
  });

  readonly qualificationChecks = computed<QualificationCheck[]>(
    () => this.snapshot()?.qualificationChecks ?? [],
  );

  readonly readinessScore = computed(() => this.snapshot()?.readinessScore ?? 0);

  readonly qualificationStatus = computed<'Not Ready' | 'Partially Ready' | 'Ready'>(() => {
    const temperature = this.selectedTemperature() ?? this.snapshot()?.lead.currentTemperature;
    const score = this.readinessScore();
    const completeCount = this.qualificationChecks().filter((c) => c.complete).length;

    if (temperature === 'Hot' && score >= QUALIFICATION_READY_THRESHOLD) return 'Ready';
    if (completeCount === 0) return 'Not Ready';
    return 'Partially Ready';
  });

  readonly riskIndicator = computed<RiskIndicator | null>(() => this.snapshot()?.riskIndicator ?? null);

  readonly gaugeCircumference = 2 * Math.PI * 42;
  readonly gaugeOffset = computed(
    () => this.gaugeCircumference * (1 - this.readinessScore() / 100),
  );

  readonly executionHistory = computed<ExecutionHistoryEntry[]>(
    () => this.snapshot()?.executionHistory ?? [],
  );

  ngOnInit(): void {
    this.loadData();
  }

  /** Loads (or reloads) the execution snapshot. Also used by the "Reload" empty-state action. */
  retryLoad(): void {
    this.loadData();
  }

  private loadData(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.executionService
      .loadSnapshot(this.leadId(), this.followUpId())
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (snapshot) => {
          this.snapshot.set(snapshot);
          this.temperatureForm.controls['newTemperature'].setValue(snapshot.lead.currentTemperature);
          this.stageForm.controls['newStage'].setValue(snapshot.lead.currentStage);
          this.nextFollowUpForm.controls['owner'].setValue(snapshot.lead.currentOwner);
        },
        error: () => {
          this.loadError.set('Unable to load communication history. Please try again.');
        },
      });
  }

  // ---- Presentation helpers (pure, template-facing) ------------------------
  tempTone(temperature: Temperature): 'danger' | 'warning' | 'info' {
    if (temperature === 'Hot') return 'danger';
    if (temperature === 'Warm') return 'warning';
    return 'info';
  }

  riskTone(level: RiskLevel): 'success' | 'warning' | 'danger' {
    if (level === 'Healthy') return 'success';
    if (level === 'Needs Attention') return 'warning';
    return 'danger';
  }

  qualTone(status: QualificationReadiness): 'success' | 'warning' | 'neutral' {
    if (status === 'Ready') return 'success';
    if (status === 'Partially Ready') return 'warning';
    return 'neutral';
  }

  priorityTone(priority: FollowUpPriority): 'danger' | 'warning' | 'info' | 'neutral' {
    switch (priority) {
      case 'Critical':
        return 'danger';
      case 'High':
        return 'warning';
      case 'Medium':
        return 'info';
      default:
        return 'neutral';
    }
  }

  // ---- Attachments ---------------------------------------------------------
  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    const extension = '.' + file.name.split('.').pop()?.toLowerCase();
    if (!ACCEPTED_ATTACHMENT_EXTENSIONS.includes(extension)) {
      this.attachmentError.set('Only PDF, DOCX, PNG, and JPG files are supported.');
      return;
    }
    if (file.size > MAX_ATTACHMENT_BYTES) {
      this.attachmentError.set('Maximum attachment size is 10 MB.');
      return;
    }

    this.attachmentError.set(null);
    const attachment: Attachment = {
      id: `ATT-${Date.now()}`,
      name: file.name,
      type: extension.replace('.', '').toUpperCase() as Attachment['type'],
      sizeLabel: this.formatBytes(file.size),
    };
    this.attachments.update((list) => [...list, attachment]);
  }

  removeAttachment(id: string): void {
    this.attachments.update((list) => list.filter((a) => a.id !== id));
  }

  private formatBytes(bytes: number): string {
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  // ---- Execution history -----------------------------------------------------
  toggleHistoryEntry(id: string): void {
    this.expandedHistoryId.update((current) => (current === id ? null : id));
  }

  // ---- Escalation modal -------------------------------------------------------
  openEscalationModal(): void {
    this.showEscalationModal.set(true);
  }

  closeEscalationModal(): void {
    this.showEscalationModal.set(false);
    this.escalationForm.reset({ escalateTo: '', reason: '', notes: '' });
  }

  submitEscalation(): void {
    if (this.escalationForm.invalid) {
      this.escalationForm.markAllAsTouched();
      return;
    }
    this.executionService
      .escalate(this.leadId(), this.escalationForm.getRawValue())
      .subscribe(() => {
        this.successMessage.set('Record escalated.');
        this.closeEscalationModal();
      });
  }

  // ---- Navigation ----------------------------------------------------------
  openCommunicationTimeline(): void {
    this.router.navigate(['/app/fundraising/relationships/communication-timeline'], {
      queryParams: this.donorId() ? { donorId: this.donorId() } : { leadId: this.leadId() },
    });
  }

  openLead(): void {
    if (this.donorId()) {
      this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { donorId: this.donorId() } });
      return;
    }
    this.router.navigate(['/app/fundraising/relationships/my-leads'], { queryParams: { leadId: this.leadId() } });
  }


  startQualification(): void {
    this.workflow.patchLead(this.leadId(), { stage: 'Qualified', qualificationReadiness: 'Ready', lastActivity: 'Qualification readiness confirmed' });
    this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { leadId: this.leadId(), conversion: 'pending' } });
  }

  backToQueue(): void {
    this.router.navigate(['/app/fundraising/relationships/follow-up-queue'], { queryParams: { followUpId: this.followUpId(), leadId: this.donorId() ? null : this.leadId(), donorId: this.donorId() } });
  }

  cancel(): void {
    this.backToQueue();
  }

  // ---- Save / Complete -------------------------------------------------------
  saveDraft(): void {
    this.persist(true);
  }

  completeFollowUp(): void {
    if (!this.validateBeforeComplete()) return;
    this.persist(false);
  }

  completeAndCreateFollowUp(): void {
    this.nextFollowUpForm.controls['enabled'].setValue(true);
    this.completeFollowUp();
  }

  private validateBeforeComplete(): boolean {
    this.formError.set(null);

    if (this.executionForm.invalid) {
      this.executionForm.markAllAsTouched();
      const outcomeMissing = this.executionForm.controls['outcome'].invalid;
      const notesMissing = this.executionForm.controls['completionNotes'].invalid;
      if (outcomeMissing) {
        this.formError.set('Outcome is required.');
      } else if (notesMissing) {
        this.formError.set('Completion notes are required.');
      } else {
        this.formError.set('Please complete all required fields and correct validation errors.');
      }
      return false;
    }

    if (this.temperatureChanged() && !this.temperatureForm.controls['reasonForChange'].value?.trim()) {
      this.temperatureForm.controls['reasonForChange'].setErrors({ required: true });
      this.formError.set('A reason is required when changing lead temperature.');
      return false;
    }

    if (this.stageTransitionBlocked()) {
      this.formError.set(
        'This stage change requires a recorded engagement level before moving to Qualified.',
      );
      return false;
    }

    if (this.dispositionForm.invalid) {
      this.dispositionForm.markAllAsTouched();
      this.formError.set('Disposition is required.');
      return false;
    }

    if (
      this.nextFollowUpForm.controls['enabled'].value &&
      (!this.nextFollowUpForm.controls['type'].value ||
        !this.nextFollowUpForm.controls['date'].value ||
        !this.nextFollowUpForm.controls['priority'].value ||
        !this.nextFollowUpForm.controls['purpose'].value?.trim() ||
        !this.nextFollowUpForm.controls['owner'].value?.trim())
    ) {
      this.formError.set('Complete all next follow-up fields, or turn the toggle off.');
      return false;
    }

    return true;
  }

  private persist(asDraft: boolean): void {
    if (!asDraft && !this.validateBeforeComplete()) return;

    this.saving.set(true);
    this.formError.set(null);

    const payload: CompleteFollowUpPayload = {
      followUpId: this.followUpId(),
      execution: this.executionForm.getRawValue(),
      temperature: this.temperatureForm.getRawValue(),
      stage: this.stageForm.getRawValue(),
      disposition: this.dispositionForm.controls['disposition'].value,
      attachments: this.attachments(),
      nextFollowUp: this.nextFollowUpForm.getRawValue(),
      asDraft,
    };

    const onSuccess = (): void => {
      this.successMessage.set(
        asDraft
          ? 'Draft execution saved.'
          : payload.nextFollowUp.enabled
            ? 'Follow-up completed successfully. Next follow-up created.'
            : 'Follow-up completed successfully.',
      );
      if (!asDraft) {
        this.router.navigate(['/app/fundraising/relationships/follow-up-queue'], {
          queryParams: { followUpId: this.followUpId(), leadId: this.donorId() ? null : this.leadId(), donorId: this.donorId(), completed: 'true' },
        });
      }
    };
    const onError = (err: Error): void => {
      this.formError.set(err.message || 'Unable to save communication.');
    };

    if (asDraft) {
      this.executionService
        .saveDraft(payload)
        .pipe(finalize(() => this.saving.set(false)))
        .subscribe({ next: onSuccess, error: onError });
    } else {
      this.executionService
        .completeFollowUp(payload)
        .pipe(finalize(() => this.saving.set(false)))
        .subscribe({ next: onSuccess, error: onError });
    }
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  private nowTime(): string {
    return new Date().toTimeString().slice(0, 5);
  }
}
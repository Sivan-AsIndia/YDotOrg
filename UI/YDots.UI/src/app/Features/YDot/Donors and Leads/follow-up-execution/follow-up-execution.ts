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
import { Observable, catchError, delay, finalize, forkJoin, map, of, switchMap, throwError } from 'rxjs';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';

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

  /** The lead the conversation is recorded against. The completion write needs it. */
  leadId: string;

  /** Which channel was actually used. Checked against the lead's consent before it is accepted. */
  followUpType: FollowUpType;
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
  private readonly api = inject(DonorApiService);

  /**
   * Everything the execution screen needs about one follow-up and its lead.
   *
   * TWO CALLS, NOT ONE, BECAUSE THERE IS NO COMBINED ENDPOINT. The planner answers the follow-up
   * and its siblings; the communication timeline answers the lead's profile and its conversation
   * history. Both are needed to draw this screen, and asking for them in parallel costs one round
   * trip rather than two.
   *
   * THE STATS AND THE READINESS CHECKS ARE COMPUTED FROM THOSE ANSWERS, not invented. Each check
   * below is a fact one of the two responses already contains - "has a conversation been
   * recorded", "has a follow-up been completed" - rather than a number chosen to look plausible.
   */
  loadSnapshot(leadId: string, followUpId: string): Observable<FollowUpExecutionSnapshot> {
    return forkJoin({
      planner: this.api.getFollowUpPlanner({ page: 1, pageSize: 50, leadId }),
      // ONLY ASKED FOR WHEN THERE IS A LEAD TO ASK ABOUT. The timeline endpoint requires an id
      // and answers 400 without one, so opening this screen from a bookmark - no query string -
      // used to fire a request that could only fail. The catchError below still covers a genuine
      // failure; this stops the request that was guaranteed to be one.
      timeline: leadId
        ? this.api.getCommunicationTimeline(leadId, null).pipe(catchError(() => of(null)))
        : of(null),
    }).pipe(
      map(({ planner, timeline }) => {
        const followUps = planner.followUps.items;
        const followUp = followUps.find((item) => item.id === followUpId) ?? followUps[0];
        const entries = timeline?.entries ?? [];

        const completed = followUps.filter((item) => item.status === 'Completed');
        const open = followUps.filter((item) => item.status === 'Scheduled' || item.status === 'Rescheduled');
        const overdue = open.filter(
          (item) => item.dueAtUtc !== null && new Date(item.dueAtUtc).getTime() < Date.now(),
        );

        const health = timeline?.healthScore ?? 0;

        const snapshot: FollowUpExecutionSnapshot = {
          lead: {
            leadId,
            fullName: timeline?.displayName ?? '',

            // MASKED BY THE SERVER unless this caller holds the sensitive-contact permission.
            phone: timeline?.mobileNumber ?? '',
            email: timeline?.emailAddress ?? '',
            campaign: timeline?.campaignName ?? '',
            leadSource: timeline?.source ?? '',
            currentOwner: timeline?.ownerName ?? 'Unassigned',
            currentStage: (timeline?.status ?? 'Assigned') as LeadStage,
            currentTemperature: (timeline?.temperature ?? 'Cold') as 'Cold' | 'Warm' | 'Hot',
            qualificationReadiness: (health >= 70 ? 'Ready' : 'Not Ready') as QualificationReadiness,
            followUpStats: {
              open: open.length,
              completed: completed.length,
              overdue: overdue.length,
            },
          },
          followUp: {
            followUpId: followUp?.id ?? followUpId,
            type: this.toFollowUpType(followUp?.permittedChannel),
            subject: followUp?.purpose ?? '',
            priority: (followUp?.priority === 'Urgent' ? 'Critical' : followUp?.priority ?? 'Medium') as FollowUpPriority,
            scheduledDate: followUp?.dueAtUtc ? followUp.dueAtUtc.slice(0, 10) : '',
            scheduledTime: followUp?.dueAtUtc
              ? new Date(followUp.dueAtUtc).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
              : '',
            assignedUser: followUp?.relationshipOwnerName ?? 'Unassigned',
            originalPurpose: followUp?.purpose ?? '',
            expectedOutcome: followUp?.nextAction ?? '',
          },
          executionHistory: [
            ...entries.slice(0, 5).map((entry) => ({
              id: entry.id,
              date: entry.occurredAtUtc.slice(0, 10),
              type: entry.interactionType as ExecutionHistoryEntry['type'],
              outcome: entry.outcome,
              detail: entry.summary,
            })),
            ...completed.slice(0, 3).map((item) => ({
              id: item.id,
              date: item.completedAtUtc ? item.completedAtUtc.slice(0, 10) : '',
              type: 'Follow-Up' as const,
              outcome: 'Completed',
              detail: item.completionOutcome ?? item.purpose ?? '',
            })),
          ],
          riskIndicator:
            health >= 70
              ? { level: 'Healthy', reason: 'Recent engagement is healthy.' }
              : health < 35
                ? { level: 'At Risk', reason: 'Low relationship health score.' }
                : { level: 'Needs Attention', reason: 'Relationship needs continued follow-up.' },
          readinessScore: health,
          qualificationChecks: [
            { label: 'Communication Recorded', complete: entries.length > 0 },
            { label: 'Follow-Up Completed', complete: completed.length > 0 },
            { label: 'Engagement High', complete: health >= 70 },
            { label: 'Temperature Hot', complete: timeline?.temperature === 'Hot' },
            {
              label: 'Positive Outcome',
              complete: entries.some(
                (entry) => entry.outcome === 'Reached' || entry.outcome === 'CallbackRequested',
              ),
            },
          ],
        };

        return snapshot;
      }),
    );
  }

  private toFollowUpType(channel: string | undefined): FollowUpType {
    switch (channel) {
      case 'Email': return 'Email';
      case 'Sms':
      case 'SMS': return 'SMS';
      case 'WhatsApp': return 'WhatsApp';
      case 'Meeting': return 'Meeting';
      case 'PhoneCall':
      case 'Call': return 'Call';
      default: return 'Call';
    }
  }

  /**
   * Save draft.
   *
   * THERE IS NO DRAFT ON THE SERVER, and saying so is better than pretending. A follow-up is
   * either scheduled or completed; the API has no half-executed state to persist. The screen
   * keeps the typed values in memory, which is what it was already doing - the difference is
   * that it no longer reports a save that did not happen.
   */
  saveDraft(_payload: CompleteFollowUpPayload): Observable<{ savedAt: string }> {
    return throwError(() => new Error(
      'Drafts are not saved on the server. Complete the follow-up, or leave the page and start again.',
    ));
  }

  /**
   * Complete Follow-Up - the document's own action.
   *
   * "Complete the required fields on the Follow-Up Execution page. Select Complete Follow-Up. The
   * follow-up details are updated in the Communication Timeline."
   *
   * THREE WRITES, IN ORDER, AND THE ORDER MATTERS. The conversation is recorded first, so that a
   * failure part-way leaves the contact on the timeline rather than losing what was said; the
   * follow-up is then completed; and a next follow-up is scheduled only if one was asked for.
   */
  completeFollowUp(
    payload: CompleteFollowUpPayload,
  ): Observable<{ completedAt: string; nextFollowUpId: string | null }> {
    if (!payload.execution.outcome) {
      return throwError(() => new Error('Outcome is required.'));
    }
    if (!payload.execution.completionNotes || payload.execution.completionNotes.trim().length < 20) {
      return throwError(() => new Error('Completion notes are required.'));
    }

    const leadId = payload.leadId;

    // THE CONVERSATION FIRST. It is the part a person actually typed, and the part that would be
    // most annoying to lose.
    const recordContact = this.api.contactLead(leadId, {
      channel: this.toConsentChannel(payload.followUpType),
      outcome: payload.execution.outcome,
      notes: [payload.execution.completionNotes.trim(), payload.execution.internalNotes?.trim()]
        .filter(Boolean)
        .join(' \u2014 '),
      occurredAtUtc: this.toUtc(payload.execution.actualContactDate, payload.execution.actualContactTime),
    });

    return recordContact.pipe(
      switchMap(() =>
        this.api.completeFollowUp(payload.followUpId, {
          completionOutcome: payload.execution.outcome!,
          completedAtUtc: new Date().toISOString(),
        }),
      ),
      switchMap(() => {
        if (!payload.nextFollowUp.enabled || !payload.nextFollowUp.date) {
          return of<{ completedAt: string; nextFollowUpId: string | null }>({
            completedAt: new Date().toISOString(),
            nextFollowUpId: null,
          });
        }

        return this.api
          .scheduleFollowUp({
            leadId,
            purpose: payload.nextFollowUp.purpose,
            permittedChannel: this.toConsentChannel(payload.nextFollowUp.type ?? 'Call'),
            nextAction: payload.nextFollowUp.purpose,
            dueAtUtc: this.toUtc(payload.nextFollowUp.date, payload.nextFollowUp.time),
            priority: payload.nextFollowUp.priority ?? 'Medium',
            consentWarningAcknowledged: false,
          })
          .pipe(
            map((created) => ({
              completedAt: new Date().toISOString(),
              nextFollowUpId: created.id,
            })),
          );
      }),
    );
  }

  /**
   * Escalate.
   *
   * A REASSIGNMENT WITH A REASON. There is no escalation state on a follow-up; escalating means
   * handing it to somebody more senior and recording why, which is what `assign` does - and
   * unlike a local status string, the new owner sees it in their own queue.
   */
  escalate(leadId: string, escalation: EscalationValue): Observable<{ escalated: true }> {
    return this.api.getFollowUpPlanner({ page: 1, pageSize: 20, leadId }).pipe(
      switchMap((planner) => {
        const target = planner.followUps.items.find(
          (item) => item.status === 'Scheduled' || item.status === 'Rescheduled',
        );

        if (!target) {
          return throwError(() => new Error('There is no open follow-up to escalate.'));
        }

        const owner = planner.ownerOptions.find((option) => option.label === escalation.escalateTo);
        if (!owner) {
          return throwError(() => new Error('Choose somebody to escalate to.'));
        }

        return this.api.assignFollowUp(target.id, {
          relationshipOwnerUserId: owner.value,
          relationshipOwnerName: owner.label,
          reason: `Escalated: ${escalation.reason}`,
          expectedVersion: target.version,
        });
      }),
      map(() => ({ escalated: true as const })),
    );
  }

  private toConsentChannel(type: string): string {
    switch (type) {
      case 'Call': return 'PhoneCall';
      case 'Email': return 'Email';
      case 'SMS': return 'Sms';
      case 'WhatsApp': return 'WhatsApp';
      default: return 'Email';
    }
  }

  private toUtc(date: string, time: string): string {
    return new Date(`${date}T${time || '09:00'}`).toISOString();
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
  private readonly api = inject(DonorApiService);
  /**
   * The record and follow-up this screen is executing.
   *
   * NO FABRICATED FALLBACKS. `followUpId` used to fall back to the literal 'FUP-2026-00421' and
   * `leadId` to 'LEAD-2026-0142' when the query string carried neither - so arriving without
   * parameters silently executed a follow-up against an invented lead. An absent id is now an
   * empty string, and the screen says it has nothing to execute.
   */
  private readonly requestedLeadId = computed(() => this.params()?.get('leadId') ?? '');
  private readonly requestedDonorId = computed(() => this.params()?.get('donorId') ?? '');

  readonly followUpId = computed(() => this.params()?.get('followUpId') ?? '');
  readonly donorId = computed(() => this.requestedDonorId() || null);
  readonly leadId = computed(() => this.requestedLeadId() || this.requestedDonorId() || '');

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


  /**
   * Confirms the lead is ready to qualify.
   *
   * IT SAVES BEFORE IT NAVIGATES. The old version patched an in-memory lead and moved to Donor
   * 360, so the "Qualified" state existed only in the tab that set it - and Donor 360, reading
   * the server, showed the lead exactly as it had been.
   */
  startQualification(): void {
    const leadId = this.leadId();
    if (!leadId) {
      return;
    }

    this.api
      .qualifyLead(leadId, {
        qualificationNotes: 'Qualification readiness confirmed from follow-up execution.',
        moveToNurture: false,
      })
      .subscribe({
        next: () =>
          this.router.navigate(['/app/fundraising/relationships/donor-360'], {
            queryParams: { leadId, conversion: 'pending' },
          }),
        error: (error: unknown) => this.formError.set(apiErrorMessage(error)),
      });
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
      leadId: this.leadId(),
      followUpType: this.snapshot()?.followUp.type ?? 'Call',
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
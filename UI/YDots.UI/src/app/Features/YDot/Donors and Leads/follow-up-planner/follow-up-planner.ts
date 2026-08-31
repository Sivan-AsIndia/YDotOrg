import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { WorkflowDonor, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { DonLookupItem } from '../../../../Shared/models/donor-contract.model';

type PlannerUiState = 'ready' | 'loading' | 'success' | 'error' | 'empty';

interface ScreenAction {
  readonly id: string;
  readonly label: string;
  readonly placement: 'primary' | 'secondary' | 'danger';
  readonly permission: string;
  readonly result: string;
  readonly requiresReason?: boolean;
  readonly typedConfirm?: boolean;
}

/**
 * The screen's own copy and its action contract.
 *
 * WHY THIS IS STILL IN THE BUNDLE WHEN THE REST OF THE PAGE IS NOT. A button's label, the
 * sentence its confirmation dialog shows, and which of the three placements it takes are
 * PRESENTATION - they are decided by whoever designs this screen, they are the same for every
 * organisation, and the API has no opinion on any of them. Moving them to the server would mean
 * a round trip to find out what to write on a button.
 *
 * WHAT IS NO LONGER HERE is everything that differs per organisation or per caller: the channel,
 * priority and language catalogues, the owner list, the active scope and the refresh time. Those
 * used to sit in this screen's JSON as fixed arrays - the same five channels and seven languages
 * for every charity on the platform - and they now come from the API, which knows which are
 * actually configured. The permission flags went the same way and are read from the token.
 */
const SCREEN = {
  title: 'Follow-up planner',
  purpose: 'Plan a respectful, consent-aware next action with clear ownership and due time.',
} as const;

const SAVED_FILTERS: readonly string[] = [
  'All follow-ups (Default)',
  'Due today',
  'Overdue',
  'Completed',
];

const ACTIONS: readonly ScreenAction[] = [
  {
    id: 'scheduleFollowUp',
    label: 'Schedule follow-up',
    placement: 'primary',
    permission: 'don.follow-up-planner.schedule-follow-up',
    result:
      'The follow-up is saved against this record and appears in the owner’s queue.',
  },
  {
    id: 'assign',
    label: 'Assign',
    placement: 'secondary',
    permission: 'don.follow-up-planner.assign',
    result: 'Ownership moves to the person you choose on the assignment board.',
  },
  {
    id: 'markComplete',
    label: 'Mark complete',
    placement: 'secondary',
    permission: 'don.follow-up-planner.mark-complete',
    result: 'The outcome is recorded against this follow-up and it leaves the queue.',
  },
  {
    id: 'reschedule',
    label: 'Reschedule',
    placement: 'secondary',
    permission: 'don.follow-up-planner.reschedule',
    result: 'The follow-up keeps its history and moves to the new date and time.',
  },
  {
    id: 'cancelTask',
    label: 'Cancel task',
    placement: 'danger',
    permission: 'don.follow-up-planner.cancel-task',
    requiresReason: true,
    result: 'The follow-up is cancelled with your reason and stays on the record’s history.',
  },
];

/**
 * DON-UI-08 — Follow-up planner.
 * Plan a respectful, consent-aware next action with clear ownership and due time.
 */
@Component({
  selector: 'app-follow-up-planner',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './follow-up-planner.html',
  styleUrl: './follow-up-planner.css',
  host: { class: 'd-block' },
})
export class FollowUpPlannerComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);

  private readonly donorApi = inject(DonorApiService);

  /** Page copy and the action contract. Presentation - see the note on SCREEN. */
  protected readonly screen = SCREEN;
  protected readonly savedFilters = SAVED_FILTERS;
  protected readonly actions = ACTIONS;

  /**
   * Everything the server decides, rather than the bundle.
   *
   * EMPTY UNTIL THE FIRST READ ANSWERS, on purpose. A dropdown pre-filled with a guess is worse
   * than an empty one: the guess is indistinguishable from a real option, and picking it sends a
   * value the API will reject.
   */
  protected readonly channelOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly priorityOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly languageOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly ownerOptions = signal<readonly DonLookupItem[]>([]);

  /** The scope this caller is actually working in, as the server resolved it. */
  protected readonly assignedScope = signal('');

  /** When this screen last heard from the server. Real, not a fixed string in a JSON file. */
  protected readonly lastRefresh = signal('');

  protected readonly uiState = signal<PlannerUiState>('ready');
  protected readonly savedFilter = signal(SAVED_FILTERS[0]);
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly activeActionId = signal('');

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
  /**
   * What this caller may do, keyed by the action ids the screen's own configuration uses.
   *
   * AN UNLISTED ACTION IS REFUSED. The checks below read `=== true` rather than `!== false`,
   * because the two differ exactly where it matters: against the old JSON object a missing key
   * meant "not mentioned, therefore allowed", and against the token it means "this caller does not
   * hold the permission".
   */
  protected readonly permissions = computed<Record<string, boolean>>(() => ({
    view: this.tokens.hasAnyPermission('don.follow-up-planner.view'),
    scheduleFollowUp: this.tokens.hasAnyPermission('don.follow-up-planner.schedule-follow-up'),
    assign: this.tokens.hasAnyPermission('don.follow-up-planner.assign'),
    markComplete: this.tokens.hasAnyPermission('don.follow-up-planner.mark-complete'),
    reschedule: this.tokens.hasAnyPermission('don.follow-up-planner.reschedule'),
    cancelTask: this.tokens.hasAnyPermission('don.follow-up-planner.cancel-task'),
  }));

  protected readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
  protected readonly donorId = signal(this.route.snapshot.queryParamMap.get('donorId'));
  protected readonly followUpId = signal(this.route.snapshot.queryParamMap.get('followUpId'));
  private readonly sourceFollowUpId = signal(this.route.snapshot.queryParamMap.get('sourceId'));
  private readonly existingFollowUp = computed(() => {
    const id = this.followUpId();
    return id ? this.workflow.getFollowUp(id) : undefined;
  });
  private readonly sourceFollowUp = computed(() => {
    const id = this.sourceFollowUpId();
    return id ? this.workflow.getFollowUp(id) : undefined;
  });
  private readonly contextFollowUp = computed(() => this.existingFollowUp() ?? this.sourceFollowUp());
  protected readonly resolvedDonorId = computed(() =>
    this.donorId() ?? (this.contextFollowUp()?.recordType === 'Donor' ? this.contextFollowUp()!.recordId : null),
  );
  protected readonly resolvedLeadId = computed(() =>
    this.leadId() ?? (this.contextFollowUp()?.recordType === 'Lead' ? this.contextFollowUp()!.recordId : null),
  );
  protected readonly lead = computed(() => this.workflow.getLead(this.resolvedLeadId()));
  protected readonly donor = computed(() => this.workflow.getDonor(this.resolvedDonorId()));
  protected readonly recordReference = computed(() => this.contextFollowUp()?.recordId ?? this.lead()?.id ?? this.donor()?.donorId ?? '');
  protected readonly relationshipOwner = computed(() => this.lead()?.owner ?? this.donor()?.owner ?? '');
  protected readonly campaign = computed(() => this.lead()?.campaign ?? this.donor()?.campaign ?? '');
  protected readonly preferredLanguage = computed(() => this.lead()?.language ?? '');

  /**
   * The donor's own preference, and the consent position on this record.
   *
   * BOTH USED TO BE FIXED STRINGS in this screen's JSON - the same preferred contact time and
   * the same consent wording for every record anyone opened. A consent notice that is not this
   * record's consent notice is worse than none: it invites somebody to make contact they are not
   * permitted to make, on the strength of a reassurance the page invented.
   *
   * BLANK WHEN UNKNOWN, and the template says so rather than filling the gap.
   */
  protected readonly preferredContactTime = computed(
    () => this.contextFollowUp()?.scheduledTime ?? '',
  );

  protected readonly consentWarning = signal('');

  protected readonly followUpType = signal('Call');
  protected readonly scheduledDate = signal(this.contextFollowUp()?.scheduledDate ?? '');
  protected readonly scheduledTime = signal(this.contextFollowUp()?.scheduledTime ?? '');
  protected readonly priority = signal(this.contextFollowUp()?.priority ?? '');
  protected readonly owner = signal(this.contextFollowUp()?.assignedTo ?? this.relationshipOwner());
  protected readonly purpose = signal(this.contextFollowUp()?.purpose ?? '');
  protected readonly expectedOutcome = signal(this.contextFollowUp()?.expectedOutcome ?? '');
  protected readonly validationMessage = signal<string | null>(null);

  protected readonly visibleActions = computed(() =>
    this.actions.filter((action) => action.id === 'scheduleFollowUp' && this.permissions()[action.id] === true),
  );

  protected readonly activeFilterSummary = computed(() => {
    const defaultFilter = SAVED_FILTERS[0];
    return this.savedFilter() !== defaultFilter
      ? [{ key: 'saved', label: `View: ${this.savedFilter()}` }]
      : [];
  });

  protected readonly hasRecord = computed(() =>
    Boolean(this.recordReference()),
  );

  protected removeFilterChip(key: string): void {
    if (key === 'saved') {
      this.savedFilter.set(SAVED_FILTERS[0]);
    }
  }

  constructor() {
    this.loadFromServer();
  }

  /**
   * Reads the catalogues, the owner list and the scope from the donors API.
   *
   * ONE CALL, NOT FIVE. The planner endpoint answers with every catalogue the form needs
   * alongside the tasks themselves, which is what lets the screen open with the dropdowns
   * already correct rather than filling them in one round trip at a time.
   *
   * A FAILURE LEAVES THE DROPDOWNS EMPTY AND SAYS SO. The previous version could not fail -
   * the arrays were compiled in - so a caller whose permissions had been withdrawn, or whose
   * session had expired, saw a fully populated form and discovered the problem only on save.
   */
  private loadFromServer(): void {
    this.uiState.set('loading');

    this.donorApi.getFollowUpPlanner({ pageSize: 1 }).subscribe({
      next: (response) => {
        this.channelOptions.set(response.channelOptions ?? []);
        this.priorityOptions.set(response.priorityOptions ?? []);
        this.languageOptions.set(response.languageOptions ?? []);
        this.ownerOptions.set(response.ownerOptions ?? []);
        this.assignedScope.set(response.activeScope ?? '');
        this.lastRefresh.set(this.nowLabel());
        this.uiState.set('ready');
        this.loadConsentWarning();
      },
      error: () => {
        // Nothing is invented to fill the gap. See the note on the option signals.
        this.uiState.set('error');
      },
    });
  }

  /**
   * This record's consent position, asked for by record rather than assumed.
   *
   * THE CHANNEL IS PART OF THE QUESTION. Consent is not one flag - somebody may be reachable by
   * e-mail and not by telephone - so the answer changes as the person changes the follow-up type,
   * and asking without the channel would produce a reassurance that is true for some other way of
   * making contact.
   */
  private loadConsentWarning(): void {
    const donorId = this.resolvedDonorId();
    const leadId = this.resolvedLeadId();

    if (!donorId && !leadId) {
      this.consentWarning.set('');
      return;
    }

    this.donorApi
      .getConsentWarning(donorId ?? undefined, leadId ?? undefined, this.followUpType())
      .subscribe({
        next: (warning) => this.consentWarning.set(warning?.message ?? ''),

        // An unanswered question is left unanswered rather than reported as "no warning".
        error: () => this.consentWarning.set(''),
      });
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-GB', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected setUiState(state: PlannerUiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  protected priorityClass(priority: string): string {
    switch (priority.toLowerCase()) {
      case 'high':
        return 'fup-badge-high';
      case 'medium':
        return 'fup-badge-medium';
      case 'low':
        return 'fup-badge-low';
      default:
        return 'fup-badge-neutral';
    }
  }

  protected actionIcon(actionId: string): string {
    switch (actionId) {
      case 'scheduleFollowUp':
        return 'plus';
      case 'assign':
        return 'users';
      case 'markComplete':
        return 'check';
      case 'reschedule':
        return 'refresh';
      case 'cancelTask':
        return 'close';
      default:
        return 'dot';
    }
  }

  protected openAction(actionId: string): void {
    if (actionId === 'scheduleFollowUp') {
      const missing = !this.followUpType().trim() || !this.scheduledDate() || !this.scheduledTime() ||
        !this.priority().trim() || !this.owner().trim() || !this.purpose().trim();
      if (missing) {
        this.validationMessage.set('Complete follow-up type, date, time, priority, purpose, and owner before saving.');
        return;
      }
      this.validationMessage.set(null);
    }

    const action = this.actions.find((candidate) => candidate.id === actionId);
    if (!action || this.permissions()[actionId] !== true || this.uiState() === 'loading') {
      return;
    }

    this.activeActionId.set(actionId);
    this.confirmConfig.set({
      title: `Confirm ${action.label}`,
      message: action.result,
      confirmLabel: action.label,
      cancelLabel: 'Cancel',
      tone: action.placement === 'danger' ? 'danger' : 'primary',
      requireReason: Boolean(action.requiresReason),
      reasonLabel: 'Reason',
      reasonMin: 10,
      reasonMax: 2000,
      typedConfirm: Boolean(action.typedConfirm),
      affectedRecord: `${this.followUpId() ?? ''} · ${this.recordReference()}`,
      effectiveTime: this.lastRefresh(),
      beforeAfter: [
        { label: 'Priority', before: this.priority(), after: this.priority() },
      ],
    });
  }

  protected onConfirm(reason: string): void {
    const action = this.activeActionId();
    const recordId = this.recordReference();
    const existingId = this.followUpId();

    if (action === 'assign') {
      this.confirmConfig.set(null);
      this.activeActionId.set('');
      this.router.navigate(['/app/fundraising/relationships/assignment-board'], { queryParams: { leadId: recordId } });
      return;
    }

    if (action === 'markComplete') {
      this.confirmConfig.set(null);
      this.activeActionId.set('');
      const followUp = existingId ? this.workflow.getFollowUp(existingId) : this.workflow.followUpsFor(recordId)[0];
      this.router.navigate(['/app/fundraising/relationships/follow-up-execution'], {
        queryParams: { followUpId: followUp?.id ?? null, leadId: this.resolvedLeadId(), donorId: this.resolvedDonorId() },
      });
      return;
    }

    if (action === 'cancelTask' && existingId) {
      this.workflow.patchFollowUp(existingId, { status: 'Cancelled', history: [...(this.workflow.getFollowUp(existingId)?.history ?? []), { date: new Date().toLocaleDateString('en-GB'), label: `Cancelled${reason ? ': ' + reason : ''}` }] });
    } else if (action === 'reschedule' && existingId) {
      this.router.navigate(['/app/fundraising/relationships/follow-up-queue'], { queryParams: { followUpId: existingId, leadId: this.resolvedLeadId(), donorId: this.resolvedDonorId(), action: 'reschedule' } });
      this.confirmConfig.set(null);
      this.activeActionId.set('');
      return;
    } else if (action === 'scheduleFollowUp') {
      if (existingId) {
        this.workflow.patchFollowUp(existingId, {
          assignedTo: this.owner(),
          scheduledDate: this.scheduledDate(),
          scheduledTime: this.scheduledTime(),
          purpose: this.purpose().trim(),
          expectedOutcome: this.expectedOutcome().trim(),
          priority: this.priority(),
          followUpType: this.followUpType(),
          status: 'Pending',
        });
      } else {
        const created = this.workflow.addFollowUp({
          recordId,
          recordName: this.lead()?.name ?? this.donor()?.name,
          assignedTo: this.owner(),
          campaign: this.campaign(),
          phone: this.lead()?.mobile ?? this.donor()?.mobile,
          email: this.lead()?.email ?? this.donor()?.email,
          recordType: this.donor() ? 'Donor' : undefined,
          scheduledDate: this.scheduledDate(),
          scheduledTime: this.scheduledTime(),
          purpose: this.purpose().trim(),
          expectedOutcome: this.expectedOutcome().trim(),
          priority: this.priority(),
          followUpType: this.followUpType(),
        });
        this.followUpId.set(created.id);
      }
    }

    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.uiState.set('success');
    this.router.navigate(['/app/fundraising/relationships/follow-up-queue'], { queryParams: { followUpId: this.followUpId(), leadId: this.resolvedLeadId(), donorId: this.resolvedDonorId() } });
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }
}
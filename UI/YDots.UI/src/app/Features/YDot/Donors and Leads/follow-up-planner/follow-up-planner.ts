import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  DonLookupItem,
  FollowUp as ApiFollowUp,
} from '../../../../Shared/models/donor-contract.model';

type PlannerUiState = 'ready' | 'loading' | 'success' | 'error' | 'empty';

interface OwnerOption {
  readonly reference: string;
  readonly label: string;
  readonly context: string;
  readonly initials: string;
}

interface ScreenAction {
  readonly id: string;
  readonly label: string;
  readonly placement: 'primary' | 'secondary' | 'danger';
  readonly permission: string;
  readonly allowedState: string;
  readonly result: string;
  readonly requiresReason?: boolean;
  readonly typedConfirm?: boolean;
}

interface ScreenData {
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
  };
  readonly permissions: Readonly<Record<string, boolean>>;
  readonly followUpReference: string;
  readonly donorOrLeadReference: string;
  readonly relationshipOwner: string;
  readonly purpose: string;
  readonly permittedChannel: string;
  readonly preferredLanguage: string;
  readonly preferredContactTime: string;
  readonly nextAction: string;
  readonly dueDate: string;
  readonly priority: string;
  readonly notes: string;
  readonly consentWarning: string;
  readonly assignedScope: string;
  readonly priorities: readonly string[];
  readonly channels: readonly string[];
  readonly languages: readonly string[];
  readonly ownerOptions: readonly OwnerOption[];
  readonly savedFilters: readonly string[];
  readonly fieldContracts: readonly {
    readonly label: string;
    readonly control: string;
    readonly required: boolean;
    readonly visibility: string;
  }[];
  readonly actions: readonly ScreenAction[];
}

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
  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);

  /**
   * The Follow-Up Planner - the destination of every "Schedule Follow-Up" in the document.
   *
   * WHAT THIS REPLACES. `follow-up-planner.json` supplied the screen's permissions, its default
   * channel, its default priority, its purpose text and a `donorOrLeadReference` used whenever
   * the in-memory store had nothing - which was on every fresh load. Scheduling then called
   * `workflow.addFollowUp`, so a follow-up planned here existed only until the tab was closed
   * and never appeared in anybody else's Follow-Up Queue.
   */

  protected readonly uiState = signal<PlannerUiState>('loading');
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly activeActionId = signal('');
  protected readonly savedFilter = signal('All follow-ups (Default)');
  protected readonly savedFilters = signal<readonly string[]>(['All follow-ups (Default)']);

  /** The caller's permitted actions, as the server listed them. */
  protected readonly permissions = signal<Record<string, boolean>>({
    scheduleFollowUp: false,
    assign: false,
    markComplete: false,
    reschedule: false,
    cancelTask: false,
  });

  protected readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
  protected readonly donorId = signal(this.route.snapshot.queryParamMap.get('donorId'));
  protected readonly followUpId = signal(this.route.snapshot.queryParamMap.get('followUpId'));

  /** The follow-up being edited, when the screen was opened on one. */
  protected readonly existing = signal<ApiFollowUp | null>(null);

  protected readonly channelOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly priorityOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly ownerOptions = signal<readonly DonLookupItem[]>([]);

  protected readonly resolvedDonorId = computed(() => this.donorId() ?? this.existing()?.donorId ?? null);
  protected readonly resolvedLeadId = computed(() => this.leadId() ?? this.existing()?.leadId ?? null);

  protected readonly recordReference = computed(
    () => this.existing()?.leadReference ?? this.existing()?.donorReference ?? this.resolvedLeadId() ?? this.resolvedDonorId() ?? '',
  );
  protected readonly relationshipOwner = computed(() => this.existing()?.relationshipOwnerName ?? '');
  protected readonly campaign = computed(() => '');
  protected readonly preferredLanguage = computed(() => this.existing()?.preferredLanguage ?? '');

  protected readonly followUpType = signal('Email');
  protected readonly scheduledDate = signal('');
  protected readonly scheduledTime = signal('');
  protected readonly priority = signal('Medium');
  protected readonly owner = signal('');
  protected readonly purpose = signal('');
  protected readonly expectedOutcome = signal('');
  protected readonly validationMessage = signal<string | null>(null);

  /**
   * The consent warning for the chosen channel.
   *
   * IT IS THE SERVER'S, AND IT BLOCKS. A follow-up on a channel the person has withdrawn consent
   * for is refused by the API; asking first means the refusal is a sentence beside the channel
   * picker rather than a 400 after the confirm dialog.
   */
  protected readonly consentWarning = signal<string>('');

  /** When the screen last read the server, for the header's freshness line. */
  protected readonly lastRefresh = signal('');

  /** Whose records this caller may see, as the server described it. */
  protected readonly activeScope = signal('');

  constructor() {
    this.load();
  }

  private load(): void {
    this.uiState.set('loading');

    this.api
      .getFollowUpPlanner({ page: 1, pageSize: 50, leadId: this.leadId(), donorId: this.donorId() })
      .subscribe({
        next: (response) => {
          this.channelOptions.set(response.channelOptions);
          this.priorityOptions.set(response.priorityOptions);
          this.ownerOptions.set(response.ownerOptions);

          // VERBS: ['Schedule follow-up','View','Assign','Mark complete','Reschedule','Cancel task'].
          const permitted = response.permittedActions ?? [];
          this.permissions.set({
            scheduleFollowUp: permitted.includes('Schedule follow-up'),
            assign: permitted.includes('Assign'),
            markComplete: permitted.includes('Mark complete'),
            reschedule: permitted.includes('Reschedule'),
            cancelTask: permitted.includes('Cancel task'),
          });

          // Editing an existing follow-up: fill the form from it.
          const editing = this.followUpId()
            ? response.followUps.items.find((item) => item.id === this.followUpId())
            : null;

          if (editing) {
            this.existing.set(editing);
            this.followUpType.set(editing.permittedChannel);
            this.priority.set(editing.priority);
            this.owner.set(editing.relationshipOwnerUserId);
            this.purpose.set(editing.purpose ?? '');
            this.expectedOutcome.set(editing.nextAction ?? '');

            if (editing.dueAtUtc) {
              const due = new Date(editing.dueAtUtc);
              this.scheduledDate.set(this.toDateInput(due));
              this.scheduledTime.set(this.toTimeInput(due));
            }
            if (editing.consentWarning?.hasWarning) {
              this.consentWarning.set(editing.consentWarning.message);
            }
          } else {
            this.followUpType.set(response.channelOptions[0]?.value ?? 'Email');
            this.priority.set(response.priorityOptions[0]?.value ?? 'Medium');
          }

          this.activeScope.set(response.activeScope);
          this.lastRefresh.set(new Date().toLocaleString('en-GB', {
            day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
          }));

          this.uiState.set('ready');
          this.checkConsent();
        },
        error: (error: unknown) => {
          this.uiState.set('error');
          this.toast.show('Planner unavailable', apiErrorMessage(error), 'error');
        },
      });
  }

  /** Re-asks the server whether the chosen channel is permitted for this person. */
  protected checkConsent(): void {
    const leadId = this.resolvedLeadId();
    const donorId = this.resolvedDonorId();
    if (!leadId && !donorId) {
      return;
    }

    this.api
      .getConsentWarning(donorId ?? undefined, leadId ?? undefined, this.followUpType())
      .subscribe({
        next: (warning) => this.consentWarning.set(warning.hasWarning ? warning.message : ''),
        error: () => this.consentWarning.set(''),
      });
  }

  private toDateInput(value: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
  }

  private toTimeInput(value: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(value.getHours())}:${pad(value.getMinutes())}`;
  }

  private toDueUtc(): string {
    return new Date(`${this.scheduledDate()}T${this.scheduledTime() || '09:00'}`).toISOString();
  }

  /**
   * The actions this screen offers.
   *
   * DECLARED HERE, GATED BY THE SERVER. The list used to come from the JSON file's `actions`
   * array, which meant the buttons a person saw were whatever the bundle said rather than what
   * their token allows. The labels are the screen's; the gate is `permissions()`.
   */
  private readonly actionCatalogue = [
    {
      id: 'scheduleFollowUp',
      label: 'Schedule follow-up',
      result: 'The follow-up is scheduled and appears in the owner\u2019s Follow-Up Queue.',
      placement: 'primary',
      requiresReason: false,
    },
    {
      id: 'reschedule',
      label: 'Reschedule',
      result: 'The follow-up moves to a new date and the reason is recorded.',
      placement: 'primary',
      requiresReason: true,
    },
    {
      id: 'cancelTask',
      label: 'Cancel follow-up',
      result: 'The follow-up is cancelled. The reason is recorded against it.',
      placement: 'danger',
      requiresReason: true,
    },
  ] as const;

  protected readonly visibleActions = computed(() =>
    this.actionCatalogue.filter((action) => this.permissions()[action.id] === true),
  );

  protected readonly activeFilterSummary = computed(() => {
    const defaultFilter = this.savedFilters()[0];
    return this.savedFilter() !== defaultFilter
      ? [{ key: 'saved', label: `View: ${this.savedFilter()}` }]
      : [];
  });

  protected readonly hasRecord = computed(() =>
    Boolean(this.recordReference()),
  );

  protected removeFilterChip(key: string): void {
    if (key === 'saved') {
      this.savedFilter.set(this.savedFilters()[0] ?? 'All follow-ups (Default)');
    }
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

    const action = this.actionCatalogue.find((candidate) => candidate.id === actionId);
    if (!action || this.permissions()[actionId] !== true || this.uiState() === 'loading') {
      return;
    }

    // A CHANNEL THE PERSON HAS WITHDRAWN IS REFUSED BY THE SERVER, so it is refused here first -
    // the alternative is a confirm dialog, a typed reason and then a 400.
    if (actionId === 'scheduleFollowUp' && this.consentWarning()) {
      this.validationMessage.set(this.consentWarning());
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
      typedConfirm: false,
      affectedRecord: `${this.existing()?.followUpReference ?? 'New follow-up'} · ${this.recordReference()}`,
      effectiveTime: `${this.scheduledDate()} ${this.scheduledTime()}`.trim() || 'On confirmation',
      beforeAfter: [
        { label: 'Priority', before: this.existing()?.priority ?? '—', after: this.priority() },
        { label: 'Due', before: this.existing()?.dueAtUtc ?? '—', after: this.scheduledDate() },
      ],
    });
  }

  protected onConfirm(reason: string): void {
    const action = this.activeActionId();
    const existingId = this.followUpId();

    if (action === 'reschedule' && existingId) {
      this.api
        .rescheduleFollowUp(existingId, {
          dueAtUtc: this.toDueUtc(),
          rescheduleReason: reason,
          priority: this.priority(),
          expectedVersion: this.existing()?.version ?? null,
        })
        .subscribe({
          next: () => this.afterWrite('Follow-up rescheduled.'),
          error: (error: unknown) => this.afterError(error),
        });
      return;
    }

    if (action === 'cancelTask' && existingId) {
      this.api
        .cancelFollowUp(existingId, { reason, expectedVersion: this.existing()?.version ?? null })
        .subscribe({
          next: () => this.afterWrite('Follow-up cancelled.'),
          error: (error: unknown) => this.afterError(error),
        });
      return;
    }

    if (action === 'scheduleFollowUp') {
      const owner = this.ownerOptions().find((option) => option.value === this.owner());

      this.api
        .scheduleFollowUp({
          leadId: this.resolvedLeadId(),
          donorId: this.resolvedDonorId(),
          relationshipOwnerUserId: owner?.value ?? null,
          relationshipOwnerName: owner?.label ?? null,
          purpose: this.purpose().trim(),
          permittedChannel: this.followUpType(),
          preferredLanguage: this.preferredLanguage() || null,
          nextAction: this.expectedOutcome().trim(),
          dueAtUtc: this.toDueUtc(),
          priority: this.priority(),

          // FALSE BECAUSE THERE IS NO WARNING. The confirm path above refuses to open when the
          // channel carries one, so acknowledging is never something this screen does silently.
          consentWarningAcknowledged: false,
        })
        .subscribe({
          next: (created) => {
            this.followUpId.set(created.id);
            this.afterWrite('Follow-up scheduled.');
          },
          error: (error: unknown) => this.afterError(error),
        });
      return;
    }

    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }

  private afterWrite(message: string): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.uiState.set('success');
    this.toast.show('Saved', message, 'success');

    // THE DOCUMENT'S DESTINATION: scheduling from the planner lands in the Follow-Up Queue.
    this.router.navigate(['/app/fundraising/relationships/follow-up-queue'], {
      queryParams: {
        followUpId: this.followUpId(),
        leadId: this.resolvedLeadId(),
        donorId: this.resolvedDonorId(),
      },
    });
  }

  private afterError(error: unknown): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.uiState.set('ready');
    this.toast.show('Not saved', apiErrorMessage(error), 'error');
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }
}
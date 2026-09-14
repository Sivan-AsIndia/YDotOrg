import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ElementRef, viewChild, effect } from '@angular/core';
import { ConfirmDialogConfig } from '../../../../Shared/models/donors-leads.model';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import {
  DonLookupItem,
  FollowUp as ApiFollowUp,
} from '../../../../Shared/models/donor-contract.model';

type PlannerUiState = 'ready' | 'loading' | 'success' | 'error' | 'empty';

/**
 * DON-UI-08 — Follow-up planner.
 * Plan a respectful, consent-aware next action with clear ownership and due time.
 */
@Component({
  selector: 'app-follow-up-planner',
  imports: [CommonModule, FormsModule],
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

  protected readonly resolvedDonorId = computed(
    () => this.donorId() ?? this.existing()?.donorId ?? null,
  );
  protected readonly resolvedLeadId = computed(
    () => this.leadId() ?? this.existing()?.leadId ?? null,
  );

  protected readonly recordReference = computed(
    () =>
      this.existing()?.leadReference ??
      this.existing()?.donorReference ??
      this.resolvedLeadId() ??
      this.resolvedDonorId() ??
      '',
  );
  protected readonly relationshipOwner = computed(
    () => this.existing()?.relationshipOwnerName ?? '',
  );
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

  protected readonly saving = signal(false);
  protected readonly page = signal(1);
  protected readonly totalPages = signal(1);
  protected readonly rows = signal<readonly ApiFollowUp[]>([]);
  protected readonly modalReason = signal('');
  protected readonly record = signal({ name: '—', email: '—', phone: '—', owner: 'Unassigned' });
  protected readonly searches = signal<Record<string, string>>({});
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('confirmation');
  private readonly showConfirmation = effect(() => {
    const dialog = this.dialog()?.nativeElement;
    if (this.confirmConfig() && dialog && !dialog.open) dialog.showModal();
  });
  protected readonly reasonValid = computed(
    () =>
      !this.confirmConfig()?.requireReason ||
      (this.modalReason().trim().length >= 10 && this.modalReason().trim().length <= 2000),
  );
  protected searchOptions(key: string, value: string): void {
    this.searches.update((current) => ({ ...current, [key]: value }));
  }
  protected options(key: string, values: readonly DonLookupItem[]): readonly DonLookupItem[] {
    const search = (this.searches()[key] ?? '').toLowerCase().trim();
    const selected =
      key === 'owner' ? this.owner() : key === 'channel' ? this.followUpType() : this.priority();
    return values.filter(
      (item) => item.value === selected || item.label.toLowerCase().includes(search),
    );
  }
  protected changePage(delta: number): void {
    const page = this.page() + delta;
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    this.load();
  }
  protected selectRecord(item: ApiFollowUp): void {
    this.followUpId.set(item.id);
    this.leadId.set(item.leadId);
    this.donorId.set(item.donorId);
    this.load();
  }
  protected cancelPlanner(): void {
    this.router.navigate(['/app/fundraising/relationships/follow-up-queue']);
  }
  private loadRecord(): void {
    const lead = this.resolvedLeadId();
    const donor = this.resolvedDonorId();
    if (lead)
      this.api.getLead(lead).subscribe({
        next: (r) =>
          this.record.set({
            name: [r.firstName, r.lastName].filter(Boolean).join(' '),
            email: r.emailAddress || '—',
            phone: r.mobileNumber || '—',
            owner: r.ownerName || 'Unassigned',
          }),
        error: () =>
          this.toast.show(
            'Record details unavailable',
            'Contact details could not be loaded.',
            'error',
          ),
      });
    else if (donor)
      this.api.getDonor(donor).subscribe({
        next: (r) =>
          this.record.set({
            name: r.displayName,
            email: r.primaryEmail || '—',
            phone: r.primaryPhone || '—',
            owner: r.relationshipOwnerName || 'Unassigned',
          }),
        error: () =>
          this.toast.show(
            'Record details unavailable',
            'Contact details could not be loaded.',
            'error',
          ),
      });
  }

  constructor() {
    this.load();
  }

  protected load(): void {
    this.uiState.set('loading');

    this.api
      .getFollowUpPlanner({
        page: this.page(),
        pageSize: 10,
        leadId: this.leadId(),
        donorId: this.donorId(),
      })
      .subscribe({
        next: (response) => {
          this.rows.set(response.followUps.items);
          this.totalPages.set(response.followUps.totalPages);
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
          this.lastRefresh.set(
            new Date().toLocaleString('en-GB', {
              day: '2-digit',
              month: 'short',
              year: 'numeric',
              hour: '2-digit',
              minute: '2-digit',
            }),
          );

          if (this.followUpId() && !editing) {
            if (response.followUps.hasNextPage) {
              this.page.update((p) => p + 1);
              this.load();
              return;
            }
            this.uiState.set('error');
            return;
          }
          this.loadRecord();
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
  private consentRequest = 0;
  protected checkConsent(): void {
    const request = ++this.consentRequest;
    this.consentWarning.set('Checking channel consent…');
    const leadId = this.resolvedLeadId();
    const donorId = this.resolvedDonorId();
    if (!leadId && !donorId) {
      this.consentWarning.set('');
      return;
    }

    this.api
      .getConsentWarning(donorId ?? undefined, leadId ?? undefined, this.followUpType())
      .subscribe({
        next: (warning) => {
          if (request === this.consentRequest)
            this.consentWarning.set(warning.hasWarning ? warning.message : '');
        },
        error: () => {
          if (request === this.consentRequest)
            this.consentWarning.set(
              'Consent could not be verified. Select the channel again to retry.',
            );
        },
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
    this.actionCatalogue.filter(
      (action) =>
        this.permissions()[action.id] === true &&
        (action.id === 'scheduleFollowUp' ? !this.existing() : !!this.existing()),
    ),
  );

  protected readonly activeFilterSummary = computed(() => {
    const defaultFilter = this.savedFilters()[0];
    return this.savedFilter() !== defaultFilter
      ? [{ key: 'saved', label: `View: ${this.savedFilter()}` }]
      : [];
  });

  protected readonly hasRecord = computed(() => Boolean(this.recordReference()));

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
    if (this.saving()) return;
    if (actionId === 'scheduleFollowUp' || actionId === 'reschedule') {
      const missing =
        !this.scheduledDate() ||
        !this.scheduledTime() ||
        !this.priority().trim() ||
        (actionId === 'scheduleFollowUp' &&
          (!this.followUpType().trim() || !this.owner().trim() || !this.purpose().trim()));
      if (missing) {
        this.validationMessage.set(
          actionId === 'reschedule'
            ? 'Complete date, time, and priority before rescheduling.'
            : 'Complete follow-up type, date, time, priority, purpose, and owner before saving.',
        );
        return;
      }
      if (
        !this.hasRecord() ||
        !Number.isFinite(new Date(`${this.scheduledDate()}T${this.scheduledTime()}`).getTime())
      ) {
        this.validationMessage.set('Select a record and enter a valid date and time.');
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

    this.modalReason.set('');
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
    if (this.saving() || !this.confirmConfig() || !this.reasonValid()) return;
    this.saving.set(true);
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

    this.saving.set(false);
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }

  private afterWrite(message: string): void {
    this.saving.set(false);
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
    this.saving.set(false);
    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.uiState.set('ready');
    this.toast.show('Not saved', apiErrorMessage(error), 'error');
  }

  protected onCancel(): void {
    if (this.saving()) return;
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }
}

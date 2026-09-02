import { CommonModule } from '@angular/common';
import { Component, Input, OnInit, computed, effect, inject, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { CampaignStatus } from '../../../../Shared/models/campaign.model';
import { ActionConfig, ActionId, CloseRequestRecord, Outcome, PauseResumePermissions, ViewState } from '../../../../Shared/models/pause-resume.model';
import { CampaignStoreService } from '../../../../Shared/services/campaign-store.service';
import { AttributionStoreService } from '../../../../Shared/services/attribution-store.service';
import { TrackingAssetStoreService } from '../../../../Shared/services/tracking-asset-store.service';
import { CloseRequestStoreService } from '../../../../Shared/services/close-request-store.service';
import { CurrentUserService } from '../../../../Shared/services/current-user.service';
import { PeopleDirectoryService } from '../../../../Shared/services/people-directory.service';

/**
 * Pause, resume and close campaign.
 *
 *  Embedded in    : Campaign detail, opened as a popup from its "Manage
 *                   lifecycle" button — not a standalone routed page.
 *  Purpose        : Control lifecycle changes without losing attribution,
 *                   donation or audit history.
 *  View permission: cam.pause-resume-and-close-campaign.view
 *  Primary action : Pause
 *
 *  The lifecycle "Current state" is the shared CampaignStatus read/written
 *  through CampaignStoreService, so a Pause / Resume / Approve close here is
 *  visible on the other campaign pages immediately. Open donation intents and
 *  active tracking assets are derived live from the shared attribution and
 *  tracking stores — never entered by hand. Permissions come from the shared
 *  CurrentUserService, and the same user id enforces the independent-approver
 *  rule between Request close and Approve close.
 */
@Component({
  selector: 'app-pause-resume-close-campaign',
  imports: [CommonModule, FormsModule],
  templateUrl: './pause-resume-and-close-campaign.html',
  styleUrl: './pause-resume-and-close-campaign.css',
})
export class PauseResumeCloseCampaignComponent implements OnInit {
  private readonly campaignStore = inject(CampaignStoreService);
  private readonly attributionStore = inject(AttributionStoreService);
  private readonly trackingStore = inject(TrackingAssetStoreService);
  private readonly closeStore = inject(CloseRequestStoreService);
  private readonly currentUser = inject(CurrentUserService);

  protected readonly operatingTimeZone = 'Asia/Kolkata';

  /** Stable campaign reference — supplied by the host (Campaign detail), defaulting to the
   *  seeded demo campaign when opened without one. The host owns the popup's open/close
   *  state and its own close affordance (backdrop click + ✕ button); this component has
   *  no closing UI of its own. */
  @Input() campaignRef = 'CAMP-2025-0011';

  /** Notifies the host (Campaign detail) whenever this component's own action off-canvas
   *  opens or closes, so the host can hide its lifecycle popup while the action panel is up
   *  and restore it when the panel closes. */
  readonly panelOpenChange = output<boolean>();

  /* ---------------- shared session (dev switcher reuses the same user profiles) ---------------- */

  /**
   * The session switcher's options.
   *
   * EMPTY, because there is no switcher any more. This listed five invented profiles and the
   * control beside it called `setProfile('super-admin')`, which granted every campaign permission
   * in the interface. Who the caller is comes from their token.
   */
  protected readonly userProfiles: readonly { key: string; name: string; role: string }[] = [];
  protected readonly currentUserRef = computed(() => this.currentUser.reference());
  protected readonly currentUserName = computed(() => this.currentUser.current().name);
  protected readonly currentProfileKey = computed(() => this.currentUser.current().key);
  protected setUserProfile(key: string): void {
    this.currentUser.setProfile(key);
    this.closeActionPanel();
  }

  /* ---------------- effective permissions ---------------- */

  protected readonly permissions = computed<PauseResumePermissions>(() => ({
    view: this.currentUser.hasPermission('cam.campaigns.view'),
    activate: this.currentUser.hasPermission('cam.campaigns.activate'),
    pause: this.currentUser.hasPermission('cam.campaigns.pause'),
    resume: this.currentUser.hasPermission('cam.campaigns.resume'),
    requestClose: this.currentUser.hasPermission('cam.campaigns.request-close'),
    approveClose: this.currentUser.hasPermission('cam.campaigns.close'),
    cancelDraft: this.currentUser.hasPermission('cam.campaigns.delete-draft'),
  }));

  protected readonly isViewOnly = computed(() => {
    const p = this.permissions();
    return p.view && !p.activate && !p.pause && !p.resume && !p.requestClose && !p.approveClose && !p.cancelDraft;
  });

  /* ---------------- campaign + lifecycle state (LIVE from the shared campaign store) ---------------- */

  protected readonly campaign = computed(() => this.campaignStore.get(this.campaignRef) ?? null);
  /** The shared lifecycle "Current state" — never a local copy (fixes the staleness bug). */
  protected readonly currentState = computed<CampaignStatus | null>(() => this.campaign()?.status ?? null);

  /**
   * Owner and approver names, resolved from IAM.
   *
   * THIS SCREEN NEEDED IT MOST. It displays who requested a campaign closure and who approved it,
   * and those two names are the record of a decision that stops a campaign taking donations. Both
   * were being read out of a seven-entry map in this file - including a 'Guest Reviewer' who could
   * appear to have approved a closure.
   */
  private readonly people = inject(PeopleDirectoryService);
  protected ownerName(ref: string): string {
    return this.people.name(ref);
  }

  /* ---------------- close-request record (LIVE from the shared close-request store) ---------------- */

  // A campaign with no close request has no record, so this really is nullable — see the note
  // on `proposedTransition` in campaign-detail for why inference does not see that.
  protected readonly closeRequest = computed<CloseRequestRecord | null>(
    () => this.closeStore.snapshot()[this.campaignRef] ?? null);
  protected readonly lifecycleHistory = computed(() => {
    // Read the store signal so the timeline re-renders when history is appended.
    this.closeStore.snapshot();
    return this.closeStore.history(this.campaignRef);
  });

  /* ---------------- derived dependencies ---------------- */

  /** External settlement check reachability. Open donation intents come from this dependent
   *  service; when it is down the count is unknown and drives the dependency-failure state,
   *  kept strictly separate from the locally-known Active tracking assets count. */
  private readonly settlementReachable = signal(true);

  /** Open donation intents — derived from the shared attribution store, filtered to this campaign
   *  and to donations not yet settled/reconciled. Null = settlement service unreachable. */
  protected readonly openDonationIntents = computed<readonly { reference: string; note: string }[] | null>(() => {
    if (!this.settlementReachable()) {
      return null;
    }
    const name = this.campaign()?.name ?? '';
    if (!name) {
      return [];
    }
    return this.attributionStore
      .forCampaign(name)
      .filter((r) => r.reconciliation !== 'Reconciled' || r.lifecycle !== 'Reconciled')
      .map((r) => ({ reference: r.reference, note: `${r.reconciliation} · ${r.lifecycle}` }));
  });
  protected readonly openDonationIntentsCount = computed<number | null>(() => this.openDonationIntents()?.length ?? null);
  protected readonly settlementUnavailable = computed(() => this.openDonationIntents() === null);

  /** Active tracking assets — derived from the shared tracking store: this campaign, active + approved. */
  protected readonly activeTrackingAssets = computed(() =>
    this.trackingStore
      .forCampaign(this.campaignRef)
      .filter((a) => a.assetStatus === 'Active' && a.approvalState === 'Approved'),
  );
  protected readonly activeTrackingAssetsCount = computed(() => this.activeTrackingAssets().length);

  /** Financial exceptions — derived from the shared attribution store: unmatched settlements. */
  protected readonly financialExceptionsCount = computed(() => {
    const name = this.campaign()?.name ?? '';
    if (!name) {
      return 0;
    }
    return this.attributionStore.forCampaign(name).filter((r) => r.reconciliation === 'Unmatched').length;
  });

  /* ---------------- actions ---------------- */

  protected readonly actions: readonly ActionConfig[] = [
    {
      id: 'activate',
      label: 'Activate campaign',
      placement: 'primary',
      permissionKey: 'activate',
      permissionCode: 'cam.campaigns.activate',
      allowedStates: ['Scheduled'],
      requiresReasonCategory: true,
      requiresDetailedReason: false,
      requiresCommunicationImpact: false,
      requiresClosureSummary: false,
      confirmVerb: 'Confirm activate',
      typedConfirm: false,
      description:
        'Brings a scheduled campaign live and begins solicitation and outbound activity. Only this authorised record in effective scope is changed; attribution and audit history are preserved.',
    },
    {
      id: 'pause',
      label: 'Pause campaign',
      placement: 'primary',
      permissionKey: 'pause',
      permissionCode: 'cam.campaigns.pause',
      allowedStates: ['Active'],
      requiresReasonCategory: true,
      requiresDetailedReason: true,
      requiresCommunicationImpact: false,
      requiresClosureSummary: false,
      confirmVerb: 'Confirm pause',
      typedConfirm: false,
      description:
        'Temporarily stops new solicitation and outbound activity. Attribution, donation and audit history are preserved and resume is available at any time.',
    },
    {
      id: 'resume',
      label: 'Resume campaign',
      placement: 'primary',
      permissionKey: 'resume',
      permissionCode: 'cam.campaigns.resume',
      allowedStates: ['Paused'],
      requiresReasonCategory: false,
      requiresDetailedReason: false,
      requiresCommunicationImpact: false,
      requiresClosureSummary: false,
      confirmVerb: 'Confirm resume',
      typedConfirm: false,
      description:
        'Restores the campaign to active solicitation from its current point. Only this authorised record in effective scope is changed.',
    },
    {
      id: 'request_close',
      label: 'Request close',
      placement: 'danger',
      permissionKey: 'requestClose',
      permissionCode: 'cam.campaigns.request-close',
      allowedStates: ['Active', 'Paused'],
      requiresReasonCategory: true,
      requiresDetailedReason: true,
      requiresCommunicationImpact: true,
      requiresClosureSummary: true,
      confirmVerb: 'Confirm request close',
      typedConfirm: true,
      description:
        'Submits the campaign for closure approval. This creates a close request — it does not itself close the campaign. Open donation intents and active tracking assets are surfaced for review first.',
    },
    {
      id: 'approve_close',
      label: 'Approve close',
      placement: 'danger',
      permissionKey: 'approveClose',
      permissionCode: 'cam.campaigns.close',
      allowedStates: ['Active', 'Paused'],
      requiresReasonCategory: true,
      requiresDetailedReason: false,
      requiresCommunicationImpact: false,
      requiresClosureSummary: false,
      confirmVerb: 'Confirm approve close',
      typedConfirm: true,
      description:
        'Records an independent closure decision. Cannot be performed by the person who requested close. Moves the campaign to Closing (or Closed when no dependencies remain).',
    },
  ];

  /**
   * The lifecycle actions offered for the campaign's CURRENT state — not every permitted action.
   * An action must be permitted AND relevant to the current state, so the panel only ever shows the
   * moves that actually apply:
   *   • Scheduled → Activate
   *   • Active    → Pause (never Activate)
   *   • Paused    → Resume, Request close
   *   • a pending close request → Approve close (surfaced on top of the state's own actions)
   */
  protected readonly visibleActions = computed(() => {
    const state = this.currentState();
    const pending = this.hasPendingCloseRequest();
    return this.actions.filter((a) => {
      if (!this.permissions()[a.permissionKey]) {
        return false;
      }
      // Approve close appears only while a close request is awaiting approval — regardless of the
      // underlying Active/Paused state it was requested from.
      if (a.id === 'approve_close') {
        return pending;
      }
      // Request close is offered from Paused only, and only when no request is already pending.
      if (a.id === 'request_close') {
        return state === 'Paused' && !pending;
      }
      // Everything else (Activate / Pause / Resume) shows strictly for its own allowed state.
      return state !== null && a.allowedStates.includes(state);
    });
  });

  /** True when the acting session requested the pending close — blocks self-approval. */
  protected readonly isOwnCloseRequest = computed(() => {
    const rec = this.closeRequest();
    return !!rec?.requestedByRef && rec.requestState === 'Requested' && rec.requestedByRef === this.currentUserRef();
  });

  /** A close request is already pending (drives the duplicate guard on Request close). */
  protected readonly hasPendingCloseRequest = computed(() => this.closeRequest()?.requestState === 'Requested');

  private stateCompatible(action: ActionConfig): boolean {
    const s = this.currentState();
    return s !== null && action.allowedStates.includes(s);
  }

  /** Every action needs compatible state + effective permission + satisfied dependency.
   *  Per the confirmed design (Q1), a non-zero Open-intents / Active-asset count is SURFACED for
   *  Request close, not auto-blocked; approve_close additionally needs a pending request from a
   *  different user; financial exceptions remain a genuine blocking dependency. */
  protected actionIsEligible(action: ActionConfig): boolean {
    if (!this.permissions()[action.permissionKey] || !this.stateCompatible(action)) {
      return false;
    }
    if (action.id === 'approve_close') {
      return this.hasPendingCloseRequest() && !this.isOwnCloseRequest();
    }
    if (action.id === 'request_close') {
      return !this.hasPendingCloseRequest() && this.financialExceptionsCount() === 0;
    }
    return true;
  }

  protected ineligibleReason(action: ActionConfig): string {
    if (!this.stateCompatible(action)) {
      return `Not available from ${this.currentState() ?? '—'} state.`;
    }
    if (action.id === 'request_close') {
      if (this.hasPendingCloseRequest()) {
        return 'A close request is already pending approval.';
      }
      if (this.financialExceptionsCount() > 0) {
        return 'Unresolved financial exceptions must be cleared first.';
      }
    }
    if (action.id === 'approve_close') {
      if (!this.hasPendingCloseRequest()) {
        return 'No close request is awaiting approval.';
      }
      if (this.isOwnCloseRequest()) {
        return `This close cannot be approved by the person who requested it (${this.currentUserName()}).`;
      }
    }
    return '';
  }

  /* ---------------- top-level view state ---------------- */

  protected readonly viewState = signal<ViewState>('loading');
  protected setViewState(state: ViewState): void {
    this.viewState.set(state);
  }

  /** Why the last transition was refused — the server's own message, shown by the 'error' state. */
  protected readonly failureMessage = signal('');
  protected dismissFailure(): void {
    this.failureMessage.set('');
    this.viewState.set(this.campaign() ? 'ready' : 'empty');
  }

  /* ---------------- concurrency snapshot ---------------- */

  private readonly loadedStatus = signal<CampaignStatus | null>(null);
  private readonly loadedRequestVersion = signal<number | null>(null);
  /**
   * Re-baselines the concurrency snapshot.
   *
   * `landedOn` EXISTS BECAUSE THE STORE IS NOW ASYNCHRONOUS. After a committed transition the
   * campaign's new status arrives with the list refresh behind it, so reading `currentState()`
   * here would capture the state the campaign was in BEFORE the change - and the moment the
   * refresh landed, `isStale()` would compare that against the new one and report a conflict on
   * the change this panel had just made. Pass the state the transition landed on; omit it
   * everywhere the snapshot is genuinely being taken from the record.
   */
  private syncSnapshot(landedOn?: CampaignStatus | null): void {
    this.loadedStatus.set(landedOn ?? this.currentState());
    this.loadedRequestVersion.set(this.closeRequest()?.version ?? null);
  }
  private isStale(): boolean {
    const statusChanged = this.loadedStatus() !== null && this.currentState() !== this.loadedStatus();
    const versionChanged =
      this.loadedRequestVersion() !== null && (this.closeRequest()?.version ?? null) !== this.loadedRequestVersion();
    return statusChanged || versionChanged;
  }

  constructor() {
    // No-access hides record, fields, counts and actions — reacts live to a session switch on any
    // CAM screen. Never a CSS-only hide.
    effect(() => {
      const canView = this.permissions().view;
      const current = untracked(this.viewState);
      if (!canView && current !== 'no-access' && current !== 'loading') {
        this.viewState.set('no-access');
      } else if (canView && current === 'no-access') {
        this.viewState.set(this.campaign() ? 'ready' : 'empty');
      }
    });

    // Tell the host whenever the action off-canvas opens/closes so it can hide/restore its
    // own lifecycle popup beneath it.
    effect(() => {
      this.panelOpenChange.emit(this.activeAction() !== null);
    });
  }

  /** Runs after Angular applies the @Input campaignRef binding (unlike the constructor,
   *  which runs before inputs are set) — the loading sequence below needs the real,
   *  host-supplied reference, not the field's default. */
  ngOnInit(): void {
    this.closeStore.ensure(this.campaignRef);
    setTimeout(() => {
      if (this.viewState() !== 'loading') {
        return;
      }
      this.syncSnapshot();
      if (!this.permissions().view) {
        this.viewState.set('no-access');
      } else if (!this.campaign()) {
        this.viewState.set('empty');
      } else {
        this.viewState.set('ready');
      }
    }, 400);
  }

  /* ---------------- header helpers ---------------- */

  protected readonly lastRefreshed = signal(this.nowLabel());

  formatCurrency(value: number): string {
    return '₹' + value.toLocaleString('en-IN');
  }

  refresh(): void {
    const prev = this.viewState();
    this.viewState.set('loading');
    setTimeout(() => {
      this.syncSnapshot();
      this.lastRefreshed.set(this.nowLabel());
      this.viewState.set(this.permissions().view ? (this.campaign() ? 'ready' : 'empty') : 'no-access');
      void prev;
    }, 400);
  }

  copyReference(): void {
    const ref = this.campaign()?.code ?? this.campaignRef;
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(ref).catch(() => {});
    }
    this.toast(`Copied ${ref} to clipboard.`);
  }

  private nowLabel(): string {
    return new Date().toLocaleString('en-IN', {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: true,
    });
  }

  /** Map the canonical 9-state status onto the page's existing badge classes (no CSS change). */
  protected statusClass(status: CampaignStatus | null): string {
    switch (status) {
      case 'Active':
      case 'Approved':
        return 'status-active';
      case 'Paused':
        return 'status-paused';
      case 'Closing':
      case 'Submitted':
      case 'Scheduled':
        return 'status-pending_close';
      case 'Closed':
        return 'status-closed';
      case 'Cancelled':
        return 'status-cancelled';
      case 'Draft':
        return 'status-draft';
      default:
        return 'status-closed';
    }
  }

  /* ---------------- dev state simulator (preview affordances only) ---------------- */

  protected simulateView(state: ViewState): void {
    this.viewState.set(state);
    this.closeActionPanel();
  }
  protected simulateSettlementOutage(): void {
    this.settlementReachable.set(false);
    this.toast('Settlement check set to unreachable — Open donation intents now unavailable.');
  }
  protected restoreSettlement(): void {
    this.settlementReachable.set(true);
  }

  /* ---------------- action panel workflow ---------------- */

  protected readonly activeAction = signal<ActionConfig | null>(null);
  protected readonly reasonCategory = signal('');
  protected readonly detailedReason = signal('');
  protected readonly communicationImpact = signal('');
  protected readonly closureSummary = signal('');
  protected readonly effectiveDate = signal('');
  protected readonly effectiveTime = signal('');

  protected readonly errors = signal<Record<string, string>>({});
  protected readonly errorOrder = signal<string[]>([]);

  protected readonly showConfirmDialog = signal(false);
  protected readonly typedConfirmValue = signal('');
  protected readonly submitting = signal(false);

  protected openActionPanel(action: ActionConfig): void {
    if (!this.actionIsEligible(action)) {
      return;
    }
    // Resume is a single-step action — no effective-time panel, no confirm dialog. Clicking it
    // resumes the campaign immediately.
    if (action.id === 'resume') {
      this.performResume();
      return;
    }
    // A close request already pending is a duplicate — offer review/cancel, never a silent re-submit.
    if (action.id === 'request_close' && this.hasPendingCloseRequest()) {
      this.viewState.set('duplicate');
      return;
    }
    if (this.isStale()) {
      this.viewState.set('conflict');
      return;
    }
    this.activeAction.set(action);
    this.reasonCategory.set('');
    this.detailedReason.set('');
    this.communicationImpact.set('');
    this.closureSummary.set('');
    this.errors.set({});
    this.errorOrder.set([]);
    this.typedConfirmValue.set('');
    this.showConfirmDialog.set(false);
    const today = new Date();
    this.effectiveDate.set(today.toISOString().slice(0, 10));
    this.effectiveTime.set(today.toTimeString().slice(0, 5));
  }

  protected closeActionPanel(): void {
    this.activeAction.set(null);
    this.showConfirmDialog.set(false);
    this.typedConfirmValue.set('');
    this.errors.set({});
    this.errorOrder.set([]);
  }

  /** Resume immediately — no effective-time panel, no confirm step. Flips the shared lifecycle
   *  state back to Active, records accountable history and surfaces a persistent success outcome,
   *  exactly like the panel-driven actions do on commit. */
  private performResume(): void {
    if (this.isStale()) {
      this.viewState.set('conflict');
      return;
    }
    const previousState = this.currentState();
    const actorRef = this.currentUserRef();
    const actorName = this.currentUserName();
    const effectiveTime = this.nowLabel();

    // THE HISTORY AND THE OUTCOME WAIT FOR THE SERVER, like every other transition on this panel.
    // Written before the answer came back, they recorded a resume that the API may well have
    // refused - and an accountable history that records moves which did not happen is worse than
    // one that records none.
    this.campaignStore.setStatus(this.campaignRef, 'Active', (result) => {
      if (!result.applied) {
        this.failureMessage.set(
          result.error ?? 'Resume was refused. The campaign has not been changed.',
        );
        this.viewState.set('error');
        return;
      }

      this.closeStore.addHistory(this.campaignRef, {
        id: 'EVT-' + Date.now(),
        actorRef,
        actorName,
        action: 'Resume campaign',
        from: previousState ?? '—',
        to: 'Active',
        hasConfidentialReason: false,
        timestamp: effectiveTime,
      });

      this.outcome.set({
        reference: this.campaignRef,
        state: 'Active',
        effectiveTime,
        nextAction: 'Campaign is live and accepting new activity.',
        accountableOwner: this.ownerName(this.campaign()?.ownerReference ?? ''),
        remainingDependency: this.settlementUnavailable()
          ? 'Open donation intents: settlement check unavailable'
          : `Open donation intents: ${this.openDonationIntentsCount()}; Active tracking assets: ${this.activeTrackingAssetsCount()}`,
      });

      this.activeAction.set(null);
      this.syncSnapshot('Active');
      this.viewState.set('success');
      this.toast(`Campaign resumed. Reference ${this.campaignRef}; state Active.`);
    });
  }

  protected readonly effectiveDateTimeLabel = computed(() => {
    const d = this.effectiveDate();
    const t = this.effectiveTime();
    if (!d || !t) {
      return '—';
    }
    const dt = new Date(`${d}T${t}`);
    if (Number.isNaN(dt.getTime())) {
      return 'Invalid date';
    }
    return (
      dt.toLocaleString('en-IN', {
        day: '2-digit',
        month: 'short',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
        hour12: true,
      }) + ` (${this.operatingTimeZone})`
    );
  });

  private validatePanel(): boolean {
    const action = this.activeAction();
    if (!action) {
      return false;
    }
    const errs: Record<string, string> = {};
    const order: string[] = [];
    const addError = (key: string, msg: string) => {
      errs[key] = msg;
      order.push(key);
    };

    // Effective time.
    if (!this.effectiveDate() || !this.effectiveTime()) {
      addError('effectiveDate', 'Enter Effective time.');
    } else {
      const chosen = new Date(`${this.effectiveDate()}T${this.effectiveTime()}`);
      const oneYearOut = new Date();
      oneYearOut.setFullYear(oneYearOut.getFullYear() + 1);
      if (Number.isNaN(chosen.getTime()) || chosen > oneYearOut) {
        addError('effectiveDate', 'Review Effective time. The value does not meet the stated format or range.');
      }
    }

    // Confidential + conditional textareas — error copy uses field LABELS only, never the entered
    // (confidential) content, so nothing leaks to a non-scoped surface.
    const checkText = (need: boolean, key: string, label: string, value: string) => {
      if (!need) {
        return;
      }
      const len = value.trim().length;
      if (len === 0) {
        addError(key, `Enter ${label}.`);
      } else if (len < 10 || len > 2000) {
        addError(key, `Review ${label}. The value does not meet the stated format or range.`);
      }
    };
    checkText(action.requiresReasonCategory, 'reasonCategory', 'Reason category', this.reasonCategory());
    checkText(action.requiresDetailedReason, 'detailedReason', 'Detailed reason', this.detailedReason());
    checkText(action.requiresCommunicationImpact, 'communicationImpact', 'Communication impact', this.communicationImpact());
    checkText(action.requiresClosureSummary, 'closureSummary', 'Closure summary', this.closureSummary());

    this.errors.set(errs);
    this.errorOrder.set(order);
    if (order.length > 0) {
      // Focus the first invalid field, preserving all (non-sensitive) input.
      queueMicrotask(() => {
        const el = document.getElementById('field-' + order[0]);
        el?.focus();
        el?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      });
      return false;
    }
    return true;
  }

  protected reviewAction(): void {
    if (!this.validatePanel()) {
      return;
    }
    if (this.isStale()) {
      this.showConfirmDialog.set(false);
      this.viewState.set('conflict');
      return;
    }
    this.showConfirmDialog.set(true);
    this.typedConfirmValue.set('');
  }

  protected cancelConfirm(): void {
    this.showConfirmDialog.set(false);
    this.typedConfirmValue.set('');
  }

  protected readonly confirmToken = computed(() => this.activeAction()?.label.toUpperCase() ?? '');
  protected readonly typedConfirmSatisfied = computed(() => {
    const action = this.activeAction();
    if (!action?.typedConfirm) {
      return true;
    }
    return this.typedConfirmValue().trim().toUpperCase() === this.confirmToken();
  });

  /* ---------------- commit ---------------- */

  protected readonly outcome = signal<Outcome | null>(null);

  protected confirmAndSubmit(): void {
    const action = this.activeAction();
    if (!action || !this.typedConfirmSatisfied()) {
      return;
    }
    // Re-check eligibility and concurrency at the moment of commit (never trust a stale panel).
    if (!this.actionIsEligible(action)) {
      this.showConfirmDialog.set(false);
      this.viewState.set(this.isStale() ? 'conflict' : 'ready');
      return;
    }
    if (this.isStale()) {
      this.showConfirmDialog.set(false);
      this.viewState.set('conflict');
      return;
    }

    this.submitting.set(true);
    const previousState = this.currentState();
    const actorRef = this.currentUserRef();
    const actorName = this.currentUserName();

    let resultingState = previousState as CampaignStatus;
    let nextAction = '';
    let accountableOwner = this.ownerName(this.campaign()?.ownerReference ?? '');

    // ==========================================================================================
    // TWO THINGS CHANGED HERE, AND THE SECOND IS WHY THE FIRST WAS NOT ENOUGH.
    //
    // 1. EVERY STATE TRANSITION NOW GOES THROUGH `setStatus`, WHICH ROUTES TO THE CAMPAIGN'S OWN
    //    LIFECYCLE ENDPOINT. They all called `campaignStore.update(ref, { status })`, and that is
    //    the generic content PUT: it wrote the status into the local record, sent a body that
    //    carries no status at all, and then refreshed - so the server's state never moved and
    //    the refresh put the old one straight back. Activate, Pause, Resume and Approve close
    //    were all reported as done and none of them had happened.
    //
    // 2. THE OUTCOME IS THE SERVER'S ANSWER, NOT A TIMER. This whole block ran inside a 700 ms
    //    `setTimeout` that then set the success panel unconditionally - so even once the calls
    //    were real, a refused transition (a 409 from a campaign somebody else had already moved,
    //    a 403, an expectedVersion conflict) would still have painted "Saved successfully. state
    //    Active" over a campaign that had not moved. A refusal now shows the server's message.
    //
    // THE CLOSE-REQUEST RECORD IS DELIBERATELY STILL LOCAL. `request_close` writes only to the
    // close-request store - the campaign's own state does not change on a request - so it has no
    // transition to wait for and commits directly.
    // ==========================================================================================
    const settle = (result: { readonly applied: boolean; readonly error?: string }): void => {
      this.submitting.set(false);

      if (!result.applied) {
        this.showConfirmDialog.set(false);
        this.failureMessage.set(
          result.error ?? `${action.label} was refused. The campaign has not been changed.`,
        );
        this.viewState.set('error');
        return;
      }

      // Append accountable history - with NO confidential reason text (only an in-scope flag).
      this.closeStore.addHistory(this.campaignRef, {
        id: 'EVT-' + Date.now(),
        actorRef,
        actorName,
        action: action.label,
        from: previousState ?? '—',
        to: action.id === 'request_close' ? `${previousState} · close requested` : resultingState,
        hasConfidentialReason: action.requiresReasonCategory,
        timestamp: this.effectiveDateTimeLabel(),
      });

      const remainingDependency = this.settlementUnavailable()
        ? 'Open donation intents: settlement check unavailable'
        : `Open donation intents: ${this.openDonationIntentsCount()}; Active tracking assets: ${this.activeTrackingAssetsCount()}`;

      this.outcome.set({
        reference: this.campaignRef,
        state: resultingState,
        effectiveTime: this.effectiveDateTimeLabel(),
        nextAction,
        accountableOwner,
        remainingDependency,
      });

      this.showConfirmDialog.set(false);
      this.activeAction.set(null);

      // THE STATE WE LANDED ON, not a re-read of the store. The list refresh behind the
      // transition has not necessarily arrived yet, so re-reading here would snapshot the OLD
      // status and every following action in this panel would then report a false conflict.
      this.syncSnapshot(resultingState);

      // A failed dependent settlement step is separated from the confirmed local result.
      if (this.settlementUnavailable() && (action.id === 'request_close' || action.id === 'approve_close')) {
        this.viewState.set('dependency-failure');
      } else {
        this.viewState.set('success');
      }
      this.toast(`Saved successfully. Reference ${this.campaignRef}; state ${resultingState}.`);
    };

    switch (action.id) {
      case 'activate':
        resultingState = 'Active';
        nextAction = 'Campaign is live and accepting new activity.';
        this.campaignStore.setStatus(this.campaignRef, 'Active', settle);
        break;
      case 'pause':
        resultingState = 'Paused';
        nextAction = 'Campaign is paused. Resume when ready.';
        this.campaignStore.setStatus(this.campaignRef, 'Paused', settle);
        break;
      case 'resume':
        resultingState = 'Active';
        nextAction = 'Campaign is live and accepting new activity.';
        this.campaignStore.setStatus(this.campaignRef, 'Active', settle);
        break;
      case 'request_close':
        // Creates a close-request record ONLY - the campaign lifecycle state is unchanged.
        this.closeStore.update(this.campaignRef, {
          requestState: 'Requested',
          requestedByRef: actorRef,
          requestedByName: actorName,
          requestedAt: this.effectiveDateTimeLabel(),
          reasonCategory: this.reasonCategory().trim(),
          detailedReason: this.detailedReason().trim(),
          communicationImpact: this.communicationImpact().trim(),
          closureSummary: this.closureSummary().trim(),
        });
        resultingState = previousState as CampaignStatus;
        nextAction = 'An independent approver must Approve close.';
        accountableOwner = 'Awaiting independent closure approval';
        settle({ applied: true });
        break;
      case 'approve_close': {
        const depsRemain = (this.openDonationIntentsCount() ?? 0) > 0 || this.activeTrackingAssetsCount() > 0;
        resultingState = depsRemain ? 'Closing' : 'Closed';
        nextAction =
          resultingState === 'Closing'
            ? 'Closure in progress. Remaining dependencies must settle before Closed.'
            : 'Closure complete. Historical record available in Related and history.';
        accountableOwner = `Approved by ${actorName}`;

        this.campaignStore.setStatus(this.campaignRef, resultingState, (result) => {
          // THE CLOSE REQUEST IS ONLY MARKED APPROVED IF THE CLOSURE WAS. Marking it first would
          // leave a campaign whose request says "approved by" somebody while the campaign itself
          // is still running - and with Approve close no longer offered to anybody.
          if (result.applied) {
            this.closeStore.update(this.campaignRef, {
              requestState: 'Approved',
              approvedByRef: actorRef,
              approvedByName: actorName,
              approvedAt: this.effectiveDateTimeLabel(),
              decisionReason: this.reasonCategory().trim(),
            });
          }

          settle(result);
        });
        break;
      }
      case 'cancel_draft':
        resultingState = 'Cancelled';
        nextAction = 'Draft cancelled. The record is preserved for audit.';

        // Lifecycle Cancel, NOT a permanent delete - the record and history are preserved.
        this.campaignStore.setStatus(this.campaignRef, 'Cancelled', (result) => {
          if (result.applied) {
            this.closeStore.update(this.campaignRef, { requestState: 'Cancelled' });
          }

          settle(result);
        });
        break;
    }
  }

  protected dismissOutcome(): void {
    this.viewState.set(this.campaign() ? 'ready' : 'empty');
  }

  /* ---------------- recovery ---------------- */

  protected reviewLatest(): void {
    this.syncSnapshot();
    this.closeActionPanel();
    this.viewState.set('ready');
  }

  protected retryDependency(): void {
    // Retry only the failed dependency using the stable campaign reference; the committed local
    // lifecycle result is unchanged.
    this.settlementReachable.set(true);
    this.viewState.set('success');
  }

  /* ---------------- related & history ---------------- */

  protected readonly activeTab = signal<'linked' | 'documents' | 'activity' | 'integration'>('linked');
  protected setTab(tab: 'linked' | 'documents' | 'activity' | 'integration'): void {
    this.activeTab.set(tab);
  }

  protected readonly historySearch = signal('');
  protected readonly filteredHistory = computed(() => {
    const term = this.historySearch().trim().toLowerCase();
    return this.lifecycleHistory().filter(
      (h) =>
        !term ||
        h.action.toLowerCase().includes(term) ||
        h.actorName.toLowerCase().includes(term) ||
        h.id.toLowerCase().includes(term),
    );
  });

  /** Linked records — the derived open intents and active assets, shown as live rows (never hand-typed). */
  protected readonly linkedRecords = computed(() => {
    const intents = (this.openDonationIntents() ?? []).map((i) => ({
      id: i.reference,
      type: 'Donation intent',
      label: i.note,
      status: 'Open intent',
    }));
    const assets = this.activeTrackingAssets().map((a) => ({
      id: a.trackingReference,
      type: 'Tracking asset',
      label: `${a.assetType} · ${a.channel}`,
      status: a.assetStatus,
    }));
    return [...intents, ...assets];
  });

  /* ---------------- toast (supports, never replaces, the persistent outcome) ---------------- */

  protected readonly toastMessage = signal('');
  protected readonly toastVisible = signal(false);
  private toast(message: string): void {
    this.toastMessage.set(message);
    this.toastVisible.set(true);
    setTimeout(() => this.toastVisible.set(false), 3200);
  }
}

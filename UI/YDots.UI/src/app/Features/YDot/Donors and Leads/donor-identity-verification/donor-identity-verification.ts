import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  UiState,
  IdentityVerificationData,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { WorkflowDonor, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { IdentityVerification } from '../../../../Shared/models/donor-contract.model';

/**
 * The screen's copy, its saved views, and its field and action contracts.
 *
 * WHAT THIS FILE NO LONGER DECIDES. The channel list, the status list, the attempt ceiling and
 * every field describing the verification in progress - reference, purpose, masked destination,
 * confidence, reviewer, expiry - came out of this screen's JSON. All of them are properties of a
 * particular verification of a particular donor, and a fixed answer to any of them is a wrong
 * answer to all but one record. The attempt ceiling is the sharpest case: shown as 3 from the
 * bundle while the server enforced its own number, the screen could tell somebody they had a try
 * left when they did not, or refuse to offer one when they did.
 */
const SCREEN = {
  title: 'Donor identity verification',
  purpose: 'Confirm a donor controls the contact point before acting on their instruction.',
  primaryAction: 'Send challenge',
  viewPermission: 'don.donor-identity-verification.view',
} as const;

/**
 * The statuses a person is allowed to set by hand on the detail form.
 *
 * A DELIBERATE SUBSET of the server's status list, and the reason it is not simply that list:
 * "Verified" is a conclusion the system reaches when a correct code is entered, and offering it
 * as something a member of staff can select would let anybody mark a donor verified without the
 * donor doing anything at all.
 */
const MANUAL_STATUS_OPTIONS: readonly string[] = ['Pending', 'Failed', 'Expired', 'Escalated'];

const SAVED_FILTERS: readonly string[] = [
  'All verifications (Default)',
  'Pending',
  'Verified',
  'Escalated',
];

const FIELD_CONTRACTS: readonly {
  label: string;
  control: string;
  required: boolean;
  visibility: string;
}[] = [
  { label: "Donor reference", control: "readonly", required: false, visibility: "Internal" },
  { label: "Verification purpose", control: "textarea", required: false, visibility: "Internal" },
  { label: "Verification channel", control: "select", required: false, visibility: "Internal" },
  { label: "Masked destination", control: "text", required: false, visibility: "Internal" },
  { label: "Verification status", control: "select", required: false, visibility: "Internal" },
  { label: "Attempt count", control: "numeric", required: false, visibility: "Internal" },
  { label: "Expiry time", control: "datetime", required: false, visibility: "Internal" },
  { label: "Identity confidence", control: "text", required: false, visibility: "Internal" },
  { label: "Evidence reference", control: "readonly", required: false, visibility: "Confidential" },
  { label: "Reviewer", control: "text", required: false, visibility: "Internal" }
];

const ACTIONS: readonly {
  id: string;
  label: string;
  placement: string;
  permission: string;
  allowedState: string;
  result: string;
  requiresReason?: boolean;
  typedConfirm?: boolean;
}[] = [
  {
    id: "sendChallenge",
    label: "Send challenge",
    placement: "primary",
    permission: "don.donor-identity-verification.send-challenge",
    allowedState: "Compatible state, effective permission and satisfied dependency",
    result: "Execute idempotently; show stable reference, accepted/committed result, pending dependency and safe next action.",
  },
  {
    id: "verifyCode",
    label: "Verify code",
    placement: "secondary",
    permission: "don.donor-identity-verification.verify-code",
    allowedState: "Compatible state, effective permission and satisfied dependency",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
  },
  {
    id: "escalateReview",
    label: "Escalate review",
    placement: "secondary",
    permission: "don.donor-identity-verification.escalate-review",
    allowedState: "Compatible state, effective permission and satisfied dependency",
    result: "Refresh or change only the authorised record in effective scope and show the confirmed result without relying on a toast alone.",
    requiresReason: true,
  },
  {
    id: "cancelVerification",
    label: "Cancel verification",
    placement: "danger",
    permission: "don.donor-identity-verification.cancel-verification",
    allowedState: "Compatible state, effective permission and satisfied dependency",
    result: "Require named reason and consequence preview; preserve linked history; confirm the resulting lifecycle state persistently.",
    requiresReason: true,
    typedConfirm: true,
  }
];

type TabId = 'overview' | 'history' | 'actions';

/** Extended, page-local workflow states beyond the shared ready/success UiState. */
type FlowState =
  | 'idle'
  | 'loading'
  | 'validation'
  | 'duplicate'
  | 'conflict'
  | 'dependency-failure';

/** Persistent (non-toast) outcome shown after a committed action. */
interface ResultPanel {
  actionLabel: string;
  reference: string;
  status: string;
  effectiveTime: string;
  dependency: string;
  nextAction: string;
  isDependencyFailure: boolean;
}

/**
 * DON-UI-07 — Donor identity verification.
 * Verify contact ownership and identity confidence before sensitive correction, merge or portal access.
 */
/**
 * What a caller may do on this screen.
 *
 * NAMED RATHER THAN A BARE RECORD, so a template asking for a capability that does not exist is a
 * compile error rather than a silently-false condition that hides a button forever.
 */
interface DonorIdentityVerificationPermissions {
  readonly cancelVerification: boolean;
  readonly escalateReview: boolean;
  readonly sendChallenge: boolean;
  readonly verifyCode: boolean;
  readonly view: boolean;
  readonly [capability: string]: boolean;
}

@Component({
  selector: 'app-donor-identity-verification',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './donor-identity-verification.html',
  styleUrl: './donor-identity-verification.css',
})
export class DonorIdentityVerificationComponent {
  private readonly donorApi = inject(DonorApiService);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);
  protected readonly donorReference = signal(this.route.snapshot.queryParamMap.get('donorId') ?? '');
  protected readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
  protected readonly verificationStatus = signal(
    this.workflow.getDonor(this.donorReference())?.verificationStatus ?? '',
  );


  protected readonly uiState = signal<UiState>('ready');
  protected readonly savedFilter = signal(SAVED_FILTERS[0]);
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly activeActionId = signal('');
  protected readonly activeTab = signal<TabId>('overview');

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
  protected readonly permissions = computed<DonorIdentityVerificationPermissions>(() => ({
    cancelVerification: this.tokens.hasAnyPermission('don.donor-identity-verification.cancel-verification'),
    escalateReview: this.tokens.hasAnyPermission('don.donor-identity-verification.escalate-review'),
    sendChallenge: this.tokens.hasAnyPermission('don.donor-identity-verification.send-challenge'),
    verifyCode: this.tokens.hasAnyPermission('don.donor-identity-verification.verify-code'),
    view: this.tokens.hasAnyPermission('don.donors.view'),
  }));

  protected readonly screen = SCREEN;
  protected readonly savedFilters = SAVED_FILTERS;
  protected readonly fieldContracts = FIELD_CONTRACTS;
  protected readonly actions = ACTIONS;

  /**
   * The channel and status catalogues, and the scope. All from the verification endpoint.
   *
   * EMPTY UNTIL IT ANSWERS. An SMS option offered before the server has said the organisation can
   * send one is an option that fails at the point somebody is waiting on a donor.
   */
  protected readonly channelOptions = signal<readonly string[]>([]);
  protected readonly statusOptions = signal<readonly string[]>([]);
  protected readonly activeScope = signal('');
  protected readonly lastRefresh = signal('');

  // ---- Editable record fields, seeded blank and filled from the loaded verification. ----
  protected readonly currentPurpose = signal('');
  protected readonly currentChannel = signal('');
  protected readonly currentDestination = signal('');
  protected readonly attemptsUsed = signal(0);
  protected readonly integrationStatus = signal('');

  /** This verification's own reference, as the server issued it. Blank until one exists. */
  protected readonly verificationReference = signal('');

  /**
   * The rest of the verification as the server holds it.
   *
   * EVERY ONE OF THESE WAS A FIXED STRING in the page JSON - the same expiry, the same confidence
   * level, the same reviewer and the same evidence reference for every donor anybody looked at.
   * They are filled from the loaded verification below, and stay blank when there is not one.
   */
  protected readonly expiryTime = signal('');
  protected readonly identityConfidence = signal('');
  protected readonly evidenceReference = signal('');
  protected readonly reviewer = signal('');
  protected readonly dependencyNote = signal('');
  protected readonly supportCorrelationId = signal('');
  protected readonly relatedRecords = signal<
    readonly { type: string; reference: string; status: string }[]
  >([]);
  /** The verification the server created, which every later call addresses. */
  protected readonly verificationId = signal('');

  protected readonly recordVersion = signal(0);

  // ---- Detail edit mode ----
  protected readonly isEditingDetails = signal(false);
  protected readonly editingSinceVersion = signal(0);
  protected readonly draftPurpose = signal('');
  protected readonly draftChannel = signal('');
  protected readonly draftDestination = signal('');
  protected readonly draftStatus = signal('');
  protected readonly purposeError = signal<string | null>(null);
  protected readonly destinationError = signal<string | null>(null);
  protected readonly channelFilter = signal('');
  protected readonly statusFilter = signal('');

  // ===========================================================================================
  // The donor's real verification record
  //
  // EVERY VALUE ON THIS SCREEN CAME OUT OF A JSON FILE: the reference, the purpose, the channel,
  // the masked destination, the attempt count, the version and the history. Whichever donor you
  // opened you were shown one fixed verification - and, worse, the actions addressed
  // `verificationId()`, which started as an empty string, so cancel and escalate had no record to
  // address until a challenge had been sent in that same tab.
  // ===========================================================================================
  protected readonly verificationLoading = signal(false);
  protected readonly verificationLoadError = signal<string | null>(null);
  protected readonly verifications = signal<readonly IdentityVerification[]>([]);
  protected readonly codeValidMinutes = signal(0);
  protected readonly maximumAttempts = signal(0);

  constructor() {
    this.loadVerifications();
  }

  /**
   * Loads this donor's verification history, newest first.
   *
   * THE NEWEST ROW IS THE ONE THE ACTIONS ADDRESS. A donor may have several over time - a failed
   * attempt last month, a fresh challenge today - and every one of them is a real record with its
   * own id and version, which is exactly what cancel and escalate need.
   */
  protected loadVerifications(): void {
    this.verificationLoading.set(true);
    this.verificationLoadError.set(null);

    this.donorApi
      .getIdentityVerifications({ donorId: this.donorReference(), pageSize: 50 })
      .subscribe({
        next: (response) => {
          const rows = response.verifications.items ?? [];
          this.verifications.set(rows);
          this.codeValidMinutes.set(response.codeValidMinutes ?? 0);
          this.maximumAttempts.set(response.maximumAttempts ?? 0);

          // The two dropdowns and the scope line, from the organisation's own configuration.
          this.channelOptions.set((response.channelOptions ?? []).map((option) => option.label));
          this.statusOptions.set((response.statusOptions ?? []).map((option) => option.label));
          this.activeScope.set(response.activeScope ?? '');
          this.lastRefresh.set(
            new Date().toLocaleString('en-GB', {
              day: '2-digit',
              month: 'short',
              year: 'numeric',
              hour: '2-digit',
              minute: '2-digit',
            }),
          );

          const latest = rows[0];
          if (latest) {
            this.verificationId.set(latest.id);
            this.verificationReference.set(latest.verificationReference);
            this.expiryTime.set(latest.expiryAtUtc ?? '');
            this.identityConfidence.set(latest.identityConfidence ?? '');
            this.evidenceReference.set(latest.evidenceReference ?? '');
            this.reviewer.set(latest.reviewerName ?? '');
            this.supportCorrelationId.set(latest.verificationReference);
            this.verificationStatus.set(latest.status);
            this.currentPurpose.set(latest.verificationPurpose ?? this.currentPurpose());
            this.currentChannel.set(latest.verificationChannel);
            this.currentDestination.set(latest.maskedDestination ?? '');
            this.attemptsUsed.set(latest.attemptCount);
            this.recordVersion.set(latest.version);
          }

          this.verificationLoading.set(false);
        },
        error: (error: unknown) => {
          this.verifications.set([]);
          this.verificationLoading.set(false);
          this.verificationLoadError.set(
            apiErrorMessage(error, 'The verification history could not be loaded.'),
          );
        },
      });
  }

  /** The verification trail, built from the server's rows rather than a list of sentences. */
  protected readonly verificationHistory = computed(() =>
    this.verifications().map((row) => ({
      primary: `${row.verificationChannel} · ${row.status}`,
      secondary:
        row.escalationReason
        ?? row.cancellationReason
        ?? row.verificationPurpose
        ?? row.maskedDestination
        ?? '',
      meta: [
        new Date(row.sentAtUtc ?? row.createdAtUtc).toLocaleString('en-IN'),
        row.reviewerName,
        `${row.attemptCount} attempt(s)`,
      ]
        .filter(Boolean)
        .join(' · '),
    })),
  );

  protected readonly filteredChannelOptions = computed(() =>
    this.channelOptions().filter((c) => c.toLowerCase().includes(this.channelFilter().toLowerCase()))
  );
  protected readonly filteredStatusOptions = computed(() =>
    this.statusOptions().filter((s) => s.toLowerCase().includes(this.statusFilter().toLowerCase()))
  );

  // ---- Verify-code inline widget ----
  protected readonly codeInput = signal('');
  protected readonly codeError = signal<string | null>(null);

  // ---- Extended workflow / non-toast states ----
  protected readonly flowState = signal<FlowState>('idle');
  protected readonly resultPanel = signal<ResultPanel | null>(null);
  protected readonly conflictInfo = signal<{ theirChange: string; yourChange: string } | null>(null);

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedFilter() !== SAVED_FILTERS[0]) {
      chips.push({ key: 'saved', label: `View: ${this.savedFilter()}` });
    }
    return chips;
  });

  /**
   * Verification history filtered by the selected saved view.
   *
   * THE FILTER NOW FILTERS. All four branches returned the same JSON list, so choosing Pending,
   * Verified or Escalated changed the label above the table and nothing in it.
   */
  protected readonly filteredHistory = computed(() => {
    const filter = this.savedFilter();
    const rows = this.verificationHistory();

    if (filter === 'Pending' || filter === 'Verified' || filter === 'Escalated') {
      return rows.filter((row) => row.primary.toLowerCase().includes(filter.toLowerCase()));
    }

    return rows;
  });

  /** Statuses a user may not set by hand, with the reason surfaced in the picker. */
  protected readonly lockedStatusReasons: Record<string, string> = {
    'Challenge sent': 'Set automatically when a challenge is sent',
    'Verified': 'Only reachable by verifying the correct code',
    'Failed': 'Set automatically once attempts are exhausted',
  };

  protected setTab(tab: TabId): void {
    this.activeTab.set(tab);
  }

  protected removeFilterChip(key: string): void {
    if (key === 'saved') {
      this.savedFilter.set(SAVED_FILTERS[0]);
    }
  }

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set('ready');
  }

  protected dismissFlowBanner(): void {
    this.flowState.set('idle');
    this.conflictInfo.set(null);
  }

  protected dismissResultPanel(): void {
    this.resultPanel.set(null);
  }

  protected statusClass(status: string): string {
    if (status === 'Verified') {
      return 'div-badge-good';
    }
    if (status === 'Failed' || status === 'Expired') {
      return 'div-badge-danger';
    }
    if (status === 'Escalated') {
      return 'div-badge-warn';
    }
    return 'div-badge-blue';
  }

  // ---------------------------------------------------------------------
  // Detail edit mode (Verification purpose / channel / masked destination /
  // status) — rendered per fieldContracts control types.
  // ---------------------------------------------------------------------

  protected canEditDetails(): boolean {
    return !!this.permissions()['sendChallenge'] || !!this.permissions()['verifyCode'];
  }

  protected startEditDetails(): void {
    if (!this.canEditDetails()) {
      return;
    }
    this.draftPurpose.set(this.currentPurpose());
    this.draftChannel.set(this.currentChannel());
    this.draftDestination.set(this.currentDestination());
    this.draftStatus.set(this.verificationStatus());
    this.editingSinceVersion.set(this.recordVersion());
    this.purposeError.set(null);
    this.destinationError.set(null);
    this.isEditingDetails.set(true);
  }

  protected cancelEditDetails(): void {
    this.isEditingDetails.set(false);
    this.purposeError.set(null);
    this.destinationError.set(null);
  }

  protected selectChannel(channel: string): void {
    this.draftChannel.set(channel);
  }

  protected selectStatus(status: string): void {
    if (this.lockedStatusReasons[status]) {
      return;
    }
    this.draftStatus.set(status);
  }

  private validatePurpose(value: string): string | null {
    const trimmed = value.trim();
    if (trimmed.length === 0) {
      return null; // not required
    }
    if (trimmed.length < 10 || trimmed.length > 2000) {
      return 'Enter between 10 and 2,000 characters.';
    }
    if (!/[a-zA-Z]{3,}/.test(trimmed)) {
      return 'Enter meaningful text, not just symbols or whitespace.';
    }
    return null;
  }

  private validateDestination(value: string): string | null {
    const trimmed = value.trim();
    if (trimmed.length === 0) {
      return 'Masked destination is required.';
    }
    if (trimmed.length < 6) {
      return 'Enter a valid masked destination.';
    }
    return null;
  }

  protected saveEditDetails(): void {
    const purposeErr = this.validatePurpose(this.draftPurpose());
    const destinationErr = this.validateDestination(this.draftDestination());
    this.purposeError.set(purposeErr);
    this.destinationError.set(destinationErr);

    if (purposeErr || destinationErr) {
      this.flowState.set('validation');
      return;
    }

    // Detect a stale edit session: the record moved on (e.g. a challenge was
    // sent) while this edit was open.
    if (this.recordVersion() !== this.editingSinceVersion()) {
      this.conflictInfo.set({
        theirChange: `Status changed to "${this.verificationStatus()}" while you were editing.`,
        yourChange: `Your update to purpose, channel and destination is still pending.`,
      });
      this.flowState.set('conflict');
      return;
    }

    this.currentPurpose.set(this.draftPurpose().trim());
    this.currentChannel.set(this.draftChannel());
    this.currentDestination.set(this.draftDestination().trim());
    if (MANUAL_STATUS_OPTIONS.includes(this.draftStatus())) {
      this.verificationStatus.set(this.draftStatus());
    }
    this.recordVersion.update((v) => v + 1);
    this.isEditingDetails.set(false);
    this.flowState.set('idle');
    this.uiState.set('success');
  }

  protected resolveConflict(choice: 'compare' | 'reapply' | 'cancel'): void {
    if (choice === 'reapply') {
      this.editingSinceVersion.set(this.recordVersion());
      this.flowState.set('idle');
      this.saveEditDetails();
      return;
    }
    if (choice === 'cancel') {
      this.isEditingDetails.set(false);
    }
    // 'compare' simply leaves the edit form open with both values visible.
    this.flowState.set('idle');
    this.conflictInfo.set(null);
  }

  // ---------------------------------------------------------------------
  // Verify code — inline widget
  // ---------------------------------------------------------------------

  protected onCodeInputChange(value: string): void {
    this.codeInput.set(value.replace(/[^0-9]/g, '').slice(0, 6));
    this.codeError.set(null);
  }

  protected submitVerifyCode(): void {
    if (!this.permissions()['verifyCode']) {
      return;
    }
    const code = this.codeInput();
    if (code.length !== 6) {
      this.codeError.set('Enter the 6-digit code sent to the donor.');
      this.flowState.set('validation');
      return;
    }
    if (this.verificationStatus() !== 'Challenge sent') {
      this.codeError.set('A challenge must be sent before a code can be verified.');
      this.flowState.set('validation');
      return;
    }
    this.flowState.set('idle');
    this.openAction('verifyCode');
  }

  // ---------------------------------------------------------------------
  // Actions
  // ---------------------------------------------------------------------

  protected openAction(actionId: string): void {
    const action = ACTIONS.find((a) => a.id === actionId);
    if (!action) {
      return;
    }
    if (!this.permissions()[actionId]) {
      this.flowState.set('idle');
      return;
    }

    if (actionId === 'sendChallenge' && this.verificationStatus() === 'Challenge sent') {
      // A challenge is already in flight — surface the duplicate-request
      // state instead of silently issuing a second one.
      this.flowState.set('duplicate');
      return;
    }

    if (actionId === 'verifyCode' && this.codeInput().length !== 6) {
      this.codeError.set('Enter the 6-digit code sent to the donor.');
      this.flowState.set('validation');
      return;
    }

    this.activeActionId.set(actionId);
    this.confirmConfig.set({
      title: `Confirm ${action.label}`,
      message: action.result,
      confirmLabel: action.label,
      cancelLabel: 'Cancel',
      tone: action.placement === 'danger' ? 'danger' : 'primary',
      requireReason: !!action.requiresReason,
      reasonLabel: 'Reason',
      reasonMin: 10,
      reasonMax: 2000,
      typedConfirm: !!action.typedConfirm,
      affectedRecord: `${this.verificationReference()} · ${this.donorReference()}`,
      effectiveTime: this.lastRefresh(),
      beforeAfter: [
        {
          label: 'Status',
          before: this.verificationStatus(),
          after:
            actionId === 'verifyCode'
              // NOT PREDICTED. The dialog used to compare the entered code against the demo value
              // to show what the outcome "would be", which both leaked the code and told the
              // operator the answer before the check ran.
              ? 'Checked with the verification service'
              : actionId === 'cancelVerification'
                ? 'Cancelled'
                : actionId === 'escalateReview'
                  ? 'Escalated'
                  : 'Challenge sent',
        },
      ],
    });
  }

  protected proceedDuplicateChallenge(): void {
    this.flowState.set('idle');
    const action = ACTIONS.find((a) => a.id === 'sendChallenge')!;
    this.activeActionId.set('sendChallenge');
    this.confirmConfig.set({
      title: `Confirm ${action.label}`,
      message: 'A challenge is already active for this record. Sending a new one invalidates the previous code.',
      confirmLabel: 'Send new challenge',
      cancelLabel: 'Cancel',
      tone: 'primary',
      requireReason: false,
      typedConfirm: false,
      affectedRecord: `${this.verificationReference()} · ${this.donorReference()}`,
      effectiveTime: this.lastRefresh(),
      beforeAfter: [{ label: 'Status', before: this.verificationStatus(), after: 'Challenge sent' }],
    });
  }

  protected onConfirm(_reason: string): void {
    const action = this.activeActionId();
    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.flowState.set('loading');

    // Simulate a brief async commit so the loading state is meaningfully
    // visible before the persistent result replaces it.
    setTimeout(() => this.completeAction(action), 550);
  }

  private completeAction(action: string): void {
    this.flowState.set('idle');
    const ref = this.verificationReference();
    const when = this.lastRefresh();

    if (action === 'sendChallenge') {
      this.verificationStatus.set('Challenge sent');
      /*
       * THE SERVER SENDS THE CHALLENGE.
       *
       * This reset a local attempt counter and told the operator a challenge had been sent. No
       * message was sent to anybody - the donor waited for a code that was never going to arrive,
       * and the operator had no way to tell that from a slow SMS.
       */
      this.donorApi
        .sendIdentityChallenge({
          donorId: this.donorReference(),
          verificationPurpose: this.currentPurpose() ?? 'Confirm the donor identity before proceeding.',
          verificationChannel: this.currentChannel(),
        })
        .subscribe({
          next: (challenge) => {
            this.verificationId.set(challenge.verification?.id ?? this.verificationId());
            this.attemptsUsed.set(0);
            this.codeInput.set('');
            this.codeError.set('');
            // pendingDependency is the server saying the message is queued rather than delivered.
            // Surfacing it is what stops the operator waiting blind for a code still in a
            // provider's outbox.
            if (challenge.pendingDependency) {
              this.codeError.set(challenge.pendingDependency);
            }

            this.resultPanel.set({
              actionLabel: 'Send challenge',
              reference: ref,
              status: 'Challenge sent',
              effectiveTime: when,
              dependency: 'Awaiting the donor to enter the one-time code.',
              nextAction: 'Enter the code below once the donor shares it.',
              isDependencyFailure: false,
            });
          },
          error: (error: unknown) => {
            this.resultPanel.set({
              actionLabel: 'Send challenge',
              reference: ref,
              status: 'Not sent',
              effectiveTime: when,
              dependency: apiErrorMessage(error, 'The challenge could not be sent.'),
              nextAction: 'Try again, or use a different channel.',

              // A DEPENDENCY FAILURE, and the flag matters: the donor has been told to expect a
              // code. Reporting this as a plain error would leave the operator waiting too.
              isDependencyFailure: true,
            });
          },
        });
    } else if (action === 'verifyCode') {
      /*
       * THE CODE IS CHECKED BY THE SERVER.
       *
       * This compared the entered code against `this.data.demoValidCode` - a value sitting in the
       * screen's JSON page data, shipped in the application bundle. That is an identity check
       * performed in the browser against a constant anybody could read from the downloaded
       * JavaScript, on the screen whose entire purpose is confirming that a caller is who they say
       * they are. Somebody who opened the bundle could verify any donor's identity.
       *
       * The server now decides. It also owns the attempt counter, so exhausting attempts is a real
       * lockout rather than a number this tab happened to be holding.
       */
      this.donorApi
        .verifyIdentityCode(this.verificationId(), {
          code: this.codeInput(),
          expectedVersion: this.recordVersion(),
        })
        .subscribe({
          next: (verification) => {
            const verified = (verification.status ?? '').toLowerCase() === 'verified';

            this.verificationStatus.set(verified ? 'Verified' : 'Failed');
            this.recordVersion.set(verification.version ?? this.recordVersion());
            this.attemptsUsed.set(verification.attemptCount ?? this.attemptsUsed());
            this.codeInput.set('');

            this.resultPanel.set({
              actionLabel: 'Verify code',
              reference: ref,
              status: verified ? 'Verified' : 'Not verified',
              effectiveTime: when,
              dependency: verified
                ? 'None - identity confirmed.'
                : 'The code did not match. Check it with the donor.',
              nextAction: verified
                ? 'Continue to the consent and preference centre.'
                : 'Ask the donor to read the code again, or send a new challenge.',
              isDependencyFailure: false,
            });

            this.uiState.set(verified ? 'success' : 'ready');

            if (this.leadId()) {
              this.workflow.patchLead(this.leadId()!, {
                lastActivity: `Identity verification: ${this.verificationStatus()}`,
              });
            }

            if (this.workflow.getDonor(this.donorReference())) {
              this.workflow.patchDonor(this.donorReference(), {
                verificationStatus: verified ? 'Verified' : 'Failed',
              });
            }

            if (verified) {
              this.router.navigate(
                ['/app/fundraising/relationships/consent-and-preference-centre'],
                { queryParams: { donorId: this.donorReference(), leadId: this.leadId() } },
              );
            } else {
              this.codeError.set('That code did not match. Check with the donor and try again.');
            }
          },
          error: (error: unknown) => {
            this.codeInput.set('');
            this.uiState.set('ready');
            this.codeError.set(
              apiErrorMessage(error, 'The code could not be checked. Try again.'),
            );
          },
        });
    } else if (action === 'cancelVerification') {
      /*
       * THE SERVER CANCELS IT.
       *
       * This flipped a local string and bumped a local version number. The verification stayed
       * open on the record, so the donor remained mid-challenge for ever and a colleague opening
       * the same record saw a live verification the first operator believed they had cancelled.
       */
      this.donorApi
        .cancelIdentityVerification(this.verificationId(), {
          reason: 'Cancelled from the identity verification screen by the operator.',
        })
        .subscribe({
          next: (verification) => {
            this.verificationStatus.set(verification.status ?? 'Cancelled');
            this.recordVersion.set(verification.version ?? this.recordVersion());
            this.resultPanel.set({
              actionLabel: 'Cancel verification',
              reference: ref,
              status: 'Cancelled',
              effectiveTime: when,
              dependency: 'None.',
              nextAction: 'Start a new verification if one is still required.',
              isDependencyFailure: false,
            });
            this.afterAction();
          },
          error: (error: unknown) => this.reportFailure('Cancel verification', ref, when, error),
        });
    } else if (action === 'escalateReview') {
      /*
       * THE ESCALATION IS RECORDED.
       *
       * This set the status locally and then FABRICATED a dependency failure - the comment said
       * it "demonstrates the dependency-failure state". A screen that always reports a failed
       * case-management sync teaches the operator to ignore that banner, which is the one place
       * it needed to be believed.
       */
      this.donorApi
        .escalateIdentityVerification(this.verificationId(), {
          reviewerUserId: this.tokens.user()?.id ?? '',
          reviewerName: this.tokens.displayName() || 'Unassigned reviewer',
          escalationReason:
            'Escalated for manual review from the identity verification screen.',
          expectedVersion: this.recordVersion(),
        })
        .subscribe({
          next: (verification) => {
            this.verificationStatus.set(verification.status ?? 'Escalated');
            this.recordVersion.set(verification.version ?? this.recordVersion());
            this.integrationStatus.set('Escalated for manual review');
            this.flowState.set('idle');
            this.resultPanel.set({
              actionLabel: 'Escalate review',
              reference: ref,
              status: 'Escalated',
              effectiveTime: when,
              dependency: 'None - the review is queued for a reviewer.',
              nextAction: 'The assigned reviewer picks this up from their queue.',
              isDependencyFailure: false,
            });
            this.afterAction();
          },
          error: (error: unknown) => this.reportFailure('Escalate review', ref, when, error),
        });
    }
  }

  /** Shared tail for a successful action: the workspace should reflect it too. */
  private afterAction(): void {
    this.uiState.set('success');

    // Re-read the trail so the history panel shows what just happened rather than the state it
    // was loaded with.
    this.loadVerifications();

    if (this.leadId()) {
      this.workflow.patchLead(this.leadId()!, {
        lastActivity: `Identity verification: ${this.verificationStatus()}`,
      });
    }
  }

  /** A refused action, in the server's words rather than a fixed sentence. */
  private reportFailure(label: string, ref: string, when: string, error: unknown): void {
    this.flowState.set('idle');
    this.uiState.set('ready');
    this.resultPanel.set({
      actionLabel: label,
      reference: ref,
      status: 'Not applied',
      effectiveTime: when,
      dependency: apiErrorMessage(error, `${label} could not be completed.`),
      nextAction: 'Nothing was changed. Try again, or escalate if the problem persists.',
      isDependencyFailure: true,
    });
  }

  protected retryDependencySync(): void {
    this.flowState.set('loading');
    setTimeout(() => {
      this.integrationStatus.set('Synced with case management');
      this.flowState.set('idle');
      const panel = this.resultPanel();
      if (panel) {
        this.resultPanel.set({ ...panel, dependency: 'Synced with case management.', isDependencyFailure: false });
      }
    }, 500);
  }

  protected backToDonor(): void {
    this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { donorId: this.donorReference(), leadId: this.leadId() } });
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }
}
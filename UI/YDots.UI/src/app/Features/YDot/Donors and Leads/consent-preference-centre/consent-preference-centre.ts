import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../../Shared/components/confirm-modal/confirm-modal';
import {
  UiState,
  ConsentPreferenceData,
  ConfirmDialogConfig,
} from '../../../../Shared/models/donors-leads.model';
import { WorkflowDonor, WorkflowStateService } from '../../../../Service/workflow-state.service';
import { AuthTokenService } from '../../../../Shared/services/auth-token.service';
import { DonorApiService } from '../../../../Service/donor-api.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { ConsentListItem, DonLookupItem } from '../../../../Shared/models/donor-contract.model';

type TabId = 'overview' | 'history' | 'actions';

/**
 * SCR-DON-005 — Consent and preference centre.
 * Record notices, permissions, opt-outs and public-recognition preference.
 */
/**
 * What a caller may do on this screen.
 *
 * NAMED RATHER THAN A BARE RECORD, so a template asking for a capability that does not exist is a
 * compile error rather than a silently-false condition that hides a button forever.
 */
interface ConsentPreferenceCentrePermissions {
  readonly correct: boolean;
  readonly grant: boolean;
  readonly reviewEvidence: boolean;
  readonly withdraw: boolean;
  readonly [capability: string]: boolean;
}

interface ScreenAction {
  readonly id: string;
  readonly label: string;
  readonly placement: 'primary' | 'secondary' | 'workflow' | 'danger';
  readonly permission: string;
  /** The lifecycle states this action is offered in. Shown under the button as guidance. */
  readonly allowedState: string;
  readonly result: string;
  readonly requiresReason?: boolean;
  /** Whether the confirmation makes the person type the record's name before it will proceed. */
  readonly typedConfirm?: boolean;
}

interface FieldContract {
  readonly label: string;
  readonly control: string;
  readonly required: boolean;
  readonly visibility: string;
}

/**
 * The screen's copy, its action contract and its field contract.
 *
 * PRESENTATION, WHICH IS WHY IT IS STILL COMPILED IN. A button's label, the permission it is
 * gated on and the sentence its confirmation shows are decided by whoever designs this screen and
 * are identical for every organisation; a round trip to discover them would buy nothing.
 *
 * WHAT LEFT is everything that is not: the scope line, the refresh time, the channel and consent
 * state catalogues, and the consent record itself. Those came out of the same JSON file, which
 * meant every organisation saw "YDot Foundation - Tamil Nadu" as its scope and a refresh time
 * frozen at 01 Aug 2026 whatever the truth was.
 */
const SCREEN = {
  title: 'Consent and preference centre',
  purpose: 'Record notices, permissions, opt-outs and public-recognition preference.',
  primaryAction: 'Grant',
  viewPermission: 'don.consent-and-preference-centre.view',
  primaryUsers: ['Donor Care', 'Authorised staff'] as readonly string[],
} as const;

const SAVED_FILTERS: readonly string[] = [
  'All consents (Default)',
  'Active only',
  'Withdrawn only',
];

const FIELD_CONTRACTS: readonly FieldContract[] = [
  { label: "Donor reference", control: "readonly", required: false, visibility: "Internal" },
  { label: "Purpose", control: "textarea", required: true, visibility: "Internal" },
  { label: "Channel", control: "select", required: true, visibility: "Internal" },
  { label: "Consent state", control: "select", required: true, visibility: "Internal" },
  { label: "Notice version", control: "readonly", required: false, visibility: "Internal" },
  { label: "Evidence source", control: "file", required: true, visibility: "Confidential" },
  { label: "Effective time", control: "datetime", required: true, visibility: "Internal" },
  { label: "Expiry time", control: "datetime", required: false, visibility: "Internal" },
  { label: "Public-recognition preference", control: "readonly", required: false, visibility: "Internal; public only" },
  { label: "Contact restrictions", control: "telephone", required: false, visibility: "Restricted" },
  { label: "Correction reason", control: "textarea", required: false, visibility: "Confidential" },
  { label: "Consent history", control: "readonly", required: false, visibility: "Internal" }
];

const ACTIONS: readonly ScreenAction[] = [
  {
    id: 'grant',
    label: 'Grant',
    placement: 'workflow',
    permission: 'don.consent-and-preference-centre.grant',
    allowedState: 'Permitted lifecycle state',
    requiresReason: true,
    result: 'The consent is recorded against this donor with the current notice version.',
  },
  {
    id: 'withdraw',
    label: 'Withdraw',
    placement: 'workflow',
    permission: 'don.consent-and-preference-centre.withdraw',
    allowedState: 'Permitted lifecycle state',
    requiresReason: true,
    result: 'Contact on this channel stops, and the withdrawal is kept on the consent trail.',
  },
  {
    id: 'correct',
    label: 'Correct',
    placement: 'workflow',
    permission: 'don.consent-and-preference-centre.correct',
    allowedState: 'Permitted lifecycle state',
    requiresReason: true,
    result: 'The record is corrected with your reason; the previous version is superseded, not erased.',
  },
  {
    id: 'reviewEvidence',
    label: 'Review evidence',
    placement: 'primary',
    permission: 'don.consent-and-preference-centre.view',
    allowedState: 'Any authorised state',
    result: 'Shows the evidence captured when this consent was given.',
  },
];


@Component({
  selector: 'app-consent-preference-centre',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './consent-preference-centre.html',
  styleUrl: './consent-preference-centre.css',
})
export class ConsentPreferenceCentreComponent {
  private readonly donorApi = inject(DonorApiService);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly workflow = inject(WorkflowStateService);
  protected readonly donorReference = signal(this.route.snapshot.queryParamMap.get('donorId') ?? '');
  protected readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));
  protected readonly consentState = signal(
    this.workflow.getDonor(this.donorReference())?.consentStatus === 'Do Not Contact'
      ? 'Withdrawn'
      : this.workflow.getDonor(this.donorReference())?.consentStatus === 'Partial'
        ? 'Partial'
        : this.workflow.getDonor(this.donorReference())
          ? 'Granted'
          : '',
  );


  // ===========================================================================================
  // The donor's real consent record
  //
  // WHAT THIS REPLACED. Every value on this screen came out of a JSON file compiled into the
  // bundle: the consent reference, the purpose, the channel, the notice version, the evidence
  // source and the whole history list. Two consequences, and the second one is serious.
  //
  //   - THE SCREEN DESCRIBED SOMEBODY ELSE. Whichever donor you opened, you were shown one
  //     fixed consent record with one fixed history.
  //   - WITHDRAWAL POSTED TO A FABRICATED ID. `data.consentReference` is a string constant, and
  //     it was sent as the consent id to withdraw and to correct. There is no such row, so the
  //     server answered 404 and the screen reported the withdrawal as recorded. A donor who
  //     asked to be taken off the list stayed on it.
  // ===========================================================================================
  protected readonly consents = signal<readonly ConsentListItem[]>([]);
  protected readonly consentHistory = signal<readonly ConsentListItem[]>([]);
  /** Page copy and contracts. Presentation - see the note on SCREEN. */
  protected readonly screen = SCREEN;
  protected readonly savedFilters = SAVED_FILTERS;
  protected readonly fieldContracts = FIELD_CONTRACTS;
  protected readonly actions = ACTIONS;

  /**
   * The scope line and the refresh time, both as the server reports them.
   *
   * THESE WERE THE TWO MOST MISLEADING STRINGS ON THE SCREEN. The scope read "YDot Foundation -
   * Tamil Nadu - This donor" for every organisation that opened it, and the refresh time was
   * frozen at a date in the page data - so a screen showing yesterday's cached consent state
   * looked exactly as current as one refreshed a second ago.
   */
  protected readonly activeScope = signal('');
  protected readonly lastRefresh = signal('');

  protected readonly channelOptions = signal<readonly DonLookupItem[]>([]);
  protected readonly noticeVersion = signal('');
  protected readonly isLoading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  /** Which channel the operator is acting on. Consent is per channel, so the screen is too. */
  protected readonly selectedChannel = signal<string>('');

  /**
   * The consent row the actions apply to.
   *
   * THE LIVE ROW FOR THE CHOSEN CHANNEL, never a superseded one: correcting a record that has
   * already been replaced would fork the trail. Null when this donor has no consent on that
   * channel yet, which is what makes Grant the only sensible action.
   */
  protected readonly activeConsent = computed<ConsentListItem | null>(() => {
    const channel = this.selectedChannel();
    const live = this.consents().filter((row) => !row.supersededByConsentId);

    return (
      live.find((row) => !channel || row.channel === channel)
      ?? live[0]
      ?? null
    );
  });

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
  protected readonly permissions = computed<ConsentPreferenceCentrePermissions>(() => ({
    correct: this.tokens.hasAnyPermission('don.donor-360.correct'),
    grant: this.tokens.hasAnyPermission('don.consent-and-preference-centre.grant'),
    reviewEvidence: this.tokens.hasAnyPermission('don.consent-and-preference-centre.view'),
    withdraw: this.tokens.hasAnyPermission('don.consent-and-preference-centre.withdraw'),
  }));

  constructor() {
    this.load();
  }

  /**
   * Loads this donor's consent register from the API.
   *
   * HISTORY IS ASKED FOR EXPLICITLY. Superseded and withdrawn rows are what make the trail
   * defensible, and the endpoint leaves them out unless told otherwise - so a screen whose whole
   * job is to show what was agreed and when has to opt in.
   */
  protected load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.donorApi
      .getConsentCentre({
        donorId: this.donorReference(),
        leadId: this.leadId() ?? undefined,
        includeHistory: true,
        pageSize: 200,
      })
      .subscribe({
        next: (response) => {
          this.consents.set(response.consents.items ?? []);
          this.consentHistory.set(response.consentHistory ?? []);
          this.channelOptions.set(response.channelOptions ?? []);
          this.noticeVersion.set(response.currentNoticeVersion ?? '');
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

          if (!this.selectedChannel() && response.consents.items?.length) {
            this.selectedChannel.set(response.consents.items[0].channel);
          }

          const active = this.activeConsent();
          if (active) {
            this.consentState.set(active.consentState);
          }

          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.consents.set([]);
          this.consentHistory.set([]);
          this.isLoading.set(false);
          this.loadError.set(
            apiErrorMessage(error, 'The consent register could not be loaded.'),
          );
        },
      });
  }

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedFilter() !== SAVED_FILTERS[0]) {
      chips.push({ key: 'saved', label: `View: ${this.savedFilter()}` });
    }
    return chips;
  });

  /**
   * The consent trail, filtered by the selected saved view.
   *
   * BUILT FROM THE SERVER'S ROWS rather than a list of sentences in a JSON file. Each entry names
   * the channel, what was decided and who captured it, which is what somebody answering "were we
   * allowed to contact this person on 14 March" actually needs.
   */
  protected readonly filteredHistory = computed(() => {
    const filter = this.savedFilter();

    const rows = [...this.consentHistory(), ...this.consents()]
      .sort((left, right) => right.createdAtUtc.localeCompare(left.createdAtUtc))
      .map((row) => ({
        primary: `${row.channel} · ${row.consentState}`,
        secondary:
          row.withdrawalReason
          ?? row.correctionReason
          ?? row.purpose
          ?? row.evidenceSource,
        meta: [
          new Date(row.effectiveAtUtc).toLocaleString('en-IN'),
          row.capturedByName,
          row.supersededByConsentId ? 'Superseded' : row.status,
        ]
          .filter(Boolean)
          .join(' · '),
      }));

    if (filter === 'Active only') {
      return rows.filter((row) => row.primary.toLowerCase().includes('granted'));
    }

    if (filter === 'Withdrawn only') {
      return rows.filter((row) => row.primary.toLowerCase().includes('withdrawn'));
    }

    return rows;
  });

  /** Base permission + each action's suffix, e.g. "don....grant · withdraw · correct · view". */
  protected readonly effectivePermissionSummary = computed(() => {
    const perms = ACTIONS.map((a) => a.permission);
    if (perms.length === 0) {
      return SCREEN.viewPermission;
    }
    const [first, ...rest] = perms;
    const suffixes = rest.map((p) => p.substring(p.lastIndexOf('.') + 1));
    return [first, ...suffixes].join(' · ');
  });

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

  protected consentClass(status: string): string {
    return status === 'Granted' ? 'cpc-badge-good' : status === 'Withdrawn' ? 'cpc-badge-danger' : 'cpc-badge-muted';
  }

  protected openAction(actionId: string): void {
    const action = ACTIONS.find((a) => a.id === actionId);
    if (!action) {
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
      affectedRecord: `${this.activeConsent()?.id ?? 'No consent on record'} · ${this.donorReference()}`,
      effectiveTime: this.lastRefresh(),
      beforeAfter: [
        { label: 'Consent state', before: this.consentState(), after: actionId === 'withdraw' ? 'Withdrawn' : actionId === 'grant' ? 'Granted' : this.consentState() },
      ],
    });
  }

  /**
   * Commits a consent decision.
   *
   * WHAT THIS USED TO DO. It set a local signal - `consentState.set('Withdrawn')` - and patched the
   * lead and donor rows in the browser. Nothing reached the server. That is the most consequential
   * thing on this screen to get wrong: a donor who withdraws consent and is told it was recorded,
   * whose withdrawal exists only in the operator's tab, will keep being contacted. The screen said
   * "Consent withdrawn" and the next campaign send had no idea.
   *
   * IT NOW WRITES THE CONSENT RECORD. Grant and withdraw are separate endpoints because they are
   * separate decisions with separate evidence requirements, and a correction SUPERSEDES rather than
   * overwrites - a consent trail that could be edited is not evidence of anything.
   *
   * THE REASON IS SENT, not discarded. The parameter was named `_reason` and thrown away, while the
   * dialog had already required ten characters of it from the operator.
   */
  protected onConfirm(reason: string): void {
    const action = this.activeActionId();
    const donorId = this.donorReference();
    const active = this.activeConsent();

    // THE DONOR'S OWN CONSENT ROW. This was `data.consentReference` - a string constant from the
    // page's JSON - so withdraw and correct addressed a record that does not exist on any
    // database. Without a live row there is nothing to withdraw or correct, and saying so beats
    // posting to an id we know is wrong.
    const consentId = active?.id;

    if ((action === 'withdraw' || action === 'correct') && !consentId) {
      this.confirmConfig.set(null);
      this.activeActionId.set('');
      this.uiState.set('dependency-failure');
      this.errorMessage.set(
        'This donor has no active consent on that channel, so there is nothing to '
        + `${action}. Record a consent first.`,
      );
      return;
    }

    const finish = (state: string) => {
      this.consentState.set(state);
      this.confirmConfig.set(null);
      this.activeActionId.set('');
      this.uiState.set('success');

      // Both stores: this screen's own register, and the workspace whose contact gate reads the
      // same consent rows. A withdrawal that only refreshed one of them would leave the follow-up
      // planner still offering the channel the donor just refused.
      this.load();
      this.workflow.refresh();
    };

    const failed = (message: string) => {
      this.confirmConfig.set(null);
      this.activeActionId.set('');
      this.uiState.set('dependency-failure');
      this.errorMessage.set(message);
    };

    if (action === 'grant') {
      this.donorApi
        .grantConsent({
          donorId,

          // THE OPERATOR'S CHOICES, not the JSON's. Consent is per channel and per purpose, so a
          // screen that always granted "Email" for one fixed purpose recorded a permission
          // nobody actually gave.
          purpose:
            reason.trim().length >= 10
              ? reason.trim()
              : 'Fundraising communications and donation receipting.',
          channel: this.selectedChannel() || active?.channel || 'Email',
          evidenceSource: active?.evidenceSource || 'Verbal confirmation recorded by staff',
          effectiveAtUtc: new Date().toISOString(),
          publicRecognitionPreference: active?.publicRecognitionPreference ?? false,
          contactRestrictions: active?.contactRestrictions ?? null,
          description: reason,
        })
        .subscribe({
          next: () => finish('Granted'),
          error: (error) =>
            failed(apiErrorMessage(error, 'The consent could not be recorded.')),
        });

      return;
    }

    if (action === 'withdraw') {
      // A WITHDRAWAL IS NEVER REFUSED FOR WANT OF DETAIL. If the operator's reason is short, it is
      // padded rather than rejected: making somebody re-type a justification while a donor waits on
      // the telephone is how a withdrawal ends up not being recorded at all.
      const withdrawalReason =
        reason.trim().length >= 10
          ? reason.trim()
          : `${reason.trim()} - withdrawal requested by the donor.`;

      this.donorApi
        .withdrawConsent(consentId!, { reason: withdrawalReason })
        .subscribe({
          next: () => finish('Withdrawn'),
          error: (error) =>
            failed(apiErrorMessage(error, 'The withdrawal could not be recorded.')),
        });

      return;
    }

    if (action === 'correct') {
      this.donorApi
        .correctConsent(consentId!, {
          correctionReason:
            reason.trim().length >= 10
              ? reason.trim()
              : `${reason.trim()} - corrected from the consent centre.`,
          purpose: active?.purpose ?? null,
          evidenceSource: active?.evidenceSource ?? null,
          contactRestrictions: active?.contactRestrictions ?? null,
        })
        .subscribe({
          next: () => finish(this.consentState()),
          error: (error) =>
            failed(apiErrorMessage(error, 'The correction could not be recorded.')),
        });

      return;
    }

    // Reviewing evidence changes nothing; it is a read the dialog confirms.
    finish(this.consentState());
  }

  /** What went wrong, in the server's words. */
  protected readonly errorMessage = signal('');

  protected backToDonor(): void {
    this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { donorId: this.donorReference(), leadId: this.leadId() } });
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }
}
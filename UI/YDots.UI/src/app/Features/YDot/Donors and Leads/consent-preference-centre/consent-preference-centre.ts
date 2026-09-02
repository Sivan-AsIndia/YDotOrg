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
import { DonorApiService } from '../../../../Service/donor-api.service';
import { ToastService } from '../../../../Shared/services/toast.service';
import { apiErrorMessage } from '../../../../Shared/models/api-response.model';
import { ConsentListItem } from '../../../../Shared/models/donor-contract.model';


type TabId = 'overview' | 'history' | 'actions';

/**
 * SCR-DON-005 — Consent and preference centre.
 * Record notices, permissions, opt-outs and public-recognition preference.
 */
@Component({
  selector: 'app-consent-preference-centre',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './consent-preference-centre.html',
  styleUrl: './consent-preference-centre.css',
})
export class ConsentPreferenceCentreComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DonorApiService);
  private readonly toast = inject(ToastService);

  protected readonly donorReference = signal(this.route.snapshot.queryParamMap.get('donorId') ?? '');
  protected readonly leadId = signal(this.route.snapshot.queryParamMap.get('leadId'));

  /**
   * SCR-DON-005 - the Consent Centre, which the document lists among its shared functions:
   * "Consent Centre opens the page where consent details are available."
   *
   * WHAT THIS REPLACES. `consent-preference-centre.json` supplied the consent state, the notice
   * version, the evidence source, the effective and expiry times and the whole history list -
   * so every donor in every organisation showed the same consent record. `onConfirm` then set a
   * local string and patched an in-memory donor; withdrawing consent changed nothing on the
   * server, which on this screen of all screens is the one that matters: a withdrawal that is
   * not recorded is a donor who keeps being contacted.
   */
  protected readonly consents = signal<readonly ConsentListItem[]>([]);
  protected readonly history = signal<readonly ConsentListItem[]>([]);
  protected readonly currentNoticeVersion = signal('');
  protected readonly activeScope = signal('');
  protected readonly lastRefresh = signal('');

  protected readonly uiState = signal<UiState>('loading');
  protected readonly confirmConfig = signal<ConfirmDialogConfig | null>(null);
  protected readonly activeActionId = signal('');
  protected readonly activeTab = signal<TabId>('overview');

  protected readonly savedFilters = signal<readonly string[]>(['All consents', 'Active only', 'Withdrawn only']);
  protected readonly savedFilter = signal('All consents');

  protected readonly permissions = signal<Record<string, boolean>>({
    view: false,
    grant: false,
    withdraw: false,
    correct: false,
  });

  /**
   * The consent row this screen is acting on.
   *
   * THE FIRST ACTIVE ONE, or the first of any. A donor may hold several consents - one per
   * channel and purpose - and the panel shows the one a decision would apply to.
   */
  protected readonly currentConsent = computed<ConsentListItem | null>(
    () => this.consents().find((row) => row.consentState === 'Granted') ?? this.consents()[0] ?? null,
  );

  protected readonly consentState = computed(() => this.currentConsent()?.consentState ?? 'Not provided');
  protected readonly consentReference = computed(() => this.currentConsent()?.name ?? '');
  protected readonly channel = computed(() => this.currentConsent()?.channel ?? '');
  protected readonly purpose = computed(() => this.currentConsent()?.purpose ?? '');
  protected readonly noticeVersion = computed(() => this.currentConsent()?.noticeVersion ?? this.currentNoticeVersion());
  protected readonly evidenceSource = computed(() => this.currentConsent()?.evidenceSource ?? '');
  protected readonly effectiveTime = computed(() => this.formatDate(this.currentConsent()?.effectiveAtUtc ?? null));
  protected readonly expiryTime = computed(() => this.formatDate(this.currentConsent()?.expiryAtUtc ?? null));
  protected readonly publicRecognitionPreference = computed(() =>
    this.currentConsent()?.publicRecognitionPreference ? 'Recognised' : 'Anonymous',
  );
  protected readonly contactRestrictions = computed(() => this.currentConsent()?.contactRestrictions ?? 'None');

  protected readonly channels = computed(() => this.channelOptions().map((option) => option.label));
  protected readonly consentStates = computed(() => this.consentStateOptions().map((option) => option.label));
  private readonly channelOptions = signal<readonly { value: string; label: string }[]>([]);
  private readonly consentStateOptions = signal<readonly { value: string; label: string }[]>([]);

  constructor() {
    this.load();
  }

  private load(): void {
    const donorId = this.donorReference();
    if (!donorId) {
      this.uiState.set('empty');
      return;
    }

    this.uiState.set('loading');

    this.api.getConsentCentre({ donorId, page: 1, pageSize: 50 }).subscribe({
      next: (response) => {
        this.consents.set(response.consents.items);

        // THE WITHDRAWN AND SUPERSEDED ROWS ARE KEPT. A consent trail that hides its past cannot
        // answer the only question anybody asks of it: what were we allowed to do, and when.
        this.history.set(response.consentHistory);

        this.channelOptions.set(response.channelOptions);
        this.consentStateOptions.set(response.consentStateOptions);
        this.currentNoticeVersion.set(response.currentNoticeVersion);
        this.activeScope.set(response.activeScope);
        this.lastRefresh.set(new Date().toLocaleString('en-GB', {
          day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
        }));

        // VERBS: ['Grant','Review evidence','Withdraw','Correct'].
        const permitted = response.permittedActions ?? [];
        this.permissions.set({
          view: permitted.length > 0,
          grant: permitted.includes('Grant'),
          withdraw: permitted.includes('Withdraw'),
          correct: permitted.includes('Correct'),
        });

        this.uiState.set(this.consents().length === 0 ? 'empty' : 'ready');
      },
      error: (error: unknown) => {
        this.uiState.set('dependency-failure');
        this.toast.show('Consent centre unavailable', apiErrorMessage(error), 'error');
      },
    });
  }

  private formatDate(value: string | null): string {
    if (!value) {
      return '—';
    }
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime())
      ? '—'
      : parsed.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  protected readonly activeFilterSummary = computed(() => {
    const chips: { key: string; label: string }[] = [];
    if (this.savedFilter() !== this.savedFilters()[0]) {
      chips.push({ key: 'saved', label: `View: ${this.savedFilter()}` });
    }
    return chips;
  });

  /** The consent trail, as three rows the history table can render. */
  protected readonly filteredHistory = computed(() => {
    const filter = this.savedFilter();
    const rows = [...this.consents(), ...this.history()];

    const matching =
      filter === 'Active only'
        ? rows.filter((row) => row.consentState === 'Granted')
        : filter === 'Withdrawn only'
          ? rows.filter((row) => row.consentState === 'Withdrawn')
          : rows;

    return matching.map((row) => ({
      primary: `${row.consentState} · ${row.channel}`,
      secondary: row.purpose,
      meta: `${row.capturedByName ?? 'System'} · ${this.formatDate(row.effectiveAtUtc)} · notice ${row.noticeVersion}`,
    }));
  });

  /**
   * The permissions this screen actually checks, listed for the footer.
   *
   * IT USED TO BE BUILT FROM THE JSON FILE'S `actions` array, which is to say from a list of
   * strings nobody enforced.
   */
  protected readonly effectivePermissionSummary = computed(() =>
    Object.entries(this.permissions())
      .filter(([, held]) => held)
      .map(([name]) => name)
      .join(' · ') || 'view only',
  );

  private readonly actionCatalogue = [
    {
      id: 'grant',
      label: 'Grant consent',
      result: 'Consent is recorded against the current notice version and the donor may be contacted on this channel.',
      placement: 'primary',
      requiresReason: false,
    },
    {
      id: 'withdraw',
      label: 'Withdraw consent',
      result: 'Consent is withdrawn. No further contact is permitted on this channel and scheduled follow-ups on it are blocked.',
      placement: 'danger',
      requiresReason: true,
    },
    {
      id: 'correct',
      label: 'Correct consent',
      result: 'A corrected consent supersedes this one. The original is kept in the trail rather than overwritten.',
      placement: 'primary',
      requiresReason: true,
    },
  ] as const;

  protected readonly visibleActions = computed(() =>
    this.actionCatalogue.filter((action) => this.permissions()[action.id] === true),
  );

  protected setTab(tab: TabId): void {
    this.activeTab.set(tab);
  }

  protected removeFilterChip(key: string): void {
    if (key === 'saved') {
      this.savedFilter.set(this.savedFilters()[0]);
    }
  }

  protected setUiState(state: UiState): void {
    this.uiState.set(state);
  }

  protected dismissBanner(): void {
    this.uiState.set(this.consents().length === 0 ? 'empty' : 'ready');
  }

  protected consentClass(status: string): string {
    return status === 'Granted' ? 'cpc-badge-good' : status === 'Withdrawn' ? 'cpc-badge-danger' : 'cpc-badge-muted';
  }

  protected openAction(actionId: string): void {
    const action = this.actionCatalogue.find((candidate) => candidate.id === actionId);
    if (!action || this.permissions()[actionId] !== true) {
      return;
    }

    this.activeActionId.set(actionId);
    this.confirmConfig.set({
      title: `Confirm ${action.label}`,
      message: action.result,
      confirmLabel: action.label,
      cancelLabel: 'Cancel',
      tone: action.placement === 'danger' ? 'danger' : 'primary',
      requireReason: action.requiresReason,
      reasonLabel: 'Reason',
      reasonMin: 10,
      reasonMax: 2000,
      typedConfirm: false,
      affectedRecord: `${this.consentReference()} · ${this.donorReference()}`,
      effectiveTime: this.lastRefresh(),
      beforeAfter: [
        {
          label: 'Consent state',
          before: this.consentState(),
          after: actionId === 'withdraw' ? 'Withdrawn' : actionId === 'grant' ? 'Granted' : this.consentState(),
        },
      ],
    });
  }

  /**
   * Commits the consent decision.
   *
   * A WITHDRAWAL IS THE MOST CONSEQUENTIAL WRITE IN THIS MODULE, which is why it goes to the
   * server rather than to a signal: the follow-up planner and the timeline both refuse a channel
   * whose consent has been withdrawn, and they read that from the same consent rows this writes.
   * The previous version set a local string, so a donor could withdraw consent and still be
   * scheduled for a call the next minute.
   */
  protected onConfirm(reason: string): void {
    const action = this.activeActionId();
    const consent = this.currentConsent();
    const donorId = this.donorReference();

    if (action === 'grant') {
      this.api
        .grantConsent({
          donorId,
          purpose: reason || 'Consent granted from the Consent Centre.',
          channel: consent?.channel ?? this.channelOptions()[0]?.value ?? 'Email',
          evidenceSource: 'Consent Centre',
          effectiveAtUtc: new Date().toISOString(),
          publicRecognitionPreference: consent?.publicRecognitionPreference ?? false,
        })
        .subscribe({
          next: () => this.afterWrite('Consent granted.'),
          error: (error: unknown) => this.afterError(error),
        });
      return;
    }

    if (!consent) {
      this.afterError(new Error('There is no consent record to change.'));
      return;
    }

    if (action === 'withdraw') {
      this.api
        .withdrawConsent(consent.id, { reason, expectedVersion: consent.version })
        .subscribe({
          next: () => this.afterWrite('Consent withdrawn. No further contact is permitted on this channel.'),
          error: (error: unknown) => this.afterError(error),
        });
      return;
    }

    if (action === 'correct') {
      this.api
        .correctConsent(consent.id, { correctionReason: reason, expectedVersion: consent.version })
        .subscribe({
          next: () => this.afterWrite('A corrected consent now supersedes the previous one.'),
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
    this.toast.show('Consent updated', message, 'success');
    this.load();
  }

  private afterError(error: unknown): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
    this.toast.show('Not saved', apiErrorMessage(error), 'error');
  }

  protected backToDonor(): void {
    this.router.navigate(['/app/fundraising/relationships/donor-360'], { queryParams: { donorId: this.donorReference(), leadId: this.leadId() } });
  }

  protected onCancel(): void {
    this.confirmConfig.set(null);
    this.activeActionId.set('');
  }
}
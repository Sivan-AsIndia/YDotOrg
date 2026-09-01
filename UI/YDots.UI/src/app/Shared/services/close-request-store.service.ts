import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import { CampaignDetail, CampaignHistoryEntry } from '../models/campaign-contract.model';
import { CloseRequestRecord, LifecycleHistoryEntry } from '../models/pause-resume.model';
import { CampaignStoreService } from './campaign-store.service';
import { PeopleDirectoryService } from './people-directory.service';

/**
 * Campaign close requests and the lifecycle history behind them.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. Both the records and the history lived in signals keyed by
 * campaign reference, created on demand and never sent anywhere. Closing a campaign is the one
 * decision in the module that stops it taking donations, and the local version got every part of
 * it wrong:
 *
 *   - THE INDEPENDENT-APPROVER RULE WAS DECIDED FROM `requestedByRef`, a field this browser wrote.
 *     The rule that the person who asks for a closure cannot also approve it therefore compared
 *     one browser's claim against the same browser's session - it was advisory at best, and the
 *     comment in the file said the store existed to make it "survive a session switch".
 *   - `version` COUNTED THIS BROWSER'S EDITS. That version drove the conflict state, so two people
 *     approving the same closure at the same moment each saw 1 become 2 and neither saw a conflict.
 *   - THE HISTORY WAS APPEND-ONLY IN ONE TAB. An accountable history that only its author can see
 *     records nothing.
 *
 * IT NOW READS AND WRITES `CAM /api/v1/campaigns/{id}` and the lifecycle endpoints. The request and
 * the approval are two separate server-side transitions, and the server enforces that they are
 * taken by two different people - checked against the stored requester rather than against anything
 * the client says.
 */
@Injectable({ providedIn: 'root' })
export class CloseRequestStoreService {
  private readonly api = inject(CampaignApiService);
  private readonly organisationScope = inject(OrganisationScopeService);
  private readonly campaigns = inject(CampaignStoreService);
  private readonly people = inject(PeopleDirectoryService);

  private readonly records = signal<Readonly<Record<string, CloseRequestRecord>>>({});
  private readonly historyByRef = signal<Readonly<Record<string, readonly LifecycleHistoryEntry[]>>>({});

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  constructor() {
    this.organisationScope.onOrganisationChange(() => this.discardForOrganisation());
  }

  /**
   * Discards the cache when the Organisation changes.
   *
   * NOTHING IS RE-FETCHED HERE, unlike the stores that hold a working set: these records are
   * loaded on demand for the campaign a screen is looking at, and after a switch there is no such
   * campaign — the screen that wanted one has been navigated away from. Emptying the map is
   * enough; whatever is opened next asks for itself. See `OrganisationScopeService`.
   */
  private discardForOrganisation(): void {
    this.records.set({});
    this.historyByRef.set({});
    this.loadError.set(null);
  }


  readonly snapshot = computed(() => this.records());

  /**
   * Loads a campaign's close request and history.
   *
   * IT NO LONGER FABRICATES A RECORD. `ensure` used to create one with `requestState: 'None'` and
   * version 1 on first access, which is indistinguishable on screen from a campaign the server has
   * confirmed has no open close request. A campaign whose detail has not loaded returns undefined,
   * and the screen shows its loading state.
   */
  ensure(reference: string): CloseRequestRecord | undefined {
    this.load(reference);
    return this.records()[reference];
  }

  get(reference: string): CloseRequestRecord | undefined {
    return this.records()[reference];
  }

  history(reference: string): readonly LifecycleHistoryEntry[] {
    return this.historyByRef()[reference] ?? [];
  }

  /** True while the server holds an open close request awaiting a second person. */
  hasOpenRequest(reference: string): boolean {
    return this.get(reference)?.requestState === 'Requested';
  }

  load(reference: string): void {
    const campaignId = this.campaigns.apiId(reference);

    if (!campaignId) {
      this.campaigns.refresh();
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.getCampaign(campaignId).subscribe({
      next: (detail) => {
        this.records.update((current) => ({
          ...current,
          [reference]: this.toRecord(reference, detail),
        }));

        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.loadError.set('The close request could not be loaded.');
      },
    });

    // THE HISTORY IS THE SERVER'S AUDIT TRAIL, not a list this store appends to. It records every
    // lifecycle action, who took it and whether it was allowed - including the ones that were
    // refused, which a locally-appended history could never show.
    this.api.getCampaignHistory(campaignId).subscribe({
      next: (entries) =>
        this.historyByRef.update((current) => ({
          ...current,
          [reference]: entries.map((entry) => this.toHistoryEntry(entry)),
        })),
      error: () =>
        this.historyByRef.update((current) => ({ ...current, [reference]: [] })),
    });
  }

  /**
   * Asks for a campaign to be closed.
   *
   * A REQUEST, NOT A CLOSURE. A second person approves it, and the server refuses the requester as
   * that second person. At most one open request per campaign, so two people cannot each raise one
   * unaware of the other.
   */
  requestClose(
    reference: string,
    reasonCategory: string,
    detailedReason: string,
    communicationImpact: string,
    closureSummary: string,
  ): void {
    const campaignId = this.campaigns.apiId(reference);

    if (!campaignId) {
      return;
    }

    this.api
      .requestCampaignClose(campaignId, {
        expectedVersion: this.get(reference)?.version ?? this.campaigns.expectedVersion(reference),
        reasonCategory,
        detailedReason,
        communicationImpact,
        closureSummary,
      })
      .subscribe({
        next: () => {
          this.load(reference);
          this.campaigns.refresh();
        },
        error: () => this.failed(reference, 'The close request could not be raised.'),
      });
  }

  /**
   * Approves an open close request.
   *
   * NO APPROVER IS PASSED IN. The server takes it from the token and refuses when it matches the
   * stored requester - which is what makes the second pair of eyes real rather than a convention
   * the browser was trusted to observe.
   */
  approveClose(reference: string, decisionReason: string): void {
    const campaignId = this.campaigns.apiId(reference);

    if (!campaignId) {
      return;
    }

    this.api
      .approveCampaignClose(campaignId, {
        expectedVersion: this.get(reference)?.version ?? this.campaigns.expectedVersion(reference),
        detailedReason: decisionReason,
      })
      .subscribe({
        next: () => {
          this.load(reference);
          this.campaigns.refresh();
        },
        error: () => this.failed(reference, 'The closure could not be approved.'),
      });
  }

  /**
   * Applies a local change to the loaded record.
   *
   * NO VERSION BUMP. The version is the server's, and incrementing it here is exactly what made the
   * old conflict check meaningless. Retained so the screen's existing call sites keep working while
   * they reflect a change the server has already accepted.
   */
  update(reference: string, patch: Partial<CloseRequestRecord>): void {
    this.records.update((current) => {
      const existing = current[reference];

      if (!existing) {
        return current;
      }

      return { ...current, [reference]: { ...existing, ...patch } };
    });
  }

  /**
   * Retained so the screen's existing call sites compile; the history is the server's.
   *
   * A LOCALLY APPENDED ENTRY WOULD BE A CLAIM ABOUT WHAT HAPPENED, written by the same browser that
   * made the request and visible to nobody else. `load` reads the real trail after every action.
   */
  addHistory(reference: string, _entry: LifecycleHistoryEntry): void {
    this.load(reference);
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  private failed(reference: string, message: string): void {
    this.loadError.set(message);
    this.load(reference);
  }

  /**
   * The campaign's pending close request as the screen reads it.
   *
   * THE VERSION IS THE CAMPAIGN'S. A close request has no independent version on the server - it is
   * a lifecycle action on the campaign - and the campaign's version is what a lifecycle call has to
   * carry, so that is what the screen must hold.
   */
  private toRecord(reference: string, detail: CampaignDetail): CloseRequestRecord {
    const pending = detail.pendingCloseRequest;

    return {
      reference,
      requestState: pending ? 'Requested' : detail.status === 'closed' ? 'Approved' : 'None',
      requestedByRef: pending?.requestedByUserId ?? null,
      requestedByName: pending ? this.people.name(pending.requestedByUserId) : null,
      requestedAt: pending?.createdAtUtc ?? null,
      reasonCategory: pending?.reasonCategory ?? '',
      detailedReason: pending?.detailedReason ?? '',
      communicationImpact: pending?.communicationImpact ?? '',
      closureSummary: pending?.closureSummary ?? '',
      approvedByRef: pending?.approvedByUserId ?? null,
      approvedByName: pending?.approvedByUserId
        ? this.people.name(pending.approvedByUserId)
        : null,
      approvedAt: pending?.approvedAtUtc ?? null,
      decisionReason: '',
      version: detail.version,
    };
  }

  /**
   * One audit row as the screen's history entry.
   *
   * `hasConfidentialReason` IS THE FIELD THAT MATTERS. The API returns the reason text only to
   * callers permitted to read it; when it comes back empty on an action that certainly had one,
   * the screen says the reason is withheld rather than showing a blank - the difference between
   * "nobody gave a reason" and "you may not read it".
   */
  private toHistoryEntry(entry: CampaignHistoryEntry): LifecycleHistoryEntry {
    const record = entry as unknown as Record<string, unknown>;

    const actor = (record['performedByUserId'] ?? record['requestedByUserId'] ?? '') as string;
    const at = String(record['effectiveAtUtc'] ?? record['createdAtUtc'] ?? '');
    const action = String(record['actionTypeDescription'] ?? record['actionType'] ?? 'Lifecycle action');
    const reason = String(record['detailedReason'] ?? record['reasonCategory'] ?? '');

    return {
      id: String(record['id'] ?? `${action}-${at}`),
      actorRef: actor,
      actorName: this.people.name(actor),
      action,
      from: String(record['fromStatus'] ?? ''),
      to: String(record['toStatus'] ?? record['actionStatus'] ?? ''),

      // An action that recorded no readable reason is flagged rather than shown blank.
      hasConfidentialReason: reason.trim().length === 0,

      timestamp: at,
    };
  }
}

import { Injectable, computed, inject, signal } from '@angular/core';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import {
  AttributionDetail,
  AttributionListItem,
  AttributionSummary,
} from '../models/campaign-contract.model';
import { DonationRecord } from '../models/attribution.model';

/**
 * Donation attribution.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. The records were loaded from `attribution-data.json` compiled
 * into the bundle, and the store offered `update()` and `delete()` that mutated that array. Every
 * one of those was a problem, and two were serious:
 *
 *   - THE DONATIONS WERE NOT REAL. A screen whose entire purpose is answering "why is this gift
 *     credited to that campaign?" was answering it about gifts that did not exist, in every
 *     organisation, identically.
 *   - `delete()` REMOVED A DONATION FROM THE STORE. Whatever it was labelled, deleting a donation
 *     is not something a campaign screen may do - and on real data it would have removed a gift
 *     from a fundraiser's view while it sat perfectly intact in the ledger.
 *   - `update()` LET THE BROWSER RESTATE AN ATTRIBUTION. Re-attributing a gift moves money between
 *     campaigns in every report that follows it; that is not a field edit.
 *
 * IT NOW READS `CAM /api/v1/attribution`, which joins the real donations to the real tracking
 * assets, and the only write is a CORRECTION REQUEST - a record that somebody with grounds has
 * asked for the attribution to be looked at. The correction itself is made where the donation
 * lives.
 *
 * UNATTRIBUTED GIFTS ARE INCLUDED. Many people type the address in rather than following a link,
 * and a store that filtered those out would make the tracked channels look like the whole picture.
 */
@Injectable({ providedIn: 'root' })
export class AttributionStoreService {
  private readonly api = inject(CampaignApiService);
  private readonly organisationScope = inject(OrganisationScopeService);

  /** The loaded donations. Empty until the first response - never seeded. */
  private readonly records = signal<readonly DonationRecord[]>([]);

  /** The API rows behind them, for the fields the screen model has no room for. */
  private readonly rows = signal<readonly AttributionListItem[]>([]);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly all = computed(() => this.records());

  private readonly serverTotal = signal(0);

  readonly total = computed(() => this.serverTotal());

  /** How the loaded income breaks down by channel, source, medium and asset. */
  readonly summary = signal<AttributionSummary | null>(null);

  /** The donation id behind a reference, so a screen working in references can act. */
  private readonly idsByReference = new Map<string, string>();

  /** The server's action list per donation. */
  private readonly actionsByReference = new Map<string, readonly string[]>();

  constructor() {
    this.refresh();
    this.organisationScope.onOrganisationChange(() => this.reloadForOrganisation());
  }

  /**
   * Everything here belongs to ONE Organisation, so a switch discards it and reloads.
   *
   * DISCARD FIRST, RELOAD SECOND, and the order is the point. Reloading alone would leave the
   * previous Organisation's rows on the screen for the length of a round trip — visible, readable
   * and wrong. Clearing first shows an empty state for that moment instead, which is honest about
   * what is known. See `OrganisationScopeService` for why this is a notification rather than
   * something the switcher calls.
   */
  private reloadForOrganisation(): void {
    this.records.set([]);
    this.rows.set([]);
    this.serverTotal.set(0);
    this.loadError.set(null);
    this.idsByReference.clear();
    this.actionsByReference.clear();
    this.refresh();
  }

  get(reference: string): DonationRecord | undefined {
    return this.records().find((record) => record.reference === reference);
  }

  /** The API row behind a reference, for the fields the screen model does not carry. */
  row(reference: string): AttributionListItem | undefined {
    return this.rows().find((row) => row.reference === reference);
  }

  /**
   * Donations attributed to one campaign.
   *
   * MATCHED ON THE CODE AS WELL AS THE NAME. The old version matched on the campaign NAME alone,
   * which meant two campaigns with the same name in different periods - a common thing - shared
   * their donations.
   */
  forCampaign(campaignName: string): readonly DonationRecord[] {
    const query = campaignName.trim().toLowerCase();

    return this.records().filter((record) => {
      const row = this.row(record.reference);

      return (
        record.campaign.trim().toLowerCase() === query
        || (row?.campaignCode ?? '').trim().toLowerCase() === query
      );
    });
  }

  permittedActions(reference: string): readonly string[] {
    return this.actionsByReference.get(reference) ?? [];
  }

  can(reference: string, action: string): boolean {
    return this.permittedActions(reference).some(
      (candidate) => candidate.toLowerCase() === action.toLowerCase(),
    );
  }

  /**
   * Reloads from the API.
   *
   * THE SUMMARY IS FETCHED ALONGSIDE, because it is computed server-side over the whole set rather
   * than over the loaded page. A breakdown computed in the browser from one page of donations
   * would describe that page and be read as describing the organisation.
   */
  refresh(campaignId?: string): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.searchAttribution({ pageSize: 200, campaignId }).subscribe({
      next: (page) => {
        this.idsByReference.clear();
        this.actionsByReference.clear();

        for (const item of page.items) {
          this.idsByReference.set(item.reference, item.donationId);
          this.actionsByReference.set(item.reference, item.permittedActions);
        }

        this.rows.set(page.items);
        this.records.set(page.items.map((item) => this.toRecord(item)));
        this.serverTotal.set(page.totalCount);
        this.isLoading.set(false);
      },
      error: () => {
        this.rows.set([]);
        this.records.set([]);
        this.serverTotal.set(0);
        this.isLoading.set(false);
        this.loadError.set('The attributed donations could not be loaded.');
      },
    });

    this.api.getAttributionSummary(campaignId).subscribe({
      next: (summary) => this.summary.set(summary),
      error: () => this.summary.set(null),
    });
  }

  /** One donation's full attribution trail, hop by hop. */
  loadDetail(reference: string): void {
    const id = this.idsByReference.get(reference);

    if (!id) {
      return;
    }

    this.api.getAttribution(id).subscribe({
      next: (detail) => this.detail.set(detail),
      error: () => this.loadError.set('The attribution trail could not be loaded.'),
    });
  }

  /** The trail for the donation last asked about. */
  readonly detail = signal<AttributionDetail | null>(null);

  /**
   * Asks for a donation's attribution to be looked at again.
   *
   * THIS IS THE ONLY WRITE THE STORE HAS, and it does not change the donation. At most one open
   * request per donation, enforced server-side, so two people cannot end up investigating the same
   * gift without knowing about each other.
   */
  requestCorrection(
    reference: string,
    reason: string,
    proposedCampaignId?: string,
    proposedTrackingAssetId?: string,
  ): void {
    const donationId = this.idsByReference.get(reference);

    if (!donationId) {
      return;
    }

    this.api
      .requestAttributionCorrection({
        donationId,
        reason,
        proposedCampaignId: proposedCampaignId ?? null,
        proposedTrackingAssetId: proposedTrackingAssetId ?? null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => this.loadError.set('The correction request could not be raised.'),
      });
  }

  /**
   * Closes a correction request.
   *
   * `attributionChanged` IS A SEPARATE ANSWER FROM BEING RESOLVED. Most requests end with "checked,
   * it was right", and recording the two as one would make it impossible to tell how often tracking
   * is actually getting it wrong.
   */
  resolveCorrection(
    requestId: string,
    expectedVersion: number,
    resolutionNote: string,
    attributionChanged: boolean,
  ): void {
    this.api
      .resolveAttributionCorrection(requestId, {
        expectedVersion,
        resolutionNote,
        attributionChanged,
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => this.loadError.set('The correction request could not be closed.'),
      });
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /**
   * One API row as the explorer reads it.
   *
   * SEVERAL SCREEN FIELDS DESCRIBE A LIFECYCLE THE API MODELS DIFFERENTLY - the intent, capture,
   * settlement and reconciliation timestamps were separate strings on the seeded record. The API
   * returns the donation's status and the moment it was received; the intermediate timestamps
   * belong to the payments module and are shown on its own screens. They are filled with the
   * status rather than with invented times, because a plausible-looking timestamp nobody recorded
   * is worse than an honest blank.
   */
  private toRecord(item: AttributionListItem): DonationRecord {
    const received = item.receivedAtUtc;

    return {
      reference: item.reference,
      campaign: item.campaignName,
      trackingAsset: item.trackingReference || '—',
      source: item.sourceName || '—',
      medium: item.mediumName || '—',
      leadOrDonor: item.donorName,
      intentCreated: received,
      paymentCaptured: received,
      settlementRecon: item.status,
      reconciliationCaptured: received,
      reconciliation: item.status,
      attributionSnapshot: item.attributionDescription,
      correctionRequest: item.hasOpenCorrectionRequest ? 'Open' : 'None',

      // The audit chain lives in the audit log rather than on the donation; the reference is what
      // somebody would search it by.
      auditChain: item.reference,

      lifecycle: item.status,
      owner: item.donorName,
      downstreamStatus: item.attributionDescription,
      amount: `${item.amount.toLocaleString('en-IN')} ${item.currencyCode}`,

      // The screen used these to drive its own local rules. They are now read from what the server
      // actually said, so a button appears when the API would accept it and not otherwise.
      restricted: !item.permittedActions.includes('RequestCorrection'),
      hasOpenCorrection: item.hasOpenCorrectionRequest,

      // A DONATION IS NEVER A DRAFT. The seeded data had drafts so the screen could demonstrate a
      // delete action; a real donation is a record of money that moved.
      isDraft: false,

      hasDownstreamReference: item.isAttributed,
      staleAfterOpen: false,
      dependencyWillFail: false,
    };
  }
}

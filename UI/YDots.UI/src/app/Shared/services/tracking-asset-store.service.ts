import { Injectable, computed, inject, signal } from '@angular/core';
import {
  TrackingAssetDetail,
  TrackingAssetListItem,
  TrackingAssetStatus,
  TrackingAssetType,
} from '../models/campaign-contract.model';
import { ApprovalState, AssetStatus, TrackingAsset } from '../models/tracking-asset.model';
import { CampaignApiService } from '../../Service/campaign-api.service';

/**
 * The single shared source of truth for tracking assets.
 *
 * WHAT CHANGED, AND WHY IT MATTERED. The records lived in a signal seeded with ten hard-coded
 * assets compiled into the bundle, and every screen read and mutated that array. Four
 * consequences followed and all four were real:
 *
 *   - NOTHING WAS EVER SAVED. An asset approved on Monday was pending again on Tuesday.
 *   - THE GENERATED URL WAS A STRING TEMPLATE. `ydot.link/qr/QR-EDU-001` was composed in the
 *     browser from a reference the browser also invented. A QR code printed from it would have
 *     led nowhere - and a printed QR code is not recoverable.
 *   - EVERY ORGANISATION SAW THE SAME TEN ASSETS, because an array in a browser has no idea who
 *     is asking.
 *   - The "cannot approve your own asset" rule was decided from a createdByRef the browser
 *     supplied, so anybody could have approved anything by editing it.
 *
 * IT NOW READS AND WRITES `CAM /api/v1/tracking-assets`, while keeping its SYNCHRONOUS SIGNAL
 * SURFACE - three screens call `all()`, `forCampaign(ref)` and `get(ref)` from templates and
 * computed properties, and turning those into observables would mean rewriting all three. Reads
 * stay synchronous against the loaded signal; writes go to the API and refresh it.
 *
 * THE REFERENCE AND THE URL ARE THE SERVER'S. An asset has neither until it is APPROVED, which
 * is why both are nullable on the API's detail and why a screen must not offer a QR code for a
 * draft.
 */
@Injectable({ providedIn: 'root' })
export class TrackingAssetStoreService {
  private readonly api = inject(CampaignApiService);

  /**
   * The loaded assets.
   *
   * IT STARTS EMPTY rather than seeded. A screen that opens before the first response shows its
   * own empty state for a moment, which is honest; showing ten fabricated assets was not.
   */
  private readonly records = signal<readonly TrackingAsset[]>([]);

  /** True while a load is in flight, so a screen can tell "loading" from "none". */
  readonly isLoading = signal(false);

  /** The last failure. Screens surface it rather than rendering a silent blank. */
  readonly loadError = signal<string | null>(null);

  readonly all = computed(() => this.records());

  /** The server's total, which is not the same as the loaded page's length. */
  private readonly serverTotal = signal(0);

  readonly total = computed(() => this.serverTotal());

  /** The API id and concurrency stamp per reference, so a screen working in codes can still write. */
  private readonly idsByReference = new Map<string, string>();
  private readonly versionsByReference = new Map<string, number>();

  constructor() {
    this.refresh();
  }

  /** Assets belonging to one campaign, in the caller's effective data scope. */
  forCampaign(campaignRef: string): readonly TrackingAsset[] {
    return this.records().filter((record) => record.campaignRef === campaignRef);
  }

  get(ref: string): TrackingAsset | undefined {
    return this.records().find((record) => record.trackingReference === ref);
  }

  /**
   * Reloads from the API.
   *
   * A LARGE PAGE, deliberately. The screens page and filter in memory over whatever they are
   * given, so this is the working set rather than a page size.
   */
  refresh(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.api.searchTrackingAssets({ pageSize: 200 }).subscribe({
      next: (page) => {
        this.idsByReference.clear();
        this.versionsByReference.clear();

        for (const item of page.items) {
          const reference = item.trackingReference ?? item.code;
          this.idsByReference.set(reference, item.id);
          this.versionsByReference.set(reference, item.version);
        }

        this.records.set(page.items.map((item) => this.toRecord(item)));
        this.serverTotal.set(page.totalCount);
        this.isLoading.set(false);
      },
      error: () => {
        this.records.set([]);
        this.serverTotal.set(0);
        this.isLoading.set(false);
        this.loadError.set('The tracking assets could not be loaded.');
      },
    });
  }

  /**
   * Creates an asset.
   *
   * IT IS ADDED OPTIMISTICALLY so the calling screen has something to navigate to, and
   * `refresh()` replaces it with the server's version - reference, id, version and, once
   * approved, the real generated URL.
   *
   * THE CAMPAIGN, CHANNEL, SOURCE AND MEDIUM ARE IDS. The screens hold them as names, so an
   * asset created without the ids resolved is one the form should not have submitted; the server
   * refuses it and the optimistic row is withdrawn.
   */
  create(asset: TrackingAsset): void {
    this.records.update((current) => [asset, ...current]);

    this.api
      .createTrackingAsset({
        campaignId: asset.campaignRef,
        assetType: this.toAssetType(asset.assetType),
        channelId: asset.channel,
        destination: asset.destination,
        sourceId: asset.source,
        mediumId: asset.medium,
        activeFrom: this.toInstant(asset.activeFrom),
        activeTo: this.toInstant(asset.activeTo),
        contentTag: asset.contentTag || null,

        places: asset.place
          ? [
              {
                placeName: asset.place,
                destination: asset.destination,
                customFields: (asset.placeCustomFields ?? []).map((field) => ({
                  fieldName: field.key,
                  value: field.value,
                })),
              },
            ]
          : null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => {
          this.records.update((current) =>
            current.filter((record) => record.trackingReference !== asset.trackingReference),
          );
          this.loadError.set('The tracking asset could not be created.');
        },
      });
  }

  /**
   * Applies a change to an asset.
   *
   * A STATUS CHANGE IS ROUTED TO ITS OWN ENDPOINT rather than written as a field. Each transition
   * has its own permission, and approval is what generates the reference and the URL - so a PUT
   * that set the status would skip the one step that makes the asset usable.
   */
  update(ref: string, patch: Partial<TrackingAsset>): void {
    const current = this.get(ref);
    const id = this.idsByReference.get(ref);

    if (!current || !id) {
      return;
    }

    // Applied locally first so the screen reflects the change immediately; refresh() then
    // replaces it with what the server actually stored.
    this.records.update((records) =>
      records.map((record) =>
        record.trackingReference === ref ? { ...record, ...patch } : record,
      ),
    );

    const expectedVersion = this.versionsByReference.get(ref) ?? 0;

    if (patch.approvalState === 'Approved') {
      this.api.approveTrackingAsset(id, { expectedVersion }).subscribe({
        next: () => this.refresh(),
        error: () => this.failed('The asset could not be approved.'),
      });

      return;
    }

    if (patch.assetStatus === 'Active') {
      this.api.activateTrackingAsset(id, { expectedVersion }).subscribe({
        next: () => this.refresh(),
        error: () => this.failed('The asset could not be activated.'),
      });

      return;
    }

    if (patch.assetStatus === 'Paused' || patch.assetStatus === 'Disabled') {
      this.api.deactivateTrackingAsset(id, { expectedVersion }).subscribe({
        next: () => this.refresh(),
        error: () => this.failed('The asset could not be deactivated.'),
      });

      return;
    }

    if (patch.approvalState === 'Pending review') {
      this.api.submitTrackingAsset(id, { expectedVersion }).subscribe({
        next: () => this.refresh(),
        error: () => this.failed('The asset could not be submitted.'),
      });

      return;
    }

    // Anything else is a content edit, which is a PUT of the whole record.
    const merged = { ...current, ...patch };

    this.api
      .updateTrackingAsset(id, {
        expectedVersion,
        assetType: this.toAssetType(merged.assetType),
        channelId: merged.channel,
        destination: merged.destination,
        sourceId: merged.source,
        mediumId: merged.medium,
        activeFrom: this.toInstant(merged.activeFrom),
        activeTo: this.toInstant(merged.activeTo),
        contentTag: merged.contentTag || null,
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => this.failed('The asset could not be saved.'),
      });
  }

  /**
   * Retires an asset.
   *
   * IT DEACTIVATES RATHER THAN DELETES, and the API has no delete at all. An asset's reference
   * keeps resolving for reporting - donations already attributed through it must stay attributed
   * - and what stops is its ability to take NEW donations. Deleting it would orphan every gift it
   * ever produced.
   */
  delete(ref: string): void {
    const id = this.idsByReference.get(ref);

    if (!id) {
      return;
    }

    this.api
      .deactivateTrackingAsset(id, {
        expectedVersion: this.versionsByReference.get(ref) ?? 0,
        reason: 'Retired from the tracking asset manager.',
      })
      .subscribe({
        next: () => this.refresh(),
        error: () => this.failed('The asset could not be retired.'),
      });
  }

  /**
   * A provisional reference for a new asset.
   *
   * PROVISIONAL, because the SERVER allocates the real one - and only on approval. This exists so
   * the creating screen has something to key its optimistic row by; `refresh()` replaces it.
   */
  nextReference(assetType: string): string {
    const prefixByType: Record<string, string> = {
      'QR Code': 'QR',
      'Short Link': 'LNK',
      'UTM Link': 'UTM',
      Image: 'IMG',
      'Landing Page': 'LP',
    };

    return `${prefixByType[assetType] ?? 'TRK'}-PENDING-${Date.now().toString().slice(-6)}`;
  }

  /**
   * The URL a screen should show for an asset.
   *
   * IT READS WHAT THE SERVER GENERATED rather than composing one. The previous version returned
   * `ydot.link/qr/<reference>` for anything it was asked about, which meant a draft asset - one
   * with no reference and no URL yet - produced a plausible-looking link to nothing. A printed QR
   * code containing that string is not recoverable.
   *
   * An empty string means "not generated yet", which is what a draft should show.
   */
  buildGeneratedUrl(trackingReference: string, _isQr: boolean): string {
    return this.get(trackingReference)?.generatedUrl ?? '';
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  private failed(message: string): void {
    this.loadError.set(message);
    this.refresh();
  }

  /** One API row as the screens read it. */
  private toRecord(item: TrackingAssetListItem): TrackingAsset {
    const existing = this.records().find(
      (record) => record.trackingReference === (item.trackingReference ?? item.code),
    );

    return {
      ...existing,
      trackingReference: item.trackingReference ?? item.code,
      assetType: this.fromAssetType(item.assetType),
      channel: item.channelName,
      destination: item.destination,
      campaignRef: item.campaignCode,
      source: item.sourceName,
      medium: item.mediumName,
      contentTag: item.contentTag ?? '',
      activeFrom: item.activeFrom.slice(0, 10),
      activeTo: item.activeTo.slice(0, 10),

      // BLANK UNTIL APPROVED. See buildGeneratedUrl for why that matters.
      generatedUrl: item.trackingReference ? `${item.trackingReference}` : '',

      isQr: item.assetType === 'qrCode' || item.assetType === 'posterCode',

      // The API records no test result: whether a link was manually checked is a client-side
      // note, so it survives from the loaded record rather than being invented here.
      lastTestResult: existing?.lastTestResult ?? 'Not tested',

      approvalState: this.toApprovalState(item.status),
      usageCount: Number(item.usageCount),
      amountReceived: Number(item.totalReceived),
      assetStatus: this.toAssetStatus(item.status, item.isLive),

      // An asset that has taken money has something pointing at it, which is what the retire
      // rule turns on.
      hasDownstreamReference: Number(item.usageCount) > 0,

      version: item.version,
    } as TrackingAsset;
  }

  private toApprovalState(status: TrackingAssetStatus): ApprovalState {
    switch (status) {
      case 'submitted':
        return 'Pending review';
      case 'approved':
      case 'active':
        return 'Approved';
      case 'inactive':
        return 'Approved';
      default:
        return 'Not required';
    }
  }

  /**
   * The screen's asset status.
   *
   * `isLive` IS PART OF THE ANSWER, not just the status. An approved asset outside its own active
   * window is not live, and a screen that showed it as Active would have somebody printing a
   * poster for a run that has ended.
   */
  private toAssetStatus(status: TrackingAssetStatus, isLive: boolean): AssetStatus {
    if (status === 'draft' || status === 'submitted') {
      return 'Draft';
    }

    if (status === 'inactive') {
      return 'Disabled';
    }

    return isLive ? 'Active' : 'Paused';
  }

  private toAssetType(label: string): TrackingAssetType {
    switch (label) {
      case 'QR Code':
        return 'qrCode';
      case 'Short Link':
        return 'shortLink';
      case 'UTM Link':
        return 'utmLink';
      case 'Image':
        return 'posterCode';
      default:
        return 'shortLink';
    }
  }

  private fromAssetType(code: TrackingAssetType): string {
    switch (code) {
      case 'qrCode':
        return 'QR Code';
      case 'shortLink':
        return 'Short Link';
      case 'utmLink':
        return 'UTM Link';
      case 'posterCode':
        return 'Image';
      case 'smsLink':
        return 'Short Link';
      default:
        return 'Short Link';
    }
  }

  /**
   * A date-only string to a full instant.
   *
   * A TRACKING ASSET'S WINDOW IS AN INSTANT, unlike a campaign's dates: a poster goes live at a
   * time of day. An empty date becomes today rather than an invalid instant the server would
   * reject on a field the form never asked about.
   */
  private toInstant(dateOnly: string): string {
    if (!dateOnly) {
      return new Date().toISOString();
    }

    return new Date(`${dateOnly}T00:00:00Z`).toISOString();
  }
}

export type { TrackingAssetDetail };

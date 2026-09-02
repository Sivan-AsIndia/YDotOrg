import { Injectable, computed, inject, signal } from '@angular/core';
import {
  TrackingAssetDetail,
  TrackingAssetListItem,
  TrackingAssetStatus,
  TrackingAssetType,
} from '../models/campaign-contract.model';
import { ApprovalState, AssetStatus, TrackingAsset } from '../models/tracking-asset.model';
import { CampaignApiService } from '../../Service/campaign-api.service';
import { OrganisationScopeService } from './organisation-scope.service';
import { CampaignStoreService } from './campaign-store.service';
import { apiErrorMessage } from '../models/api-response.model';

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
  private readonly organisationScope = inject(OrganisationScopeService);

  /** Holds the code -> API id map. A tracking asset is created against the campaign's ID. */
  private readonly campaigns = inject(CampaignStoreService);

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
    this.organisationScope.onOrganisationChange(() => this.reloadForOrganisation());
  }

  /**
   * Everything here belongs to ONE Organisation, so a switch discards it and reloads.
   *
   * Discarded FIRST: reloading alone would leave the previous Organisation's rows readable on
   * screen for the length of a round trip. See `OrganisationScopeService`.
   */
  private reloadForOrganisation(): void {
    this.records.set([]);
    this.serverTotal.set(0);
    this.loadError.set(null);
    this.idsByReference.clear();
    this.versionsByReference.clear();
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
  create(
    asset: TrackingAsset,
    onDone?: (outcome: { readonly created: boolean; readonly error?: string }) => void,
  ): void {
    // ---- THE CAMPAIGN GOES UP AS ITS ID -----------------------------------------------------
    //
    // `campaignRef` is the campaign's CODE - 'CAMP-2026-0004' - because that is the key every
    // screen holds a campaign by. It went into `campaignId`, which the API declares as a Guid, so
    // System.Text.Json refused the whole body with
    //
    //     400  The JSON value could not be converted to CreateTrackingAssetRequest
    //
    // before model binding finished. Nothing was ever created, from either the Campaign Manager's
    // screen or the Organisation Administrator's, and the screen still said "Tracking asset
    // created" because this method reported nothing back to it.
    const campaignId = this.campaigns.apiId(asset.campaignRef);

    if (!campaignId) {
      const message =
        'That campaign could not be identified. Reload the page and choose the campaign again.';

      this.loadError.set(message);
      onDone?.({ created: false, error: message });
      return;
    }

    this.records.update((current) => [asset, ...current]);

    this.api
      .createTrackingAsset({
        campaignId,
        assetType: this.toAssetType(asset.assetType),

        // The channel, source and medium are ALREADY IDS: the Generate form now picks them from
        // the CAM reference catalogues rather than from a hard-coded label list and two free-text
        // boxes. See TrackingAssetManagerComponent.loadReferenceCatalogues.
        channelId: asset.channel,
        destination: asset.destination,
        sourceId: asset.source,
        mediumId: asset.medium,
        activeFrom: this.toInstant(asset.activeFrom),
        activeTo: this.toInstant(asset.activeTo),
        contentTag: asset.contentTag || null,

        // THE STATUS THE FORM ASKED FOR, SENT EXPLICITLY. It used to be left off entirely and the
        // server defaulted it to Draft - so the "Asset status" the person chose on the Generate
        // form was collected, shown back to them in the success toast, and never left the browser.
        // The server now requires it.
        status: this.toApiStatus(asset.assetStatus),

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
        next: () => {
          this.refresh();
          onDone?.({ created: true });
        },
        // THE SERVER'S OWN MESSAGE, and the caller is told. A create is refused for reasons a
        // person can act on - a duplicate destination, a retired channel, a campaign that cannot
        // take assets - and 'The tracking asset could not be created.' threw all of it away.
        error: (error: unknown) => {
          this.records.update((current) =>
            current.filter((record) => record.trackingReference !== asset.trackingReference),
          );

          const message = apiErrorMessage(error, 'The tracking asset could not be created.');
          this.loadError.set(message);
          onDone?.({ created: false, error: message });
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

    // THE MAKER'S HALF OF THE DISABLE PAIR. An Initiator holds request-disable and not
    // deactivate, so routing their click to the decision endpoint answered 403.
    if (patch.assetStatus === 'Disable requested') {
      this.api.requestDisableTrackingAsset(id, { expectedVersion }).subscribe({
        next: () => this.refresh(),
        error: (error: unknown) =>
          this.failed(apiErrorMessage(error, 'The disable request could not be raised.')),
      });

      return;
    }

    if (patch.assetStatus === 'Paused' || patch.assetStatus === 'Disabled') {
      this.api.deactivateTrackingAsset(id, { expectedVersion }).subscribe({
        next: () => this.refresh(),
        error: (error: unknown) =>
          this.failed(apiErrorMessage(error, 'The asset could not be deactivated.')),
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
   * Discards an unused DRAFT asset, or retires anything further along.
   *
   * THE TWO ARE DIFFERENT ACTS AND NOW CALL DIFFERENT ENDPOINTS. A draft has never been
   * activated, so it holds no tracking reference and nothing can have been attributed through it
   * - it can simply go, and CAM's DELETE removes it. Anything past Draft is DEACTIVATED instead,
   * because its reference has to keep resolving for the donations already credited through it;
   * deleting one of those would orphan every gift it ever produced.
   *
   * IT USED TO DEACTIVATE IN BOTH CASES, with a note saying the API had no delete. It has one
   * now, so a draft discarded from the register is actually gone rather than left on the list as
   * an Inactive row nobody asked for.
   */
  delete(ref: string): void {
    const id = this.idsByReference.get(ref);

    if (!id) {
      return;
    }

    const expectedVersion = this.versionsByReference.get(ref) ?? 0;

    if (this.get(ref)?.assetStatus === 'Draft') {
      this.api
        .deleteDraftTrackingAsset(id, {
          expectedVersion,
          reason: 'Unused draft discarded from the tracking asset manager.',
        })
        .subscribe({
          next: () => this.refresh(),
          error: (error: unknown) =>
            this.failed(apiErrorMessage(error, 'The draft could not be deleted.')),
        });

      return;
    }

    this.api
      .deactivateTrackingAsset(id, {
        expectedVersion,
        reason: 'Retired from the tracking asset manager.',
      })
      .subscribe({
        next: () => this.refresh(),
        error: (error: unknown) =>
          this.failed(apiErrorMessage(error, 'The asset could not be retired.')),
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

      isQr: item.assetType === 'qrCode',

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
      case 'disableRequested':
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
   * IT NOW REPORTS THE STATUS THE SERVER ACTUALLY HOLDS. Two of the five collapsed onto something
   * else, and both were visible to anyone using the screen:
   *
   *   - `submitted` returned 'Draft'. An initiator submitted an asset for approval, the API moved
   *     it to Submitted, and the Status column went on saying Draft - so the submission looked
   *     like it had not happened. Worse, `submitAllowed` and `editAllowed` both test for 'Draft',
   *     so the row went on offering Submit and Edit on an asset that was already out for review.
   *
   *   - `approved` fell through to `isLive ? 'Active' : 'Paused'`, and `IsLiveAt` on the server is
   *     `Status == Active && inside the window` - so a freshly approved asset is never live, and
   *     the approver saw their own approval land as 'Paused'. A state nobody chose, describing a
   *     stop that nobody made.
   *
   * `isLive` STILL DECIDES ONE THING, and only that one: an Active asset outside its own window is
   * shown as Paused rather than Active, because it is not resolving scans. That is the case the
   * flag was added for - somebody printing a poster for a run that has ended - and it is the only
   * case where the calendar, rather than the status, is the honest answer.
   */
  private toAssetStatus(status: TrackingAssetStatus, isLive: boolean): AssetStatus {
    switch (status) {
      case 'draft':
        return 'Draft';

      case 'submitted':
        return 'Submitted';

      case 'approved':
        return 'Approved';

      // STILL LIVE, and the badge says what is pending rather than what has happened. The asset
      // goes on resolving scans until somebody decides the request - nothing about asking should
      // change what a donor's scan does.
      case 'disableRequested':
        return 'Disable requested';

      // 'Disabled' rather than 'Inactive' because that is the word the Disable action writes, and
      // the badge for the two is the same.
      case 'inactive':
        return 'Disabled';

      default:
        return isLive ? 'Active' : 'Paused';
    }
  }

  /**
   * The screen's asset status back onto the API's enum, for a create.
   *
   * ONLY DRAFT AND SUBMITTED ARE REACHABLE HERE, which is why everything else collapses onto
   * Submitted rather than being mapped faithfully. The API refuses a create in any further state:
   * Approved, Active and Inactive are reached through their own endpoints, each with its own
   * permission and its own rules, and accepting one on a create would route around all of them.
   */
  private toApiStatus(status: AssetStatus): TrackingAssetStatus {
    return status === 'Draft' ? 'draft' : 'submitted';
  }

  /** True while a disable request is waiting on an approver. */
  awaitingDisableDecision(ref: string): boolean {
    return this.get(ref)?.assetStatus === 'Disable requested';
  }

  /**
   * The screen's asset-type label onto the API's enum.
   *
   * THE API HAS FOUR: QRCode, ShortLink, UTMLink, LandingPage. This mapped 'Image' to
   * 'posterCode', which the server does not define, and had no case for 'Landing Page' at all -
   * so choosing Landing Page silently created a short link instead.
   */
  private toAssetType(label: string): TrackingAssetType {
    switch (label) {
      case 'QR Code':
        return 'qrCode';
      case 'Short Link':
        return 'shortLink';
      case 'UTM Link':
        return 'utmLink';
      case 'Landing Page':
        return 'landingPage';
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
      case 'landingPage':
        return 'Landing Page';
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

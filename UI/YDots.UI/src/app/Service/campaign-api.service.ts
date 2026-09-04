import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AllocateBudgetPlanRequest,
  AssignReadinessBlockerRequest,
  AttributionDetail,
  AttributionListItem,
  AttributionSearchFilter,
  AttributionSummary,
  BudgetPlanDecisionRequest,
  BudgetPlanDetail,
  BudgetPlanListItem,
  BudgetPlanSearchFilter,
  CampaignBudgetSummary,
  CampaignDetail,
  CampaignHistoryEntry,
  CampaignLifecycleRequest,
  CampaignListItem,
  CampaignLookup,
  CampaignReadiness,
  CampaignReferenceData,
  CampaignSearchFilter,
  CampaignStatistics,
  CreateCampaignRequest,
  CreateReadinessCheckRequest,
  CreateTrackingAssetRequest,
  ReadinessCheckDetail,
  ReadinessVerdictRequest,
  RequestAttributionCorrectionRequest,
  ResolveAttributionCorrectionRequest,
  ResolveReadinessBlockerRequest,
  ReturnCampaignToDraftRequest,
  ReviseBudgetPlanRequest,
  SubmitBudgetPlanVersionRequest,
  TrackingAssetDetail,
  TrackingAssetLifecycleRequest,
  TrackingAssetListItem,
  TrackingAssetSearchFilter,
  UpdateBudgetPlanVersionRequest,
  UpdateCampaignRequest,
  UpdateReadinessCheckRequest,
  UpdateTrackingAssetRequest,
} from '../Shared/models/campaign-contract.model';

/**
 * The single door to the Campaign service.
 *
 * IT UNWRAPS THE ENVELOPE AND RETURNS THE PAYLOAD. Every endpoint answers the same six-key
 * `ApiResponse`, so `map((response) => response.data!)` happens once here rather than in each
 * screen. A failure never reaches that map: the HTTP interceptor rethrows the parsed envelope,
 * so a component's `error` callback receives something `apiErrorMessage` understands.
 *
 * WHAT THIS REPLACES. `CampaignStoreService` held ten campaigns in a signal, compiled into the
 * bundle, and every screen read and mutated that array. Approving a campaign flipped a string;
 * a refresh restored the original ten. Every organisation saw the same fabricated list, because a
 * signal in a browser has no idea who is asking.
 *
 * THE LIFECYCLE IS SEVEN SEPARATE ENDPOINTS, not one status setter, and that shape is the point.
 * Each transition has its own permission, its own required reason and its own rules - approval
 * refuses the person who submitted, a close needs a second person, a pause needs a statement of
 * what it does to donors mid-flight. A single `PUT status` could express none of that.
 *
 * ONLY THE REFERENCE CATALOGUE IS CACHED. Channels, sources, mediums and the enum lists do not
 * change between two page views. Campaigns and tracking assets are never cached: an approval by a
 * colleague thirty seconds ago must show on the next refresh.
 */
@Injectable({ providedIn: 'root' })
export class CampaignApiService {
  private readonly http = inject(HttpClient);

  private readonly campaignsUrl = `${environment.campaignApiBaseUrl}/campaigns`;
  private readonly trackingAssetsUrl = `${environment.campaignApiBaseUrl}/tracking-assets`;
  private readonly referenceUrl = `${environment.campaignApiBaseUrl}/campaign-reference`;

  /** The readiness endpoints hang off /api/v1 directly rather than under one resource. */
  private readonly rootUrl = environment.campaignApiBaseUrl;

  private referenceData$?: Observable<CampaignReferenceData>;

  // =========================================================================================
  // Campaigns
  // =========================================================================================

  searchCampaigns(filter: CampaignSearchFilter = {}): Observable<PagedResponse<CampaignListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<CampaignListItem>>>(this.campaignsUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /** Counts per status for the register's tiles. Server-side, so they cover every page. */
  getCampaignStatistics(): Observable<CampaignStatistics> {
    return this.http
      .get<ApiResponse<CampaignStatistics>>(`${this.campaignsUrl}/statistics`)
      .pipe(map((response) => response.data!));
  }

  /** The campaign autocomplete, for the tracking-asset and donation forms. */
  lookupCampaigns(search?: string): Observable<CampaignLookup[]> {
    return this.http
      .get<ApiResponse<CampaignLookup[]>>(`${this.campaignsUrl}/lookup`, {
        params: this.toParams({ search }),
      })
      .pipe(map((response) => response.data ?? []));
  }

  getCampaign(id: string): Observable<CampaignDetail> {
    return this.http
      .get<ApiResponse<CampaignDetail>>(`${this.campaignsUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * The append-only audit trail: every lifecycle action, who took it and whether it was allowed.
   *
   * THE PAYLOAD IS PAGED, and this used to say it was not. `GET /campaigns/{id}/history` returns
   * `ApiResponse<PagedResponse<CampaignHistoryResponse>>` - the controller's own
   * `ProducesResponseType` says so - so `response.data` is `{ items, totalCount, page, pageSize }`
   * and never an array. The old body handed that OBJECT back as `CampaignHistoryEntry[]`, and the
   * one caller then called `.map` on it, which threw `entries.map is not a function` inside the
   * subscriber. The Related history tab was empty on every campaign, and the failure did not even
   * reach the error branch that would have said so.
   */
  getCampaignHistory(id: string): Observable<CampaignHistoryEntry[]> {
    return this.http
      .get<ApiResponse<PagedResponse<CampaignHistoryEntry>>>(`${this.campaignsUrl}/${id}/history`)
      .pipe(map((response) => response.data?.items ?? []));
  }

  exportCampaigns(filter: CampaignSearchFilter = {}): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.campaignsUrl}/export`, {
        params: this.toParams(filter),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => this.toDownload(response, 'campaigns.csv')));
  }

  createCampaign(request: CreateCampaignRequest): Observable<CampaignDetail> {
    return this.http
      .post<ApiResponse<CampaignDetail>>(this.campaignsUrl, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Edits a campaign.
   *
   * IT CANNOT CHANGE THE CODE OR THE STATUS. Both are absent from the request by design - see the
   * note on `UpdateCampaignRequest`. A screen that offers either is offering something the API
   * will ignore.
   */
  updateCampaign(id: string, request: UpdateCampaignRequest): Observable<CampaignDetail> {
    return this.http
      .put<ApiResponse<CampaignDetail>>(`${this.campaignsUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Deletes a DRAFT campaign.
   *
   * ONLY A DRAFT, and the server enforces it. Once a campaign has been submitted it has an
   * approval trail; once it has run it has donations attributed to it. Neither can be deleted
   * without destroying a record somebody may need.
   */
  deleteCampaignDraft(id: string, expectedVersion: number): Observable<OutcomeResponse> {
    return this.http
      .request<ApiResponse<OutcomeResponse>>('delete', `${this.campaignsUrl}/${id}`, {
        body: { expectedVersion },
      })
      .pipe(map((response) => response.data!));
  }

  // ---- The lifecycle -------------------------------------------------------------------------
  //
  // SEVEN ENDPOINTS, NOT A STATUS SETTER. See the class comment.

  submitCampaign(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'submit', request);
  }

  /**
   * Approves a campaign.
   *
   * REFUSED TO THE PERSON WHO CREATED OR SUBMITTED IT, whatever permissions they hold. Check
   * `canPerformCampaignAction(detail, 'Approve')` before drawing the button: the server's answer
   * already folds that in, and no local condition can.
   */
  approveCampaign(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'approve', request);
  }

  activateCampaign(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'activate', request);
  }

  /**
   * Pauses a live campaign.
   *
   * `communicationImpact` MATTERS HERE more than anywhere else in the lifecycle: donors may be
   * holding live payment links for this campaign right now, and whoever pauses it has to say what
   * happens to them.
   */
  pauseCampaign(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'pause', request);
  }

  resumeCampaign(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'resume', request);
  }

  /** Asks for a close. A second person approves it - closing is never one signature. */
  requestCampaignClose(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'request-close', request);
  }

  approveCampaignClose(id: string, request: CampaignLifecycleRequest): Observable<OutcomeResponse> {
    return this.lifecycle(id, 'approve-close', request);
  }

  private lifecycle(
    id: string,
    transition: string,
    request: CampaignLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.campaignsUrl}/${id}/${transition}`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Tracking assets
  // =========================================================================================

  searchTrackingAssets(
    filter: TrackingAssetSearchFilter = {},
  ): Observable<PagedResponse<TrackingAssetListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<TrackingAssetListItem>>>(this.trackingAssetsUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  exportTrackingAssets(
    filter: TrackingAssetSearchFilter = {},
  ): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.trackingAssetsUrl}/export`, {
        params: this.toParams(filter),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => this.toDownload(response, 'tracking-assets.csv')));
  }

  createTrackingAsset(request: CreateTrackingAssetRequest): Observable<TrackingAssetDetail> {
    return this.http
      .post<ApiResponse<TrackingAssetDetail>>(this.trackingAssetsUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateTrackingAsset(
    id: string,
    request: UpdateTrackingAssetRequest,
  ): Observable<TrackingAssetDetail> {
    return this.http
      .put<ApiResponse<TrackingAssetDetail>>(`${this.trackingAssetsUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  submitTrackingAsset(
    id: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.assetLifecycle(id, 'submit', request);
  }

  /**
   * Approves a tracking asset, which is what generates its reference and its URL.
   *
   * THE URL DOES NOT EXIST BEFORE THIS. That is why `trackingReference` and `generatedUrl` are
   * nullable on the detail: a draft asset has nothing to print yet, and a screen that offered a
   * QR code for one would produce a code leading nowhere.
   */
  approveTrackingAsset(
    id: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.assetLifecycle(id, 'approve', request);
  }

  activateTrackingAsset(
    id: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.assetLifecycle(id, 'activate', request);
  }

  /**
   * Asks for a live asset to be taken down. Active to DisableRequested.
   *
   * THE MAKER'S HALF of the disable pair. Disabling an asset stops a printed QR code resolving,
   * so the person who made it asks and somebody else decides - which is why an Initiator holds
   * this and not `deactivateTrackingAsset`.
   */
  requestDisableTrackingAsset(
    id: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.assetLifecycle(id, 'request-disable', request);
  }

  /**
   * Destroys an unused Draft asset.
   *
   * THE ONE DELETE IN THE MODULE, and safe only because a Draft has never been activated: it
   * holds no tracking reference, so no donation can have been attributed through it. Anything
   * past Draft is retired with `deactivateTrackingAsset` instead.
   */
  deleteDraftTrackingAsset(
    id: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.trackingAssetsUrl}/${id}`, { body: request })
      .pipe(map((response) => response.data!));
  }

  /**
   * Retires a tracking asset.
   *
   * THE ASSET IS NOT DELETED and its reference keeps resolving for reporting - donations already
   * attributed through it must stay attributed. What stops is its ability to take NEW donations.
   */
  deactivateTrackingAsset(
    id: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.assetLifecycle(id, 'deactivate', request);
  }

  private assetLifecycle(
    id: string,
    transition: string,
    request: TrackingAssetLifecycleRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.trackingAssetsUrl}/${id}/${transition}`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Campaign readiness
  // =========================================================================================

  /** The whole checklist, its counts and the server's `canLaunch` verdict, in one call. */
  getCampaignReadiness(campaignId: string): Observable<CampaignReadiness> {
    return this.http
      .get<ApiResponse<CampaignReadiness>>(`${this.rootUrl}/campaigns/${campaignId}/readiness`)
      .pipe(map((response) => response.data!));
  }

  addReadinessCheck(
    campaignId: string,
    request: CreateReadinessCheckRequest,
  ): Observable<ReadinessCheckDetail> {
    return this.http
      .post<ApiResponse<ReadinessCheckDetail>>(
        `${this.rootUrl}/campaigns/${campaignId}/readiness-checks`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  updateReadinessCheck(
    id: string,
    request: UpdateReadinessCheckRequest,
  ): Observable<ReadinessCheckDetail> {
    return this.http
      .put<ApiResponse<ReadinessCheckDetail>>(`${this.rootUrl}/readiness-checks/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Signs a check off as clear.
   *
   * PASS AND FAIL ARE SEPARATELY PERMISSIONED. An organisation may well want somebody able to
   * record a problem without being able to declare one solved, and two endpoints is what lets it
   * express that.
   */
  passReadinessCheck(id: string, request: ReadinessVerdictRequest): Observable<ReadinessCheckDetail> {
    return this.http
      .post<ApiResponse<ReadinessCheckDetail>>(`${this.rootUrl}/readiness-checks/${id}/pass`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Removes a Pending check from the checklist.
   *
   * PENDING ONLY - CAM refuses anything further along, because a judged check holds somebody's
   * verdict and deleting it would destroy the record that a person looked.
   */
  deleteReadinessCheck(id: string, request: ReadinessVerdictRequest): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.rootUrl}/readiness-checks/${id}`, { body: request })
      .pipe(map((response) => response.data!));
  }

  failReadinessCheck(id: string, request: ReadinessVerdictRequest): Observable<ReadinessCheckDetail> {
    return this.http
      .post<ApiResponse<ReadinessCheckDetail>>(`${this.rootUrl}/readiness-checks/${id}/fail`, request)
      .pipe(map((response) => response.data!));
  }

  /** Raises a blocker against a check and gives it an owner. At most one open per check. */
  addReadinessBlocker(
    checkId: string,
    request: AssignReadinessBlockerRequest,
  ): Observable<ReadinessCheckDetail> {
    return this.http
      .post<ApiResponse<ReadinessCheckDetail>>(
        `${this.rootUrl}/readiness-checks/${checkId}/blockers`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  resolveReadinessBlocker(
    blockerId: string,
    request: ResolveReadinessBlockerRequest,
  ): Observable<ReadinessCheckDetail> {
    return this.http
      .post<ApiResponse<ReadinessCheckDetail>>(
        `${this.rootUrl}/readiness-blockers/${blockerId}/resolve`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  /**
   * Sends a campaign back to Draft from the readiness screen.
   *
   * IT DOES NOT APPROVE ANYTHING. Campaign approval happens in exactly ONE place - the lifecycle
   * endpoint above, which enforces segregation of duties. A second approval path here would be a
   * way around that check, which is why this endpoint only ever moves a campaign backwards.
   */
  returnCampaignToDraft(
    campaignId: string,
    request: ReturnCampaignToDraftRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/campaigns/${campaignId}/readiness/return-to-draft`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Reference data
  // =========================================================================================

  /**
   * Channels, sources, mediums and every enum list, in one cached call.
   *
   * CACHED for the life of the application: every campaign screen opens by asking for the same
   * lists and none of them change between two page views.
   */
  getReferenceData(): Observable<CampaignReferenceData> {
    this.referenceData$ ??= this.http
      .get<ApiResponse<CampaignReferenceData>>(this.referenceUrl)
      .pipe(
        map((response) => response.data!),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.referenceData$;
  }

  invalidateReferenceData(): void {
    this.referenceData$ = undefined;
  }

  // =========================================================================================
  // Internals
  // =========================================================================================
  // Budget and target plans
  // =========================================================================================

  searchBudgetPlans(
    filter: BudgetPlanSearchFilter,
  ): Observable<PagedResponse<BudgetPlanListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<BudgetPlanListItem>>>(`${this.rootUrl}/budget-plans`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getBudgetPlan(id: string): Observable<BudgetPlanDetail> {
    return this.http
      .get<ApiResponse<BudgetPlanDetail>>(`${this.rootUrl}/budget-plans/${id}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * A campaign's committed budget.
   *
   * APPROVED VERSIONS ONLY, one per plan. It is what the campaign detail page shows next to what
   * has actually come in, and it must never include figures nobody has agreed to.
   */
  getCampaignBudgetSummary(campaignId: string): Observable<CampaignBudgetSummary> {
    return this.http
      .get<ApiResponse<CampaignBudgetSummary>>(
        `${this.rootUrl}/campaigns/${campaignId}/budget-summary`,
      )
      .pipe(map((response) => response.data!));
  }

  /**
   * Allocates a plan and its first draft version.
   *
   * THE REFERENCE COMES BACK FROM THE SERVER. Never compose one here: two people allocating at the
   * same moment would be free to mint the same code, and a plan reference is what a finance team
   * quotes in correspondence.
   */
  allocateBudgetPlan(request: AllocateBudgetPlanRequest): Observable<BudgetPlanDetail> {
    return this.http
      .post<ApiResponse<BudgetPlanDetail>>(`${this.rootUrl}/budget-plans`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Revises a plan into a NEW version.
   *
   * A POST, because it creates something. The approved version stays exactly as approved.
   */
  reviseBudgetPlan(
    id: string,
    request: ReviseBudgetPlanRequest,
  ): Observable<BudgetPlanDetail> {
    return this.http
      .post<ApiResponse<BudgetPlanDetail>>(`${this.rootUrl}/budget-plans/${id}/revisions`, request)
      .pipe(map((response) => response.data!));
  }

  updateBudgetPlanVersion(
    versionId: string,
    request: UpdateBudgetPlanVersionRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/budget-plan-versions/${versionId}`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  submitBudgetPlanVersion(
    versionId: string,
    request: SubmitBudgetPlanVersionRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/budget-plan-versions/${versionId}/submit`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  /**
   * Approves a version, making its figures the plan's committed budget.
   *
   * THE SERVER REFUSES THE SUBMITTER, with a 403. Read permittedActions rather than deciding
   * locally whether to draw the button - the segregation-of-duties check is invisible from here.
   */
  approveBudgetPlanVersion(
    versionId: string,
    request: BudgetPlanDecisionRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/budget-plan-versions/${versionId}/approve`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  rejectBudgetPlanVersion(
    versionId: string,
    request: BudgetPlanDecisionRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/budget-plan-versions/${versionId}/reject`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  exportBudgetPlans(
    filter: BudgetPlanSearchFilter,
  ): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.rootUrl}/budget-plans/export`, {
        params: this.toParams(filter),
        observe: 'response',
        responseType: 'blob',
      })
      .pipe(map((response) => this.toDownload(response, 'budget-target-plans.csv')));
  }

  // =========================================================================================
  // Attribution
  // =========================================================================================

  searchAttribution(
    filter: AttributionSearchFilter,
  ): Observable<PagedResponse<AttributionListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<AttributionListItem>>>(`${this.rootUrl}/attribution`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /** One donation's full attribution trail, hop by hop. */
  getAttribution(donationId: string): Observable<AttributionDetail> {
    return this.http
      .get<ApiResponse<AttributionDetail>>(`${this.rootUrl}/attribution/${donationId}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * How income breaks down by channel, source, medium and asset.
   *
   * EVERY SHARE IS OF THE TOTAL INCLUDING UNTRACED GIFTS. Do not renormalise these percentages
   * over the traced portion - a channel shown as 60% when it is 60% of the third that could be
   * traced overstates it threefold.
   */
  getAttributionSummary(campaignId?: string): Observable<AttributionSummary> {
    return this.http
      .get<ApiResponse<AttributionSummary>>(`${this.rootUrl}/attribution/summary`, {
        params: this.toParams(campaignId ? { campaignId } : {}),
      })
      .pipe(map((response) => response.data!));
  }

  exportAttribution(
    filter: AttributionSearchFilter,
  ): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.rootUrl}/attribution/export`, {
        params: this.toParams(filter),
        observe: 'response',
        responseType: 'blob',
      })
      .pipe(map((response) => this.toDownload(response, 'attributed-donations.csv')));
  }

  /**
   * Asks for a donation's attribution to be looked at again.
   *
   * IT DOES NOT CHANGE THE DONATION. Re-attributing a gift restates a campaign's income in every
   * report that follows, so this records the request and the correction is made where the donation
   * lives. At most one open request per donation.
   */
  requestAttributionCorrection(
    request: RequestAttributionCorrectionRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/attribution/correction-requests`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  resolveAttributionCorrection(
    id: string,
    request: ResolveAttributionCorrectionRequest,
  ): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.rootUrl}/attribution/correction-requests/${id}/resolve`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Internals
  // =========================================================================================

  /**
   * Turns a filter object into query parameters, dropping anything unset.
   *
   * NULLS AND EMPTY STRINGS ARE OMITTED, not sent. `?status=` binds as an empty string on the
   * server and would filter to nothing rather than to everything.
   */
  private toParams<TFilter extends object>(filter: TFilter): HttpParams {
    let params = new HttpParams();

    for (const [key, value] of Object.entries(filter)) {
      if (value === null || value === undefined || value === '') {
        continue;
      }

      params = params.set(key, String(value));
    }

    return params;
  }

  private toDownload(
    response: { body: Blob | null; headers: { get(name: string): string | null } },
    fallbackName: string,
  ): { blob: Blob; fileName: string } {
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);

    return {
      blob: response.body ?? new Blob(),
      fileName: match ? decodeURIComponent(match[1]) : fallbackName,
    };
  }
}

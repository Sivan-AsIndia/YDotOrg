import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AcceptLeadRequest,
  AssignFollowUpRequest,
  AssignLeadRequest,
  AssignmentBoardLead,
  AssignmentBoardResponse,
  AssignmentHistory,
  AssignmentRequest,
  BulkRouteRequest,
  BulkRouteResult,
  CampaignLookup,
  CompleteFollowUpRequest,
  ConsentCentreResponse,
  ConsentListItem,
  ContactLeadRequest,
  ConvertLeadRequest,
  CorrectConsentRequest,
  CorrectDonorRequest,
  CreateDonorRequest,
  CreateDuplicateReviewRequest,
  CreateIntentRequest,
  CreateLeadRequest,
  DeduplicateResult,
  DonReferenceData,
  Donor360Response,
  DonorDetail,
  DonorListItem,
  DonorLookup,
  DonorMenuResponse,
  DonorSearchFilter,
  DuplicateReviewDetail,
  DuplicateReviewListResponse,
  EscalateVerificationRequest,
  FollowUp,
  FollowUpPlannerResponse,
  GrantConsentRequest,
  IdentityVerification,
  IdentityVerificationListResponse,
  ChallengeSentResponse,
  LeadCaptureResponse,
  LeadDetail,
  LeadLookup,
  LeadWorkQueueFilter,
  LeadWorkQueueResponse,
  MergeDecisionRequest,
  QualifyLeadRequest,
  ReasonRequest,
  RescheduleFollowUpRequest,
  ScheduleFollowUpRequest,
  SendChallengeRequest,
  UpdateDonorRequest,
  UpdateLeadRequest,
  VerifyCodeRequest,
} from '../Shared/models/donor-contract.model';

/**
 * The single door to the Donors and Leads service.
 *
 * IT UNWRAPS THE ENVELOPE AND RETURNS THE PAYLOAD. Every endpoint answers the same six-key
 * `ApiResponse`, so `map((response) => response.data!)` happens once here rather than in each of
 * fourteen screens. A failure never reaches that map: the HTTP interceptor rethrows the parsed
 * envelope, so a component's `error` callback receives something `apiErrorMessage` understands.
 *
 * WHAT THIS REPLACES. Fourteen screens each imported a JSON file from `assets/data/` at build
 * time - `import * as pageData from '.../lead-work-queue.json'` - and worked over the resulting
 * array in memory. Three consequences followed and all three were real:
 *
 *   - NOTHING WAS EVER SAVED. A lead accepted, contacted or qualified on the work queue existed
 *     until the tab was refreshed and no further.
 *   - THE DATA WAS IDENTICAL FOR EVERY ORGANISATION AND EVERY USER, because a file compiled into
 *     the bundle has no idea who is asking. Tenant isolation stopped at the API boundary.
 *   - The masking rules could not work. Whether a donor's phone number is shown depends on a
 *     permission the server checks; a static file has one answer for everybody.
 *
 * ONE CALL PER SCREEN, DELIBERATELY. Most GETs here return rows, dropdowns, totals and permitted
 * actions together - that is how the API is shaped, and it is the right shape: a screen making
 * six calls renders six times and can show a filter list that disagrees with the rows beneath it.
 *
 * ONLY THE REFERENCE CATALOGUES ARE CACHED. The enum lists behind every selector do not change
 * between two page views, so `shareReplay` means the second screen a person opens costs nothing.
 * Nothing else is cached: a lead assigned by a colleague thirty seconds ago must appear on the
 * next refresh, not when a cache happens to expire.
 */
@Injectable({ providedIn: 'root' })
export class DonorApiService {
  private readonly http = inject(HttpClient);

  private readonly baseUrl = `${environment.donorApiBaseUrl}/donors`;

  /** The enum catalogues, fetched once and shared. See the class comment. */
  private referenceData$?: Observable<DonReferenceData>;

  // =========================================================================================
  // Navigation and reference data
  // =========================================================================================

  /**
   * The section's menu, plus the flags that decide whether sensitive fields and exports are
   * offered at all.
   *
   * HIDING A MENU ENTRY IS A CONVENIENCE, NEVER THE AUTHORISATION. Every route behind it is
   * rechecked by the server when it is actually called, so a person who edits their browser
   * state gets the component and nothing in it.
   */
  getMenu(): Observable<DonorMenuResponse> {
    return this.http
      .get<ApiResponse<DonorMenuResponse>>(`${this.baseUrl}/menu`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Every enum catalogue the section's selectors draw from, in one call.
   *
   * SERVER-SUPPLIED RATHER THAN HARD-CODED IN THE SCREENS, so a value added to an enum appears
   * without anybody remembering to update a list on this side - which is precisely how a dropdown
   * ends up offering a value the API rejects.
   */
  getReferenceData(): Observable<DonReferenceData> {
    this.referenceData$ ??= this.http
      .get<ApiResponse<DonReferenceData>>(`${this.baseUrl}/reference-data`)
      .pipe(
        map((response) => response.data!),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.referenceData$;
  }

  /** Drops the cached catalogues. Called after anything that could change them. */
  invalidateReferenceData(): void {
    this.referenceData$ = undefined;
  }

  /**
   * The campaign autocomplete.
   *
   * NOT CACHED, unlike the enum catalogues: the answer depends on the search term, and caching
   * it would hand the next caller somebody else's results.
   */
  searchCampaigns(search?: string, maximumRows = 20): Observable<CampaignLookup[]> {
    return this.http
      .get<ApiResponse<CampaignLookup[]>>(`${this.baseUrl}/reference-data/campaigns`, {
        params: this.toParams({ search, maximumRows }),
      })
      .pipe(map((response) => response.data ?? []));
  }

  searchLeads(search?: string, maximumRows = 20): Observable<LeadLookup[]> {
    return this.http
      .get<ApiResponse<LeadLookup[]>>(`${this.baseUrl}/reference-data/leads`, {
        params: this.toParams({ search, maximumRows }),
      })
      .pipe(map((response) => response.data ?? []));
  }

  // =========================================================================================
  // Donors
  // =========================================================================================

  searchDonors(filter: DonorSearchFilter = {}): Observable<PagedResponse<DonorListItem>> {
    return this.http
      .get<ApiResponse<PagedResponse<DonorListItem>>>(this.baseUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  lookupDonors(search?: string, maximumRows = 20): Observable<DonorLookup[]> {
    return this.http
      .get<ApiResponse<DonorLookup[]>>(`${this.baseUrl}/lookup`, {
        params: this.toParams({ search, maximumRows }),
      })
      .pipe(map((response) => response.data ?? []));
  }

  /**
   * The CSV export.
   *
   * IT NEEDS ITS OWN PERMISSION and the contact columns are masked in the file unless the caller
   * also holds the sensitive-contact one. A CSV outlives the session that produced it and travels
   * by e-mail, so if anything it needs the masking more than the screen does.
   */
  exportDonors(filter: DonorSearchFilter = {}): Observable<{ blob: Blob; fileName: string }> {
    return this.http
      .get(`${this.baseUrl}/export`, {
        params: this.toParams(filter),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => this.toDownload(response, 'donors.csv')));
  }

  getDonor(id: string): Observable<DonorDetail> {
    return this.http
      .get<ApiResponse<DonorDetail>>(`${this.baseUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  createDonor(request: CreateDonorRequest): Observable<DonorDetail> {
    return this.http
      .post<ApiResponse<DonorDetail>>(this.baseUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateDonor(id: string, request: UpdateDonorRequest): Observable<DonorDetail> {
    return this.http
      .put<ApiResponse<DonorDetail>>(`${this.baseUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  submitDonor(id: string, request: ReasonRequest): Observable<DonorDetail> {
    return this.http
      .post<ApiResponse<DonorDetail>>(`${this.baseUrl}/${id}/submit`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Approves a donor record.
   *
   * A SEPARATE PERMISSION FROM SUBMIT, and the server refuses the person who submitted it. That
   * is a per-record rule no permission code can express, which is why the Approve button is drawn
   * from `permittedActions` rather than from a permission check on this side.
   */
  approveDonor(id: string, request: ReasonRequest): Observable<DonorDetail> {
    return this.http
      .post<ApiResponse<DonorDetail>>(`${this.baseUrl}/${id}/approve`, request)
      .pipe(map((response) => response.data!));
  }

  cancelDonor(id: string, request: ReasonRequest): Observable<DonorDetail> {
    return this.http
      .post<ApiResponse<DonorDetail>>(`${this.baseUrl}/${id}/cancel`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Archives a donor.
   *
   * ARCHIVE, NOT DELETE - and the API has no delete at all. A donor with consent records, a
   * giving history or a receipt cannot be removed without destroying evidence somebody may be
   * asked to produce years later.
   */
  archiveDonor(id: string, request: ReasonRequest): Observable<DonorDetail> {
    return this.http
      .post<ApiResponse<DonorDetail>>(`${this.baseUrl}/${id}/archive`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Donor 360 - SCR-DON-003
  // =========================================================================================

  /** Every panel of the 360 view in ONE call, so no tab is empty when it is opened. */
  getDonor360(donorId: string): Observable<Donor360Response> {
    return this.http
      .get<ApiResponse<Donor360Response>>(`${this.baseUrl}/donor-360/${donorId}`)
      .pipe(map((response) => response.data!));
  }

  correctDonor(donorId: string, request: CorrectDonorRequest): Observable<DonorDetail> {
    return this.http
      .post<ApiResponse<DonorDetail>>(`${this.baseUrl}/donor-360/${donorId}/correct`, request)
      .pipe(map((response) => response.data!));
  }

  /** Records a stated giving intention as a promise. Not a payment - PAY owns those. */
  createDonorIntent(donorId: string, request: CreateIntentRequest): Observable<Donor360Response> {
    return this.http
      .post<ApiResponse<Donor360Response>>(
        `${this.baseUrl}/donor-360/${donorId}/create-intent`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  /** Discards an unsubmitted draft. Only ever a draft: see archiveDonor for the rest. */
  deleteDonorDraft(donorId: string, request: ReasonRequest): Observable<unknown> {
    return this.http.request<ApiResponse<unknown>>(
      'delete',
      `${this.baseUrl}/donor-360/${donorId}`,
      { body: request },
    );
  }

  // =========================================================================================
  // Lead work queue
  // =========================================================================================

  /** Rows, every filter option, the status counts and the permitted actions, in one call. */
  getLeadWorkQueue(filter: LeadWorkQueueFilter = {}): Observable<LeadWorkQueueResponse> {
    return this.http
      .get<ApiResponse<LeadWorkQueueResponse>>(`${this.baseUrl}/lead-work-queue`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getLead(id: string): Observable<LeadDetail> {
    return this.http
      .get<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** The caller becomes the owner. No target user, by design: you accept your own work. */
  acceptLead(id: string, request: AcceptLeadRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}/accept`, request)
      .pipe(map((response) => response.data!));
  }

  assignLead(id: string, request: AssignLeadRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}/assign`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Records one conversation and its outcome.
   *
   * THE CHANNEL IS CHECKED AGAINST THE CONSENT ROWS. Logging a call to somebody who withdrew
   * phone consent is refused rather than recorded, because recording it would be evidence of the
   * breach rather than a note about a conversation.
   */
  contactLead(id: string, request: ContactLeadRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}/contact`, request)
      .pipe(map((response) => response.data!));
  }

  qualifyLead(id: string, request: QualifyLeadRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}/qualify`, request)
      .pipe(map((response) => response.data!));
  }

  closeLead(id: string, request: ReasonRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}/close`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Converts a lead into a donor.
   *
   * IT PRESERVES THE LEAD'S HISTORY AND ATTRIBUTION rather than replacing it - the campaign that
   * produced the lead is what makes the eventual donation attributable, and a conversion that
   * discarded it would break every attribution report downstream.
   */
  convertLead(id: string, request: ConvertLeadRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-work-queue/${id}/convert`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Lead capture - SCR-DON-002
  // =========================================================================================

  /** The blank form plus every catalogue it needs. Pass an id to load an existing draft. */
  getLeadCaptureForm(): Observable<LeadCaptureResponse> {
    return this.http
      .get<ApiResponse<LeadCaptureResponse>>(`${this.baseUrl}/lead-capture`)
      .pipe(map((response) => response.data!));
  }

  getLeadCapture(id: string): Observable<LeadCaptureResponse> {
    return this.http
      .get<ApiResponse<LeadCaptureResponse>>(`${this.baseUrl}/lead-capture/${id}`)
      .pipe(map((response) => response.data!));
  }

  saveLead(request: CreateLeadRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-capture`, request)
      .pipe(map((response) => response.data!));
  }

  updateLead(id: string, request: UpdateLeadRequest): Observable<LeadDetail> {
    return this.http
      .put<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-capture/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Looks for possible duplicates of this lead.
   *
   * THE RESULT NAMES NO ONE. It returns a category, a confidence and a route to compare - never
   * the other person's name, e-mail or phone. Anything more would turn the check into a directory
   * lookup for whoever can reach the screen.
   */
  deduplicateLead(id: string): Observable<DeduplicateResult> {
    return this.http
      .post<ApiResponse<DeduplicateResult>>(`${this.baseUrl}/lead-capture/${id}/deduplicate`, {})
      .pipe(map((response) => response.data!));
  }

  submitLead(id: string, request: ReasonRequest): Observable<LeadDetail> {
    return this.http
      .post<ApiResponse<LeadDetail>>(`${this.baseUrl}/lead-capture/${id}/submit`, request)
      .pipe(map((response) => response.data!));
  }

  /** Only ever a DRAFT. A submitted lead is closed with a reason, never removed. */
  deleteLeadDraft(id: string, request: ReasonRequest): Observable<unknown> {
    return this.http.request<ApiResponse<unknown>>(
      'delete',
      `${this.baseUrl}/lead-capture/${id}`,
      { body: request },
    );
  }

  // =========================================================================================
  // Assignment board
  // =========================================================================================

  getAssignmentBoard(filter: Record<string, unknown> = {}): Observable<AssignmentBoardResponse> {
    return this.http
      .get<ApiResponse<AssignmentBoardResponse>>(`${this.baseUrl}/assignment-board`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /** The append-only ownership trail behind "Inspect history". */
  getAssignmentHistory(leadId: string): Observable<AssignmentHistory> {
    return this.http
      .get<ApiResponse<AssignmentHistory>>(`${this.baseUrl}/assignment-board/${leadId}/history`)
      .pipe(map((response) => response.data!));
  }

  assignFromBoard(request: AssignmentRequest): Observable<AssignmentBoardLead> {
    return this.http
      .post<ApiResponse<AssignmentBoardLead>>(`${this.baseUrl}/assignment-board/assign`, request)
      .pipe(map((response) => response.data!));
  }

  reassignFromBoard(request: AssignmentRequest): Observable<AssignmentBoardLead> {
    return this.http
      .post<ApiResponse<AssignmentBoardLead>>(`${this.baseUrl}/assignment-board/reassign`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Routes several leads at once.
   *
   * THE RESULT REPORTS EACH LEAD SEPARATELY, and the screen must show that rather than a count.
   * Partial processing is explicit here by design: a lead that could not be routed - already
   * closed, outside the caller's scope - is named, never silently skipped.
   */
  bulkRoute(request: BulkRouteRequest): Observable<BulkRouteResult> {
    return this.http
      .post<ApiResponse<BulkRouteResult>>(`${this.baseUrl}/assignment-board/bulk-route`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Consent and preference centre
  // =========================================================================================

  getConsentCentre(filter: Record<string, unknown> = {}): Observable<ConsentCentreResponse> {
    return this.http
      .get<ApiResponse<ConsentCentreResponse>>(
        `${this.baseUrl}/consent-and-preference-centre`,
        { params: this.toParams(filter) },
      )
      .pipe(map((response) => response.data!));
  }

  grantConsent(request: GrantConsentRequest): Observable<ConsentListItem> {
    return this.http
      .post<ApiResponse<ConsentListItem>>(
        `${this.baseUrl}/consent-and-preference-centre/grant`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  withdrawConsent(id: string, request: ReasonRequest): Observable<ConsentListItem> {
    return this.http
      .post<ApiResponse<ConsentListItem>>(
        `${this.baseUrl}/consent-and-preference-centre/${id}/withdraw`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  /**
   * Corrects a consent record.
   *
   * IT SUPERSEDES RATHER THAN OVERWRITES: the original row stays and points at its replacement.
   * That is what makes the consent history defensible - a trail that could be edited is not
   * evidence of anything.
   */
  correctConsent(id: string, request: CorrectConsentRequest): Observable<ConsentListItem> {
    return this.http
      .post<ApiResponse<ConsentListItem>>(
        `${this.baseUrl}/consent-and-preference-centre/${id}/correct`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Duplicate review
  // =========================================================================================

  getDuplicateReviews(filter: Record<string, unknown> = {}): Observable<DuplicateReviewListResponse> {
    return this.http
      .get<ApiResponse<DuplicateReviewListResponse>>(`${this.baseUrl}/duplicate-review`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  createDuplicateReview(request: CreateDuplicateReviewRequest): Observable<DuplicateReviewDetail> {
    return this.http
      .post<ApiResponse<DuplicateReviewDetail>>(`${this.baseUrl}/duplicate-review`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Merges two donor records.
   *
   * IRREVERSIBLE, which is why the detail response carries `donationHistoryImpact` and
   * `consentImpact` and why the screen must show them BEFORE this is called. A merge that
   * surprises somebody afterwards cannot be undone.
   */
  mergeDuplicates(id: string, request: MergeDecisionRequest): Observable<DuplicateReviewDetail> {
    return this.http
      .post<ApiResponse<DuplicateReviewDetail>>(
        `${this.baseUrl}/duplicate-review/${id}/merge`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  rejectDuplicateCandidate(id: string, request: ReasonRequest): Observable<DuplicateReviewDetail> {
    return this.http
      .post<ApiResponse<DuplicateReviewDetail>>(
        `${this.baseUrl}/duplicate-review/${id}/reject-candidate`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Follow-up planner
  // =========================================================================================

  getFollowUpPlanner(filter: Record<string, unknown> = {}): Observable<FollowUpPlannerResponse> {
    return this.http
      .get<ApiResponse<FollowUpPlannerResponse>>(`${this.baseUrl}/follow-up-planner`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * Whether the planned channel is one the donor actually permitted.
   *
   * ASKED BEFORE THE FOLLOW-UP IS SAVED. Scheduling a call to somebody who withdrew phone consent
   * is a breach committed by whoever scheduled it, and this is what gives them the chance not to.
   */
  getConsentWarning(
    donorId?: string,
    leadId?: string,
    channel?: string,
  ): Observable<FollowUpPlannerResponse['followUps']['items'][number]['consentWarning']> {
    return this.http
      .get<ApiResponse<FollowUpPlannerResponse['followUps']['items'][number]['consentWarning']>>(
        `${this.baseUrl}/follow-up-planner/consent-warning`,
        { params: this.toParams({ donorId, leadId, channel }) },
      )
      .pipe(map((response) => response.data!));
  }

  scheduleFollowUp(request: ScheduleFollowUpRequest): Observable<FollowUp> {
    return this.http
      .post<ApiResponse<FollowUp>>(`${this.baseUrl}/follow-up-planner`, request)
      .pipe(map((response) => response.data!));
  }

  assignFollowUp(id: string, request: AssignFollowUpRequest): Observable<FollowUp> {
    return this.http
      .post<ApiResponse<FollowUp>>(`${this.baseUrl}/follow-up-planner/${id}/assign`, request)
      .pipe(map((response) => response.data!));
  }

  completeFollowUp(id: string, request: CompleteFollowUpRequest): Observable<FollowUp> {
    return this.http
      .post<ApiResponse<FollowUp>>(`${this.baseUrl}/follow-up-planner/${id}/mark-complete`, request)
      .pipe(map((response) => response.data!));
  }

  rescheduleFollowUp(id: string, request: RescheduleFollowUpRequest): Observable<FollowUp> {
    return this.http
      .post<ApiResponse<FollowUp>>(`${this.baseUrl}/follow-up-planner/${id}/reschedule`, request)
      .pipe(map((response) => response.data!));
  }

  cancelFollowUp(id: string, request: ReasonRequest): Observable<FollowUp> {
    return this.http
      .post<ApiResponse<FollowUp>>(`${this.baseUrl}/follow-up-planner/${id}/cancel-task`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Donor identity verification
  // =========================================================================================

  getIdentityVerifications(
    filter: Record<string, unknown> = {},
  ): Observable<IdentityVerificationListResponse> {
    return this.http
      .get<ApiResponse<IdentityVerificationListResponse>>(
        `${this.baseUrl}/donor-identity-verification`,
        { params: this.toParams(filter) },
      )
      .pipe(map((response) => response.data!));
  }

  /**
   * Sends a verification code.
   *
   * THE DESTINATION COMES BACK MASKED. The screen can say "we sent a code to ******1234" without
   * the operator ever seeing the full number, which is the point: verifying somebody's identity
   * should not require reading their contact details.
   */
  sendIdentityChallenge(request: SendChallengeRequest): Observable<ChallengeSentResponse> {
    return this.http
      .post<ApiResponse<ChallengeSentResponse>>(
        `${this.baseUrl}/donor-identity-verification/send-challenge`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  verifyIdentityCode(id: string, request: VerifyCodeRequest): Observable<IdentityVerification> {
    return this.http
      .post<ApiResponse<IdentityVerification>>(
        `${this.baseUrl}/donor-identity-verification/${id}/verify-code`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  escalateIdentityVerification(
    id: string,
    request: EscalateVerificationRequest,
  ): Observable<IdentityVerification> {
    return this.http
      .post<ApiResponse<IdentityVerification>>(
        `${this.baseUrl}/donor-identity-verification/${id}/escalate-review`,
        request,
      )
      .pipe(map((response) => response.data!));
  }

  cancelIdentityVerification(id: string, request: ReasonRequest): Observable<IdentityVerification> {
    return this.http
      .post<ApiResponse<IdentityVerification>>(
        `${this.baseUrl}/donor-identity-verification/${id}/cancel-verification`,
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
   * server and would filter to nothing rather than to everything - a grid that mysteriously
   * empties the moment somebody clears a dropdown.
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

  /**
   * Pulls the file name out of the Content-Disposition header.
   *
   * The server names the file with a timestamp so two exports taken minutes apart do not
   * overwrite one another in the browser's downloads folder.
   */
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

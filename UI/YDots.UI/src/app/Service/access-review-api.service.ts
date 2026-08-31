import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AccessReviewCampaignResponse,
  AccessReviewDetailResponse,
  AccessReviewListItemResponse,
  CancelAccessReviewRequest,
  CloseAccessReviewCampaignRequest,
  CreateAccessReviewCampaignRequest,
  CreateAccessReviewRequest,
  DecideAccessReviewRequest,
  DelegateAccessReviewRequest,
  EscalateAccessReviewRequest,
} from '../Shared/models/iam-contract.model';

/** What the review queue can be narrowed by. Mirrors the API's query parameters. */
export interface AccessReviewSearchFilter {
  search?: string;
  status?: string;
  campaignId?: string;
  reviewerUserId?: string;
  assignedToMe?: boolean;
  overdueOnly?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * Access reviews: periodically re-certifying access that people already hold.
 *
 * WHY RE-CERTIFICATION IS A SEPARATE THING FROM APPROVAL
 * ------------------------------------------------------
 * Access is granted once and then kept for years. People change roles, projects end, contractors
 * leave — and none of that revokes anything by itself. A review campaign is the periodic question
 * "does this person still need this?", asked of somebody who can actually answer it.
 *
 * THE TWO RULES, AS ELSEWHERE IN GOVERNANCE
 * -----------------------------------------
 * **Independence**: nobody certifies their own access. Checked server-side against the persisted
 * holder, not against anything sent from here.
 *
 * **Optimistic concurrency**: every decision carries `expectedVersion`, so two reviewers on the
 * same row means the second is told to reload rather than overwriting the first.
 *
 * AND A REVOKE DECISION REMOVES THE ACCESS IMMEDIATELY, in the same transaction as the decision.
 * A review that records "this should be removed" and then does not remove it is worse than no
 * review at all: it produces a paper trail saying the risk was dealt with.
 */
@Injectable({ providedIn: 'root' })
export class AccessReviewApiService {
  private readonly http = inject(HttpClient);
  private readonly reviewsUrl = `${environment.apiBaseUrl}/governance/access-reviews`;
  private readonly campaignsUrl = `${environment.apiBaseUrl}/governance/access-review-campaigns`;

  // =========================================================================================
  // Campaigns
  // =========================================================================================

  /**
   * Opens a campaign and generates one review row per access holding in its scope.
   *
   * Generated up front rather than lazily, which is what makes "how far through are we" a
   * countable number instead of a guess.
   */
  createCampaign(request: CreateAccessReviewCampaignRequest):
    Observable<AccessReviewCampaignResponse> {
    return this.http
      .post<ApiResponse<AccessReviewCampaignResponse>>(this.campaignsUrl, request)
      .pipe(map((response) => response.data!));
  }

  getCampaigns(): Observable<AccessReviewCampaignResponse[]> {
    return this.http
      .get<ApiResponse<AccessReviewCampaignResponse[]>>(this.campaignsUrl)
      .pipe(map((response) => response.data ?? []));
  }

  getCampaign(id: string): Observable<AccessReviewCampaignResponse> {
    return this.http
      .get<ApiResponse<AccessReviewCampaignResponse>>(`${this.campaignsUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Closes a campaign.
   *
   * Anything still undecided is recorded as undecided rather than assumed approved. Silence is
   * not certification, and a campaign that quietly certified everything nobody looked at would
   * be worse than never running one.
   */
  closeCampaign(id: string, request: CloseAccessReviewCampaignRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.campaignsUrl}/${id}/close`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Individual reviews
  // =========================================================================================

  search(filter: AccessReviewSearchFilter): Observable<PagedResponse<AccessReviewListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<AccessReviewListItemResponse>>>(this.reviewsUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  get(id: string): Observable<AccessReviewDetailResponse> {
    return this.http
      .get<ApiResponse<AccessReviewDetailResponse>>(`${this.reviewsUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** Adds a single ad-hoc review outside a campaign, for a one-off concern. */
  create(request: CreateAccessReviewRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(this.reviewsUrl, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Certifies, modifies or revokes.
   *
   * A revoke removes the access there and then — see the note at the top of this file for why
   * that is not left to a later job.
   */
  decide(id: string, request: DecideAccessReviewRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.reviewsUrl}/${id}/decide`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Convenience over `decide`, for a screen with three buttons rather than a form.
   *
   * `applyImmediately` is true on a revoke because that is the whole point: a review recording
   * that access should go, and then not removing it, produces a paper trail saying the risk was
   * dealt with when it was not.
   */
  certify(id: string, expectedVersion: number, decisionReason?: string): Observable<OutcomeResponse> {
    return this.decide(id, { decision: 'retain', expectedVersion, decisionReason });
  }

  revoke(id: string, expectedVersion: number, decisionReason: string): Observable<OutcomeResponse> {
    return this.decide(id, {
      decision: 'revoke',
      expectedVersion,
      decisionReason,
      applyImmediately: true,
    });
  }

  modify(id: string, expectedVersion: number, decisionReason: string): Observable<OutcomeResponse> {
    return this.decide(id, { decision: 'modify', expectedVersion, decisionReason });
  }

  /**
   * Hands a review to somebody better placed to answer it.
   *
   * The original reviewer is kept on the record, because "who was asked" and "who answered" are
   * different questions and an audit of a certification wants both.
   */
  delegate(id: string, request: DelegateAccessReviewRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.reviewsUrl}/${id}/delegate`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Escalates a review the reviewer cannot answer alone.
   *
   * The same handover as a delegation, recorded differently: an escalation says the access looks
   * wrong and removing it is above the reviewer's authority, which is exactly what a governance
   * report needs to count separately.
   */
  escalate(id: string, request: EscalateAccessReviewRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.reviewsUrl}/${id}/escalate`, request)
      .pipe(map((response) => response.data!));
  }

  /** Alias for the vocabulary the review screen uses. */
  getReviews = (filter: AccessReviewSearchFilter) => this.search(filter);

  cancel(id: string, request: CancelAccessReviewRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.reviewsUrl}/${id}/cancel`, request)
      .pipe(map((response) => response.data!));
  }

  private toParams(filter: AccessReviewSearchFilter): HttpParams {
    let params = new HttpParams();

    Object.entries(filter as Record<string, unknown>).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}

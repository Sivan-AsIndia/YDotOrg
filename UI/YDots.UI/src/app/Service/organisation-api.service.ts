import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AddOrganisationDomainRequest,
  ArchiveOrganisationRequest,
  BusinessUnitResponse,
  CheckSubdomainResponse,
  CreateOrganisationRequest,
  CreateOrganisationResponse,
  OrganisationDetailResponse,
  OrganisationDocumentResponse,
  OrganisationDomainResponse,
  OrganisationListItemResponse,
  OrganisationStatisticsResponse,
  OrganisationTimelineResponse,
  ReactivateOrganisationRequest,
  ReviewOrganisationDocumentRequest,
  ReviewOrganisationRequest,
  StartOrganisationReviewRequest,
  SubmitOrganisationRequest,
  SuspendOrganisationRequest,
  TenantStatus,
  TransitionRequest,
  UpdateOrganisationProfileRequest,
  UpdateOrganisationSettingsRequest,
  UploadOrganisationDocumentRequest,
  VerifyOrganisationDomainRequest,
} from '../Shared/models/iam-contract.model';

/** What the Organisation directory can be filtered by. Mirrors the API's query parameters. */
export interface OrganisationSearchFilter {
  search?: string;
  status?: TenantStatus;
  awaitingReviewOnly?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

/**
 * Organisations — Tenants in the schema, Organisations everywhere a person can see.
 *
 * THE TWO HALVES OF THIS SERVICE HAVE DIFFERENT AUDIENCES, and the split is the whole point.
 *
 * The **platform** calls (`/organisations/...`) are SuperAdmin's: create, review, approve,
 * suspend, and every one of them names an Organisation by id.
 *
 * The **mine** calls (`/organisations/mine`) are the TenantAdmin's, and they take NO ID AT ALL.
 * The Organisation is resolved from the signed token, so a TenantAdmin has nothing in the URL to
 * change in order to reach somebody else's. That is the simplest protection available and it is
 * why the two live on separate routes rather than one route with a permission check.
 *
 * EVERY STATE CHANGE CARRIES ExpectedVersion. Two administrators opening the same Organisation
 * means the second save is refused with a 409 rather than silently overwriting the first.
 */
@Injectable({ providedIn: 'root' })
export class OrganisationApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/organisations`;

  // =========================================================================================
  // Platform: administering every Organisation
  // =========================================================================================

  search(filter: OrganisationSearchFilter): Observable<PagedResponse<OrganisationListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<OrganisationListItemResponse>>>(this.baseUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  get(id: string): Observable<OrganisationDetailResponse> {
    return this.http
      .get<ApiResponse<OrganisationDetailResponse>>(`${this.baseUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  getStatistics(): Observable<OrganisationStatisticsResponse> {
    return this.http
      .get<ApiResponse<OrganisationStatisticsResponse>>(`${this.baseUrl}/statistics`)
      .pipe(map((response) => response.data!));
  }

  /** Everything sitting on the SuperAdmin desk, oldest first. */
  getAwaitingReview(): Observable<OrganisationListItemResponse[]> {
    return this.http
      .get<ApiResponse<OrganisationListItemResponse[]>>(`${this.baseUrl}/awaiting-review`)
      .pipe(map((response) => response.data ?? []));
  }

  /**
   * Checks whether a web address is free.
   *
   * Answers only "available or not" and never lists what is taken, so it cannot be walked to
   * enumerate the platform's customers.
   */
  checkSubdomain(subdomain: string): Observable<CheckSubdomainResponse> {
    return this.http
      .post<ApiResponse<CheckSubdomainResponse>>(`${this.baseUrl}/check-subdomain`, { subdomain })
      .pipe(map((response) => response.data!));
  }

  /**
   * Creates an Organisation and invites its first administrator.
   *
   * ONE CALL DOES ALL OF IT — the Organisation, its host, its roles, its default navigation, the
   * TenantAdmin account and the invitation — because an Organisation missing any of those is not
   * usable, and a half-created one is worse than none.
   */
  create(request: CreateOrganisationRequest): Observable<CreateOrganisationResponse> {
    return this.http
      .post<ApiResponse<CreateOrganisationResponse>>(this.baseUrl, request)
      .pipe(map((response) => response.data!));
  }

  resendInvitation(id: string): Observable<CreateOrganisationResponse> {
    return this.http
      .post<ApiResponse<CreateOrganisationResponse>>(`${this.baseUrl}/${id}/resend-invitation`, {})
      .pipe(map((response) => response.data!));
  }

  // ---- Review and decision ------------------------------------------------------------------

  startReview(id: string, request: StartOrganisationReviewRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/start-review`, request)
      .pipe(map((response) => response.data!));
  }

  /** Approves or rejects. A rejection must carry a reason the Organisation can act on. */
  review(id: string, request: ReviewOrganisationRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/review`, request)
      .pipe(map((response) => response.data!));
  }

  activate(id: string, request: TransitionRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/activate`, request)
      .pipe(map((response) => response.data!));
  }

  suspend(id: string, request: SuspendOrganisationRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/suspend`, request)
      .pipe(map((response) => response.data!));
  }

  reactivate(id: string, request: ReactivateOrganisationRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/reactivate`, request)
      .pipe(map((response) => response.data!));
  }

  archive(id: string, request: ArchiveOrganisationRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/archive`, request)
      .pipe(map((response) => response.data!));
  }

  getTimeline(id: string): Observable<OrganisationTimelineResponse[]> {
    return this.http
      .get<ApiResponse<OrganisationTimelineResponse[]>>(`${this.baseUrl}/${id}/timeline`)
      .pipe(map((response) => response.data ?? []));
  }

  // ---- Hosts ----------------------------------------------------------------------------------

  getDomains(id: string): Observable<OrganisationDomainResponse[]> {
    return this.http
      .get<ApiResponse<OrganisationDomainResponse[]>>(`${this.baseUrl}/${id}/domains`)
      .pipe(map((response) => response.data ?? []));
  }

  addDomain(id: string, request: AddOrganisationDomainRequest): Observable<OrganisationDomainResponse> {
    return this.http
      .post<ApiResponse<OrganisationDomainResponse>>(`${this.baseUrl}/${id}/domains`, request)
      .pipe(map((response) => response.data!));
  }

  verifyDomain(id: string, request: VerifyOrganisationDomainRequest): Observable<OrganisationDomainResponse> {
    return this.http
      .post<ApiResponse<OrganisationDomainResponse>>(`${this.baseUrl}/${id}/domains/verify`, request)
      .pipe(map((response) => response.data!));
  }

  removeDomain(id: string, domainId: string): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/domains/${domainId}`)
      .pipe(map((response) => response.data!));
  }

  // ---- Documents ---------------------------------------------------------------------------------

  getDocuments(id: string): Observable<OrganisationDocumentResponse[]> {
    return this.http
      .get<ApiResponse<OrganisationDocumentResponse[]>>(`${this.baseUrl}/${id}/documents`)
      .pipe(map((response) => response.data ?? []));
  }

  reviewDocument(id: string, request: ReviewOrganisationDocumentRequest):
    Observable<OrganisationDocumentResponse> {
    return this.http
      .post<ApiResponse<OrganisationDocumentResponse>>(`${this.baseUrl}/${id}/documents/review`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // The caller's OWN Organisation. No id in any of these.
  // =========================================================================================

  getMine(): Observable<OrganisationDetailResponse> {
    return this.http
      .get<ApiResponse<OrganisationDetailResponse>>(`${this.baseUrl}/mine`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Saves the profile.
   *
   * PARTIAL SAVES ARE ALLOWED. Completeness is enforced at SUBMISSION, so a half-finished
   * profile can be parked and picked up later rather than losing the work.
   */
  updateMine(request: UpdateOrganisationProfileRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/mine`, request)
      .pipe(map((response) => response.data!));
  }

  /** Submits the profile for approval. This is where completeness is checked. */
  submitMine(request: SubmitOrganisationRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/mine/submit`, request)
      .pipe(map((response) => response.data!));
  }

  getMyDocuments(): Observable<OrganisationDocumentResponse[]> {
    return this.http
      .get<ApiResponse<OrganisationDocumentResponse[]>>(`${this.baseUrl}/mine/documents`)
      .pipe(map((response) => response.data ?? []));
  }

  uploadMyDocument(request: UploadOrganisationDocumentRequest): Observable<OrganisationDocumentResponse> {
    return this.http
      .post<ApiResponse<OrganisationDocumentResponse>>(`${this.baseUrl}/mine/documents`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * The Organisation's security policy.
   *
   * An Organisation may TIGHTEN these but never loosen them below the platform floor — the
   * server clamps every value, so a request asking for a four-character minimum password comes
   * back having been raised to the floor rather than accepted.
   */
  updateMySettings(request: UpdateOrganisationSettingsRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/mine/settings`, request)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // BusinessUnit — the platform root
  // =========================================================================================

  getBusinessUnit(): Observable<BusinessUnitResponse> {
    return this.http
      .get<ApiResponse<BusinessUnitResponse>>(`${environment.apiBaseUrl}/business-units/current`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Only the filters that were actually set are sent.
   *
   * An empty `status=` on the query string is not the same as omitting it: the server would try
   * to parse the empty string as an enum and reject the whole request.
   */
  private toParams(filter: OrganisationSearchFilter): HttpParams {
    let params = new HttpParams();

    Object.entries(filter).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}

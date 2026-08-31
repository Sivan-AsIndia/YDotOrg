import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AccessRequestDetailResponse,
  AccessRequestListItemResponse,
  CreateAccessRequestRequest,
  DecideAccessRequestRequest,
  ReturnAccessRequestRequest,
  SubmitAccessRequestRequest,
  UpdateAccessRequestRequest,
  WithdrawAccessRequestRequest,
} from '../Shared/models/iam-contract.model';

/** What the access-request queue can be narrowed by. Mirrors the API's query parameters. */
export interface AccessRequestSearchFilter {
  search?: string;
  status?: string;
  requestType?: string;
  requestedForUserId?: string;
  awaitingMyDecision?: boolean;
  raisedByMe?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * Access requests: asking for access, and deciding on the asking.
 *
 * TWO RULES RUN THROUGH EVERY CALL HERE, and they are the reason the module exists.
 *
 * **Independence.** Nobody approves their own request. The server checks it against the
 * persisted requester, never against anything this client sends, so the rule holds however the
 * request is constructed.
 *
 * **Optimistic concurrency.** Every state change carries `expectedVersion`. Two approvers
 * opening the same request means the second is refused with a 409 rather than silently
 * overwriting the first — and the screen is expected to reload rather than retry.
 *
 * ONE DECIDE ENDPOINT, NOT FOUR. Approve and reject are the same operation with a different
 * answer, and the server applies the access in the SAME TRANSACTION as an approval: an approval
 * that is recorded but not granted is the worst of both worlds, because the trail says yes and
 * the person still cannot work.
 */
@Injectable({ providedIn: 'root' })
export class AccessRequestApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/governance/access-requests`;

  search(filter: AccessRequestSearchFilter):
    Observable<PagedResponse<AccessRequestListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<AccessRequestListItemResponse>>>(this.baseUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * One request, with the actions THIS caller may take on it.
   *
   * `permittedActions` is computed server-side from the state, the caller's permissions and the
   * independence rule, so the buttons the screen renders and the rules the API enforces cannot
   * drift apart.
   */
  get(id: string): Observable<AccessRequestDetailResponse> {
    return this.http
      .get<ApiResponse<AccessRequestDetailResponse>>(`${this.baseUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  create(request: CreateAccessRequestRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(this.baseUrl, request)
      .pipe(map((response) => response.data!));
  }

  /** Edits a draft. Once submitted a request is immutable — reject it and raise another. */
  update(id: string, request: UpdateAccessRequestRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  submit(id: string, request: SubmitAccessRequestRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/submit`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Approves or rejects.
   *
   * On approval the access is granted in the same transaction as the decision. A rejection must
   * carry a reason the requester can act on — the server refuses one without.
   */
  decide(id: string, request: DecideAccessRequestRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/decide`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Convenience over `decide`, for a screen with two buttons rather than a form.
   *
   * The server calls the field `notes` for both outcomes, and requires it on a rejection: a
   * refusal the requester cannot act on is a dead end rather than a decision.
   */
  approve(id: string, expectedVersion: number, notes?: string): Observable<OutcomeResponse> {
    return this.decide(id, { approved: true, expectedVersion, notes });
  }

  reject(id: string, expectedVersion: number, reason: string): Observable<OutcomeResponse> {
    return this.decide(id, { approved: false, expectedVersion, notes: reason });
  }

  /**
   * Sends a request back to the requester for more information.
   *
   * NOT A REJECTION. A rejection is a decision; a return says the approver cannot answer yet,
   * almost always because the justification does not explain what the access is for. The request
   * keeps its number and its history and goes back to be improved.
   */
  returnForInformation(id: string, request: ReturnAccessRequestRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/return`, request)
      .pipe(map((response) => response.data!));
  }

  /** The requester withdrawing their own request before anybody has decided on it. */
  withdraw(id: string, request: WithdrawAccessRequestRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/withdraw`, request)
      .pipe(map((response) => response.data!));
  }

  // ---- Aliases for the vocabulary the screens use -----------------------------------------
  //
  // The screen speaks of requests; this service speaks of the resource. Both are reasonable, and
  // a handful of one-line aliases is cheaper than renaming every call site — and far cheaper
  // than two services that do the same thing.

  getRequests = (filter: AccessRequestSearchFilter) => this.search(filter);
  createRequest = (request: CreateAccessRequestRequest) => this.create(request);
  submitRequest = (id: string, request: SubmitAccessRequestRequest) => this.submit(id, request);
  approveRequest = (id: string, expectedVersion: number, notes?: string) =>
    this.approve(id, expectedVersion, notes);
  rejectRequest = (id: string, expectedVersion: number, reason: string) =>
    this.reject(id, expectedVersion, reason);
  returnRequest = (id: string, request: ReturnAccessRequestRequest) =>
    this.returnForInformation(id, request);
  cancelRequest = (id: string, request: WithdrawAccessRequestRequest) => this.withdraw(id, request);

  /**
   * Deleting a draft.
   *
   * Withdrawal is the same operation from the server's point of view: a draft nobody has decided
   * on is simply withdrawn, keeping its number and its trail. Nothing in this system deletes a
   * governance record, because "there was a request and it went away" is not something an audit
   * should ever have to reconstruct.
   */
  deleteDraft = (id: string, request: WithdrawAccessRequestRequest) => this.withdraw(id, request);

  private toParams(filter: AccessRequestSearchFilter): HttpParams {
    let params = new HttpParams();

    Object.entries(filter as Record<string, unknown>).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}

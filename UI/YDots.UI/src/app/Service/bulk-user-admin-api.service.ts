import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  ApplyBulkOperationRequest,
  BulkOperationDetailResponse,
  BulkOperationListItemResponse,
  CreateBulkOperationRequest,
} from '../Shared/models/iam-contract.model';

/**
 * Bulk administration: doing one thing to many people at once.
 *
 * THE TWO-STEP SHAPE IS THE POINT
 * --------------------------------
 * Creating an operation VALIDATES the selection and reports what would happen, row by row,
 * without changing anything. Applying it then carries it out. Somebody suspending forty accounts
 * gets to see that three of them are already suspended and one is the platform administrator
 * BEFORE it happens, rather than discovering it in the audit trail afterwards.
 *
 * `applyImmediately` collapses the two for a caller that genuinely wants one step — an automated
 * import, say. A person driving a screen should not use it.
 *
 * PARTIAL SUCCESS IS A REAL RESULT, NOT A FAILURE. Forty-seven of fifty succeeding is exactly
 * what happened, and the three that did not are listed with their reasons. Rolling back the
 * forty-seven because of three would be worse: the work was valid and would have to be done
 * again.
 */
@Injectable({ providedIn: 'root' })
export class BulkUserAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/users/bulk-actions`;
  private readonly operationsUrl = `${environment.apiBaseUrl}/governance/bulk-operations`;

  /**
   * Validates a selection and reports what would happen, changing nothing.
   *
   * The response carries a row per person with its own outcome, which is what the preview screen
   * renders. Nothing has been done at this point.
   */
  createOperation(request: CreateBulkOperationRequest): Observable<BulkOperationDetailResponse> {
    return this.http
      .post<ApiResponse<BulkOperationDetailResponse>>(this.baseUrl, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Carries out an operation that was validated first.
   *
   * `expectedVersion` guards against two administrators applying the same batch twice, which
   * would otherwise send forty invitations to the same forty people.
   */
  apply(request: ApplyBulkOperationRequest): Observable<BulkOperationDetailResponse> {
    return this.http
      .post<ApiResponse<BulkOperationDetailResponse>>(`${this.baseUrl}/apply`, request)
      .pipe(map((response) => response.data!));
  }

  /** One operation with its per-row outcomes. Used for the preview and for the result. */
  getOperation(id: string): Observable<BulkOperationDetailResponse> {
    return this.http
      .get<ApiResponse<BulkOperationDetailResponse>>(`${this.baseUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** The operations this Organisation has run, newest first. */
  getOperations(page = 1, pageSize = 20): Observable<PagedResponse<BulkOperationListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<BulkOperationListItemResponse>>>(this.baseUrl, {
        params: new HttpParams().set('page', page).set('pageSize', pageSize),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * The full history, including operations somebody else ran.
   *
   * A different endpoint from the list above because it is a governance read rather than a
   * working list, and it is gated on the bulk-administration permission accordingly.
   */
  getHistory(page = 1, pageSize = 20): Observable<PagedResponse<BulkOperationListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<BulkOperationListItemResponse>>>(this.operationsUrl, {
        params: new HttpParams().set('page', page).set('pageSize', pageSize),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * Cancels an operation that has been validated but not applied.
   *
   * Once applied there is nothing to cancel: the changes are made, and undoing them means a
   * fresh operation in the other direction — which is honest about what actually happened rather
   * than pretending it can be rewound.
   */
  cancel(id: string, expectedVersion: number, reason?: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/cancel`, {
        expectedVersion,
        reason,
      })
      .pipe(map((response) => response.data!));
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, LookupItem, OutcomeResponse } from '../Shared/models/api-response.model';
import {
  CheckUserIdentityRequest,
  CheckUserIdentityResponse,
  CreateUserRequest,
  CreateUserResponse,
  EnumOption,
  EnumOptionsResponse,
  ReferenceDataResponse,
} from '../Shared/models/iam-contract.model';

/**
 * IAM-USR-01 — creating a user and sending their invitation.
 *
 * WHY THE FORM ASKS THE SERVER WHAT TO SHOW
 * -----------------------------------------
 * The dropdowns on this form are not lists of words, they are lists of identifiers. A role is a
 * GUID, not the string "Finance Officer". Hard-coding names in the UI produces a form that looks
 * right and cannot save, because the server has no idea which role "Finance Officer" meant.
 * `getFormReferenceData()` returns the exact options this caller may choose from, already scoped
 * to their Organisation and to what they are allowed to grant.
 *
 * THERE IS NO ORGANISATION PARAMETER ANYWHERE BELOW
 * --------------------------------------------------
 * Not on the reference data, not on the availability check, not on create. The Organisation comes
 * from the signed token. An id supplied by this client would let a form aimed at another
 * Organisation create a user inside it, which is precisely the boundary that must never be
 * client-controlled.
 */
@Injectable({ providedIn: 'root' })
export class UserAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly usersUrl = `${environment.apiBaseUrl}/users`;
  private readonly referenceUrl = `${environment.apiBaseUrl}/reference-data`;

  /**
   * Every dropdown the form needs, in ONE call.
   *
   * One call rather than six: a form that opens with six parallel requests has six chances to
   * render half-populated, and the component then has to sequence them.
   */
  getFormReferenceData(): Observable<ReferenceDataResponse> {
    return this.http
      .get<ApiResponse<ReferenceDataResponse>>(this.referenceUrl)
      .pipe(map((response) => response.data!));
  }

  /**
   * The enumerations the form renders as dropdowns — account categories, engagement types,
   * MFA requirements, and so on — with their display labels.
   *
   * Served from the API rather than duplicated in TypeScript, so adding a status to the domain
   * makes it appear in the UI without an Angular change.
   */
  getEnumOptions(): Observable<EnumOptionsResponse> {
    return this.http
      .get<ApiResponse<EnumOptionsResponse>>(`${this.referenceUrl}/enums`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Whether an e-mail address or username is free.
   *
   * Scoped to the caller's Organisation, which is the scope the uniqueness rule itself uses: the
   * same address may exist in another Organisation and that is not a clash. The answer never
   * names whoever holds a taken value.
   */
  checkIdentity(request: CheckUserIdentityRequest): Observable<CheckUserIdentityResponse> {
    return this.http
      .post<ApiResponse<CheckUserIdentityResponse>>(`${this.usersUrl}/check-identity`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Creates the user and, unless told otherwise, sends the invitation.
   *
   * `sendInvitation: false` leaves the account in Draft with no credentials and no way in, which
   * is what a staged import wants — the invitations go out later on a schedule.
   */
  createUser(request: CreateUserRequest): Observable<CreateUserResponse> {
    return this.http
      .post<ApiResponse<CreateUserResponse>>(this.usersUrl, request)
      .pipe(map((response) => response.data!));
  }

  /** Re-sends an invitation that lapsed or never arrived. */
  resendInvitation(userId: string, message?: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/${userId}/resend-invitation`, { userId, message })
      .pipe(map((response) => response.data!));
  }

  /**
   * Withdraws an invitation that has not been accepted.
   *
   * The link stops working immediately, which is the point: an invitation sent to the wrong
   * address is not fixed by ignoring it.
   */
  revokeInvitation(userId: string, reason: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/${userId}/revoke-invitation`, { reason })
      .pipe(map((response) => response.data!));
  }

  /** People who can be named as a manager, for the manager picker. */
  getManagerOptions(): Observable<LookupItem[]> {
    return this.getFormReferenceData().pipe(map((data) => data.managers ?? []));
  }

  /**
   * Finds one enumeration in the options payload.
   *
   * A small helper because the server returns them keyed by name, and every caller otherwise
   * writes the same `?? []` fallback — which is the one somebody eventually forgets, producing
   * a template that iterates undefined.
   */
  static optionsFor(source: EnumOptionsResponse | null, name: keyof EnumOptionsResponse): EnumOption[] {
    if (!source) {
      return [];
    }

    const value = source[name];
    return Array.isArray(value) ? (value as EnumOption[]) : [];
  }
}

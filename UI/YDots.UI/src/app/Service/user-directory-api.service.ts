import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, map, switchMap } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  EnumOption,
  EnumOptionsResponse,
  ReferenceDataResponse,
  UserAccessPreviewResponse,
  UserDetailResponse,
  UserListItemResponse,
  UserSecurityResponse,
} from '../Shared/models/iam-contract.model';
import {
  ReasonRequest,
  UpdateUserRequest,
  UserDirectoryResponse,
  UserSearchFilter,
} from '../Shared/models/user-directory.model';

/**
 * The user directory: searching, opening and administering the people in one Organisation.
 *
 * NO ORGANISATION IS EVER SENT. Not on the search, not on an action, not on the export. The
 * Organisation comes from the signed token, which is the whole reason a TEN001 administrator
 * cannot reach a TEN002 user however the request is shaped: there is no parameter to change.
 *
 * WHY THE DIRECTORY IS ASSEMBLED FROM TWO CALLS
 * ---------------------------------------------
 * The screen needs a page of people AND the options for its filters — statuses, departments,
 * roles. The API serves those separately, and rightly: the filter options change once a month
 * and the page changes on every keystroke. Fetching them together on the first load and the page
 * alone afterwards is what keeps typing in the search box from re-fetching the whole reference
 * set thirty times a minute.
 *
 * EVERY ACTION CARRIES ExpectedVersion. Two administrators on the same record means the second
 * one is told to reload rather than silently overwriting the first.
 */
/** One colleague, as the picker endpoint returns them. */
export interface PersonLookupResponse {
  id: string;
  displayName: string;
  code?: string | null;
}

@Injectable({ providedIn: 'root' })
export class UserDirectoryApiService {
  private readonly http = inject(HttpClient);
  private readonly usersUrl = `${environment.apiBaseUrl}/users`;
  private readonly referenceUrl = `${environment.apiBaseUrl}/reference-data`;

  /**
   * The directory: a page of people, plus everything the filter bar needs.
   *
   * The reference data comes along on every call rather than being cached here, because a
   * service that caches has to decide when to invalidate — and getting that wrong shows a
   * department that was deleted an hour ago. The payload is small and the request is cheap.
   */
  getDirectory(filter: UserSearchFilter): Observable<UserDirectoryResponse> {
    return forkJoin({
      users: this.searchUsers(filter),
      reference: this.http
        .get<ApiResponse<ReferenceDataResponse>>(this.referenceUrl)
        .pipe(map((response) => response.data!)),
      enums: this.http
        .get<ApiResponse<EnumOptionsResponse>>(`${this.referenceUrl}/enums`)
        .pipe(map((response) => response.data!)),
    }).pipe(
      map(({ users, reference, enums }) => ({
        screenId: 'IAM-USR-01',
        route: '/app/administration/access/user-directory',
        users,

        // The enum payloads are value/label; the directory model wants LookupItem, which is
        // id/code/name. Converted here, once, rather than at every binding in the template.
        statusOptions: this.toLookups(enums.userStatuses),
        invitationStatusOptions: this.toLookups(enums.userStatuses),
        accountCategoryOptions: this.toLookups(enums.accountCategories),
        dataScopeTypeOptions: this.toLookups(enums.dataScopeTypes),

        // These are the Organisation's own records and are already LookupItems.
        organisationUnitOptions: reference.organisationUnits ?? [],
        departmentOptions: reference.departments ?? [],
        roleOptions: reference.roles ?? [],

        permittedActions: [],
        activeFilterSummary: this.describeFilter(filter),
        dataScopeSummary: reference.currentTenantName ?? '',
        state: 'Ready',
      })),
    );
  }

  /**
   * The organisation's people, for a picker.
   *
   * A DIFFERENT ENDPOINT FROM searchUsers, and deliberately so. That one is the administration
   * directory and needs iam.users.view; this one needs only that you are a member. Every
   * "choose a person" control has to work for people who are not user administrators - a
   * Campaign Owner naming an owner, a Campaign Manager routing a lead - and pointing those
   * controls at the administration search is why the Owner box read "No eligible person in
   * scope" on the one screen whose whole purpose is to name an owner.
   */
  peopleDirectory(search?: string): Observable<PersonLookupResponse[]> {
    let params = new HttpParams().set('take', '200');

    if (search) {
      params = params.set('search', search);
    }

    return this.http
      .get<ApiResponse<PersonLookupResponse[]>>(`${this.usersUrl}/directory`, { params })
      .pipe(map((response) => response.data ?? []));
  }

  /** One page of people. Used on every search, without the reference data. */
  searchUsers(filter: UserSearchFilter): Observable<PagedResponse<UserListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<UserListItemResponse>>>(this.usersUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /** The full record behind one row, for the view and edit dialogs. */
  /**
   * Finds somebody by the reference people actually quote, and returns the full record.
   *
   * WHY THIS EXISTS. Several screens are reached by a URL carrying a human reference — the
   * USR-000184 an administrator reads off a ticket — while every API route is keyed by the
   * internal id. Resolving in one place means those screens do not each invent their own
   * lookup, and a reference that matches nothing produces one clear error instead of a page
   * that silently renders blank.
   *
   * The search is exact on the code. A prefix search would happily return the wrong person
   * when one reference is a prefix of another.
   */
  getUserByReference(reference: string): Observable<UserDetailResponse> {
    return this.searchUsers({ search: reference, pageSize: 5 } as UserSearchFilter).pipe(
      map((page) => {
        const match = (page.items ?? []).find(
          (user) => (user.code ?? '').toLowerCase() === reference.toLowerCase());

        if (!match?.id) {
          throw new Error(`No user matches the reference ${reference}.`);
        }

        return match.id;
      }),
      switchMap((id) => this.getUser(id)),
    );
  }

  getUser(id: string): Observable<UserDetailResponse> {
    return this.http
      .get<ApiResponse<UserDetailResponse>>(`${this.usersUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** Sessions, devices, factors and recent sign-in activity for one person. */
  /**
   * What one person can actually reach: roles, the permissions those roles carry, and the data
   * scopes that narrow them.
   *
   * SEPARATE FROM THE RECORD because it is a different question and a different permission. An
   * administrator may legitimately be able to read somebody's profile without being allowed to
   * see the full reach of their access.
   */
  getUserAccess(id: string): Observable<UserAccessPreviewResponse> {
    return this.http
      .get<ApiResponse<UserAccessPreviewResponse>>(`${this.usersUrl}/${id}/access`)
      .pipe(map((response) => response.data!));
  }

  getUserSecurity(id: string): Observable<UserSecurityResponse> {
    return this.http
      .get<ApiResponse<UserSecurityResponse>>(`${this.usersUrl}/${id}/security`)
      .pipe(map((response) => response.data!));
  }

  updateUser(id: string, request: UpdateUserRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.usersUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Suspends the account.
   *
   * Every live session is revoked server-side at the same moment, which is the part that
   * matters: an account that cannot sign in but whose existing session keeps working is not
   * suspended in any useful sense.
   */
  suspendUser(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'suspend', request);
  }

  /** Lifts a suspension. The person can sign in again straight away. */
  reactivateUser(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'reactivate', request);
  }

  /**
   * Deactivates permanently.
   *
   * Distinct from suspension: suspension is a pause, deactivation is the end of the account.
   * Neither deletes anything, because the person's history has to remain attributable.
   */
  deactivateUser(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'deactivate', request);
  }

  /** Withdraws an account that never activated, and the invitation with it. */
  withdrawUser(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'withdraw', request);
  }

  /** Clears a lockout early, when the person has been identified another way. */
  unlockUser(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'unlock', request);
  }

  /** Sends a password reset link. No password is ever generated or e-mailed. */
  resetPassword(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'reset-password', request);
  }

  /** Ends every session this person has, everywhere. For a lost device or a departure. */
  forceSignOut(id: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.action(id, 'force-sign-out', request);
  }

  /** Re-sends an invitation that lapsed or never arrived. */
  resendInvitation(id: string, message?: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(
        `${this.usersUrl}/${id}/resend-invitation`, { userId: id, message })
      .pipe(map((response) => response.data!));
  }

  /**
   * Withdraws an invitation that has not been accepted.
   *
   * The link stops working immediately — an invitation sent to the wrong address is not fixed by
   * ignoring it.
   */
  revokeInvitation(id: string, reason: string): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.usersUrl}/${id}/revoke-invitation`, { reason })
      .pipe(map((response) => response.data!));
  }

  /** Moves the end of somebody's access window, for a contract that was extended. */
  extendAccess(id: string, accessEndsAtUtc: string, expectedVersion: number, reason?: string):
    Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.usersUrl}/${id}/extend-access`, {
        accessEndsAtUtc,
        expectedVersion,
        reason,
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * Exports the directory to CSV.
   *
   * The same filter as the screen, so what is exported is what is on screen. The export is
   * audited server-side, filter included.
   */
  exportDirectory(filter: UserSearchFilter): Observable<Blob> {
    return this.http.get(`${this.usersUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  /** Sends a blob to the browser as a download, releasing the object URL afterwards. */
  saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = fileName;
    link.click();

    URL.revokeObjectURL(url);
  }

  /**
   * Every lifecycle action shares one shape: a reason and the version it expects.
   *
   * One helper rather than nine near-identical methods, so a change to the envelope is made once
   * and the nine cannot drift.
   */
  private action(id: string, verb: string, request: ReasonRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.usersUrl}/${id}/${verb}`, request)
      .pipe(map((response) => response.data!));
  }

  /** Enum options are value/label on the wire; the directory model speaks LookupItem. */
  private toLookups(options: EnumOption[] | null | undefined) {
    return (options ?? []).map((option) => ({
      id: option.value ?? '',
      code: option.value ?? '',
      name: option.label ?? option.value ?? '',
      isActive: true,
      description: null,
    }));
  }

  /** A one-line summary of what is being filtered, for the header of the results. */
  private describeFilter(filter: UserSearchFilter): string {
    const parts: string[] = [];

    if (filter.search) { parts.push(`matching "${filter.search}"`); }
    if (filter.status) { parts.push(String(filter.status).toLowerCase()); }
    if (filter.departmentId) { parts.push('in one department'); }
    if (filter.organisationUnitId) { parts.push('in one office'); }

    return parts.length > 0 ? `Showing people ${parts.join(', ')}.` : 'Showing everybody.';
  }

  /**
   * Only the filters that were actually set are sent.
   *
   * An empty `status=` is not the same as omitting it: the server would try to parse the empty
   * string as an enum and reject the whole request.
   */
  private toParams(filter: UserSearchFilter): HttpParams {
    let params = new HttpParams();

    Object.entries(filter as Record<string, unknown>).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        // The screen calls it pageIndex; the API calls it page. Renamed here rather than in the
        // component, so the component keeps one vocabulary.
        params = params.set(key === 'pageIndex' ? 'page' : key, String(value));
      }
    });

    return params;
  }
}

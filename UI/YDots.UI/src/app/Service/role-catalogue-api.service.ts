import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, forkJoin, map } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  ApiResponse,
  LookupItem,
  OutcomeResponse,
  PagedResponse,
} from '../Shared/models/api-response.model';
import {
  AssignRoleClaimsRequest,
  AssignRolePermissionsRequest,
  ChangeRoleStatusRequest,
  CreateRoleIncompatibilityRequest,
  CreateRoleRequest,
  DeleteRoleRequest,
  PermissionMatrixResponse,
  RoleDetailResponse,
  RoleIncompatibilityResponse,
  RoleListItemResponse,
  RoleLookupResponse,
  RoleMemberResponse,
  UpdateRoleRequest,
} from '../Shared/models/iam-contract.model';

/** The catalogue as the screen works from it: rows plus its filter vocabularies. */
export interface RoleCatalogueView {
  roles: RoleListItemResponse[];
  totalCount: number;
  statusOptions: LookupItem[];
  roleTypeOptions: LookupItem[];
}

/** Two roles and the difference between what they grant. */
export interface RoleComparison {
  left: RoleDetailResponse;
  right: RoleDetailResponse;
  onlyInLeft: string[];
  onlyInRight: string[];
  inBoth: string[];
}

/** What the role catalogue can be narrowed by. Mirrors the API's query parameters. */
export interface RoleSearchFilter {
  search?: string;
  status?: string;
  roleType?: string;
  privilegedOnly?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

/**
 * Roles and what they grant.
 *
 * ROLES ARE PER-ORGANISATION, so nothing here takes one as a parameter: the caller's token
 * decides. Two Organisations may both have a role coded ADMIN and neither can see the other's —
 * which is also why a role code is unique inside an Organisation and only inside it.
 *
 * PERMISSIONS ARE ASSIGNED AS A SET, NOT ONE AT A TIME. `assignPermissions` replaces the whole
 * list, which is what makes the screen's Save button mean what it appears to mean: what is ticked
 * is what the role ends up with. Adding and removing one at a time reads as simpler and produces
 * a role that is half-saved when somebody closes the tab.
 *
 * A PLATFORM-ONLY CODE IS REFUSED, not silently dropped. An administrator who was quietly given
 * less than they asked for would believe they had granted something they had not.
 */
@Injectable({ providedIn: 'root' })
export class RoleCatalogueApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/roles`;
  private readonly permissionsUrl = `${environment.apiBaseUrl}/permissions`;

  // =========================================================================================
  // Reading
  // =========================================================================================

  search(filter: RoleSearchFilter): Observable<PagedResponse<RoleListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<RoleListItemResponse>>>(this.baseUrl, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getRole(id: string): Observable<RoleDetailResponse> {
    return this.http
      .get<ApiResponse<RoleDetailResponse>>(`${this.baseUrl}/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** The roles that can be assigned, for a dropdown. Platform roles are excluded by the server. */
  getRoleLookup(): Observable<RoleLookupResponse[]> {
    return this.http
      .get<ApiResponse<RoleLookupResponse[]>>(`${this.baseUrl}/lookup`)
      .pipe(map((response) => response.data ?? []));
  }

  /** Who holds this role. Paged, because a default role holds everybody. */
  getMembers(id: string, page = 1, pageSize = 20): Observable<PagedResponse<RoleMemberResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<RoleMemberResponse>>>(`${this.baseUrl}/${id}/members`, {
        params: new HttpParams().set('page', page).set('pageSize', pageSize),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * The permission matrix, optionally showing what one role already holds.
   *
   * Grouped by module and group: a flat list of a hundred and thirty codes cannot be reasoned
   * about, and the point of the editor is to let somebody decide what a role should do.
   */
  getPermissionMatrix(roleId?: string): Observable<PermissionMatrixResponse> {
    return this.http
      .get<ApiResponse<PermissionMatrixResponse>>(`${this.permissionsUrl}/matrix`, {
        params: roleId ? new HttpParams().set('roleId', roleId) : undefined,
      })
      .pipe(map((response) => response.data!));
  }

  /** Exports the catalogue to CSV. Audited server-side, filter included. */
  export(filter: RoleSearchFilter): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  // =========================================================================================
  // Writing
  // =========================================================================================

  createRole(request: CreateRoleRequest): Observable<RoleDetailResponse> {
    return this.http
      .post<ApiResponse<RoleDetailResponse>>(this.baseUrl, request)
      .pipe(map((response) => response.data!));
  }

  updateRole(id: string, request: UpdateRoleRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Replaces the role's permission set.
   *
   * `deniedPermissionCodes` is the explicit-deny list, and deny beats allow: a code that appears
   * in both is denied. That is what makes it possible to give somebody a broad role and carve
   * one thing out of it, rather than building a near-duplicate role.
   */
  assignPermissions(id: string, request: AssignRolePermissionsRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/permissions`, request)
      .pipe(map((response) => response.data!));
  }

  assignClaims(id: string, request: AssignRoleClaimsRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/claims`, request)
      .pipe(map((response) => response.data!));
  }

  changeStatus(id: string, request: ChangeRoleStatusRequest): Observable<OutcomeResponse> {
    return this.http
      .post<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}/status`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * Deletes a role.
   *
   * Refused while anybody holds it, and refused outright for a system role. Deactivating is what
   * to use for a role with history attached — it disappears from the pickers and leaves every
   * past assignment intact and explicable.
   */
  deleteRole(id: string, request: DeleteRoleRequest): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/${id}`, { body: request })
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Segregation of duties
  // =========================================================================================

  /**
   * Records that two roles must not be held together.
   *
   * The classic case is raising a payment and approving one. `isBlocking` decides whether the
   * combination is refused outright or merely flagged for somebody to justify — both are useful,
   * and which applies is a policy decision rather than a technical one.
   */
  addIncompatibility(request: CreateRoleIncompatibilityRequest):
    Observable<RoleIncompatibilityResponse> {
    return this.http
      .post<ApiResponse<RoleIncompatibilityResponse>>(`${this.baseUrl}/incompatibilities`, request)
      .pipe(map((response) => response.data!));
  }

  removeIncompatibility(incompatibilityId: string): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.baseUrl}/incompatibilities/${incompatibilityId}`)
      .pipe(map((response) => response.data!));
  }

  // ---- The vocabulary the role screen uses ---------------------------------------------------

  /**
   * The catalogue, shaped for the screen.
   *
   * The screen wants rows plus its filter vocabularies; the API serves the rows. The
   * vocabularies are the domain's own — three statuses and three role types — so they are named
   * here rather than fetched from an endpoint whose whole payload would be six words.
   */
  getCatalogue(filter: RoleSearchFilter): Observable<RoleCatalogueView> {
    return this.search(filter).pipe(
      map((page) => ({
        roles: page.items ?? [],
        totalCount: page.totalCount ?? 0,
        statusOptions: RoleCatalogueApiService.STATUS_OPTIONS,
        roleTypeOptions: RoleCatalogueApiService.ROLE_TYPE_OPTIONS,
      })),
    );
  }

  /**
   * Compares two roles' permissions.
   *
   * Done here rather than server-side because it is set arithmetic over two records the client
   * can already read, and an endpoint for it would be a round trip to compute something both
   * sides already have. Fetching both and diffing is one call fewer and one contract fewer.
   */
  compareRoles(leftRoleId: string, rightRoleId: string): Observable<RoleComparison> {
    return forkJoin({
      left: this.getRole(leftRoleId),
      right: this.getRole(rightRoleId),
    }).pipe(
      map(({ left, right }) => {
        const codesOf = (role: RoleDetailResponse) =>
          new Set((role.permissions ?? [])
            .filter((permission) => permission.isDenied !== true)
            .map((permission) => permission.permissionCode ?? '')
            .filter(Boolean));

        const leftCodes = codesOf(left);
        const rightCodes = codesOf(right);

        return {
          left,
          right,
          onlyInLeft: [...leftCodes].filter((code) => !rightCodes.has(code)).sort(),
          onlyInRight: [...rightCodes].filter((code) => !leftCodes.has(code)).sort(),
          inBoth: [...leftCodes].filter((code) => rightCodes.has(code)).sort(),
        };
      }),
    );
  }

  /**
   * Activating a role.
   *
   * There is no separate approval workflow for a role: a role IS its permissions, and the
   * approval that matters is the one on the permissions themselves. Draft and active are the
   * two states that mean anything — draft is being built, active is in use.
   */
  submitRole = (id: string, expectedVersion: number, reason = 'Ready for use.') =>
    this.changeStatus(id, { status: 'active', expectedVersion, reason });

  /**
   * Retiring a role.
   *
   * Inactive rather than deleted, whenever anybody has ever held it: the assignment history has
   * to stay explicable, and a dangling role id in an audit row explains nothing.
   */
  retireRole = (id: string, expectedVersion: number, reason: string) =>
    this.changeStatus(id, { status: 'inactive', expectedVersion, reason });

  /** Removing a draft nobody has ever held. */
  deleteDraftRole = (id: string, expectedVersion: number, reason: string) =>
    this.deleteRole(id, { expectedVersion, reason });

  /**
   * The three states a role can be in.
   *
   * Named here because they are the domain's own vocabulary — a new one would mean new server
   * behaviour rather than a new row in a table.
   */
  private static readonly STATUS_OPTIONS: LookupItem[] = [
    { id: 'draft', code: 'draft', name: 'Draft', isActive: true },
    { id: 'active', code: 'active', name: 'In use', isActive: true },
    { id: 'inactive', code: 'inactive', name: 'Retired', isActive: true },
  ];

  private static readonly ROLE_TYPE_OPTIONS: LookupItem[] = [
    { id: 'platform', code: 'platform', name: 'Platform', isActive: true },
    { id: 'tenant', code: 'tenant', name: 'Organisation', isActive: true },
    { id: 'custom', code: 'custom', name: 'Custom', isActive: true },
  ];

  private toParams(filter: RoleSearchFilter): HttpParams {
    let params = new HttpParams();

    Object.entries(filter as Record<string, unknown>).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}

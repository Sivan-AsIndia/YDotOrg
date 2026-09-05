import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse, OutcomeResponse, PagedResponse } from '../Shared/models/api-response.model';
import {
  AuditEventResponse,
  ConfigureTenantMenuRequest,
  CreateDepartmentRequest,
  CreateMenuDefinitionRequest,
  CreateOrganisationUnitRequest,
  DeleteStructureRequest,
  DepartmentResponse,
  MapRoleMenusRequest,
  MenuDefinitionResponse,
  MenuNode,
  OrganisationUnitResponse,
  PermissionListItemResponse,
  PermissionMatrixResponse,
  RoleDetailResponse,
  RoleLookupResponse,
  RoleMenuMappingResponse,
  TenantMenuConfigurationResponse,
  UpdateDepartmentRequest,
  UpdateMenuDefinitionRequest,
  UpdateOrganisationUnitRequest,
} from '../Shared/models/iam-contract.model';

/** What the audit trail can be narrowed by. Mirrors the API's query parameters. */
export interface AuditSearchFilter {
  search?: string;
  actionCode?: string;
  targetType?: string;
  actorUserId?: string;
  result?: string;
  fromUtc?: string;
  toUtc?: string;
  page?: number;
  pageSize?: number;
}

/**
 * The administration screens the other services do not cover: navigation, the permission
 * catalogue, the structural masters and the audit trail.
 *
 * NONE OF THESE TAKES AN ORGANISATION. Every one is scoped by the signed token — the navigation
 * a person may configure, the departments they may edit, the audit trail they may read are all
 * their own Organisation's. The single exception is the platform audit trail, which is a
 * different endpoint gated on global scope rather than the same endpoint with a wider parameter.
 */
@Injectable({ providedIn: 'root' })
export class IamAdminApiService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiBaseUrl;

  // =========================================================================================
  // Navigation
  // =========================================================================================

  /**
   * The full catalogue of navigation nodes the platform defines.
   *
   * Platform-only nodes come back only for a global-scope caller, so a TenantAdmin configuring
   * their own menu never learns those nodes exist.
   */
  getMenuCatalogue(includePlatformNodes = false): Observable<MenuNode[]> {
    return this.http
      .get<ApiResponse<MenuNode[]>>(`${this.base}/menus/catalogue`, {
        params: new HttpParams().set('includePlatformNodes', includePlatformNodes),
      })
      .pipe(map((response) => response.data ?? []));
  }

  /** What this Organisation has enabled, node by node. */
  getMenuConfiguration(): Observable<TenantMenuConfigurationResponse> {
    return this.http
      .get<ApiResponse<TenantMenuConfigurationResponse>>(`${this.base}/menus/configuration`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Turns navigation nodes on or off for this Organisation.
   *
   * Disabling a parent disables its children: a reachable child under a hidden parent is a hole,
   * not a convenience.
   */
  configureMenu(request: ConfigureTenantMenuRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.base}/menus/configuration`, request)
      .pipe(map((response) => response.data!));
  }

  getRoleMenuMapping(roleId: string): Observable<RoleMenuMappingResponse> {
    return this.http
      .get<ApiResponse<RoleMenuMappingResponse>>(`${this.base}/menus/role-mapping/${roleId}`)
      .pipe(map((response) => response.data!));
  }

  /**
   * Maps navigation nodes to a role.
   *
   * A node the Organisation has not enabled cannot be mapped — the mapping would be dead weight
   * and would confuse whoever read it next.
   */
  mapRoleMenus(roleId: string, request: MapRoleMenusRequest): Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.base}/menus/role-mapping/${roleId}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * The permission codes a catalogue node may be gated on.
   *
   * Not GET /permissions: that is gated on the TENANT permission iam.permissions.view, which a
   * SuperAdmin at platform level does not hold - so the picker rendered empty for the only
   * person who authors the catalogue.
   */
  getMenuPermissionCodes(): Observable<{ code: string; name: string; moduleCode: string }[]> {
    return this.http
      .get<ApiResponse<{ code: string; name: string; moduleCode: string }[]>>(
        `${this.base}/menus/definitions/permission-codes`)
      .pipe(map((response) => response.data ?? []));
  }

  /**
   * The catalogue in full, for the authoring screen.
   *
   * Not the same as getMenuCatalogue(): that returns what the SIDEBAR needs and carries no
   * version, status, description, parent id or flags, so nothing could be edited against it.
   */
  getMenuDefinitions(includeRetired = false): Observable<MenuDefinitionResponse[]> {
    return this.http
      .get<ApiResponse<MenuDefinitionResponse[]>>(`${this.base}/menus/definitions`, {
        params: new HttpParams().set('includeRetired', includeRetired),
      })
      .pipe(map((response) => response.data ?? []));
  }

  /**
   * Removes a node from the catalogue.
   *
   * Refused by the server the moment anything depends on it. Retiring through
   * updateMenuDefinition({ status: 'retired' }) is the usual answer.
   */
  deleteMenuDefinition(menuId: string, expectedVersion: number): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(`${this.base}/menus/definitions/${menuId}`, {
        params: new HttpParams().set('expectedVersion', expectedVersion),
      })
      .pipe(map((response) => response.data!));
  }

  /** Adds a node to the PLATFORM catalogue: a new product feature, for every Organisation. */
  createMenuDefinition(request: CreateMenuDefinitionRequest): Observable<MenuDefinitionResponse> {
    return this.http
      .post<ApiResponse<MenuDefinitionResponse>>(`${this.base}/menus/definitions`, request)
      .pipe(map((response) => response.data!));
  }

  updateMenuDefinition(menuId: string, request: UpdateMenuDefinitionRequest):
    Observable<OutcomeResponse> {
    return this.http
      .put<ApiResponse<OutcomeResponse>>(`${this.base}/menus/definitions/${menuId}`, request)
      .pipe(map((response) => response.data!));
  }

  /**
   * The roles in this Organisation, for a dropdown.
   *
   * Platform roles are excluded by the server: SUPER_ADMIN belongs to nobody's Organisation and
   * cannot be mapped, so offering it here would only produce a refusal.
   */
  getRoleLookup(): Observable<RoleLookupResponse[]> {
    return this.http
      .get<ApiResponse<RoleLookupResponse[]>>(`${this.base}/roles/lookup`)
      .pipe(map((response) => response.data ?? []));
  }

  /** One role, when the menu mapping needs its version to save against. */
  getRole(roleId: string): Observable<RoleDetailResponse> {
    return this.http
      .get<ApiResponse<RoleDetailResponse>>(`${this.base}/roles/${roleId}`)
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Permission catalogue
  // =========================================================================================

  searchPermissions(filter: { search?: string; module?: string; page?: number; pageSize?: number }):
    Observable<PagedResponse<PermissionListItemResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<PermissionListItemResponse>>>(`${this.base}/permissions`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  /**
   * The permission matrix the role editor renders.
   *
   * Grouped by module and group, because a flat list of a hundred and thirty codes is unusable
   * and the point of the screen is to let somebody reason about what a role can do.
   */
  getPermissionMatrix(roleId?: string): Observable<PermissionMatrixResponse> {
    return this.http
      .get<ApiResponse<PermissionMatrixResponse>>(`${this.base}/permissions/matrix`, {
        params: roleId ? new HttpParams().set('roleId', roleId) : undefined,
      })
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Departments and units
  // =========================================================================================

  getDepartments(): Observable<DepartmentResponse[]> {
    return this.http
      .get<ApiResponse<DepartmentResponse[]>>(`${this.base}/organisations/mine/departments`)
      .pipe(map((response) => response.data ?? []));
  }

  createDepartment(request: CreateDepartmentRequest): Observable<DepartmentResponse> {
    return this.http
      .post<ApiResponse<DepartmentResponse>>(`${this.base}/organisations/mine/departments`, request)
      .pipe(map((response) => response.data!));
  }

  updateDepartment(id: string, request: UpdateDepartmentRequest): Observable<DepartmentResponse> {
    return this.http
      .put<ApiResponse<DepartmentResponse>>(
        `${this.base}/organisations/mine/departments/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  /** Refused while anybody is still in it, or while another department sits under it. */
  deleteDepartment(id: string, request: DeleteStructureRequest): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(
        `${this.base}/organisations/mine/departments/${id}`, { body: request })
      .pipe(map((response) => response.data!));
  }

  getUnits(): Observable<OrganisationUnitResponse[]> {
    return this.http
      .get<ApiResponse<OrganisationUnitResponse[]>>(`${this.base}/organisations/mine/units`)
      .pipe(map((response) => response.data ?? []));
  }

  createUnit(request: CreateOrganisationUnitRequest): Observable<OrganisationUnitResponse> {
    return this.http
      .post<ApiResponse<OrganisationUnitResponse>>(`${this.base}/organisations/mine/units`, request)
      .pipe(map((response) => response.data!));
  }

  updateUnit(id: string, request: UpdateOrganisationUnitRequest): Observable<OrganisationUnitResponse> {
    return this.http
      .put<ApiResponse<OrganisationUnitResponse>>(
        `${this.base}/organisations/mine/units/${id}`, request)
      .pipe(map((response) => response.data!));
  }

  deleteUnit(id: string, request: DeleteStructureRequest): Observable<OutcomeResponse> {
    return this.http
      .delete<ApiResponse<OutcomeResponse>>(
        `${this.base}/organisations/mine/units/${id}`, { body: request })
      .pipe(map((response) => response.data!));
  }

  // =========================================================================================
  // Audit trail
  // =========================================================================================

  /**
   * Searches the trail.
   *
   * Organisation-scoped like everything else. What comes back is graded by permission: without
   * `iam.audit.view-sensitive` the before/after payloads are withheld and only the envelope is
   * returned. Knowing a colleague's password was reset is routine; seeing the contents is not.
   */
  /**
   * The record types this Organisation's trail actually holds, for the filter dropdown.
   *
   * The screen used to carry its own list of eleven entity names. See the note on the endpoint:
   * a hardcoded list can only be wrong in two directions, and is silent in both.
   */
  getAuditTargetTypes(): Observable<string[]> {
    return this.http
      .get<ApiResponse<string[]>>(`${this.base}/audit-events/target-types`)
      .pipe(map((response) => response.data ?? []));
  }

  searchAuditEvents(filter: AuditSearchFilter): Observable<PagedResponse<AuditEventResponse>> {
    return this.http
      .get<ApiResponse<PagedResponse<AuditEventResponse>>>(`${this.base}/audit-events`, {
        params: this.toParams(filter),
      })
      .pipe(map((response) => response.data!));
  }

  getAuditEvent(id: string): Observable<AuditEventResponse> {
    return this.http
      .get<ApiResponse<AuditEventResponse>>(`${this.base}/audit-events/${id}`)
      .pipe(map((response) => response.data!));
  }

  /** The recent history of one record — what the Activity tab on a user or role shows. */
  getAuditTrail(targetType: string, targetId: string, take = 20): Observable<AuditEventResponse[]> {
    return this.http
      .get<ApiResponse<AuditEventResponse[]>>(
        `${this.base}/audit-events/trail/${targetType}/${targetId}`,
        { params: new HttpParams().set('take', take) })
      .pipe(map((response) => response.data ?? []));
  }

  /**
   * Exports the trail to CSV.
   *
   * The export is itself audited, including the filter used — an unusual export is exactly the
   * kind of thing a later investigation needs to see.
   */
  exportAuditEvents(filter: AuditSearchFilter): Observable<Blob> {
    return this.http.get(`${this.base}/audit-events/export`, {
      params: this.toParams(filter),
      responseType: 'blob',
    });
  }

  /**
   * Sends a blob to the browser as a download.
   *
   * The object URL is revoked afterwards; without that, every export holds its bytes in memory
   * until the tab is closed, which on a long administrative session adds up.
   */
  saveBlob(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = fileName;
    link.click();

    URL.revokeObjectURL(url);
  }

  /**
   * Only the filters that were actually set are sent.
   *
   * An empty `result=` on the query string is not the same as omitting it: the server would try
   * to parse the empty string as an enum and reject the whole request.
   */
  private toParams(filter: object): HttpParams {
    let params = new HttpParams();

    Object.entries(filter as Record<string, unknown>).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    });

    return params;
  }
}

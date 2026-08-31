import {
  CreateRoleRequest as ApiCreateRoleRequest,
  UpdateRoleRequest as ApiUpdateRoleRequest,
  RoleClaimResponse,
  RoleDetailResponse,
  RoleIncompatibilityResponse,
  RoleListItemResponse,
  RolePermissionResponse,
} from './iam-contract.model';
import { LookupItem } from './api-response.model';

/** One row of the role catalogue. An alias of the generated contract. */
export type RoleListItem = RoleListItemResponse;

/** The full role record. An alias of the generated contract. */
export type RoleDetail = RoleDetailResponse;

/** One permission inside a role. An alias of the generated contract. */
export type RolePermission = RolePermissionResponse;

/** One claim inside a role. An alias of the generated contract. */
export type RoleClaim = RoleClaimResponse;

/** One segregation-of-duties rule. An alias of the generated contract. */
export type RoleIncompatibility = RoleIncompatibilityResponse;

/** Creating a role. An alias of the generated contract. */
export type CreateRoleRequest = ApiCreateRoleRequest;

/** Updating a role. An alias of the generated contract. */
export type UpdateRoleRequest = ApiUpdateRoleRequest;

/**
 * The catalogue as the screen works from it.
 *
 * ASSEMBLED ON THIS SIDE. The API serves rows; the filter vocabularies are the domain's own and
 * are named in the service. Declaring a server response that returns all of it would be
 * describing an endpoint that does not exist.
 */
export interface RoleCatalogueResponse {
  roles: RoleListItem[];
  totalCount: number;
  statusOptions: LookupItem[];
  roleTypeOptions: LookupItem[];
}

/** Query string for the role catalogue endpoint. Mirrors the API's parameters exactly. */
export interface RoleSearchFilter {
  search?: string;
  status?: string;
  roleType?: string;
  privilegedOnly?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

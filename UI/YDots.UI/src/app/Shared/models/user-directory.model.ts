import { LookupItem, PagedResponse } from './api-response.model';
import {
  ReasonRequest as ApiReasonRequest,
  UpdateUserRequest as ApiUpdateUserRequest,
  UserDetailResponse,
  UserListItemResponse,
} from './iam-contract.model';

/**
 * One row of the user directory, exactly as `GET /api/v1/user-directory` returns it.
 *
 * These names match the WebAPI's `UserListItemResponse` field for field. The screen used to read
 * a hand-written JSON file with an entirely different shape (`reference`, `loginEmail`,
 * `accountStatus`, and a `password` field that had no business existing in a list payload), which
 * is why nothing the user did in the directory ever reached the server. Matching the contract is
 * what makes the two projects one system.
 */
/**
 * One row of the user directory.
 *
 * AN ALIAS OF THE GENERATED CONTRACT, not a copy. This used to be a hand-written interface, and
 * the trouble with a copy is that it drifts silently: the server renames a field, TypeScript is
 * perfectly happy with the old name, and the binding reads `undefined` for ever. Pointing at the
 * generated type means the same rename stops the build.
 */
export type UserListItem = UserListItemResponse;

/** The full record behind one row. Also an alias — same reasoning as above. */
export type UserDetail = UserDetailResponse;

/** The whole directory payload: rows plus every filter option, in one call. */
export interface UserDirectoryResponse {
  screenId: string;
  route: string;
  users: PagedResponse<UserListItem>;
  statusOptions: LookupItem[];
  invitationStatusOptions: LookupItem[];
  accountCategoryOptions: LookupItem[];
  organisationUnitOptions: LookupItem[];
  departmentOptions: LookupItem[];
  roleOptions: LookupItem[];
  dataScopeTypeOptions: LookupItem[];
  /** What this caller is allowed to do here, decided by their permissions on the server. */
  permittedActions: string[];
  activeFilterSummary: string;
  dataScopeSummary: string;
  state: string;
}

/** Query string for the directory endpoint. Every field is optional. */
export interface UserSearchFilter {
  search?: string;
  status?: string;
  invitationStatus?: string;
  accountCategory?: string;
  organisationUnitId?: string;
  departmentId?: string;
  roleId?: string;
  mfaEnrolled?: boolean;
  riskFlag?: string;
  pageIndex?: number;
  pageSize?: number;
  /**
   * Server-side sort expression. The API exposes a single `sort` string, not the
   * `sortBy` / `sortDescending` pair this once declared — those were sent as query parameters
   * and silently ignored, so the list never actually sorted.
   */
  sort?: string;
}

export interface UserRoleAssignment {
  id: string;
  roleId: string;
  roleCode: string;
  roleName: string;
  privilegeLevel: string;
  isPrimary: boolean;
  status: string;
  assignedAtUtc: string;
  effectiveFromUtc: string;
  effectiveToUtc: string | null;
}

export interface UserDataScope {
  id: string;
  scopeType: string;
  scopeValue: string;
  scopeDisplayName: string | null;
  roleId: string | null;
}

/** Body for a lifecycle action: suspend, reactivate, deactivate, withdraw, delete. */
/**
 * What every lifecycle action sends: why, and the version it expects.
 *
 * `expectedVersion` is not optional in practice even though the server tolerates its absence —
 * without it, two administrators acting on the same person means the second silently undoes the
 * first, and neither finds out.
 */
export type ReasonRequest = ApiReasonRequest;


/**
 * Body for saving profile changes from the edit dialog.
 *
 * Note what is absent: e-mail, username, role and data scope. Each of those has its own flow with
 * its own approval, because changing someone's role is a very different act from correcting the
 * spelling of their surname, and the two should not share one Save button.
 */
/** Editing a user. An alias of the generated contract. */
export type UpdateUserRequest = ApiUpdateUserRequest;

/**
 * Editing MY OWN profile — `PUT /api/v1/my-profile`.
 *
 * DELIBERATELY NARROWER THAN {@link UpdateUserRequest}, and the missing fields are the point.
 * An administrator may move somebody's department, manager, account category, access window and
 * MFA requirement; a person editing their own record may not, because each of those is a
 * statement about their place in the organisation rather than about them. The server's request
 * record carries the same five fields, so a field that is not here cannot be reached at all.
 */
export interface UpdateMyProfileRequest {
  expectedVersion: number;
  displayName?: string | null;
  mobileCountryCode?: string | null;
  mobileNumber?: string | null;
  designation?: string | null;
  preferredCulture?: string | null;
  timeZone?: string | null;
  reason?: string | null;
}

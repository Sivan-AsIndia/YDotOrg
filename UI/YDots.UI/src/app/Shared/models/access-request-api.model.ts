import {
  AccessRequestListItemResponse,
  CreateAccessRequestRequest as ApiCreateAccessRequestRequest,
} from './iam-contract.model';
import { LookupItem, PagedResponse } from './api-response.model';

/**
 * One access request row, exactly as `GET /api/v1/access-requests` returns it.
 *
 * Field names mirror the WebAPI's `AccessRequestResponse` record exactly.
 */
/**
 * One row of the access-request queue.
 *
 * An alias of the generated contract rather than a copy — see the note on `UserListItem` for
 * why every one of these is an alias now.
 */
export type AccessRequestItemApi = AccessRequestListItemResponse;

/** The whole access request list payload: rows plus every filter option, in one call. */
/**
 * What the access-request screen works from.
 *
 * ASSEMBLED ON THIS SIDE, not returned by one endpoint. The API serves a page of requests; the
 * filter vocabularies are the domain's own and are named in the component. Pretending one call
 * returns all of it would mean inventing a server response that does not exist — which is
 * exactly the drift this file used to contain.
 */
export interface AccessRequestListResponse {
  requests: AccessRequestItemApi[];
  totalCount: number;
  statusOptions: LookupItem[];
  requestTypeOptions: LookupItem[];
  scopeTypeOptions: LookupItem[];
  roleOptions: LookupItem[];
}

/** Query string for the access request endpoint. Every field is optional. */
export interface AccessRequestSearchFilter {
  search?: string;
  status?: string;
  requestType?: string;
  requestedForUserId?: string;
  requestedByUserId?: string;
  roleId?: string;
  approverUserId?: string;
  slaOverdue?: boolean;
  pageIndex?: number;
  pageSize?: number;
  sort?: string;
}

/** Body for creating an access request. */
/** Raising a request. An alias of the generated contract. */
export type CreateAccessRequestRequest = ApiCreateAccessRequestRequest;

/** Body for updating a draft access request. */
export interface UpdateAccessRequestRequest {
  roleId?: string | null;
  scopeType?: string | null;
  scopeValue?: string | null;
  accessStartsAtUtc: string;
  accessEndsAtUtc?: string | null;
  reviewAtUtc?: string | null;
  businessJustification: string;
  supportingDocumentReference?: string | null;
  approverUserId?: string | null;
  expectedVersion: number;
}
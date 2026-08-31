import {
  AccessReviewCampaignResponse,
  AccessReviewListItemResponse,
} from './iam-contract.model';

import { LookupItem, PagedResponse } from './api-response.model';

/**
 * One review line in an access review campaign, exactly as
 * `GET /api/v1/access-reviews` returns it.
 *
 * Field names mirror the WebAPI's `AccessReviewResponse` record exactly.
 */
/** One row of the review queue. An alias of the generated contract. */
export type AccessReviewItem = AccessReviewListItemResponse;

/** One access review campaign with its completion progress. */
/** A review campaign. An alias of the generated contract. */
export type AccessReviewCampaign = AccessReviewCampaignResponse;

/** The whole access review payload: review lines plus campaigns and filter options. */
export interface AccessReviewCampaignViewResponse {
  screenId: string;
  route: string;
  reviews: PagedResponse<AccessReviewItem>;
  campaigns: AccessReviewCampaign[];
  decisionOptions: LookupItem[];
  statusOptions: LookupItem[];
  roleOptions: LookupItem[];
  privilegeOptions: LookupItem[];
  reviewerProgress: string;
  /** What this caller is allowed to do here, decided by their permissions on the server. */
  permittedActions: string[];
  state: string;
}

/** Query string for the access review endpoint. Every field is optional. */
export interface AccessReviewSearchFilter {
  campaignId?: string;
  reviewerUserId?: string;
  subjectUserId?: string;
  organisationUnitId?: string;
  roleId?: string;
  privilegeLevel?: string;
  riskFlag?: string;
  decision?: string;
  status?: string;
  overdue?: boolean;
  pageIndex?: number;
  pageSize?: number;
  sort?: string;
}

/** Body for certify or revoke a review line. */
export interface AccessReviewDecisionRequest {
  decision: string;
  /** Required on Modify and Revoke. 10 to 1000 characters. */
  decisionReason?: string | null;
  revisedAccessEndsAtUtc?: string | null;
  revisedScopeValue?: string | null;
  expectedVersion?: number;
}

/** Body for delegating a review to another reviewer. */
export interface DelegateAccessReviewRequest {
  delegateToUserId: string;
  reason: string;
}

/** Body for creating a review campaign. */
export interface CreateAccessReviewCampaignRequest {
  name: string;
  campaignOwnerUserId?: string | null;
  periodStartUtc: string;
  periodEndUtc: string;
  populationRule?: string | null;
  subjectUserIds: string[];
  reviewDueInDays: number;
}
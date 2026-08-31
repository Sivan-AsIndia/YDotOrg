import {
  BulkOperationDetailResponse,
  BulkOperationItemResponse as ApiBulkOperationItemResponse,
  CreateBulkOperationRequest,
} from './iam-contract.model';

/**
 * Models for Bulk User Administration (IAM-USR-06).
 *
 * Field names here mirror the API's request/response records exactly. They must: TypeScript checks
 * the shape you *declare*, not the JSON that actually arrives, so a misspelt property compiles
 * cleanly and then reads `undefined` for ever.
 */

/** The action types available for a bulk operation — mirrors `BulkActionType` enum in the API. */
export enum BulkActionType {
  SendInvitation = 'SendInvitation',
  ResendInvitation = 'ResendInvitation',
  WithdrawInvitation = 'WithdrawInvitation',
  Suspend = 'Suspend',
  Reactivate = 'Reactivate',
  Deactivate = 'Deactivate',
  RequirePasswordReset = 'RequirePasswordReset',
  RevokeSessions = 'RevokeSessions',
  AssignAccessReview = 'AssignAccessReview',
}

/** A display option for the action grid — mirrors the API's `LookupItem`. */
export interface BulkActionOption {
  value: string;
  label: string;
  description?: string | null;
}

/** A scope option (organisation, geography, campaign…) — mirrors the API's `LookupItem`. */
export interface BulkScopeOption {
  value: string;
  label: string;
  description?: string | null;
}

/** An access review campaign option for the conditional campaign selector. */
export interface BulkCampaignOption {
  value: string;
  label: string;
  description?: string | null;
}

/** The view response for the bulk-user-administration screen. */
/**
 * What the bulk screen works from.
 *
 * ASSEMBLED ON THIS SIDE. The available actions are the domain's own vocabulary — each one is a
 * code path on the server, not a configurable row — and the recent operations come from the
 * operations endpoint that owns them. Declaring a single server response for all of it would be
 * describing an endpoint that does not exist.
 */
export interface BulkUserAdministrationViewResponse {
  availableActions: BulkActionOption[];
}

/**
 * The body sent to validate-selection, preview-impact and submit.
 * Mirrors `BulkActionRequest` in the API.
 */
/** Creating an operation. An alias of the generated contract. */
export type BulkActionRequest = CreateBulkOperationRequest;

/** One row in the preview/result breakdown. */
/** One row of the per-person breakdown. An alias of the generated contract. */
export type BulkOperationItemResponse = ApiBulkOperationItemResponse;

/** The impact preview returned by validate-selection and preview-impact. */
/**
 * What a validated operation reports before it is applied.
 *
 * An alias of the generated contract: creating an operation IS the preview — it validates the
 * selection and reports what would happen, row by row, without changing anything.
 */
export type BulkImpactPreviewResponse = BulkOperationDetailResponse;

/** The full operation record returned after submit. */
/** The operation record. An alias of the generated contract. */
export type BulkOperationResponse = BulkOperationDetailResponse;

/** Reason body for cancel. */
export interface BulkCancelRequest {
  reason: string;
  expectedVersion?: number | null;
}
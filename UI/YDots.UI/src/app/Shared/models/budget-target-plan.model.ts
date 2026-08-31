/* ===== Budget and Target Plan interfaces (SCR-CAM-006) ===== */

/**
 * Lifecycle states for a single plan version (doc 03 §4.6.3, doc 00 lifecycle
 * rule). Exactly one version per plan reference may be `Approved` (the current
 * one counted toward totals). `Superseded` is set on the previously-approved
 * version when a newer revision is approved — it is preserved for history, never
 * deleted.
 */
export type ApprovalState = 'Draft' | 'Submitted' | 'Approved' | 'Superseded' | 'Rejected';

/**
 * Required UI states for SCR-CAM-006 (doc 03 §4.6.4). Each must be reachable and
 * testable. `conflict` is the record-changed-since-load case; `dependency-failure`
 * separates a failed external-finance step from the locally committed draft.
 */
export type UiState =
  | 'ready'
  | 'loading'
  | 'empty'
  | 'validation'
  | 'no-access'
  | 'duplicate'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

export type ActionMode = 'allocate' | 'revise' | 'submit' | 'approve';

/**
 * One immutable version of a plan under a stable plan reference. Revising a plan
 * appends a NEW version rather than mutating an existing one, so history is never
 * overwritten (doc 03 §4.6 purpose: "versioned … without double counting").
 */
export interface PlanVersion {
  /** 1-based version number; renders as `v{versionNumber}`. */
  readonly versionNumber: number;
  targetAmount: number;
  budgetAmount: number;
  budgetCategory: string;
  expectedVolume: number;
  assumptions: string;
  approvalState: ApprovalState;
  /** Session "user id" (CurrentUserService.reference) that submitted this version. */
  submittedByRef: string | null;
  /** Session "user id" that approved this version (segregation-of-duties audit). */
  approvedByRef: string | null;
  /** Effective time this version reached its current committed state. */
  effectiveTime: string | null;
  /** Server-derived; meaningful only for the current Approved version. */
  actualReconciledResult: string;
  /** Server-derived; meaningful only for the current Approved version. */
  variance: string;
}

/**
 * A plan, keyed by its stable `reference` (minted once at Allocate, never
 * regenerated). Carries the identifying dimensions plus the full version history.
 */
export interface PlanRecord {
  /** Stable plan reference — minted once, immutable thereafter. */
  readonly reference: string;
  campaign: string;
  /** Link to the shared campaign store (CampaignRecord.code). */
  campaignRef: string;
  planPeriod: string;
  targetDimension: string;
  owner: string;
  /** Session "user id" of the owner/creator. */
  ownerRef: string;
  /** Full version history; index order is creation order, latest = highest versionNumber. */
  versions: PlanVersion[];
}

/**
 * Flat, read-only view-model consumed by the SCR-CAM-006 table and the Campaign
 * Detail Budget/Targets tabs. Derived from a PlanRecord + a chosen version — the
 * table never sees the raw version array, which is how per-row rendering stays
 * unchanged while the store underneath is versioned.
 */
export interface PlanItem {
  reference: string;
  campaign: string;
  campaignRef: string;
  planPeriod: string;
  targetDimension: string;
  owner: string;
  ownerRef: string;
  targetAmount: number;
  budgetCategory: string;
  budgetAmount: number;
  expectedVolume: number;
  assumptions: string;
  /** Display label of the version this row represents, e.g. `v3`. */
  version: string;
  versionNumber: number;
  approvalState: ApprovalState;
  submittedByRef: string | null;
  actualReconciledResult: string;
  variance: string;
  effectiveTime: string | null;
  /** True when this reference has a distinct Approved version counted toward totals. */
  hasApprovedVersion: boolean;
}

/** Inputs a person supplies when allocating or revising a plan (the editable §4.6.2 fields). */
export interface PlanEditableFields {
  campaign: string;
  campaignRef: string;
  planPeriod: string;
  targetDimension: string;
  owner: string;
  ownerRef: string;
  targetAmount: number;
  budgetCategory: string;
  budgetAmount: number;
  expectedVolume: number;
  assumptions: string;
}

// ================= CAM-UI-07 Campaign readiness checklist shared models =================

/**
 * The six launch dependencies aggregated on CAM-UI-07 (doc 03 §4.7.2 status
 * fields). `budget` and `tracking` are DERIVED from the shared budget-plan
 * (page 6) and tracking-asset (page 4) stores; `content`, `template`,
 * `payment` and `consent` come from a clearly-labelled stub store standing in
 * for content / communication / payment modules that live outside these 8
 * pages.
 */
export type DependencyKey = 'content' | 'budget' | 'tracking' | 'payment' | 'template' | 'consent';

/**
 * A single dependency's derived readiness. `unknown` is reserved for a stubbed
 * dependency whose backing service is unreachable (drives the §4.7.4 Dependency
 * failure state) — it is never used for the locally computed budget/tracking
 * results, which is how the UI keeps the two apart.
 */
export type DependencyStatus = 'pass' | 'fail' | 'pending' | 'unknown';

/** One row of the aggregation, recomputed by Validate readiness (§4.7.3). */
export interface DependencyResult {
  readonly key: DependencyKey;
  readonly label: string;
  readonly status: DependencyStatus;
  readonly note: string;
  /** True = locally computed from a first-party store (budget/tracking); false = stubbed external domain. */
  readonly derived: boolean;
}

/**
 * Lifecycle of the readiness *record itself* (distinct from the campaign
 * lifecycle in §5.5). `Submitted` = awaiting Approve launch after Request
 * approval; `Approved` = launch approved and the campaign advanced.
 */
export type ReadinessRequestState = 'Draft' | 'Submitted' | 'Approved';

/** An Open-blocker entry (§4.7.2 Open blockers) tied to a specific unmet dependency and an owner. */
export interface Blocker {
  readonly id: string;
  /** A derived dependency key, or a manually-added check's id — blockers can be raised on either. */
  readonly dependencyKey: DependencyKey | string;
  readonly dependencyLabel: string;
  readonly owner: string;
  readonly ownerRef: string;
  readonly note: string;
  /** Session "user id" that raised the blocker. */
  readonly createdByRef: string;
  readonly createdAt: string;
}

/**
 * The readiness record for one campaign, keyed by the stable campaign
 * reference. Held in the shared CampaignReadinessStoreService so a request /
 * approval / blocker survives navigation and is visible to a concurrent
 * viewer (which is what makes the §4.7.4 Conflict state real).
 */
export interface CampaignReadinessRecord {
  readonly campaignRef: string;
  requestState: ReadinessRequestState;
  /** Session "user id" that submitted Request approval — used for the self-approval block on Approve launch. */
  requestedByRef: string | null;
  requestedByName: string | null;
  requestedAt: string | null;
  /** §4.7.2 Planned launch time; interpreted in the operating time zone. */
  plannedLaunchTime: string;
  /** §4.7.2 Owner (conditional selector) — the accountable owner reference. */
  ownerReference: string;
  blockers: Blocker[];
  approvedByRef: string | null;
  approvedByName: string | null;
  approvedAt: string | null;
  decisionReason: string | null;
  /** Concurrency version — increments on every mutation; detects "record changed after you opened it". */
  version: number;
}

/** Required UI states for CAM-UI-07 (doc 03 §4.7.4), each reachable and testable. */
export type CrcUiState =
  | 'loading'
  | 'ready'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** A scope-aware owner option for the §4.7.2 Owner selector. */
export interface ReadinessOwnerOption {
  readonly reference: string;
  readonly name: string;
  readonly context: string;
}

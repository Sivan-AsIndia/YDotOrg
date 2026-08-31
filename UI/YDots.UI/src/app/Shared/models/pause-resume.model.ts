/* ===== CAM-UI-08 — Pause, resume and close campaign models =====
 *
 * The lifecycle "Current state" is NOT modelled here — it is the shared
 * CampaignStatus (doc 03 §5.5) read/written through CampaignStoreService, the
 * same field pages 1, 3 and 7 use. This page holds no independent state copy,
 * no local role→permission map (permissions come from CurrentUserService) and
 * no manually-entered dependency counts (Open donation intents / Active
 * tracking assets / Financial exceptions are derived live from the page-5
 * attribution and page-4 tracking stores).
 */

import { CampaignStatus } from './campaign.model';

/** The page-local view/recovery states (doc 03 §4.8.4). */
export type ViewState =
  | 'loading'
  | 'ready'
  | 'empty'
  | 'validation'
  | 'duplicate'
  | 'no-access'
  | 'conflict'
  | 'dependency-failure'
  | 'success';

/** The lifecycle actions on this screen (doc 03 §4.8.3) plus Activate (Scheduled → Active),
 *  moved here from Campaign detail so every live-lifecycle operation runs on one screen. */
export type ActionId = 'activate' | 'pause' | 'resume' | 'request_close' | 'approve_close' | 'cancel_draft';

/** Effective permissions for this screen — resolved from the shared session, never a local map. */
export interface PauseResumePermissions {
  readonly view: boolean;
  readonly activate: boolean;
  readonly pause: boolean;
  readonly resume: boolean;
  readonly requestClose: boolean;
  readonly approveClose: boolean;
  readonly cancelDraft: boolean;
}

/** Static contract for one action (doc 03 §4.8.3), stated in canonical CampaignStatus terms. */
export interface ActionConfig {
  readonly id: ActionId;
  readonly label: string;
  readonly placement: 'primary' | 'danger';
  readonly permissionKey: keyof PauseResumePermissions;
  readonly permissionCode: string;
  /** Campaign lifecycle states this action is compatible with. */
  readonly allowedStates: readonly CampaignStatus[];
  readonly requiresReasonCategory: boolean;
  readonly requiresDetailedReason: boolean;
  readonly requiresCommunicationImpact: boolean;
  readonly requiresClosureSummary: boolean;
  readonly confirmVerb: string;
  /** Typed confirmation for irreversible / high-risk actions (doc 03 §4.8.6). */
  readonly typedConfirm: boolean;
  readonly description: string;
}

/** State of the close request for a campaign (doc 03 §4.8.3 — Request close creates a record; it
 *  does NOT itself close the campaign). Preserved as history, never deleted (doc 00 DELETE RULE). */
export type CloseRequestState = 'None' | 'Requested' | 'Approved' | 'Cancelled';

/** The close-request / lifecycle-decision record held in the shared close-request store.
 *  reasonCategory and detailedReason are CONFIDENTIAL — kept in record scope only and never
 *  rendered into a non-scoped surface (history timeline, tooltip, storage, log, export, error). */
export interface CloseRequestRecord {
  reference: string;
  requestState: CloseRequestState;
  requestedByRef: string | null;
  requestedByName: string | null;
  requestedAt: string | null;
  /** Confidential (doc 03 §4.8.2). */
  reasonCategory: string;
  /** Confidential (doc 03 §4.8.2). */
  detailedReason: string;
  communicationImpact: string;
  closureSummary: string;
  approvedByRef: string | null;
  approvedByName: string | null;
  approvedAt: string | null;
  decisionReason: string;
  /** Increments on every store mutation — detects "record changed after you opened it" (§4.8.4 Conflict). */
  version: number;
}

/** One visible lifecycle-history entry. Carries NO confidential reason text — only a neutral
 *  in-scope flag that a confidential reason exists on file (doc 03 §4.8.2 masking rigor). */
export interface LifecycleHistoryEntry {
  readonly id: string;
  readonly actorRef: string;
  readonly actorName: string;
  readonly action: string;
  readonly from: string;
  readonly to: string;
  readonly hasConfidentialReason: boolean;
  readonly timestamp: string;
}

/** Persistent success outcome (doc 03 §4.8.4 Success — never a toast alone). */
export interface Outcome {
  readonly reference: string;
  readonly state: string;
  readonly effectiveTime: string;
  readonly nextAction: string;
  readonly accountableOwner: string;
  readonly remainingDependency: string;
}

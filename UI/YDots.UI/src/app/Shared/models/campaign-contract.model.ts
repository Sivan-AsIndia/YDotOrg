/**
 * The typed contract for the Campaign service.
 *
 * IT MIRRORS THE SERVER'S DTOs FIELD FOR FIELD, and the field NAMES are copied rather than
 * improved. A name invented on this side is `undefined` at runtime, which is exactly the failure
 * this file exists to prevent.
 *
 * THREE CONVENTIONS RUN THROUGH THE MODULE.
 *
 * `permittedActions` IS THE SERVER'S ANSWER to what this caller may do next. On a campaign it
 * folds in something no permission code can express: SEGREGATION OF DUTIES. The person who
 * created or submitted a campaign cannot approve it, whatever permissions they hold, so a screen
 * that decided for itself would draw an Approve button that answers 409.
 *
 * `version` IS THE OPTIMISTIC-CONCURRENCY STAMP. Every state-changing call sends the version it
 * read back as `expectedVersion`; a stale one answers 409 rather than overwriting a colleague's
 * lifecycle change.
 *
 * DATES ARE STRINGS AND SO ARE DATE-ONLY VALUES. `startDate` is `yyyy-MM-dd` with no time and no
 * zone - a campaign starting on the 1st starts on the 1st wherever anybody reads it - while
 * `activeFrom` on a tracking asset is a full instant. The two are not interchangeable, and the
 * types say so.
 */

import { PagedResponse } from './api-response.model';

export type { PagedResponse };

// =============================================================================================
// Enumerations - the string values the server serialises
// =============================================================================================

export type CampaignStatus =
  | 'draft'
  | 'submitted'
  | 'approved'
  | 'scheduled'
  | 'active'
  | 'paused'
  | 'closing'
  | 'closed'
  | 'cancelled';

/**
 * Whether a campaign goes live by hand or on its start date.
 *
 * THE NAMES ARE THE SERVER'S ENUM NAMES, camel-cased - `LifecycleActivation.Auto` and
 * `.Manual`. This said `'automatic'`, which is not a member of that enum, so every campaign
 * created or edited with auto-activation was refused by System.Text.Json before any handler
 * or validator ran, and came back as the framework's generic "Some of the details are not
 * valid."
 */
export type LifecycleActivation = 'manual' | 'auto';

export type TrackingAssetType = 'qrCode' | 'shortLink' | 'utmLink' | 'posterCode' | 'smsLink';

export type TrackingAssetStatus = 'draft' | 'submitted' | 'approved' | 'active' | 'inactive';

export type ReadinessCheckStatus = 'pending' | 'passed' | 'failed';

export type ReadinessCheckCategory =
  | 'content'
  | 'compliance'
  | 'payment'
  | 'attribution'
  | 'communications'
  | 'operations';

export type CampaignLifecycleActionType =
  | 'submit'
  | 'approve'
  | 'activate'
  | 'pause'
  | 'resume'
  | 'requestClose'
  | 'approveClose';

export type CampaignLifecycleActionStatus = 'pending' | 'approved' | 'rejected' | 'applied';

export type AuditResult = 'succeeded' | 'failed' | 'denied';

/** A server-supplied enum option, so the client never hard-codes a list that can drift. */
export interface EnumOption {
  value: string;
  label: string;
  ordinal: number;
}

// =============================================================================================
// Campaigns
// =============================================================================================

export interface CreateCampaignRequest {
  name: string;
  code: string;
  purpose: string;
  fundOrProgramme: string;
  /** `yyyy-MM-dd`. A date, not an instant - see the file header. */
  startDate: string;
  endDate: string;
  /**
   * NO targetAmount OR budgetAmount. Target & Budget is on hold, no step collects either, and the
   * server no longer accepts them on this contract - a campaign is created with a target of 0
   * until the module returns.
   */
  currencyId: string;
  countryId: string;
  /** At least one. A campaign with no owner is one nobody is accountable for. */
  ownerIds: string[];
  stateId?: string | null;
  cityId?: string | null;
  zipCode?: string | null;
  lifecycleActivation?: LifecycleActivation;
  daysBeforeStart?: number;
  /** `HH:mm`. When the pre-launch reminder goes out. */
  reminderTime?: string;
  publicDescription?: string | null;
  termsAndNotice?: string | null;
  status?: CampaignStatus;
  channelIds?: string[] | null;
}

/**
 * Editing a campaign.
 *
 * `code` AND `status` ARE BOTH ABSENT, and neither is an oversight. The code appears in every
 * tracking URL ever generated for the campaign, so repointing it would break printed QR codes.
 * The status moves through the lifecycle endpoints, each of which has its own permission and its
 * own rules - allowing a PUT to set it would let somebody move a Draft straight to Closed and
 * skip every approval on the way.
 */
export interface UpdateCampaignRequest {
  expectedVersion: number;
  name: string;
  purpose: string;
  fundOrProgramme: string;
  startDate: string;
  endDate: string;
  /** Absent for the same reason as on create; an edit cannot touch a stored target. */
  currencyId: string;
  countryId: string;
  ownerIds: string[];
  stateId?: string | null;
  cityId?: string | null;
  zipCode?: string | null;
  lifecycleActivation?: LifecycleActivation;
  daysBeforeStart?: number;
  reminderTime?: string;
  publicDescription?: string | null;
  termsAndNotice?: string | null;
  channelIds?: string[] | null;
}

/** The body of every lifecycle transition. Which fields matter depends on the transition. */
export interface CampaignLifecycleRequest {
  expectedVersion: number;
  reasonCategory?: string | null;
  detailedReason?: string | null;
  /** What this does to donors mid-flight. Required on a pause: they may hold a live payment link. */
  communicationImpact?: string | null;
  closureSummary?: string | null;
}

export interface CampaignListItem {
  id: string;
  tenantId: string;
  code: string;
  name: string;
  fundOrProgramme: string;
  startDate: string;
  endDate: string;
  targetAmount: number;
  budgetAmount: number | null;
  currencyId: string;
  status: CampaignStatus;
  statusDescription: string;
  /** How far through its own window the campaign is. Null before it starts. */
  elapsedPercent: number | null;
  ownerCount: number;
  trackingAssetCount: number;
  /** Readiness checks still outstanding. Non-zero is why a launch button is refused. */
  outstandingCheckCount: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface CampaignLifecycleAction {
  id: string;
  actionType: CampaignLifecycleActionType;
  actionTypeDescription: string;
  actionStatus: CampaignLifecycleActionStatus;
  effectiveAtUtc: string;
  reasonCategory: string | null;
  detailedReason: string | null;
  communicationImpact: string | null;
  closureSummary: string | null;
  requestedByUserId: string | null;
  approvedByUserId: string | null;
  approvedAtUtc: string | null;
  createdAtUtc: string;
}

export interface CampaignDetail {
  id: string;
  tenantId: string;
  businessUnitId: string;
  code: string;
  name: string;
  purpose: string;
  fundOrProgramme: string;
  startDate: string;
  endDate: string;
  targetAmount: number;
  currencyId: string;
  budgetAmount: number | null;
  countryId: string;
  stateId: string | null;
  cityId: string | null;
  zipCode: string | null;
  lifecycleActivation: LifecycleActivation;
  daysBeforeStart: number;
  reminderTime: string;
  publicDescription: string | null;
  termsAndNotice: string | null;
  status: CampaignStatus;
  statusDescription: string;
  ownerIds: string[];
  channelIds: string[];
  /** Who submitted it. The server refuses to let the same person approve it. */
  submittedByUserId: string | null;
  submittedAtUtc: string | null;
  approvedByUserId: string | null;
  approvedAtUtc: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  /** A close request awaiting a second person. At most one may be open at a time. */
  pendingCloseRequest: CampaignLifecycleAction | null;
  permittedActions: string[];
}

export interface CampaignHistoryEntry {
  id: string;
  actionCode: string;
  actorUserId: string | null;
  targetType: string;
  targetId: string;
  result: AuditResult;
  reason: string | null;
  occurredAtUtc: string;
}

export interface CampaignStatistics {
  total: number;
  draft: number;
  submitted: number;
  approved: number;
  scheduled: number;
  active: number;
  paused: number;
  closing: number;
  closed: number;
  cancelled: number;
}

export interface CampaignSearchFilter {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  status?: CampaignStatus | null;
  currencyId?: string | null;
  countryId?: string | null;
  ownerId?: string | null;
  startsOnOrAfter?: string | null;
  endsOnOrBefore?: string | null;
  /** Campaigns live at this moment, which is narrower than status = active. */
  isRunningNow?: boolean | null;
}

export interface CampaignLookup {
  id: string;
  code: string;
  name: string;
  status: CampaignStatus;
}

// =============================================================================================
// Tracking assets
// =============================================================================================

export interface TrackingAssetCustomFieldRequest {
  fieldName: string;
  value: string;
  id?: string | null;
}

export interface TrackingAssetPlaceRequest {
  placeName: string;
  destination: string;
  id?: string | null;
  cityId?: string | null;
  stateId?: string | null;
  customFields?: TrackingAssetCustomFieldRequest[] | null;
}

export interface CreateTrackingAssetRequest {
  campaignId: string;
  assetType: TrackingAssetType;
  channelId: string;
  destination: string;
  sourceId: string;
  mediumId: string;
  /** A full instant, unlike a campaign's dates. A poster goes live at a time of day. */
  activeFrom: string;
  activeTo: string;
  contentTag?: string | null;
  status?: TrackingAssetStatus;
  places?: TrackingAssetPlaceRequest[] | null;
}

/** `campaignId` is absent: re-parenting an asset would re-attribute every gift it ever produced. */
export interface UpdateTrackingAssetRequest {
  expectedVersion: number;
  assetType: TrackingAssetType;
  channelId: string;
  destination: string;
  sourceId: string;
  mediumId: string;
  activeFrom: string;
  activeTo: string;
  contentTag?: string | null;
  places?: TrackingAssetPlaceRequest[] | null;
}

export interface TrackingAssetLifecycleRequest {
  expectedVersion: number;
  reason?: string | null;
}

export interface TrackingAssetListItem {
  id: string;
  tenantId: string;
  code: string;
  /** What a donor's link actually carries. Null until the asset is approved and generated. */
  trackingReference: string | null;
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  assetType: TrackingAssetType;
  channelId: string;
  channelName: string;
  destination: string;
  sourceId: string;
  sourceName: string;
  mediumId: string;
  mediumName: string;
  contentTag: string | null;
  status: TrackingAssetStatus;
  statusDescription: string;
  activeFrom: string;
  activeTo: string;
  /** Approved AND inside its window. A poster whose run has ended is not live. */
  isLive: boolean;
  usageCount: number;
  totalReceived: number;
  placeCount: number;
  updatedAtUtc: string | null;
  version: number;
}

export interface TrackingAssetCustomField {
  id: string;
  fieldName: string;
  value: string;
}

export interface TrackingAssetPlace {
  id: string;
  placeName: string;
  cityId: string | null;
  stateId: string | null;
  destination: string;
  customFields: TrackingAssetCustomField[];
}

export interface TrackingAssetDetail {
  id: string;
  tenantId: string;
  businessUnitId: string;
  code: string;
  trackingReference: string | null;
  /** The full URL that goes into the QR code. Generated by the server, never composed here. */
  generatedUrl: string | null;
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  assetType: TrackingAssetType;
  channelId: string;
  channelName: string;
  destination: string;
  sourceId: string;
  sourceName: string;
  mediumId: string;
  mediumName: string;
  contentTag: string | null;
  status: TrackingAssetStatus;
  statusDescription: string;
  activeFrom: string;
  activeTo: string;
  isLive: boolean;
  usageCount: number;
  totalReceived: number;
  submittedByUserId: string | null;
  submittedAtUtc: string | null;
  approvedByUserId: string | null;
  approvedAtUtc: string | null;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  places: TrackingAssetPlace[];
  permittedActions: string[];
}

export interface TrackingAssetSearchFilter {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  campaignId?: string | null;
  status?: TrackingAssetStatus | null;
  assetType?: TrackingAssetType | null;
  channelId?: string | null;
  sourceId?: string | null;
  mediumId?: string | null;
  isLiveNow?: boolean | null;
}

// =============================================================================================
// Campaign readiness
// =============================================================================================

export interface CreateReadinessCheckRequest {
  checkName: string;
  category: ReadinessCheckCategory;
  /** What "passed" means for this check. Required, so a pass is a decision rather than a click. */
  successCriteria: string;
  description?: string | null;
  requiredForLaunch?: boolean;
  ownerUserId?: string | null;
  dueDate?: string | null;
  notes?: string | null;
}

export interface UpdateReadinessCheckRequest {
  expectedVersion: number;
  checkName: string;
  category: ReadinessCheckCategory;
  successCriteria: string;
  description?: string | null;
  requiredForLaunch?: boolean;
  ownerUserId?: string | null;
  dueDate?: string | null;
  notes?: string | null;
}

export interface ReadinessVerdictRequest {
  expectedVersion: number;
  notes?: string | null;
}

export interface AssignReadinessBlockerRequest {
  ownerUserId: string;
  blockerNote: string;
  expectedVersion: number;
}

export interface ResolveReadinessBlockerRequest {
  resolutionNote?: string | null;
}

export interface ReturnCampaignToDraftRequest {
  expectedVersion: number;
  reason: string;
}

export interface ReadinessBlocker {
  id: string;
  ownerUserId: string;
  blockerNote: string;
  isResolved: boolean;
  resolvedByUserId: string | null;
  resolvedAtUtc: string | null;
  resolutionNote: string | null;
  createdAtUtc: string;
}

export interface ReadinessCheckListItem {
  id: string;
  checkName: string;
  description: string | null;
  category: ReadinessCheckCategory;
  categoryDescription: string;
  successCriteria: string;
  requiredForLaunch: boolean;
  ownerUserId: string | null;
  dueDate: string | null;
  isOverdue: boolean;
  notes: string | null;
  status: ReadinessCheckStatus;
  statusDescription: string;
  hasOpenBlocker: boolean;
  /** True when this one check is what stands between the campaign and a launch. */
  blocksLaunch: boolean;
  version: number;
}

export interface ReadinessCheckDetail {
  id: string;
  campaignId: string;
  checkName: string;
  description: string | null;
  category: ReadinessCheckCategory;
  categoryDescription: string;
  successCriteria: string;
  requiredForLaunch: boolean;
  ownerUserId: string | null;
  dueDate: string | null;
  isOverdue: boolean;
  notes: string | null;
  status: ReadinessCheckStatus;
  statusDescription: string;
  blocksLaunch: boolean;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  blockers: ReadinessBlocker[];
  permittedActions: string[];
}

/**
 * The whole checklist for one campaign.
 *
 * `canLaunch` IS THE SERVER'S VERDICT and the only one that counts. It folds in the required
 * outstanding checks, the open blockers and the configured "allow launch with outstanding checks"
 * setting - three things a screen would have to reproduce exactly to reach the same answer.
 */
export interface CampaignReadiness {
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  campaignStatus: CampaignStatus;
  totalItems: number;
  passed: number;
  failed: number;
  pending: number;
  requiredOutstanding: number;
  openBlockers: number;
  readinessPercentage: number;
  canLaunch: boolean;
  items: ReadinessCheckListItem[];
}

// =============================================================================================
// Reference data
// =============================================================================================

export interface CampaignReferenceItem {
  id: string;
  code: string;
  name: string;
  description: string | null;
  status: string;
  isActive: boolean;
  sortOrder: number;
}

/**
 * Every catalogue the campaign screens draw from, in one call.
 *
 * CHANNELS, SOURCES AND MEDIUMS ARE PLATFORM-WIDE. They appear in tracking URLs and in reporting
 * that spans organisations, so one code has to mean one thing everywhere - which is why only a
 * platform administrator may maintain them, and why they are read-only to an organisation here.
 */
export interface CampaignReferenceData {
  channels: CampaignReferenceItem[];
  sources: CampaignReferenceItem[];
  mediums: CampaignReferenceItem[];
  campaignStatuses: EnumOption[];
  lifecycleActivations: EnumOption[];
  trackingAssetTypes: EnumOption[];
  trackingAssetStatuses: EnumOption[];
  readinessCategories: EnumOption[];
  readinessStatuses: EnumOption[];
}

// =============================================================================================
// Budget and target plans
// =============================================================================================

export type PlanApprovalState = 'Draft' | 'Submitted' | 'Approved' | 'Superseded' | 'Rejected';

export interface AllocateBudgetPlanRequest {
  campaignId: string;
  planPeriod: string;
  targetDimension: string;
  ownerUserId: string;
  targetAmount: number;
  budgetAmount: number;
  /** Defaults to the campaign's currency when omitted. */
  currencyId?: string | null;
  budgetCategory: string;
  expectedVolume: number;
  assumptions?: string | null;
}

export interface ReviseBudgetPlanRequest {
  expectedVersion: number;
  targetAmount: number;
  budgetAmount: number;
  currencyId?: string | null;
  budgetCategory: string;
  expectedVolume: number;
  assumptions?: string | null;
  revisionReason?: string | null;
}

export interface UpdateBudgetPlanVersionRequest {
  expectedVersion: number;
  targetAmount: number;
  budgetAmount: number;
  currencyId?: string | null;
  budgetCategory: string;
  expectedVolume: number;
  assumptions?: string | null;
  ownerUserId?: string | null;
}

export interface SubmitBudgetPlanVersionRequest {
  expectedVersion: number;
  note?: string | null;
}

export interface BudgetPlanDecisionRequest {
  expectedVersion: number;
  /** Required on a rejection; the server refuses one without it. */
  reason?: string | null;
}

export interface BudgetPlanSearchFilter {
  search?: string;
  campaignId?: string;
  ownerUserId?: string;
  planPeriod?: string;
  targetDimension?: string;
  approvalState?: PlanApprovalState;
  hasApprovedVersion?: boolean;
  page?: number;
  pageSize?: number;
  sort?: string;
}

export interface BudgetPlanVersion {
  id: string;
  versionNumber: number;
  versionLabel: string;
  targetAmount: number;
  budgetAmount: number;
  currencyId: string;
  currencyCode: string;
  budgetCategory: string;
  expectedVolume: number;
  assumptions: string | null;
  approvalState: PlanApprovalState;
  approvalStateDescription: string;
  submittedByUserId: string | null;
  submittedAtUtc: string | null;
  approvedByUserId: string | null;
  approvedAtUtc: string | null;
  decisionReason: string | null;
  effectiveAtUtc: string | null;
  /** Only meaningful on the approved version - see the API mapping for why. */
  actualReconciledAmount: number;
  variance: number;
  variancePercentage: number;
  version: number;
  isEditable: boolean;
  countsTowardTotals: boolean;
}

export interface BudgetPlanListItem {
  id: string;
  code: string;
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  planPeriod: string;
  targetDimension: string;
  ownerUserId: string;
  /** The approved version when there is one, else the latest. */
  displayVersion: BudgetPlanVersion | null;
  hasApprovedVersion: boolean;
  versionCount: number;
  version: number;
  permittedActions: string[];
}

export interface BudgetPlanDetail {
  id: string;
  code: string;
  tenantId: string;
  businessUnitId: string;
  campaignId: string;
  campaignCode: string;
  campaignName: string;
  planPeriod: string;
  targetDimension: string;
  ownerUserId: string;
  versions: BudgetPlanVersion[];
  approvedVersion: BudgetPlanVersion | null;
  latestVersion: BudgetPlanVersion | null;
  hasApprovedVersion: boolean;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
  permittedActions: string[];
}

export interface CampaignBudgetSummary {
  campaignId: string;
  campaignCode: string;
  committedTargetAmount: number;
  committedBudgetAmount: number;
  committedExpectedVolume: number;
  actualReconciledAmount: number;
  variance: number;
  variancePercentage: number;
  planCount: number;
  approvedPlanCount: number;
  awaitingApprovalCount: number;
  currencyCode: string;
}

// =============================================================================================
// Attribution
// =============================================================================================

export interface AttributionSearchFilter {
  search?: string;
  campaignId?: string;
  trackingAssetId?: string;
  fromUtc?: string;
  toUtc?: string;
  /** True for traced gifts, false for untraced, omitted for both. */
  attributedOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface RequestAttributionCorrectionRequest {
  donationId: string;
  proposedCampaignId?: string | null;
  proposedTrackingAssetId?: string | null;
  reason: string;
}

export interface ResolveAttributionCorrectionRequest {
  expectedVersion: number;
  resolutionNote: string;
  /** Whether the attribution was actually changed, as opposed to checked and found correct. */
  attributionChanged: boolean;
}

export interface AttributionListItem {
  donationId: string;
  reference: string;
  receivedAtUtc: string;
  amount: number;
  currencyCode: string;
  status: string;
  campaignId: string | null;
  campaignCode: string;
  campaignName: string;
  trackingAssetId: string | null;
  trackingReference: string;
  assetType: TrackingAssetType | null;
  channelName: string;
  sourceName: string;
  mediumName: string;
  donorName: string;
  donorId: string | null;
  /** Traced to a tracking asset. NOT the same as having a campaign. */
  isAttributed: boolean;
  attributionDescription: string;
  hasOpenCorrectionRequest: boolean;
  permittedActions: string[];
}

export interface AttributionTraceField {
  key: string;
  label: string;
  value: string;
  copyable: boolean;
}

export interface AttributionTraceStep {
  key: string;
  title: string;
  caption: string;
  fields: AttributionTraceField[];
}

export interface AttributionDetail {
  donationId: string;
  reference: string;
  receivedAtUtc: string;
  amount: number;
  currencyCode: string;
  status: string;
  donorName: string;
  donorId: string | null;
  campaignId: string | null;
  campaignCode: string;
  campaignName: string;
  campaignStatus: string;
  trackingAssetId: string | null;
  trackingReference: string;
  assetType: TrackingAssetType | null;
  assetDestination: string | null;
  channelName: string;
  sourceName: string;
  mediumName: string;
  isAttributed: boolean;
  attributionDescription: string;
  hasOpenCorrectionRequest: boolean;
  /** The hops, in the order they happened. */
  trace: AttributionTraceStep[];
  permittedActions: string[];
}

export interface AttributionBreakdownRow {
  key: string;
  label: string;
  amount: number;
  donationCount: number;
  /** Of the TOTAL including untraced gifts, not of the traced portion. */
  sharePercentage: number;
}

export interface AttributionSummary {
  campaignId: string | null;
  totalAmount: number;
  totalDonations: number;
  attributedAmount: number;
  attributedDonations: number;
  unattributedAmount: number;
  unattributedDonations: number;
  attributionRate: number;
  currencyCode: string;
  byChannel: AttributionBreakdownRow[];
  bySource: AttributionBreakdownRow[];
  byMedium: AttributionBreakdownRow[];
  byAsset: AttributionBreakdownRow[];
}

// =============================================================================================
// Helper
// =============================================================================================

/**
 * Whether the server said this caller may take an action.
 *
 * ALWAYS ASK THIS rather than re-deriving the rule. On a campaign the server's answer includes
 * the segregation-of-duties check - the submitter cannot approve - which is invisible from here,
 * so a local condition would draw a button the API refuses.
 */
export function canPerformCampaignAction(
  record: { permittedActions?: readonly string[] } | null | undefined,
  action: string,
): boolean {
  return !!record?.permittedActions?.some(
    (candidate) => candidate.toLowerCase() === action.toLowerCase(),
  );
}
